using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using System.Text.Json;
using Ato.Copilot.Channels.Abstractions;
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces;
using Ato.Copilot.Core.Models;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Chat.Channels;
using Ato.Copilot.Chat.Data;
using Ato.Copilot.Chat.Hubs;
using Ato.Copilot.Chat.Services;

// ────────────────────────────────────────────────────────────────
//  ATO Copilot — Chat Application
//  Full-stack SPA + REST API + SignalR hub
// ────────────────────────────────────────────────────────────────

// Build a minimal IConfiguration for Serilog bootstrap so the `Serilog`
// appsettings section drives sinks, levels, output templates, and retention.
// Programmatic .Enrich.WithProperty + Application Insights sink calls below
// augment whatever the JSON configures — they are NOT replaced by
// ReadFrom.Configuration.
var bootstrapConfig = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile(
        $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
        optional: true)
    .AddEnvironmentVariables("ATO_")
    .Build();

var logConfig = new LoggerConfiguration()
    .ReadFrom.Configuration(bootstrapConfig)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "ATO Copilot Chat");

// Conditionally add Application Insights sink when connection string is available
var appInsightsConnectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
if (!string.IsNullOrEmpty(appInsightsConnectionString))
{
    logConfig = logConfig.WriteTo.ApplicationInsights(appInsightsConnectionString, TelemetryConverter.Traces);
}

Log.Logger = logConfig.CreateLogger();

try
{
    Log.Information("ATO Copilot Chat starting");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // ─── Configuration ───────────────────────────────────────────────

    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
        .AddEnvironmentVariables("ATO_");

    // ─── Database ────────────────────────────────────────────────────

    var connectionString = builder.Configuration.GetConnectionString("ChatDb")
                           ?? "Data Source=chat.db";

    // DatabaseOptions drives EF Core retry, command timeout, and
    // sensitive-data-logging — same wire-up pattern as Core's RegisterDbContext.
    var chatDbOptions = new Ato.Copilot.Core.Configuration.DatabaseOptions();
    builder.Configuration
        .GetSection(Ato.Copilot.Core.Configuration.DatabaseOptions.SectionName)
        .Bind(chatDbOptions);

    builder.Services.AddDbContext<ChatDbContext>(options =>
    {
        if (chatDbOptions.EnableSensitiveDataLogging)
        {
            options.EnableSensitiveDataLogging();
        }

        if (connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            options.UseSqlite(connectionString,
                sqliteOptions => sqliteOptions.CommandTimeout(chatDbOptions.CommandTimeoutSeconds));
        else
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: chatDbOptions.MaxRetryCount,
                    maxRetryDelay: TimeSpan.FromSeconds(chatDbOptions.MaxRetryDelay),
                    errorNumbersToAdd: null);
                sqlOptions.CommandTimeout(chatDbOptions.CommandTimeoutSeconds);
            });
    });

    // ─── Services ────────────────────────────────────────────────────

    // ChatService depends on IPathSanitizationService for upload-path
    // safety checks. Chat does not call AddAtoCopilotCore (that pulls in
    // the full compliance graph), so the dependency is registered
    // explicitly here. Singleton matches the canonical registration in
    // CoreServiceExtensions.AddAtoCopilotCore.
    builder.Services.AddSingleton<IPathSanitizationService, PathSanitizationService>();
    builder.Services.AddScoped<IChatService, ChatService>();

    // ─── Channels Adapter Services ───────────────────────────────────
    // Bridge SignalR transport with the Channels library abstractions
    // so Chat and external channels (VS Code, M365) share the same contracts.

    builder.Services.AddSingleton<SignalRConnectionTracker>();
    builder.Services.AddSingleton<IChannel, SignalRChannel>();
    builder.Services.AddSingleton<IChannelManager, SignalRChannelManager>();
    builder.Services.AddScoped<IMessageHandler, ChatServiceMessageHandler>();

    // Tenant scope propagation for in-process MCP tool invocation via Channels
    // (FR-021/FR-024). Chat owns the bridge because it references both Core
    // (ITenantContextAccessor) and Channels (ITenantScopeBinder).
    builder.Services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
    builder.Services.AddSingleton<ITenantScopeBinder, AccessorTenantScopeBinder>();

    builder.Services.AddHttpClient("McpServer", client =>
    {
        var mcpBaseUrl = builder.Configuration.GetValue<string>("McpServer:BaseUrl") ?? "http://localhost:3001";
        client.BaseAddress = new Uri(mcpBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(180);
    })
    .ConfigureResiliencePipeline(new ResiliencePipelineConfig
    {
        Name = "McpServer",
        MaxRetryAttempts = 3,
        BaseDelaySeconds = 2.0,
        UseJitter = true,
        RequestTimeoutSeconds = 180
    });
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });
    builder.Services.AddSignalR()
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });
    builder.Services.AddHealthChecks();

    // ─── Authentication & Authorization (DEF-001) ────────────────────────────
    // Validates Azure AD JWT Bearer tokens using the same AzureAd configuration
    // section as the MCP server (port 3001). SignalR connections pass the token
    // via the access_token query string (standard SignalR + JWT pattern).
    //
    // The MCP server uses a custom CacAuthenticationMiddleware to do the same
    // validation and additionally enforce CAC/PIV amr claims. Chat uses the
    // standard JwtBearer handler for simplicity; CAC enforcement can be layered
    // on in a follow-on if Chat users are required to hold PIV cards.
    var azureAdSection = builder.Configuration.GetSection("AzureAd");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = azureAdSection["Authority"]
                ?? $"https://login.microsoftonline.com/{azureAdSection["TenantId"]}/v2.0";
            options.Audience = azureAdSection["ClientId"];
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
            };
            // Allow SignalR to pass the bearer token as a query string parameter
            // (WebSocket / SSE connections cannot set Authorization headers).
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) &&
                        (path.StartsWithSegments("/hubs/chat") ||
                         path.StartsWithSegments("/hubs/collaboration")))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });

    // Default policy: every endpoint requires an authenticated user.
    // FallbackPolicy extends this to minimal-API endpoints (MapGet, etc.)
    // that have no explicit [Authorize] or [AllowAnonymous].
    // Endpoints that must remain public (health, info) use .AllowAnonymous().
    builder.Services.AddAuthorization(options =>
    {
        var requireAuth = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        options.DefaultPolicy = requireAuth;
        options.FallbackPolicy = requireAuth;
    });

    builder.Services.AddCors(options =>
    {
        // DEF-001: renamed from "AllowAll" to "AllowDashboard" to make intent explicit.
        // Origins are config-driven (Cors:AllowedOrigins); dev fallback covers the
        // React dev server and Vite. AllowCredentials() is required for SignalR.
        options.AddPolicy("AllowDashboard", policy =>
        {
            var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                          ?? new[] { "http://localhost:3000", "http://localhost:5173" };
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });
    var app = builder.Build();

    // ─── Database Initialization ─────────────────────────────────────

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ChatDbContext>>();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            logger.LogInformation("Ensuring chat database is created...");
            await db.Database.EnsureCreatedAsync(cts.Token);
            logger.LogInformation("Chat database ready");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Chat database initialization failed — shutting down");
            Environment.ExitCode = 1;
            throw;
        }
    }

    // ─── Middleware Pipeline ─────────────────────────────────────────
    // Order per research.md Topic 4: Middleware Pipeline Order

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();
    // DEF-001: renamed policy + UseAuthentication/UseAuthorization wired in correct order.
    // ASP.NET Core middleware ordering: CORS must come before auth so preflight
    // requests are handled before the auth challenge runs.
    app.UseCors("AllowDashboard");
    app.UseAuthentication();
    app.UseAuthorization();

    // ─── Endpoints ───────────────────────────────────────────────────

    app.MapControllers();
    app.MapHub<ChatHub>("/hubs/chat");
    app.MapHub<CollaborationHub>("/hubs/collaboration"); // #1357
    // Health and info are public — they must be explicitly opted out of the
    // FallbackPolicy (RequireAuthenticatedUser) set in AddAuthorization above.
    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        ResponseWriter = WriteHealthCheckResponseAsync
    }).AllowAnonymous();

    app.MapGet("/api/info", () => Results.Json(new
    {
        service = "ATO Copilot Chat",
        version = "1.0.0",
        endpoints = new
        {
            conversations = "GET /api/conversations",
            messages = "GET /api/messages",
            hub = "ws /hubs/chat",
            health = "GET /health"
        }
    })).AllowAnonymous();

    // SPA fallback — MUST be last.
    // DEF-001 R1: AllowAnonymous so unauthenticated browsers can load the
    // app shell and reach the MSAL login page. The FallbackPolicy
    // (RequireAuthenticatedUser) would otherwise 401 every fresh session
    // before index.html is served, preventing MSAL from ever running.
    // API controllers and SignalR hubs retain their auth requirement via
    // [Authorize] attributes and the DefaultPolicy.
    app.MapFallbackToFile("index.html").AllowAnonymous();

    var port = builder.Configuration.GetValue("Server:Port", 5001);
    var urls = builder.Configuration.GetValue("Server:Urls", $"http://0.0.0.0:{port}");
    app.Urls.Add(urls!);

    Log.Information("ATO Copilot Chat listening on {Urls}", urls);

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ATO Copilot Chat terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// ────────────────────────────────────────────────────────────────
//  Health Check Response Writer
// ────────────────────────────────────────────────────────────────
async Task WriteHealthCheckResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    var response = new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        entries = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description ?? string.Empty
        })
    };

    var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });

    await context.Response.WriteAsync(json);
}

// Make Program class accessible for WebApplicationFactory in integration tests
public partial class Program { }

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using System.Text.Json;
using Ato.Copilot.Channels.Abstractions;
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models;
using Ato.Copilot.Core.Services.Tenancy;
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
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
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
    app.UseCors("AllowAll");

    // ─── Endpoints ───────────────────────────────────────────────────

    app.MapControllers();
    app.MapHub<ChatHub>("/hubs/chat");
    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        ResponseWriter = WriteHealthCheckResponseAsync
    });

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
    }));

    // SPA fallback — MUST be last
    app.MapFallbackToFile("index.html");

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

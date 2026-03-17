using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Azure.Identity;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Extensions;
using Ato.Copilot.Core.Observability;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;
using Ato.Copilot.State.Extensions;
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Mcp.Extensions;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Server;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Ato.Copilot.Core.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// ────────────────────────────────────────────────────────────────
//  ATO Copilot — Compliance-Only MCP Server
//  Supports dual-mode: stdio (GitHub Copilot / Claude) and HTTP
// ────────────────────────────────────────────────────────────────

var mode = DetermineRunMode(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "ATO Copilot")
    .Destructure.With<SensitiveDataDestructuringPolicy>()
    .WriteTo.File(
        path: "logs/ato-copilot-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

if (mode != "stdio")
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "ATO Copilot")
        .Destructure.With<SensitiveDataDestructuringPolicy>()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: "logs/ato-copilot-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14)
        .CreateLogger();
}

try
{
    Log.Information("ATO Copilot starting in {Mode} mode", mode);

    if (mode == "stdio")
        await RunStdioModeAsync(args);
    else
        await RunHttpModeAsync(args);
}
catch (Exception ex)
{
    Log.Fatal(ex, "ATO Copilot terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// ────────────────────────────────────────────────────────────────
//  Stdio Mode — for GitHub Copilot, Claude Desktop, etc.
// ────────────────────────────────────────────────────────────────
async Task RunStdioModeAsync(string[] args)
{
    var builder = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureAppConfiguration((ctx, config) =>
        {
            config.SetBasePath(AppContext.BaseDirectory);
            config.AddJsonFile("appsettings.json", optional: true);
            config.AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true);
            config.AddEnvironmentVariables("ATO_");

            // Azure Key Vault configuration provider (non-Development only, per FR-038)
            if (!ctx.HostingEnvironment.IsDevelopment())
            {
                var builtConfig = config.Build();
                var vaultUri = builtConfig["KeyVault:VaultUri"];
                if (!string.IsNullOrEmpty(vaultUri))
                {
                    config.AddAzureKeyVault(
                        new Uri(vaultUri),
                        new DefaultAzureCredential(new DefaultAzureCredentialOptions
                        {
                            AuthorityHost = AzureAuthorityHosts.AzureGovernment
                        }));
                }
            }
        })
        .ConfigureServices((ctx, services) =>
        {
            RegisterCoreServices(services, ctx.Configuration);

            // Validate CAC simulation mode configuration
            ValidateCacSimulationConfig(ctx.Configuration, ctx.HostingEnvironment.EnvironmentName);

            services.AddMcpStdioService();
        });

    using var host = builder.Build();

    // Auto-migrate database at startup (fail = exit code 1)
    await MigrateDatabaseAsync(host.Services);

    await host.RunAsync();
}

// ────────────────────────────────────────────────────────────────
//  HTTP Mode — REST API for web apps, dashboards, etc.
// ────────────────────────────────────────────────────────────────
async Task RunHttpModeAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Configuration
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
        .AddEnvironmentVariables("ATO_");

    // Azure Key Vault configuration provider (non-Development only, per FR-038)
    if (!builder.Environment.IsDevelopment())
    {
        var vaultUri = builder.Configuration["KeyVault:VaultUri"];
        if (!string.IsNullOrEmpty(vaultUri))
        {
            builder.Configuration.AddAzureKeyVault(
                new Uri(vaultUri),
                new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    AuthorityHost = AzureAuthorityHosts.AzureGovernment
                }));
            Log.Information("Azure Key Vault configuration provider added: {VaultUri}", vaultUri);
        }
    }

    // Register services
    RegisterCoreServices(builder.Services, builder.Configuration);

    // Validate CAC simulation mode configuration
    ValidateCacSimulationConfig(builder.Configuration, builder.Environment.EnvironmentName);
    builder.Services.AddHealthChecks()
        .AddCheck<AgentHealthCheck>("compliance-agent")
        .AddCheck<Ato.Copilot.Agents.Observability.NistControlsHealthCheck>("nist-controls");

    // Rate limiting (FR-006 through FR-010a)
    var rateLimitingOptions = new RateLimitingOptions();
    builder.Configuration.GetSection(RateLimitingOptions.SectionName).Bind(rateLimitingOptions);

    builder.Services.AddRateLimiter(options =>
    {
        foreach (var policy in rateLimitingOptions.Policies)
        {
            options.AddPolicy(policy.PolicyName, context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                  ?? context.Connection.RemoteIpAddress?.ToString()
                                  ?? "anonymous",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = policy.PermitLimit,
                        Window = TimeSpan.FromSeconds(policy.WindowSeconds),
                        SegmentsPerWindow = policy.SegmentsPerWindow,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
        }

        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.ContentType = "application/json";

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
            }
            else
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    rateLimitingOptions.Policies.FirstOrDefault()?.WindowSeconds.ToString() ?? "60";
            }

            var errorDetail = new
            {
                errorCode = "RATE_LIMITED",
                message = $"Rate limit exceeded for {context.HttpContext.Request.Path}.",
                suggestion = "Reduce request frequency or wait before retrying."
            };

            Log.Warning("Rate limit exceeded for {Endpoint} by {Client}",
                context.HttpContext.Request.Path,
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            await context.HttpContext.Response.WriteAsync(
                JsonSerializer.Serialize(errorDetail), cancellationToken);
        };
    });

    // OpenTelemetry metrics + tracing (FR-021, FR-022, FR-024)
    var otelOptions = new OpenTelemetryOptions();
    builder.Configuration.GetSection(OpenTelemetryOptions.SectionName).Bind(otelOptions);

    if (otelOptions.Enabled)
    {
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddMeter(HttpMetrics.MeterName);          // ato.copilot.http
                metrics.AddMeter(ToolMetrics.MeterName);          // Ato.Copilot (tools + compliance)
                if (otelOptions.EnablePrometheus)
                    metrics.AddPrometheusExporter();
                else
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otelOptions.OtlpEndpoint));
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation();
                tracing.AddSource("Ato.Copilot.Mcp");
                tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otelOptions.OtlpEndpoint));
            });
    }

    // Add HTTP-specific services
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? new[] { "http://localhost:3000", "http://localhost:5173" };
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    // SignalR for real-time notification push
    builder.Services.AddSignalR()
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });
    builder.Services.AddSingleton<Ato.Copilot.Core.Interfaces.Compliance.INotificationBroadcaster,
        Ato.Copilot.Mcp.Services.SignalRNotificationBroadcaster>();

    var app = builder.Build();

    // Auto-migrate database at startup (fail = exit code 1)
    await MigrateDatabaseAsync(app.Services);

    // Configure pipeline — middleware ordering per R-012:
    // 1. Correlation ID (MUST be first — before Serilog request logging)
    // 2. Request logging (Serilog)
    // 3. CORS
    // 4. Rate limiting (per-endpoint sliding window, FR-006)
    // 5. CAC authentication (JWT validation, amr claim check for CAC/PIV)
    // 6. Authorization (role-based access checks, Tier 2 CAC gate, PIM tier enforcement)
    // 7. Audit logging (captures all requests with user/role/action)
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors();
    app.UseRateLimiter();
    app.UseMiddleware<RequestSizeLimitMiddleware>();
    app.UseMiddleware<CacAuthenticationMiddleware>();
    app.UseMiddleware<ComplianceAuthorizationMiddleware>();
    app.UseMiddleware<RequestMetricsMiddleware>();
    app.UseMiddleware<AuditLoggingMiddleware>();

    // Map MCP HTTP endpoints
    var httpBridge = app.Services.GetRequiredService<McpHttpBridge>();
    httpBridge.MapEndpoints(app);

    // Map Dashboard REST API endpoints (Feature 030)
    app.MapDashboardEndpoints();

    // Map Notification REST API endpoints
    app.MapNotificationEndpoints();

    // Map SignalR notification hub
    app.MapHub<Ato.Copilot.Mcp.Hubs.NotificationHub>("/hubs/notifications");

    // Health check endpoint with custom JSON writer (per FR-045 / SC-015)
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = WriteHealthCheckResponseAsync
    });

    // Prometheus scrape endpoint (FR-021) — only when enabled; otherwise 404
    if (otelOptions.Enabled && otelOptions.EnablePrometheus)
    {
        app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics");
    }
    else
    {
        app.MapGet("/metrics", () => Results.Json(new
        {
            errorCode = "METRICS_DISABLED",
            message = "Prometheus metrics endpoint is not enabled.",
            suggestion = "Set OpenTelemetry:EnablePrometheus to true in configuration."
        }, statusCode: 404));
    }

    // Root endpoint
    app.MapGet("/", () => Results.Json(new
    {
        service = "ATO Copilot MCP Server",
        version = "1.0.0",
        mode = "http",
        tools = "Use MCP protocol to list tools",
        endpoints = new
        {
            chat = "POST /mcp/chat",
            mcp = "POST /mcp",
            tools = "GET /mcp/tools",
            health = "GET /health"
        }
    }));

    var port = builder.Configuration.GetValue("Server:Port", 3001);
    var urls = builder.Configuration.GetValue("Server:Urls", $"http://0.0.0.0:{port}");
    app.Urls.Add(urls!);

    Log.Information("ATO Copilot HTTP server listening on {Urls}", urls);

    await app.RunAsync();
}

// ────────────────────────────────────────────────────────────────
//  Database Migration
// ────────────────────────────────────────────────────────────────
/// <summary>
/// Applies pending EF Core migrations at startup. Fails fast (exit code 1)
/// if migration cannot complete within 30 seconds.
/// </summary>
async Task MigrateDatabaseAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<AtoCopilotContext>>();

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var provider = scope.ServiceProvider.GetRequiredService<IConfiguration>()
            .GetValue<string>("Database:Provider") ?? "SQLite";

        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            // SQL Server: Use EnsureCreated to build schema from model directly.
            // EF migrations were authored for SQLite and may have incompatible
            // index sizes for SQL Server's 900-byte key limit.
            logger.LogInformation("SQL Server detected — ensuring database is created from model...");
            await db.Database.EnsureCreatedAsync(cts.Token);

            // EnsureCreated only creates the DB if it doesn't exist at all.
            // Create any new tables that were added after initial creation.
            await EnsureNewTablesAsync(db, logger, cts.Token);

            logger.LogInformation("SQL Server database ready");
        }
        else
        {
            logger.LogInformation("Applying database migrations...");
            await db.Database.MigrateAsync(cts.Token);
            logger.LogInformation("Database migrations applied successfully");
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database migration failed — shutting down");
        Environment.ExitCode = 1;
        throw;
    }
}

/// <summary>
/// Creates tables that were added to the model after the initial EnsureCreated call.
/// EnsureCreated is a no-op when the database already exists, so new DbSets
/// need manual CREATE TABLE IF NOT EXISTS statements.
/// </summary>
async Task EnsureNewTablesAsync(AtoCopilotContext db, Microsoft.Extensions.Logging.ILogger<AtoCopilotContext> logger, CancellationToken ct)
{
    const string deferredSql = """
        IF OBJECT_ID(N'DeferredPrerequisites', N'U') IS NULL
        BEGIN
            CREATE TABLE DeferredPrerequisites (
                Id           NVARCHAR(36)   NOT NULL PRIMARY KEY,
                RegisteredSystemId NVARCHAR(36) NOT NULL,
                GateName     NVARCHAR(200)  NOT NULL,
                Message      NVARCHAR(1000) NOT NULL,
                SkippedFromPhase NVARCHAR(50) NOT NULL,
                AdvancedToPhase  NVARCHAR(50) NOT NULL,
                CreatedAt    DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
                CreatedBy    NVARCHAR(200)  NOT NULL DEFAULT '',
                IsResolved   BIT            NOT NULL DEFAULT 0,
                ResolvedAt   DATETIME2      NULL,
                ResolvedBy   NVARCHAR(200)  NULL,
                CONSTRAINT FK_DeferredPrerequisites_RegisteredSystems
                    FOREIGN KEY (RegisteredSystemId) REFERENCES RegisteredSystems(Id)
                    ON DELETE CASCADE
            );
            CREATE INDEX IX_DeferredPrerequisite_System_Resolved
                ON DeferredPrerequisites (RegisteredSystemId, IsResolved);
        END
        """;

    try
    {
        await db.Database.ExecuteSqlRawAsync(deferredSql, ct);
        logger.LogInformation("Verified DeferredPrerequisites table exists");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not ensure DeferredPrerequisites table — non-fatal");
    }

    // Add notification columns and table for notification enhancements
    const string notificationSchemaSql = """
        -- Add new columns to AlertNotifications if they don't exist
        IF COL_LENGTH('AlertNotifications', 'UserId') IS NULL
        BEGIN
            ALTER TABLE AlertNotifications ADD UserId NVARCHAR(200) NULL;
        END

        IF COL_LENGTH('AlertNotifications', 'IsRead') IS NULL
        BEGIN
            ALTER TABLE AlertNotifications ADD IsRead BIT NOT NULL DEFAULT 0;
        END

        IF COL_LENGTH('AlertNotifications', 'ReadAt') IS NULL
        BEGIN
            ALTER TABLE AlertNotifications ADD ReadAt DATETIMEOFFSET NULL;
        END

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AlertNotification_User_Read')
        BEGIN
            CREATE INDEX IX_AlertNotification_User_Read
                ON AlertNotifications (UserId, IsRead);
        END

        -- Create NotificationPreferences table
        IF OBJECT_ID(N'NotificationPreferences', N'U') IS NULL
        BEGIN
            CREATE TABLE NotificationPreferences (
                Id                    UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                UserId                NVARCHAR(200)    NOT NULL,
                PoamOverdueAlerts     BIT              NOT NULL DEFAULT 1,
                AtoExpirationAlerts   BIT              NOT NULL DEFAULT 1,
                ComplianceDriftAlerts BIT              NOT NULL DEFAULT 1,
                AlertDaysBefore       INT              NOT NULL DEFAULT 30,
                CreatedAt             DATETIMEOFFSET   NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                UpdatedAt             DATETIMEOFFSET   NOT NULL DEFAULT SYSDATETIMEOFFSET()
            );
            CREATE UNIQUE INDEX IX_NotificationPreferences_UserId
                ON NotificationPreferences (UserId);
        END
        """;

    try
    {
        await db.Database.ExecuteSqlRawAsync(notificationSchemaSql, ct);
        logger.LogInformation("Verified notification schema enhancements exist");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not ensure notification schema — non-fatal");
    }
}

// ────────────────────────────────────────────────────────────────
//  Service Registration (shared between modes)
// ────────────────────────────────────────────────────────────────
void RegisterCoreServices(IServiceCollection services, IConfiguration configuration)
{
    // Core infrastructure
    services.AddAtoCopilotCore(configuration);

    // State management
    services.AddInMemoryStateManagement();

    // Compliance agent + tools
    services.AddComplianceAgent(configuration);

    // Configuration agent + tools
    services.AddConfigurationAgent();

    // KnowledgeBase agent + services
    services.AddKnowledgeBaseAgent(configuration);

    // MCP server
    services.AddMcpServer(configuration);

    // Dashboard services (Feature 030)
    services.AddScoped<Ato.Copilot.Core.Services.DashboardService>();
    services.AddScoped<Ato.Copilot.Core.Services.TodoService>();
    services.AddScoped<Ato.Copilot.Core.Services.CapabilityService>();
    services.AddScoped<Ato.Copilot.Core.Services.ComponentService>();
    services.AddSingleton<Ato.Copilot.Core.Services.NarrativeTemplateService>();
    services.AddSingleton<Ato.Copilot.Core.Services.ComplianceTrendSnapshotService>();
    services.AddHostedService(sp => sp.GetRequiredService<Ato.Copilot.Core.Services.ComplianceTrendSnapshotService>());

    // Boundary services (Feature 033)
    services.AddScoped<Ato.Copilot.Core.Services.BoundaryDefinitionService>();
    services.AddSingleton<Ato.Copilot.Agents.Compliance.Services.AzureResourceDiscoveryService>();

    // Roadmap services (Feature 031)
    services.AddScoped<Ato.Copilot.Core.Interfaces.Roadmap.IRoadmapService, Ato.Copilot.Core.Services.RoadmapService>();
}

// ────────────────────────────────────────────────────────────────
//  CAC Simulation Mode Validation
// ────────────────────────────────────────────────────────────────
void ValidateCacSimulationConfig(IConfiguration configuration, string environmentName)
{
    var cacAuthOptions = configuration.GetSection(CacAuthOptions.SectionName).Get<CacAuthOptions>();
    if (cacAuthOptions?.SimulationMode != true)
        return;

    if (environmentName == "Development")
    {
        if (cacAuthOptions.SimulatedIdentity is null)
            throw new InvalidOperationException(
                "CacAuth:SimulatedIdentity configuration is required when CacAuth:SimulationMode is enabled.");

        if (string.IsNullOrWhiteSpace(cacAuthOptions.SimulatedIdentity.UserPrincipalName))
            throw new InvalidOperationException(
                "CacAuth:SimulatedIdentity:UserPrincipalName must not be empty when CacAuth:SimulationMode is enabled.");

        if (string.IsNullOrWhiteSpace(cacAuthOptions.SimulatedIdentity.DisplayName))
            throw new InvalidOperationException(
                "CacAuth:SimulatedIdentity:DisplayName must not be empty when CacAuth:SimulationMode is enabled.");

        Log.Information("CAC simulation mode active. Simulated identity: {UserPrincipalName}",
            cacAuthOptions.SimulatedIdentity.UserPrincipalName);
    }
    else
    {
        Log.Warning(
            "CacAuth:SimulationMode is enabled but environment is {Environment}. Simulation mode will be ignored.",
            environmentName);
    }
}

// ────────────────────────────────────────────────────────────────
//  Mode Detection
// ────────────────────────────────────────────────────────────────
string DetermineRunMode(string[] args)
{
    // Check command line: --stdio or --http
    if (args.Contains("--stdio")) return "stdio";
    if (args.Contains("--http")) return "http";

    // Check environment variable
    var envMode = Environment.GetEnvironmentVariable("ATO_RUN_MODE");
    if (!string.IsNullOrEmpty(envMode)) return envMode.ToLowerInvariant();

    // Default: if stdin is not a terminal → stdio, otherwise HTTP
    if (!Console.IsInputRedirected) return "http";
    return "stdio";
}

// ────────────────────────────────────────────────────────────────
//  Health Check Response Writer (per FR-045 / SC-015)
// ────────────────────────────────────────────────────────────────
/// <summary>
/// Custom JSON response writer for the /health endpoint.
/// Output format: { "status": "Healthy|Degraded|Unhealthy",
///   "agents": [{ "name": "...", "status": "...", "description": "..." }],
///   "totalDurationMs": 45 }
/// </summary>
async Task WriteHealthCheckResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    var agents = report.Entries.Select(e => new
    {
        name = e.Key,
        status = e.Value.Status.ToString(),
        description = e.Value.Description ?? string.Empty
    });

    var response = new
    {
        status = report.Status.ToString(),
        agents,
        totalDurationMs = report.TotalDuration.TotalMilliseconds
    };

    var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });

    await context.Response.WriteAsync(json);
}

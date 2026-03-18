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
        var addedPolicies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var policy in rateLimitingOptions.Policies)
        {
            if (!addedPolicies.Add(policy.PolicyName)) continue;
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

            // Always apply column additions to existing tables
            await EnsureSchemaAdditionsAsync(db, logger, cts.Token);

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
/// Queries INFORMATION_SCHEMA.TABLES to find which model entity tables are missing,
/// then generates and runs CREATE TABLE + index DDL from the EF Core model.
/// </summary>
async Task EnsureNewTablesAsync(AtoCopilotContext db, Microsoft.Extensions.Logging.ILogger<AtoCopilotContext> logger, CancellationToken ct)
{
    // 1. Gather all table names the EF model expects
    var model = db.Model;
    var modelTables = model.GetEntityTypes()
        .Select(e => e.GetTableName())
        .Where(t => t != null)
        .Distinct()
        .ToList();

    // 2. Gather all table names that actually exist in SQL Server
    var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var cmd = db.Database.GetDbConnection().CreateCommand())
    {
        cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
            await cmd.Connection.OpenAsync(ct);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            existingTables.Add(reader.GetString(0));
    }

    var missingTables = modelTables
        .Where(t => !existingTables.Contains(t!))
        .ToList();

    if (missingTables.Count == 0)
    {
        logger.LogInformation("All model tables exist in SQL Server — nothing to create");
        return;
    }

    logger.LogInformation("Found {Count} missing table(s): {Tables}", missingTables.Count, string.Join(", ", missingTables));

    // 3. Also ensure new columns on existing tables (schema additions)
    await EnsureSchemaAdditionsAsync(db, logger, ct);

    // 4. Generate CREATE TABLE DDL from the EF model for missing tables
    var missingEntityTypes = model.GetEntityTypes()
        .Where(e => missingTables.Contains(e.GetTableName(), StringComparer.OrdinalIgnoreCase))
        .ToList();

    foreach (var entityType in missingEntityTypes)
    {
        var tableName = entityType.GetTableName()!;
        var schema = entityType.GetSchema();
        var storeObject = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(tableName, schema);

        // Build column definitions
        var columns = new List<string>();
        foreach (var property in entityType.GetProperties())
        {
            var columnName = property.GetColumnName(storeObject) ?? property.Name;
            var columnType = property.GetColumnType() ?? GetSqlServerColumnType(property);
            var nullable = property.IsNullable ? "NULL" : "NOT NULL";

            var defaultValue = property.GetDefaultValueSql();
            var defaultClause = defaultValue != null ? $" DEFAULT {defaultValue}" : "";

            columns.Add($"    [{columnName}] {columnType} {nullable}{defaultClause}");
        }

        // Build primary key
        var pk = entityType.FindPrimaryKey();
        var pkColumns = pk?.Properties.Select(p => $"[{p.GetColumnName(storeObject) ?? p.Name}]");
        if (pkColumns != null)
            columns.Add($"    CONSTRAINT [PK_{tableName}] PRIMARY KEY ({string.Join(", ", pkColumns)})");

        var createSql = $"CREATE TABLE [{tableName}] (\n{string.Join(",\n", columns)}\n);";

        // Build indexes
        var indexStatements = new List<string>();
        foreach (var index in entityType.GetIndexes())
        {
            var indexName = index.GetDatabaseName() ?? $"IX_{tableName}_{string.Join("_", index.Properties.Select(p => p.Name))}";
            var unique = index.IsUnique ? "UNIQUE " : "";
            var indexColumns = string.Join(", ", index.Properties.Select(p => $"[{p.GetColumnName(storeObject) ?? p.Name}]"));
            indexStatements.Add($"CREATE {unique}INDEX [{indexName}] ON [{tableName}] ({indexColumns});");
        }

        // Build foreign keys
        var fkStatements = new List<string>();
        foreach (var fk in entityType.GetForeignKeys())
        {
            var principalTable = fk.PrincipalEntityType.GetTableName();
            if (principalTable == null) continue;
            var principalStoreObject = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(principalTable, fk.PrincipalEntityType.GetSchema());

            var fkColumns = string.Join(", ", fk.Properties.Select(p => $"[{p.GetColumnName(storeObject) ?? p.Name}]"));
            var pkRefColumns = string.Join(", ", fk.PrincipalKey.Properties.Select(p => $"[{p.GetColumnName(principalStoreObject) ?? p.Name}]"));
            var fkName = $"FK_{tableName}_{principalTable}_{string.Join("_", fk.Properties.Select(p => p.Name))}";
            var deleteAction = fk.DeleteBehavior switch
            {
                DeleteBehavior.Cascade => "CASCADE",
                DeleteBehavior.SetNull => "SET NULL",
                DeleteBehavior.Restrict or DeleteBehavior.NoAction => "NO ACTION",
                _ => "NO ACTION"
            };
            fkStatements.Add($"ALTER TABLE [{tableName}] ADD CONSTRAINT [{fkName}] FOREIGN KEY ({fkColumns}) REFERENCES [{principalTable}] ({pkRefColumns}) ON DELETE {deleteAction};");
        }

        try
        {
            logger.LogInformation("Creating table [{Table}] with {Cols} columns, {Idx} indexes, {Fks} foreign keys",
                tableName, entityType.GetProperties().Count(), indexStatements.Count, fkStatements.Count);

            await db.Database.ExecuteSqlRawAsync(createSql, ct);

            foreach (var idx in indexStatements)
                await db.Database.ExecuteSqlRawAsync(idx, ct);

            foreach (var fk in fkStatements)
            {
                try { await db.Database.ExecuteSqlRawAsync(fk, ct); }
                catch (Exception ex) { logger.LogWarning(ex, "Could not create FK: {Sql}", fk); }
            }

            logger.LogInformation("Successfully created table [{Table}]", tableName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create table [{Table}] — non-fatal", tableName);
        }
    }
}

/// <summary>
/// Maps CLR types to SQL Server column types when EF metadata doesn't provide an explicit column type.
/// </summary>
string GetSqlServerColumnType(Microsoft.EntityFrameworkCore.Metadata.IProperty property)
{
    var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
    var maxLength = property.GetMaxLength();

    return clrType switch
    {
        Type t when t == typeof(string) => maxLength.HasValue ? $"NVARCHAR({maxLength})" : "NVARCHAR(MAX)",
        Type t when t == typeof(int) => "INT",
        Type t when t == typeof(long) => "BIGINT",
        Type t when t == typeof(short) => "SMALLINT",
        Type t when t == typeof(byte) => "TINYINT",
        Type t when t == typeof(bool) => "BIT",
        Type t when t == typeof(decimal) => "DECIMAL(18,2)",
        Type t when t == typeof(double) => "FLOAT",
        Type t when t == typeof(float) => "REAL",
        Type t when t == typeof(DateTime) => "DATETIME2",
        Type t when t == typeof(DateTimeOffset) => "DATETIMEOFFSET",
        Type t when t == typeof(DateOnly) => "DATE",
        Type t when t == typeof(TimeOnly) => "TIME",
        Type t when t == typeof(TimeSpan) => "TIME",
        Type t when t == typeof(Guid) => "UNIQUEIDENTIFIER",
        Type t when t == typeof(byte[]) => maxLength.HasValue ? $"VARBINARY({maxLength})" : "VARBINARY(MAX)",
        Type t when t.IsEnum => "INT",
        _ => "NVARCHAR(MAX)"
    };
}

/// <summary>
/// Applies ALTER TABLE ADD COLUMN for known schema additions that EnsureCreated won't cover.
/// </summary>
async Task EnsureSchemaAdditionsAsync(AtoCopilotContext db, Microsoft.Extensions.Logging.ILogger<AtoCopilotContext> logger, CancellationToken ct)
{
    // Add columns/indexes that were added to existing tables after initial EnsureCreated
    const string alterSql = """
        IF COL_LENGTH('AlertNotifications', 'UserId') IS NULL
            ALTER TABLE AlertNotifications ADD UserId NVARCHAR(200) NULL;

        IF COL_LENGTH('AlertNotifications', 'IsRead') IS NULL
            ALTER TABLE AlertNotifications ADD IsRead BIT NOT NULL DEFAULT 0;

        IF COL_LENGTH('AlertNotifications', 'ReadAt') IS NULL
            ALTER TABLE AlertNotifications ADD ReadAt DATETIMEOFFSET NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AlertNotification_User_Read')
            CREATE INDEX IX_AlertNotification_User_Read ON AlertNotifications (UserId, IsRead);

        IF COL_LENGTH('PoamItems', 'DeviationId') IS NULL
            ALTER TABLE PoamItems ADD DeviationId NVARCHAR(450) NULL;

        IF COL_LENGTH('Findings', 'DeviationId') IS NULL
            ALTER TABLE Findings ADD DeviationId NVARCHAR(450) NULL;

        -- Feature 036: Make RegisteredSystemId nullable for org-wide components
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SystemComponents') AND name = 'RegisteredSystemId' AND is_nullable = 0)
            ALTER TABLE SystemComponents ALTER COLUMN RegisteredSystemId NVARCHAR(36) NULL;

        -- Person fields on SystemComponents
        IF COL_LENGTH('SystemComponents', 'PersonName') IS NULL
            ALTER TABLE SystemComponents ADD PersonName NVARCHAR(200) NULL;

        IF COL_LENGTH('SystemComponents', 'Email') IS NULL
            ALTER TABLE SystemComponents ADD Email NVARCHAR(200) NULL;
        """;

    try
    {
        await db.Database.ExecuteSqlRawAsync(alterSql, ct);
        logger.LogInformation("Verified schema additions on existing tables");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not apply schema additions — non-fatal");
    }

    // Feature 036: Migrate existing system-scoped components to org-wide assignments
    try
    {
        var componentsToMigrate = await db.SystemComponents
            .Where(c => c.RegisteredSystemId != null)
            .Where(c => !db.ComponentSystemAssignments.Any(a => a.SystemComponentId == c.Id && a.RegisteredSystemId == c.RegisteredSystemId))
            .ToListAsync(ct);

        if (componentsToMigrate.Count > 0)
        {
            foreach (var comp in componentsToMigrate)
            {
                db.ComponentSystemAssignments.Add(new Ato.Copilot.Core.Models.Compliance.ComponentSystemAssignment
                {
                    SystemComponentId = comp.Id,
                    RegisteredSystemId = comp.RegisteredSystemId!,
                    AuthorizationBoundaryDefinitionId = comp.AuthorizationBoundaryDefinitionId,
                    CreatedBy = "system-migration",
                });
                comp.RegisteredSystemId = null;
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Migrated {Count} system-scoped components to org-wide assignments", componentsToMigrate.Count);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not migrate system-scoped components — non-fatal");
    }

    // Feature 037: SSP Document Export tables
    const string sspExportSql = """
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SspExports')
        CREATE TABLE SspExports (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
            SystemId NVARCHAR(36) NOT NULL,
            Format NVARCHAR(10) NOT NULL,
            Status NVARCHAR(20) NOT NULL DEFAULT 'Pending',
            FilePath NVARCHAR(500) NULL,
            FileSize BIGINT NULL,
            ContentHash NVARCHAR(128) NULL,
            TemplateId UNIQUEIDENTIFIER NULL,
            GeneratedBy NVARCHAR(200) NOT NULL,
            GeneratedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
            CompletedAt DATETIMEOFFSET NULL,
            ExpiresAt DATETIMEOFFSET NOT NULL,
            ErrorMessage NVARCHAR(2000) NULL,
            ControlCount INT NULL
        );

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SspExports_SystemId_GeneratedAt')
            CREATE INDEX IX_SspExports_SystemId_GeneratedAt ON SspExports (SystemId, GeneratedAt DESC);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SspExports_ExpiresAt')
            CREATE INDEX IX_SspExports_ExpiresAt ON SspExports (ExpiresAt);

        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SspTemplates')
        CREATE TABLE SspTemplates (
            Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
            Name NVARCHAR(200) NOT NULL,
            Description NVARCHAR(1000) NULL,
            FilePath NVARCHAR(500) NOT NULL,
            FileSize BIGINT NOT NULL,
            MergeFields NVARCHAR(MAX) NULL,
            IsDefault BIT NOT NULL DEFAULT 0,
            IsActive BIT NOT NULL DEFAULT 1,
            UploadedBy NVARCHAR(200) NOT NULL,
            UploadedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
            UpdatedAt DATETIMEOFFSET NULL
        );

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SspTemplates_IsActive_Name')
            CREATE INDEX IX_SspTemplates_IsActive_Name ON SspTemplates (IsActive, Name);
        """;

    try
    {
        await db.Database.ExecuteSqlRawAsync(sspExportSql, ct);
        logger.LogInformation("Verified SSP Export schema (Feature 037)");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not apply SSP Export schema — non-fatal");
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
    services.AddSingleton<Ato.Copilot.Core.Services.NarrativeTemplateService>(sp =>
    {
        var chatClient = sp.GetService<Microsoft.Extensions.AI.IChatClient>();
        var aiOptions = sp.GetService<Microsoft.Extensions.Options.IOptions<Ato.Copilot.Core.Configuration.AzureAiOptions>>()?.Value;
        var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<Ato.Copilot.Core.Services.NarrativeTemplateService>>();
        return new Ato.Copilot.Core.Services.NarrativeTemplateService(chatClient, aiOptions, logger);
    });
    services.AddSingleton<Ato.Copilot.Core.Services.ComplianceTrendSnapshotService>();
    services.AddHostedService(sp => sp.GetRequiredService<Ato.Copilot.Core.Services.ComplianceTrendSnapshotService>());

    // Boundary services (Feature 033)
    services.AddScoped<Ato.Copilot.Core.Services.BoundaryDefinitionService>();
    services.AddSingleton<Ato.Copilot.Agents.Compliance.Services.AzureResourceDiscoveryService>();

    // Roadmap services (Feature 031)
    services.AddScoped<Ato.Copilot.Core.Interfaces.Roadmap.IRoadmapService, Ato.Copilot.Core.Services.RoadmapService>();

    // Deviation services (Feature 035)
    services.AddScoped<Ato.Copilot.Core.Interfaces.Compliance.IDeviationService, Ato.Copilot.Core.Services.DeviationService>();
    services.AddHostedService<Ato.Copilot.Agents.Compliance.Services.DeviationExpirationService>();

    // SSP Export services (Feature 037)
    services.Configure<Ato.Copilot.Core.Configuration.ExportSettings>(
        configuration.GetSection(Ato.Copilot.Core.Configuration.ExportSettings.SectionName));
    services.AddSingleton(System.Threading.Channels.Channel.CreateBounded<Ato.Copilot.Core.Dtos.Dashboard.SspExportJob>(
        new System.Threading.Channels.BoundedChannelOptions(100) { FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait }));
    services.AddScoped<Ato.Copilot.Core.Interfaces.Compliance.ISspExportService,
        Ato.Copilot.Agents.Compliance.Services.SspExportService>();
    services.AddSingleton<Ato.Copilot.Core.Interfaces.Compliance.ISspExportNotifier,
        Ato.Copilot.Mcp.Services.SignalRSspExportNotifier>();
    services.AddHostedService<Ato.Copilot.Agents.Compliance.Services.SspExportBackgroundService>();
    services.AddHostedService<Ato.Copilot.Agents.Compliance.Services.SspExportRetentionService>();
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

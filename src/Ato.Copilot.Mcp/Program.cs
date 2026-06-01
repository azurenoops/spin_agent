using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Azure.Identity;
using Azure.ResourceManager;
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
using Ato.Copilot.Mcp.Endpoints.Onboarding;
using Ato.Copilot.Mcp.Endpoints.Csp;
using Ato.Copilot.Mcp.Endpoints.Tenancy;
using Ato.Copilot.Mcp.Endpoints.Auth;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Logging;
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

// Build a minimal IConfiguration for Serilog bootstrap so the `Serilog`
// appsettings section drives sinks, levels, output templates, and retention.
// Programmatic .Enrich.WithProperty calls below augment whatever the JSON
// configures — they are NOT replaced by ReadFrom.Configuration.
var bootstrapConfig = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile(
        $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
        optional: true)
    .AddEnvironmentVariables("ATO_")
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(bootstrapConfig)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "ATO Copilot")
    // Feature 048 tenant log-context properties (populated per request via
    // LogContext.PushProperty in TenantResolutionMiddleware / TenantContextLogEnricher).
    // Declared up front so they appear consistently in structured logs.
    .Enrich.WithProperty("TenantId", (string?)null)
    .Enrich.WithProperty("EffectiveTenantId", (string?)null)
    .Enrich.WithProperty("ImpersonatedTenantId", (string?)null)
    .Enrich.WithProperty("ActorTenantId", (string?)null)
    .Destructure.With<SensitiveDataDestructuringPolicy>()
    .CreateLogger();

if (mode != "stdio")
{
    // HTTP mode adds Console sink on top of whatever the JSON configures.
    // (stdio mode must NEVER write to Console — that channel is the MCP transport.)
    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(bootstrapConfig)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "ATO Copilot")
        // Feature 048 tenant log-context properties (see comment in stdio config above).
        .Enrich.WithProperty("TenantId", (string?)null)
        .Enrich.WithProperty("EffectiveTenantId", (string?)null)
        .Enrich.WithProperty("ImpersonatedTenantId", (string?)null)
        .Enrich.WithProperty("ActorTenantId", (string?)null)
        .Destructure.With<SensitiveDataDestructuringPolicy>()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
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
catch (Exception ex) when (!IsHostBuildAbortedException(ex))
{
    Log.Fatal(ex, "ATO Copilot terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// Allow Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory to capture the
// built host: it aborts the entry point with HostAbortedException (or the
// HostFactoryResolver internal StopTheHostException) AFTER the host is built.
// Both must propagate out of Main; if the outer catch swallows them the
// integration-test fixture reports "entry point exited without ever building
// an IHost" or "the server has not been started".
static bool IsHostBuildAbortedException(Exception ex)
{
    var typeName = ex.GetType().FullName ?? string.Empty;
    return typeName == "Microsoft.Extensions.Hosting.HostAbortedException"
        || typeName.Contains("HostFactoryResolver", StringComparison.Ordinal);
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
            services.AddAtoCopilotMcp(ctx.Configuration);

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
    builder.Services.AddAtoCopilotMcp(builder.Configuration);

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
    // Feature 048 (T149): tenant impersonation fan-out (consumed by TenantsEndpoints).
    builder.Services.AddSingleton<Ato.Copilot.Mcp.Hubs.ITenantContextNotifier,
        Ato.Copilot.Mcp.Hubs.TenantContextNotifier>();
    // Feature 048 (T187, US8/SC-005): CSP cross-tenant dashboard fan-out
    // (broadcast on tenant Status transitions from TenantsEndpoints).
    builder.Services.AddSingleton<Ato.Copilot.Mcp.Hubs.ICspDashboardNotifier,
        Ato.Copilot.Mcp.Hubs.CspDashboardNotifier>();

    // Onboarding wizard (Feature 047) — bind options + register policy / services / hosted job worker.
    builder.Services.Configure<Ato.Copilot.Core.Configuration.OnboardingOptions>(
        builder.Configuration.GetSection(Ato.Copilot.Core.Configuration.OnboardingOptions.SectionName));

    // Register a default authentication scheme that surfaces the principal already
    // populated by CacAuthenticationMiddleware. Required so AuthorizationMiddleware
    // can issue ChallengeAsync/ForbidAsync for endpoints with RequireAuthorization().
    //
    // Feature 051 T080 [US4] — Entra/JWT failure classification map:
    //
    //   | LoginErrorClass       | Where it's wired today                              |
    //   |-----------------------|-----------------------------------------------------|
    //   | NoTenantAssignment    | AuthEndpoints.GetMeAsync (writes audit + 403)        |
    //   | MfaFailure            | CacAuthenticationMiddleware amr branch (T079)        |
    //   | NoCardInserted        | CLIENT-SIDE — browser surfaces cert prompt           |
    //   | CertExpired           | TODO(Phase 13) — needs cert-auth handler             |
    //   | CertNotYetValid       | TODO(Phase 13) — needs cert-auth handler             |
    //   | CertRevoked           | TODO(Phase 13) — needs OCSP/CRL fetcher              |
    //   | ClockSkew             | TODO(Phase 13) — needs nbf/iat skew detector         |
    //   | AccountDisabled       | TODO(Phase 13) — needs JwtBearerEvents.OnAuthFailed  |
    //   | ConditionalAccessBlock| TODO(Phase 13) — needs MSAL claims-challenge handler |
    //   | NetworkFailure        | CLIENT-SIDE — MSAL surfaces network errors           |
    //
    // The classifier (LoginErrorClassifier) supports all 10 classes via pure inputs;
    // wiring the cert-auth / Conditional-Access paths is deferred until Phase 13
    // installs a real X509 client-cert authentication handler on the gateway.
    builder.Services
        .AddAuthentication(Ato.Copilot.Mcp.Authentication.CacPassthroughAuthHandler.SchemeName)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
                   Ato.Copilot.Mcp.Authentication.CacPassthroughAuthHandler>(
            Ato.Copilot.Mcp.Authentication.CacPassthroughAuthHandler.SchemeName,
            _ => { });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy(
            Ato.Copilot.Mcp.Authorization.OnboardingAdministratorRequirement.PolicyName,
            policy => policy.Requirements.Add(
                new Ato.Copilot.Mcp.Authorization.OnboardingAdministratorRequirement()));
    });
    builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
        Ato.Copilot.Mcp.Authorization.OnboardingAdministratorHandler>();
    builder.Services.AddSingleton<Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs.WizardJobChannel>();
    builder.Services.AddSingleton<Ato.Copilot.Core.Interfaces.Onboarding.IWizardProgressNotifier,
        Ato.Copilot.Mcp.Hubs.Onboarding.SignalRWizardProgressNotifier>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IWizardAuditService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.Auditing.WizardAuditService>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IBootstrapAdministratorService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.BootstrapAdministratorService>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IOnboardingStateService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.OnboardingStateService>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IWizardJobRunner,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs.WizardJobRunner>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IWizardArtifactDependencyService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.WizardArtifactDependencyService>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IOrganizationContextService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.OrganizationContextService>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IDirectorySearchClient,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.GraphDirectorySearchClient>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IPersonService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.PersonService>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IOrganizationRoleAssignmentService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.OrganizationRoleAssignmentService>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IRegisteredSystemRoleSnapshotter,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.RegisteredSystemRoleSnapshotter>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IEmassImportParser,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass.EmassImportParser>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IEmassImportService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass.EmassImportService>();
    builder.Services.AddScoped<Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs.IWizardJobHandler,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass.Handlers.EmassParseJobHandler>();
    builder.Services.AddScoped<Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs.IWizardJobHandler,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass.Handlers.EmassCommitJobHandler>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.ISspPdfExtractionService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.SspPdf.SspPdfExtractionService>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.ISspPdfImportService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.SspPdf.SspPdfImportService>();
    builder.Services.AddScoped<Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs.IWizardJobHandler,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.SspPdf.Handlers.SspPdfExtractJobHandler>();
    builder.Services.AddSingleton<Ato.Copilot.Core.Interfaces.Onboarding.IDelegatedArmTokenProvider,
        Ato.Copilot.Mcp.Onboarding.ConfiguredArmTokenProvider>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IAzureSubscriptionEnumerationService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.AzureSubscriptions.AzureSubscriptionEnumerationService>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IAzureSubscriptionRegistrationService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.AzureSubscriptions.AzureSubscriptionRegistrationService>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IAzureSubscriptionScopeResolver,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.AzureSubscriptions.AzureSubscriptionScopeResolver>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IOrganizationTemplateService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.Templates.OrganizationTemplateService>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.INarrativeSeedDocumentService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.NarrativeSeeds.NarrativeSeedDocumentService>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Onboarding.IWizardArtifactInventoryService,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.WizardArtifactInventoryService>();
    builder.Services.AddScoped<Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs.IWizardJobHandler,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.NarrativeSeeds.Handlers.NarrativeSeedIndexJobHandler>();
    builder.Services.AddScoped<Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs.IWizardJobHandler,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.Cascade.ExportRerenderJobHandler>();
    builder.Services.AddScoped<Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs.IWizardJobHandler,
        Ato.Copilot.Agents.Compliance.Services.Onboarding.Cascade.ImportRerenderJobHandler>();
    builder.Services.AddHostedService<Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs.WizardJobHostedService>();

    // Tenancy (Feature 048) — bind options + register ITenantContext / accessor.
    // The accessor is Singleton (AsyncLocal-backed); the context itself is Scoped
    // and populated per-request by TenantResolutionMiddleware (added in Phase 3).
    builder.Services.Configure<Ato.Copilot.Mcp.Configuration.DeploymentOptions>(
        builder.Configuration.GetSection(Ato.Copilot.Mcp.Configuration.DeploymentOptions.SectionName));
    builder.Services.Configure<Ato.Copilot.Mcp.Configuration.RoleClaimMappingsOptions>(
        builder.Configuration.GetSection(Ato.Copilot.Mcp.Configuration.RoleClaimMappingsOptions.SectionName));

    // Feature 051 (T021–T024 / FR-001..FR-036a): bind AuthOptions + register the
    // startup validator. Per contracts/internal-services.md § 5.2 the validator
    // requires IHostEnvironment so it can flip the cookie-key gate on/off based
    // on ASPNETCORE_ENVIRONMENT. ValidateOnStart fails fast in Staging/Production
    // if Auth:Cookie:SigningKey is missing.
    builder.Services.AddOptions<Ato.Copilot.Core.Configuration.Auth.AuthOptions>()
        .Bind(builder.Configuration.GetSection(Ato.Copilot.Core.Configuration.Auth.AuthOptions.SectionName))
        .ValidateOnStart();
    builder.Services.AddSingleton<
        Microsoft.Extensions.Options.IValidateOptions<Ato.Copilot.Core.Configuration.Auth.AuthOptions>,
        Ato.Copilot.Core.Configuration.Auth.AuthOptionsValidator>();

    // Feature 051 (T028): the audit-write service. Scoped because it uses
    // IDbContextFactory<AtoCopilotContext> and follows the F050
    // CapabilityHistoryService SRP — AppendAsync does not call
    // SaveChangesAsync (caller owns the transaction).
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Auth.ILoginAuditService,
        Ato.Copilot.Core.Services.Auth.LoginAuditService>();

    // Feature 051 (T039): forensic context extractor used by Auth endpoints
    // to populate SourceIp / UserAgent / CorrelationId on every audit row.
    // Scoped to match request lifetime (it operates on HttpContext).
    builder.Services.AddScoped<Ato.Copilot.Mcp.Middleware.LoginAuditContextAccessor>();

    // Feature 051 (T067 / US3 / FR-012): HMAC-SHA256 issuer + validator for
    // the device-only `ato-remembered-tenant` cookie. Singleton because the
    // signing key is bound once at construction from AuthOptions.
    builder.Services.AddSingleton<Ato.Copilot.Core.Interfaces.Auth.IRememberedTenantCookieService,
        Ato.Copilot.Core.Services.Auth.RememberedTenantCookieService>();

    // Feature 051 (T099 / FR-036a): cold-archive sink + daily hosted
    // service. Sink is selected by Auth:Archive:Sink (FileSystem in
    // dev / CI, AzureBlobAppend in prod against the AzureUSGovernment
    // storage account configured in Auth:Archive:AzureBlobAccountUrl).
    // The hosted service wakes at Auth:Archive:RunHourUtc and migrates
    // LoginAuditEvent rows older than 13 months in 1,000-row batches.
    var auth051ArchiveOptions = builder.Configuration
        .GetSection(Ato.Copilot.Core.Configuration.Auth.AuthOptions.SectionName)
        .Get<Ato.Copilot.Core.Configuration.Auth.AuthOptions>()
        ?.Archive
        ?? new Ato.Copilot.Core.Configuration.Auth.AuthArchiveOptions();
    if (auth051ArchiveOptions.Sink == Ato.Copilot.Core.Configuration.Auth.ArchiveSinkKind.AzureBlobAppend)
    {
        builder.Services.AddSingleton<Ato.Copilot.Core.Interfaces.Auth.ILoginAuditArchiveSink,
            Ato.Copilot.Core.Services.Auth.AzureBlobAppendArchiveSink>();
    }
    else
    {
        builder.Services.AddSingleton<Ato.Copilot.Core.Interfaces.Auth.ILoginAuditArchiveSink,
            Ato.Copilot.Core.Services.Auth.FileSystemArchiveSink>();
    }
    builder.Services.AddHostedService<Ato.Copilot.Core.Services.Auth.LoginAuditArchiveService>();

    // Feature 051 (T032 / FR-034 / FR-035): throttle service + the
    // IDistributedCache backing store. Dev/Test use the in-process
    // distributed memory cache so unit tests need no Redis; non-Development
    // wires Redis per contracts/internal-services.md § 6 + research § R7.
    // Per analysis C11 any non-Development environment uses the Production
    // throttle block + Redis cache.
    builder.Services.AddSingleton<Ato.Copilot.Core.Interfaces.Auth.ILoginThrottleService,
        Ato.Copilot.Core.Services.Auth.LoginThrottleService>();
    if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
    {
        builder.Services.AddDistributedMemoryCache();
    }
    else
    {
        builder.Services.AddStackExchangeRedisCache(o =>
        {
            o.Configuration = builder.Configuration.GetConnectionString("Redis")
                ?? "localhost:6379";
            o.InstanceName = "ato-throttle:";
        });
    }

    builder.Services.AddSingleton<Ato.Copilot.Core.Interfaces.Tenancy.ITenantContextAccessor,
        Ato.Copilot.Core.Services.Tenancy.TenantContextAccessor>();
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.ITenantContext,
        Ato.Copilot.Core.Services.Tenancy.TenantContext>();
    // T041: SaveChanges interceptor that stamps TenantId + validates FK consistency.
    builder.Services.AddSingleton<Ato.Copilot.Core.Data.Interceptors.TenantStampingSaveChangesInterceptor>();
    // T107 [US5]: SQL Server SESSION_CONTEXT publisher — emits TenantId /
    // EffectiveTenantId / IsCspAdmin on every connection open so the RLS
    // policy installed by RlsPolicyInstaller can enforce defense-in-depth
    // tenancy at the database layer (FR-030 / FR-031). No-op on SQLite.
    builder.Services.AddSingleton<Ato.Copilot.Core.Data.Interceptors.SqlServerSessionContextConnectionInterceptor>();
    // T066: tenant provisioning surface for /api/tenants endpoints.
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.ITenantProvisioningService,
        Ato.Copilot.Core.Services.Tenancy.TenantProvisioningService>();
    // T093 [US4]: Tenant-and-Organization onboarding wizard service
    // (Feature 048 / FR-054). Scoped so it can pull a fresh DbContext per
    // request via IDbContextFactory.
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.ITenantOnboardingService,
        Ato.Copilot.Core.Services.Tenancy.TenantOnboardingService>();
    // T162 [US7]: CSP-Admin singleton-profile + onboarding-wizard service
    // (Feature 048 / FR-006 / FR-090 / FR-092). Scoped to honor
    // IDbContextFactory + IMemoryCache lifetimes; the 30 s read cache is
    // backed by the singleton IMemoryCache so it spans requests.
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.ICspProfileService,
        Ato.Copilot.Core.Services.Tenancy.CspProfileService>();
    // T177 [US8]: CSP-Admin cross-tenant operational dashboard service
    // (Feature 048 / FR-094 / FR-098). Scoped to share the
    // IDbContextFactory pool + ITenantContext request scope. Reads execute on
    // the CSP-Admin global path (T042 query filter returns every row).
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.ICspDashboardService,
        Ato.Copilot.Core.Services.Tenancy.CspDashboardService>();
    // T123 (FR-073..FR-076): shared multi-tenant migration logic used by
    // both /api/admin/migrate-to-multitenant and `ato-cli tenant migrate`.
    builder.Services.AddScoped<Ato.Copilot.Core.Services.Tenancy.MultiTenantMigrationService>();
    // T134 (FR-081/FR-082): cross-tenant baseline publish/unpublish service.
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.IGlobalBaselineService,
        Ato.Copilot.Core.Services.Tenancy.GlobalBaselineService>();
    // Feature 048 follow-up (user ask #2): per-org NIST control overrides.
    builder.Services.AddScoped<Ato.Copilot.Core.Interfaces.Tenancy.IOrgControlOverrideService,
        Ato.Copilot.Core.Services.Tenancy.OrgControlOverrideService>();
    // T067: HMAC-signed impersonation cookie service. Singleton because the
    // signing key is loaded once at startup; all state lives in the cookie.
    builder.Services.AddSingleton<Ato.Copilot.Mcp.Services.Tenancy.ITenantImpersonationService>(sp =>
    {
        var cfg = sp.GetRequiredService<IConfiguration>();
        var env = sp.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>();
        var key = cfg["Auth:Impersonation:SigningKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            // Feature 048 §T152 (OWASP A02): in production, missing key is
            // fatal at startup. The intended source is Azure Key Vault, wired
            // through the configuration provider chain as
            // Auth:Impersonation:SigningKey (base64-encoded 32-byte value).
            if (env.IsProduction())
            {
                throw new InvalidOperationException(
                    "Auth:Impersonation:SigningKey is not configured. Production " +
                    "deployments MUST source this from Azure Key Vault — refusing to " +
                    "fall back to the dev-only key.");
            }
            // Development fallback only — stable, non-secret dev key.
            key = "dev-only-impersonation-signing-key-change-me-please-32b";
        }
        return new Ato.Copilot.Mcp.Services.Tenancy.TenantImpersonationService(key);
    });

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
    // Feature 051 T144 [Phase 13.1]: per-IP + per-identity throttle
    // (FR-034 / FR-035) wraps the auth-gated /api/auth/* endpoints.
    // Placed AFTER CacAuthenticationMiddleware so the throttle can read
    // the resolved principal's `oid` for the identity key when present
    // (the inbound peek + outbound register paths both honor the
    // analysis-C17 signal selector).
    app.UseMiddleware<LoginThrottleMiddleware>();
    // T069 (Feature 048): tenant scope MUST be resolved BEFORE authorization
    // checks so role gates can read ITenantContext.IsCspAdmin and the global
    // EF query filter sees the resolved EffectiveTenantId.
    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseMiddleware<ComplianceAuthorizationMiddleware>();
    app.UseMiddleware<RequestMetricsMiddleware>();
    app.UseMiddleware<AuditLoggingMiddleware>();
    app.UseOnboardingTelemetry();

    // Standard ASP.NET Core authentication + authorization run only for endpoints with
    // [Authorize] / RequireAuthorization() metadata (Feature 047 wizard endpoints).
    // The custom CAC + Compliance middleware above still gates everything else.
    app.UseAuthentication();
    app.UseAuthorization();

    // Map MCP HTTP endpoints
    var httpBridge = app.Services.GetRequiredService<McpHttpBridge>();
    httpBridge.MapEndpoints(app);

    // Map Dashboard REST API endpoints (Feature 030)
    app.MapDashboardEndpoints();

    // Feature 051 [US1]: dashboard login surface — login-config + me.
    // The endpoint group lives under /api/auth and is anonymous-by-default
    // (the GET /me handler enforces its own authentication check).
    app.MapAuthEndpoints();

    // Map Authorization Package & SAR endpoints (Feature 041)
    app.MapPackageEndpoints();

    // Map Notification REST API endpoints
    app.MapNotificationEndpoints();

    // Map Onboarding Wizard endpoints (Feature 047)
    app.MapOnboardingStateEndpoints();
    app.MapWizardJobsEndpoints();
    app.MapOrganizationContextEndpoints();
    app.MapPersonEndpoints();
    app.MapRoleAssignmentEndpoints();
    // Feature 049 (T040a): unified per-system 7-role surface
    // (POST/GET /api/roles/system/{systemId}, POST/GET /api/roles/organization,
    // GET /api/roles/effective-role) per FR-008..FR-027.
    app.MapSystemRolesEndpoints();
    app.MapEmassImportEndpoints();
    app.MapSspPdfImportEndpoints();
    app.MapAzureSubscriptionEndpoints();
    app.MapOrganizationTemplateEndpoints();
    app.MapNarrativeSeedEndpoints();
    app.MapImportedDocumentsEndpoints();
    // Feature 048 (T070): tenants administration + impersonation surface.
    app.MapTenantsEndpoints();
    // Feature 048 (T084): deployment-mode probe for dashboard mode-aware UI.
    app.MapDeploymentEndpoints();
    // Feature 048 (T093 [US4]): tenant-and-organization onboarding wizard.
    app.MapTenantOnboardingEndpoints();
    // Feature 048 (T163 [US7]): CSP-Admin onboarding wizard.
    app.MapCspOnboardingEndpoints();
    // Feature 048 (T208 [US9]): CSP-inherited components management surface
    // — read-only across tenants, write-gated to CSP-Admin (FR-104..FR-106).
    app.MapCspInheritedComponentEndpoints();
    // Feature 048 (T181 [US8]): CSP-Admin cross-tenant operational dashboard.
    app.MapCspDashboardEndpoints();
    // Feature 048 (T116 [US6]): CSP-Admin audit query surface.
    app.MapAuditQueryEndpoints();
    // Feature 048 (T124, FR-073..FR-076): CSP-Admin migration utility surface.
    app.MapAdminMigrationEndpoints();
    // Feature 048 (T135, FR-081/FR-082): CSP-Admin cross-tenant baseline publish/unpublish surface.
    app.MapGlobalBaselineEndpoints();
    // Feature 048 follow-up (user ask #2): per-org NIST control override surface.
    app.MapOrgControlOverrideEndpoints();

    // Map SignalR notification hub
    app.MapHub<Ato.Copilot.Mcp.Hubs.NotificationHub>("/hubs/notifications");
    app.MapHub<Ato.Copilot.Mcp.Hubs.PackageHub>("/hubs/package");
    // Feature 048 (T149): tenant impersonation fan-out for connected dashboards.
    app.MapHub<Ato.Copilot.Mcp.Hubs.TenantContextHub>("/hubs/tenant-context");
    // Feature 048 (T187, US8/SC-005): CSP cross-tenant dashboard fan-out for
    // tenant Status transitions (Active/Suspended/Disabled).
    app.MapHub<Ato.Copilot.Mcp.Hubs.CspDashboardHub>("/hubs/csp-dashboard");

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
    var deploymentOpts = scope.ServiceProvider
        .GetService<Microsoft.Extensions.Options.IOptions<Ato.Copilot.Mcp.Configuration.DeploymentOptions>>()?.Value;

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
            await EnsureSchemaAdditionsAsync(db, logger, cts.Token, deploymentOpts);

            logger.LogInformation("SQL Server database ready");
        }
        else
        {
            logger.LogInformation("Applying database migrations...");
            await db.Database.MigrateAsync(cts.Token);
            // Feature 048 (T056): apply additive tenancy schema (Tenants /
            // Organizations tables, plus TenantId columns on every
            // [TenantScoped] entity) on top of the migration baseline. The
            // additions are idempotent so it's safe to re-run on every boot.
            await EnsureSchemaAdditionsAsync(db, logger, cts.Token, deploymentOpts);
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
async Task EnsureSchemaAdditionsAsync(AtoCopilotContext db, Microsoft.Extensions.Logging.ILogger<AtoCopilotContext> logger, CancellationToken ct, Ato.Copilot.Mcp.Configuration.DeploymentOptions? deploymentOptions = null)
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

        IF COL_LENGTH('SystemComponents', 'RmfRoleName') IS NULL
            ALTER TABLE SystemComponents ADD RmfRoleName NVARCHAR(50) NULL;

        -- Feature 044: widen FrameworkControls string columns from the original
        -- 800-53-sized widths (Family=10, ControlId/Parent/WithdrawnTo=20) to
        -- nvarchar(50). NIST 800-171 Rev 3 family ids ("SP_800_171_03.01", 16
        -- chars) and normalized control ids ("SP_800_171_03(01.01", ~19 chars)
        -- overflow the narrow columns, raising "String or binary data would be
        -- truncated in column 'Family'" and aborting the 800-171 import.
        -- EnsureCreated never alters existing columns, so widen them here.
        -- max_length is in BYTES for nvarchar (2 per char), so 50 chars = 100.
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FrameworkControls') AND name = 'Family' AND max_length < 100)
            ALTER TABLE FrameworkControls ALTER COLUMN Family NVARCHAR(50) NOT NULL;

        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FrameworkControls') AND name = 'ControlId' AND max_length < 100)
            ALTER TABLE FrameworkControls ALTER COLUMN ControlId NVARCHAR(50) NOT NULL;

        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FrameworkControls') AND name = 'ParentControlId' AND max_length < 100)
            ALTER TABLE FrameworkControls ALTER COLUMN ParentControlId NVARCHAR(50) NULL;

        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FrameworkControls') AND name = 'WithdrawnTo' AND max_length < 100)
            ALTER TABLE FrameworkControls ALTER COLUMN WithdrawnTo NVARCHAR(50) NULL;
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

    // Feature 047: Repair Persons.UX_Person_Tenant_EntraObjectId — drop the
    // unfiltered unique index and recreate it with `[EntraObjectId] IS NOT NULL`
    // so multiple local-only persons (no Entra OID) can coexist per tenant.
    // SQL Server treats NULL as a single value in a UNIQUE index by default.
    const string personIndexRepairSql = """
        IF EXISTS (
            SELECT 1
            FROM sys.indexes i
            JOIN sys.objects o ON o.object_id = i.object_id
            WHERE o.name = 'Persons'
              AND i.name = 'UX_Person_Tenant_EntraObjectId'
              AND i.has_filter = 0)
        BEGIN
            DROP INDEX UX_Person_Tenant_EntraObjectId ON Persons;
            CREATE UNIQUE INDEX UX_Person_Tenant_EntraObjectId
                ON Persons (TenantId, EntraObjectId)
                WHERE EntraObjectId IS NOT NULL;
        END
        """;

    try
    {
        await db.Database.ExecuteSqlRawAsync(personIndexRepairSql, ct);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not repair Persons unique index — non-fatal");
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

    // Feature 040: Component-Centric Boundary Model — add Azure fields + ComponentId FK
    const string feature040Sql = """
        -- Azure resource fields on SystemComponents
        IF COL_LENGTH('SystemComponents', 'AzureResourceId') IS NULL
            ALTER TABLE SystemComponents ADD AzureResourceId NVARCHAR(500) NULL;
        IF COL_LENGTH('SystemComponents', 'AzureResourceType') IS NULL
            ALTER TABLE SystemComponents ADD AzureResourceType NVARCHAR(200) NULL;
        IF COL_LENGTH('SystemComponents', 'AzureResourceGroup') IS NULL
            ALTER TABLE SystemComponents ADD AzureResourceGroup NVARCHAR(200) NULL;
        IF COL_LENGTH('SystemComponents', 'AzureLocation') IS NULL
            ALTER TABLE SystemComponents ADD AzureLocation NVARCHAR(100) NULL;

        -- Index for dedup/linkage on AzureResourceId
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SystemComponent_AzureResourceId')
            CREATE INDEX IX_SystemComponent_AzureResourceId ON SystemComponents (AzureResourceId);

        -- ComponentId FK on Findings
        IF COL_LENGTH('Findings', 'ComponentId') IS NULL
            ALTER TABLE Findings ADD ComponentId NVARCHAR(450) NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ComplianceFinding_ComponentId')
            CREATE INDEX IX_ComplianceFinding_ComponentId ON Findings (ComponentId);

        -- BoundaryComponentAssignment table
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'BoundaryComponentAssignments')
        CREATE TABLE BoundaryComponentAssignments (
            Id NVARCHAR(450) NOT NULL PRIMARY KEY,
            SystemComponentId NVARCHAR(450) NOT NULL,
            AuthorizationBoundaryDefinitionId NVARCHAR(450) NOT NULL,
            IsInScope BIT NOT NULL DEFAULT 1,
            ExclusionRationale NVARCHAR(1000) NULL,
            InheritanceProvider NVARCHAR(500) NULL,
            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
            CreatedBy NVARCHAR(200) NULL
        );

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BCA_ComponentBoundary')
            CREATE UNIQUE INDEX IX_BCA_ComponentBoundary ON BoundaryComponentAssignments (SystemComponentId, AuthorizationBoundaryDefinitionId);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BCA_BoundaryId')
            CREATE INDEX IX_BCA_BoundaryId ON BoundaryComponentAssignments (AuthorizationBoundaryDefinitionId);
        """;

    try
    {
        await db.Database.ExecuteSqlRawAsync(feature040Sql, ct);
        logger.LogInformation("Verified Feature 040 schema (Component-Centric Boundary)");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not apply Feature 040 schema — non-fatal");
    }

    // Feature 048: Tenancy schema additions (Tenants, Organizations) and system-tenant bootstrap.
    await Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions.TenantsAndOrganizationsSchemaAdditions
        .ApplyAsync(db, logger, ct);
    // Feature 048 (T056): Adds TenantId column + index to every retrofitted
    // [TenantScoped] entity table. Idempotent / additive.
    await Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions.TenantIdColumnAdditions
        .ApplyAsync(db, logger, ct);
    // Feature 048 (T073): Adds AuditLogs.ActorTenantId / ImpersonatedTenantId
    // columns plus the two composite tenant-attribution indexes.
    await Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions.AuditLogTenantAttributionAdditions
        .ApplyAsync(db, logger, ct);
    // Feature 048 (T134): Adds GlobalBaselines table for cross-tenant sharing.
    await Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions.GlobalBaselineSchemaAdditions
        .ApplyAsync(db, logger, ct);
    // Feature 050 (FR-004 / FR-014 / FR-015): Adds the CapabilityHistoryEvents
    // table for CSP-inherited capability lifecycle auditing. Tenant-scoped
    // (Cascade FK to Tenants); intentionally no DB-level FK to capabilities
    // so history outlives a hard-deleted capability per FR-015.
    await Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions.CapabilityHistoryEventsSchemaAdditions
        .ApplyAsync(db, logger, ct);
    // Feature 051 (FR-032 / FR-033 / FR-034 / FR-036a): Adds the LoginAuditEvents
    // table for authentication-event auditing across all four surfaces
    // (Dashboard, VS Code, M365, Chat). Tenant-scoped on EffectiveTenantId
    // (Cascade FK to Tenants); pre-session and NoTenantAssignment rows use
    // SYSTEM_TENANT_ID per clarification Q2. Three indexes ship: per-tenant
    // read (Tenant, OccurredAt DESC), daily archive scan (OccurredAt), and
    // forensic per-Oid lookup (Oid, OccurredAt DESC, filtered Oid IS NOT NULL).
    await Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions.LoginAuditEventsSchemaAdditions
        .ApplyAsync(db, logger, ct);
    await Ato.Copilot.Core.Services.Tenancy.TenantBootstrapService.EnsureSystemTenantAsync(db, logger, ct);
    // T060: Backfill OrganizationContext rows whose TenantId still holds the
    // Entra `tid` rather than the new Tenants.Id. Idempotent; no-op once done.
    await Ato.Copilot.Core.Services.Tenancy.TenantBootstrapService
        .BackfillOrganizationContextTenantIdsAsync(db, logger, ct);

    // Feature 048 (T081–T083, FR-070 / FR-071): in SingleTenant mode ensure
    // the default tenant exists and backfill any NULL TenantId rows; in
    // MultiTenant mode fail fast if any retrofitted table still holds NULL
    // TenantIds. The deployment options are passed through from the
    // Migrate/Startup call sites which both have IServiceProvider access.
    // Skip when deploymentOptions is null (e.g. when EnsureNewTablesAsync's
    // nested call runs the schema additions during table creation — the
    // outer call site that owns the deployment options will perform the
    // bootstrap once tables are confirmed present).
    if (deploymentOptions is not null)
    {
        var isSingleTenant = deploymentOptions.Mode == Ato.Copilot.Mcp.Configuration.DeploymentMode.SingleTenant;
        await Ato.Copilot.Core.Services.Tenancy.TenantBootstrapService
            .EnsureDefaultTenantAndBackfillAsync(
                db,
                isSingleTenantMode: isSingleTenant,
                defaultTenantIdOverride: deploymentOptions.DefaultTenantId,
                logger,
                ct);
    }

    // Feature 048 (T109 / T110, FR-030 – FR-032): install Row-Level Security
    // FILTER + BLOCK predicates on every [TenantScoped] table when running
    // against SQL Server. Idempotent (drops + re-creates the policy so a new
    // [TenantScoped] entity added in a later release picks up the predicate
    // automatically). For SQLite, RlsPolicyInstaller.ApplyAsync emits the
    // FR-033 startup warning that RLS is unavailable.
    await Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions.RlsPolicyInstaller
        .ApplyAsync(db, logger, ct);
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

// ----------------------------------------------------------------------------
// Test-host hook (Feature 048 / WebApplicationFactory)
// ----------------------------------------------------------------------------
// `Program` is the implicit class produced by C# top-level statements. The
// integration test project references both the MCP host (this project) and
// the Chat host, each of which produces a global-namespace `Program`. To
// allow `WebApplicationFactory<McpProgram>` to target the MCP host
// unambiguously from tests, we expose this empty marker class in the
// `Ato.Copilot.Mcp` namespace. WebApplicationFactory only uses TEntryPoint
// to locate the application's assembly — it does not invoke the type itself.
namespace Ato.Copilot.Mcp
{
    /// <summary>
    /// Test-host marker for <c>Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory&lt;T&gt;</c>.
    /// Tests that need to bring up the MCP host should use
    /// <c>WebApplicationFactory&lt;McpProgram&gt;</c>; the factory will discover
    /// the top-level Program entry point via the assembly that defines
    /// <see cref="McpProgram"/>.
    /// </summary>
    public sealed class McpProgram { }
}

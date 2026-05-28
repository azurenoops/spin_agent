using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// Test fixture that boots the host with two seeded tenants (Tenant A, Tenant B)
/// and replaces the production <see cref="ITenantContext"/> registration with
/// a switchable <see cref="FakeTenantContext"/>. Tests can flip the active
/// tenant per request via <see cref="GetActiveContext"/>.
/// </summary>
/// <typeparam name="TStartup">The startup / Program type to spin up.</typeparam>
/// <remarks>
/// This is the canonical fixture for Feature 048 cross-tenant isolation tests
/// (US1, US2, US3). It is intentionally minimal: it exposes the seed tenant
/// ids as static fields so test cases can assert against well-known values.
/// The fixture replaces only the <see cref="ITenantContext"/> registration —
/// the tenant accessor (Singleton) is left untouched so AsyncLocal flow is
/// preserved.
/// </remarks>
public class MultiTenantWebApplicationFactory<TStartup> : WebApplicationFactory<TStartup>
    where TStartup : class
{
    /// <summary>Stable id of seeded Tenant A. Tests use this id when asserting.</summary>
    public static readonly Guid TenantAId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>Stable id of seeded Tenant B.</summary>
    public static readonly Guid TenantBId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // Use the production TenantContext type so the middleware's downcast
    // (var ctx = (TenantContext)tenantContext;) succeeds. The class exposes
    // public setters for all switchable fields, so tests can mutate it
    // directly via GetActiveContext().
    private readonly TenantContext _activeContext = new() { TenantId = TenantAId };
    private readonly string _sqliteFile = Path.Combine(
        Path.GetTempPath(),
        $"ato-copilot-tests-{Guid.NewGuid():N}.db");

    /// <summary>Returns the live <see cref="TenantContext"/> the test is mutating.</summary>
    public TenantContext GetActiveContext() => _activeContext;

    /// <summary>
    /// Drops every <c>CspProfile</c> row and clears the
    /// <see cref="ICspProfileService"/> cache so the next test method sees a
    /// fresh "Pending" deployment. Intended for callers that share a class
    /// fixture and would otherwise inherit prior tests' onboarded state
    /// (Feature 048 / US7 / T155-T159 isolation).
    /// </summary>
    public async Task ResetCspProfileAsync(CancellationToken ct = default)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        // Use raw SQL — EF's DbSet.RemoveRange() would require Tracking and
        // hits the [GlobalReference] interceptor which expects a TenantId.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"CspProfiles\";", ct);

        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        cache.Remove(Ato.Copilot.Core.Services.Tenancy.CspProfileService.CacheKey);
    }

    public MultiTenantWebApplicationFactory()
    {
        // T112 [US5]: when ATO_TEST_SQLSERVER_CONNSTRING is set (typically
        // by an outer fixture that boots a Testcontainers SQL Server), wire
        // the host to use SQL Server instead of the per-fixture SQLite file.
        // This lets RLS-enabled integration tests run the *full* HTTP pipeline
        // against a server that actually enforces the BLOCK predicate.
        var sqlServerConn = Environment.GetEnvironmentVariable("ATO_TEST_SQLSERVER_CONNSTRING");
        if (!string.IsNullOrEmpty(sqlServerConn))
        {
            Environment.SetEnvironmentVariable("ATO_Database__Provider", "SqlServer");
            Environment.SetEnvironmentVariable("ATO_ConnectionStrings__DefaultConnection", sqlServerConn);
        }
        else
        {
            // Force SQLite + a per-fixture unique file-backed DB BEFORE the host
            // builder reads configuration. The MCP host's default appsettings.json
            // points at SQL Server which is not reachable from the unit-test
            // environment. Program.cs registers env-var configuration with the
            // "ATO_" prefix, so we MUST use that prefix for the override to win
            // over appsettings.json.
            Environment.SetEnvironmentVariable("ATO_Database__Provider", "Sqlite");
            Environment.SetEnvironmentVariable("ATO_ConnectionStrings__DefaultConnection",
                $"Data Source={_sqliteFile};Mode=ReadWriteCreate");
        }

        Environment.SetEnvironmentVariable("ATO_Deployment__Mode", "MultiTenant");
        Environment.SetEnvironmentVariable("ATO_Deployment__Tenants__AllowSelfOnboarding", "false");
        Environment.SetEnvironmentVariable("ATO_Auth__Impersonation__SigningKey",
            "ato-copilot-tests-impersonation-signing-key-stable-32B!");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");

        // Bypass the production TenantResolutionMiddleware so the FakeTenantContext
        // injected by ConfigureServices is the sole authority. The middleware
        // honors this only when explicitly opted in via configuration.
        Environment.SetEnvironmentVariable("ATO_Tenant__Resolution__BypassForTests", "true");
        // Authentication bypass for tests — ComplianceAuthorizationMiddleware
        // would otherwise return 401 because no JWT/CAC handler is wired up.
        Environment.SetEnvironmentVariable("ATO_Auth__BypassForTests", "true");
    }

    /// <summary>
    /// Returns the <c>Deployment:Mode</c> this fixture should pin via
    /// in-memory configuration. Defaults to <c>"MultiTenant"</c>; derived
    /// fixtures (e.g. <c>SingleTenantWebApplicationFactory</c>,
    /// <c>SharedSqliteFactory</c>) override to flip mode without
    /// process-global env-var contention.
    /// </summary>
    protected virtual string DeploymentModeOverride => "MultiTenant";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Pin the deployment mode for THIS host via in-memory configuration.
        // Env-var-only setting (`ATO_Deployment__Mode`) is process-global and
        // races with sibling fixtures (e.g. CspOnboardingSingleTenantTests's
        // `SingleTenantFactory`) when xUnit constructs them in parallel.
        // In-memory configuration is per-host and resolves the race.
        builder.ConfigureAppConfiguration(cfg =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Deployment:Mode"] = DeploymentModeOverride,
                ["Deployment:Tenants:AllowSelfOnboarding"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Ignore unhandled background-service exceptions in tests so the
            // host stays up when ancillary background services (e.g. evidence
            // purges, SSP export retention) hit unexpected schema in our
            // SQLite test DB. The default StopHost behavior would tear down
            // the test server.
            services.Configure<HostOptions>(o =>
                o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

            // Replace ITenantContext (Scoped) with the test-controlled instance.
            services.RemoveAll<ITenantContext>();
            services.AddScoped<ITenantContext>(_ => _activeContext);

            // Ensure accessor is the production AsyncLocal-backed one.
            services.RemoveAll<ITenantContextAccessor>();
            services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();

            // Remove hosted services whose StartAsync is hard-coded for SQL
            // Server. These would crash a SQLite-backed test host. Only the
            // tenancy seed runs as a hosted service in tests.
            RemoveHostedService<Ato.Copilot.Core.Services.BoundaryMigrationService>(services);

            // Seed tenants A and B AFTER the host's database migration runs.
            // We use a hosted service rather than calling EnsureCreated() at
            // ConfigureServices-time because the host's startup hook runs
            // MigrateAsync, which fails if EnsureCreated already created tables
            // outside the migration history.
            services.AddHostedService<TenancySeedHostedService>();
        });
    }

    private static void RemoveHostedService<T>(IServiceCollection services) where T : class
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var d = services[i];
            if (d.ServiceType == typeof(IHostedService) &&
                (d.ImplementationType == typeof(T) ||
                 d.ImplementationInstance?.GetType() == typeof(T)))
            {
                services.RemoveAt(i);
            }
        }
    }
}

/// <summary>
/// Lightweight extension to <see cref="IServiceCollection"/> mirroring the
/// behavior of <c>Microsoft.Extensions.DependencyInjection.ServiceCollectionDescriptorExtensions</c>
/// so we don't need to take a transitive dependency in tests.
/// </summary>
internal static class TenantServiceCollectionExtensions
{
    public static void RemoveAll<TService>(this IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(TService))
            {
                services.RemoveAt(i);
            }
        }
    }
}

/// <summary>
/// Hosted service that seeds Tenant A and Tenant B once the host's database
/// migration step has completed. Hosted services run after the entry point's
/// <c>MigrateDatabaseAsync</c> hook, so the schema is guaranteed to exist when
/// <see cref="StartAsync"/> fires.
/// </summary>
internal sealed class TenancySeedHostedService : IHostedService
{
    private readonly IServiceProvider _services;

    public TenancySeedHostedService(IServiceProvider services) => _services = services;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetService<AtoCopilotContext>();
        if (db is null) return;

        // Feature 048's tenancy DbSets (Tenants, CertificateRoleMappings,
        // CacSessions, JitRequestEntities) were added to AtoCopilotContext
        // but not yet captured in any EF migration. Production code creates
        // them via SQL Server-only `EnsureNewTablesAsync`. For SQLite-backed
        // integration tests we issue equivalent CREATE TABLE IF NOT EXISTS
        // statements so the seed below can find the Tenants table.
        await CreateTenancyTablesIfMissingAsync(db, cancellationToken);

        // The CspProfile seed below is only valid in MultiTenant mode —
        // SingleTenant deployments never have a `CspProfile` row (FR-093).
        // The CSP-onboarding mode-switch test asserts `count == 0` after a
        // SingleTenant boot, so we MUST honor the configured mode here.
        var deploymentOptions = scope.ServiceProvider
            .GetService<Microsoft.Extensions.Options.IOptions<Ato.Copilot.Mcp.Configuration.DeploymentOptions>>()?.Value;
        var isMultiTenant = deploymentOptions?.Mode
            == Ato.Copilot.Mcp.Configuration.DeploymentMode.MultiTenant;

        var tenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        if (!db.Tenants.Any(t => t.Id == tenantA))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantA,
                DisplayName = "Test Tenant A",
                Status = TenantStatus.Active,
                OnboardingState = OnboardingState.Active,
                CreatedBy = "test",
            });
        }

        if (!db.Tenants.Any(t => t.Id == tenantB))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantB,
                DisplayName = "Test Tenant B",
                Status = TenantStatus.Active,
                OnboardingState = OnboardingState.Active,
                CreatedBy = "test",
            });
        }

        // Pre-seed an Active CspProfile so the CSP-onboarding gate (FR-090,
        // US7) does NOT 503 every tenant-scoped request in the shared
        // fixture. CSP-onboarding test classes call ResetCspProfileAsync()
        // in their ctors to delete this row and exercise the gate.
        // Skipped in SingleTenant mode (FR-093).
        if (isMultiTenant && !db.Set<CspProfile>().IgnoreQueryFilters().Any())
        {
            db.Set<CspProfile>().Add(new CspProfile
            {
                Id = Guid.NewGuid(),
                LegalEntityName = "Test Hosting CSP",
                DisplayName = "Test CSP",
                OnboardingState = OnboardingState.Active,
                OnboardingCompletedAt = DateTimeOffset.UtcNow,
                IdentityCompletedAt = DateTimeOffset.UtcNow,
                SupportCompletedAt = DateTimeOffset.UtcNow,
                ClassificationCompletedAt = DateTimeOffset.UtcNow,
                CreatedBy = "test-fixture",
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Issues SQLite-compatible <c>CREATE TABLE IF NOT EXISTS</c> for the
    /// Feature 048 tenancy entities (Tenants, CspProfiles, etc.) so a fresh
    /// fixture host can host them without an EF migration. Exposed as
    /// <c>internal static</c> so sibling test factories
    /// (e.g. <c>ModeFactory</c>) can issue the DDL without inheriting the
    /// auto-seed behavior of <see cref="TenancySeedHostedService"/>.
    /// </summary>
    internal static Task CreateTenancyTablesIfMissingPublicAsync(
        AtoCopilotContext db,
        CancellationToken ct)
        => CreateTenancyTablesIfMissingAsync(db, ct);

    private static async Task CreateTenancyTablesIfMissingAsync(
        AtoCopilotContext db,
        CancellationToken ct)
    {
        // SQLite-compatible DDL — relies on `CREATE TABLE IF NOT EXISTS`
        // semantics. Columns mirror Ato.Copilot.Core.Models.Tenancy.Tenant
        // and surrounding entities. Keep in sync with the EF model.
        const string ddl = """
            CREATE TABLE IF NOT EXISTS "Tenants" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Tenants" PRIMARY KEY,
                "EntraTenantId" TEXT NULL,
                "DisplayName" TEXT NOT NULL,
                "LegalEntityName" TEXT NULL,
                "DoDComponent" TEXT NULL,
                "PrimaryPocName" TEXT NULL,
                "PrimaryPocEmail" TEXT NULL,
                "PrimaryPocPhone" TEXT NULL,
                "HqAddressLine1" TEXT NULL,
                "HqAddressLine2" TEXT NULL,
                "HqCity" TEXT NULL,
                "HqStateOrProvince" TEXT NULL,
                "HqPostalCode" TEXT NULL,
                "HqCountry" TEXT NULL,
                "DefaultClassificationLevel" INTEGER NOT NULL DEFAULT 0,
                "AuthorizingOfficialName" TEXT NULL,
                "AuthorizingOfficialEmail" TEXT NULL,
                "TimeZone" TEXT NOT NULL DEFAULT 'UTC',
                "Status" INTEGER NOT NULL DEFAULT 0,
                "OnboardingState" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "CreatedBy" TEXT NOT NULL DEFAULT 'system',
                "UpdatedAt" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "RowVersion" BLOB NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Tenants_EntraTenantId"
                ON "Tenants" ("EntraTenantId") WHERE "EntraTenantId" IS NOT NULL;
            CREATE INDEX IF NOT EXISTS "IX_Tenants_Status"
                ON "Tenants" ("Status");
            CREATE TABLE IF NOT EXISTS "CspProfiles" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_CspProfiles" PRIMARY KEY,
                "LegalEntityName" TEXT NOT NULL,
                "DisplayName" TEXT NOT NULL,
                "LogoUrl" TEXT NULL,
                "PrimarySupportEmail" TEXT NULL,
                "SupportPhone" TEXT NULL,
                "DefaultClassificationFloor" INTEGER NOT NULL DEFAULT 0,
                "OnboardingState" INTEGER NOT NULL DEFAULT 0,
                "OnboardingCompletedAt" TEXT NULL,
                "IdentityCompletedAt" TEXT NULL,
                "SupportCompletedAt" TEXT NULL,
                "ClassificationCompletedAt" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "CreatedBy" TEXT NOT NULL DEFAULT 'system',
                "UpdatedAt" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "RowVersion" BLOB NULL
            );
            CREATE TABLE IF NOT EXISTS "CspInheritedComponents" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_CspInheritedComponents" PRIMARY KEY,
                "CspProfileId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Description" TEXT NOT NULL DEFAULT '',
                "ComponentType" INTEGER NOT NULL DEFAULT 0,
                "SourceFileName" TEXT NULL,
                "SourceFormat" INTEGER NOT NULL DEFAULT 0,
                "SourceArtifactReference" TEXT NULL,
                "Status" INTEGER NOT NULL DEFAULT 0,
                "ImportedAt" TEXT NOT NULL,
                "ImportedBy" TEXT NOT NULL,
                "UpdatedAt" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "RowVersion" BLOB NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_CspInheritedComponents_CspProfileId_Status"
                ON "CspInheritedComponents" ("CspProfileId", "Status");
            CREATE TABLE IF NOT EXISTS "CspInheritedCapabilities" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_CspInheritedCapabilities" PRIMARY KEY,
                "CspInheritedComponentId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Description" TEXT NOT NULL DEFAULT '',
                "MappedNistControlIds" TEXT NOT NULL DEFAULT '[]',
                "MappingConfidence" REAL NULL,
                "Status" INTEGER NOT NULL DEFAULT 1,
                "MappingFailureReason" TEXT NULL,
                "MappedBy" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "CreatedBy" TEXT NOT NULL,
                "ReviewedAt" TEXT NULL,
                "ReviewedBy" TEXT NULL,
                "ReviewerNote" TEXT NULL,
                "RowVersion" BLOB NULL,
                CONSTRAINT "FK_CspInheritedCapabilities_CspInheritedComponents_CspInheritedComponentId"
                    FOREIGN KEY ("CspInheritedComponentId")
                    REFERENCES "CspInheritedComponents" ("Id")
                    ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS "IX_CspInheritedCapabilities_ComponentId_Status"
                ON "CspInheritedCapabilities" ("CspInheritedComponentId", "Status");
            CREATE TABLE IF NOT EXISTS "CapabilityHistoryEvents" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_CapabilityHistoryEvents" PRIMARY KEY,
                "CapabilityId" TEXT NOT NULL,
                "TenantId" TEXT NOT NULL,
                "EventType" TEXT NOT NULL,
                "ActorOid" TEXT NOT NULL,
                "OccurredAt" TEXT NOT NULL,
                "Summary" TEXT NOT NULL,
                "MetadataJson" TEXT NULL,
                CONSTRAINT "FK_CapabilityHistoryEvents_Tenants_TenantId"
                    FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id")
                    ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_CapabilityHistoryEvents_Tenant_Capability_Occurred"
                ON "CapabilityHistoryEvents" ("TenantId" ASC, "CapabilityId" ASC, "OccurredAt" DESC);
            """;
        await db.Database.ExecuteSqlRawAsync(ddl, ct);
    }
}

using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

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

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
                "CreatedAt" TEXT NOT NULL,
                "CreatedBy" TEXT NOT NULL DEFAULT 'system',
                "UpdatedAt" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "RowVersion" BLOB NULL
            );
            """;
        await db.Database.ExecuteSqlRawAsync(ddl, ct);
    }
}

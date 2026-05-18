using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Rls;

/// <summary>
/// T102 [US5]: shared SQL Server testcontainer fixture used by the
/// RLS integration tests (T103–T105). Spins up an
/// <c>mcr.microsoft.com/mssql/server:2022-latest</c> container, applies
/// the Feature 048 schema additions including
/// <see cref="RlsPolicyInstaller"/>, and seeds two tenants × N rows so
/// each test starts from a known cross-tenant baseline.
/// </summary>
/// <remarks>
/// All consumers should gate test entry with <see cref="Skip.IfNot(bool, string)"/>
/// against <see cref="DockerAvailable"/> so the suite remains friendly to
/// machines / CI runners that have no Docker daemon. Container startup is
/// expensive; the fixture is shared across the entire <c>RLS</c> xunit
/// collection (see <see cref="RlsCollection"/>).
/// </remarks>
public sealed class RlsIntegrationFixture : IAsyncLifetime
{
    public Guid TenantA { get; } = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public Guid TenantB { get; } = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public bool DockerAvailable { get; private set; }
    public string ConnectionString { get; private set; } = string.Empty;
    public string? SkipReason { get; private set; }

    private MsSqlContainer? _container;

    public async Task InitializeAsync()
    {
        // Allow CI / local dev to forcibly skip RLS tests without paying
        // the container startup cost (e.g., on PR validation runners).
        if (Environment.GetEnvironmentVariable("ATO_SKIP_DOCKER_TESTS") == "1")
        {
            DockerAvailable = false;
            SkipReason = "ATO_SKIP_DOCKER_TESTS=1";
            return;
        }

        try
        {
            _container = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();
            // Bound the start with a generous timeout; if Docker is down
            // Testcontainers throws quickly, but image pull on a fresh
            // machine can take a couple of minutes.
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await _container.StartAsync(cts.Token);
            ConnectionString = _container.GetConnectionString();

            // Apply the EF model + schema additions so RLS policies
            // are installed against the freshly-created database.
            var optsBuilder = new DbContextOptionsBuilder<AtoCopilotContext>()
                .UseSqlServer(ConnectionString);

            await using (var db = new AtoCopilotContext(optsBuilder.Options))
            {
                await db.Database.EnsureCreatedAsync();
                await RlsPolicyInstaller.ApplyAsync(db, NullLogger.Instance);
            }

            await SeedAsync();
            DockerAvailable = true;
        }
        catch (Exception ex)
        {
            // If Docker isn't running, image pull fails, license accept
            // hangs, etc., mark unavailable so dependent tests skip
            // cleanly rather than crashing the whole suite.
            DockerAvailable = false;
            SkipReason = $"Container start failed: {ex.GetType().Name}: {ex.Message}";
            if (_container is not null)
            {
                try { await _container.DisposeAsync(); } catch { /* swallow */ }
                _container = null;
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Open a new <see cref="SqlConnection"/> against the testcontainer.
    /// Tests are expected to set <c>SESSION_CONTEXT</c> themselves to
    /// simulate the runtime publisher (T107).
    /// </summary>
    public async Task<SqlConnection> OpenConnectionAsync()
    {
        var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task SeedAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        // Seed both tenants in a single batch — no SESSION_CONTEXT means
        // the predicate's third clause (`SESSION_CONTEXT IS NULL`) allows
        // the inserts. We use OrganizationContexts (a [TenantScoped]
        // entity that has its own TenantId column) rather than Tenants
        // (which IS the tenant root and has no TenantId column).
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM OrganizationContexts WHERE TenantId = @ta)
                INSERT INTO OrganizationContexts
                    (Id, TenantId, OrganizationName, Branch, ClassificationPosture, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES
                    (NEWID(), @ta, 'Tenant A Org', 6, NULL, SYSUTCDATETIME(), '00000000-0000-0000-0000-000000000000', SYSUTCDATETIME(), '00000000-0000-0000-0000-000000000000');
            IF NOT EXISTS (SELECT 1 FROM OrganizationContexts WHERE TenantId = @tb)
                INSERT INTO OrganizationContexts
                    (Id, TenantId, OrganizationName, Branch, ClassificationPosture, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
                VALUES
                    (NEWID(), @tb, 'Tenant B Org', 6, NULL, SYSUTCDATETIME(), '00000000-0000-0000-0000-000000000000', SYSUTCDATETIME(), '00000000-0000-0000-0000-000000000000');
            """;
        cmd.Parameters.AddWithValue("@ta", TenantA);
        cmd.Parameters.AddWithValue("@tb", TenantB);
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("RLS")]
public sealed class RlsCollection : ICollectionFixture<RlsIntegrationFixture>
{
}


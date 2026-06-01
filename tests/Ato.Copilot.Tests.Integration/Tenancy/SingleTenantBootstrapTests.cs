using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Interceptors;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T078 [US3]: Verifies the SingleTenant bootstrap path
/// (<see cref="TenantBootstrapService.EnsureDefaultTenantAndBackfillAsync"/>):
/// <list type="bullet">
///   <item>creates the default tenant when absent (FR-070 / acceptance scenario 1),</item>
///   <item>backfills every NULL <c>TenantId</c> on retrofitted tables to the
///         default tenant (FR-070, acceptance scenario 1),</item>
///   <item>emits the required <c>"Migrated {Count} rows to default tenant {DefaultTenantId}"</c>
///         log entry exactly once,</item>
///   <item>is idempotent — second call is a no-op and emits no migration line.</item>
/// </list>
/// </summary>
/// <remarks>
/// RED until T081–T083 are implemented. Uses a per-fixture in-memory SQLite
/// database with the production <see cref="AtoCopilotContext"/>.
/// </remarks>
public class SingleTenantBootstrapTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _sp = null!;
    private CapturingLoggerProvider _logProvider = null!;

    public SingleTenantBootstrapTests(ITestOutputHelper output)
    {
        _ = output;
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        _logProvider = new CapturingLoggerProvider();
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Debug);
            b.AddProvider(_logProvider);
        });
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<TenantStampingSaveChangesInterceptor>();
        services.AddDbContext<AtoCopilotContext>((sp, opt) =>
        {
            opt.UseSqlite(_connection);
            opt.AddInterceptors(sp.GetRequiredService<TenantStampingSaveChangesInterceptor>());
        });
        _sp = services.BuildServiceProvider();

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await db.Database.EnsureCreatedAsync();

        // Simulate the pre-retrofit upgrade scenario: drop the EF-created
        // Organizations table (which has TenantId NOT NULL per the model) and
        // recreate it with TenantId NULLABLE — this is the on-disk state of a
        // production database that existed before Feature 048's TenantId
        // column was added. The bootstrap's backfill is what migrates these
        // rows to have a non-NULL TenantId.
        await db.Database.ExecuteSqlRawAsync(
            """
            DROP TABLE IF EXISTS "Organizations";
            CREATE TABLE "Organizations" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Organizations" PRIMARY KEY,
                "TenantId" TEXT NULL,
                "Name" TEXT NOT NULL,
                "Description" TEXT NULL,
                "ParentOrganizationId" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "CreatedBy" TEXT NOT NULL DEFAULT 'system',
                "RowVersion" BLOB NULL
            );
            """);
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task EnsureDefaultTenant_CreatesRow_WhenMissing()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SingleTenantBootstrapTests>>();

        var report = await TenantBootstrapService.EnsureDefaultTenantAndBackfillAsync(
            db, isSingleTenantMode: true, defaultTenantIdOverride: null, logger);

        report.Created.Should().BeTrue("default tenant did not exist before the call");
        report.DefaultTenantId.Should().Be(TenantBootstrapService.DefaultTenantId);

        var stored = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == TenantBootstrapService.DefaultTenantId);
        stored.Should().NotBeNull();
        stored!.OnboardingState.Should().Be(OnboardingState.Active);
        stored.Status.Should().Be(TenantStatus.Active);
    }

    /// <summary>
    /// SingleTenant bootstrap must stamp <c>EntraTenantId = Id</c> on the
    /// new default tenant row so that <c>TenantResolutionMiddleware</c> can
    /// resolve it via the simulated CAC <c>tid</c> claim (and any other
    /// caller whose <c>tid</c> matches the well-known SingleTenant
    /// sentinel). Without this stamp the middleware lookup
    /// <c>Tenants WHERE EntraTenantId = tid</c> misses and the request 401s
    /// with <c>TENANT_NOT_PROVISIONED</c>.
    /// </summary>
    [Fact]
    public async Task EnsureDefaultTenant_StampsEntraTenantId_OnNewRow()
    {
        // Arrange
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SingleTenantBootstrapTests>>();

        // Act
        var report = await TenantBootstrapService.EnsureDefaultTenantAndBackfillAsync(
            db, isSingleTenantMode: true, defaultTenantIdOverride: null, logger);

        // Assert
        report.Created.Should().BeTrue();
        var stored = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == TenantBootstrapService.DefaultTenantId);
        stored.Should().NotBeNull();
        stored!.EntraTenantId.Should().Be(
            TenantBootstrapService.DefaultTenantId,
            "the SingleTenant sentinel row must be addressable by EntraTenantId so the resolution middleware finds it");
    }

    /// <summary>
    /// Idempotent backfill: if a deployment was bootstrapped before this
    /// stamping was introduced, the default tenant row exists but has
    /// <c>EntraTenantId = NULL</c>. The next startup MUST repair that row
    /// in place rather than leaving the request pipeline broken.
    /// </summary>
    [Fact]
    public async Task EnsureDefaultTenant_BackfillsEntraTenantId_OnExistingNullRow()
    {
        // Arrange — preseed an existing default-tenant row with EntraTenantId = NULL
        // (simulating a row created by the previous bootstrap implementation).
        await using (var seedScope = _sp.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            seedDb.Tenants.Add(new Tenant
            {
                Id = TenantBootstrapService.DefaultTenantId,
                EntraTenantId = null,
                DisplayName = "Default Organization",
                Status = TenantStatus.Active,
                OnboardingState = OnboardingState.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "system",
                TimeZone = "UTC",
                DefaultClassificationLevel = ClassificationLevel.Unclassified,
            });
            await seedDb.SaveChangesAsync();
        }

        // Act
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SingleTenantBootstrapTests>>();
        var report = await TenantBootstrapService.EnsureDefaultTenantAndBackfillAsync(
            db, isSingleTenantMode: true, defaultTenantIdOverride: null, logger);

        // Assert
        report.Created.Should().BeFalse("the row already existed");
        var stored = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == TenantBootstrapService.DefaultTenantId);
        stored.Should().NotBeNull();
        stored!.EntraTenantId.Should().Be(
            TenantBootstrapService.DefaultTenantId,
            "an existing default-tenant row with EntraTenantId = NULL must be backfilled idempotently on the next bootstrap");
    }

    [Fact]
    public async Task EnsureDefaultTenant_BackfillsNullTenantIdRows_AndEmitsMigrationLogLine()
    {
        // Arrange — insert an Organization with NULL TenantId by going around
        // the EF model (raw SQL) so the interceptor doesn't stamp it.
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Organizations" ("Id","TenantId","Name","CreatedAt","CreatedBy")
                VALUES (@p0, NULL, 'NullScoped', @p1, 'seed');
                """,
                Guid.NewGuid().ToString(),
                DateTimeOffset.UtcNow.ToString("o"));
        }

        // Act
        await using var scope2 = _sp.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var logger = scope2.ServiceProvider.GetRequiredService<ILogger<SingleTenantBootstrapTests>>();

        var report = await TenantBootstrapService.EnsureDefaultTenantAndBackfillAsync(
            db2, isSingleTenantMode: true, defaultTenantIdOverride: null, logger);

        // Assert — at least one row migrated, log line present.
        report.RowsBackfilled.Should().BeGreaterOrEqualTo(1);
        report.TablesTouched.Should().Contain("Organizations");

        var migrationLine = _logProvider.Entries
            .FirstOrDefault(e => e.Message.Contains("Migrated") &&
                                  e.Message.Contains("rows to default tenant"));
        migrationLine.Should().NotBeNull(
            "FR-070 requires a single 'Migrated {Count} rows to default tenant {DefaultTenantId}' log entry");

        // Verify no NULL TenantId rows remain on Organizations.
        var nullCount = (long)(await db2.Database.ExecuteSqlRawAsync(
            "SELECT COUNT(*) FROM \"Organizations\" WHERE \"TenantId\" IS NULL"));
        // ExecuteSqlRawAsync returns rows-affected for SELECT under SQLite in EF Core 9;
        // use a scalar query for robust assertion instead.
        var conn = db2.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM \"Organizations\" WHERE \"TenantId\" IS NULL";
        var nullsRemaining = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        nullsRemaining.Should().Be(0, "all NULL TenantId rows must be backfilled");
    }

    [Fact]
    public async Task EnsureDefaultTenant_IsIdempotent()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SingleTenantBootstrapTests>>();

        var first = await TenantBootstrapService.EnsureDefaultTenantAndBackfillAsync(
            db, isSingleTenantMode: true, defaultTenantIdOverride: null, logger);
        var second = await TenantBootstrapService.EnsureDefaultTenantAndBackfillAsync(
            db, isSingleTenantMode: true, defaultTenantIdOverride: null, logger);

        first.Created.Should().BeTrue();
        second.Created.Should().BeFalse("default tenant already existed on the second call");
        second.RowsBackfilled.Should().Be(0, "no NULL rows should remain after the first call");
    }

    [Fact]
    public async Task MultiTenantMode_DoesNotCreateDefaultTenant_AndFailsFastOnNullRows()
    {
        // Arrange — insert a NULL-TenantId row to force the FR-071 fail-fast path.
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Organizations" ("Id","TenantId","Name","CreatedAt","CreatedBy")
                VALUES (@p0, NULL, 'NullScoped-Multi', @p1, 'seed');
                """,
                Guid.NewGuid().ToString(),
                DateTimeOffset.UtcNow.ToString("o"));
        }

        await using var scope2 = _sp.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var logger = scope2.ServiceProvider.GetRequiredService<ILogger<SingleTenantBootstrapTests>>();

        var act = async () =>
            await TenantBootstrapService.EnsureDefaultTenantAndBackfillAsync(
                db2, isSingleTenantMode: false, defaultTenantIdOverride: null, logger);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("MultiTenant", StringComparison.OrdinalIgnoreCase),
                "FR-071 requires fail-fast in MultiTenant mode when NULL TenantIds remain");
    }

    /// <summary>
    /// Regression: a <c>[TenantScoped]</c> entity that deliberately scopes via a
    /// domain-specific column (Feature 051 <see cref="LoginAuditEvent.EffectiveTenantId"/>)
    /// and therefore has NO mapped conventional <c>TenantId</c> property must be
    /// excluded from the MultiTenant boot guard — exactly as the stamping
    /// interceptor, the query-filter installer, and
    /// <c>MultiTenantMigrationService.ResolveTenantScopedTables</c> already do.
    /// </summary>
    /// <remarks>
    /// Reproduces the production crash loop: a legacy/orphan nullable
    /// <c>TenantId</c> column existed on <c>LoginAuditEvents</c> with NULL values
    /// (pre-session / failed-login rows that EF never stamps because the model
    /// maps <c>EffectiveTenantId</c> instead). The boot guard discovered the
    /// table by its <c>[TenantScoped]</c> attribute alone and aborted MultiTenant
    /// boot on those NULLs, crash-looping the MCP container. The fix adds the
    /// missing <c>FindProperty("TenantId") is not null</c> clause to the
    /// discovery filter.
    /// </remarks>
    [Fact]
    public async Task MultiTenantMode_DoesNotFailFast_OnEffectiveTenantIdScopedEntity_WithOrphanNullTenantIdColumn()
    {
        // Arrange — recreate the production on-disk state: LoginAuditEvents
        // carries an orphan nullable TenantId column (EF does NOT map one) and
        // holds a row written without an ambient tenant context, so TenantId is
        // NULL while EffectiveTenantId is correctly populated.
        var tenantId = Guid.NewGuid();
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

            // FK target for EffectiveTenantId.
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                EntraTenantId = tenantId,
                DisplayName = "Audit Tenant",
                Status = TenantStatus.Active,
                OnboardingState = OnboardingState.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "system",
                TimeZone = "UTC",
                DefaultClassificationLevel = ClassificationLevel.Unclassified,
            });
            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"LoginAuditEvents\" ADD COLUMN \"TenantId\" TEXT NULL;");

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "LoginAuditEvents"
                    ("Id","EventType","EffectiveTenantId","CorrelationId","SourceIp","UserAgent","Surface","OccurredAt","TenantId")
                VALUES (@p0, 0, @p1, 'corr', '127.0.0.1', 'agent', 0, @p2, NULL);
                """,
                Guid.NewGuid().ToString().ToUpperInvariant(),
                tenantId.ToString().ToUpperInvariant(),
                DateTimeOffset.UtcNow.ToString("o"));
        }

        // Act
        await using var scope2 = _sp.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var logger = scope2.ServiceProvider.GetRequiredService<ILogger<SingleTenantBootstrapTests>>();

        var act = async () =>
            await TenantBootstrapService.EnsureDefaultTenantAndBackfillAsync(
                db2, isSingleTenantMode: false, defaultTenantIdOverride: null, logger);

        // Assert — the EffectiveTenantId-scoped entity must NOT trip the
        // conventional-TenantId boot guard, so MultiTenant boot proceeds.
        await act.Should().NotThrowAsync(
            "LoginAuditEvents scopes via EffectiveTenantId and has no mapped TenantId, " +
            "so its orphan NULL TenantId column must not abort MultiTenant boot");
    }

    /// <summary>Lightweight in-memory <see cref="ILoggerProvider"/> capturing log events for assertion.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly CapturingLoggerProvider _parent;
            public CapturingLogger(CapturingLoggerProvider parent) => _parent = parent;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
                Func<TState, Exception?, string> formatter)
            {
                lock (_parent.Entries)
                {
                    _parent.Entries.Add((level, formatter(state, ex)));
                }
            }
        }
    }
}

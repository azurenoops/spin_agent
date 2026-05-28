using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Auth;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T085 [US10] — contract test for
/// <see cref="ILoginAuditService.ListAsync"/> and
/// <see cref="ILoginAuditService.ListSystemTenantAsync"/>. Verifies the
/// SOC-analyst read path defined in
/// <c>contracts/internal-services.md § 1.3</c>:
/// <list type="bullet">
///   <item><see cref="ILoginAuditService.ListAsync"/> returns tenant
///         rows in descending <see cref="LoginAuditEvent.OccurredAt"/>
///         order, capped at <c>take</c>.</item>
///   <item>Subject to the automatic tenant query filter installed on
///         <see cref="LoginAuditEvent"/> (data-model § 1.9 / § 4) —
///         querying as Tenant B sees zero of Tenant A's rows.</item>
///   <item><see cref="ILoginAuditService.ListSystemTenantAsync"/>
///         throws <see cref="UnauthorizedAccessException"/> without the
///         <c>Auth.SocAnalyst</c> role claim.</item>
///   <item>With the claim, returns only rows where
///         <c>EffectiveTenantId == Guid.Empty</c> (SYSTEM_TENANT_ID).</item>
/// </list>
/// </summary>
public sealed class LoginAuditServiceListTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AtoCopilotContext> _baseOptions;
    private readonly TenantContextAccessor _tenantAccessor;
    private readonly StubDbContextFactory _factory;

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public LoginAuditServiceListTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _baseOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(_connection)
            .Options;

        _tenantAccessor = new TenantContextAccessor();
        _factory = new StubDbContextFactory(_baseOptions, _tenantAccessor);

        using var ctx = new AtoCopilotContext(_baseOptions);
        ctx.Database.EnsureCreated();

        // Seed the two tenants the audit rows reference (FK target). The
        // SYSTEM tenant (Guid.Empty) is also seeded so audit rows with
        // EffectiveTenantId == SYSTEM_TENANT_ID can satisfy the cascade
        // FK to Tenants — mirrors Feature 048 FR-070 (Bootstrap-phase
        // SYSTEM_TENANT_ID is a real Tenant row, not a sentinel).
        ctx.Tenants.Add(new Tenant
        {
            Id = Guid.Empty,
            DisplayName = "System Tenant",
            CreatedBy = "test",
        });
        ctx.Tenants.Add(new Tenant
        {
            Id = TenantA,
            DisplayName = "Tenant A",
            CreatedBy = "test",
        });
        ctx.Tenants.Add(new Tenant
        {
            Id = TenantB,
            DisplayName = "Tenant B",
            CreatedBy = "test",
        });
        ctx.SaveChanges();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    // ─── ListAsync — returns tenant rows, newest first, capped by take ──

    [Fact]
    public async Task ListAsync_ReturnsRowsForTenant_NewestFirst_CappedByTake()
    {
        // Arrange — seed 5 rows for tenant A spaced across time.
        await SeedRowsAsync(TenantA, count: 5, baseTime: DateTimeOffset.UtcNow.AddMinutes(-10));
        var sut = NewSut();

        // Push tenant A so the filter does not exclude its rows.
        using var _ = _tenantAccessor.Push(new TenantContext(TenantA));

        // Act
        var rows = await sut.ListAsync(TenantA, since: null, take: 3);

        // Assert
        rows.Should().HaveCount(3,
            "take=3 caps the response to the three newest rows.");
        rows.Should().BeInDescendingOrder(r => r.OccurredAt,
            "contracts/internal-services.md § 1.3 — OrderByDescending(OccurredAt).");
        rows.Should().OnlyContain(r => r.EffectiveTenantId == TenantA);
    }

    [Fact]
    public async Task ListAsync_FiltersBySince()
    {
        // Arrange — seed 4 rows at known offsets so we can pin the
        // since boundary deterministically.
        var anchor = DateTimeOffset.UtcNow.AddMinutes(-60);
        await SeedRowsAsync(TenantA, count: 4, baseTime: anchor, spacingMinutes: 10);
        var sut = NewSut();

        // since = anchor + 25 min → only the two rows newer than that
        // should come back (rows are at anchor+0, +10, +20, +30 minutes).
        var since = anchor.AddMinutes(25);

        using var _ = _tenantAccessor.Push(new TenantContext(TenantA));

        // Act
        var rows = await sut.ListAsync(TenantA, since: since, take: 100);

        // Assert
        rows.Should().HaveCount(1,
            "rows older than `since` MUST be excluded; only the +30 row qualifies.");
        rows.Single().OccurredAt.Should().BeOnOrAfter(since);
    }

    [Fact]
    public async Task ListAsync_FromOtherTenantContext_ReturnsZeroRows()
    {
        // Arrange — seed rows for tenant A.
        await SeedRowsAsync(TenantA, count: 3, baseTime: DateTimeOffset.UtcNow.AddMinutes(-5));
        var sut = NewSut();

        // Push tenant B → the auto-applied query filter on LoginAuditEvent
        // (AtoCopilotContext.OnModelCreating, EffectiveTenantId match) hides
        // tenant A's rows. The caller-supplied tenantId is irrelevant for
        // security — passing TenantA here MUST still return zero rows.
        using var _ = _tenantAccessor.Push(new TenantContext(TenantB));

        // Act
        var rows = await sut.ListAsync(TenantA, since: null, take: 100);

        // Assert
        rows.Should().BeEmpty(
            "the [TenantScoped] HasQueryFilter scopes reads to the active tenant; cross-tenant queries return empty.");
    }

    // ─── ListSystemTenantAsync — SOC-analyst claim gate ────────────────

    [Fact]
    public async Task ListSystemTenantAsync_WithoutSocAnalystClaim_Throws()
    {
        // Arrange — seed one SYSTEM_TENANT_ID row.
        await SeedRowsAsync(Guid.Empty, count: 1, baseTime: DateTimeOffset.UtcNow);
        var sut = NewSut(socAnalyst: false);

        // Act
        Func<Task> act = () => sut.ListSystemTenantAsync();

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .Where(ex => ex.Message.Contains("Auth.SocAnalyst", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListSystemTenantAsync_WithSocAnalystClaim_ReturnsOnlySystemTenantRows()
    {
        // Arrange — three SYSTEM_TENANT_ID rows and three TenantA rows.
        var anchor = DateTimeOffset.UtcNow.AddMinutes(-20);
        await SeedRowsAsync(Guid.Empty, count: 3, baseTime: anchor, spacingMinutes: 1);
        await SeedRowsAsync(TenantA, count: 3, baseTime: anchor.AddMinutes(10), spacingMinutes: 1);
        var sut = NewSut(socAnalyst: true);

        // Act
        var rows = await sut.ListSystemTenantAsync(since: null, take: 100);

        // Assert
        rows.Should().HaveCount(3,
            "only the three SYSTEM_TENANT_ID rows must surface.");
        rows.Should().OnlyContain(r => r.EffectiveTenantId == Guid.Empty,
            "ListSystemTenantAsync MUST NOT return any tenant-owned rows even when query filters are ignored.");
        rows.Should().BeInDescendingOrder(r => r.OccurredAt);
    }

    [Fact]
    public async Task ListSystemTenantAsync_HonorsSinceAndTake()
    {
        // Arrange — 5 SYSTEM_TENANT_ID rows spaced 10 minutes apart.
        var anchor = DateTimeOffset.UtcNow.AddMinutes(-60);
        await SeedRowsAsync(Guid.Empty, count: 5, baseTime: anchor, spacingMinutes: 10);
        var sut = NewSut(socAnalyst: true);

        // since = anchor+25 → only rows at +30 and +40 qualify (2 rows).
        // take = 1 caps it to one.
        var since = anchor.AddMinutes(25);

        // Act
        var rows = await sut.ListSystemTenantAsync(since: since, take: 1);

        // Assert
        rows.Should().HaveCount(1, "take=1 caps the response.");
        rows.Single().OccurredAt.Should().BeOnOrAfter(since);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private LoginAuditService NewSut(bool socAnalyst = false)
    {
        var http = BuildHttpContextAccessor(socAnalyst);
        return new LoginAuditService(
            NullLogger<LoginAuditService>.Instance,
            _factory,
            http);
    }

    private static IHttpContextAccessor BuildHttpContextAccessor(bool socAnalyst)
    {
        var ctx = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new("oid", Guid.NewGuid().ToString()),
        };
        if (socAnalyst)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Auth.SocAnalyst"));
        }
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var mock = new Mock<IHttpContextAccessor>();
        mock.SetupGet(a => a.HttpContext).Returns(ctx);
        return mock.Object;
    }

    private async Task SeedRowsAsync(
        Guid tenantId,
        int count,
        DateTimeOffset baseTime,
        int spacingMinutes = 1)
    {
        await using var db = new AtoCopilotContext(_baseOptions);
        for (int i = 0; i < count; i++)
        {
            db.LoginAuditEvents.Add(new LoginAuditEvent
            {
                Id = Guid.NewGuid(),
                EventType = LoginAuditEventType.LoginSuccess,
                Oid = $"oid-{i}",
                Tid = null,
                EffectiveTenantId = tenantId,
                CorrelationId = $"corr-{i}",
                SourceIp = "10.0.0.1",
                UserAgent = "Mozilla/5.0",
                Surface = LoginSurface.Dashboard,
                OccurredAt = baseTime.AddMinutes(i * spacingMinutes),
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Minimal <see cref="IDbContextFactory{TContext}"/> backed by the
    /// fixture's shared SQLite connection. Each created context is
    /// constructed with the live <see cref="TenantContextAccessor"/> so
    /// the inline HasQueryFilter on LoginAuditEvent picks up the ambient
    /// tenant via the <c>TenantFilterEffectiveId</c> accessor.
    /// </summary>
    private sealed class StubDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        private readonly ITenantContextAccessor _tenantAccessor;

        public StubDbContextFactory(
            DbContextOptions<AtoCopilotContext> options,
            ITenantContextAccessor tenantAccessor)
        {
            _options = options;
            _tenantAccessor = tenantAccessor;
        }

        public AtoCopilotContext CreateDbContext()
            => new(_options, _tenantAccessor);
    }
}

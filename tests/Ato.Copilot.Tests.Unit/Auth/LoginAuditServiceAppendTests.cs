using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Auth;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T026 — contract test for
/// <see cref="ILoginAuditService.AppendAsync"/>. Verifies that the service
/// pins to the SRP boundary defined in
/// <c>contracts/internal-services.md § 1.3</c>:
/// <list type="bullet">
///   <item>Populates <see cref="LoginAuditEvent.Id"/> and
///         <see cref="LoginAuditEvent.OccurredAt"/> server-side.</item>
///   <item>Attaches the entity to the caller-provided
///         <see cref="AtoCopilotContext"/> in <see cref="EntityState.Added"/>
///         state but does NOT call <c>SaveChangesAsync</c> — the caller
///         owns the transaction (R6 / Feature-050 SRP parity, mirroring
///         <c>CapabilityHistoryService.AppendAsync</c>).</item>
///   <item>A caller that subsequently calls <c>SaveChangesAsync</c>
///         persists the row.</item>
///   <item>Accepts <c>SYSTEM_TENANT_ID</c> (<see cref="Guid.Empty"/>) for
///         <see cref="LoginAuditEvent.EffectiveTenantId"/> per
///         clarification Q2.</item>
///   <item>Rejects <see cref="LoginAuditEventDraft.Oid"/> &gt; 254 chars.</item>
///   <item>The interface public surface is exactly
///         <c>{ AppendAsync, ListAsync, ListSystemTenantAsync }</c>.</item>
/// </list>
/// </summary>
public sealed class LoginAuditServiceAppendTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AtoCopilotContext> _options;

    public LoginAuditServiceAppendTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = new AtoCopilotContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    // Interface-surface invariant moved to LoginAuditServiceSurfaceTests.cs
    // in Phase 7 (T087) so the surface guard is discoverable independently
    // of the write-path coverage. See contracts/internal-services.md § 1.4.

    // ─── AppendAsync — populates Id and OccurredAt server-side ──────────

    [Fact]
    public async Task AppendAsync_PopulatesIdAndOccurredAt()
    {
        // Arrange
        var sut = NewSut();
        await using var db = NewContext();
        var draft = NewDraft();
        var before = DateTimeOffset.UtcNow;

        // Act
        var evt = await sut.AppendAsync(db, draft, CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        // Assert
        evt.Should().NotBeNull();
        evt.Id.Should().NotBe(Guid.Empty, "AppendAsync must mint a fresh Guid for each row.");
        evt.OccurredAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ─── AppendAsync — adds to caller's context but does NOT save ───────

    [Fact]
    public async Task AppendAsync_TracksEntity_ButDoesNotCallSaveChanges()
    {
        // Arrange
        var sut = NewSut();
        await using var db = NewContext();
        var draft = NewDraft();

        // Act
        var evt = await sut.AppendAsync(db, draft, CancellationToken.None);

        // Assert — the entity IS tracked on the caller's context …
        db.Entry(evt).State.Should().Be(EntityState.Added,
            "AppendAsync attaches the entity to the caller's context for the caller to commit.");

        // … but a fresh context sees zero rows because the service did
        // not call SaveChangesAsync on the caller's behalf.
        await using var verify = NewContext();
        var count = await verify.LoginAuditEvents.IgnoreQueryFilters().CountAsync();
        count.Should().Be(0,
            "AppendAsync MUST NOT call SaveChangesAsync per contracts/internal-services.md § 1.3.");
    }

    [Fact]
    public async Task AppendAsync_FollowedBySaveChanges_PersistsRow()
    {
        // Arrange — seed the FK target so SaveChangesAsync does not trip
        // the Tenants FK constraint (SQLite enforces FKs by default in
        // recent EF Core versions).
        var tenantId = await SeedTenantAsync();
        var sut = NewSut();
        await using var db = NewContext();
        var draft = NewDraft() with { EffectiveTenantId = tenantId };

        // Act — the production flow: caller appends + commits atomically.
        var evt = await sut.AppendAsync(db, draft, CancellationToken.None);
        await db.SaveChangesAsync();

        // Assert — row reaches the database when the caller commits.
        await using var verify = NewContext();
        var persisted = await verify.LoginAuditEvents.IgnoreQueryFilters().SingleAsync();
        persisted.Id.Should().Be(evt.Id);
        persisted.EventType.Should().Be(draft.EventType);
        persisted.EffectiveTenantId.Should().Be(tenantId);
    }

    // ─── AppendAsync — accepts SYSTEM_TENANT_ID (Q2) ────────────────────

    [Fact]
    public async Task AppendAsync_AcceptsSystemTenantId()
    {
        // Arrange
        var sut = NewSut();
        await using var db = NewContext();
        var draft = NewDraft() with { EffectiveTenantId = Guid.Empty };

        // Act
        var evt = await sut.AppendAsync(db, draft, CancellationToken.None);

        // Assert
        evt.EffectiveTenantId.Should().Be(Guid.Empty,
            "Q2 — pre-session and NoTenantAssignment rows use SYSTEM_TENANT_ID.");
    }

    // ─── AppendAsync — rejects Oid > 254 chars ──────────────────────────

    [Fact]
    public async Task AppendAsync_OidTooLong_Throws()
    {
        // Arrange
        var sut = NewSut();
        await using var db = NewContext();
        var tooLong = new string('a', 255);
        var draft = NewDraft() with { Oid = tooLong };

        // Act
        Func<Task> act = () => sut.AppendAsync(db, draft, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.Message.Contains("Oid"));
    }

    [Fact]
    public async Task AppendAsync_OidAt254Chars_Succeeds()
    {
        // Arrange — boundary case: 254 chars is the documented cap.
        var sut = NewSut();
        await using var db = NewContext();
        var atCap = new string('a', 254);
        var draft = NewDraft() with { Oid = atCap };

        // Act
        var evt = await sut.AppendAsync(db, draft, CancellationToken.None);

        // Assert
        evt.Oid.Should().Be(atCap);
    }

    [Fact]
    public async Task AppendAsync_NullOid_Succeeds()
    {
        // Arrange — pre-Entra events (e.g. CertExpired) carry no Oid.
        var sut = NewSut();
        await using var db = NewContext();
        var draft = NewDraft() with { Oid = null };

        // Act
        var evt = await sut.AppendAsync(db, draft, CancellationToken.None);

        // Assert
        evt.Oid.Should().BeNull();
    }

    // ─── AppendAsync — null arg throws ──────────────────────────────────

    [Fact]
    public async Task AppendAsync_NullDb_Throws()
    {
        // Arrange
        var sut = NewSut();

        // Act
        Func<Task> act = () => sut.AppendAsync(null!, NewDraft(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AppendAsync_NullDraft_Throws()
    {
        // Arrange
        var sut = NewSut();
        await using var db = NewContext();

        // Act
        Func<Task> act = () => sut.AppendAsync(db, null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ─── List methods — when the legacy AppendAsync-only ctor is used,
    // both read paths surface a clear InvalidOperationException so the
    // append-only test doubles fail loudly if a future test
    // accidentally invokes the read path under the legacy ctor (which
    // intentionally has no IDbContextFactory or IHttpContextAccessor).
    // The behavioural contract for the read paths is exercised in
    // LoginAuditServiceListTests via the production constructor.

    [Fact]
    public async Task ListAsync_LegacyCtor_Throws_InvalidOperation()
    {
        // Arrange
        var sut = NewSut();

        // Act
        Func<Task> act = () => sut.ListAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ListSystemTenantAsync_LegacyCtor_Throws_InvalidOperation()
    {
        // Arrange
        var sut = NewSut();

        // Act
        Func<Task> act = () => sut.ListSystemTenantAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private LoginAuditService NewSut()
        => new(NullLogger<LoginAuditService>.Instance);

    private AtoCopilotContext NewContext()
        => new(_options);

    private async Task<Guid> SeedTenantAsync()
    {
        await using var db = NewContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Tenant",
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static LoginAuditEventDraft NewDraft() => new(
        EventType: LoginAuditEventType.LoginSuccess,
        Oid: "00000000-0000-0000-0000-000000000abc",
        Tid: "00000000-0000-0000-0000-000000000def",
        EffectiveTenantId: Guid.NewGuid(),
        CorrelationId: "corr-1234",
        SourceIp: "10.0.0.1",
        UserAgent: "Mozilla/5.0",
        Surface: LoginSurface.Dashboard);
}

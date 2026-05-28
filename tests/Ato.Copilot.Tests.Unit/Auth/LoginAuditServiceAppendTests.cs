using System.Reflection;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;
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
///   <item>Does NOT call <c>SaveChangesAsync</c> — the caller owns the
///         transaction (R6 / Feature-050 SRP parity).</item>
///   <item>Accepts <c>SYSTEM_TENANT_ID</c> (<see cref="Guid.Empty"/>) for
///         <see cref="LoginAuditEvent.EffectiveTenantId"/> per
///         clarification Q2.</item>
///   <item>Rejects <see cref="LoginAuditEventDraft.Oid"/> &gt; 254 chars.</item>
///   <item>The interface public surface is exactly
///         <c>{ AppendAsync, ListAsync, ListSystemTenantAsync }</c>.</item>
/// </list>
/// </summary>
/// <remarks>
/// Uses a SQLite <c>:memory:</c> connection-backed factory so the
/// LoginAuditEvents schema is materialised for the post-append zero-count
/// verification query (matches the
/// <c>LoginAuditEventModelBuilderTests</c> pattern).
/// </remarks>
public sealed class LoginAuditServiceAppendTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDbContextFactory _factory;

    public LoginAuditServiceAppendTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(_connection)
            .Options;
        _factory = new TestDbContextFactory(options);

        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    // ─── Interface surface invariant ────────────────────────────────────

    [Fact]
    public void Interface_HasExactlyThreePublicMethods()
    {
        // Arrange
        var methods = typeof(ILoginAuditService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToHashSet();

        // Act + Assert
        methods.Should().BeEquivalentTo(
            new[] { "AppendAsync", "ListAsync", "ListSystemTenantAsync" },
            "contracts/internal-services.md § 1.4 — no Update or Delete on the surface.");
    }

    // ─── AppendAsync — populates Id and OccurredAt server-side ──────────

    [Fact]
    public async Task AppendAsync_PopulatesIdAndOccurredAt()
    {
        // Arrange
        var sut = NewSut();
        var draft = NewDraft();
        var before = DateTimeOffset.UtcNow;

        // Act
        var evt = await sut.AppendAsync(draft, CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        // Assert
        evt.Should().NotBeNull();
        evt.Id.Should().NotBe(Guid.Empty, "AppendAsync must mint a fresh Guid for each row.");
        evt.OccurredAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ─── AppendAsync — does NOT call SaveChangesAsync ───────────────────

    [Fact]
    public async Task AppendAsync_DoesNotCallSaveChanges_RowIsNotPersisted()
    {
        // Arrange
        var sut = NewSut();
        var draft = NewDraft();

        // Act
        await sut.AppendAsync(draft, CancellationToken.None);

        // Assert — the service-owned context disposed without SaveChanges,
        // so a fresh context (same SQLite connection / shared schema) sees
        // zero rows. R6 SRP — caller owns the transaction.
        await using var verify = _factory.CreateDbContext();
        var count = await verify.LoginAuditEvents.IgnoreQueryFilters().CountAsync();
        count.Should().Be(0,
            "AppendAsync MUST NOT call SaveChangesAsync per contracts/internal-services.md § 1.3.");
    }

    // ─── AppendAsync — accepts SYSTEM_TENANT_ID (Q2) ────────────────────

    [Fact]
    public async Task AppendAsync_AcceptsSystemTenantId()
    {
        // Arrange
        var sut = NewSut();
        var draft = NewDraft() with { EffectiveTenantId = Guid.Empty };

        // Act
        var evt = await sut.AppendAsync(draft, CancellationToken.None);

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
        var tooLong = new string('a', 255);
        var draft = NewDraft() with { Oid = tooLong };

        // Act
        Func<Task> act = () => sut.AppendAsync(draft, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.Message.Contains("Oid"));
    }

    [Fact]
    public async Task AppendAsync_OidAt254Chars_Succeeds()
    {
        // Arrange — boundary case: 254 chars is the documented cap.
        var sut = NewSut();
        var atCap = new string('a', 254);
        var draft = NewDraft() with { Oid = atCap };

        // Act
        var evt = await sut.AppendAsync(draft, CancellationToken.None);

        // Assert
        evt.Oid.Should().Be(atCap);
    }

    [Fact]
    public async Task AppendAsync_NullOid_Succeeds()
    {
        // Arrange — pre-Entra events (e.g. CertExpired) carry no Oid.
        var sut = NewSut();
        var draft = NewDraft() with { Oid = null };

        // Act
        var evt = await sut.AppendAsync(draft, CancellationToken.None);

        // Assert
        evt.Oid.Should().BeNull();
    }

    // ─── AppendAsync — null draft throws ────────────────────────────────

    [Fact]
    public async Task AppendAsync_NullDraft_Throws()
    {
        // Arrange
        var sut = NewSut();

        // Act
        Func<Task> act = () => sut.AppendAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ─── List methods are stubbed pending T085 / T086 ───────────────────

    [Fact]
    public async Task ListAsync_StubbedUntilT085()
    {
        // Arrange
        var sut = NewSut();

        // Act
        Func<Task> act = () => sut.ListAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task ListSystemTenantAsync_StubbedUntilT086()
    {
        // Arrange
        var sut = NewSut();

        // Act
        Func<Task> act = () => sut.ListSystemTenantAsync();

        // Assert
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private LoginAuditService NewSut()
        => new(_factory, NullLogger<LoginAuditService>.Instance);

    private static LoginAuditEventDraft NewDraft() => new(
        EventType: LoginAuditEventType.LoginSuccess,
        Oid: "00000000-0000-0000-0000-000000000abc",
        Tid: "00000000-0000-0000-0000-000000000def",
        EffectiveTenantId: Guid.NewGuid(),
        CorrelationId: "corr-1234",
        SourceIp: "10.0.0.1",
        UserAgent: "Mozilla/5.0",
        Surface: LoginSurface.Dashboard);

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;

        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;

        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}

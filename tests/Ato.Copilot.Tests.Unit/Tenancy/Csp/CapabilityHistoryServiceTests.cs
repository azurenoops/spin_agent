using System.Reflection;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy.Csp;

/// <summary>
/// T005 [Feature 050 Foundational]: unit tests for
/// <see cref="ICapabilityHistoryService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Asserts the contract pinned in
/// <c>specs/050-csp-capability-lifecycle/contracts/internal-services.md § 1</c>:
/// </para>
/// <list type="bullet">
///   <item><c>AppendAsync</c> does NOT call <c>SaveChangesAsync</c>
///         (atomic-with-caller invariant, R1).</item>
///   <item>The interface surface is exactly
///         <c>{ AppendAsync, ListAsync }</c> — no <c>Update</c> / <c>Delete</c>
///         (immutability invariant, FR-004).</item>
///   <item>Validation on <c>ActorOid</c> empty and <c>Summary</c> &gt; 500.</item>
///   <item><c>metadata = null</c> produces <c>MetadataJson = null</c>
///         (not the string <c>"null"</c>).</item>
///   <item><c>ListAsync</c> filters by <c>TenantId</c> then
///         <c>CapabilityId</c>, orders by <c>OccurredAt DESC, Id DESC</c>,
///         clamps <c>pageSize</c> to <c>[1, 200]</c>, returns empty page
///         (NOT exception) on no-match.</item>
/// </list>
/// </remarks>
public sealed class CapabilityHistoryServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;

    public CapabilityHistoryServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CapHistory_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    // ─── Interface surface invariant ────────────────────────────────────

    [Fact]
    public void Interface_HasExactlyTwoPublicMethods_AppendAsync_And_ListAsync()
    {
        // Arrange
        var methods = typeof(ICapabilityHistoryService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToHashSet();

        // Act + Assert
        methods.Should().BeEquivalentTo(new[] { "AppendAsync", "ListAsync" },
            "Feature 050 FR-004 keeps history rows immutable — no Update or Delete on the surface.");
    }

    // ─── AppendAsync — does not SaveChanges, just AddAsync ──────────────

    [Fact]
    public async Task AppendAsync_AddsRowButDoesNotSaveChanges()
    {
        // Arrange
        var sut = NewSut();
        await using var db = _factory.CreateDbContext();
        var tenantId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();

        // Act
        var evt = await sut.AppendAsync(
            db, capabilityId, tenantId,
            CapabilityHistoryEventType.Created,
            actorOid: "actor@example.com",
            summary: "Created.",
            metadata: null,
            ct: CancellationToken.None);

        // Assert — row tracked but not persisted (no SaveChanges).
        db.Entry(evt).State.Should().Be(EntityState.Added);
        var persistedCount = await _factory.CreateDbContext()
            .CapabilityHistoryEvents.CountAsync();
        persistedCount.Should().Be(0,
            "AppendAsync must NOT call SaveChangesAsync — the caller owns the transaction.");
    }

    [Fact]
    public async Task AppendAsync_NullMetadata_ProducesNullJsonColumn()
    {
        // Arrange
        var sut = NewSut();
        await using var db = _factory.CreateDbContext();

        // Act
        var evt = await sut.AppendAsync(
            db, Guid.NewGuid(), Guid.NewGuid(),
            CapabilityHistoryEventType.Archived,
            actorOid: "actor@example.com",
            summary: "Archived.",
            metadata: null);

        // Assert — must be the SQL NULL, never the literal string "null".
        evt.MetadataJson.Should().BeNull();
    }

    [Fact]
    public async Task AppendAsync_ObjectMetadata_SerializesToCamelCaseJson()
    {
        // Arrange
        var sut = NewSut();
        await using var db = _factory.CreateDbContext();
        var fromComponentId = Guid.NewGuid();
        var toComponentId = Guid.NewGuid();

        // Act
        var evt = await sut.AppendAsync(
            db, Guid.NewGuid(), Guid.NewGuid(),
            CapabilityHistoryEventType.Moved,
            actorOid: "actor@example.com",
            summary: "Moved.",
            metadata: new { fromComponentId, toComponentId });

        // Assert
        evt.MetadataJson.Should().NotBeNull();
        var doc = JsonDocument.Parse(evt.MetadataJson!);
        doc.RootElement.GetProperty("fromComponentId").GetGuid().Should().Be(fromComponentId);
        doc.RootElement.GetProperty("toComponentId").GetGuid().Should().Be(toComponentId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AppendAsync_EmptyActorOid_Throws(string actor)
    {
        // Arrange
        var sut = NewSut();
        await using var db = _factory.CreateDbContext();

        // Act
        Func<Task> act = () => sut.AppendAsync(
            db, Guid.NewGuid(), Guid.NewGuid(),
            CapabilityHistoryEventType.Created,
            actorOid: actor,
            summary: "Summary.");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AppendAsync_SummaryOver500Chars_Throws()
    {
        // Arrange
        var sut = NewSut();
        await using var db = _factory.CreateDbContext();
        var oversize = new string('x', 501);

        // Act
        Func<Task> act = () => sut.AppendAsync(
            db, Guid.NewGuid(), Guid.NewGuid(),
            CapabilityHistoryEventType.Created,
            actorOid: "actor@example.com",
            summary: oversize);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── ListAsync — tenant isolation + ordering + paging ───────────────

    [Fact]
    public async Task ListAsync_FiltersByTenantThenCapability_AndOrdersOccurredAtDesc()
    {
        // Arrange
        var sut = NewSut();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var capability = Guid.NewGuid();
        var otherCapability = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var db = _factory.CreateDbContext())
        {
            // Two rows for (tenantA, capability) at different times.
            db.CapabilityHistoryEvents.Add(NewEvent(tenantA, capability, now.AddMinutes(-2), CapabilityHistoryEventType.Created));
            db.CapabilityHistoryEvents.Add(NewEvent(tenantA, capability, now, CapabilityHistoryEventType.Reviewed));
            // Cross-tenant noise — must NOT appear.
            db.CapabilityHistoryEvents.Add(NewEvent(tenantB, capability, now, CapabilityHistoryEventType.Moved));
            // Different capability noise — must NOT appear.
            db.CapabilityHistoryEvents.Add(NewEvent(tenantA, otherCapability, now, CapabilityHistoryEventType.Moved));
            await db.SaveChangesAsync();
        }

        // Act
        var page = await sut.ListAsync(capability, tenantA, page: 1, pageSize: 50);

        // Assert
        page.Total.Should().Be(2);
        page.Items.Should().HaveCount(2);
        page.Items[0].EventType.Should().Be(CapabilityHistoryEventType.Reviewed);
        page.Items[1].EventType.Should().Be(CapabilityHistoryEventType.Created);
    }

    [Fact]
    public async Task ListAsync_EmptyResult_ReturnsEmptyPage_NotException()
    {
        // Arrange
        var sut = NewSut();

        // Act
        var page = await sut.ListAsync(Guid.NewGuid(), Guid.NewGuid(), page: 1, pageSize: 50);

        // Assert
        page.Items.Should().BeEmpty();
        page.Total.Should().Be(0);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(50);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(500, 200)]
    [InlineData(50, 50)]
    public async Task ListAsync_ClampsPageSize_To_1_And_200(int requested, int expected)
    {
        // Arrange
        var sut = NewSut();

        // Act
        var page = await sut.ListAsync(Guid.NewGuid(), Guid.NewGuid(), page: 1, pageSize: requested);

        // Assert
        page.PageSize.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public async Task ListAsync_ClampsPage_To_AtLeast_1(int requested, int expected)
    {
        // Arrange
        var sut = NewSut();

        // Act
        var page = await sut.ListAsync(Guid.NewGuid(), Guid.NewGuid(), page: requested, pageSize: 50);

        // Assert
        page.Page.Should().Be(expected);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private CapabilityHistoryService NewSut()
        => new(_factory, NullLogger<CapabilityHistoryService>.Instance);

    private static CapabilityHistoryEvent NewEvent(
        Guid tenantId,
        Guid capabilityId,
        DateTimeOffset occurredAt,
        CapabilityHistoryEventType type)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CapabilityId = capabilityId,
            EventType = type,
            ActorOid = "seed@example.com",
            OccurredAt = occurredAt,
            Summary = $"Event {type}",
            MetadataJson = null,
        };

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}

using Ato.Copilot.Core.Configuration.Tenancy;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy.Csp;

/// <summary>
/// T011 [Feature 050 / US1]: unit tests for
/// <see cref="CspInheritedComponentService.AddCapabilityAsync"/> — vetted-
/// by-default behavior (FR-001).
/// </summary>
/// <remarks>
/// <para>
/// Asserts the contract pinned in
/// <c>specs/050-csp-capability-lifecycle/contracts/internal-services.md § 2.1</c>:
/// </para>
/// <list type="bullet">
///   <item>Default (<c>markMappedImmediately = false</c>) →
///     <c>Status = NeedsReview</c>, <c>ReviewedBy = null</c>, exactly
///     <b>one</b> <c>Created</c> history row with <c>MetadataJson = null</c>.</item>
///   <item>Override (<c>markMappedImmediately = true</c>) →
///     <c>Status = Mapped</c>, <c>ReviewedBy = actor</c>,
///     <c>ReviewerNote = "Mapped on create by creator."</c>, exactly
///     <b>two</b> history rows (<c>Created</c> with
///     <c>{ markedMappedImmediately: true }</c>, <c>Reviewed</c> with
///     <c>{ reviewerNote: "..." }</c>).</item>
///   <item>Capability + history rows commit atomically (a thrown
///     <c>SaveChangesAsync</c> on the integration boundary leaves no
///     orphaned rows). The InMemory provider does not enforce
///     transactions, so this case is covered at the integration layer
///     in T012; this file pins the in-process collaborator behavior.</item>
/// </list>
/// </remarks>
public sealed class AddCapabilityAsyncDefaultsTests : IDisposable
{
    private readonly string _dbName = $"AddCapability_{Guid.NewGuid()}";
    private readonly TestDbContextFactory _factory;

    public AddCapabilityAsyncDefaultsTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        _factory = new TestDbContextFactory(options);
    }

    public void Dispose() => GC.SuppressFinalize(this);

    // ─── Default behavior: NeedsReview + one Created row ────────────────

    [Fact]
    public async Task AddCapability_Default_PersistsAsNeedsReview_WithOneCreatedHistoryRow()
    {
        // Arrange
        var componentId = await SeedComponentAsync();
        var sut = BuildSut();

        // Act
        var capability = await sut.AddCapabilityAsync(
            componentId,
            name: "Tenant-level RBAC enforcement",
            description: "Azure RBAC role assignments.",
            mappedNistControlIds: new[] { "AC-2", "AC-2(1)" },
            actor: "user-oid",
            markMappedImmediately: false,
            ct: CancellationToken.None);

        // Assert — capability shape
        capability.Status.Should().Be(CspInheritedCapabilityStatus.NeedsReview);
        capability.MappedBy.Should().Be(MappedBy.User);
        capability.ReviewedBy.Should().BeNull();
        capability.ReviewedAt.Should().BeNull();
        capability.ReviewerNote.Should().BeNull();
        capability.CreatedBy.Should().Be("user-oid");
        capability.MappingConfidence.Should().BeNull();

        // Assert — exactly one Created history row, null metadata
        await using var db = _factory.CreateDbContext();
        var history = await db.CapabilityHistoryEvents
            .Where(h => h.CapabilityId == capability.Id)
            .ToListAsync();
        history.Should().HaveCount(1);
        history[0].EventType.Should().Be(CapabilityHistoryEventType.Created);
        history[0].MetadataJson.Should().BeNull();
        history[0].ActorOid.Should().Be("user-oid");
    }

    // ─── Override behavior: Mapped + two history rows ───────────────────

    [Fact]
    public async Task AddCapability_MarkMappedImmediately_PersistsAsMapped_WithTwoHistoryRows()
    {
        // Arrange
        var componentId = await SeedComponentAsync();
        var sut = BuildSut();

        // Act
        var capability = await sut.AddCapabilityAsync(
            componentId,
            name: "Tenant-level RBAC enforcement",
            description: "Azure RBAC role assignments.",
            mappedNistControlIds: new[] { "AC-2" },
            actor: "user-oid",
            markMappedImmediately: true);

        // Assert — capability shape
        capability.Status.Should().Be(CspInheritedCapabilityStatus.Mapped);
        capability.MappedBy.Should().Be(MappedBy.User);
        capability.ReviewedBy.Should().Be("user-oid");
        capability.ReviewedAt.Should().NotBeNull();
        capability.ReviewerNote.Should().Be("Mapped on create by creator.");

        // Assert — exactly two history rows
        await using var db = _factory.CreateDbContext();
        var history = await db.CapabilityHistoryEvents
            .Where(h => h.CapabilityId == capability.Id)
            .OrderBy(h => h.OccurredAt)
            .ThenBy(h => h.Id)
            .ToListAsync();
        history.Should().HaveCount(2);

        var created = history.Single(h => h.EventType == CapabilityHistoryEventType.Created);
        created.MetadataJson.Should().NotBeNull();
        created.MetadataJson.Should().Contain("markedMappedImmediately");
        created.MetadataJson.Should().Contain("true");

        var reviewed = history.Single(h => h.EventType == CapabilityHistoryEventType.Reviewed);
        reviewed.MetadataJson.Should().NotBeNull();
        reviewed.MetadataJson.Should().Contain("reviewerNote");
        reviewed.MetadataJson.Should().Contain("Mapped on create by creator.");
    }

    // ─── Existing validation preserved ──────────────────────────────────

    [Fact]
    public async Task AddCapability_UnknownComponent_ThrowsKeyNotFoundException()
    {
        // Arrange
        var sut = BuildSut();

        // Act
        Func<Task> act = () => sut.AddCapabilityAsync(
            Guid.NewGuid(),
            name: "Cap",
            description: "Desc",
            mappedNistControlIds: new[] { "AC-2" },
            actor: "user-oid");

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddCapability_EmptyActor_Throws(string actor)
    {
        // Arrange
        var componentId = await SeedComponentAsync();
        var sut = BuildSut();

        // Act
        Func<Task> act = () => sut.AddCapabilityAsync(
            componentId,
            name: "Cap",
            description: "Desc",
            mappedNistControlIds: new[] { "AC-2" },
            actor: actor);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private CspInheritedComponentService BuildSut()
    {
        var historySvc = new CapabilityHistoryService(
            _factory, NullLogger<CapabilityHistoryService>.Instance);
        var options = Options.Create(new CspInheritedOptions { MappingConfidenceThreshold = 0.6d });

        // Mock ITenantContext — CSP-Admin in TenantA.
        var tenantId = Guid.NewGuid();
        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.SetupGet(t => t.TenantId).Returns(tenantId);
        tenantCtx.SetupGet(t => t.EffectiveTenantId).Returns(tenantId);
        tenantCtx.SetupGet(t => t.IsCspAdmin).Returns(true);
        tenantCtx.SetupGet(t => t.ImpersonatedTenantId).Returns((Guid?)null);
        tenantCtx.SetupGet(t => t.OrganizationId).Returns((Guid?)null);
        tenantCtx.SetupGet(t => t.Status).Returns(TenantStatus.Active);

        return new CspInheritedComponentService(
            _factory,
            Mock.Of<ICspCapabilityMappingService>(),
            options,
            NullLogger<CspInheritedComponentService>.Instance,
            historySvc,
            tenantCtx.Object);
    }

    private async Task<Guid> SeedComponentAsync()
    {
        await using var db = _factory.CreateDbContext();
        var id = Guid.NewGuid();
        db.CspInheritedComponents.Add(new CspInheritedComponent
        {
            Id = id,
            CspProfileId = Guid.NewGuid(),
            Name = "Seed Component",
            Description = "seed",
            ComponentType = CspComponentType.Service,
            SourceFormat = SourceFormat.Manual,
            Status = CspInheritedComponentStatus.Published,
            ImportedAt = DateTimeOffset.UtcNow,
            ImportedBy = "seed",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}

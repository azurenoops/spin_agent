using Ato.Copilot.Core.Configuration.Tenancy;
using Ato.Copilot.Core.Data.Context;
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
/// T019 [Feature 050 / US2]: unit tests for
/// <see cref="CspInheritedComponentService.ReparentCapabilityAsync"/> —
/// move-capability behavior (FR-002 / FR-012).
/// </summary>
/// <remarks>
/// <para>
/// Asserts the contract pinned in
/// <c>specs/050-csp-capability-lifecycle/contracts/internal-services.md § 2.2</c>:
/// </para>
/// <list type="bullet">
///   <item>Success path → <c>CspInheritedComponentId</c> updated,
///     <c>Status = NeedsReview</c>, reviewer fields cleared,
///     <c>MappingFailureReason = "Moved to a new component; re-review required."</c>,
///     exactly one <c>Moved</c> history row with metadata
///     <c>{ fromComponentId, toComponentId }</c>.</item>
///   <item>Preserved fields invariant — <c>Name</c>, <c>Description</c>,
///     <c>MappedNistControlIds</c>, <c>MappingConfidence</c>,
///     <c>MappedBy</c>, <c>CreatedAt</c>, <c>CreatedBy</c> unchanged.</item>
///   <item>Target equals current component → <see cref="ArgumentException"/>.</item>
///   <item>Archived target → <see cref="KeyNotFoundException"/>.</item>
///   <item>Unknown target → <see cref="KeyNotFoundException"/>.</item>
///   <item>Capability not under source component →
///     <see cref="KeyNotFoundException"/>.</item>
///   <item><b>Deviation</b>: stale <c>rowVersion</c> producing
///     <see cref="DbUpdateConcurrencyException"/> cannot be expressed
///     against the InMemory provider (no concurrency tokens honored).
///     The endpoint-layer 412 mapping is covered in the integration
///     suite (T020).</item>
///   <item><b>Deviation</b>: cross-tenant 404 guard is covered in the
///     integration suite (T020) because the unit-level
///     <c>ITenantContext</c> is mocked to a single tenant.</item>
/// </list>
/// </remarks>
public sealed class ReparentCapabilityAsyncTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ReparentCapabilityAsyncTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"Reparent_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
    }

    public void Dispose() => GC.SuppressFinalize(this);

    // ─── Happy path ──────────────────────────────────────────────────────

    [Fact]
    public async Task Reparent_HappyPath_ChangesParent_ResetsReview_WritesOneMovedRow()
    {
        // Arrange — two components in the same tenant, one capability
        // already reviewed under the source component.
        var (sourceId, targetId) = await SeedTwoComponentsAsync(
            sourceArchived: false, targetArchived: false);
        var capability = await SeedCapabilityUnderAsync(sourceId, status: CspInheritedCapabilityStatus.Mapped);
        var sut = BuildSut();

        // Act
        var moved = await sut.ReparentCapabilityAsync(
            componentId: sourceId,
            capabilityId: capability.Id,
            targetComponentId: targetId,
            rowVersion: capability.RowVersion ?? Array.Empty<byte>(),
            actor: "user-oid");

        // Assert — reparented + review reset
        moved.CspInheritedComponentId.Should().Be(targetId);
        moved.Status.Should().Be(CspInheritedCapabilityStatus.NeedsReview);
        moved.ReviewedBy.Should().BeNull();
        moved.ReviewedAt.Should().BeNull();
        moved.ReviewerNote.Should().BeNull();
        moved.MappingFailureReason.Should().Be("Moved to a new component; re-review required.");

        // Assert — exactly one Moved history row
        await using var db = _factory.CreateDbContext();
        var rows = await db.CapabilityHistoryEvents
            .Where(h => h.CapabilityId == capability.Id)
            .ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].EventType.Should().Be(CapabilityHistoryEventType.Moved);
        rows[0].TenantId.Should().Be(_tenantId);
        rows[0].MetadataJson.Should().NotBeNull();
        rows[0].MetadataJson.Should().Contain("fromComponentId");
        rows[0].MetadataJson.Should().Contain("toComponentId");
        rows[0].MetadataJson.Should().Contain(sourceId.ToString());
        rows[0].MetadataJson.Should().Contain(targetId.ToString());
    }

    [Fact]
    public async Task Reparent_PreservesIdentityFields()
    {
        // Arrange — capture original values, reparent, assert unchanged.
        var (sourceId, targetId) = await SeedTwoComponentsAsync(false, false);
        var capability = await SeedCapabilityUnderAsync(sourceId);
        var beforeId = capability.Id;
        var beforeName = capability.Name;
        var beforeDescription = capability.Description;
        var beforeControlIds = capability.MappedNistControlIds.ToList();
        var beforeConfidence = capability.MappingConfidence;
        var beforeMappedBy = capability.MappedBy;
        var beforeCreatedAt = capability.CreatedAt;
        var beforeCreatedBy = capability.CreatedBy;
        var sut = BuildSut();

        // Act
        var moved = await sut.ReparentCapabilityAsync(
            sourceId, capability.Id, targetId,
            capability.RowVersion ?? Array.Empty<byte>(),
            actor: "user-oid");

        // Assert — preserved-fields invariant per FR-002
        moved.Id.Should().Be(beforeId);
        moved.Name.Should().Be(beforeName);
        moved.Description.Should().Be(beforeDescription);
        moved.MappedNistControlIds.Should().BeEquivalentTo(beforeControlIds);
        moved.MappingConfidence.Should().Be(beforeConfidence);
        moved.MappedBy.Should().Be(beforeMappedBy);
        moved.CreatedAt.Should().Be(beforeCreatedAt);
        moved.CreatedBy.Should().Be(beforeCreatedBy);
    }

    // ─── Negative paths ─────────────────────────────────────────────────

    [Fact]
    public async Task Reparent_TargetEqualsSource_ThrowsArgumentException()
    {
        // Arrange
        var (sourceId, _) = await SeedTwoComponentsAsync(false, false);
        var capability = await SeedCapabilityUnderAsync(sourceId);
        var sut = BuildSut();

        // Act
        Func<Task> act = () => sut.ReparentCapabilityAsync(
            sourceId, capability.Id,
            targetComponentId: sourceId,
            rowVersion: capability.RowVersion ?? Array.Empty<byte>(),
            actor: "user-oid");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Reparent_ArchivedTarget_ThrowsKeyNotFoundException()
    {
        // Arrange — target is Archived; should look like "not found" to caller.
        var (sourceId, targetId) = await SeedTwoComponentsAsync(
            sourceArchived: false, targetArchived: true);
        var capability = await SeedCapabilityUnderAsync(sourceId);
        var sut = BuildSut();

        // Act
        Func<Task> act = () => sut.ReparentCapabilityAsync(
            sourceId, capability.Id, targetId,
            capability.RowVersion ?? Array.Empty<byte>(),
            actor: "user-oid");

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Reparent_UnknownTarget_ThrowsKeyNotFoundException()
    {
        // Arrange
        var (sourceId, _) = await SeedTwoComponentsAsync(false, false);
        var capability = await SeedCapabilityUnderAsync(sourceId);
        var sut = BuildSut();

        // Act
        Func<Task> act = () => sut.ReparentCapabilityAsync(
            sourceId, capability.Id,
            targetComponentId: Guid.NewGuid(),
            rowVersion: capability.RowVersion ?? Array.Empty<byte>(),
            actor: "user-oid");

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Reparent_CapabilityNotUnderSource_ThrowsKeyNotFoundException()
    {
        // Arrange — capability belongs to a third component, not the source.
        var (sourceId, targetId) = await SeedTwoComponentsAsync(false, false);
        var otherSourceId = await SeedComponentAsync("Other", archived: false);
        var capability = await SeedCapabilityUnderAsync(otherSourceId);
        var sut = BuildSut();

        // Act
        Func<Task> act = () => sut.ReparentCapabilityAsync(
            componentId: sourceId,
            capabilityId: capability.Id,
            targetComponentId: targetId,
            rowVersion: capability.RowVersion ?? Array.Empty<byte>(),
            actor: "user-oid");

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Reparent_NullRowVersion_ThrowsArgumentNullException()
    {
        // Arrange — endpoint guarantees non-null, but the service contract
        // makes that guarantee explicit (defense in depth, per contract § 2.2.2).
        var (sourceId, targetId) = await SeedTwoComponentsAsync(false, false);
        var capability = await SeedCapabilityUnderAsync(sourceId);
        var sut = BuildSut();

        // Act
        Func<Task> act = () => sut.ReparentCapabilityAsync(
            sourceId, capability.Id, targetId,
            rowVersion: null!,
            actor: "user-oid");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CspInheritedComponentService BuildSut()
    {
        var historySvc = new CapabilityHistoryService(
            _factory, NullLogger<CapabilityHistoryService>.Instance);
        var options = Options.Create(new CspInheritedOptions { MappingConfidenceThreshold = 0.6d });

        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.SetupGet(t => t.TenantId).Returns(_tenantId);
        tenantCtx.SetupGet(t => t.EffectiveTenantId).Returns(_tenantId);
        tenantCtx.SetupGet(t => t.IsCspAdmin).Returns(true);
        tenantCtx.SetupGet(t => t.ImpersonatedTenantId).Returns((Guid?)null);
        tenantCtx.SetupGet(t => t.OrganizationId).Returns((Guid?)null);
        tenantCtx.SetupGet(t => t.Status).Returns(TenantStatus.Active);

        return new CspInheritedComponentService(
            _factory,
            Mock.Of<Ato.Copilot.Core.Interfaces.Tenancy.ICspCapabilityMappingService>(),
            options,
            NullLogger<CspInheritedComponentService>.Instance,
            historySvc,
            tenantCtx.Object);
    }

    private async Task<Guid> SeedComponentAsync(string name, bool archived)
    {
        await using var db = _factory.CreateDbContext();
        var id = Guid.NewGuid();
        db.CspInheritedComponents.Add(new CspInheritedComponent
        {
            Id = id,
            CspProfileId = Guid.NewGuid(),
            Name = name,
            Description = "seed",
            ComponentType = CspComponentType.Service,
            SourceFormat = SourceFormat.Manual,
            Status = archived
                ? CspInheritedComponentStatus.Archived
                : CspInheritedComponentStatus.Published,
            ImportedAt = DateTimeOffset.UtcNow,
            ImportedBy = "seed",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<(Guid source, Guid target)> SeedTwoComponentsAsync(
        bool sourceArchived, bool targetArchived)
    {
        var source = await SeedComponentAsync("Source", sourceArchived);
        var target = await SeedComponentAsync("Target", targetArchived);
        return (source, target);
    }

    private async Task<CspInheritedCapability> SeedCapabilityUnderAsync(
        Guid componentId,
        CspInheritedCapabilityStatus status = CspInheritedCapabilityStatus.Mapped)
    {
        await using var db = _factory.CreateDbContext();
        var cap = new CspInheritedCapability
        {
            Id = Guid.NewGuid(),
            CspInheritedComponentId = componentId,
            Name = "Tenant RBAC",
            Description = "Azure RBAC role assignments.",
            MappedNistControlIds = new List<string> { "AC-2", "AC-2(1)" },
            MappingConfidence = 0.87,
            MappedBy = MappedBy.AI,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedBy = "system",
            ReviewedAt = status == CspInheritedCapabilityStatus.Mapped ? DateTimeOffset.UtcNow.AddMinutes(-30) : null,
            ReviewedBy = status == CspInheritedCapabilityStatus.Mapped ? "reviewer-oid" : null,
            ReviewerNote = status == CspInheritedCapabilityStatus.Mapped ? "looks good" : null,
            RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
        };
        db.CspInheritedCapabilities.Add(cap);
        await db.SaveChangesAsync();
        return cap;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}

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
/// T030 [Feature 050 / US3]: assert
/// <see cref="CspInheritedComponentService.ReviewCapabilityAsync"/> writes
/// exactly one <see cref="CapabilityHistoryEventType.Reviewed"/> row.
/// <c>metadata.reviewerNote</c> appears iff a note was supplied.
/// Pinned by <c>contracts/internal-services.md § 2.3</c>.
/// </summary>
public sealed class ReviewCapabilityAsyncHistoryTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ReviewCapabilityAsyncHistoryTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"ReviewHistory_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
    }

    public void Dispose() => GC.SuppressFinalize(this);

    [Fact]
    public async Task ReviewCapability_WithReviewerNote_WritesReviewedRow_WithNoteInMetadata()
    {
        // Arrange
        var componentId = await SeedComponentAsync();
        var capability = await SeedNeedsReviewCapabilityAsync(componentId);
        var sut = BuildSut();

        // Act
        await sut.ReviewCapabilityAsync(
            componentId, capability.Id,
            mappedControlIds: new[] { "AC-2" },
            reviewerNote: "Approved after manual NIST cross-check.",
            actor: "user-oid");

        // Assert
        await using var db = _factory.CreateDbContext();
        var rows = await db.CapabilityHistoryEvents
            .Where(h => h.CapabilityId == capability.Id)
            .ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].EventType.Should().Be(CapabilityHistoryEventType.Reviewed);
        rows[0].MetadataJson.Should().NotBeNull();
        rows[0].MetadataJson.Should().Contain("reviewerNote");
        rows[0].MetadataJson.Should().Contain("Approved after manual NIST cross-check.");
    }

    [Fact]
    public async Task ReviewCapability_WithoutReviewerNote_WritesReviewedRow_WithNullMetadata()
    {
        // Arrange
        var componentId = await SeedComponentAsync();
        var capability = await SeedNeedsReviewCapabilityAsync(componentId);
        var sut = BuildSut();

        // Act
        await sut.ReviewCapabilityAsync(
            componentId, capability.Id,
            mappedControlIds: new[] { "AC-2" },
            reviewerNote: null,
            actor: "user-oid");

        // Assert
        await using var db = _factory.CreateDbContext();
        var rows = await db.CapabilityHistoryEvents
            .Where(h => h.CapabilityId == capability.Id)
            .ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].EventType.Should().Be(CapabilityHistoryEventType.Reviewed);
        rows[0].MetadataJson.Should().BeNull(
            "no reviewerNote was supplied — metadata must be null, not the literal string \"null\".");
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private CspInheritedComponentService BuildSut()
    {
        var historySvc = new CapabilityHistoryService(
            _factory, NullLogger<CapabilityHistoryService>.Instance);
        var options = Options.Create(new CspInheritedOptions { MappingConfidenceThreshold = 0.6d });

        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.SetupGet(t => t.TenantId).Returns(_tenantId);
        tenantCtx.SetupGet(t => t.EffectiveTenantId).Returns(_tenantId);
        tenantCtx.SetupGet(t => t.IsCspAdmin).Returns(true);
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
            Name = "Component",
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

    private async Task<CspInheritedCapability> SeedNeedsReviewCapabilityAsync(Guid componentId)
    {
        await using var db = _factory.CreateDbContext();
        var cap = new CspInheritedCapability
        {
            Id = Guid.NewGuid(),
            CspInheritedComponentId = componentId,
            Name = "Cap",
            Description = "desc",
            MappedNistControlIds = new List<string> { "AC-2" },
            MappingConfidence = 0.42,
            MappedBy = MappedBy.AI,
            Status = CspInheritedCapabilityStatus.NeedsReview,
            MappingFailureReason = "Confidence below threshold (0.42)",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedBy = "system",
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

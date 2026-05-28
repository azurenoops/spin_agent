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
/// T029 [Feature 050 / US3]: assert <see cref="CspInheritedComponentService.UpdateCapabilityAsync"/>
/// writes exactly one <see cref="CapabilityHistoryEventType.Edited"/> row
/// with diff-summary metadata in the same transaction as the field update.
/// Pinned by <c>contracts/internal-services.md § 2.3</c>.
/// </summary>
public sealed class UpdateCapabilityAsyncHistoryTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public UpdateCapabilityAsyncHistoryTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"UpdateHistory_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
    }

    public void Dispose() => GC.SuppressFinalize(this);

    [Fact]
    public async Task UpdateCapability_WritesEditedHistoryRow_WithChangedFieldsInMetadata()
    {
        // Arrange
        var componentId = await SeedComponentAsync();
        var capability = await SeedCapabilityAsync(componentId,
            name: "Old name", description: "Old desc",
            controls: new[] { "AC-2" });
        var sut = BuildSut();

        // Act — change only Name + Controls.
        await sut.UpdateCapabilityAsync(
            componentId, capability.Id,
            name: "New name", description: "Old desc",
            mappedNistControlIds: new[] { "AC-2", "AC-2(1)" },
            rowVersion: null, actor: "user-oid");

        // Assert — exactly one Edited row with the changed-fields list.
        await using var db = _factory.CreateDbContext();
        var rows = await db.CapabilityHistoryEvents
            .Where(h => h.CapabilityId == capability.Id)
            .ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].EventType.Should().Be(CapabilityHistoryEventType.Edited);
        rows[0].MetadataJson.Should().NotBeNull();
        rows[0].MetadataJson.Should().Contain("name");
        rows[0].MetadataJson.Should().Contain("mappedNistControlIds");
        rows[0].MetadataJson.Should().NotContain("description",
            "the Description field was not changed so it must not appear in the diff list.");
    }

    [Fact]
    public async Task UpdateCapability_NoChange_StillWritesOneEditedRow()
    {
        // Arrange — same values as seed.
        var componentId = await SeedComponentAsync();
        var capability = await SeedCapabilityAsync(componentId,
            name: "Stable", description: "Stable",
            controls: new[] { "AC-2" });
        var sut = BuildSut();

        // Act — no field changes.
        await sut.UpdateCapabilityAsync(
            componentId, capability.Id,
            name: "Stable", description: "Stable",
            mappedNistControlIds: new[] { "AC-2" },
            rowVersion: null, actor: "user-oid");

        // Assert — Edited row written even when no fields changed; metadata
        // has an empty `fields` array. (The audit trail captures the click.)
        await using var db = _factory.CreateDbContext();
        var rows = await db.CapabilityHistoryEvents
            .Where(h => h.CapabilityId == capability.Id)
            .ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].EventType.Should().Be(CapabilityHistoryEventType.Edited);
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

    private async Task<CspInheritedCapability> SeedCapabilityAsync(
        Guid componentId, string name, string description, string[] controls)
    {
        await using var db = _factory.CreateDbContext();
        var cap = new CspInheritedCapability
        {
            Id = Guid.NewGuid(),
            CspInheritedComponentId = componentId,
            Name = name,
            Description = description,
            MappedNistControlIds = controls.ToList(),
            MappedBy = MappedBy.User,
            Status = CspInheritedCapabilityStatus.Mapped,
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

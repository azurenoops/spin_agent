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
/// T031 [Feature 050 / US3]: assert
/// <see cref="CspInheritedComponentService.ArchiveCapabilityAsync"/> writes
/// exactly one <see cref="CapabilityHistoryEventType.Archived"/> row with
/// <c>MetadataJson = null</c>; archiving an already-Archived capability
/// writes NO new row (idempotency preserved).
/// Pinned by <c>contracts/internal-services.md § 2.3</c>.
/// </summary>
public sealed class ArchiveCapabilityAsyncHistoryTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ArchiveCapabilityAsyncHistoryTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"ArchiveHistory_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
    }

    public void Dispose() => GC.SuppressFinalize(this);

    [Fact]
    public async Task ArchiveCapability_FromMapped_WritesOneArchivedRow_WithNullMetadata()
    {
        // Arrange
        var componentId = await SeedComponentAsync();
        var capability = await SeedCapabilityAsync(componentId, status: CspInheritedCapabilityStatus.Mapped);
        var sut = BuildSut();

        // Act
        await sut.ArchiveCapabilityAsync(componentId, capability.Id, actor: "user-oid");

        // Assert
        await using var db = _factory.CreateDbContext();
        var rows = await db.CapabilityHistoryEvents
            .Where(h => h.CapabilityId == capability.Id)
            .ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].EventType.Should().Be(CapabilityHistoryEventType.Archived);
        rows[0].MetadataJson.Should().BeNull();
    }

    [Fact]
    public async Task ArchiveCapability_AlreadyArchived_WritesNoNewRow()
    {
        // Arrange — capability is already Archived.
        var componentId = await SeedComponentAsync();
        var capability = await SeedCapabilityAsync(componentId,
            status: CspInheritedCapabilityStatus.Archived);
        var sut = BuildSut();

        // Act
        await sut.ArchiveCapabilityAsync(componentId, capability.Id, actor: "user-oid");

        // Assert — idempotency: NO new history row.
        await using var db = _factory.CreateDbContext();
        var rows = await db.CapabilityHistoryEvents
            .Where(h => h.CapabilityId == capability.Id)
            .ToListAsync();
        rows.Should().BeEmpty(
            "archiving an already-Archived capability is idempotent — no audit row written.");
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
        Guid componentId, CspInheritedCapabilityStatus status)
    {
        await using var db = _factory.CreateDbContext();
        var cap = new CspInheritedCapability
        {
            Id = Guid.NewGuid(),
            CspInheritedComponentId = componentId,
            Name = "Cap",
            Description = "desc",
            MappedNistControlIds = new List<string> { "AC-2" },
            MappedBy = MappedBy.User,
            Status = status,
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

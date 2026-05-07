using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Services;

public class ComponentServiceTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly ComponentService _sut;
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;

    private const string SystemId = "sys-001";
    private const string CapId1 = "cap-001";
    private const string CapId2 = "cap-002";

    public ComponentServiceTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"ComponentTests_{Guid.NewGuid()}")
            .Options;
        var factory = new TestDbContextFactory(_dbOptions);
        _db = factory.Context;
        var logger = Mock.Of<ILogger<ComponentService>>();
        _sut = new ComponentService(factory, logger, new NarrativeTemplateService(), new SystemCapabilityLinkService(factory, Mock.Of<ILogger<SystemCapabilityLinkService>>()));

        SeedData();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void SeedData()
    {
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = SystemId,
            Name = "Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true,
        });

        _db.SecurityCapabilities.Add(new SecurityCapability
        {
            Id = CapId1,
            Name = "MFA",
            Provider = "Entra ID",
            Category = "IA",
            Description = "Multi-factor authentication",
            ImplementationStatus = CapabilityStatus.Implemented,
            Owner = "IAM Team",
            CreatedBy = "test",
        });

        _db.SecurityCapabilities.Add(new SecurityCapability
        {
            Id = CapId2,
            Name = "Encryption at Rest",
            Provider = "Key Vault",
            Category = "SC",
            Description = "Data encryption",
            ImplementationStatus = CapabilityStatus.Implemented,
            Owner = "Platform Team",
            CreatedBy = "test",
        });

        _db.SaveChanges();
    }

    // ─── GetComponentsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetComponentsAsync_EmptyInventory_ReturnsZeroCounts()
    {
        var result = await _sut.GetComponentsAsync(SystemId, new ComponentQuery());

        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.Summary.TotalCount.Should().Be(0);
        result.Summary.PersonCount.Should().Be(0);
        result.Summary.PlaceCount.Should().Be(0);
        result.Summary.ThingCount.Should().Be(0);
    }

    [Fact]
    public async Task GetComponentsAsync_UnknownSystem_ReturnsNull()
    {
        var result = await _sut.GetComponentsAsync("no-such-system", new ComponentQuery());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetComponentsAsync_FilterByType_ReturnsOnlyMatching()
    {
        await SeedComponents();

        var result = await _sut.GetComponentsAsync(SystemId, new ComponentQuery { Type = "Person" });

        result.Should().NotBeNull();
        result!.Items.Should().AllSatisfy(c => c.ComponentType.Should().Be("Person"));
    }

    [Fact]
    public async Task GetComponentsAsync_FilterByStatus_ReturnsOnlyMatching()
    {
        await SeedComponents();

        var result = await _sut.GetComponentsAsync(SystemId, new ComponentQuery { Status = "Planned" });

        result.Should().NotBeNull();
        result!.Items.Should().AllSatisfy(c => c.Status.Should().Be("Planned"));
    }

    [Fact]
    public async Task GetComponentsAsync_Search_MatchesNameOrDescription()
    {
        await SeedComponents();

        var result = await _sut.GetComponentsAsync(SystemId, new ComponentQuery { Search = "Sentinel" });

        result.Should().NotBeNull();
        result!.Items.Should().ContainSingle();
        result.Items[0].Name.Should().Be("Sentinel");
    }

    [Fact]
    public async Task GetComponentsAsync_SummaryCounts_AreUnfiltered()
    {
        await SeedComponents();

        var result = await _sut.GetComponentsAsync(SystemId, new ComponentQuery { Type = "Person" });

        result.Should().NotBeNull();
        // Summary reflects all components, not just filtered
        result!.Summary.TotalCount.Should().Be(3);
        result.Summary.PersonCount.Should().Be(1);
        result.Summary.PlaceCount.Should().Be(1);
        result.Summary.ThingCount.Should().Be(1);
    }

    // ─── CreateComponentAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CreateComponentAsync_ValidRequest_ReturnsDto()
    {
        var request = new CreateComponentRequest
        {
            Name = "ISSM",
            ComponentType = "Person",
            SubType = "Security Personnel",
            Description = "Information System Security Manager",
            Owner = "Security Office",
            Status = "Active",
            LinkedCapabilityIds = [],
        };

        var result = await _sut.CreateComponentAsync(SystemId, request, "tester");

        result.Should().NotBeNull();
        result!.Name.Should().Be("ISSM");
        result.ComponentType.Should().Be("Person");
        result.SubType.Should().Be("Security Personnel");
        result.Status.Should().Be("Active");
    }

    [Fact]
    public async Task CreateComponentAsync_WithCapabilityLinks_LinksAreCreated()
    {
        var request = new CreateComponentRequest
        {
            Name = "Entra ID",
            ComponentType = "Thing",
            Status = "Active",
            LinkedCapabilityIds = [CapId1, CapId2],
        };

        var result = await _sut.CreateComponentAsync(SystemId, request, "tester");

        result.Should().NotBeNull();
        result!.LinkedCapabilities.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateComponentAsync_InvalidSystem_ReturnsNull()
    {
        var request = new CreateComponentRequest
        {
            Name = "Test",
            ComponentType = "Thing",
            Status = "Active",
        };

        var result = await _sut.CreateComponentAsync("no-such-system", request, "tester");
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateComponentAsync_NonexistentCapability_SkipsLink()
    {
        var request = new CreateComponentRequest
        {
            Name = "Test",
            ComponentType = "Thing",
            Status = "Active",
            LinkedCapabilityIds = [CapId1, "nonexistent-cap"],
        };

        var result = await _sut.CreateComponentAsync(SystemId, request, "tester");

        result.Should().NotBeNull();
        result!.LinkedCapabilities.Should().HaveCount(1);
    }

    // ─── UpdateComponentAsync ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateComponentAsync_ExistingComponent_ReturnsUpdated()
    {
        var comp = await CreateTestComponent("Original", ComponentType.Thing);

        var request = new CreateComponentRequest
        {
            Name = "Updated",
            ComponentType = "Person",
            Status = "Planned",
        };

        var result = await _sut.UpdateComponentAsync(comp.Id, request);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated");
        result.ComponentType.Should().Be("Person");
        result.Status.Should().Be("Planned");
    }

    [Fact]
    public async Task UpdateComponentAsync_ReconcileLinks_OldRemovedNewAdded()
    {
        var comp = await CreateTestComponent("WithLinks", ComponentType.Thing, [CapId1]);

        var request = new CreateComponentRequest
        {
            Name = "WithLinks",
            ComponentType = "Thing",
            Status = "Active",
            LinkedCapabilityIds = [CapId2],
        };

        var result = await _sut.UpdateComponentAsync(comp.Id, request);

        result.Should().NotBeNull();
        result!.LinkedCapabilities.Should().HaveCount(1);
        result.LinkedCapabilities[0].CapabilityName.Should().Be("Encryption at Rest");
    }

    [Fact]
    public async Task UpdateComponentAsync_NonexistentComponent_ReturnsNull()
    {
        var request = new CreateComponentRequest
        {
            Name = "X",
            ComponentType = "Thing",
            Status = "Active",
        };

        var result = await _sut.UpdateComponentAsync("no-such-id", request);
        result.Should().BeNull();
    }

    // ─── DeleteComponentAsync ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteComponentAsync_ActiveWithLinks_FlagsCapabilities()
    {
        var comp = await CreateTestComponent("ActiveLinked", ComponentType.Thing, [CapId1, CapId2]);

        var result = await _sut.DeleteComponentAsync(comp.Id, "tester");

        result.Should().NotBeNull();
        result!.DeletedId.Should().Be(comp.Id);
        result.FlaggedCapabilities.Should().HaveCount(2);
        result.FlaggedCapabilities.Should().Contain(f => f.CapabilityName == "MFA");
        result.FlaggedCapabilities.Should().Contain(f => f.CapabilityName == "Encryption at Rest");

        // Dashboard activity entries created
        var activities = await _db.DashboardActivities.Where(a => a.EventType == "ComponentDeleted").ToListAsync();
        activities.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteComponentAsync_DecommissionedWithLinks_NoFlags()
    {
        var comp = await CreateTestComponent("DecommLinked", ComponentType.Thing, [CapId1], ComponentStatus.Decommissioned);

        var result = await _sut.DeleteComponentAsync(comp.Id, "tester");

        result.Should().NotBeNull();
        result!.FlaggedCapabilities.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteComponentAsync_ActiveNoLinks_EmptyFlags()
    {
        var comp = await CreateTestComponent("ActiveNoLinks", ComponentType.Person);

        var result = await _sut.DeleteComponentAsync(comp.Id, "tester");

        result.Should().NotBeNull();
        result!.FlaggedCapabilities.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteComponentAsync_NonexistentComponent_ReturnsNull()
    {
        var result = await _sut.DeleteComponentAsync("no-such-id", "tester");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteComponentAsync_ComponentRemovedFromDb()
    {
        var comp = await CreateTestComponent("ToDelete", ComponentType.Thing);

        await _sut.DeleteComponentAsync(comp.Id, "tester");

        var exists = await _db.SystemComponents.AnyAsync(c => c.Id == comp.Id);
        exists.Should().BeFalse();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task SeedComponents()
    {
        _db.SystemComponents.AddRange(
            new SystemComponent { RegisteredSystemId = SystemId, Name = "ISSM", ComponentType = ComponentType.Person, Status = ComponentStatus.Active, CreatedBy = "test" },
            new SystemComponent { RegisteredSystemId = SystemId, Name = "Azure Gov East", ComponentType = ComponentType.Place, Status = ComponentStatus.Planned, CreatedBy = "test" },
            new SystemComponent { RegisteredSystemId = SystemId, Name = "Sentinel", ComponentType = ComponentType.Thing, Status = ComponentStatus.Active, Description = "SIEM tool", CreatedBy = "test" }
        );
        await _db.SaveChangesAsync();
    }

    private async Task<SystemComponent> CreateTestComponent(
        string name,
        ComponentType type,
        List<string>? capIds = null,
        ComponentStatus status = ComponentStatus.Active)
    {
        var entity = new SystemComponent
        {
            RegisteredSystemId = SystemId,
            Name = name,
            ComponentType = type,
            Status = status,
            CreatedBy = "test",
        };
        _db.SystemComponents.Add(entity);

        if (capIds is not null)
        {
            foreach (var capId in capIds)
            {
                _db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
                {
                    SystemComponentId = entity.Id,
                    SecurityCapabilityId = capId,
                });
            }
        }

        await _db.SaveChangesAsync();
        return entity;
    }

    // ─── Component Cascade Tests ─────────────────────────────────────────────

    private async Task<string> SeedCascadeData()
    {
        var comp = new SystemComponent
        {
            Id = "comp-cascade",
            Name = "Old Component Name",
            ComponentType = ComponentType.Thing,
            Status = ComponentStatus.Active,
            Description = "Old description",
            Owner = "Old Owner",
            CreatedBy = "test",
        };
        _db.SystemComponents.Add(comp);

        _db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
        {
            SystemComponentId = comp.Id,
            SecurityCapabilityId = CapId1,
        });

        _db.ComponentSystemAssignments.Add(new ComponentSystemAssignment
        {
            SystemComponentId = comp.Id,
            RegisteredSystemId = SystemId,
            CreatedBy = "test",
        });

        _db.CapabilityControlMappings.Add(new CapabilityControlMapping
        {
            SecurityCapabilityId = CapId1,
            ControlId = "ia-2",
            RegisteredSystemId = SystemId,
            Role = CapabilityMappingRole.Primary,
            CreatedBy = "test",
        });

        _db.NistControls.Add(new NistControl
        {
            Id = "ia-2",
            Title = "Identification and Authentication",
            Family = "IA",
            ImpactLevel = "Low",
        });

        _db.ControlImplementations.Add(new ControlImplementation
        {
            Id = "impl-cascade",
            RegisteredSystemId = SystemId,
            ControlId = "ia-2",
            SecurityCapabilityId = CapId1,
            Narrative = "Original narrative text",
            IsAutoPopulated = true,
            AuthoredBy = "test",
            CurrentVersion = 1,
        });

        await _db.SaveChangesAsync();
        return comp.Id;
    }

    [Fact]
    public async Task UpdateOrgComponent_NameChange_CascadesNarrativeRegeneration()
    {
        var compId = await SeedCascadeData();

        await _sut.UpdateOrgComponentAsync(compId, new CreateComponentRequest
        {
            Name = "New Component Name",
            ComponentType = "Thing",
            Status = "Active",
            Description = "Old description",
            Owner = "Old Owner",
            LinkedCapabilityIds = [CapId1],
        });

        var impl = await _db.ControlImplementations.FindAsync("impl-cascade");
        impl!.Narrative.Should().NotBe("Original narrative text");
        impl.Narrative.Should().Contain("New Component Name");
    }

    [Fact]
    public async Task UpdateOrgComponent_NameChange_CreatesNarrativeVersion()
    {
        var compId = await SeedCascadeData();

        await _sut.UpdateOrgComponentAsync(compId, new CreateComponentRequest
        {
            Name = "Renamed",
            ComponentType = "Thing",
            Status = "Active",
            Description = "Old description",
            Owner = "Old Owner",
            LinkedCapabilityIds = [CapId1],
        });

        var versions = await _db.NarrativeVersions.ToListAsync();
        versions.Should().HaveCount(1);
        versions[0].Content.Should().Be("Original narrative text");
        versions[0].ChangeReason.Should().Contain("component");

        var impl = await _db.ControlImplementations.FindAsync("impl-cascade");
        impl!.CurrentVersion.Should().Be(2);
    }

    [Fact]
    public async Task UpdateOrgComponent_NoMetadataChange_SkipsCascade()
    {
        var compId = await SeedCascadeData();

        await _sut.UpdateOrgComponentAsync(compId, new CreateComponentRequest
        {
            Name = "Old Component Name", // same
            ComponentType = "Person",    // different type, not cascade-triggering
            Status = "Active",
            Description = "Old description", // same
            Owner = "Old Owner",             // same
            LinkedCapabilityIds = [CapId1],
        });

        var impl = await _db.ControlImplementations.FindAsync("impl-cascade");
        impl!.Narrative.Should().Be("Original narrative text"); // unchanged
    }

    [Fact]
    public async Task UpdateOrgComponent_ManuallyCustomized_SkipsCascade()
    {
        var compId = await SeedCascadeData();
        var impl = await _db.ControlImplementations.FindAsync("impl-cascade");
        impl!.IsManuallyCustomized = true;
        impl.Narrative = "Custom narrative";
        await _db.SaveChangesAsync();

        await _sut.UpdateOrgComponentAsync(compId, new CreateComponentRequest
        {
            Name = "New Name",
            ComponentType = "Thing",
            Status = "Active",
            Description = "Old description",
            Owner = "Old Owner",
            LinkedCapabilityIds = [CapId1],
        });

        impl = await _db.ControlImplementations.FindAsync("impl-cascade");
        impl!.Narrative.Should().Be("Custom narrative"); // unchanged
    }

    [Fact]
    public async Task GetComponentImpactPreview_ReturnsCorrectCounts()
    {
        var compId = await SeedCascadeData();

        var preview = await _sut.GetComponentImpactPreviewAsync(compId);

        preview.Should().NotBeNull();
        preview!.TotalNarratives.Should().Be(1);
        preview.TotalSystems.Should().Be(1);
        preview.CustomSkipped.Should().Be(0);
        preview.BySystem.Should().HaveCount(1);
        preview.BySystem[0].SystemId.Should().Be(SystemId);
    }

    [Fact]
    public async Task GetComponentImpactPreview_NotFound_ReturnsNull()
    {
        var preview = await _sut.GetComponentImpactPreviewAsync("nonexistent");
        preview.Should().BeNull();
    }
}

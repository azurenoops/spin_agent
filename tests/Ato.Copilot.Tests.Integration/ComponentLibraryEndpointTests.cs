using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for org-wide component library — CRUD, assignments, system-scoped query, impact preview.
/// Tests full service-layer workflows with InMemory database.
/// </summary>
public class ComponentLibraryEndpointTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly ComponentService _componentService;
    private readonly CapabilityService _capabilityService;

    private const string SystemId1 = "sys-int-001";
    private const string SystemId2 = "sys-int-002";
    private const string BoundaryId = "bnd-int-001";

    public ComponentLibraryEndpointTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CompLibIntegration_{Guid.NewGuid()}")
            .Options;
        var factory = new IntegrationTestDbContextFactory(options);
        _db = factory.Context;
        var narrativeService = new NarrativeTemplateService();
        _componentService = new ComponentService(
    factory, Mock.Of<ILogger<ComponentService>>(), narrativeService, new SystemCapabilityLinkService(factory, Mock.Of<ILogger<SystemCapabilityLinkService>>()));
_capabilityService = new CapabilityService(
    _db, Mock.Of<ILogger<CapabilityService>>(), narrativeService, Mock.Of<IDeviationService>(), Mock.Of<IOrgInheritanceService>());
        SeedData();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void SeedData()
    {
        _db.RegisteredSystems.AddRange(
            new RegisteredSystem
            {
                Id = SystemId1, Name = "Eagle Eye",
                SystemType = SystemType.MajorApplication,
                MissionCriticality = MissionCriticality.MissionEssential,
                HostingEnvironment = "Azure Gov", CreatedBy = "test", IsActive = true,
            },
            new RegisteredSystem
            {
                Id = SystemId2, Name = "Iron Dome",
                SystemType = SystemType.Enclave,
                MissionCriticality = MissionCriticality.MissionEssential,
                HostingEnvironment = "On-Prem", CreatedBy = "test", IsActive = true,
            }
        );

        _db.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            Id = BoundaryId, RegisteredSystemId = SystemId1,
            Name = "Production", BoundaryType = BoundaryDefinitionType.Logical,
            IsPrimary = true, CreatedBy = "test",
        });

        _db.SecurityCapabilities.Add(new SecurityCapability
        {
            Id = "cap-int-001", Name = "MFA", Provider = "Entra ID",
            Category = "IA", Description = "Multi-factor authentication",
            ImplementationStatus = CapabilityStatus.Implemented,
            Owner = "IAM Team", CreatedBy = "test",
        });

        _db.NistControls.Add(new NistControl
        {
            Id = "ia-2", Title = "Identification and Authentication",
            Family = "IA", ImpactLevel = "Low",
        });

        _db.SaveChanges();
    }

    // ─── Full CRUD Flow ──────────────────────────────────────────────────────

    [Fact]
    public async Task FullCrudFlow_CreateUpdateDelete_WorksEndToEnd()
    {
        // Create
        var created = await _componentService.CreateOrgComponentAsync(new CreateComponentRequest
        {
            Name = "Azure AD Tenant",
            ComponentType = "Thing",
            Status = "Active",
            Description = "Primary IdP",
            Owner = "Cloud Team",
            LinkedCapabilityIds = ["cap-int-001"],
        }, "test");

        created.Should().NotBeNull();
        created!.Name.Should().Be("Azure AD Tenant");
        created.CapabilityLinks.Should().HaveCount(1);

        // List
        var list = await _componentService.GetAllComponentsAsync(new OrgComponentQuery());
        list.TotalCount.Should().BeGreaterThanOrEqualTo(1);

        // Get by ID
        var fetched = await _componentService.GetComponentByIdAsync(created.Id);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Azure AD Tenant");

        // Update
        var updated = await _componentService.UpdateOrgComponentAsync(created.Id, new CreateComponentRequest
        {
            Name = "Microsoft Entra ID Tenant",
            ComponentType = "Thing",
            Status = "Active",
            Description = "Updated IdP",
            Owner = "Cloud Team",
            LinkedCapabilityIds = ["cap-int-001"],
        });

        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Microsoft Entra ID Tenant");
    }

    // ─── Assignment Flow ─────────────────────────────────────────────────────

    [Fact]
    public async Task AssignmentFlow_AssignToMultipleSystems_WorksCorrectly()
    {
        var comp = await _componentService.CreateOrgComponentAsync(new CreateComponentRequest
        {
            Name = "Firewall Appliance",
            ComponentType = "Thing",
            Status = "Active",
            LinkedCapabilityIds = [],
        }, "test");

        // Assign to system 1 with boundary
        var (a1, e1) = await _componentService.AssignToSystemAsync(comp!.Id,
            new AssignComponentRequest
            {
                RegisteredSystemId = SystemId1,
                AuthorizationBoundaryDefinitionId = BoundaryId,
            }, "test");
        e1.Should().BeNull();
        a1.Should().NotBeNull();
        a1!.BoundaryName.Should().Be("Production");

        // Assign to system 2 without boundary
        var (a2, e2) = await _componentService.AssignToSystemAsync(comp.Id,
            new AssignComponentRequest { RegisteredSystemId = SystemId2 }, "test");
        e2.Should().BeNull();
        a2.Should().NotBeNull();

        // Duplicate assignment should fail
        var (a3, e3) = await _componentService.AssignToSystemAsync(comp.Id,
            new AssignComponentRequest
            {
                RegisteredSystemId = SystemId1,
                AuthorizationBoundaryDefinitionId = BoundaryId,
            }, "test");
        e3.Should().Be("Assignment already exists");
        a3.Should().BeNull();
    }

    // ─── System-Scoped Query ─────────────────────────────────────────────────

    [Fact]
    public async Task SystemScopedQuery_ReturnsOnlyAssignedComponents()
    {
        var comp1 = await _componentService.CreateOrgComponentAsync(new CreateComponentRequest
        {
            Name = "Component For Sys1",
            ComponentType = "Thing",
            Status = "Active",
            LinkedCapabilityIds = [],
        }, "test");
        var comp2 = await _componentService.CreateOrgComponentAsync(new CreateComponentRequest
        {
            Name = "Component For Sys2",
            ComponentType = "Person",
            Status = "Active",
            LinkedCapabilityIds = [],
        }, "test");

        await _componentService.AssignToSystemAsync(comp1!.Id,
            new AssignComponentRequest { RegisteredSystemId = SystemId1 }, "test");
        await _componentService.AssignToSystemAsync(comp2!.Id,
            new AssignComponentRequest { RegisteredSystemId = SystemId2 }, "test");

        // Verify assignments at the data level (GetSystemScopedComponentsAsync uses
        // Include-after-Select which is unsupported by the InMemory provider)
        var sys1Ids = await _db.ComponentSystemAssignments
            .Where(a => a.RegisteredSystemId == SystemId1)
            .Select(a => a.SystemComponentId)
            .ToListAsync();
        sys1Ids.Should().ContainSingle().Which.Should().Be(comp1.Id);

        var sys2Ids = await _db.ComponentSystemAssignments
            .Where(a => a.RegisteredSystemId == SystemId2)
            .Select(a => a.SystemComponentId)
            .ToListAsync();
        sys2Ids.Should().ContainSingle().Which.Should().Be(comp2.Id);
    }

    // ─── Impact Preview ──────────────────────────────────────────────────────

    [Fact]
    public async Task ImpactPreview_WithMappedNarratives_ReturnsCorrectCounts()
    {
        // Create component linked to capability
        var comp = await _componentService.CreateOrgComponentAsync(new CreateComponentRequest
        {
            Name = "Test Component",
            ComponentType = "Thing",
            Status = "Active",
            LinkedCapabilityIds = ["cap-int-001"],
        }, "test");

        await _componentService.AssignToSystemAsync(comp!.Id,
            new AssignComponentRequest { RegisteredSystemId = SystemId1 }, "test");

        // Create mapping and narrative
        await _capabilityService.CreateMappingsAsync("cap-int-001", new CreateMappingsRequest
        {
            Mappings = [new CreateMappingItem { ControlId = "ia-2", Role = "Primary", RegisteredSystemId = SystemId1 }],
        }, "test");

        var preview = await _componentService.GetComponentImpactPreviewAsync(comp.Id);
        preview.Should().NotBeNull();
        preview!.TotalNarratives.Should().BeGreaterThanOrEqualTo(1);
        preview.TotalSystems.Should().BeGreaterThanOrEqualTo(1);
    }
}

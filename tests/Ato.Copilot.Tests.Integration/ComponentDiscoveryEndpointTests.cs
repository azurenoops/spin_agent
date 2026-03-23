using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for org-level and system-level Azure discovery and import
/// workflows (Feature 040 — User Story 1 / User Story 2).
/// </summary>
public class ComponentDiscoveryEndpointTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly ComponentService _service;

    public ComponentDiscoveryEndpointTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CompDiscoveryIntegration_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(options);
        _service = new ComponentService(
            _db, NullLogger<ComponentService>.Instance, new NarrativeTemplateService(),
            new SystemCapabilityLinkService(_db, NullLogger<SystemCapabilityLinkService>.Instance));
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    // ─── Org-Level Import E2E ────────────────────────────────────────────────

    [Fact]
    public async Task OrgImport_EndToEnd_DiscoverImportRediscover()
    {
        // Step 1: import 3 Azure resources into org library
        var resources = new List<AzureImportResource>
        {
            new() { ResourceId = "/sub/1/rg/providers/Microsoft.Compute/vm/web-01", Name = "web-01", Type = "Microsoft.Compute/virtualMachines", ResourceGroup = "rg-prod", Location = "usgovvirginia" },
            new() { ResourceId = "/sub/1/rg/providers/Microsoft.Sql/servers/sql-01", Name = "sql-01", Type = "Microsoft.Sql/servers", ResourceGroup = "rg-prod", Location = "usgovvirginia" },
            new() { ResourceId = "/sub/1/rg/providers/Microsoft.Storage/storageAccounts/sa01", Name = "sa01", Type = "Microsoft.Storage/storageAccounts", ResourceGroup = "rg-prod", Location = "usgovvirginia" },
        };

        var result = await _service.ImportAzureComponentsAsync(resources, "test-user");

        result.Imported.Should().Be(3);
        result.Skipped.Should().Be(0);
        result.Components.Should().HaveCount(3);
        result.Components.Should().AllSatisfy(c => c.ComponentType.Should().Be("Thing"));

        // Step 2: verify in database
        var dbComponents = await _db.SystemComponents
            .Where(c => c.RegisteredSystemId == null)
            .ToListAsync();
        dbComponents.Should().HaveCount(3);
        dbComponents.Should().AllSatisfy(c =>
        {
            c.AzureResourceId.Should().NotBeNullOrEmpty();
            c.AzureResourceType.Should().NotBeNullOrEmpty();
            c.AzureResourceGroup.Should().Be("rg-prod");
            c.AzureLocation.Should().Be("usgovvirginia");
        });

        // Step 3: re-import same resources — all should be skipped
        var result2 = await _service.ImportAzureComponentsAsync(resources, "test-user");
        result2.Imported.Should().Be(0);
        result2.Skipped.Should().Be(3);
    }

    [Fact]
    public async Task OrgImport_PartialDuplicate_ImportsOnlyNew()
    {
        // Pre-seed one component
        _db.SystemComponents.Add(new SystemComponent
        {
            Name = "existing-vm", ComponentType = ComponentType.Thing,
            AzureResourceId = "/sub/rg/providers/Microsoft.Compute/vm/existing",
            RegisteredSystemId = null, CreatedBy = "seed",
        });
        await _db.SaveChangesAsync();

        var resources = new List<AzureImportResource>
        {
            new() { ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/existing", Name = "existing-vm", Type = "Microsoft.Compute/virtualMachines", ResourceGroup = "rg-prod", Location = "usgovvirginia" },
            new() { ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/new-vm", Name = "new-vm", Type = "Microsoft.Compute/virtualMachines", ResourceGroup = "rg-prod", Location = "usgovvirginia" },
        };

        var result = await _service.ImportAzureComponentsAsync(resources, "test-user");

        result.Imported.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.SkippedDetails.Should().ContainSingle()
            .Which.ResourceId.Should().Contain("existing");

        // Verify total org components = 2
        var total = await _db.SystemComponents
            .CountAsync(c => c.RegisteredSystemId == null);
        total.Should().Be(2);
    }

    // ─── System-Level Import E2E ─────────────────────────────────────────────

    [Fact]
    public async Task SystemImport_EndToEnd_ImportAndAssign()
    {
        // Setup system
        var system = new RegisteredSystem
        {
            Name = "Eagle Eye", SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "AzureGovernment", CreatedBy = "test",
        };
        _db.RegisteredSystems.Add(system);

        // Pre-seed an org-library component
        var orgComp = new SystemComponent
        {
            Name = "shared-kv", ComponentType = ComponentType.Thing,
            AzureResourceId = "/sub/rg/providers/Microsoft.KeyVault/vaults/shared-kv",
            RegisteredSystemId = null, CreatedBy = "seed",
        };
        _db.SystemComponents.Add(orgComp);
        await _db.SaveChangesAsync();

        // Import new resources + assign existing org component
        var resources = new List<AzureImportResource>
        {
            new() { ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/app-vm", Name = "app-vm", Type = "Microsoft.Compute/virtualMachines", ResourceGroup = "rg-app", Location = "usgovvirginia" },
        };

        var result = await _service.ImportSystemAzureComponentsAsync(
            system.Id, resources,
            new List<string> { orgComp.Id }, "test-user");

        result.Imported.Should().Be(1);
        result.AssignedFromOrg.Should().Be(1);

        // Verify system-scoped component
        var sysComp = await _db.SystemComponents
            .FirstAsync(c => c.RegisteredSystemId == system.Id);
        sysComp.AzureResourceType.Should().Be("Microsoft.Compute/virtualMachines");

        // Verify org assignment link
        var assignment = await _db.ComponentSystemAssignments
            .FirstAsync(a => a.SystemComponentId == orgComp.Id);
        assignment.RegisteredSystemId.Should().Be(system.Id);
    }

    [Fact]
    public async Task SystemImport_DuplicateAssign_SkipsExistingAssignment()
    {
        var system = new RegisteredSystem
        {
            Name = "Iron Dome", SystemType = SystemType.Enclave,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "On-Prem", CreatedBy = "test",
        };
        _db.RegisteredSystems.Add(system);

        var orgComp = new SystemComponent
        {
            Name = "shared-db", ComponentType = ComponentType.Thing,
            AzureResourceId = "/sub/rg/providers/Microsoft.Sql/servers/shared-db",
            RegisteredSystemId = null, CreatedBy = "seed",
        };
        _db.SystemComponents.Add(orgComp);

        // Pre-create assignment
        _db.ComponentSystemAssignments.Add(new ComponentSystemAssignment
        {
            SystemComponentId = orgComp.Id,
            RegisteredSystemId = system.Id,
            CreatedBy = "seed",
        });
        await _db.SaveChangesAsync();

        // Try to assign again
        var result = await _service.ImportSystemAzureComponentsAsync(
            system.Id, new List<AzureImportResource>(),
            new List<string> { orgComp.Id }, "test-user");

        result.AssignedFromOrg.Should().Be(0); // already assigned

        // Still only one assignment
        var count = await _db.ComponentSystemAssignments
            .CountAsync(a => a.SystemComponentId == orgComp.Id);
        count.Should().Be(1);
    }
}

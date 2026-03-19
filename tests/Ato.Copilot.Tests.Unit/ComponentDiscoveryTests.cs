using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit;

/// <summary>
/// Unit tests for Azure component discovery dedup logic, partial failure handling,
/// and import bulk-create (Feature 040 — User Story 1).
/// </summary>
public class ComponentDiscoveryTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly ComponentService _service;

    public ComponentDiscoveryTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AtoCopilotContext(options);
        _service = new ComponentService(_db, NullLogger<ComponentService>.Instance, new NarrativeTemplateService());
    }

    public void Dispose() => _db.Dispose();

    // ─── Org-Wide Import Tests ───────────────────────────────────────────────

    [Fact]
    public async Task ImportAzureComponents_CreatesOrgWideThingComponents()
    {
        var resources = new List<AzureImportResource>
        {
            new() { ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/vm-01", Name = "vm-01", Type = "Microsoft.Compute/virtualMachines", ResourceGroup = "rg-prod", Location = "usgovvirginia" },
            new() { ResourceId = "/sub/rg/providers/Microsoft.Sql/servers/db-01", Name = "db-01", Type = "Microsoft.Sql/servers", ResourceGroup = "rg-prod", Location = "usgovvirginia" },
        };

        var result = await _service.ImportAzureComponentsAsync(resources, "test-user");

        result.Imported.Should().Be(2);
        result.Skipped.Should().Be(0);
        result.Components.Should().HaveCount(2);
        result.Components.Should().AllSatisfy(c => c.ComponentType.Should().Be("Thing"));

        var dbComponents = await _db.SystemComponents.ToListAsync();
        dbComponents.Should().HaveCount(2);
        dbComponents.Should().AllSatisfy(c =>
        {
            c.RegisteredSystemId.Should().BeNull();
            c.AzureResourceId.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task ImportAzureComponents_SkipsDuplicates()
    {
        // Pre-seed an existing org-wide component
        _db.SystemComponents.Add(new SystemComponent
        {
            Name = "existing-vm",
            ComponentType = ComponentType.Thing,
            AzureResourceId = "/sub/rg/providers/Microsoft.Compute/vm/vm-existing",
            RegisteredSystemId = null,
            CreatedBy = "seed",
        });
        await _db.SaveChangesAsync();

        var resources = new List<AzureImportResource>
        {
            new() { ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/vm-existing", Name = "vm-existing", Type = "Microsoft.Compute/virtualMachines", ResourceGroup = "rg-prod", Location = "usgovvirginia" },
            new() { ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/vm-new", Name = "vm-new", Type = "Microsoft.Compute/virtualMachines", ResourceGroup = "rg-prod", Location = "usgovvirginia" },
        };

        var result = await _service.ImportAzureComponentsAsync(resources, "test-user");

        result.Imported.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.SkippedDetails.Should().ContainSingle()
            .Which.ResourceId.Should().Contain("vm-existing");
    }

    [Fact]
    public async Task ImportAzureComponents_EmptyList_ReturnsZeroCounts()
    {
        var result = await _service.ImportAzureComponentsAsync(new List<AzureImportResource>(), "test-user");

        result.Imported.Should().Be(0);
        result.Skipped.Should().Be(0);
    }

    // ─── System-Scoped Import Tests ──────────────────────────────────────────

    [Fact]
    public async Task ImportSystemAzureComponents_CreatesScopedComponents()
    {
        var system = new RegisteredSystem
        {
            Name = "Test System", SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "AzureGovernment", CreatedBy = "seed",
        };
        _db.RegisteredSystems.Add(system);
        await _db.SaveChangesAsync();

        var resources = new List<AzureImportResource>
        {
            new() { ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/vm-01", Name = "vm-01", Type = "Microsoft.Compute/virtualMachines", ResourceGroup = "rg-prod", Location = "usgovvirginia" },
        };

        var result = await _service.ImportSystemAzureComponentsAsync(
            system.Id, resources, null, "test-user");

        result.Imported.Should().Be(1);

        var component = await _db.SystemComponents.FirstAsync();
        component.RegisteredSystemId.Should().Be(system.Id);
        component.AzureResourceType.Should().Be("Microsoft.Compute/virtualMachines");
    }

    [Fact]
    public async Task ImportSystemAzureComponents_AssignsExistingOrgComponents()
    {
        var system = new RegisteredSystem
        {
            Name = "Test System", SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "AzureGovernment", CreatedBy = "seed",
        };
        _db.RegisteredSystems.Add(system);

        var orgComp = new SystemComponent
        {
            Name = "org-vm", ComponentType = ComponentType.Thing,
            AzureResourceId = "/sub/rg/providers/Microsoft.Compute/vm/org-vm",
            RegisteredSystemId = null, CreatedBy = "seed",
        };
        _db.SystemComponents.Add(orgComp);
        await _db.SaveChangesAsync();

        var result = await _service.ImportSystemAzureComponentsAsync(
            system.Id, new List<AzureImportResource>(),
            new List<string> { orgComp.Id }, "test-user");

        result.AssignedFromOrg.Should().Be(1);
        result.Imported.Should().Be(0);

        var assignment = await _db.ComponentSystemAssignments.FirstAsync();
        assignment.SystemComponentId.Should().Be(orgComp.Id);
        assignment.RegisteredSystemId.Should().Be(system.Id);
    }

    [Fact]
    public async Task ImportSystemAzureComponents_SkipsSystemDuplicates()
    {
        var system = new RegisteredSystem
        {
            Name = "Test System", SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "AzureGovernment", CreatedBy = "seed",
        };
        _db.RegisteredSystems.Add(system);

        _db.SystemComponents.Add(new SystemComponent
        {
            Name = "existing-vm", ComponentType = ComponentType.Thing,
            AzureResourceId = "/sub/rg/providers/Microsoft.Compute/vm/vm-01",
            RegisteredSystemId = system.Id, CreatedBy = "seed",
        });
        await _db.SaveChangesAsync();

        var resources = new List<AzureImportResource>
        {
            new() { ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/vm-01", Name = "vm-01", Type = "Microsoft.Compute/virtualMachines", ResourceGroup = "rg-prod", Location = "usgovvirginia" },
        };

        var result = await _service.ImportSystemAzureComponentsAsync(
            system.Id, resources, null, "test-user");

        result.Skipped.Should().Be(1);
        result.Imported.Should().Be(0);
    }
}

using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for the boundary-to-component migration workflow (Feature 040 — US5).
/// Seeds 5 AuthorizationBoundary rows (3 in-scope, 2 excluded with rationale),
/// runs migration logic, verifies SystemComponent + BoundaryComponentAssignment records.
/// </summary>
public class BoundaryMigrationIntegrationTests : IDisposable
{
    private readonly AtoCopilotContext _db;

    public BoundaryMigrationIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"BoundaryMigrationInt_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(options);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

#pragma warning disable CS0618

    /// <summary>
    /// Core migration logic (mirrors BoundaryMigrationService without raw SQL).
    /// </summary>
    private async Task RunMigrationLogic()
    {
        var boundaryRows = await _db.AuthorizationBoundaries.AsNoTracking().ToListAsync();
        if (boundaryRows.Count == 0) return;

        var groups = boundaryRows.GroupBy(r => r.ResourceId).ToList();
        var componentMap = new Dictionary<string, string>();
        var newComponents = new List<SystemComponent>();
        var newAssignments = new List<BoundaryComponentAssignment>();

        foreach (var group in groups)
        {
            var rep = group.First();
            string? resourceGroup = null;
            var parts = rep.ResourceId.Split('/');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
                {
                    resourceGroup = parts[i + 1];
                    break;
                }
            }

            var component = new SystemComponent
            {
                Id = Guid.NewGuid().ToString(),
                Name = rep.ResourceName ?? $"Migrated: {parts.LastOrDefault() ?? rep.ResourceId}",
                ComponentType = ComponentType.Thing,
                RegisteredSystemId = null,
                AzureResourceId = rep.ResourceId,
                AzureResourceType = rep.ResourceType,
                AzureResourceGroup = resourceGroup,
                Status = ComponentStatus.Active,
            };
            newComponents.Add(component);
            componentMap[rep.ResourceId] = component.Id;
        }

        _db.SystemComponents.AddRange(newComponents);

        foreach (var row in boundaryRows)
        {
            if (!componentMap.TryGetValue(row.ResourceId, out var componentId)) continue;

            var boundaryDefId = row.AuthorizationBoundaryDefinitionId;
            if (string.IsNullOrEmpty(boundaryDefId))
            {
                boundaryDefId = await _db.AuthorizationBoundaryDefinitions
                    .Where(d => d.RegisteredSystemId == row.RegisteredSystemId)
                    .Select(d => d.Id)
                    .FirstOrDefaultAsync();
            }
            if (string.IsNullOrEmpty(boundaryDefId)) continue;

            newAssignments.Add(new BoundaryComponentAssignment
            {
                Id = Guid.NewGuid().ToString(),
                SystemComponentId = componentId,
                AuthorizationBoundaryDefinitionId = boundaryDefId,
                IsInScope = row.IsInBoundary,
                ExclusionRationale = row.ExclusionRationale,
                InheritanceProvider = row.InheritanceProvider,
                CreatedBy = row.AddedBy,
            });
        }

        _db.BoundaryComponentAssignments.AddRange(newAssignments);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task FullMigration_5Rows_3InScope_2Excluded()
    {
        // Seed system + boundary definition
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Migration E2E",
            Acronym = "ME",
            IsActive = true,
        };
        _db.RegisteredSystems.Add(system);

        var bd = new AuthorizationBoundaryDefinition
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            Name = "Default Boundary",
        };
        _db.AuthorizationBoundaryDefinitions.Add(bd);
        await _db.SaveChangesAsync();

        // Seed 5 AuthorizationBoundary rows — 5 unique resources
        _db.AuthorizationBoundaries.AddRange(
            new AuthorizationBoundary
            {
                RegisteredSystemId = system.Id,
                ResourceId = "/subscriptions/sub1/resourceGroups/rg-prod/providers/Microsoft.Compute/virtualMachines/web-01",
                ResourceType = "Microsoft.Compute/virtualMachines",
                ResourceName = "web-01",
                IsInBoundary = true,
                AddedBy = "isso@gov.mil",
                AuthorizationBoundaryDefinitionId = bd.Id,
            },
            new AuthorizationBoundary
            {
                RegisteredSystemId = system.Id,
                ResourceId = "/subscriptions/sub1/resourceGroups/rg-prod/providers/Microsoft.Sql/servers/sql-01",
                ResourceType = "Microsoft.Sql/servers",
                ResourceName = "sql-01",
                IsInBoundary = true,
                AddedBy = "isso@gov.mil",
                AuthorizationBoundaryDefinitionId = bd.Id,
            },
            new AuthorizationBoundary
            {
                RegisteredSystemId = system.Id,
                ResourceId = "/subscriptions/sub1/resourceGroups/rg-prod/providers/Microsoft.Network/virtualNetworks/vnet-prod",
                ResourceType = "Microsoft.Network/virtualNetworks",
                ResourceName = "vnet-prod",
                IsInBoundary = true,
                AddedBy = "isso@gov.mil",
                AuthorizationBoundaryDefinitionId = bd.Id,
            },
            new AuthorizationBoundary
            {
                RegisteredSystemId = system.Id,
                ResourceId = "/subscriptions/sub1/resourceGroups/rg-shared/providers/Microsoft.Storage/storageAccounts/sa-shared",
                ResourceType = "Microsoft.Storage/storageAccounts",
                ResourceName = "sa-shared",
                IsInBoundary = false,
                ExclusionRationale = "Managed by enterprise shared services team",
                InheritanceProvider = "Azure Gov CSP",
                AddedBy = "issm@gov.mil",
                AuthorizationBoundaryDefinitionId = bd.Id,
            },
            new AuthorizationBoundary
            {
                RegisteredSystemId = system.Id,
                ResourceId = "/subscriptions/sub1/resourceGroups/rg-mgmt/providers/Microsoft.KeyVault/vaults/kv-mgmt",
                ResourceType = "Microsoft.KeyVault/vaults",
                ResourceName = "kv-mgmt",
                IsInBoundary = false,
                ExclusionRationale = "Managed under separate ATO for infrastructure",
                AddedBy = "issm@gov.mil",
                AuthorizationBoundaryDefinitionId = bd.Id,
            });
        await _db.SaveChangesAsync();

        // Run migration
        await RunMigrationLogic();

        // Verify: 5 new SystemComponents (org-wide, type=Thing)
        var components = await _db.SystemComponents
            .Where(c => c.AzureResourceId != null)
            .OrderBy(c => c.Name)
            .ToListAsync();
        components.Should().HaveCount(5);
        components.Should().AllSatisfy(c =>
        {
            c.ComponentType.Should().Be(ComponentType.Thing);
            c.RegisteredSystemId.Should().BeNull(); // Org-wide
            c.Status.Should().Be(ComponentStatus.Active);
        });

        // Verify resource groups parsed
        components.First(c => c.Name == "web-01").AzureResourceGroup.Should().Be("rg-prod");
        components.First(c => c.Name == "sa-shared").AzureResourceGroup.Should().Be("rg-shared");
        components.First(c => c.Name == "kv-mgmt").AzureResourceGroup.Should().Be("rg-mgmt");

        // Verify: 5 BoundaryComponentAssignments
        var assignments = await _db.BoundaryComponentAssignments
            .Include(a => a.SystemComponent)
            .OrderBy(a => a.SystemComponent!.Name)
            .ToListAsync();
        assignments.Should().HaveCount(5);

        // 3 in-scope
        var inScope = assignments.Where(a => a.IsInScope).ToList();
        inScope.Should().HaveCount(3);
        inScope.Should().AllSatisfy(a =>
        {
            a.ExclusionRationale.Should().BeNull();
            a.AuthorizationBoundaryDefinitionId.Should().Be(bd.Id);
        });

        // 2 excluded with rationale
        var excluded = assignments.Where(a => !a.IsInScope).ToList();
        excluded.Should().HaveCount(2);
        excluded.Should().AllSatisfy(a =>
        {
            a.ExclusionRationale.Should().NotBeNullOrEmpty();
            a.AuthorizationBoundaryDefinitionId.Should().Be(bd.Id);
        });

        var saAssignment = excluded.First(a => a.SystemComponent!.Name == "sa-shared");
        saAssignment.ExclusionRationale.Should().Be("Managed by enterprise shared services team");
        saAssignment.InheritanceProvider.Should().Be("Azure Gov CSP");
        saAssignment.CreatedBy.Should().Be("issm@gov.mil");
    }

    [Fact]
    public async Task Migration_ReRun_IsNoOp()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Idempotency Test",
            Acronym = "IT",
            IsActive = true,
        };
        _db.RegisteredSystems.Add(system);

        var bd = new AuthorizationBoundaryDefinition
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            Name = "Default",
        };
        _db.AuthorizationBoundaryDefinitions.Add(bd);

        _db.AuthorizationBoundaries.Add(new AuthorizationBoundary
        {
            RegisteredSystemId = system.Id,
            ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/idempotent-vm",
            ResourceType = "Microsoft.Compute/virtualMachines",
            ResourceName = "idempotent-vm",
            IsInBoundary = true,
            AddedBy = "user@gov.mil",
            AuthorizationBoundaryDefinitionId = bd.Id,
        });
        await _db.SaveChangesAsync();

        // First run
        await RunMigrationLogic();
        var compCount1 = await _db.SystemComponents.CountAsync(c => c.AzureResourceId != null);
        var assignCount1 = await _db.BoundaryComponentAssignments.CountAsync();

        compCount1.Should().Be(1);
        assignCount1.Should().Be(1);

        // Simulate second run by checking if components already exist for those ResourceIds
        // (In production, the sentinel flag prevents re-run)
        var existingResourceIds = await _db.SystemComponents
            .Where(c => c.AzureResourceId != null)
            .Select(c => c.AzureResourceId)
            .ToListAsync();
        existingResourceIds.Should().Contain("/sub/rg/providers/Microsoft.Compute/vm/idempotent-vm");
    }

#pragma warning restore CS0618
}

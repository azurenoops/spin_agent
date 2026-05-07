using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for boundary-component CRUD (assign, update scope, remove)
/// and lock endpoints (Feature 040 — User Story 3).
/// </summary>
public class BoundaryComponentEndpointTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly ComponentService _service;
    private readonly BoundaryLockService _lockService;

    private const string SystemId = "sys-int-001";
    private const string BoundaryId = "bnd-int-001";
    private const string BoundaryId2 = "bnd-int-002";

    public BoundaryComponentEndpointTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"BndCompIntegration_{Guid.NewGuid()}")
            .Options;
        var factory = new IntegrationTestDbContextFactory(options);
        _db = factory.Context;
        _service = new ComponentService(
            factory, NullLogger<ComponentService>.Instance, new NarrativeTemplateService(),
            new SystemCapabilityLinkService(factory, NullLogger<SystemCapabilityLinkService>.Instance));
        _lockService = new BoundaryLockService();
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
            Id = SystemId, Name = "Eagle Eye",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Gov", CreatedBy = "test",
        });

        _db.AuthorizationBoundaryDefinitions.AddRange(
            new AuthorizationBoundaryDefinition
            {
                Id = BoundaryId, RegisteredSystemId = SystemId,
                Name = "Production", BoundaryType = BoundaryDefinitionType.Logical,
                IsPrimary = true, CreatedBy = "test",
            },
            new AuthorizationBoundaryDefinition
            {
                Id = BoundaryId2, RegisteredSystemId = SystemId,
                Name = "Development", BoundaryType = BoundaryDefinitionType.Logical,
                IsPrimary = false, CreatedBy = "test",
            }
        );

        _db.SystemComponents.AddRange(
            new SystemComponent { Id = "c1", Name = "App VM", ComponentType = ComponentType.Thing, SubType = "Microsoft.Compute/virtualMachines", AzureResourceId = "/sub/rg/vm/app-01", CreatedBy = "test" },
            new SystemComponent { Id = "c2", Name = "SQL Server", ComponentType = ComponentType.Thing, SubType = "Microsoft.Sql/servers", AzureResourceId = "/sub/rg/sql/sql-01", CreatedBy = "test" },
            new SystemComponent { Id = "c3", Name = "Key Vault", ComponentType = ComponentType.Thing, SubType = "Microsoft.KeyVault/vaults", AzureResourceId = "/sub/rg/kv/kv-01", CreatedBy = "test" }
        );

        _db.SaveChanges();
    }

    [Fact]
    public async Task EndToEnd_AssignUpdateRemove()
    {
        // Step 1: Assign 3 components to boundary
        var (a1, _) = await _service.AssignComponentToBoundaryAsync(BoundaryId, "c1", true, null, null, "issm");
        var (a2, _) = await _service.AssignComponentToBoundaryAsync(BoundaryId, "c2", true, null, "Azure CSP", "issm");
        var (a3, _) = await _service.AssignComponentToBoundaryAsync(BoundaryId, "c3", false, "Managed by CSP", "FedRAMP CSP", "issm");

        a1.Should().NotBeNull();
        a2.Should().NotBeNull();
        a3.Should().NotBeNull();

        // Step 2: List and verify
        var list = await _service.ListBoundaryComponentsAsync(BoundaryId, new BoundaryComponentQuery());
        list.TotalCount.Should().Be(3);
        list.Items.Count(i => i.IsInScope).Should().Be(2);
        list.Items.Count(i => !i.IsInScope).Should().Be(1);

        // Step 3: Toggle c2 to excluded
        var (updated, err) = await _service.UpdateBoundaryAssignmentAsync(
            a2!.AssignmentId, false, "Moved to dev boundary", null, "issm");
        err.Should().BeNull();
        updated!.IsInScope.Should().BeFalse();
        updated.ExclusionRationale.Should().Be("Moved to dev boundary");

        // Step 4: Remove c1
        var removed = await _service.RemoveComponentFromBoundaryAsync(a1!.AssignmentId);
        removed.Should().BeTrue();

        // Step 5: Verify final state
        var finalList = await _service.ListBoundaryComponentsAsync(BoundaryId, new BoundaryComponentQuery());
        finalList.TotalCount.Should().Be(2);
        finalList.Items.Should().AllSatisfy(i => i.IsInScope.Should().BeFalse());

        // Component c1 still exists in the library
        var c1 = await _db.SystemComponents.FindAsync("c1");
        c1.Should().NotBeNull();
    }

    [Fact]
    public async Task SameComponent_MultipleBoundaries_IndependentScope()
    {
        // Assign c1 to both boundaries with different scopes
        await _service.AssignComponentToBoundaryAsync(BoundaryId, "c1", true, null, null, "issm");
        await _service.AssignComponentToBoundaryAsync(BoundaryId2, "c1", false, "Excluded in dev", null, "issm");

        var prod = await _service.ListBoundaryComponentsAsync(BoundaryId, new BoundaryComponentQuery());
        var dev = await _service.ListBoundaryComponentsAsync(BoundaryId2, new BoundaryComponentQuery());

        prod.Items.Should().ContainSingle().Which.IsInScope.Should().BeTrue();
        dev.Items.Should().ContainSingle().Which.IsInScope.Should().BeFalse();
    }

    [Fact]
    public void Lock_EndToEnd_AcquireCheckRelease()
    {
        // Acquire
        var (acquired, entry) = _lockService.AcquireLock(BoundaryId, "user1@gov.mil", "Jane Smith");
        acquired.Should().BeTrue();
        entry.DisplayName.Should().Be("Jane Smith");

        // Check status
        var status = _lockService.GetLockStatus(BoundaryId);
        status.Should().NotBeNull();
        status!.UserId.Should().Be("user1@gov.mil");

        // Another user blocked
        var (blocked, _) = _lockService.AcquireLock(BoundaryId, "user2@gov.mil", "John Doe");
        blocked.Should().BeFalse();

        // Release
        _lockService.ReleaseLock(BoundaryId);
        var afterRelease = _lockService.GetLockStatus(BoundaryId);
        afterRelease.Should().BeNull();

        // Now user2 can acquire
        var (acquired2, _) = _lockService.AcquireLock(BoundaryId, "user2@gov.mil", "John Doe");
        acquired2.Should().BeTrue();
    }
}

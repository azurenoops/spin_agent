using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit;

/// <summary>
/// Unit tests for boundary component assignment: duplicate prevention, rationale validation,
/// scope toggle, lock acquire/release/expiry (Feature 040 — User Story 3).
/// </summary>
public class BoundaryComponentAssignmentTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly ComponentService _service;
    private readonly BoundaryLockService _lockService;

    private const string BoundaryId = "bnd-test-001";
    private const string BoundaryId2 = "bnd-test-002";
    private const string SystemId = "sys-test-001";

    public BoundaryComponentAssignmentTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AtoCopilotContext(options);
        _service = new ComponentService(_db, NullLogger<ComponentService>.Instance, new NarrativeTemplateService(), new SystemCapabilityLinkService(_db, NullLogger<SystemCapabilityLinkService>.Instance));
        _lockService = new BoundaryLockService();
        SeedData();
    }

    public void Dispose() => _db.Dispose();

    private void SeedData()
    {
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = SystemId, Name = "Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "AzureGovernment", CreatedBy = "test",
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

        _db.SystemComponents.Add(new SystemComponent
        {
            Id = "comp-001", Name = "SQL Database",
            ComponentType = ComponentType.Thing, SubType = "Azure SQL",
            AzureResourceId = "/subscriptions/sub/rg/providers/Microsoft.Sql/servers/sql-01",
            RegisteredSystemId = null, CreatedBy = "test",
        });

        _db.SaveChanges();
    }

    // ─── Assignment Tests ────────────────────────────────────────────────────

    [Fact]
    public async Task Assign_InScope_Succeeds()
    {
        var (dto, error) = await _service.AssignComponentToBoundaryAsync(
            BoundaryId, "comp-001", true, null, null, "test-user");

        error.Should().BeNull();
        dto.Should().NotBeNull();
        dto!.IsInScope.Should().BeTrue();
        dto.ComponentName.Should().Be("SQL Database");
        dto.ExclusionRationale.Should().BeNull();
    }

    [Fact]
    public async Task Assign_Excluded_WithRationale_Succeeds()
    {
        var (dto, error) = await _service.AssignComponentToBoundaryAsync(
            BoundaryId, "comp-001", false, "Managed by external CSP", "Azure CSP", "test-user");

        error.Should().BeNull();
        dto!.IsInScope.Should().BeFalse();
        dto.ExclusionRationale.Should().Be("Managed by external CSP");
        dto.InheritanceProvider.Should().Be("Azure CSP");
    }

    [Fact]
    public async Task Assign_Excluded_WithoutRationale_Fails()
    {
        var (dto, error) = await _service.AssignComponentToBoundaryAsync(
            BoundaryId, "comp-001", false, null, null, "test-user");

        error.Should().Be("RATIONALE_REQUIRED");
        dto.Should().BeNull();
    }

    [Fact]
    public async Task Assign_Duplicate_Returns409()
    {
        await _service.AssignComponentToBoundaryAsync(
            BoundaryId, "comp-001", true, null, null, "test-user");

        var (dto, error) = await _service.AssignComponentToBoundaryAsync(
            BoundaryId, "comp-001", true, null, null, "test-user");

        error.Should().Be("DUPLICATE_ASSIGNMENT");
        dto.Should().BeNull();
    }

    [Fact]
    public async Task Assign_SameComponent_DifferentBoundaries_Succeeds()
    {
        var (dto1, err1) = await _service.AssignComponentToBoundaryAsync(
            BoundaryId, "comp-001", true, null, null, "test-user");
        var (dto2, err2) = await _service.AssignComponentToBoundaryAsync(
            BoundaryId2, "comp-001", false, "Dev excluded", null, "test-user");

        err1.Should().BeNull();
        err2.Should().BeNull();
        dto1!.IsInScope.Should().BeTrue();
        dto2!.IsInScope.Should().BeFalse();
    }

    // ─── Update Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ToggleToExcluded_RequiresRationale()
    {
        var (initial, _) = await _service.AssignComponentToBoundaryAsync(
            BoundaryId, "comp-001", true, null, null, "test-user");

        var (dto, error) = await _service.UpdateBoundaryAssignmentAsync(
            initial!.AssignmentId, false, null, null, "test-user");

        error.Should().Be("RATIONALE_REQUIRED");
    }

    [Fact]
    public async Task Update_ToggleToExcluded_WithRationale_Succeeds()
    {
        var (initial, _) = await _service.AssignComponentToBoundaryAsync(
            BoundaryId, "comp-001", true, null, null, "test-user");

        var (dto, error) = await _service.UpdateBoundaryAssignmentAsync(
            initial!.AssignmentId, false, "Now excluded per policy", "Azure CSP", "test-user");

        error.Should().BeNull();
        dto!.IsInScope.Should().BeFalse();
        dto.ExclusionRationale.Should().Be("Now excluded per policy");
    }

    // ─── Remove Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_DeletesAssignment_KeepsComponent()
    {
        var (initial, _) = await _service.AssignComponentToBoundaryAsync(
            BoundaryId, "comp-001", true, null, null, "test-user");

        var removed = await _service.RemoveComponentFromBoundaryAsync(initial!.AssignmentId);

        removed.Should().BeTrue();

        // Component still exists
        var comp = await _db.SystemComponents.FindAsync("comp-001");
        comp.Should().NotBeNull();

        // No assignments remain
        var count = await _db.BoundaryComponentAssignments.CountAsync();
        count.Should().Be(0);
    }

    // ─── List Tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task List_FiltersByScope()
    {
        await _service.AssignComponentToBoundaryAsync(BoundaryId, "comp-001", true, null, null, "test-user");

        // Add another component
        _db.SystemComponents.Add(new SystemComponent
        {
            Id = "comp-002", Name = "Key Vault",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = null, CreatedBy = "test",
        });
        await _db.SaveChangesAsync();
        await _service.AssignComponentToBoundaryAsync(BoundaryId, "comp-002", false, "Excluded reason", null, "test-user");

        var inScope = await _service.ListBoundaryComponentsAsync(BoundaryId,
            new BoundaryComponentQuery { ScopeFilter = "InScope" });
        var excluded = await _service.ListBoundaryComponentsAsync(BoundaryId,
            new BoundaryComponentQuery { ScopeFilter = "Excluded" });

        inScope.Items.Should().HaveCount(1);
        inScope.Items[0].ComponentName.Should().Be("SQL Database");
        excluded.Items.Should().HaveCount(1);
        excluded.Items[0].ComponentName.Should().Be("Key Vault");
    }

    // ─── Lock Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Lock_Acquire_Succeeds()
    {
        var (acquired, entry) = _lockService.AcquireLock(BoundaryId, "user1", "Jane");

        acquired.Should().BeTrue();
        entry.UserId.Should().Be("user1");
        entry.DisplayName.Should().Be("Jane");
    }

    [Fact]
    public void Lock_SecondUser_Blocked()
    {
        _lockService.AcquireLock(BoundaryId, "user1", "Jane");
        var (acquired, entry) = _lockService.AcquireLock(BoundaryId, "user2", "John");

        acquired.Should().BeFalse();
        entry.DisplayName.Should().Be("Jane");
    }

    [Fact]
    public void Lock_SameUser_Reacquires()
    {
        _lockService.AcquireLock(BoundaryId, "user1", "Jane");
        var (acquired, _) = _lockService.AcquireLock(BoundaryId, "user1", "Jane");

        acquired.Should().BeTrue();
    }

    [Fact]
    public void Lock_Release_AllowsOthers()
    {
        _lockService.AcquireLock(BoundaryId, "user1", "Jane");
        _lockService.ReleaseLock(BoundaryId);

        var (acquired, _) = _lockService.AcquireLock(BoundaryId, "user2", "John");
        acquired.Should().BeTrue();
    }

    [Fact]
    public void Lock_Status_ReturnsNull_WhenNoLock()
    {
        var status = _lockService.GetLockStatus(BoundaryId);
        status.Should().BeNull();
    }
}

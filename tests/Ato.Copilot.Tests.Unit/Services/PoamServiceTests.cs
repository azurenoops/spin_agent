using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Services;

public class PoamServiceTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly PoamService _sut;

    private const string SystemId = "sys-poam-001";
    private const string CompId1 = "comp-001";
    private const string CompId2 = "comp-002";

    public PoamServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"PoamTests_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(dbOptions);
        var logger = Mock.Of<ILogger<PoamService>>();
        _sut = new PoamService(_db, logger);

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
            Name = "POA&M Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true,
        });

        _db.SystemComponents.Add(new SystemComponent
        {
            Id = CompId1,
            RegisteredSystemId = SystemId,
            Name = "Web Server",
            ComponentType = ComponentType.Thing,
            Status = ComponentStatus.Active,
            CreatedBy = "test",
        });

        _db.SystemComponents.Add(new SystemComponent
        {
            Id = CompId2,
            RegisteredSystemId = SystemId,
            Name = "Database Server",
            ComponentType = ComponentType.Thing,
            Status = ComponentStatus.Active,
            CreatedBy = "test",
        });

        _db.SaveChanges();
    }

    // ─── Create Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithValidData_CreatesPoamItem()
    {
        var result = await _sut.CreateAsync(
            SystemId, "Weak password policy", "STIG", "IA-5",
            CatSeverity.CatII, "John Doe", DateTime.UtcNow.AddDays(30),
            createdBy: "test-user");

        result.Should().NotBeNull();
        result.Id.Should().NotBeNullOrEmpty();
        result.Weakness.Should().Be("Weak password policy");
        result.SecurityControlNumber.Should().Be("IA-5");
        result.CatSeverity.Should().Be(CatSeverity.CatII);
        result.Status.Should().Be(PoamStatus.Ongoing);
        result.CreatedBy.Should().Be("test-user");
    }

    [Fact]
    public async Task CreateAsync_WithComponentIds_LinksComponents()
    {
        var result = await _sut.CreateAsync(
            SystemId, "Missing MFA", "Assessment", "IA-2",
            CatSeverity.CatI, "Jane Doe", DateTime.UtcNow.AddDays(30),
            componentIds: new[] { CompId1, CompId2 },
            createdBy: "test-user");

        var links = await _db.PoamComponentLinks
            .Where(cl => cl.PoamItemId == result.Id)
            .ToListAsync();

        links.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateAsync_WithMilestones_CreatesMilestones()
    {
        var result = await _sut.CreateAsync(
            SystemId, "Unpatched software", "ACAS", "SI-2",
            CatSeverity.CatII, "John Doe", DateTime.UtcNow.AddDays(60),
            milestones: new[]
            {
                ("Plan remediation", DateTime.UtcNow.AddDays(15)),
                ("Apply patches", DateTime.UtcNow.AddDays(30)),
                ("Validate fix", DateTime.UtcNow.AddDays(45)),
            },
            createdBy: "test-user");

        result.Milestones.Should().HaveCount(3);
        result.Milestones.Should().BeInAscendingOrder(m => m.Sequence);
    }

    [Fact]
    public async Task CreateAsync_CreatesHistoryEntry()
    {
        var result = await _sut.CreateAsync(
            SystemId, "Test weakness", "Manual", "AC-1",
            CatSeverity.CatIII, "Test POC", DateTime.UtcNow.AddDays(30),
            createdBy: "test-user");

        var history = await _db.PoamHistoryEntries
            .Where(h => h.PoamItemId == result.Id)
            .ToListAsync();

        history.Should().HaveCount(1);
        history.First().EventType.Should().Be(PoamHistoryEventType.Created);
    }

    [Fact]
    public async Task CreateAsync_InvalidSystemId_Throws()
    {
        var act = () => _sut.CreateAsync(
            "nonexistent", "Test", "STIG", "AC-1",
            CatSeverity.CatI, "POC", DateTime.UtcNow.AddDays(30));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // ─── GetById Tests ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ExistingPoam_ReturnsWithIncludes()
    {
        var created = await _sut.CreateAsync(
            SystemId, "Test", "Manual", "AC-1",
            CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30),
            componentIds: new[] { CompId1 },
            milestones: new[] { ("Step 1", DateTime.UtcNow.AddDays(15)) });

        var result = await _sut.GetByIdAsync(created.Id);

        result.Should().NotBeNull();
        result!.ComponentLinks.Should().HaveCount(1);
        result.Milestones.Should().HaveCount(1);
        result.History.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        var result = await _sut.GetByIdAsync("nonexistent");
        result.Should().BeNull();
    }

    // ─── List Tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_Pagination_ReturnsCorrectPage()
    {
        // Create 5 items
        for (int i = 0; i < 5; i++)
        {
            await _sut.CreateAsync(
                SystemId, $"Weakness {i}", "Manual", $"AC-{i}",
                CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30 + i));
        }

        var (items, total) = await _sut.ListAsync(SystemId, page: 1, pageSize: 2);

        total.Should().Be(5);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAsync_StatusFilter_FiltersCorrectly()
    {
        await _sut.CreateAsync(SystemId, "Active", "Manual", "AC-1", CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var completed = await _sut.CreateAsync(SystemId, "Done", "Manual", "AC-2", CatSeverity.CatIII, "POC", DateTime.UtcNow.AddDays(30));
        completed.Status = PoamStatus.Completed;
        completed.ActualCompletionDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var (items, total) = await _sut.ListAsync(SystemId, statusFilter: PoamStatus.Ongoing);

        items.Should().AllSatisfy(p => p.Status.Should().Be(PoamStatus.Ongoing));
    }

    [Fact]
    public async Task ListAsync_SeverityFilter_FiltersCorrectly()
    {
        await _sut.CreateAsync(SystemId, "Critical", "STIG", "IA-5", CatSeverity.CatI, "POC", DateTime.UtcNow.AddDays(30));
        await _sut.CreateAsync(SystemId, "Medium", "STIG", "IA-6", CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var (items, _) = await _sut.ListAsync(SystemId, severityFilter: CatSeverity.CatI);

        items.Should().AllSatisfy(p => p.CatSeverity.Should().Be(CatSeverity.CatI));
    }

    [Fact]
    public async Task ListAsync_PageSizeClamped()
    {
        var (_, _) = await _sut.ListAsync(pageSize: 200);
        // Should not throw — pageSize clamped to 100 internally
    }

    // ─── Update Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WithCorrectRowVersion_Succeeds()
    {
        var created = await _sut.CreateAsync(
            SystemId, "Original", "Manual", "AC-1",
            CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var updated = await _sut.UpdateAsync(created.Id, created.RowVersion, poam =>
        {
            poam.Weakness = "Updated weakness";
        });

        updated.Weakness.Should().Be("Updated weakness");
        updated.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_WithStaleRowVersion_ThrowsConcurrencyConflict()
    {
        var created = await _sut.CreateAsync(
            SystemId, "Original", "Manual", "AC-1",
            CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var staleVersion = Guid.NewGuid();

        var act = () => _sut.UpdateAsync(created.Id, staleVersion, poam =>
        {
            poam.Weakness = "Should fail";
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CONCURRENCY*");
    }

    [Fact]
    public async Task UpdateAsync_NonExistent_Throws()
    {
        var act = () => _sut.UpdateAsync("nonexistent", Guid.NewGuid(), _ => { });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // ─── Delete Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingPoam_Removes()
    {
        var created = await _sut.CreateAsync(
            SystemId, "To delete", "Manual", "AC-1",
            CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        await _sut.DeleteAsync(created.Id);

        var result = await _db.PoamItems.FindAsync(created.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_Throws()
    {
        var act = () => _sut.DeleteAsync("nonexistent");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // ─── Metrics Tests ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetricsAsync_ReturnsCorrectCounts()
    {
        await _sut.CreateAsync(SystemId, "Open Cat I", "STIG", "IA-5", CatSeverity.CatI, "POC", DateTime.UtcNow.AddDays(30));
        await _sut.CreateAsync(SystemId, "Open Cat II", "STIG", "AC-2", CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));
        await _sut.CreateAsync(SystemId, "Overdue", "STIG", "AC-3", CatSeverity.CatI, "POC", DateTime.UtcNow.AddDays(-5));

        var metrics = await _sut.GetMetricsAsync(SystemId);

        metrics.TotalOpen.Should().Be(3);
        metrics.Overdue.Should().Be(1);
        metrics.CatICount.Should().Be(2);
        metrics.CatIICount.Should().Be(1);
    }

    // ─── Component Linkage Tests ─────────────────────────────────────────────

    [Fact]
    public async Task LinkComponentsAsync_LinksSuccessfully()
    {
        var poam = await _sut.CreateAsync(
            SystemId, "Test", "Manual", "AC-1",
            CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        await _sut.LinkComponentsAsync(poam.Id, new[] { CompId1, CompId2 });

        var links = await _db.PoamComponentLinks
            .Where(cl => cl.PoamItemId == poam.Id)
            .ToListAsync();

        links.Should().HaveCount(2);
    }

    [Fact]
    public async Task LinkComponentsAsync_DuplicateLink_SkipsGracefully()
    {
        var poam = await _sut.CreateAsync(
            SystemId, "Test", "Manual", "AC-1",
            CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30),
            componentIds: new[] { CompId1 });

        // Link same component again
        await _sut.LinkComponentsAsync(poam.Id, new[] { CompId1 });

        var links = await _db.PoamComponentLinks
            .Where(cl => cl.PoamItemId == poam.Id)
            .ToListAsync();

        links.Should().HaveCount(1); // No duplicate
    }

    [Fact]
    public async Task UnlinkComponentsAsync_RemovesLinks()
    {
        var poam = await _sut.CreateAsync(
            SystemId, "Test", "Manual", "AC-1",
            CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30),
            componentIds: new[] { CompId1, CompId2 });

        await _sut.UnlinkComponentsAsync(poam.Id, new[] { CompId1 });

        var links = await _db.PoamComponentLinks
            .Where(cl => cl.PoamItemId == poam.Id)
            .ToListAsync();

        links.Should().HaveCount(1);
        links.First().SystemComponentId.Should().Be(CompId2);
    }

    [Fact]
    public async Task GetPoamsByComponentAsync_ReturnsCorrectSummary()
    {
        await _sut.CreateAsync(
            SystemId, "Issue 1", "STIG", "AC-1",
            CatSeverity.CatI, "POC", DateTime.UtcNow.AddDays(30),
            componentIds: new[] { CompId1 });

        await _sut.CreateAsync(
            SystemId, "Issue 2", "STIG", "AC-2",
            CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(-5),
            componentIds: new[] { CompId1 });

        var summary = await _sut.GetPoamsByComponentAsync(CompId1);

        summary.TotalPoams.Should().Be(2);
        summary.OpenCount.Should().Be(2);
        summary.OverdueCount.Should().Be(1);
        summary.HighestSeverity.Should().Be(CatSeverity.CatI);
    }

    // ─── Lifecycle Transition Tests (T051) ──────────────────────────────────

    [Fact]
    public async Task UpdateStatusAsync_OngoingToDelayed_RequiresReasonAndDate()
    {
        var poam = await CreateOngoingPoam();

        var act = () => _sut.UpdateStatusAsync(
            poam.Id, PoamStatus.Delayed, poam.RowVersion, "user",
            delayReason: null, revisedDate: null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*POAM_DELAY_REASON_REQUIRED*");
    }

    [Fact]
    public async Task UpdateStatusAsync_OngoingToDelayed_WithReasonAndDate_Succeeds()
    {
        var poam = await CreateOngoingPoam();
        var revised = DateTime.UtcNow.AddDays(60);

        var result = await _sut.UpdateStatusAsync(
            poam.Id, PoamStatus.Delayed, poam.RowVersion, "user",
            delayReason: "Awaiting vendor patch", revisedDate: revised);

        result.Status.Should().Be(PoamStatus.Delayed);
        result.ScheduledCompletionDate.Should().BeCloseTo(revised, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UpdateStatusAsync_DelayedToOngoing_RequiresRevisedDate()
    {
        var poam = await CreateDelayedPoam();

        var act = () => _sut.UpdateStatusAsync(
            poam.Id, PoamStatus.Ongoing, poam.RowVersion, "user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*POAM_REVISED_DATE_REQUIRED*");
    }

    [Fact]
    public async Task UpdateStatusAsync_Resume_DelayedToOngoing_Succeeds()
    {
        var poam = await CreateDelayedPoam();
        var newDate = DateTime.UtcNow.AddDays(45);

        var result = await _sut.UpdateStatusAsync(
            poam.Id, PoamStatus.Ongoing, poam.RowVersion, "user",
            revisedDate: newDate);

        result.Status.Should().Be(PoamStatus.Ongoing);
        result.ScheduledCompletionDate.Should().BeCloseTo(newDate, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UpdateStatusAsync_OngoingToCompleted_SetsActualCompletionDate()
    {
        var poam = await CreateOngoingPoam();

        var result = await _sut.UpdateStatusAsync(
            poam.Id, PoamStatus.Completed, poam.RowVersion, "user");

        result.Status.Should().Be(PoamStatus.Completed);
        result.ActualCompletionDate.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_RiskAccepted_RequiresDeviationId()
    {
        var poam = await CreateOngoingPoam();

        var act = () => _sut.UpdateStatusAsync(
            poam.Id, PoamStatus.RiskAccepted, poam.RowVersion, "user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*POAM_DEVIATION_REQUIRED*");
    }

    [Fact]
    public async Task UpdateStatusAsync_RiskAccepted_WithDeviationId_Succeeds()
    {
        var poam = await CreateOngoingPoam();

        var result = await _sut.UpdateStatusAsync(
            poam.Id, PoamStatus.RiskAccepted, poam.RowVersion, "user",
            deviationId: "dev-001");

        result.Status.Should().Be(PoamStatus.RiskAccepted);
        result.DeviationId.Should().Be("dev-001");
    }

    [Fact]
    public async Task UpdateStatusAsync_CompletedToOngoing_IsInvalidTransition()
    {
        var poam = await CreateOngoingPoam();
        var completed = await _sut.UpdateStatusAsync(
            poam.Id, PoamStatus.Completed, poam.RowVersion, "user");

        var act = () => _sut.UpdateStatusAsync(
            completed.Id, PoamStatus.Ongoing, completed.RowVersion, "user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*POAM_INVALID_TRANSITION*");
    }

    [Fact]
    public async Task UpdateStatusAsync_ConcurrencyConflict_ThrowsError()
    {
        var poam = await CreateOngoingPoam();
        var staleRowVersion = Guid.NewGuid(); // Wrong rowVersion

        var act = () => _sut.UpdateStatusAsync(
            poam.Id, PoamStatus.Completed, staleRowVersion, "user");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CONCURRENCY*");
    }

    [Fact]
    public async Task UpdateStatusAsync_CreatesAuditHistoryEntry()
    {
        var poam = await CreateOngoingPoam();

        await _sut.UpdateStatusAsync(
            poam.Id, PoamStatus.Completed, poam.RowVersion, "test-user");

        var history = await _db.PoamHistoryEntries
            .Where(h => h.PoamItemId == poam.Id && h.EventType == PoamHistoryEventType.StatusChanged)
            .ToListAsync();

        history.Should().HaveCount(1);
        history.First().OldValue.Should().Be("Ongoing");
        history.First().NewValue.Should().Be("Completed");
        history.First().ActingUserId.Should().Be("test-user");
    }

    [Fact]
    public async Task BulkUpdateStatusAsync_PartialFailure_ReportsPerItem()
    {
        var p1 = await CreateOngoingPoam();
        var p2 = await CreateOngoingPoam();

        // Complete p1 first so it can't be completed again
        await _sut.UpdateStatusAsync(p1.Id, PoamStatus.Completed, p1.RowVersion, "user");

        var results = await _sut.BulkUpdateStatusAsync(
            new[] { p1.Id, p2.Id }, PoamStatus.Completed, "user");

        results.Should().HaveCount(2);
        results.First(r => r.PoamId == p1.Id).Success.Should().BeFalse(); // Already completed
        results.First(r => r.PoamId == p2.Id).Success.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateMilestoneAsync_MarkComplete_SetCompletedDate()
    {
        var poam = await _sut.CreateAsync(
            SystemId, "W", "S", "AC-1", CatSeverity.CatII, "POC",
            DateTime.UtcNow.AddDays(30),
            milestones: new[] { ("Milestone 1", DateTime.UtcNow.AddDays(10)) });

        var milestone = poam.Milestones.First();

        var result = await _sut.UpdateMilestoneAsync(
            poam.Id, milestone.Id, poam.RowVersion, "user", markComplete: true);

        result.Milestones.First().CompletedDate.Should().NotBeNull();
    }

    // ─── Helper Methods ─────────────────────────────────────────────────

    private async Task<PoamItem> CreateOngoingPoam()
    {
        return await _sut.CreateAsync(
            SystemId, "Test weakness", "STIG", "AC-1",
            CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30),
            createdBy: "test-user");
    }

    private async Task<PoamItem> CreateDelayedPoam()
    {
        var poam = await CreateOngoingPoam();
        return await _sut.UpdateStatusAsync(
            poam.Id, PoamStatus.Delayed, poam.RowVersion, "user",
            delayReason: "Resource unavailable", revisedDate: DateTime.UtcNow.AddDays(60));
    }
}

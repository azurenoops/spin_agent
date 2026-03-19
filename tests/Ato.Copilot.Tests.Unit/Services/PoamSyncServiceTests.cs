using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Services;

public class PoamSyncServiceTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly PoamService _poamService;
    private readonly PoamSyncService _sut;

    private const string SystemId = "sys-sync-001";
    private const string BoardId = "board-001";

    public PoamSyncServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"SyncTests_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(dbOptions);
        var poamLogger = Mock.Of<ILogger<PoamService>>();
        _poamService = new PoamService(_db, poamLogger);
        var syncLogger = Mock.Of<ILogger<PoamSyncService>>();
        _sut = new PoamSyncService(_db, _poamService, syncLogger);

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
            Name = "Sync Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true,
        });

        _db.RemediationBoards.Add(new RemediationBoard
        {
            Id = BoardId,
            SubscriptionId = SystemId,
            Name = "Test Board",
            Owner = "test",
        });

        _db.SaveChanges();
    }

    // ─── CreateTaskFromPoam Tests ────────────────────────────────────────────

    [Fact]
    public async Task CreateTaskFromPoamAsync_CreatesTaskWithMappedFields()
    {
        var poam = await _poamService.CreateAsync(
            SystemId, "Missing MFA on admin accounts", "STIG", "IA-2",
            CatSeverity.CatI, "Jane Smith", DateTime.UtcNow.AddDays(30),
            createdBy: "test-user");

        var task = await _sut.CreateTaskFromPoamAsync(poam.Id, BoardId, "test-user");

        task.Should().NotBeNull();
        task.Title.Should().Contain("IA-2");
        task.ControlId.Should().Be("IA-2");
        task.Severity.Should().Be(FindingSeverity.Critical); // CatI → Critical
        task.PoamItemId.Should().Be(poam.Id);
        task.AssigneeName.Should().Be("Jane Smith");
    }

    [Fact]
    public async Task CreateTaskFromPoamAsync_SetsBidirectionalFKs()
    {
        var poam = await _poamService.CreateAsync(
            SystemId, "Weakness", "STIG", "AC-1",
            CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var task = await _sut.CreateTaskFromPoamAsync(poam.Id, BoardId, "user");

        // Verify task.PoamItemId → poam.Id
        task.PoamItemId.Should().Be(poam.Id);

        // Verify poam.RemediationTaskId → task.Id
        var updatedPoam = await _db.PoamItems.FindAsync(poam.Id);
        updatedPoam!.RemediationTaskId.Should().Be(task.Id);
    }

    [Fact]
    public async Task CreateTaskFromPoamAsync_AlreadyLinked_Throws()
    {
        var poam = await _poamService.CreateAsync(
            SystemId, "W", "S", "AC-1", CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        await _sut.CreateTaskFromPoamAsync(poam.Id, BoardId, "user");

        // Trying to create again should fail
        var act = () => _sut.CreateTaskFromPoamAsync(poam.Id, BoardId, "user");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ALREADY_LINKED*");
    }

    // ─── Link Tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LinkAsync_SetsBidirectionalFKs()
    {
        var poam = await _poamService.CreateAsync(
            SystemId, "W", "S", "AC-1", CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var task = new RemediationTask
        {
            BoardId = BoardId,
            Title = "Test task",
            ControlId = "AC-1",
            CreatedBy = "test"
        };
        _db.RemediationTasks.Add(task);
        await _db.SaveChangesAsync();

        await _sut.LinkAsync(poam.Id, task.Id, "user");

        var updatedPoam = await _db.PoamItems.FindAsync(poam.Id);
        var updatedTask = await _db.RemediationTasks.FindAsync(task.Id);

        updatedPoam!.RemediationTaskId.Should().Be(task.Id);
        updatedTask!.PoamItemId.Should().Be(poam.Id);
    }

    [Fact]
    public async Task LinkAsync_AlreadyLinkedPoam_Throws()
    {
        var poam = await _poamService.CreateAsync(
            SystemId, "W", "S", "AC-1", CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var task1 = new RemediationTask { BoardId = BoardId, Title = "T1", ControlId = "AC-1", CreatedBy = "t" };
        var task2 = new RemediationTask { BoardId = BoardId, Title = "T2", ControlId = "AC-1", CreatedBy = "t" };
        _db.RemediationTasks.AddRange(task1, task2);
        await _db.SaveChangesAsync();

        await _sut.LinkAsync(poam.Id, task1.Id, "user");

        var act = () => _sut.LinkAsync(poam.Id, task2.Id, "user");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ALREADY_LINKED*");
    }

    // ─── Unlink Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task UnlinkAsync_ClearsBothFKs()
    {
        var poam = await _poamService.CreateAsync(
            SystemId, "W", "S", "AC-1", CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var task = await _sut.CreateTaskFromPoamAsync(poam.Id, BoardId, "user");

        await _sut.UnlinkAsync(poam.Id, "user");

        var updatedPoam = await _db.PoamItems.FindAsync(poam.Id);
        var updatedTask = await _db.RemediationTasks.FindAsync(task.Id);

        updatedPoam!.RemediationTaskId.Should().BeNull();
        updatedTask!.PoamItemId.Should().BeNull();
    }

    [Fact]
    public async Task UnlinkAsync_NotLinked_Throws()
    {
        var poam = await _poamService.CreateAsync(
            SystemId, "W", "S", "AC-1", CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var act = () => _sut.UnlinkAsync(poam.Id, "user");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NOT_LINKED*");
    }

    // ─── Cascade Tests ───────────────────────────────────────────────────────

    [Fact]
    public async Task CascadeStatusChangeAsync_FromPoam_UpdatesLinkedTask()
    {
        var poam = await _poamService.CreateAsync(
            SystemId, "W", "S", "AC-1", CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var task = await _sut.CreateTaskFromPoamAsync(poam.Id, BoardId, "user");

        await _sut.CascadeStatusChangeAsync(
            poam.Id, PoamStatus.Completed, CascadeOrigin.FromPoam, "user");

        var updatedTask = await _db.RemediationTasks.FindAsync(task.Id);
        updatedTask!.Status.Should().Be(Ato.Copilot.Core.Models.Kanban.TaskStatus.Done);
    }

    [Fact]
    public async Task CascadeStatusChangeAsync_FromTask_PreventsCascadeLoop()
    {
        var poam = await _poamService.CreateAsync(
            SystemId, "W", "S", "AC-1", CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var task = await _sut.CreateTaskFromPoamAsync(poam.Id, BoardId, "user");

        // When origin is Task, cascade should be skipped (loop prevention)
        await _sut.CascadeStatusChangeAsync(
            poam.Id, PoamStatus.Completed, CascadeOrigin.FromTask, "user");

        var unchangedTask = await _db.RemediationTasks.FindAsync(task.Id);
        unchangedTask!.Status.Should().Be(Ato.Copilot.Core.Models.Kanban.TaskStatus.Backlog); // Unchanged
    }

    [Fact]
    public async Task CascadeMetadataChangeAsync_UpdatesTaskDueDate()
    {
        var poam = await _poamService.CreateAsync(
            SystemId, "W", "S", "AC-1", CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var task = await _sut.CreateTaskFromPoamAsync(poam.Id, BoardId, "user");

        var newDueDate = DateTime.UtcNow.AddDays(60);
        await _sut.CascadeMetadataChangeAsync(
            poam.Id, newDueDate, null, CascadeOrigin.FromPoam, "user");

        var updatedTask = await _db.RemediationTasks.FindAsync(task.Id);
        updatedTask!.DueDate.Should().BeCloseTo(newDueDate, TimeSpan.FromSeconds(1));
    }

    // ─── History Entry Tests ─────────────────────────────────────────────────

    [Fact]
    public async Task LinkAsync_CreatesHistoryEntry()
    {
        var poam = await _poamService.CreateAsync(
            SystemId, "W", "S", "AC-1", CatSeverity.CatII, "POC", DateTime.UtcNow.AddDays(30));

        var task = new RemediationTask { BoardId = BoardId, Title = "T", ControlId = "AC-1", CreatedBy = "t" };
        _db.RemediationTasks.Add(task);
        await _db.SaveChangesAsync();

        await _sut.LinkAsync(poam.Id, task.Id, "test-user");

        var history = await _db.PoamHistoryEntries
            .Where(h => h.PoamItemId == poam.Id && h.EventType == PoamHistoryEventType.TaskLinked)
            .ToListAsync();

        history.Should().HaveCount(1);
        history.First().ActingUserId.Should().Be("test-user");
    }
}

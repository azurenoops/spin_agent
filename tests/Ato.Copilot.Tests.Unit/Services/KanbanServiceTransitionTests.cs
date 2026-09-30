using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.State.Abstractions;
using TaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for KanbanService.MoveTaskAsync: transition rules, RBAC, auto-assign,
/// comment requirements, and history logging.
/// </summary>
public class KanbanServiceTransitionTests : IDisposable
{
    private readonly AtoCopilotContext _context;
    private readonly KanbanService _service;
    private readonly Mock<INotificationService> _notificationMock = new();

    public KanbanServiceTransitionTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"KanbanTransition_{Guid.NewGuid()}")
            .Options;
        _context = new AtoCopilotContext(options);

        _service = new KanbanService(
            _context,
            Mock.Of<ILogger<KanbanService>>(),
            _notificationMock.Object,
            Mock.Of<IAgentStateManager>(),
            Mock.Of<IAtoComplianceEngine>(),
            Mock.Of<IRemediationEngine>());
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task MoveTask_BacklogToToDo_Succeeds()
    {
        var task = await SeedTask(TaskStatus.Backlog);

        var result = await _service.MoveTaskAsync(
            task.Id, TaskStatus.ToDo, "user1", "User One", ComplianceRoles.Administrator);

        result.Status.Should().Be(TaskStatus.ToDo);
    }

    [Fact]
    public async Task MoveTask_BacklogToInProgress_AutoAssignsIfUnassigned()
    {
        var task = await SeedTask(TaskStatus.Backlog, assigneeId: null);

        var result = await _service.MoveTaskAsync(
            task.Id, TaskStatus.InProgress, "user1", "User One", ComplianceRoles.Administrator);

        result.Status.Should().Be(TaskStatus.InProgress);
        result.AssigneeId.Should().Be("user1");
        result.AssigneeName.Should().Be("User One");
    }

    [Fact]
    public async Task MoveTask_ToBlocked_WithoutComment_Throws()
    {
        var task = await SeedTask(TaskStatus.InProgress, assigneeId: "user1");

        var act = () => _service.MoveTaskAsync(
            task.Id, TaskStatus.Blocked, "user1", "User One", ComplianceRoles.Administrator);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*BLOCKER_COMMENT_REQUIRED*");
    }

    [Fact]
    public async Task MoveTask_ToBlocked_WithComment_Succeeds()
    {
        var task = await SeedTask(TaskStatus.InProgress, assigneeId: "user1");

        var result = await _service.MoveTaskAsync(
            task.Id, TaskStatus.Blocked, "user1", "User One", ComplianceRoles.Administrator,
            comment: "Waiting on external dependency");

        result.Status.Should().Be(TaskStatus.Blocked);
    }

    [Fact]
    public async Task MoveTask_FromBlocked_WithoutResolutionComment_Throws()
    {
        var task = await SeedTask(TaskStatus.Blocked, assigneeId: "user1");

        var act = () => _service.MoveTaskAsync(
            task.Id, TaskStatus.InProgress, "user1", "User One", ComplianceRoles.Administrator);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RESOLUTION_COMMENT_REQUIRED*");
    }

    [Fact]
    public async Task MoveTask_FromBlocked_WithResolutionComment_Succeeds()
    {
        var task = await SeedTask(TaskStatus.Blocked, assigneeId: "user1");

        var result = await _service.MoveTaskAsync(
            task.Id, TaskStatus.InProgress, "user1", "User One", ComplianceRoles.Administrator,
            comment: "Dependency resolved");

        result.Status.Should().Be(TaskStatus.InProgress);
    }

    [Fact]
    public async Task MoveTask_InReviewToDone_WithSkipValidation_Succeeds_ForCO()
    {
        var task = await SeedTask(TaskStatus.InReview, assigneeId: "user1");

        var result = await _service.MoveTaskAsync(
            task.Id, TaskStatus.Done, "admin1", "Admin One", ComplianceRoles.Administrator,
            comment: "Explicit administrator verification exception", skipValidation: true);

        result.Status.Should().Be(TaskStatus.Done);
    }

    [Fact]
    public async Task MoveTask_InReviewToDone_WithoutSkipValidation_NonCO_Throws()
    {
        var task = await SeedTask(TaskStatus.InReview, assigneeId: "user1");

        var act = () => _service.MoveTaskAsync(
            task.Id, TaskStatus.Done, "user1", "User One", ComplianceRoles.Analyst);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*VALIDATION_REQUIRED*");
    }

    [Fact]
    public async Task MoveTask_DoneToAnything_Throws_TerminalState()
    {
        var task = await SeedTask(TaskStatus.Done, assigneeId: "user1");

        var act = () => _service.MoveTaskAsync(
            task.Id, TaskStatus.Backlog, "admin1", "Admin One", ComplianceRoles.Administrator);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*TERMINAL_STATE*");
    }

    [Fact]
    public async Task MoveTask_InvalidTransition_BacklogToDone_Throws()
    {
        var task = await SeedTask(TaskStatus.Backlog);

        var act = () => _service.MoveTaskAsync(
            task.Id, TaskStatus.Done, "admin1", "Admin One", ComplianceRoles.Administrator);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*INVALID_TRANSITION*");
    }

    [Fact]
    public async Task MoveTask_LogsHistoryEntry()
    {
        var task = await SeedTask(TaskStatus.Backlog, assigneeId: "user1");

        await _service.MoveTaskAsync(
            task.Id, TaskStatus.ToDo, "user1", "User One", ComplianceRoles.Administrator);

        var history = await _context.TaskHistoryEntries
            .Where(h => h.TaskId == task.Id && h.EventType == HistoryEventType.StatusChanged)
            .ToListAsync();

        history.Should().HaveCount(1);
        history[0].OldValue.Should().Be("Backlog");
        history[0].NewValue.Should().Be("ToDo");
    }

    [Fact]
    public async Task MoveTask_EnqueuesNotification()
    {
        var task = await SeedTask(TaskStatus.Backlog, assigneeId: "user1");

        await _service.MoveTaskAsync(
            task.Id, TaskStatus.ToDo, "user1", "User One", ComplianceRoles.Administrator);

        _notificationMock.Verify(
            n => n.EnqueueAsync(It.Is<NotificationMessage>(m =>
                m.EventType == NotificationEventType.StatusChanged)),
            Times.Once);
    }

    [Fact]
    public async Task MoveTask_ToDone_EnqueuesTaskClosedNotification()
    {
        var task = await SeedTask(TaskStatus.InReview, assigneeId: "user1");

        await _service.MoveTaskAsync(
            task.Id, TaskStatus.Done, "admin1", "Admin One", ComplianceRoles.Administrator,
            comment: "Explicit administrator verification exception", skipValidation: true);

        _notificationMock.Verify(
            n => n.EnqueueAsync(It.Is<NotificationMessage>(m =>
                m.EventType == NotificationEventType.TaskClosed)),
            Times.Once);
    }

    [Fact]
    public async Task MoveTask_RBAC_AnalystCanMoveOwn()
    {
        var task = await SeedTask(TaskStatus.Backlog, assigneeId: "analyst1");

        var result = await _service.MoveTaskAsync(
            task.Id, TaskStatus.ToDo, "analyst1", "Analyst One", ComplianceRoles.Analyst);

        result.Status.Should().Be(TaskStatus.ToDo);
    }

    [Fact]
    public async Task MoveTask_RBAC_AnalystCannotMoveOthers()
    {
        var task = await SeedTask(TaskStatus.Backlog, assigneeId: "other-user");

        var act = () => _service.MoveTaskAsync(
            task.Id, TaskStatus.ToDo, "analyst1", "Analyst One", ComplianceRoles.Analyst);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*UNAUTHORIZED*");
    }

    [Fact]
    public async Task MoveTask_RBAC_AuditorCannotMove()
    {
        var task = await SeedTask(TaskStatus.Backlog, assigneeId: "auditor1");

        var act = () => _service.MoveTaskAsync(
            task.Id, TaskStatus.ToDo, "auditor1", "Auditor One", ComplianceRoles.Auditor);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*UNAUTHORIZED*");
    }

    [Fact]
    public async Task MoveTask_TaskNotFound_Throws()
    {
        var act = () => _service.MoveTaskAsync(
            "nonexistent", TaskStatus.ToDo, "user1", "User One", ComplianceRoles.Administrator);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task MoveTask_FullLifecycle_BacklogThroughDone()
    {
        var task = await SeedTask(TaskStatus.Backlog);
        var role = ComplianceRoles.Administrator;

        // Backlog → ToDo
        task = await _service.MoveTaskAsync(task.Id, TaskStatus.ToDo, "u1", "U1", role);
        task.Status.Should().Be(TaskStatus.ToDo);

        // Reload for tracking
        task = (await _context.RemediationTasks.FindAsync(task.Id))!;

        // ToDo → InProgress (auto-assigns)
        task = await _service.MoveTaskAsync(task.Id, TaskStatus.InProgress, "u1", "U1", role);
        task.Status.Should().Be(TaskStatus.InProgress);

        task = (await _context.RemediationTasks.FindAsync(task.Id))!;

        // InProgress → InReview
        task = await _service.MoveTaskAsync(task.Id, TaskStatus.InReview, "u1", "U1", role);
        task.Status.Should().Be(TaskStatus.InReview);

        task = (await _context.RemediationTasks.FindAsync(task.Id))!;

        // InReview → Done (skip validation)
        task = await _service.MoveTaskAsync(task.Id, TaskStatus.Done, "u1", "U1", role,
            comment: "Explicit administrator verification exception", skipValidation: true);
        task.Status.Should().Be(TaskStatus.Done);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<RemediationTask> SeedTask(TaskStatus status, string? assigneeId = "user1")
    {
        var board = new RemediationBoard
        {
            Name = "Test Board",
            SubscriptionId = "sub",
            Owner = "owner",
        };
        _context.RemediationBoards.Add(board);

        var task = new RemediationTask
        {
            BoardId = board.Id,
            TaskNumber = "REM-001",
            Title = "Test Task",
            ControlId = "AC-2",
            ControlFamily = "AC",
            Status = status,
            AssigneeId = assigneeId,
            AssigneeName = assigneeId != null ? $"{assigneeId}-name" : null,
            Severity = FindingSeverity.High,
            CreatedBy = "owner",
        };
        _context.RemediationTasks.Add(task);
        await _context.SaveChangesAsync();
        return task;
    }
}

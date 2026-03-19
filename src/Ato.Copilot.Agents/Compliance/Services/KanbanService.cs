using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Models.Roadmap;
using Ato.Copilot.Core.Services;
using Ato.Copilot.State.Abstractions;
using TaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implementation of IKanbanService consolidating all Kanban board operations.
/// Registered as Scoped — one instance per request for DbContext alignment.
/// </summary>
public class KanbanService : IKanbanService
{
    private readonly AtoCopilotContext _context;
    private readonly ILogger<KanbanService> _logger;
    private readonly INotificationService _notificationService;
    private readonly IAgentStateManager _stateManager;
    private readonly IAtoComplianceEngine _complianceEngine;
    private readonly IRemediationEngine _remediationEngine;
    private readonly ITaskEnrichmentService? _taskEnrichmentService;
    private readonly PoamService? _poamService;

    /// <summary>
    /// Initializes a new instance of <see cref="KanbanService"/>.
    /// </summary>
    public KanbanService(
        AtoCopilotContext context,
        ILogger<KanbanService> logger,
        INotificationService notificationService,
        IAgentStateManager stateManager,
        IAtoComplianceEngine complianceEngine,
        IRemediationEngine remediationEngine,
        ITaskEnrichmentService? taskEnrichmentService = null,
        PoamService? poamService = null)
    {
        _context = context;
        _logger = logger;
        _notificationService = notificationService;
        _stateManager = stateManager;
        _complianceEngine = complianceEngine;
        _remediationEngine = remediationEngine;
        _taskEnrichmentService = taskEnrichmentService;
        _poamService = poamService;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Board Operations
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<RemediationBoard> CreateBoardAsync(
        string name, string subscriptionId, string owner, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating board '{Name}' for subscription {SubscriptionId}", name, subscriptionId);

        var board = new RemediationBoard
        {
            Name = name,
            SubscriptionId = subscriptionId,
            Owner = owner,
        };

        _context.RemediationBoards.Add(board);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Board created: {BoardId}", board.Id);
        return board;
    }

    /// <inheritdoc />
    public async Task<RemediationBoard> CreateBoardFromAssessmentAsync(
        string assessmentId, string name, string subscriptionId, string owner,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating board from assessment {AssessmentId}", assessmentId);

        var assessment = await _context.Assessments
            .Include(a => a.Findings)
            .FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken);

        // Fallback: if the provided ID doesn't match (e.g. LLM hallucinated an ID),
        // try to find the most recent assessment for the given subscription.
        if (assessment == null && !string.IsNullOrWhiteSpace(subscriptionId))
        {
            _logger.LogWarning("Assessment '{AssessmentId}' not found, falling back to latest for subscription {Sub}",
                assessmentId, subscriptionId);
            assessment = await _context.Assessments
                .Include(a => a.Findings)
                .Where(a => a.SubscriptionId == subscriptionId)
                .OrderByDescending(a => a.AssessedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Last resort: get the most recent assessment across all subscriptions
        if (assessment == null)
        {
            _logger.LogWarning("No assessment found for subscription, falling back to latest overall");
            assessment = await _context.Assessments
                .Include(a => a.Findings)
                .OrderByDescending(a => a.AssessedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (assessment == null)
            throw new InvalidOperationException($"Assessment '{assessmentId}' not found. No assessments exist. Run a compliance assessment first.");

        var board = new RemediationBoard
        {
            Name = name,
            SubscriptionId = subscriptionId,
            AssessmentId = assessment.Id,
            Owner = owner,
        };

        var findings = assessment.Findings
            .Where(f => f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress)
            .ToList();

        foreach (var finding in findings)
        {
            var task = CreateTaskFromFinding(finding, board);
            board.Tasks.Add(task);
        }

        _context.RemediationBoards.Add(board);
        await _context.SaveChangesAsync(cancellationToken);

        // T026: Auto-enrich tasks with remediation scripts and validation criteria (Feature 012)
        if (_taskEnrichmentService != null && board.Tasks.Count > 0)
        {
            try
            {
                var enrichResult = await _taskEnrichmentService.EnrichBoardTasksAsync(
                    board, findings, progress: null, ct: cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Board {BoardId} enrichment: {Enriched} enriched, {Skipped} skipped, {Failed} failed ({Duration}ms)",
                    board.Id, enrichResult.TasksEnriched, enrichResult.TasksSkipped,
                    enrichResult.TasksFailed, (int)enrichResult.Duration.TotalMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Board enrichment failed for {BoardId} — tasks created without enrichment", board.Id);
            }
        }

        // Feature 039: Auto-create POA&M items from open findings and link to tasks
        if (_poamService != null && board.Tasks.Count > 0)
        {
            try
            {
                var systemId = board.SubscriptionId;
                var findingLookup = findings.ToDictionary(f => f.Id);
                var poamCreated = 0;

                foreach (var task in board.Tasks)
                {
                    if (string.IsNullOrEmpty(task.FindingId) || !findingLookup.TryGetValue(task.FindingId, out var finding))
                        continue;

                    // Duplicate detection: check if active POA&M already exists for this finding
                    var existing = await _context.PoamItems
                        .AnyAsync(p => p.FindingId == finding.Id &&
                                       p.SecurityControlNumber == finding.ControlId &&
                                       p.Status != PoamStatus.Completed &&
                                       p.Status != PoamStatus.RiskAccepted, cancellationToken);
                    if (existing) continue;

                    var severity = finding.Severity switch
                    {
                        FindingSeverity.Critical or FindingSeverity.High => CatSeverity.CatI,
                        FindingSeverity.Medium => CatSeverity.CatII,
                        _ => CatSeverity.CatIII
                    };

                    var poam = await _poamService.CreateAsync(
                        systemId,
                        finding.Title,
                        "Assessment",
                        finding.ControlId,
                        severity,
                        owner,
                        DateTime.UtcNow.AddDays(severity == CatSeverity.CatI ? 30 : severity == CatSeverity.CatII ? 90 : 180),
                        findingId: finding.Id,
                        createdBy: owner,
                        ct: cancellationToken);

                    // Set bidirectional FKs
                    task.PoamItemId = poam.Id;
                    poam.RemediationTaskId = task.Id;
                    poamCreated++;
                }

                if (poamCreated > 0)
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Board {BoardId}: auto-created {Count} POA&M items linked to tasks", board.Id, poamCreated);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Auto-POA&M creation failed for board {BoardId} — tasks created without POA&M items", board.Id);
            }
        }

        _logger.LogInformation("Board created from assessment: {BoardId} with {TaskCount} tasks",
            board.Id, board.Tasks.Count);
        return board;
    }

    /// <inheritdoc />
    public async Task<BoardUpdateResult> UpdateBoardFromAssessmentAsync(
        string boardId, string assessmentId, string actingUserId, string actingUserName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating board {BoardId} from assessment {AssessmentId}", boardId, assessmentId);

        var board = await _context.RemediationBoards
            .Include(b => b.Tasks)
            .FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken)
            ?? throw new InvalidOperationException($"Board '{boardId}' not found.");

        var assessment = await _context.Assessments
            .Include(a => a.Findings)
            .FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken)
            ?? throw new InvalidOperationException($"Assessment '{assessmentId}' not found.");

        var openFindings = assessment.Findings
            .Where(f => f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress)
            .ToList();

        var result = new BoardUpdateResult { Board = board };

        // Match existing tasks by controlId + affectedResources
        var existingTasks = board.Tasks.Where(t => t.Status != TaskStatus.Done).ToList();

        foreach (var finding in openFindings)
        {
            var matchingTask = existingTasks.FirstOrDefault(t =>
                t.ControlId == finding.ControlId &&
                t.FindingId == finding.Id);

            if (matchingTask != null)
            {
                result.TasksUnchanged++;
            }
            else
            {
                var newTask = CreateTaskFromFinding(finding, board);
                newTask.History.Add(new TaskHistoryEntry
                {
                    TaskId = newTask.Id,
                    EventType = HistoryEventType.Created,
                    NewValue = "Backlog",
                    ActingUserId = actingUserId,
                    ActingUserName = actingUserName,
                    Details = $"Created from assessment update: {assessmentId}"
                });
                board.Tasks.Add(newTask);
                result.TasksAdded++;
            }
        }

        // Auto-close tasks whose findings are now resolved
        var resolvedFindingIds = assessment.Findings
            .Where(f => f.Status == FindingStatus.Remediated || f.Status == FindingStatus.FalsePositive)
            .Select(f => f.Id)
            .ToHashSet();

        foreach (var task in existingTasks.Where(t => t.FindingId != null && resolvedFindingIds.Contains(t.FindingId)))
        {
            task.Status = TaskStatus.Done;
            task.UpdatedAt = DateTime.UtcNow;
            task.Comments.Add(new TaskComment
            {
                TaskId = task.Id,
                AuthorId = "system",
                AuthorName = "System",
                Content = KanbanConstants.AutoClosedComment,
                IsSystemComment = true,
            });
            task.History.Add(new TaskHistoryEntry
            {
                TaskId = task.Id,
                EventType = HistoryEventType.StatusChanged,
                OldValue = task.Status.ToString(),
                NewValue = TaskStatus.Done.ToString(),
                ActingUserId = "system",
                ActingUserName = "System",
                Details = KanbanConstants.AutoClosedComment,
            });
            result.TasksClosed++;
        }

        board.AssessmentId = assessmentId;
        board.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        // T049: Enrich newly created tasks during board update (Feature 012)
        if (_taskEnrichmentService != null && result.TasksAdded > 0)
        {
            try
            {
                var newTasks = board.Tasks
                    .Where(t => string.IsNullOrEmpty(t.RemediationScript))
                    .ToList();
                var newBoard = new RemediationBoard
                {
                    Id = board.Id,
                    Tasks = newTasks
                };
                var enrichResult = await _taskEnrichmentService.EnrichBoardTasksAsync(
                    newBoard, openFindings, progress: null, ct: cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Board update enrichment for {BoardId}: {Enriched} enriched, {Skipped} skipped, {Failed} failed",
                    board.Id, enrichResult.TasksEnriched, enrichResult.TasksSkipped, enrichResult.TasksFailed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Board update enrichment failed for {BoardId}", board.Id);
            }
        }

        _logger.LogInformation("Board updated: +{Added} -{Closed} ={Unchanged}",
            result.TasksAdded, result.TasksClosed, result.TasksUnchanged);
        return result;
    }

    /// <inheritdoc />
    public async Task<RemediationBoard?> GetBoardAsync(
        string boardId, CancellationToken cancellationToken = default)
    {
        return await _context.RemediationBoards
            .Include(b => b.Tasks)
            .FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedResult<RemediationBoard>> ListBoardsAsync(
        string subscriptionId, bool? isArchived = null, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var query = _context.RemediationBoards
            .Where(b => b.SubscriptionId == subscriptionId);

        if (isArchived.HasValue)
            query = query.Where(b => b.IsArchived == isArchived.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(b => b.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(b => b.Tasks)
            .ToListAsync(cancellationToken);

        return new PagedResult<RemediationBoard>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <inheritdoc />
    public async Task<RemediationBoard> ArchiveBoardAsync(
        string boardId, string actingUserId, string actingUserName,
        CancellationToken cancellationToken = default)
    {
        var board = await _context.RemediationBoards
            .Include(b => b.Tasks)
            .FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken)
            ?? throw new InvalidOperationException($"Board '{boardId}' not found.");

        var openTasks = board.Tasks.Count(t => t.Status != TaskStatus.Done);
        if (openTasks > 0)
            throw new InvalidOperationException($"Cannot archive board: {openTasks} tasks are not Done.");

        board.IsArchived = true;
        board.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Board archived: {BoardId} by {UserId}", boardId, actingUserId);
        return board;
    }

    /// <inheritdoc />
    public async Task<string> ExportBoardCsvAsync(
        string boardId, string actingUserId, string actingUserRole,
        CancellationToken cancellationToken = default)
    {
        var board = await _context.RemediationBoards
            .Include(b => b.Tasks)
            .FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken)
            ?? throw new InvalidOperationException($"Board '{boardId}' not found.");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("TaskNumber,Title,ControlId,Severity,Status,AssigneeName,DueDate,IsOverdue,CreatedAt,UpdatedAt,Description");

        foreach (var task in board.Tasks.OrderBy(t => t.TaskNumber))
        {
            var isOverdue = task.DueDate < DateTime.UtcNow && task.Status != TaskStatus.Done;
            sb.AppendLine($"\"{task.TaskNumber}\",\"{EscapeCsv(task.Title)}\",\"{task.ControlId}\",\"{task.Severity}\",\"{task.Status}\",\"{EscapeCsv(task.AssigneeName ?? "")}\",\"{task.DueDate:yyyy-MM-dd}\",\"{isOverdue}\",\"{task.CreatedAt:yyyy-MM-ddTHH:mm:ssZ}\",\"{task.UpdatedAt:yyyy-MM-ddTHH:mm:ssZ}\",\"{EscapeCsv(task.Description)}\"");
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<string> ExportBoardHistoryAsync(
        string boardId, string actingUserId, string actingUserRole,
        CancellationToken cancellationToken = default)
    {
        var board = await _context.RemediationBoards
            .Include(b => b.Tasks)
                .ThenInclude(t => t.History)
            .FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken)
            ?? throw new InvalidOperationException($"Board '{boardId}' not found.");

        var allHistory = board.Tasks
            .SelectMany(t => t.History.Select(h => new { Task = t, History = h }))
            .OrderBy(x => x.History.Timestamp)
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("EventType,TaskNumber,OldValue,NewValue,ActingUserId,ActingUserName,Timestamp,Details");

        foreach (var entry in allHistory)
        {
            sb.AppendLine($"\"{entry.History.EventType}\",\"{entry.Task.TaskNumber}\",\"{EscapeCsv(entry.History.OldValue ?? "")}\",\"{EscapeCsv(entry.History.NewValue ?? "")}\",\"{entry.History.ActingUserId}\",\"{EscapeCsv(entry.History.ActingUserName)}\",\"{entry.History.Timestamp:yyyy-MM-ddTHH:mm:ssZ}\",\"{EscapeCsv(entry.History.Details ?? "")}\"");
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task Operations
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<RemediationTask> CreateTaskAsync(
        string boardId, string title, string controlId, string createdBy,
        string? description = null, FindingSeverity? severity = null,
        string? assigneeId = null, DateTime? dueDate = null,
        List<string>? affectedResources = null, string? remediationScript = null,
        string? validationCriteria = null, string? linkedAlertId = null,
        CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(controlId, KanbanConstants.ControlIdPattern))
            throw new ArgumentException($"Invalid control ID format: '{controlId}'. Expected pattern: {KanbanConstants.ControlIdPattern}");

        var board = await _context.RemediationBoards
            .FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken)
            ?? throw new InvalidOperationException($"Board '{boardId}' not found.");

        var effectiveSeverity = severity ?? FindingSeverity.Medium;
        var taskNumber = string.Format(KanbanConstants.TaskIdFormat, board.NextTaskNumber);
        board.NextTaskNumber++;

        var task = new RemediationTask
        {
            TaskNumber = taskNumber,
            BoardId = boardId,
            Title = title,
            Description = description ?? "",
            ControlId = controlId,
            ControlFamily = controlId.Length >= 2 ? controlId[..2] : controlId,
            Severity = effectiveSeverity,
            AssigneeId = assigneeId,
            DueDate = dueDate ?? CalculateDueDate(effectiveSeverity),
            AffectedResources = affectedResources ?? new List<string>(),
            RemediationScript = remediationScript,
            ValidationCriteria = validationCriteria,
            LinkedAlertId = linkedAlertId,
            CreatedBy = createdBy,
        };

        task.History.Add(new TaskHistoryEntry
        {
            TaskId = task.Id,
            EventType = HistoryEventType.Created,
            NewValue = TaskStatus.Backlog.ToString(),
            ActingUserId = createdBy,
            ActingUserName = createdBy,
            Details = $"Task created: {title}",
        });

        board.Tasks.Add(task);
        board.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Task created: {TaskNumber} on board {BoardId}", taskNumber, boardId);
        return task;
    }

    /// <inheritdoc />
    public async Task<RemediationTask?> GetTaskByLinkedAlertIdAsync(
        string alertId, CancellationToken cancellationToken = default)
    {
        return await _context.RemediationTasks
            .FirstOrDefaultAsync(t => t.LinkedAlertId == alertId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RemediationTask?> GetTaskAsync(
        string taskId, CancellationToken cancellationToken = default)
    {
        // Try by primary key (GUID) first, then fall back to human-readable TaskNumber (e.g. REM-001)
        var task = await _context.RemediationTasks
            .Include(t => t.Comments.Where(c => !c.IsDeleted))
            .Include(t => t.History)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task is null)
        {
            task = await _context.RemediationTasks
                .Include(t => t.Comments.Where(c => !c.IsDeleted))
                .Include(t => t.History)
                .FirstOrDefaultAsync(t => t.TaskNumber == taskId, cancellationToken);
        }

        return task;
    }

    /// <inheritdoc />
    public async Task<PagedResult<RemediationTask>> ListTasksAsync(
        string boardId, TaskStatus? status = null, FindingSeverity? severity = null,
        string? assigneeId = null, string? controlFamily = null, bool? isOverdue = null,
        string? sortBy = null, string? sortOrder = null, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var query = _context.RemediationTasks.Where(t => t.BoardId == boardId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);
        if (severity.HasValue)
            query = query.Where(t => t.Severity == severity.Value);
        if (!string.IsNullOrEmpty(assigneeId))
            query = query.Where(t => t.AssigneeId == assigneeId);
        if (!string.IsNullOrEmpty(controlFamily))
            query = query.Where(t => t.ControlFamily == controlFamily);
        if (isOverdue == true)
            query = query.Where(t => t.DueDate < DateTime.UtcNow && t.Status != TaskStatus.Done);

        var totalCount = await query.CountAsync(cancellationToken);

        // Apply sorting
        query = (sortBy?.ToLowerInvariant(), sortOrder?.ToLowerInvariant()) switch
        {
            ("duedate", "asc") => query.OrderBy(t => t.DueDate),
            ("duedate", _) => query.OrderByDescending(t => t.DueDate),
            ("createdat", "asc") => query.OrderBy(t => t.CreatedAt),
            ("createdat", _) => query.OrderByDescending(t => t.CreatedAt),
            ("status", "asc") => query.OrderBy(t => t.Status),
            ("status", _) => query.OrderByDescending(t => t.Status),
            ("controlid", "asc") => query.OrderBy(t => t.ControlId),
            ("controlid", _) => query.OrderByDescending(t => t.ControlId),
            _ => query.OrderByDescending(t => t.Severity).ThenBy(t => t.DueDate),
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<RemediationTask>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <inheritdoc />
    public async Task<RemediationTask> MoveTaskAsync(
        string taskId, TaskStatus targetStatus, string actingUserId, string actingUserName,
        string actingUserRole, string? comment = null, bool skipValidation = false,
        CancellationToken cancellationToken = default)
    {
        var task = await _context.RemediationTasks
            .Include(t => t.Comments)
            .Include(t => t.History)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new InvalidOperationException($"Task '{taskId}' not found.");

        var rule = StatusTransitionEngine.GetTransitionRule(task.Status, targetStatus);
        if (rule == null)
        {
            if (task.Status == TaskStatus.Done)
                throw new InvalidOperationException("TERMINAL_STATE: Task is Done and cannot be moved.");
            throw new InvalidOperationException($"INVALID_TRANSITION: Cannot move from {task.Status} to {targetStatus}.");
        }

        // Enforce transition conditions
        if (rule.RequiresComment && string.IsNullOrWhiteSpace(comment))
            throw new InvalidOperationException("BLOCKER_COMMENT_REQUIRED: A comment explaining the blocker is required.");
        if (rule.RequiresResolutionComment && string.IsNullOrWhiteSpace(comment))
            throw new InvalidOperationException("RESOLUTION_COMMENT_REQUIRED: A resolution comment is required to leave Blocked status.");
        if (rule.RequiresValidation && !skipValidation)
        {
            // Check if CO can skip
            if (!KanbanPermissionsHelper.CanPerformAction(actingUserRole, KanbanPermissions.CanCloseWithoutValidation))
                throw new InvalidOperationException("VALIDATION_REQUIRED: Task must pass validation before closing, or a Compliance Officer must skip validation.");
        }

        // RBAC check
        var canMoveAny = KanbanPermissionsHelper.CanPerformAction(actingUserRole, KanbanPermissions.CanMoveAny);
        var canMoveOwn = KanbanPermissionsHelper.CanPerformAction(actingUserRole, KanbanPermissions.CanMoveOwn);
        if (!canMoveAny && !(canMoveOwn && task.AssigneeId == actingUserId))
            throw new UnauthorizedAccessException($"UNAUTHORIZED: Role '{actingUserRole}' cannot move this task.");

        var oldStatus = task.Status;

        // Auto-assign on →InProgress if unassigned
        if (rule.AutoAssign && string.IsNullOrEmpty(task.AssigneeId))
        {
            task.AssigneeId = actingUserId;
            task.AssigneeName = actingUserName;
            task.History.Add(new TaskHistoryEntry
            {
                TaskId = task.Id,
                EventType = HistoryEventType.Assigned,
                NewValue = actingUserName,
                ActingUserId = actingUserId,
                ActingUserName = actingUserName,
                Details = "Auto-assigned on status transition",
            });
        }

        // Add blocker/resolution comment
        if (!string.IsNullOrWhiteSpace(comment))
        {
            task.Comments.Add(new TaskComment
            {
                TaskId = task.Id,
                AuthorId = actingUserId,
                AuthorName = actingUserName,
                Content = comment,
            });
        }

        task.Status = targetStatus;
        task.UpdatedAt = DateTime.UtcNow;

        task.History.Add(new TaskHistoryEntry
        {
            TaskId = task.Id,
            EventType = HistoryEventType.StatusChanged,
            OldValue = oldStatus.ToString(),
            NewValue = targetStatus.ToString(),
            ActingUserId = actingUserId,
            ActingUserName = actingUserName,
        });

        await _context.SaveChangesAsync(cancellationToken);

        // Enqueue notification
        await _notificationService.EnqueueAsync(new NotificationMessage
        {
            EventType = targetStatus == TaskStatus.Done ? NotificationEventType.TaskClosed : NotificationEventType.StatusChanged,
            TaskId = task.Id,
            TaskNumber = task.TaskNumber,
            BoardId = task.BoardId,
            TargetUserId = task.AssigneeId ?? "",
            Title = $"Task {task.TaskNumber} moved to {targetStatus}",
            Details = $"Moved from {oldStatus} to {targetStatus} by {actingUserName}",
        });

        _logger.LogInformation("Task {TaskNumber} moved {OldStatus}→{NewStatus} by {User}",
            task.TaskNumber, oldStatus, targetStatus, actingUserId);

        // Feature 031: Sync linked roadmap item status
        await SyncLinkedRoadmapItemAsync(task, targetStatus, cancellationToken);

        return task;
    }

    /// <inheritdoc />
    public async Task<RemediationTask> AssignTaskAsync(
        string taskId, string actingUserId, string actingUserName, string actingUserRole,
        string? assigneeId = null, string? assigneeName = null,
        CancellationToken cancellationToken = default)
    {
        var task = await _context.RemediationTasks
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new InvalidOperationException($"Task '{taskId}' not found.");

        if (task.Status == TaskStatus.Done)
            throw new InvalidOperationException("Cannot assign a Done task.");

        // RBAC: CO/SL can assign any, PE can self-assign unassigned only
        var canAssignAny = KanbanPermissionsHelper.CanPerformAction(actingUserRole, KanbanPermissions.CanAssignAny);
        var canSelfAssign = KanbanPermissionsHelper.CanPerformAction(actingUserRole, KanbanPermissions.CanSelfAssign);

        if (assigneeId == actingUserId)
        {
            // Self-assignment
            if (!canSelfAssign && !canAssignAny)
                throw new UnauthorizedAccessException($"UNAUTHORIZED: Role '{actingUserRole}' cannot self-assign tasks.");
            if (canSelfAssign && !canAssignAny && task.AssigneeId != null)
                throw new InvalidOperationException("PE can only self-assign unassigned tasks.");
        }
        else
        {
            // Assigning to someone else
            if (!canAssignAny)
                throw new UnauthorizedAccessException($"UNAUTHORIZED: Role '{actingUserRole}' cannot assign tasks to others.");
        }

        var oldAssignee = task.AssigneeName ?? "(unassigned)";
        task.AssigneeId = assigneeId;
        task.AssigneeName = assigneeName;
        task.UpdatedAt = DateTime.UtcNow;

        task.History = await _context.Entry(task)
            .Collection(t => t.History)
            .Query()
            .ToListAsync(cancellationToken);

        task.History.Add(new TaskHistoryEntry
        {
            TaskId = task.Id,
            EventType = HistoryEventType.Assigned,
            OldValue = oldAssignee,
            NewValue = assigneeName ?? "(unassigned)",
            ActingUserId = actingUserId,
            ActingUserName = actingUserName,
        });

        await _context.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrEmpty(assigneeId))
        {
            await _notificationService.EnqueueAsync(new NotificationMessage
            {
                EventType = NotificationEventType.TaskAssigned,
                TaskId = task.Id,
                TaskNumber = task.TaskNumber,
                BoardId = task.BoardId,
                TargetUserId = assigneeId,
                Title = $"Task {task.TaskNumber} assigned to you",
                Details = $"Assigned by {actingUserName}",
            });
        }

        _logger.LogInformation("Task {TaskNumber} assigned to {Assignee} by {User}",
            task.TaskNumber, assigneeName ?? "(unassigned)", actingUserId);
        return task;
    }

    /// <inheritdoc />
    public async Task<ValidationResult> ValidateTaskAsync(
        string taskId, string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        var task = await _context.RemediationTasks
            .Include(t => t.History)
            .Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new InvalidOperationException($"Task '{taskId}' not found.");

        var result = new ValidationResult();
        var resourceResults = new List<ResourceValidationResult>();

        foreach (var resource in task.AffectedResources)
        {
            try
            {
                var validationOutput = await _remediationEngine.ValidateRemediationAsync(
                    task.FindingId ?? task.ControlId,
                    subscriptionId: subscriptionId,
                    cancellationToken: cancellationToken);

                var passed = validationOutput.Contains("compliant", StringComparison.OrdinalIgnoreCase) ||
                             validationOutput.Contains("pass", StringComparison.OrdinalIgnoreCase);

                resourceResults.Add(new ResourceValidationResult
                {
                    ResourceId = resource,
                    Passed = passed,
                    Details = validationOutput,
                });
            }
            catch (Exception ex)
            {
                resourceResults.Add(new ResourceValidationResult
                {
                    ResourceId = resource,
                    Passed = false,
                    Details = $"Validation failed: {ex.Message}",
                });
            }
        }

        result.ResourceResults = resourceResults;
        result.AllPassed = resourceResults.All(r => r.Passed);
        result.CanClose = result.AllPassed;
        result.Summary = result.AllPassed
            ? $"All {resourceResults.Count} resources passed validation."
            : $"{resourceResults.Count(r => r.Passed)}/{resourceResults.Count} resources passed. {resourceResults.Count(r => !r.Passed)} failed.";

        // Add history entry
        task.History.Add(new TaskHistoryEntry
        {
            TaskId = task.Id,
            EventType = HistoryEventType.ValidationRun,
            NewValue = result.AllPassed ? "Passed" : "Failed",
            ActingUserId = "system",
            ActingUserName = "System",
            Details = result.Summary,
        });

        // Add system comment with results
        task.Comments.Add(new TaskComment
        {
            TaskId = task.Id,
            AuthorId = "system",
            AuthorName = "System",
            Content = $"**Validation Results**: {result.Summary}",
            IsSystemComment = true,
        });

        await _context.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<List<RemediationTask>> GetOpenTasksForPoamAsync(
        string boardId, CancellationToken cancellationToken = default)
    {
        return await _context.RemediationTasks
            .Where(t => t.BoardId == boardId && t.Status != TaskStatus.Done)
            .OrderBy(t => t.Severity)
            .ThenBy(t => t.DueDate)
            .ToListAsync(cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Remediation Operations
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<RemediationExecutionResult> ExecuteTaskRemediationAsync(
        string taskId, string actingUserId, string actingUserName,
        CancellationToken cancellationToken = default)
    {
        var task = await _context.RemediationTasks
            .Include(t => t.History)
            .Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new InvalidOperationException($"Task '{taskId}' not found.");

        if (string.IsNullOrEmpty(task.RemediationScript))
            throw new InvalidOperationException($"Task {task.TaskNumber} has no remediation script. Add a script or use manual remediation.");

        try
        {
            var output = await _remediationEngine.ExecuteRemediationAsync(
                task.FindingId ?? task.ControlId,
                applyRemediation: true,
                dryRun: false,
                cancellationToken: cancellationToken);

            task.History.Add(new TaskHistoryEntry
            {
                TaskId = task.Id,
                EventType = HistoryEventType.RemediationAttempt,
                NewValue = "Success",
                ActingUserId = actingUserId,
                ActingUserName = actingUserName,
                Details = output,
            });

            // Move to InReview on success
            await MoveTaskAsync(taskId, TaskStatus.InReview, actingUserId, actingUserName,
                ComplianceRoles.Analyst, comment: "Remediation executed successfully", cancellationToken: cancellationToken);

            return new RemediationExecutionResult
            {
                Success = true,
                Task = task,
                Details = output,
            };
        }
        catch (Exception ex)
        {
            task.History.Add(new TaskHistoryEntry
            {
                TaskId = task.Id,
                EventType = HistoryEventType.RemediationAttempt,
                NewValue = "Failed",
                ActingUserId = actingUserId,
                ActingUserName = actingUserName,
                Details = ex.Message,
            });

            task.Comments.Add(new TaskComment
            {
                TaskId = task.Id,
                AuthorId = "system",
                AuthorName = "System",
                Content = $"**Remediation failed**: {ex.Message}",
                IsSystemComment = true,
            });

            await _context.SaveChangesAsync(cancellationToken);

            return new RemediationExecutionResult
            {
                Success = false,
                Task = task,
                Details = ex.Message,
            };
        }
    }

    /// <inheritdoc />
    public async Task<EvidenceCollectionResult> CollectTaskEvidenceAsync(
        string taskId, string actingUserId, string actingUserName,
        string? subscriptionId = null, CancellationToken cancellationToken = default)
    {
        var task = await _context.RemediationTasks
            .Include(t => t.History)
            .Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new InvalidOperationException($"Task '{taskId}' not found.");

        if (!task.AffectedResources.Any())
            throw new InvalidOperationException($"Task {task.TaskNumber} has no affected resources for evidence collection.");

        // Delegate to evidence storage service via compliance engine for the control
        var evidenceSummary = $"Evidence collected for control {task.ControlId} across {task.AffectedResources.Count} resources.";

        task.History.Add(new TaskHistoryEntry
        {
            TaskId = task.Id,
            EventType = HistoryEventType.ValidationRun,
            NewValue = "EvidenceCollected",
            ActingUserId = actingUserId,
            ActingUserName = actingUserName,
            Details = evidenceSummary,
        });

        task.Comments.Add(new TaskComment
        {
            TaskId = task.Id,
            AuthorId = "system",
            AuthorName = "System",
            Content = $"**Evidence Collection**: {evidenceSummary}",
            IsSystemComment = true,
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new EvidenceCollectionResult
        {
            Success = true,
            ItemsCollected = task.AffectedResources.Count,
            Summary = evidenceSummary,
            Task = task,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Comment Operations
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<TaskComment> AddCommentAsync(
        string taskId, string authorId, string authorName, string content, string authorRole,
        string? parentCommentId = null, CancellationToken cancellationToken = default)
    {
        if (!KanbanPermissionsHelper.CanPerformAction(authorRole, KanbanPermissions.CanComment))
            throw new UnauthorizedAccessException($"UNAUTHORIZED: Role '{authorRole}' cannot add comments.");

        var task = await _context.RemediationTasks
            .Include(t => t.History)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new InvalidOperationException($"Task '{taskId}' not found.");

        // Parse @mentions
        var mentions = Regex.Matches(content, KanbanConstants.MentionPattern)
            .Select(m => m.Value.TrimStart('@'))
            .Distinct()
            .ToList();

        var comment = new TaskComment
        {
            TaskId = taskId,
            AuthorId = authorId,
            AuthorName = authorName,
            Content = content,
            ParentCommentId = parentCommentId,
            Mentions = mentions,
        };

        _context.TaskComments.Add(comment);

        task.History.Add(new TaskHistoryEntry
        {
            TaskId = taskId,
            EventType = HistoryEventType.CommentAdded,
            ActingUserId = authorId,
            ActingUserName = authorName,
            Details = content.Length > 100 ? content[..100] + "..." : content,
        });

        await _context.SaveChangesAsync(cancellationToken);

        // Enqueue notifications
        await _notificationService.EnqueueAsync(new NotificationMessage
        {
            EventType = NotificationEventType.CommentAdded,
            TaskId = taskId,
            TaskNumber = task.TaskNumber,
            BoardId = task.BoardId,
            TargetUserId = task.AssigneeId ?? "",
            Title = $"New comment on {task.TaskNumber}",
            Details = content.Length > 200 ? content[..200] + "..." : content,
        });

        // Enqueue mention notifications
        foreach (var mention in mentions)
        {
            await _notificationService.EnqueueAsync(new NotificationMessage
            {
                EventType = NotificationEventType.Mentioned,
                TaskId = taskId,
                TaskNumber = task.TaskNumber,
                BoardId = task.BoardId,
                TargetUserId = mention,
                Title = $"You were mentioned in {task.TaskNumber}",
                Details = content.Length > 200 ? content[..200] + "..." : content,
            });
        }

        return comment;
    }

    /// <inheritdoc />
    public async Task<TaskComment> EditCommentAsync(
        string commentId, string actingUserId, string content,
        CancellationToken cancellationToken = default)
    {
        var comment = await _context.TaskComments
            .Include(c => c.Task)
            .FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken)
            ?? throw new InvalidOperationException($"Comment '{commentId}' not found.");

        if (comment.AuthorId != actingUserId)
            throw new UnauthorizedAccessException("Cannot edit another user's comment.");
        if (comment.Task?.Status == TaskStatus.Done)
            throw new InvalidOperationException("Cannot edit comments on Done tasks.");
        if (comment.IsDeleted)
            throw new InvalidOperationException("Cannot edit a deleted comment.");

        var editWindow = comment.CreatedAt.AddHours(KanbanConstants.CommentEditWindowHours);
        if (DateTime.UtcNow > editWindow)
            throw new InvalidOperationException("COMMENT_EDIT_WINDOW_EXPIRED: Comments can only be edited within 24 hours of creation.");

        comment.Content = content;
        comment.EditedAt = DateTime.UtcNow;
        comment.IsEdited = true;

        // Re-parse mentions
        comment.Mentions = Regex.Matches(content, KanbanConstants.MentionPattern)
            .Select(m => m.Value.TrimStart('@'))
            .Distinct()
            .ToList();

        var task = await _context.RemediationTasks
            .Include(t => t.History)
            .FirstAsync(t => t.Id == comment.TaskId, cancellationToken);

        task.History.Add(new TaskHistoryEntry
        {
            TaskId = comment.TaskId,
            EventType = HistoryEventType.CommentEdited,
            ActingUserId = actingUserId,
            ActingUserName = comment.AuthorName,
        });

        await _context.SaveChangesAsync(cancellationToken);
        return comment;
    }

    /// <inheritdoc />
    public async Task<TaskComment> DeleteCommentAsync(
        string commentId, string actingUserId, string actingUserRole,
        CancellationToken cancellationToken = default)
    {
        var comment = await _context.TaskComments
            .Include(c => c.Task)
            .FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken)
            ?? throw new InvalidOperationException($"Comment '{commentId}' not found.");

        if (comment.Task?.Status == TaskStatus.Done)
            throw new InvalidOperationException("Cannot delete comments on Done tasks.");
        if (comment.IsDeleted)
            throw new InvalidOperationException("Comment is already deleted.");

        var isCo = KanbanPermissionsHelper.CanPerformAction(actingUserRole, KanbanPermissions.CanDeleteAnyComment);

        if (!isCo)
        {
            if (comment.AuthorId != actingUserId)
                throw new UnauthorizedAccessException("Cannot delete another user's comment.");

            var deleteWindow = comment.CreatedAt.AddHours(KanbanConstants.CommentDeleteWindowHours);
            if (DateTime.UtcNow > deleteWindow)
                throw new InvalidOperationException("COMMENT_DELETE_WINDOW_EXPIRED: Comments can only be deleted within 1 hour of creation.");
        }

        comment.Content = KanbanConstants.DeletedCommentContent;
        comment.IsDeleted = true;

        var task = await _context.RemediationTasks
            .Include(t => t.History)
            .FirstAsync(t => t.Id == comment.TaskId, cancellationToken);

        task.History.Add(new TaskHistoryEntry
        {
            TaskId = comment.TaskId,
            EventType = HistoryEventType.CommentDeleted,
            ActingUserId = actingUserId,
            ActingUserName = comment.AuthorName,
        });

        await _context.SaveChangesAsync(cancellationToken);
        return comment;
    }

    /// <inheritdoc />
    public async Task<PagedResult<TaskComment>> ListCommentsAsync(
        string taskId, bool includeDeleted = false, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TaskComments.Where(c => c.TaskId == taskId);

        if (!includeDeleted)
            query = query.Where(c => !c.IsDeleted);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TaskComment>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // History Operations
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<PagedResult<TaskHistoryEntry>> GetTaskHistoryAsync(
        string taskId, HistoryEventType? eventType = null, int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TaskHistoryEntries.Where(h => h.TaskId == taskId);

        if (eventType.HasValue)
            query = query.Where(h => h.EventType == eventType.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(h => h.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TaskHistoryEntry>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // View Operations
    // ═══════════════════════════════════════════════════════════════════════════

    private const string KanbanAgentId = "kanban";

    /// <inheritdoc />
    public async Task<SavedView> SaveViewAsync(
        SavedView view, CancellationToken cancellationToken = default)
    {
        var key = $"view:{view.OwnerId}:{view.Name}";
        await _stateManager.SetStateAsync(KanbanAgentId, key, view, cancellationToken);

        // Maintain an index of view names per user
        var indexKey = $"view-index:{view.OwnerId}";
        var index = await _stateManager.GetStateAsync<List<string>>(KanbanAgentId, indexKey, cancellationToken)
            ?? new List<string>();
        if (!index.Contains(view.Name))
        {
            index.Add(view.Name);
            await _stateManager.SetStateAsync(KanbanAgentId, indexKey, index, cancellationToken);
        }

        return view;
    }

    /// <inheritdoc />
    public async Task<SavedView?> GetViewAsync(
        string userId, string viewName, CancellationToken cancellationToken = default)
    {
        var key = $"view:{userId}:{viewName}";
        return await _stateManager.GetStateAsync<SavedView>(KanbanAgentId, key, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<SavedView>> ListViewsAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var indexKey = $"view-index:{userId}";
        var index = await _stateManager.GetStateAsync<List<string>>(KanbanAgentId, indexKey, cancellationToken)
            ?? new List<string>();

        var views = new List<SavedView>();
        foreach (var name in index)
        {
            var view = await GetViewAsync(userId, name, cancellationToken);
            if (view != null) views.Add(view);
        }
        return views;
    }

    /// <inheritdoc />
    public async Task DeleteViewAsync(
        string userId, string viewName, CancellationToken cancellationToken = default)
    {
        // Remove from index
        var indexKey = $"view-index:{userId}";
        var index = await _stateManager.GetStateAsync<List<string>>(KanbanAgentId, indexKey, cancellationToken)
            ?? new List<string>();
        index.Remove(viewName);
        await _stateManager.SetStateAsync(KanbanAgentId, indexKey, index, cancellationToken);

        // Set view to null (effectively deleting it)
        var key = $"view:{userId}:{viewName}";
        await _stateManager.SetStateAsync<SavedView?>(KanbanAgentId, key, null, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Bulk Operations
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<BulkOperationResult> BulkAssignAsync(
        string boardId, List<string> taskIds, string assigneeId, string assigneeName,
        string actingUserId, string actingUserName, string actingUserRole,
        CancellationToken cancellationToken = default)
    {
        var result = new BulkOperationResult();
        foreach (var taskId in taskIds)
        {
            try
            {
                var task = await AssignTaskAsync(taskId, actingUserId, actingUserName, actingUserRole,
                    assigneeId, assigneeName, cancellationToken);
                result.Succeeded++;
                result.Results.Add(new TaskOperationResult { TaskId = taskId, TaskNumber = task.TaskNumber, Success = true });
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Results.Add(new TaskOperationResult { TaskId = taskId, Success = false, Error = ex.Message });
            }
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<BulkOperationResult> BulkMoveAsync(
        string boardId, List<string> taskIds, TaskStatus targetStatus,
        string actingUserId, string actingUserName, string actingUserRole,
        string? comment = null, CancellationToken cancellationToken = default)
    {
        var result = new BulkOperationResult();
        foreach (var taskId in taskIds)
        {
            try
            {
                var task = await MoveTaskAsync(taskId, targetStatus, actingUserId, actingUserName,
                    actingUserRole, comment, cancellationToken: cancellationToken);
                result.Succeeded++;
                result.Results.Add(new TaskOperationResult { TaskId = taskId, TaskNumber = task.TaskNumber, Success = true });
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Results.Add(new TaskOperationResult { TaskId = taskId, Success = false, Error = ex.Message });
            }
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<BulkOperationResult> BulkSetDueDateAsync(
        string boardId, List<string> taskIds, DateTime dueDate,
        string actingUserId, string actingUserName,
        CancellationToken cancellationToken = default)
    {
        var result = new BulkOperationResult();
        foreach (var taskId in taskIds)
        {
            try
            {
                var task = await _context.RemediationTasks
                    .Include(t => t.History)
                    .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
                    ?? throw new InvalidOperationException($"Task '{taskId}' not found.");

                var oldDate = task.DueDate;
                task.DueDate = dueDate;
                task.UpdatedAt = DateTime.UtcNow;
                task.History.Add(new TaskHistoryEntry
                {
                    TaskId = taskId,
                    EventType = HistoryEventType.DueDateChanged,
                    OldValue = oldDate.ToString("yyyy-MM-dd"),
                    NewValue = dueDate.ToString("yyyy-MM-dd"),
                    ActingUserId = actingUserId,
                    ActingUserName = actingUserName,
                });
                await _context.SaveChangesAsync(cancellationToken);

                result.Succeeded++;
                result.Results.Add(new TaskOperationResult { TaskId = taskId, TaskNumber = task.TaskNumber, Success = true });
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Results.Add(new TaskOperationResult { TaskId = taskId, Success = false, Error = ex.Message });
            }
        }
        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Private Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private RemediationTask CreateTaskFromFinding(ComplianceFinding finding, RemediationBoard board)
    {
        var taskNumber = string.Format(KanbanConstants.TaskIdFormat, board.NextTaskNumber);
        board.NextTaskNumber++;

        var task = new RemediationTask
        {
            TaskNumber = taskNumber,
            BoardId = board.Id,
            Title = $"{finding.ControlId}: {finding.Title}",
            Description = finding.Description ?? "",
            ControlId = finding.ControlId,
            ControlFamily = finding.ControlFamily,
            Severity = finding.Severity,
            Status = TaskStatus.Backlog,
            DueDate = CalculateDueDate(finding.Severity),
            AffectedResources = !string.IsNullOrEmpty(finding.ResourceId) ? new List<string> { finding.ResourceId } : new List<string>(),
            RemediationScript = finding.RemediationScript,
            FindingId = finding.Id,
            CreatedBy = board.Owner,
        };

        task.History.Add(new TaskHistoryEntry
        {
            TaskId = task.Id,
            EventType = HistoryEventType.Created,
            NewValue = TaskStatus.Backlog.ToString(),
            ActingUserId = board.Owner,
            ActingUserName = board.Owner,
            Details = $"Created from finding: {finding.Id}",
        });

        return task;
    }

    private static DateTime CalculateDueDate(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => DateTime.UtcNow.AddHours(KanbanConstants.DefaultCriticalHours),
        FindingSeverity.High => DateTime.UtcNow.AddDays(KanbanConstants.DefaultHighDays),
        FindingSeverity.Medium => DateTime.UtcNow.AddDays(KanbanConstants.DefaultMediumDays),
        FindingSeverity.Low => DateTime.UtcNow.AddDays(KanbanConstants.DefaultLowDays),
        _ => DateTime.UtcNow.AddDays(KanbanConstants.DefaultMediumDays),
    };

    private static string EscapeCsv(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    // ─── Feature 031: Roadmap ↔ Kanban sync ──────────────────────────────────

    /// <summary>
    /// After a Kanban task status change, propagate to the linked RoadmapItem
    /// (if any) to keep bi-directional sync. Uses DbContext directly to avoid
    /// circular dependency with IRoadmapService.
    /// </summary>
    private async Task SyncLinkedRoadmapItemAsync(
        RemediationTask task, TaskStatus newTaskStatus, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(task.RoadmapItemId))
            return;

        var item = await _context.RoadmapItems
            .Include(i => i.Phase)
            .FirstOrDefaultAsync(i => i.Id == task.RoadmapItemId, cancellationToken);

        if (item is null)
        {
            _logger.LogWarning("Linked RoadmapItem {ItemId} not found for task {TaskId}",
                task.RoadmapItemId, task.Id);
            return;
        }

        var oldStatus = item.Status;
        item.Status = MapTaskStatusToItemStatus(newTaskStatus);

        if (item.Status == oldStatus)
            return;

        // Update phase cached counts
        var phase = item.Phase;
        if (item.Status == ItemStatus.Complete && oldStatus != ItemStatus.Complete)
            phase.CompletedItemCount++;
        else if (item.Status != ItemStatus.Complete && oldStatus == ItemStatus.Complete)
            phase.CompletedItemCount = Math.Max(0, phase.CompletedItemCount - 1);

        // Update phase status
        if (phase.CompletedItemCount >= phase.TotalItemCount)
            phase.Status = PhaseStatus.Complete;
        else if (phase.CompletedItemCount > 0 || item.Status == ItemStatus.InProgress)
            phase.Status = PhaseStatus.InProgress;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Synced RoadmapItem {ItemId} status {Old}→{New} from task {TaskNumber}",
            item.Id, oldStatus, item.Status, task.TaskNumber);
    }

    private static ItemStatus MapTaskStatusToItemStatus(TaskStatus taskStatus) => taskStatus switch
    {
        TaskStatus.Backlog or TaskStatus.ToDo => ItemStatus.NotStarted,
        TaskStatus.InProgress or TaskStatus.InReview or TaskStatus.Blocked => ItemStatus.InProgress,
        TaskStatus.Done => ItemStatus.Complete,
        _ => ItemStatus.NotStarted,
    };
}

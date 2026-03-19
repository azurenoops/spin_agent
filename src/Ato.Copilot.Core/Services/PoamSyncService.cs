using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Manages bidirectional sync between POA&amp;M items and remediation tasks.
/// Uses CascadeOrigin tracking to prevent infinite loops per R-001/FR-008c/FR-008d.
/// </summary>
public class PoamSyncService
{
    private readonly AtoCopilotContext _db;
    private readonly PoamService _poamService;
    private readonly ILogger<PoamSyncService> _logger;

    public PoamSyncService(AtoCopilotContext db, PoamService poamService, ILogger<PoamSyncService> logger)
    {
        _db = db;
        _poamService = poamService;
        _logger = logger;
    }

    /// <summary>Creates a remediation task from a POA&amp;M item with field mapping and bidirectional FK linking.</summary>
    public async Task<RemediationTask> CreateTaskFromPoamAsync(
        string poamId,
        string boardId,
        string actingUserId,
        CancellationToken ct = default)
    {
        var poam = await _db.PoamItems
            .Include(p => p.ComponentLinks)
            .FirstOrDefaultAsync(p => p.Id == poamId, ct)
            ?? throw new InvalidOperationException($"POA&M '{poamId}' not found.");

        if (!string.IsNullOrEmpty(poam.RemediationTaskId))
            throw new InvalidOperationException("POAM_ALREADY_LINKED: This POA&M is already linked to a remediation task.");

        // Map fields: weakness→title, catSeverity→taskSeverity, poc→assignee
        var task = new RemediationTask
        {
            BoardId = boardId,
            Title = $"{poam.SecurityControlNumber}: {poam.Weakness[..Math.Min(100, poam.Weakness.Length)]}",
            Description = poam.Weakness,
            ControlId = poam.SecurityControlNumber,
            ControlFamily = poam.SecurityControlNumber.Length >= 2 ? poam.SecurityControlNumber[..2] : "",
            Severity = MapCatSeverityToFindingSeverity(poam.CatSeverity),
            Status = Models.Kanban.TaskStatus.Backlog,
            DueDate = poam.ScheduledCompletionDate,
            AssigneeName = poam.PointOfContact,
            FindingId = poam.FindingId,
            PoamItemId = poam.Id,
            CreatedBy = actingUserId,
        };

        _db.RemediationTasks.Add(task);

        // Set bidirectional FK
        poam.RemediationTaskId = task.Id;
        poam.ModifiedAt = DateTime.UtcNow;
        poam.ModifiedBy = actingUserId;

        // Audit entries on both sides
        _poamService.AddHistoryEntry(poam, PoamHistoryEventType.TaskLinked,
            null, task.Id, actingUserId, actingUserId,
            $"Created remediation task from POA&M", CascadeOrigin.FromPoam);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created task {TaskId} from POA&M {PoamId}", task.Id, poamId);
        return task;
    }

    /// <summary>Links an existing remediation task to a POA&amp;M item with bidirectional FK setting.</summary>
    public async Task LinkAsync(string poamId, string taskId, string actingUserId, CancellationToken ct = default)
    {
        var poam = await _db.PoamItems.FindAsync(new object[] { poamId }, ct)
            ?? throw new InvalidOperationException($"POA&M '{poamId}' not found.");
        var task = await _db.RemediationTasks.FindAsync(new object[] { taskId }, ct)
            ?? throw new InvalidOperationException($"Task '{taskId}' not found.");

        if (!string.IsNullOrEmpty(poam.RemediationTaskId))
            throw new InvalidOperationException("POAM_ALREADY_LINKED: This POA&M is already linked to a task.");
        if (!string.IsNullOrEmpty(task.PoamItemId))
            throw new InvalidOperationException("TASK_ALREADY_LINKED: This task is already linked to a POA&M.");

        poam.RemediationTaskId = taskId;
        poam.ModifiedAt = DateTime.UtcNow;
        task.PoamItemId = poamId;
        task.UpdatedAt = DateTime.UtcNow;

        _poamService.AddHistoryEntry(poam, PoamHistoryEventType.TaskLinked,
            null, taskId, actingUserId, actingUserId, "Linked to existing task");

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Linked POA&M {PoamId} ↔ Task {TaskId}", poamId, taskId);
    }

    /// <summary>Unlinks a POA&amp;M and its remediation task, clearing both FKs.</summary>
    public async Task UnlinkAsync(string poamId, string actingUserId, CancellationToken ct = default)
    {
        var poam = await _db.PoamItems.FindAsync(new object[] { poamId }, ct)
            ?? throw new InvalidOperationException($"POA&M '{poamId}' not found.");

        if (string.IsNullOrEmpty(poam.RemediationTaskId))
            throw new InvalidOperationException("POAM_NOT_LINKED: This POA&M is not linked to any task.");

        var task = await _db.RemediationTasks.FindAsync(new object[] { poam.RemediationTaskId }, ct);

        var oldTaskId = poam.RemediationTaskId;
        poam.RemediationTaskId = null;
        poam.ModifiedAt = DateTime.UtcNow;

        if (task != null)
        {
            task.PoamItemId = null;
            task.UpdatedAt = DateTime.UtcNow;
        }

        _poamService.AddHistoryEntry(poam, PoamHistoryEventType.TaskUnlinked,
            oldTaskId, null, actingUserId, actingUserId, "Unlinked task from POA&M");

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Unlinked POA&M {PoamId} from Task {TaskId}", poamId, oldTaskId);
    }

    /// <summary>Cascades a POA&amp;M status change to the linked task (with CascadeOrigin to prevent loops).</summary>
    public async Task CascadeStatusChangeAsync(
        string poamId,
        PoamStatus newPoamStatus,
        CascadeOrigin origin,
        string actingUserId,
        CancellationToken ct = default)
    {
        // Prevent infinite loops — only cascade from POA&M side
        if (origin == CascadeOrigin.FromTask) return;

        var poam = await _db.PoamItems.FindAsync(new object[] { poamId }, ct);
        if (poam == null || string.IsNullOrEmpty(poam.RemediationTaskId)) return;

        var task = await _db.RemediationTasks.FindAsync(new object[] { poam.RemediationTaskId }, ct);
        if (task == null) return;

        var newTaskStatus = MapPoamStatusToTaskStatus(newPoamStatus);
        if (task.Status == newTaskStatus) return;

        var oldStatus = task.Status;
        task.Status = newTaskStatus;
        task.UpdatedAt = DateTime.UtcNow;

        _poamService.AddHistoryEntry(poam, PoamHistoryEventType.CascadeApplied,
            oldStatus.ToString(), newTaskStatus.ToString(), actingUserId, actingUserId,
            $"Cascaded status to task: {oldStatus} → {newTaskStatus}", CascadeOrigin.FromPoam);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Cascaded POA&M {PoamId} status {Status} → Task {TaskId}", poamId, newPoamStatus, task.Id);
    }

    /// <summary>Cascades metadata changes (due date, severity) from POA&amp;M to linked task.</summary>
    public async Task CascadeMetadataChangeAsync(
        string poamId,
        DateTime? newDueDate,
        CatSeverity? newSeverity,
        CascadeOrigin origin,
        string actingUserId,
        CancellationToken ct = default)
    {
        if (origin == CascadeOrigin.FromTask) return;

        var poam = await _db.PoamItems.FindAsync(new object[] { poamId }, ct);
        if (poam == null || string.IsNullOrEmpty(poam.RemediationTaskId)) return;

        var task = await _db.RemediationTasks.FindAsync(new object[] { poam.RemediationTaskId }, ct);
        if (task == null) return;

        var changes = new List<string>();

        if (newDueDate.HasValue && task.DueDate != newDueDate.Value)
        {
            var old = task.DueDate;
            task.DueDate = newDueDate.Value;
            changes.Add($"due date: {old:yyyy-MM-dd} → {newDueDate.Value:yyyy-MM-dd}");
        }

        if (newSeverity.HasValue)
        {
            var newTaskSev = MapCatSeverityToFindingSeverity(newSeverity.Value);
            if (task.Severity != newTaskSev)
            {
                var old = task.Severity;
                task.Severity = newTaskSev;
                changes.Add($"severity: {old} → {newTaskSev}");
            }
        }

        if (changes.Count == 0) return;

        task.UpdatedAt = DateTime.UtcNow;

        _poamService.AddHistoryEntry(poam, PoamHistoryEventType.CascadeApplied,
            null, null, actingUserId, actingUserId,
            $"Cascaded to task: {string.Join(", ", changes)}", CascadeOrigin.FromPoam);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Cascaded POA&M {PoamId} metadata to Task {TaskId}: {Changes}", poamId, task.Id, string.Join(", ", changes));
    }

    // ─── Mapping Helpers ─────────────────────────────────────────────────────

    private static FindingSeverity MapCatSeverityToFindingSeverity(CatSeverity cat) => cat switch
    {
        CatSeverity.CatI => FindingSeverity.Critical,
        CatSeverity.CatII => FindingSeverity.High,
        CatSeverity.CatIII => FindingSeverity.Medium,
        _ => FindingSeverity.Low
    };

    private static Models.Kanban.TaskStatus MapPoamStatusToTaskStatus(PoamStatus status) => status switch
    {
        PoamStatus.Ongoing => Models.Kanban.TaskStatus.InProgress,
        PoamStatus.Delayed => Models.Kanban.TaskStatus.Blocked,
        PoamStatus.Completed => Models.Kanban.TaskStatus.Done,
        PoamStatus.RiskAccepted => Models.Kanban.TaskStatus.Done,
        _ => Models.Kanban.TaskStatus.Backlog
    };
}

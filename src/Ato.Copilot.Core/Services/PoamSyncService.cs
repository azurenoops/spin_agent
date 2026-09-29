using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services.Roles;

namespace Ato.Copilot.Core.Services;

/// <summary>Maintains explicit, same-system task relationships without lifecycle cascades.</summary>
public class PoamSyncService(AtoCopilotContext db, PoamService poamService, ILogger<PoamSyncService> logger)
{
    public async Task<RemediationTask> CreateTaskFromPoamAsync(
        string poamId, string boardId, string actingUserId, CancellationToken ct = default)
    {
        var poam = await db.PoamItems.SingleOrDefaultAsync(p => p.Id == poamId, ct)
            ?? throw new InvalidOperationException("POA&M not found.");
        await SystemWorkspaceAccessPolicy.RequireAsync(db, poam.RegisteredSystemId, p => p.CanManageRemediation, ct);
        var board = await db.RemediationBoards.SingleOrDefaultAsync(b => b.Id == boardId && b.TenantId == poam.TenantId, ct)
            ?? throw new InvalidOperationException("Board not found.");
        var task = new RemediationTask
        {
            TenantId = poam.TenantId, BoardId = boardId,
            Title = $"{poam.SecurityControlNumber}: {poam.Weakness[..Math.Min(100, poam.Weakness.Length)]}",
            Description = poam.Weakness, ControlId = poam.SecurityControlNumber,
            ControlFamily = poam.SecurityControlNumber.Split('-')[0],
            Severity = poam.CatSeverity switch { CatSeverity.CatI => FindingSeverity.Critical,
                CatSeverity.CatII => FindingSeverity.High, _ => FindingSeverity.Medium },
            DueDate = poam.ScheduledCompletionDate, AssigneeName = poam.PointOfContact,
            FindingId = poam.FindingId, CreatedBy = actingUserId,
        };
        // A board without retained system provenance cannot be attached by subscription.
        if (await RemediationScope.TaskSystemAsync(db, task, ct) != poam.RegisteredSystemId)
            throw new InvalidOperationException("TASK_SCOPE_UNRESOLVED: Board/finding must establish the POA&M system.");
        task.RegisteredSystemId = poam.RegisteredSystemId;
        task.TaskNumber = $"REM-{board.NextTaskNumber++:D3}";
        board.UpdatedAt = DateTime.UtcNow;
        db.RemediationTasks.Add(task);
        await RetainLegacyLinksAsync(poam, task, ct);
        AddLink(poam, task, actingUserId);
        await RefreshHintsAsync(poam, task, ct);
        await db.SaveChangesAsync(ct);
        return task;
    }

    public async Task LinkAsync(string poamId, string taskId, string actingUserId, CancellationToken ct = default)
    {
        var (poam, task) = await ResolveAsync(poamId, taskId, ct);
        await RetainLegacyLinksAsync(poam, task, ct);
        if (await HasLinkAsync(poamId, taskId, ct)) return;
        AddLink(poam, task, actingUserId);
        await RefreshHintsAsync(poam, task, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UnlinkAsync(string poamId, string actingUserId, CancellationToken ct = default)
    {
        var poam = await db.PoamItems.SingleOrDefaultAsync(p => p.Id == poamId, ct)
            ?? throw new InvalidOperationException("POA&M not found.");
        var ids = await db.PoamTaskLinks.Where(l => l.PoamItemId == poamId).Select(l => l.RemediationTaskId).ToListAsync(ct);
        if (poam.RemediationTaskId is not null) ids.Add(poam.RemediationTaskId);
        ids.AddRange(await db.RemediationTasks.Where(t => t.PoamItemId == poamId).Select(t => t.Id).ToListAsync(ct));
        ids = ids.Distinct().ToList();
        if (ids.Count > 1) throw new InvalidOperationException("AMBIGUOUS_UNLINK: Specify a task ID.");
        if (ids.Count == 1) await UnlinkAsync(poamId, ids[0], actingUserId, ct);
    }

    public async Task UnlinkAsync(string poamId, string taskId, string actingUserId, CancellationToken ct = default)
    {
        var (poam, task) = await ResolveAsync(poamId, taskId, ct);
        await RetainLegacyLinksAsync(poam, task, ct);
        var link = db.PoamTaskLinks.Local.FirstOrDefault(l => l.PoamItemId == poamId && l.RemediationTaskId == taskId)
            ?? await db.PoamTaskLinks.SingleOrDefaultAsync(l => l.PoamItemId == poamId && l.RemediationTaskId == taskId, ct);
        if (link is null) return;
        db.PoamTaskLinks.Remove(link);
        Audit(poam, task, actingUserId, false);
        await RefreshHintsAsync(poam, task, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<(PoamItem Poam, RemediationTask Task)> ResolveAsync(string poamId, string taskId, CancellationToken ct)
    {
        var poam = await db.PoamItems.SingleOrDefaultAsync(p => p.Id == poamId, ct)
            ?? throw new InvalidOperationException("POA&M not found.");
        await SystemWorkspaceAccessPolicy.RequireAsync(db, poam.RegisteredSystemId, p => p.CanManageRemediation, ct);
        var task = await db.RemediationTasks.SingleOrDefaultAsync(t => t.Id == taskId && t.TenantId == poam.TenantId, ct)
            ?? throw new InvalidOperationException("Task not found.");
        if (await RemediationScope.TaskSystemAsync(db, task, ct) != poam.RegisteredSystemId)
            throw new InvalidOperationException("CROSS_SYSTEM_LINK: Task and POA&M must have the same retained system owner.");
        return (poam, task);
    }

    private async Task<bool> HasLinkAsync(string poamId, string taskId, CancellationToken ct) =>
        db.PoamTaskLinks.Local.Any(l => l.PoamItemId == poamId && l.RemediationTaskId == taskId) ||
        await db.PoamTaskLinks.AnyAsync(l => l.PoamItemId == poamId && l.RemediationTaskId == taskId, ct);

    private async Task RetainLegacyLinksAsync(PoamItem poam, RemediationTask task, CancellationToken ct)
    {
        var pairs = new HashSet<(string Poam, string Task)>();
        if (poam.RemediationTaskId is not null) pairs.Add((poam.Id, poam.RemediationTaskId));
        if (task.PoamItemId is not null) pairs.Add((task.PoamItemId, task.Id));
        foreach (var legacyTask in await db.RemediationTasks.Where(t => t.PoamItemId == poam.Id).ToListAsync(ct))
            pairs.Add((poam.Id, legacyTask.Id));
        foreach (var legacyPoam in await db.PoamItems.Where(p => p.RemediationTaskId == task.Id).ToListAsync(ct))
            pairs.Add((legacyPoam.Id, task.Id));
        foreach (var pair in pairs)
        {
            if (await HasLinkAsync(pair.Poam, pair.Task, ct)) continue;
            var (p, t) = await ResolveAsync(pair.Poam, pair.Task, ct);
            db.PoamTaskLinks.Add(new PoamTaskLink { TenantId = p.TenantId, RegisteredSystemId = p.RegisteredSystemId,
                PoamItemId = p.Id, RemediationTaskId = t.Id, LinkedBy = "legacy-migration" });
        }
    }

    private void AddLink(PoamItem poam, RemediationTask task, string actor)
    {
        db.PoamTaskLinks.Add(new PoamTaskLink { TenantId = poam.TenantId, RegisteredSystemId = poam.RegisteredSystemId,
            PoamItemId = poam.Id, RemediationTaskId = task.Id, LinkedBy = actor });
        Audit(poam, task, actor, true);
    }

    private void Audit(PoamItem poam, RemediationTask task, string actor, bool linked)
    {
        poam.ModifiedAt = task.UpdatedAt = DateTime.UtcNow;
        poam.ModifiedBy = actor;
        poamService.AddHistoryEntry(poam, linked ? PoamHistoryEventType.TaskLinked : PoamHistoryEventType.TaskUnlinked,
            linked ? null : task.Id, linked ? task.Id : null, actor, actor, linked ? "Task linked" : "Task unlinked");
        task.History.Add(new TaskHistoryEntry { TaskId = task.Id, ActingUserId = actor, ActingUserName = actor,
            EventType = linked ? HistoryEventType.RelationshipLinked : HistoryEventType.RelationshipUnlinked,
            OldValue = linked ? null : poam.Id, NewValue = linked ? poam.Id : null,
            Details = linked ? "POA&M relationship linked" : "POA&M relationship unlinked" });
    }

    private async Task RefreshHintsAsync(PoamItem poam, RemediationTask task, CancellationToken ct)
    {
        await db.PoamTaskLinks.Where(l => l.PoamItemId == poam.Id || l.RemediationTaskId == task.Id).LoadAsync(ct);
        var links = db.ChangeTracker.Entries<PoamTaskLink>().Where(e => e.State != EntityState.Deleted)
            .Select(e => e.Entity).ToArray();
        var taskIds = links.Where(l => l.PoamItemId == poam.Id).Select(l => l.RemediationTaskId).Distinct().ToArray();
        var poamIds = links.Where(l => l.RemediationTaskId == task.Id).Select(l => l.PoamItemId).Distinct().ToArray();
        poam.RemediationTaskId = taskIds.Length == 1 ? taskIds[0] : null;
        task.PoamItemId = poamIds.Length == 1 ? poamIds[0] : null;
    }

    // Compatibility entry points deliberately no longer mutate independent work/decision states.
    public Task CascadeStatusChangeAsync(string poamId, PoamStatus newPoamStatus, CascadeOrigin origin,
        string actingUserId, CancellationToken ct = default)
    {
        logger.LogDebug("POA&M {PoamId} status remains independent from task state", poamId);
        return Task.CompletedTask;
    }

    public Task CascadeMetadataChangeAsync(string poamId, DateTime? newDueDate, CatSeverity? newSeverity,
        CascadeOrigin origin, string actingUserId, CancellationToken ct = default)
    {
        logger.LogDebug("POA&M {PoamId} metadata remains independent from shared task metadata", poamId);
        return Task.CompletedTask;
    }
}

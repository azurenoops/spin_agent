using System.Text.Json;
using System.Text.RegularExpressions;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Services;
using Microsoft.EntityFrameworkCore;
using TaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;
using RemediationHistory = Ato.Copilot.Core.Dtos.Dashboard.RemediationHistory;
using Ato.Copilot.Core.Services.Roles;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed class RemediationWorkspaceService(AtoCopilotContext db, IKanbanService kanban)
{
    public async Task<RemediationWorkspace> GetAsync(Guid tenantId, string systemId, RemediationPermissions permissions, CancellationToken ct)
    {
        await SystemAsync(tenantId, systemId, ct);
        var assessments = await db.Assessments.Where(a => a.TenantId == tenantId && a.RegisteredSystemId == systemId).ToListAsync(ct);
        var imports = await db.ScanImportRecords.Where(a => a.TenantId == tenantId && a.RegisteredSystemId == systemId).ToListAsync(ct);
        var assessmentIds = assessments.Select(a => a.Id).ToArray();
        var importIds = imports.Select(a => a.Id).ToArray();
        var candidates = await db.Findings.Where(f => f.TenantId == tenantId &&
            (assessmentIds.Contains(f.AssessmentId) || importIds.Contains(f.ImportRecordId!))).ToListAsync(ct);
        var findings = new List<ComplianceFinding>();
        foreach (var finding in candidates)
            if (await RemediationScope.FindingSystemAsync(db, finding, ct) == systemId) findings.Add(finding);
        var findingIds = findings.Select(f => f.Id).ToArray();
        var boardIds = await db.RemediationBoards.Where(b => b.TenantId == tenantId && assessmentIds.Contains(b.AssessmentId!))
            .Select(b => b.Id).ToArrayAsync(ct);
        var taskCandidates = await db.RemediationTasks.Include(t => t.History)
            .Where(t => t.TenantId == tenantId && (t.RegisteredSystemId == systemId ||
                findingIds.Contains(t.FindingId!) || boardIds.Contains(t.BoardId))).ToListAsync(ct);
        var tasks = new List<RemediationTask>();
        foreach (var task in taskCandidates)
            if (await RemediationScope.TaskSystemAsync(db, task, ct) == systemId) tasks.Add(task);
        var poams = await db.PoamItems.Include(p => p.Milestones).Include(p => p.History)
            .Where(p => p.TenantId == tenantId && p.RegisteredSystemId == systemId).ToListAsync(ct);
        var exceptions = await db.Deviations.Where(d => d.TenantId == tenantId && d.RegisteredSystemId == systemId).ToListAsync(ct);
        var links = await db.PoamTaskLinks.Where(l => l.TenantId == tenantId && l.RegisteredSystemId == systemId).ToListAsync(ct);
        var validTaskIds = tasks.Select(t => t.Id).ToHashSet();
        var validPoamIds = poams.Select(p => p.Id).ToHashSet();
        var pairs = links.Where(l => validTaskIds.Contains(l.RemediationTaskId) && validPoamIds.Contains(l.PoamItemId))
            .Select(l => (Poam: l.PoamItemId, Task: l.RemediationTaskId)).ToHashSet();
        foreach (var p in poams.Where(p => p.RemediationTaskId is not null && validTaskIds.Contains(p.RemediationTaskId)))
            pairs.Add((p.Id, p.RemediationTaskId!));
        foreach (var t in tasks.Where(t => t.PoamItemId is not null && validPoamIds.Contains(t.PoamItemId)))
            pairs.Add((t.PoamItemId!, t.Id));

        RemediationProvenance? Provenance(ComplianceFinding f)
        {
            var import = imports.FirstOrDefault(i => i.Id == f.ImportRecordId);
            if (import is not null) return new(import.Id, import.FileName, import.ImportType.ToString(),
                AssessmentResultProvenance.Read(import.ResultProvenanceJson).Plan);
            var assessment = assessments.FirstOrDefault(a => a.Id == f.AssessmentId);
            return assessment is null ? null : new(assessment.Id,
                assessment.ScanType == "manual" ? "Manual finding" : $"{assessment.ScanType} assessment · {assessment.AssessedAt:yyyy-MM-dd}",
                assessment.ScanType == "manual" ? "Manual" : "Assessment",
                AssessmentResultProvenance.Read(assessment.ResultProvenanceJson).Plan);
        }

        var findingRows = findings.OrderByDescending(f => f.DiscoveredAt).Select(f => new RemediationFindingRow(
            f.Id, f.Title, f.Description, f.ControlId, f.Severity.ToString(), f.Status.ToString(), f.Source,
            f.AssessmentId, f.ImportRecordId, f.DiscoveredAt,
            tasks.Where(t => t.FindingId == f.Id).Select(t => t.Id).ToArray(),
            poams.Where(p => p.FindingId == f.Id || pairs.Any(l => l.Poam == p.Id && tasks.Any(t => t.Id == l.Task && t.FindingId == f.Id)))
                .Select(p => p.Id).ToArray(),
            exceptions.Any(d => d.Id == f.DeviationId) ? f.DeviationId : null, Provenance(f))).ToArray();
        var taskRows = tasks.OrderBy(t => t.DueDate).Select(t => new RemediationTaskRow(t.Id, t.TaskNumber, t.BoardId,
            t.Title, t.Description, t.ControlId, t.Severity.ToString(), t.Status.ToString(), t.AssigneeId, t.AssigneeName,
            t.DueDate, findingIds.Contains(t.FindingId) ? t.FindingId : null, pairs.Where(l => l.Task == t.Id).Select(l => l.Poam).Order().ToArray(),
            t.RowVersion, t.VerificationStatus, t.VerificationNotes, t.VerifiedBy, t.VerifiedAt, Evidence(t),
            t.History.OrderByDescending(h => h.Timestamp).Select(h => new RemediationHistory(h.Id, h.EventType.ToString(),
                h.OldValue, h.NewValue, h.ActingUserName, h.Timestamp, h.Details)).ToArray(),
            StatusTransitionEngine.GetAllowedTransitions(t.Status).Select(s => s.ToString()).ToArray(),
            t.AffectedResources.ToArray(), t.ValidationCriteria, t.RemediationScript, t.RemediationScriptType)).ToArray();
        var poamRows = poams.OrderBy(p => p.ScheduledCompletionDate).Select(p => new RemediationPoamRow(
            p.Id, p.Id, p.Weakness, p.WeaknessSource, p.SecurityControlNumber, p.CatSeverity.ToString(), p.Status.ToString(),
            p.PointOfContact, p.PocEmail, p.ScheduledCompletionDate, p.ActualCompletionDate,
            findingIds.Contains(p.FindingId) ? p.FindingId : null, pairs.Where(l => l.Poam == p.Id).Select(l => l.Task).Order().ToArray(), p.RowVersion,
            p.Milestones.OrderBy(m => m.Sequence).Select(m => new RemediationMilestone(m.Id, m.Description, m.TargetDate, m.CompletedDate, m.Sequence)).ToArray(),
            p.History.OrderByDescending(h => h.Timestamp).Select(h => new RemediationHistory(h.Id, h.EventType.ToString(),
                h.OldValue, h.NewValue, h.ActingUserName, h.Timestamp, h.Details)).ToArray(),
            exceptions.Any(d => d.Id == p.DeviationId) ? p.DeviationId : null, p.Comments, p.ResourcesRequired, p.CostEstimate, p.CreatedAt, p.ModifiedAt)).ToArray();
        var now = DateTime.UtcNow;
        return new(systemId, findingRows, taskRows, poamRows,
            exceptions.Select(d => new RemediationExceptionRow(d.Id, d.DeviationType.ToString(), d.Status.ToString(),
                d.ControlId, d.Justification, d.ExpirationDate, d.Status == DeviationStatus.Approved && d.ExpirationDate > now,
                findingIds.Contains(d.FindingId) ? d.FindingId : null, validPoamIds.Contains(d.PoamEntryId ?? "") ? d.PoamEntryId : null,
                d.ReviewedBy, d.ReviewerRole, d.ReviewedAt, d.CompensatingControls)).ToArray(),
            permissions, new(findings.Count, tasks.Count, poams.Count,
                findings.Count(f => f.Status is FindingStatus.Open or FindingStatus.InProgress),
                tasks.Count(t => t.Status != TaskStatus.Done),
                poams.Count(p => p.Status is PoamStatus.Ongoing or PoamStatus.Delayed),
                tasks.Count(t => t.Status != TaskStatus.Done && t.DueDate < now),
                poams.Count(p => p.Status is PoamStatus.Ongoing or PoamStatus.Delayed && p.ScheduledCompletionDate < now)),
            await OwnersAsync(tenantId, systemId, ct));
    }

    private async Task<RemediationOwner[]> OwnersAsync(Guid tenantId, string systemId, CancellationToken ct)
    {
        var members = await (from membership in db.OrganizationMemberships.AsNoTracking()
            join person in db.Persons.AsNoTracking() on membership.PersonId equals person.Id
            where membership.TenantId == tenantId && person.TenantId == tenantId && membership.RevokedAt == null
            select new { person.Id, person.DisplayName, membership.ObjectId }).ToListAsync(ct);
        var owners = new List<RemediationOwner>();
        foreach (var person in members.GroupBy(p => p.Id))
        {
            var roles = await SystemWorkspaceAccessPolicy.ResolveRolesAsync(db, tenantId, person.Key, systemId, ct);
            if (!SystemWorkspaceAccessPolicy.Permissions(roles, false, false).CanMoveOwnRemediationTasks) continue;
            owners.AddRange(person.Where(p => p.ObjectId != Guid.Empty)
                .Select(p => new RemediationOwner(p.ObjectId.ToString(), p.DisplayName)));
        }
        return owners.DistinctBy(o => o.Id).OrderBy(o => o.Name).ThenBy(o => o.Id).ToArray();
    }

    public async Task<string> CreateFindingAsync(Guid tenantId, string systemId, CreateRemediationFinding request, string actor, CancellationToken ct)
    {
        await SystemAsync(tenantId, systemId, ct);
        await SystemWorkspaceAccessPolicy.RequireAsync(db, systemId, p => p.CanManageRemediation, ct);
        ValidateInput(request.Title, request.Description, request.ControlId, request.Severity);
        var normalizedControl = request.ControlId.Trim().ToUpperInvariant();
        if (!await db.NistControls.AnyAsync(c => c.Id == normalizedControl, ct))
            throw new ArgumentException("The control ID must exist in the retained NIST control catalog.");
        var key = OperationKey(tenantId, systemId, "finding", request.RequestId);
        var intent = AssessmentResultProvenance.Hash(JsonSerializer.Serialize(request));
        var existing = await db.Assessments.SingleOrDefaultAsync(a => a.WorkspaceOperationKey == key && a.TenantId == tenantId &&
            a.RegisteredSystemId == systemId, ct);
        if (existing is not null)
        {
            if (AssessmentResultProvenance.Read(existing.ResultProvenanceJson).RequestIntentHash != intent)
                throw new InvalidOperationException("REQUEST_ID_REUSED: Use a new request ID for different content.");
            return await db.Findings.Where(f => f.AssessmentId == existing.Id && f.TenantId == tenantId).Select(f => f.Id).SingleAsync(ct);
        }
        var assessment = new ComplianceAssessment { TenantId = tenantId, RegisteredSystemId = systemId, ScanType = "manual",
            Status = AssessmentStatus.Completed, InitiatedBy = actor, CompletedAt = DateTime.UtcNow, WorkspaceOperationKey = key,
            ResultProvenanceJson = new AssessmentResultProvenance { SystemId = systemId, RequestIntentHash = intent }.Serialize() };
        var finding = new ComplianceFinding { TenantId = tenantId, AssessmentId = assessment.Id, Title = request.Title.Trim(),
            Description = request.Description.Trim(), ControlId = request.ControlId.Trim().ToUpperInvariant(),
            ControlFamily = request.ControlId.Trim().Split('-')[0].ToUpperInvariant(), Severity = Enum.Parse<FindingSeverity>(request.Severity, true),
            Source = "Manual", Status = FindingStatus.Open };
        db.Assessments.Add(assessment);
        db.Findings.Add(finding);
        Activity(tenantId, systemId, actor, "FindingCreated", "ComplianceFinding", finding.Id, "Manually recorded finding");
        await db.SaveChangesAsync(ct);
        return finding.Id;
    }

    public async Task<string> CreateTaskAsync(Guid tenantId, string systemId, CreateWorkspaceTask request, string actor, CancellationToken ct)
    {
        await SystemAsync(tenantId, systemId, ct);
        await SystemWorkspaceAccessPolicy.RequireAsync(db, systemId, p => p.CanCreateRemediationTasks, ct);
        ValidateInput(request.Title, request.Description, request.ControlId, request.Severity);
        if (request.FindingId is not null) await FindingAsync(tenantId, systemId, request.FindingId, ct);
        var key = OperationKey(tenantId, systemId, "task", request.RequestId);
        var intent = AssessmentResultProvenance.Hash(JsonSerializer.Serialize(request));
        var existing = await db.RemediationTasks.SingleOrDefaultAsync(t => t.WorkspaceOperationKey == key &&
            t.TenantId == tenantId && t.RegisteredSystemId == systemId, ct);
        if (existing is not null)
        {
            if (existing.WorkspaceIntentHash != intent) throw new InvalidOperationException("REQUEST_ID_REUSED: Use a new request ID for different content.");
            return existing.Id;
        }
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var boardId = new Guid(Convert.FromHexString(AssessmentResultProvenance.Hash($"{tenantId}/{systemId}/remediation-board")[..32])).ToString();
        var board = await db.RemediationBoards.SingleOrDefaultAsync(b => b.Id == boardId && b.TenantId == tenantId, ct);
        if (board is null)
        {
            board = new RemediationBoard { Id = boardId, TenantId = tenantId, Name = "System remediation", Owner = actor };
            db.RemediationBoards.Add(board);
            await db.SaveChangesAsync(ct);
        }
        var task = await kanban.CreateTaskAsync(board.Id, request.Title.Trim(), request.ControlId.Trim().ToUpperInvariant(), actor,
            description: request.Description.Trim(), severity: Enum.Parse<FindingSeverity>(request.Severity, true),
            dueDate: request.DueDate, cancellationToken: ct);
        task.TenantId = tenantId;
        task.RegisteredSystemId = systemId;
        task.FindingId = request.FindingId;
        task.WorkspaceOperationKey = key;
        task.WorkspaceIntentHash = intent;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return task.Id;
    }

    public async Task UpdateTaskAsync(Guid tenantId, string systemId, string taskId, UpdateWorkspaceTask request,
        string actor, string actorName, string role, CancellationToken ct)
    {
        var task = await TaskAsync(tenantId, systemId, taskId, ct);
        await SystemWorkspaceAccessPolicy.RequireAsync(db, systemId, p => p.CanManageRemediation, ct);
        CheckVersion(task, request.RowVersion);
        if (task.Status == TaskStatus.Done) throw new InvalidOperationException("Completed tasks cannot be edited.");
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 500 || request.Description is null || request.Description.Length > 4000 ||
            request.AssigneeId?.Length > 200 || request.AssigneeName?.Length > 200 || request.DueDate == default)
            throw new ArgumentException("A title up to 500 characters, description up to 4000 characters, assignee fields up to 200 characters and a due date are required.");
        if (task.Title != request.Title.Trim())
            History(task, actor, "Task title updated", task.Title, request.Title.Trim());
        if (task.Description != request.Description.Trim())
            History(task, actor, "Task description updated (SHA-256 content hashes)",
                AssessmentResultProvenance.Hash(task.Description), AssessmentResultProvenance.Hash(request.Description.Trim()));
        if (task.DueDate != request.DueDate)
            History(task, actor, "Task due date updated", task.DueDate.ToString("O"), request.DueDate.ToString("O"), HistoryEventType.DueDateChanged);
        if (task.Title != request.Title.Trim() || task.Description != request.Description.Trim()) ResetVerification(task);
        task.Title = request.Title.Trim();
        task.Description = request.Description.Trim();
        task.DueDate = request.DueDate;
        task.UpdatedAt = DateTime.UtcNow;
        if (task.AssigneeId != request.AssigneeId || task.AssigneeName != request.AssigneeName)
            await kanban.AssignTaskAsync(taskId, actor, actorName, role, request.AssigneeId, request.AssigneeName, ct);
        else await db.SaveChangesAsync(ct);
    }

    public async Task MoveAsync(Guid tenantId, string systemId, string taskId, MoveWorkspaceTask request,
        string actor, string actorName, string role, CancellationToken ct)
    {
        var task = await TaskAsync(tenantId, systemId, taskId, ct);
        await SystemWorkspaceAccessPolicy.RequireAsync(db, systemId,
            p => p.CanMoveAnyRemediationTasks || p.CanMoveOwnRemediationTasks && task.AssigneeId == actor, ct);
        CheckVersion(task, request.RowVersion);
        if (!Enum.TryParse<TaskStatus>(request.Status, true, out var status) || !Enum.IsDefined(status))
            throw new ArgumentException("Invalid task status.");
        if (request.Comment?.Length > 3800) throw new ArgumentException("Transition comments must be at most 3800 characters.");
        await kanban.MoveTaskAsync(taskId, status, actor, actorName, role, request.Comment, request.SkipValidation, ct);
    }

    public async Task LinkEvidenceAsync(Guid tenantId, string systemId, string taskId, LinkTaskEvidence request, string actor, CancellationToken ct)
    {
        var task = await TaskAsync(tenantId, systemId, taskId, ct);
        await SystemWorkspaceAccessPolicy.RequireAsync(db, systemId, p => p.CanManageRemediation, ct);
        CheckVersion(task, request.RowVersion);
        var artifact = await db.EvidenceArtifacts.SingleOrDefaultAsync(e => e.Id == request.EvidenceId && e.TenantId == tenantId &&
            e.RegisteredSystemId == systemId && !e.IsDeleted, ct) ?? throw new KeyNotFoundException();
        var evidence = Evidence(task).ToList();
        if (evidence.Any(e => e.Id == artifact.Id && e.ContentHash == artifact.ContentHash)) return;
        evidence.RemoveAll(e => e.Id == artifact.Id);
        evidence.Add(new(artifact.Id, artifact.FileName, artifact.ContentHash, DateTime.UtcNow, actor));
        task.EvidenceReferencesJson = JsonSerializer.Serialize(evidence);
        ResetVerification(task);
        task.UpdatedAt = DateTime.UtcNow;
        History(task, actor, "Evidence reference linked; verification reset", null, artifact.Id, HistoryEventType.EvidenceLinked);
        await db.SaveChangesAsync(ct);
    }

    public async Task VerifyAsync(Guid tenantId, string systemId, string taskId, VerifyWorkspaceTask request, string actor, CancellationToken ct)
    {
        var task = await TaskAsync(tenantId, systemId, taskId, ct);
        await SystemWorkspaceAccessPolicy.RequireAsync(db, systemId, p => p.CanManageRemediation, ct);
        CheckVersion(task, request.RowVersion);
        if (request.Status is not ("Passed" or "Failed") || string.IsNullOrWhiteSpace(request.Notes) || request.Notes.Length > 3900)
            throw new ArgumentException("Verification requires Passed or Failed and review notes up to 3900 characters.");
        var old = task.VerificationStatus;
        task.VerificationStatus = request.Status;
        task.VerificationNotes = request.Notes.Trim();
        task.VerifiedBy = actor;
        task.VerifiedAt = task.UpdatedAt = DateTime.UtcNow;
        History(task, actor, $"Manual verification: {request.Notes.Trim()}", old, request.Status, HistoryEventType.VerificationRecorded);
        await db.SaveChangesAsync(ct);
    }

    public async Task<RemediationTask> TaskAsync(Guid tenantId, string systemId, string taskId, CancellationToken ct)
    {
        await SystemAsync(tenantId, systemId, ct);
        var task = await db.RemediationTasks.SingleOrDefaultAsync(t => t.Id == taskId && t.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException();
        if (await RemediationScope.TaskSystemAsync(db, task, ct) != systemId) throw new KeyNotFoundException();
        return task;
    }

    public async Task LinkFindingTaskAsync(Guid tenantId, string systemId, string findingId, string taskId,
        Guid rowVersion, string actor, CancellationToken ct)
    {
        var task = await TaskAsync(tenantId, systemId, taskId, ct);
        await SystemWorkspaceAccessPolicy.RequireAsync(db, systemId, p => p.CanManageRemediation, ct);
        await FindingAsync(tenantId, systemId, findingId, ct);
        CheckVersion(task, rowVersion);
        if (task.FindingId == findingId) return;
        if (!string.IsNullOrEmpty(task.FindingId))
            throw new InvalidOperationException("FINDING_ALREADY_LINKED: The task already has a different originating finding.");
        task.FindingId = findingId;
        task.UpdatedAt = DateTime.UtcNow;
        History(task, actor, "Originating finding linked", null, findingId, HistoryEventType.RelationshipLinked);
        Activity(tenantId, systemId, actor, "FindingTaskLinked", "ComplianceFinding", findingId,
            $"Existing remediation task {task.Id} linked without changing lifecycle state.");
        await db.SaveChangesAsync(ct);
    }

    public async Task<PoamItem> PoamAsync(Guid tenantId, string systemId, string poamId, CancellationToken ct) =>
        await db.PoamItems.SingleOrDefaultAsync(p => p.Id == poamId && p.TenantId == tenantId &&
            p.RegisteredSystemId == systemId, ct) ?? throw new KeyNotFoundException();

    public async Task<ComplianceFinding> FindingAsync(Guid tenantId, string systemId, string findingId, CancellationToken ct)
    {
        var finding = await db.Findings.SingleOrDefaultAsync(f => f.Id == findingId && f.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException();
        if (await RemediationScope.FindingSystemAsync(db, finding, ct) != systemId) throw new KeyNotFoundException();
        return finding;
    }

    private async Task SystemAsync(Guid tenantId, string systemId, CancellationToken ct)
    {
        if (!await db.RegisteredSystems.AnyAsync(s => s.Id == systemId && s.TenantId == tenantId, ct)) throw new KeyNotFoundException();
    }

    private static void ValidateInput(string title, string description, string controlId, string severity)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 500 || string.IsNullOrWhiteSpace(description) || description.Length > 4000)
            throw new ArgumentException("Title and description are required (maximum 500 and 4000 characters).");
        if (!Regex.IsMatch(controlId ?? "", @"^[A-Za-z]{2}-\d+(?:\.\d+|\(\d+\))?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("A valid NIST control ID is required.");
        if (!Enum.TryParse<FindingSeverity>(severity, true, out var parsed) || !Enum.IsDefined(parsed))
            throw new ArgumentException("Invalid finding severity.");
    }

    private static string OperationKey(Guid tenant, string system, string kind, string request)
    {
        if (string.IsNullOrWhiteSpace(request) || request.Length > 128) throw new ArgumentException("A request ID up to 128 characters is required.");
        return AssessmentResultProvenance.Hash(JsonSerializer.Serialize(new { tenant, system, kind, request }));
    }

    private static void CheckVersion(RemediationTask task, Guid expected)
    {
        if (task.RowVersion != expected) throw new DbUpdateConcurrencyException("Task changed. Reload before retrying.");
    }

    private static RemediationEvidence[] Evidence(RemediationTask task) =>
        JsonSerializer.Deserialize<RemediationEvidence[]>(task.EvidenceReferencesJson) ?? [];

    private static void ResetVerification(RemediationTask task)
    {
        task.VerificationStatus = "NotVerified";
        task.VerificationNotes = null;
        task.VerifiedAt = null;
        task.VerifiedBy = null;
    }

    private static void History(RemediationTask task, string actor, string details, string? oldValue, string? newValue,
        HistoryEventType type = HistoryEventType.DetailsUpdated) =>
        task.History.Add(new TaskHistoryEntry { TenantId = task.TenantId, TaskId = task.Id, ActingUserId = actor, ActingUserName = actor,
            EventType = type, Details = details, OldValue = oldValue, NewValue = newValue });

    private void Activity(Guid tenant, string system, string actor, string eventType, string entityType, string entityId, string summary) =>
        db.DashboardActivities.Add(new DashboardActivity { TenantId = tenant, RegisteredSystemId = system, Actor = actor,
            EventType = eventType, RelatedEntityType = entityType, RelatedEntityId = entityId, Summary = summary });
}

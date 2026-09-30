using System.Text.Json;
using System.Security.Claims;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Core.Dtos.Dashboard;

namespace Ato.Copilot.Mcp.Services;

public sealed record WorkspaceResultItem(string Id, string RecordId, string Name, string Source,
    string Method, string CollectionStatus, string ReviewStatus, DateTime RecordedAt, string? Actor,
    string? PlanId, long? PlanRevision, string? PlanTitle, string? PlanStatusAtCollection,
    bool RequiresReconciliation, int ObservedControlCount, int ReviewedControlCount, int? ScopeControlCount,
    string Revision, string[] Warnings, bool CanReview);
public sealed record ResultEvidence(string Id, string Name, string? ContentHash, string? DownloadUrl);
public sealed record ResultHistory(string Action, string? Actor, DateTime At, string Description);
public sealed record ResultPermissions(bool CanReview, string? ReviewReason, bool CanReconcile,
    string? ReconcileReason, bool CanRemediate, bool CanRequestDeviation);
public sealed record WorkspaceResultDetail(string SystemId, WorkspaceResultItem Item,
    string[] OriginalScope, string[] SelectedScope, string[] ObservedControls, string[] MissingControls,
    string[] OutOfScopeControls, string[] ExcludedControls, string[] DuplicateControls,
    ResultEvidence[] Evidence, ResultFindingSnapshot[] Findings, string[] Errors,
    ResultHistory[] History, ResultPermissions Permissions);
public sealed record CollectResultsRequest(string? PlanId, string? ExpectedPlanHash, string RequestId);
public sealed record ReconcileResultRequest(string PlanId, string ExpectedPlanHash, string ExpectedResultRevision);
public sealed record ReviewResultRequest(string ExpectedResultRevision, string ControlId, string Determination,
    string Method, string Notes, string[] EvidenceIds, string? CatSeverity);
public sealed record CreateWorkspaceReportRequest(string? PlanId, string? ExpectedPlanHash,
    string[] ResultIds, Dictionary<string, string> ExpectedResultRevisions, string RequestId, string Title);
public sealed record AssessmentEnvironmentAccessResponse(string SystemId, bool CanConfigure, string? Reason);

public sealed class AssessmentWorkspaceException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Projection and transitions over existing assessment, import and SAR stores.</summary>
public sealed class AssessmentResultsWorkspaceService(AtoCopilotContext db, ITenantContext tenant,
    ISystemWorkspaceAccessService accessService, IAssessmentEnvironmentService environment,
    IAssessmentArtifactService artifacts, IAtoComplianceEngine engine,
    ISecurityAssessmentReportService reports, ILogger<AssessmentResultsWorkspaceService> logger,
    Microsoft.AspNetCore.Authorization.IAuthorizationService authorization,
    ISystemEnvironmentScopeResolver? environments = null)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;
    public async Task<SystemWorkspaceAccessResponse> AccessAsync(string systemId, CancellationToken ct)
    {
        var access = await accessService.GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId,
            systemId, tenant.IsCspAdmin, ct);
        if (!access.Permissions.CanRead) throw new AssessmentWorkspaceException(404, "System not accessible.");
        return access;
    }
    public async Task<AssessmentEnvironmentAccessResponse> GetConfigurationAccessAsync(
        string systemId, ClaimsPrincipal user, CancellationToken ct)
    {
        await AccessAsync(systemId, ct);
        return await ConfigurationDecisionAsync(systemId, user);
    }
    private async Task<AssessmentEnvironmentAccessResponse> ConfigurationDecisionAsync(string systemId, ClaimsPrincipal user)
    {
        if (tenant.Status != Ato.Copilot.Core.Models.Tenancy.TenantStatus.Active
            || tenant.ImpersonatedTenantId is not null || tenant.IsWorkspaceRequest && tenant.IsCspAdmin)
            return new(systemId, false, "Azure configuration is unavailable in read-only or provider/support contexts.");
        var allowed = (await authorization.AuthorizeAsync(user, null, Policies.ComplianceWriter)).Succeeded;
        return new(systemId, allowed, allowed ? null : "The ComplianceWriter policy is required to configure Azure assessment attachments.");
    }
    private string Actor => tenant.PersonId?.ToString() ?? "authenticated-user";
    private static bool Reviewer(SystemWorkspaceAccessResponse access) =>
        access.Roles.Contains("Sca", IdComparer);
    private static bool IsCollecting(Source source) =>
        source.Import?.ImportStatus is ScanImportStatus.Queued or ScanImportStatus.Processing
        || source.Import is null && source.Assessment.Status is AssessmentStatus.Pending or AssessmentStatus.InProgress;
    public async Task<AssessmentPlanPin?> PlanAsync(string systemId, string? planId,
        string? expectedHash, bool validateHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            if (expectedHash is not null) throw new AssessmentWorkspaceException(400, "A plan hash requires a plan.");
            return null;
        }
        var plan = await db.SecurityAssessmentPlans.Include(x => x.ControlEntries).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == planId && x.RegisteredSystemId == systemId, ct)
            ?? throw new AssessmentWorkspaceException(404, "Plan not found for this system.");
        var pin = AssessmentResultProvenance.Pin(plan);
        if (validateHash && !string.Equals(pin.Hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new AssessmentWorkspaceException(409, "The selected plan changed. Refresh before continuing.");
        return pin;
    }

    private sealed record Source(ComplianceAssessment Assessment, ScanImportRecord? Import,
        AssessmentResultProvenance Provenance, ResultFindingSnapshot[] Findings, string[] Observed)
    {
        public string Id => Import is null ? "assessment:" + Assessment.Id : "import:" + Import.Id;
        public string? Json => Import is null ? Assessment.ResultProvenanceJson : Import.ResultProvenanceJson;
        public void Save()
        {
            if (Import is null) Assessment.ResultProvenanceJson = Provenance.Serialize();
            else Import.ResultProvenanceJson = Provenance.Serialize();
        }
    }
    private async Task<Source> SourceAsync(string systemId, string qualifiedId, CancellationToken ct)
    {
        var parts = qualifiedId.Split(':', 2);
        if (parts.Length != 2 || !new[] { "assessment", "import" }.Contains(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            throw new AssessmentWorkspaceException(400, "Use a qualified assessment:<id> or import:<id> result.");
        ScanImportRecord? import = null;
        var assessmentId = parts[1];
        if (parts[0] == "import")
        {
            import = await db.ScanImportRecords.SingleOrDefaultAsync(x => x.Id == parts[1]
                && x.RegisteredSystemId == systemId && !x.IsDryRun, ct)
                ?? throw new AssessmentWorkspaceException(404, "Import not found.");
            assessmentId = import.AssessmentId;
        }
        var assessment = await db.Assessments.Include(x => x.Findings)
            .SingleOrDefaultAsync(x => x.Id == assessmentId && x.RegisteredSystemId == systemId, ct)
            ?? throw new AssessmentWorkspaceException(404, "Assessment not found.");
        if (import is null && await db.ScanImportRecords.AnyAsync(x => x.AssessmentId == assessmentId && !x.IsDryRun, ct))
            throw new AssessmentWorkspaceException(400, "Select the individual imported result, not its shared assessment context.");
        var provenance = AssessmentResultProvenance.Read(import is null ? assessment.ResultProvenanceJson : import.ResultProvenanceJson);
        var findings = assessment.Findings.Select(ResultFindingSnapshot.From).ToArray();
        string[] observations;
        if (import is not null)
        {
            var rows = await db.ScanImportFindings.Where(x => x.ScanImportRecordId == import.Id).ToListAsync(ct);
            observations = rows.Where(x => !new[] { "Not_Reviewed", "notchecked", "unknown", "error" }
                .Contains(x.RawStatus, IdComparer)).SelectMany(x => x.ResolvedNistControlIds).ToArray();
            var ids = rows.Select(x => x.ComplianceFindingId).Where(x => x != null).ToHashSet();
            findings = findings.Where(x => ids.Contains(x.FindingId)).ToArray();
        }
        else
        {
            var evidenceControls = await db.Evidence.Where(x => x.AssessmentId == assessmentId && x.ControlId != "")
                .Select(x => x.ControlId).ToListAsync(ct);
            observations = findings.Where(x => x.ControlId != null).Select(x => x.ControlId!).Concat(evidenceControls).ToArray();
        }
        return new(assessment, import, provenance, provenance.Findings?.ToArray() ?? findings,
            provenance.ObservedControlIds ?? observations.Distinct(IdComparer).Order().ToArray());
    }
    private static string Revision(Source source) => AssessmentResultProvenance.Hash(JsonSerializer.Serialize(new
    {
        source.Id, provenance = source.Json, status = source.Import?.ImportStatus.ToString() ?? source.Assessment.Status.ToString(),
        source.Observed, source.Findings
    }));
    private static string[] Scoped(IEnumerable<string> ids, AssessmentPlanPin? plan) =>
        (plan is null ? ids : ids.Intersect(plan.IncludedControlIds, IdComparer)).Distinct(IdComparer).Order().ToArray();
    private static bool Reconciled(Source source, AssessmentPlanPin? plan) => plan is null
        || source.Provenance.Plan is { } original && original.Id == plan.Id && original.Revision == plan.Revision && original.Hash == plan.Hash
        || source.Provenance.Reconciliations.Any(r => r.Plan.Id == plan.Id && r.Plan.Revision == plan.Revision && r.Plan.Hash == plan.Hash);
    private static string[] Warnings(Source source, AssessmentPlanPin? plan)
    {
        var warnings = new List<string>();
        if (source.Provenance.Plan is null) warnings.Add("Preliminary result: original plan scope is unknown.");
        if (!Reconciled(source, plan)) warnings.Add("This result requires explicit reconciliation with the selected plan.");
        if (source.Provenance.Reviews.Count == 0) warnings.Add("No explicit human control reviews are retained for this result.");
        if (IsCollecting(source)) warnings.Add("Collection is still in progress; this snapshot is preliminary.");
        if (source.Import?.Warnings is { } importWarnings) warnings.AddRange(importWarnings);
        if (source.Import is null && source.Assessment.ScanPillarResults.Any(x => !x.Value))
            warnings.Add("One or more scan pillars are unavailable or unverified; no passing checks are inferred.");
        return warnings.Distinct().ToArray();
    }
    private static WorkspaceResultItem Item(Source source, AssessmentPlanPin? plan, bool canReview)
    {
        var pin = source.Provenance.Plan;
        var observed = Scoped(source.Observed, plan);
        var reviewed = Scoped(source.Provenance.Reviews.Select(x => x.ControlId), plan);
        return new(source.Id, source.Import?.Id ?? source.Assessment.Id,
            source.Import?.FileName ?? $"Assessment {source.Assessment.AssessedAt:yyyy-MM-dd HH:mm}",
            source.Import?.ImportType.ToString() ?? (source.Assessment.ScanType == "comprehensive" ? "Azure" : "Assessment"),
            source.Import is null ? "Collected observations" : "Scan import",
            source.Import?.ImportStatus.ToString() ?? (source.Assessment.Status == AssessmentStatus.Failed && source.Observed.Length > 0 ? "Partial" : source.Assessment.Status.ToString()),
            reviewed.Length == 0 ? "Pending" : observed.Except(reviewed, IdComparer).Any() ? "Partial" : "Reviewed",
            source.Import?.ImportedAt ?? source.Assessment.AssessedAt, source.Import?.ImportedBy ?? source.Assessment.InitiatedBy,
            pin?.Id, pin?.Revision, pin?.Title, pin?.Status, !Reconciled(source, plan), observed.Length,
            reviewed.Length, plan?.IncludedControlIds.Length ?? pin?.IncludedControlIds.Length,
            Revision(source), Warnings(source, plan), canReview && !IsCollecting(source));
    }
    public async Task<WorkspaceResultDetail> DetailAsync(string systemId, string id, string? planId, CancellationToken ct)
    {
        var access = await AccessAsync(systemId, ct);
        var plan = await PlanAsync(systemId, planId, null, false, ct);
        var source = await SourceAsync(systemId, id, ct);
        var canReview = Reviewer(access) && !IsCollecting(source);
        var evidence = await db.Evidence.Where(e => e.AssessmentId == source.Assessment.Id).ToListAsync(ct);
        var observations = source.Observed;
        var errors = source.Provenance.Errors.Concat(source.Assessment.ControlFamilyResults
            .Where(x => x.Status == FamilyAssessmentStatus.Failed).Select(x => $"{x.FamilyCode}: collection failed."))
            .Concat(source.Import?.ErrorMessage is not null ? new[] { "Import reported errors; retained partial observations are shown." } : [])
            .Concat(source.Import?.ErrorCount > 0 ? new[] { $"{source.Import.ErrorCount} scan entries have errors or unknown outcomes." } : [])
            .Concat(source.Import is null && source.Assessment.Status == AssessmentStatus.Failed
                ? new[] { "Collection did not complete successfully; absence of findings is not a passing check." } : []).Distinct().ToArray();
        var duplicateControls = Array.Empty<string>();
        if (source.Import is not null)
        {
            var rows = await db.ScanImportFindings.Where(x => x.ScanImportRecordId == source.Import.Id).ToListAsync(ct);
            duplicateControls = rows.SelectMany(x => x.ResolvedNistControlIds).GroupBy(x => x, IdComparer)
                .Where(x => x.Count() > 1).Select(x => x.Key).Order().ToArray();
        }
        var scope = plan?.IncludedControlIds ?? source.Provenance.Plan?.IncludedControlIds ?? [];
        return new(systemId, Item(source, plan, canReview), source.Provenance.Plan?.IncludedControlIds ?? [],
            scope, observations, scope.Except(observations, IdComparer).ToArray(),
            scope.Length == 0 ? [] : observations.Except(scope, IdComparer).ToArray(),
            plan?.ExcludedControlIds ?? source.Provenance.Plan?.ExcludedControlIds ?? [], duplicateControls,
            evidence.Where(e => source.Import is null ? observations.Contains(e.ControlId, IdComparer)
                    : source.Provenance.EvidenceIds.Contains(e.Id))
                .Select(e => new ResultEvidence(e.Id, e.Description, AssessmentResultProvenance.Hash(e.Content), null)).ToArray(),
            source.Findings, errors,
            source.Provenance.Reviews.Select(r => new ResultHistory("Review", r.Actor, r.At, $"{r.ControlId}: {r.Determination}"))
                .Concat(source.Provenance.Reconciliations.Select(r => new ResultHistory("Reconcile", r.Actor, r.At,
                    $"Compared with {r.Plan.Title}, revision {r.Plan.Revision}; original collection scope retained."))).ToArray(),
            new(canReview, canReview ? null : IsCollecting(source) ? "Collection is still running." : "Only the system's assigned SCA may review controls.",
                canReview, canReview ? null : IsCollecting(source) ? "Collection is still running." : "Only the system's assigned SCA may reconcile results.",
                access.Permissions.CanCreateRemediationTasks, false));
    }

    public async Task<object> ListAsync(string systemId, string? planId, string? search, int page, int pageSize,
        string? selectedResultIds, ClaimsPrincipal user, CancellationToken ct)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100) throw new AssessmentWorkspaceException(400, "Invalid page or pageSize.");
        var access = await AccessAsync(systemId, ct);
        var plan = await PlanAsync(systemId, planId, null, false, ct);
        var imports = await db.ScanImportRecords.Where(x => x.RegisteredSystemId == systemId && !x.IsDryRun).ToListAsync(ct);
        var importedAssessments = imports.Select(x => x.AssessmentId).Distinct().ToArray();
        var assessments = await db.Assessments.Where(x => x.RegisteredSystemId == systemId && !importedAssessments.Contains(x.Id)).ToListAsync(ct);
        var sources = new List<Source>();
        foreach (var id in imports.Select(x => "import:" + x.Id).Concat(assessments.Select(x => "assessment:" + x.Id)))
            sources.Add(await SourceAsync(systemId, id, ct));
        var selected = selectedResultIds is null ? sources.Select(x => x.Id).ToArray()
            : selectedResultIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
        if (selected.Any(id => sources.All(s => s.Id != id))) throw new AssessmentWorkspaceException(400, "Selected result is not in this system.");
        var selectedSources = sources.Where(x => selected.Contains(x.Id)).ToArray();
        var observed = Scoped(selectedSources.SelectMany(x => x.Observed), plan);
        var reviewSummary = SelectedAssessmentReviewSummary.Build(selectedSources
            .Select(s => (s.Id, s.Observed.AsEnumerable(), s.Provenance.Reviews.AsEnumerable())), plan?.IncludedControlIds);
        var reviewed = reviewSummary.Reviews.Select(r => r.ControlId).ToArray();
        var missing = plan?.IncludedControlIds.Except(observed, IdComparer).ToArray() ?? [];
        var blockers = new List<string>();
        if (!access.Permissions.CanGenerateSar) blockers.Add("Report preparation requires the system's SCA or ISSM.");
        if (selected.Length == 0) blockers.Add("Select at least one result.");
        var warnings = selectedSources.SelectMany(x => Warnings(x, plan)).Concat(reviewSummary.Warnings).Distinct().ToList();
        if (plan is null) warnings.Add("No plan selected; scope coverage is unknown.");
        if (missing.Length > 0) warnings.Add($"{missing.Length} selected controls have no observations.");
        if (reviewed.Length < observed.Length) warnings.Add("Unreviewed observations will remain pending in the SAR draft.");
        var configurationAccess = await ConfigurationDecisionAsync(systemId, user);
        var canConfigure = configurationAccess.CanConfigure;
        AssessmentReadinessResponse? readiness = null;
        var readinessState = "NotChecked";
        var readinessMessage = "Azure readiness was not probed. Collection or Azure configuration permission is required.";
        if (access.Permissions.CanRunAssessments || canConfigure)
        {
            try
            {
                readiness = await environment.GetReadinessAsync(systemId, ct);
                readinessState = readiness.IsReady ? "Ready" : "Blocked";
                readinessMessage = readiness.Message;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Azure readiness probe unavailable for system {SystemId}; retained history remains readable.", systemId);
                readinessState = "Unavailable";
                readinessMessage = "Azure readiness could not be verified. Historical results remain available; retry connectivity checks before collection.";
            }
        }
        var scopeReason = await UnsupportedScopeAsync(systemId, ct);
        var canRun = access.Permissions.CanRunAssessments && readiness?.IsReady == true && scopeReason is null;
        var items = sources.Select(s => Item(s, plan, Reviewer(access))).Where(x =>
            string.IsNullOrWhiteSpace(search) || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || x.Source.Contains(search, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.RecordedAt).ToArray();
        var retainedReports = await db.SecurityAssessmentReports.AsNoTracking()
            .Where(x => x.RegisteredSystemId == systemId).OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.Title, x.Status, x.CreatedAt }).ToListAsync(ct);
        return new
        {
            systemId, items = items.Skip((page - 1) * pageSize).Take(pageSize), totalCount = items.Length, page, pageSize,
            selectedResults = selectedSources.Select(s => Item(s, plan, Reviewer(access)))
                .Select(x => new { x.Id, x.Name, x.Revision }),
            reports = retainedReports.Select(x => new { x.Id, x.Title, status = x.Status.ToString(), x.CreatedAt }),
            collection = new
            {
                canRunAzure = canRun, runReason = canRun ? null : !access.Permissions.CanRunAssessments
                    ? "You cannot collect assessment results for this system." : scopeReason ?? readinessMessage,
                canImport = access.Permissions.CanRunAssessments, importReason = access.Permissions.CanRunAssessments ? null : "Collection permission required.",
                canConfigureAzure = canConfigure, configurationReason = configurationAccess.Reason,
                azure = new { state = scopeReason is null ? readinessState : "Blocked", message = scopeReason ?? readinessMessage,
                    checkedAt = readiness?.CheckedAt, subscriptions = (readiness?.Subscriptions ?? Array.Empty<AssessmentSubscriptionResponse>())
                        .Select(s => new { id = s.SubscriptionId, name = s.DisplayName }),
                    scopeDescription = new[] { "All configured subscriptions; resource-restricted boundaries require a scope-capable evaluator.",
                        "Azure evaluators collect their supported controls; results outside the selected plan are identified separately." } },
                importFormats = new[] { ".ckl", ".xml", ".nessus" }
            },
            sarReadiness = new { canPrepareDraft = blockers.Count == 0, blockers, warnings,
                scopeCount = plan?.IncludedControlIds.Length, observedControlCount = observed.Length,
                reviewedControlCount = reviewed.Length, missingControlIds = missing, selectedResultIds = selected },
            permissions = new { canReview = Reviewer(access), reviewReason = Reviewer(access) ? null : "Assigned SCA permission required.",
                canRemediate = access.Permissions.CanCreateRemediationTasks, canRequestDeviation = false }
        };
    }

    private async Task<string?> UnsupportedScopeAsync(string systemId, CancellationToken ct) =>
        await db.AuthorizationBoundaries.AnyAsync(x => x.RegisteredSystemId == systemId, ct)
            || await db.BoundaryComponentAssignments.AnyAsync(x => x.AuthorizationBoundaryDefinition.RegisteredSystemId == systemId, ct)
            ? "The configured resource boundary cannot be enforced by every Azure evaluator. Import scoped results instead; a broader scan will not be run."
            : null;

    public async Task<object> CollectAsync(string systemId, CollectResultsRequest request, CancellationToken ct)
    {
        var access = await AccessAsync(systemId, ct);
        if (!access.Permissions.CanRunAssessments) throw new AssessmentWorkspaceException(403, "Collection permission required.");
        ValidateRequestId(request.RequestId);
        var key = AssessmentResultProvenance.Hash($"{tenant.EffectiveTenantId}:{systemId}:collect:{request.RequestId}")[..27];
        var gate = Gates.GetOrAdd(key, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var intent = AssessmentResultProvenance.Hash(JsonSerializer.Serialize(new { request.PlanId, request.ExpectedPlanHash }));
            var existing = await db.Assessments.Where(x => x.RegisteredSystemId == systemId
                && x.WorkspaceOperationKey != null && x.WorkspaceOperationKey.StartsWith(key)).Include(x => x.Findings).ToListAsync(ct);
            if (existing.Any(x => AssessmentResultProvenance.Read(x.ResultProvenanceJson).RequestIntentHash != intent))
                throw new AssessmentWorkspaceException(409, "Request key already used for another collection.");
            if (existing.Count > 0 && existing.All(x => x.Status == AssessmentStatus.Completed))
                return new { status = "Completed", message = "Retained collection results.", resultIds = existing.Select(x => "assessment:" + x.Id).ToArray() };
            await EnsureCurrentEnvironmentAsync(systemId, ct);
            var pin = existing.Count > 0 ? AssessmentResultProvenance.Read(existing[0].ResultProvenanceJson).Plan
                : await PlanAsync(systemId, request.PlanId, request.ExpectedPlanHash, true, ct);
            var readiness = await environment.GetReadinessAsync(systemId, ct);
            if (!readiness.IsReady) throw new AssessmentWorkspaceException(409, readiness.Message);
            if (existing.Any(x => !readiness.Subscriptions.Any(s => s.SubscriptionId == x.SubscriptionId)))
                throw new AssessmentWorkspaceException(409, "Configured subscriptions changed. Existing partial results are retained; start a new scoped collection.");
            var unsupported = await UnsupportedScopeAsync(systemId, ct);
            if (unsupported is not null) throw new AssessmentWorkspaceException(409, unsupported);
            // One existing assessment per subscription; the unique key is the durable admission fence.
            if (existing.Count == 0)
            {
                foreach (var subscription in readiness.Subscriptions)
                {
                    existing.Add(new ComplianceAssessment { RegisteredSystemId = systemId, SubscriptionId = subscription.SubscriptionId,
                        SubscriptionIds = [subscription.SubscriptionId], InitiatedBy = Actor, ScanType = "comprehensive",
                        WorkspaceOperationKey = key + ":" + subscription.SubscriptionId,
                        ResultProvenanceJson = new AssessmentResultProvenance { Plan = pin, RequestIntentHash = intent }.Serialize() });
                }
                db.Assessments.AddRange(existing);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { throw new AssessmentWorkspaceException(409, "Collection admission raced with another request. Retry the same key."); }
            }
            var failed = false;
            foreach (var assessment in existing.Where(x => x.Status != AssessmentStatus.Completed))
            {
                await EnsureCurrentEnvironmentAsync(systemId, ct);
                if (db.Entry(assessment).State == EntityState.Detached) db.Attach(assessment);
                var execution = AssessmentResultProvenance.Read(assessment.ResultProvenanceJson);
                if (assessment.Status == AssessmentStatus.InProgress && execution.ExecutionLeaseExpiresAt > DateTime.UtcNow)
                    throw new AssessmentWorkspaceException(409, "This collection is in progress; refresh its retained results.");
                assessment.Status = AssessmentStatus.InProgress;
                execution.ExecutionToken = Guid.NewGuid().ToString();
                execution.ExecutionLeaseExpiresAt = DateTime.UtcNow.AddMinutes(30);
                assessment.ResultProvenanceJson = execution.Serialize();
                await db.SaveChangesAsync(ct);
                // The executor owns persistence while it runs. Its family checkpoints update the
                // concurrency token, so the HTTP tracker must not write an older graph afterward.
                db.ChangeTracker.Clear();
                try
                {
                    await engine.RunRetainedAssessmentAsync(assessment, cancellationToken: ct);
                    failed |= assessment.Status != AssessmentStatus.Completed;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Retained assessment {AssessmentId} collection failed for system {SystemId}", assessment.Id, systemId);
                    // The engine persists partial families before returning; do not overwrite them
                    // using the HTTP context's stale tracker after a storage/scanner failure.
                    await db.Entry(assessment).ReloadAsync(CancellationToken.None);
                    failed = true;
                    assessment.Status = AssessmentStatus.Failed;
                    var provenance = AssessmentResultProvenance.Read(assessment.ResultProvenanceJson);
                    provenance.Errors.Add("Collection failed; completed scanner observations retained. Retry the same request.");
                    assessment.ResultProvenanceJson = provenance.Serialize();
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                }
            }
            return new { status = failed ? "Partial" : "Completed",
                message = failed ? "Partial results retained. Review collection errors." : "Collection retained. Human review is pending.",
                resultIds = existing.Select(x => "assessment:" + x.Id).ToArray() };
        }
        finally { gate.Release(); }
    }

    private async Task EnsureCurrentEnvironmentAsync(string systemId, CancellationToken ct)
    {
        if (environments is null) return;
        var scopes = await environments.ResolveAsync(systemId, EnvironmentScopePurpose.Assessment, ct);
        if (CanonicalEnvironmentExecutionGate.IsCanonical(scopes))
            throw new AssessmentWorkspaceException(409,
                $"{CanonicalEnvironmentExecutionGate.Failure(scopes).Message} {CanonicalEnvironmentExecutionGate.Suggestion}");
    }

    public async Task<WorkspaceResultDetail> ReconcileAsync(string systemId, string id,
        ReconcileResultRequest request, CancellationToken ct)
    {
        var access = await AccessAsync(systemId, ct);
        if (!Reviewer(access)) throw new AssessmentWorkspaceException(403, "Assigned SCA permission required.");
        var plan = await PlanAsync(systemId, request.PlanId, request.ExpectedPlanHash, true, ct)
            ?? throw new AssessmentWorkspaceException(400, "Select a plan.");
        var source = await SourceAsync(systemId, id, ct);
        if (Revision(source) != request.ExpectedResultRevision) throw new AssessmentWorkspaceException(409, "Result changed. Refresh first.");
        if (IsCollecting(source))
            throw new AssessmentWorkspaceException(409, "Collection is still running. Review a retained terminal result.");
        source.Provenance.Reconciliations.Add(new(plan, Actor, DateTime.UtcNow));
        source.Save();
        await db.SaveChangesAsync(ct);
        return await DetailAsync(systemId, id, request.PlanId, ct);
    }

    public async Task<WorkspaceResultDetail> ReviewAsync(string systemId, string id,
        ReviewResultRequest request, CancellationToken ct)
    {
        var access = await AccessAsync(systemId, ct);
        if (!Reviewer(access)) throw new AssessmentWorkspaceException(403, "Assigned SCA permission required.");
        var source = await SourceAsync(systemId, id, ct);
        if (IsCollecting(source))
            throw new AssessmentWorkspaceException(409, "Collection is still running. Review a retained terminal result.");
        if (Revision(source) != request.ExpectedResultRevision) throw new AssessmentWorkspaceException(409, "Result changed. Refresh first.");
        var reviewPlan = source.Provenance.Reconciliations.LastOrDefault()?.Plan ?? source.Provenance.Plan;
        if (!source.Observed.Contains(request.ControlId, IdComparer)
            || reviewPlan is { } pin && !pin.IncludedControlIds.Contains(request.ControlId, IdComparer))
            throw new AssessmentWorkspaceException(400, "Control is not an observed control in this result's original scope.");
        if (!new[] { "Satisfied", "OtherThanSatisfied" }.Contains(request.Determination)
            || !new[] { "Examine", "Interview", "Test" }.Contains(request.Method)
            || string.IsNullOrWhiteSpace(request.Notes)
            || request.Notes.Length > 32000 || request.EvidenceIds is null || request.EvidenceIds.Length > 100
            || request.Determination == "OtherThanSatisfied" && !new[] { "CatI", "CatII", "CatIII" }.Contains(request.CatSeverity))
            throw new AssessmentWorkspaceException(400, "Supply a determination, valid assessment method, notes, and CAT severity for OtherThanSatisfied.");
        var evidenceIds = request.EvidenceIds.Distinct().ToArray();
        var validEvidence = await db.Evidence.Where(x => x.AssessmentId == source.Assessment.Id
            && evidenceIds.Contains(x.Id)).ToListAsync(ct);
        validEvidence = validEvidence.Where(x => source.Import is null ? x.ControlId == request.ControlId
            : source.Provenance.EvidenceIds.Contains(x.Id)).ToList();
        if (validEvidence.Count != evidenceIds.Length) throw new AssessmentWorkspaceException(400, "Evidence must belong to this result assessment and reviewed control.");
        // Freeze shared imported findings before the existing effectiveness upsert can change current records.
        if (source.Import is not null)
        {
            source.Provenance.Findings ??= source.Findings.ToList();
            source.Provenance.ObservedControlIds ??= source.Observed;
        }
        source.Provenance.SourceResultId = source.Id;
        source.Provenance.SystemId = systemId;
        var at = DateTime.UtcNow;
        var snapshotId = Guid.NewGuid();
        source.Provenance.Reviews.Add(new(request.ControlId, request.Determination, request.Method,
            request.Notes, evidenceIds, request.CatSeverity, Actor, at, request.ExpectedResultRevision, snapshotId.ToString(),
            validEvidence.Select(e => new ResultEvidenceSnapshot(e.Id, e.Description, AssessmentResultProvenance.Hash(e.Content))).ToArray(), reviewPlan));
        source.Save();
        var snapshotJson = JsonSerializer.Serialize(new { sourceResultId = source.Id, systemId,
            source.Observed, source.Findings, provenance = source.Provenance });
        db.ComplianceSnapshots.Add(new ComplianceSnapshot { Id = snapshotId, SubscriptionId = source.Assessment.SubscriptionId,
            CapturedAt = at, IsImmutable = true, TotalControls = 1,
            PassedControls = request.Determination == "Satisfied" ? 1 : 0,
            FailedControls = request.Determination == "OtherThanSatisfied" ? 1 : 0,
            ControlFamilyBreakdown = snapshotJson, IntegrityHash = AssessmentResultProvenance.Hash(snapshotJson) });
        await artifacts.AssessControlInContextAsync(db, source.Assessment.Id, request.ControlId, request.Determination,
            request.Method, evidenceIds.ToList(), request.Notes, request.CatSeverity, Actor, ct);
        return await DetailAsync(systemId, id, null, ct);
    }

    public async Task<object> ReportAsync(string systemId, CreateWorkspaceReportRequest request, CancellationToken ct)
    {
        var access = await AccessAsync(systemId, ct);
        if (!access.Permissions.CanGenerateSar) throw new AssessmentWorkspaceException(403, "Report preparation permission required.");
        ValidateRequestId(request.RequestId);
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 500
            || request.ResultIds is null || request.ResultIds.Length is 0 or > 100 || request.ExpectedResultRevisions is null)
            throw new AssessmentWorkspaceException(400, "A title and at least one selected result are required.");
        var key = AssessmentResultProvenance.Hash($"{tenant.EffectiveTenantId}:{systemId}:sar:{request.RequestId}");
        var intent = AssessmentResultProvenance.Hash(JsonSerializer.Serialize(new { request.PlanId,
            request.ExpectedPlanHash, ids = request.ResultIds.Order().ToArray(), revisions = request.ExpectedResultRevisions.OrderBy(x => x.Key), request.Title }));
        var existing = await db.SecurityAssessmentReports.Include(x => x.Sections)
            .SingleOrDefaultAsync(x => x.RegisteredSystemId == systemId && x.WorkspaceOperationKey == key, ct);
        if (existing is not null)
        {
            if (JsonSerializer.Deserialize<ScopedSarInput>(existing.SourceSnapshotJson!)?.IntentHash != intent)
                throw new AssessmentWorkspaceException(409, "Request key already used for another report.");
            return ReportResponse(existing);
        }
        var plan = await PlanAsync(systemId, request.PlanId, request.ExpectedPlanHash, true, ct);
        var sources = new List<RetainedSarSource>();
        foreach (var id in request.ResultIds.Distinct())
        {
            var source = await SourceAsync(systemId, id, ct);
            if (!request.ExpectedResultRevisions.TryGetValue(id, out var revision) || revision != Revision(source))
                throw new AssessmentWorkspaceException(409, "Selected result changed. Refresh before preparing a draft.");
            var detail = await DetailAsync(systemId, id, request.PlanId, ct);
            sources.Add(new(id, revision, source.Provenance.Plan, source.Observed, source.Provenance.Reviews.ToArray(),
                source.Findings, detail.Evidence.Select(x => x.Id).ToArray(), detail.Item.Warnings,
                detail.Evidence.Select(x => new ResultEvidenceSnapshot(x.Id, x.Name, x.ContentHash ?? "")).ToArray()));
        }
        var warnings = sources.SelectMany(x => x.Warnings).Distinct().ToList();
        if (plan is null) warnings.Add("No plan selected; coverage is unknown.");
        else if (plan.IncludedControlIds.Except(sources.SelectMany(s => s.ObservedControls), IdComparer).Any())
            warnings.Add("Selected scope includes controls without observations.");
        var sar = await reports.CreateScopedSarAsync(systemId, new(key, intent, request.Title,
            plan, sources.ToArray(), warnings.ToArray()), Actor, ct);
        return ReportResponse(sar);
    }
    public async Task<object> GetReportAsync(string systemId, string sarId, CancellationToken ct)
    {
        await AccessAsync(systemId, ct);
        var sar = await db.SecurityAssessmentReports.Include(x => x.Sections).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == sarId && x.RegisteredSystemId == systemId, ct)
            ?? throw new AssessmentWorkspaceException(404, "Report not found.");
        return ReportResponse(sar);
    }
    private static object ReportResponse(SecurityAssessmentReport report)
    {
        var snapshot = report.SourceSnapshotJson is null ? null : JsonSerializer.Deserialize<ScopedSarInput>(report.SourceSnapshotJson);
        return new { report.Id, report.Title, status = report.Status.ToString(), report.CreatedAt,
            downloadUrl = $"/api/v1/systems/{report.RegisteredSystemId}/sar/{report.Id}/export",
            sections = report.Sections.OrderBy(x => x.SectionType).Select(x => new { x.Title, content = x.Content ?? "" }),
            sourceResultIds = snapshot?.Sources.Select(x => x.Id).ToArray() ?? [],
            warnings = snapshot?.Warnings ?? ["Legacy report: selected-source provenance unknown."] };
    }
    private static void ValidateRequestId(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 200)
            throw new AssessmentWorkspaceException(400, "A stable requestId of at most 200 characters is required.");
    }

    public async Task<ScanImportRecord> CaptureImportAsync(string systemId, string? planId,
        string? expectedPlanHash, string? requestId, string fileHash, string fileName, long fileSize,
        string importType, CancellationToken ct)
    {
        var access = await AccessAsync(systemId, ct);
        if (!access.Permissions.CanRunAssessments) throw new AssessmentWorkspaceException(403, "Import permission required.");
        requestId ??= fileHash;
        ValidateRequestId(requestId);
        var key = AssessmentResultProvenance.Hash($"{tenant.EffectiveTenantId}:{systemId}:import:{requestId}");
        var intent = AssessmentResultProvenance.Hash(JsonSerializer.Serialize(new { planId, expectedPlanHash, fileHash }));
        var prior = await db.ScanImportRecords.SingleOrDefaultAsync(x => x.RegisteredSystemId == systemId && x.WorkspaceOperationKey == key, ct);
        if (prior is not null)
        {
            if (prior.FileHash != fileHash || AssessmentResultProvenance.Read(prior.ResultProvenanceJson).RequestIntentHash != intent)
                throw new AssessmentWorkspaceException(409, "Request key was already used for a different file or plan scope.");
            return prior;
        }
        var plan = await PlanAsync(systemId, planId, expectedPlanHash, true, ct);
        var duplicate = await db.ScanImportRecords.Where(x => x.RegisteredSystemId == systemId
            && x.FileHash == fileHash && !x.IsDryRun).OrderByDescending(x => x.ImportedAt).FirstOrDefaultAsync(ct);
        if (duplicate is not null) return duplicate;
        var assessmentId = await Ato.Copilot.Agents.Compliance.Services.ScanImport.ScanImportService
            .GetOrCreateAssessmentAsync(db, systemId, Actor, ct);
        var record = new ScanImportRecord
        {
            RegisteredSystemId = systemId, AssessmentId = assessmentId, FileName = Path.GetFileName(fileName),
            FileHash = fileHash, FileSizeBytes = fileSize, ImportStatus = ScanImportStatus.Queued,
            ImportType = importType == "CKL" ? ScanImportType.Ckl : importType == "XCCDF" ? ScanImportType.Xccdf : ScanImportType.NessusXml,
            ImportedBy = Actor, WorkspaceOperationKey = key,
            ResultProvenanceJson = new AssessmentResultProvenance { Plan = plan,
                RequestIntentHash = intent }.Serialize()
        };
        db.ScanImportRecords.Add(record);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var concurrent = await db.ScanImportRecords.FirstOrDefaultAsync(x => x.RegisteredSystemId == systemId
                && (x.WorkspaceOperationKey == key || x.FileHash == fileHash), ct);
            if (concurrent is null || concurrent.FileHash != fileHash) throw;
            return concurrent;
        }
        return record;
    }
}

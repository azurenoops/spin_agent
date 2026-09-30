using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.Environments;

public sealed partial class SystemEnvironmentService
{
    public async Task<EnvironmentImpactPreview> PreviewScopeAsync(string systemId, Guid attachmentId,
        EnvironmentScopeChangeRequest request, string actor, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AuthorizeAsync(db, systemId, true, ct);
        ExpectedVersion(await VersionAsync(db, systemId, ct), request.ExpectedVersion);
        var row = await Attachments(db, systemId).SingleOrDefaultAsync(x => x.Id == attachmentId, ct)
            ?? throw new KeyNotFoundException("Environment attachment not found.");
        ExpectedVersion(row.Version, request.ExpectedAttachmentVersion);
        if (row.State != "Attached") throw new ArgumentException("Reattach the environment before changing or reviewing its scope.");
        var currentScope = Read<EnvironmentScope>(row.ScopeJson);
        var allocationVersion = row.AllocationId is { } aid
            ? (long?)await Allocations(db).Where(x => x.Id == aid).Select(x => x.Revision).SingleAsync(ct) : null;
        var selection = new EnvironmentSourceSelection(row.Source, row.RegistrationId, row.AllocationId, allocationVersion);
        var source = await SourceAsync(db, selection, ct);
        var scope = await SelectedScopeAsync(db, systemId, request.DiscoveryToken, actor, request.ExpectedVersion,
            selection, source, request.ResourceIds, request.Exclusions, request.SharedDependencyResourceIds,
            currentScope.Version + 1, ct);
        if (request.ReviewPendingScope)
        {
            if (currentScope.ReviewState != "PendingReview" || !SameSelection(currentScope, scope))
                throw new ArgumentException("Explicit review must retain the exact pending resource selection, exclusions and shared dependencies. Stage selection changes separately.");
            scope = scope with { ReviewState = "Reviewed" };
        }
        return await SavePreviewAsync(db, row, request.ExpectedVersion, actor, "Scope", request.Rationale, scope, ct,
            request.ReviewPendingScope);
    }

    public async Task<EnvironmentImpactPreview> PreviewDetachAsync(string systemId, Guid attachmentId,
        PreviewEnvironmentDetachRequest request, string actor, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AuthorizeAsync(db, systemId, true, ct);
        ExpectedVersion(await VersionAsync(db, systemId, ct), request.ExpectedVersion);
        var row = await Attachments(db, systemId).SingleOrDefaultAsync(x => x.Id == attachmentId, ct)
            ?? throw new KeyNotFoundException("Environment attachment not found.");
        ExpectedVersion(row.Version, request.ExpectedAttachmentVersion);
        return await SavePreviewAsync(db, row, request.ExpectedVersion, actor, "Detach", request.Rationale, null, ct);
    }

    private async Task<EnvironmentImpactPreview> SavePreviewAsync(Data.Context.AtoCopilotContext db,
        SystemEnvironmentAttachmentRecord row, long version, string actor, string kind, string rationale,
        EnvironmentScope? scope, CancellationToken ct, bool reviewPendingScope = false)
    {
        rationale = Text(rationale, "change rationale", 8000);
        var pending = new SystemEnvironmentPendingOperation { TenantId = TenantId, SystemId = row.SystemId,
            Actor = Text(actor, "actor", 254), Kind = kind, Version = version, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15) };
        var system = await RequireSystemAsync(db, row.SystemId, ct);
        var warnings = new List<string> {
            kind == "Detach" ? "Future access and collection stop; retained evidence and scope history remain."
                : reviewPendingScope
                    ? "This reviews only the exact environment resource selection. Recorded boundaries, approved baselines, inheritance and authorization decisions remain unchanged; reconcile documentation separately."
                    : "Access checks must be repeated. Existing boundary records and approved baselines are not changed."
        };
        if (kind == "Scope" && !reviewPendingScope && row.AllocationId.HasValue)
            warnings.Add("Optional provider-scope links and their applicability reviews are independent and are not automatically changed.");
        var impact = new EnvironmentImpactPreview(pending.Id.ToString(), row.SystemId, row.AllocationId, version,
            pending.ExpiresAt, [new(row.SystemId, system.Name, row.Id, row.Version,
                Read<EnvironmentScope>(row.ScopeJson).ResourceIds.Count, true, true)], scope?.ReviewState == "PendingReview",
            warnings);
        pending.MaterialJson = Json(new ChangeMaterial(row.Id, row.Version, rationale, scope, impact)
        { ReviewPendingScope = reviewPendingScope });
        db.Add(pending);
        await db.SaveChangesAsync(ct);
        return impact;
    }

    public Task<SystemEnvironmentsResponse> CommitScopeAsync(string systemId, Guid attachmentId,
        CommitEnvironmentChangeRequest request, string key, string actor, CancellationToken ct = default) =>
        CommitChangeAsync(systemId, attachmentId, request, key, actor, "Scope", ct);
    public Task<SystemEnvironmentsResponse> DetachAsync(string systemId, Guid attachmentId,
        CommitEnvironmentChangeRequest request, string key, string actor, CancellationToken ct = default) =>
        CommitChangeAsync(systemId, attachmentId, request, key, actor, "Detach", ct);

    private Task<SystemEnvironmentsResponse> CommitChangeAsync(string systemId, Guid attachmentId,
        CommitEnvironmentChangeRequest request, string key, string actor, string kind, CancellationToken ct) =>
        WriteAsync(systemId, kind, request, key, actor, request.ExpectedVersion, async db =>
        {
            if (!request.AcknowledgeImpact || !Guid.TryParse(request.PreviewId, out var previewId))
                throw new ArgumentException("Review and acknowledge the current impact preview.");
            var preview = await db.Set<SystemEnvironmentPendingOperation>().SingleOrDefaultAsync(x => x.Id == previewId
                && x.TenantId == TenantId && x.SystemId == systemId && x.Actor == actor && x.Kind == kind, ct);
            if (preview is null || preview.ExpiresAt <= DateTimeOffset.UtcNow || preview.Version != request.ExpectedVersion)
                throw new DbUpdateConcurrencyException("The impact preview expired or no longer matches this context.");
            var material = Read<ChangeMaterial>(preview.MaterialJson);
            if (material.AttachmentId != attachmentId || Text(request.Rationale, "change rationale", 8000) != material.Rationale)
                throw new ArgumentException("The change must match the reviewed attachment and rationale.");
            var row = await Attachments(db, systemId).SingleOrDefaultAsync(x => x.Id == attachmentId, ct)
                ?? throw new KeyNotFoundException("Environment attachment not found.");
            ExpectedVersion(row.Version, material.AttachmentVersion);
            if (kind == "Scope")
            {
                if (row.State != "Attached") throw new DbUpdateConcurrencyException("The environment is no longer attached.");
                var allocationVersion = row.AllocationId is { } aid
                    ? (long?)await Allocations(db).Where(x => x.Id == aid).Select(x => x.Revision).SingleAsync(ct) : null;
                var source = await SourceAsync(db, new(row.Source, row.RegistrationId, row.AllocationId, allocationVersion), ct);
                if (material.Scope is null || material.Scope.ResourceIds.Any(x => !source.PermittedScopes.Any(s => Inside(s, x))))
                    throw new DbUpdateConcurrencyException("The current source no longer permits the reviewed scope.");
                var currentScope = Read<EnvironmentScope>(row.ScopeJson);
                if (material.ReviewPendingScope && (currentScope.ReviewState != "PendingReview"
                    || !SameSelection(currentScope, material.Scope)))
                    throw new DbUpdateConcurrencyException("The pending selection changed after review.");
            }
            Retain(row, material.ReviewPendingScope ? "ScopeReviewed" : kind, actor, material.Rationale);
            if (kind == "Detach")
            {
                row.State = "Detached";
                await UnlinkAttachmentAsync(db, systemId, row.Id, actor, material.Rationale, ct);
            }
            else
            {
                var reviewed = material.Scope ?? throw new InvalidDataException("The retained scope preview has no selected scope.");
                row.ScopeJson = Json(reviewed.ReviewState == "Reviewed"
                    ? reviewed with { ReviewedBy = actor, ReviewedAt = DateTimeOffset.UtcNow } : reviewed);
            }
            row.Version++;
            row.UpdatedBy = actor;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.AssessmentAccessJson = row.MonitoringAccessJson = Json(Unchecked);
        }, ct);

    private static bool SameSelection(EnvironmentScope left, EnvironmentScope right) =>
        left.ResourceIds.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(
            right.ResourceIds.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
        && left.SharedDependencyResourceIds.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(
            right.SharedDependencyResourceIds.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
        && left.Exclusions.OrderBy(x => x.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Select(x => (x.ResourceId.ToLowerInvariant(), x.Rationale)).SequenceEqual(
                right.Exclusions.OrderBy(x => x.ResourceId, StringComparer.OrdinalIgnoreCase)
                    .Select(x => (x.ResourceId.ToLowerInvariant(), x.Rationale)));

    public async Task<EnvironmentAccessResponse> CheckAccessAsync(string systemId, CheckEnvironmentAccessRequest request,
        string actor, CancellationToken ct = default)
    {
        if (!Enum.TryParse<EnvironmentScopePurpose>(request.Purpose, out var purpose)
            || purpose == EnvironmentScopePurpose.Documentation) throw new ArgumentException("Choose Assessment or Monitoring access.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var permission = await AuthorizeAsync(db, systemId, false, ct);
        if (!permission.CanCheckAccess) throw new UnauthorizedAccessException("Environment access-check permission is required.");
        ExpectedVersion(await VersionAsync(db, systemId, ct), request.ExpectedVersion);
        var rows = await Attachments(db, systemId).ToListAsync(ct);
        foreach (var row in rows)
        {
            var source = await ResolveSourceAsync(db, row, ct);
            var priorJson = purpose == EnvironmentScopePurpose.Assessment ? row.AssessmentAccessJson : row.MonitoringAccessJson;
            var prior = Read<EnvironmentCheckState>(priorJson);
            var result = source.Eligible ? await azure.CheckAccessAsync(source, purpose, ct)
                : BlockedAccess(priorJson, source.IneligibleReason);
            result = result with { Sources = result.Sources.Select(check => check with
            {
                LastSucceededAt = check.LastSucceededAt ?? prior.Sources.SingleOrDefault(previous =>
                    previous.SourceId == check.SourceId && previous.Kind == check.Kind
                    && previous.SourceRevision == check.SourceRevision)?.LastSucceededAt
            }).ToArray() };
            // Recheck after the external read; concurrent withdrawal must not restore an available grant.
            var current = await ResolveSourceAsync(db, row, ct);
            if (!current.Eligible) result = BlockedAccess(Json(result), current.IneligibleReason);
            if (purpose == EnvironmentScopePurpose.Assessment) row.AssessmentAccessJson = Json(result);
            else row.MonitoringAccessJson = Json(result);
        }
        await db.SaveChangesAsync(ct);
        var projected = await ProjectAsync(db, systemId, permission, ct);
        return new(systemId, projected.Version, projected.Attachments);
    }
}

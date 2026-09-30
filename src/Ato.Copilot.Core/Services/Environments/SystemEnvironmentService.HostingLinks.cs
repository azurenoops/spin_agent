using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.Environments;

public sealed partial class SystemEnvironmentService
{
    private sealed record LinkPreviewMaterial(PreviewEnvironmentHostingLinkRequest Request,
        long SelectionVersion, long? LinkVersion)
    {
        public bool RemoveInvalidLegacyMapping { get; init; }
    }
    private sealed record ScopeRemovalMaterial(Guid AssignmentId, long AssignmentVersion, long SelectionVersion,
        long? RelationshipVersion, string Rationale, IReadOnlyList<EnvironmentHostingLink> Links)
    {
        public IReadOnlyList<string> Blockers { get; init; } = [];
    }

    private IQueryable<SystemEnvironmentHostingLinkRecord> HostingLinks(AtoCopilotContext db, string systemId) =>
        db.Set<SystemEnvironmentHostingLinkRecord>().Where(x => x.TenantId == TenantId && x.SystemId == systemId);

    private async Task RequireInitialLinkTargetAsync(AtoCopilotContext db, string systemId, Guid assignmentId, CancellationToken ct)
    {
        var assignment = await SystemHosting(db, systemId).AsNoTracking().SingleOrDefaultAsync(x => x.Id == assignmentId, ct);
        if (assignment is null || await ProviderSelections(db, systemId).AnyAsync(x =>
            x.AssignmentId == assignmentId && x.State == "Removed", ct))
            throw new ArgumentException("Choose an existing active provider scope in this exact system, or attach without a provider link.");
        var current = await db.Set<ProviderOffering>().IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.Id == assignment.OfferingId && x.ProviderId == assignment.ProviderId && x.Lifecycle == "Active"
            && x.CurrentHostingScopeRevisionId == assignment.HostingScopeRevisionId, ct);
        if (!current || !await db.Set<ProviderHostingScopeRevision>().IgnoreQueryFilters().AnyAsync(x =>
            x.Id == assignment.HostingScopeRevisionId && x.OfferingId == assignment.OfferingId
            && x.ProviderId == assignment.ProviderId, ct))
            throw new ArgumentException("The selected provider scope is no longer current. Refresh the scope selection or attach without a link.");
    }

    private async Task ApplyInitialLinkAsync(AtoCopilotContext db, string systemId, SystemEnvironmentAttachmentRecord attachment,
        Guid assignmentId, string actor, CancellationToken ct)
    {
        var row = await HostingLinks(db, systemId).SingleOrDefaultAsync(x =>
            x.AttachmentId == attachment.Id && x.AssignmentId == assignmentId, ct);
        if (row is null)
        {
            row = new() { TenantId = TenantId, SystemId = systemId, AttachmentId = attachment.Id, AssignmentId = assignmentId };
            db.Add(row);
        }
        else row.Version++;
        row.State = "Linked";
        row.Source = "Explicit";
        row.UpdatedBy = actor;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.HistoryJson = AppendHistory(row.HistoryJson, "LinkedOnExplicitApply", actor,
            "Provider scope explicitly selected in the reviewed subscription attachment request.", row.Version);
        attachment.HostingAssignmentId = assignmentId;
    }

    private async Task<(IReadOnlyList<EnvironmentHostingLink> Links, IReadOnlyList<LegacyEnvironmentReference> Warnings)>
        LinkProjectionAsync(AtoCopilotContext db, string systemId, CancellationToken ct)
    {
        var records = await HostingLinks(db, systemId).OrderBy(x => x.Id).ToListAsync(ct);
        var attachments = await Attachments(db, systemId).ToListAsync(ct);
        var assignments = await SystemHosting(db, systemId).ToDictionaryAsync(x => x.Id, ct);
        var removed = await ProviderSelections(db, systemId).Where(x => x.State == "Removed").Select(x => x.AssignmentId).ToListAsync(ct);
        var links = records.Select(x => new EnvironmentHostingLink(x.Id, x.AttachmentId, x.AssignmentId,
            x.Version, x.State, x.Source, x.UpdatedAt)).ToList();
        var warnings = new List<LegacyEnvironmentReference>();
        foreach (var attachment in attachments.Where(x => x.HostingAssignmentId.HasValue))
        {
            var assignmentId = attachment.HostingAssignmentId!.Value;
            if (records.Any(x => x.AttachmentId == attachment.Id && x.AssignmentId == assignmentId)) continue;
            var assignment = assignments.GetValueOrDefault(assignmentId);
            var valid = assignment is not null && await db.Set<ProviderHostingScopeRevision>().IgnoreQueryFilters().AnyAsync(x =>
                x.Id == assignment.HostingScopeRevisionId && x.ProviderId == assignment.ProviderId && x.OfferingId == assignment.OfferingId, ct);
            if (!valid)
            {
                var identity = Read<EnvironmentRegistration>(attachment.RegistrationSnapshotJson);
                warnings.Add(new(attachment.Id.ToString(), "ProviderScopeLink", identity.DisplayName,
                    "ReconciliationRequired", $"The retained provider link for subscription '{identity.DisplayName}' does not identify a scope in this system. Remove or replace this link; the subscription is preserved."));
                continue;
            }
            links.Add(new(attachment.Id, attachment.Id, assignmentId, 0,
                attachment.State == "Attached" && !removed.Contains(assignmentId) ? "Linked" : "Unlinked", "RetainedLegacy", attachment.UpdatedAt));
        }
        return (links, warnings);
    }

    private async Task PreserveLegacyLinksAsync(AtoCopilotContext db, string systemId, CancellationToken ct)
    {
        var projection = await LinkProjectionAsync(db, systemId, ct);
        foreach (var link in projection.Links.Where(x => x.Source == "RetainedLegacy" && x.Version == 0))
            db.Add(new SystemEnvironmentHostingLinkRecord { Id = link.LinkId, TenantId = TenantId, SystemId = systemId,
                AttachmentId = link.AttachmentId, AssignmentId = link.AssignmentId, State = link.State,
                Source = "RetainedLegacy", UpdatedAt = link.UpdatedAt, UpdatedBy = "retained-explicit-link" });
        if (projection.Links.Any(x => x.Source == "RetainedLegacy" && x.Version == 0))
            await db.SaveChangesAsync(ct);
    }

    public async Task<EnvironmentImpactPreview> PreviewHostingLinkAsync(string systemId,
        PreviewEnvironmentHostingLinkRequest request, string actor, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AuthorizeAsync(db, systemId, true, ct);
        ExpectedVersion(await VersionAsync(db, systemId, ct), request.ExpectedVersion);
        if (request.Action is not ("Link" or "Unlink")) throw new ArgumentException("Choose Link or Unlink.");
        var attachment = await Attachments(db, systemId).SingleOrDefaultAsync(x => x.Id == request.AttachmentId, ct)
            ?? throw new KeyNotFoundException("Subscription attachment not found in this system.");
        ExpectedVersion(attachment.Version, request.ExpectedAttachmentVersion);
        var assignment = await SystemHosting(db, systemId).SingleOrDefaultAsync(x => x.Id == request.AssignmentId, ct);
        if (assignment is null && request.Action == "Unlink" && request.ExpectedAssignmentVersion == 0
            && attachment.HostingAssignmentId == request.AssignmentId)
        {
            var invalid = new LinkPreviewMaterial(request with { Rationale = Text(request.Rationale, "link rationale", 8000) }, 0, null)
            { RemoveInvalidLegacyMapping = true };
            return await SaveIndependentPreviewAsync(db, systemId, request.ExpectedVersion, actor, "HostingLink", Json(invalid),
                [new(systemId, (await RequireSystemAsync(db, systemId, ct)).Name, attachment.Id, attachment.Version,
                    Read<EnvironmentScope>(attachment.ScopeJson).ResourceIds.Count, false, false)],
                ["Remove only the invalid retained mapping. No foreign provider record is read or changed; the subscription and historical mapping remain retained."], ct);
        }
        if (assignment is null) throw new KeyNotFoundException("Provider scope not found in this system.");
        ExpectedVersion(assignment.Revision, request.ExpectedAssignmentVersion);
        var selection = await ProviderSelections(db, systemId).SingleOrDefaultAsync(x => x.AssignmentId == assignment.Id, ct);
        if (request.Action == "Link" && (attachment.State != "Attached" || selection?.State == "Removed"))
            throw new ArgumentException("Link an attached subscription and an active provider scope.");
        var projected = (await LinkProjectionAsync(db, systemId, ct)).Links.SingleOrDefault(x =>
            x.AttachmentId == attachment.Id && x.AssignmentId == assignment.Id);
        if (request.Action == "Unlink" && projected?.State != "Linked")
            throw new ArgumentException("This subscription and provider scope have no active link.");
        var material = new LinkPreviewMaterial(request with { Rationale = Text(request.Rationale, "link rationale", 8000) },
            selection?.Version ?? 0, projected?.Version);
        return await SaveIndependentPreviewAsync(db, systemId, request.ExpectedVersion, actor, "HostingLink", Json(material),
            [new(systemId, (await RequireSystemAsync(db, systemId, ct)).Name, attachment.Id, attachment.Version,
                Read<EnvironmentScope>(attachment.ScopeJson).ResourceIds.Count, false, false)],
            ["This changes only an optional documentary link. It does not change subscription entitlement, selected resources, provider scope, applicability or responsibility acceptance."], ct);
    }

    public Task<SystemEnvironmentsResponse> CommitHostingLinkAsync(string systemId, CommitEnvironmentChangeRequest request,
        string key, string actor, CancellationToken ct = default) =>
        WriteAsync(systemId, "HostingLink", request, key, actor, request.ExpectedVersion, async db =>
        {
            var pending = await RequireIndependentPreviewAsync(db, systemId, request, actor, "HostingLink", ct);
            var material = Read<LinkPreviewMaterial>(pending.MaterialJson);
            if (Text(request.Rationale, "link rationale", 8000) != material.Request.Rationale)
                throw new ArgumentException("The rationale must match the reviewed link change.");
            var attachment = await Attachments(db, systemId).SingleOrDefaultAsync(x => x.Id == material.Request.AttachmentId, ct)
                ?? throw new KeyNotFoundException("Attachment not found.");
            ExpectedVersion(attachment.Version, material.Request.ExpectedAttachmentVersion);
            if (material.RemoveInvalidLegacyMapping)
            {
                if (attachment.HostingAssignmentId != material.Request.AssignmentId
                    || await SystemHosting(db, systemId).AnyAsync(x => x.Id == material.Request.AssignmentId, ct))
                    throw new DbUpdateConcurrencyException("The retained mapping changed after preview.");
                Retain(attachment, "InvalidHostingLinkRemoved", actor, request.Rationale);
                attachment.HostingAssignmentId = null;
                return;
            }
            var assignment = await SystemHosting(db, systemId).SingleOrDefaultAsync(x => x.Id == material.Request.AssignmentId, ct)
                ?? throw new KeyNotFoundException("Provider scope not found.");
            ExpectedVersion(assignment.Revision, material.Request.ExpectedAssignmentVersion);
            var selection = await ProviderSelections(db, systemId).SingleOrDefaultAsync(x => x.AssignmentId == assignment.Id, ct);
            ExpectedVersion(selection?.Version ?? 0, material.SelectionVersion);
            if (material.Request.Action == "Link" && (attachment.State != "Attached" || selection?.State == "Removed"))
                throw new DbUpdateConcurrencyException("The subscription or provider relationship is no longer active.");
            var row = await HostingLinks(db, systemId).SingleOrDefaultAsync(x => x.AttachmentId == attachment.Id && x.AssignmentId == assignment.Id, ct);
            if (row is not null && !(material.LinkVersion == 0 && row.Source == "RetainedLegacy" && row.Version == 1))
                ExpectedVersion(row.Version, material.LinkVersion ?? -1);
            if (row is null && material.LinkVersion.HasValue) throw new DbUpdateConcurrencyException("The link changed after preview.");
            if (row is null)
            {
                row = new() { TenantId = TenantId, SystemId = systemId, AttachmentId = attachment.Id, AssignmentId = assignment.Id };
                db.Add(row);
            }
            else row.Version++;
            row.State = material.Request.Action == "Link" ? "Linked" : "Unlinked";
            row.HistoryJson = AppendHistory(row.HistoryJson, material.Request.Action, actor, request.Rationale, row.Version);
            row.UpdatedAt = DateTimeOffset.UtcNow; row.UpdatedBy = actor;
            if (attachment.HostingAssignmentId == assignment.Id) attachment.HostingAssignmentId = null;
        }, ct);

    private async Task UnlinkAttachmentAsync(AtoCopilotContext db, string systemId, Guid attachmentId,
        string actor, string rationale, CancellationToken ct)
    {
        foreach (var link in await HostingLinks(db, systemId).Where(x => x.AttachmentId == attachmentId && x.State == "Linked").ToListAsync(ct))
            Unlink(link, actor, rationale);
        var attachment = await Attachments(db, systemId).SingleAsync(x => x.Id == attachmentId, ct);
        attachment.HostingAssignmentId = null;
    }
    private static void Unlink(SystemEnvironmentHostingLinkRecord row, string actor, string rationale)
    {
        row.HistoryJson = AppendHistory(row.HistoryJson, "Unlinked", actor, rationale, row.Version);
        row.State = "Unlinked"; row.Version++; row.UpdatedBy = actor; row.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public async Task<EnvironmentImpactPreview> PreviewProviderScopeRemovalAsync(string systemId, Guid assignmentId,
        PreviewProviderScopeRemovalRequest request, string actor, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AuthorizeAsync(db, systemId, true, ct);
        ExpectedVersion(await VersionAsync(db, systemId, ct), request.ExpectedVersion);
        var assignment = await SystemHosting(db, systemId).SingleOrDefaultAsync(x => x.Id == assignmentId, ct)
            ?? throw new KeyNotFoundException("Provider scope not found in this system.");
        ExpectedVersion(assignment.Revision, request.ExpectedAssignmentVersion);
        var selection = await ProviderSelections(db, systemId).SingleOrDefaultAsync(x => x.AssignmentId == assignmentId, ct);
        ExpectedVersion(selection?.Version ?? 0, request.ExpectedSelectionVersion);
        if (selection?.State == "Removed") throw new ArgumentException("The provider scope is already removed.");
        var review = await db.Set<MissionProviderRelationshipReview>().SingleOrDefaultAsync(x =>
            x.TenantId == TenantId && x.SystemId == systemId && x.AssignmentId == assignmentId, ct);
        var links = (await LinkProjectionAsync(db, systemId, ct)).Links.Where(x => x.AssignmentId == assignmentId && x.State == "Linked").ToArray();
        var ids = links.Select(x => x.AttachmentId).ToArray();
        var rows = await Attachments(db, systemId).Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var name = (await RequireSystemAsync(db, systemId, ct)).Name;
        var blockers = await ProviderRemovalBlockersAsync(db, systemId, assignment, ct);
        var material = new ScopeRemovalMaterial(assignmentId, assignment.Revision, selection?.Version ?? 0,
            review?.Revision, Text(request.Rationale, "removal rationale", 8000), links) { Blockers = blockers };
        var adoptionCount = await db.Set<CapabilityAdoptionSnapshot>().CountAsync(x =>
            x.TenantId == TenantId && x.SystemId == systemId && x.AssignmentId == assignmentId, ct);
        var preview = await SaveIndependentPreviewAsync(db, systemId, request.ExpectedVersion, actor, "RemoveProviderScope",
            Json(material), rows.Select(x => new EnvironmentImpactSystem(systemId, name, x.Id, x.Version,
                Read<EnvironmentScope>(x.ScopeJson).ResourceIds.Count, false, false)).ToArray(),
            ["The provider relationship and optional links are removed from active use, not deleted. Subscription attachments, allocation entitlement, recorded applicability/responsibility decisions and evidence remain retained; review downstream documentation separately.",
                $"{adoptionCount} retained capability adoption records remain unchanged. Review their applicability and customer responsibilities separately."], ct);
        return preview with { Blockers = blockers, CanCommit = blockers.Count == 0 };
    }

    public Task<SystemEnvironmentsResponse> RemoveProviderScopeAsync(string systemId, Guid assignmentId,
        CommitEnvironmentChangeRequest request, string key, string actor, CancellationToken ct = default) =>
        WriteAsync(systemId, $"RemoveProviderScope:{assignmentId:D}", request, key, actor, request.ExpectedVersion, async db =>
        {
            var pending = await RequireIndependentPreviewAsync(db, systemId, request, actor, "RemoveProviderScope", ct);
            var material = Read<ScopeRemovalMaterial>(pending.MaterialJson);
            if (material.AssignmentId != assignmentId || material.Rationale != Text(request.Rationale, "removal rationale", 8000))
                throw new ArgumentException("The target and rationale must match the reviewed removal.");
            var assignment = await SystemHosting(db, systemId).SingleOrDefaultAsync(x => x.Id == assignmentId, ct)
                ?? throw new KeyNotFoundException("Provider scope not found.");
            ExpectedVersion(assignment.Revision, material.AssignmentVersion);
            var selection = await ProviderSelections(db, systemId).SingleOrDefaultAsync(x => x.AssignmentId == assignmentId, ct);
            ExpectedVersion(selection?.Version ?? 0, material.SelectionVersion);
            var review = await db.Set<MissionProviderRelationshipReview>().SingleOrDefaultAsync(x =>
                x.TenantId == TenantId && x.SystemId == systemId && x.AssignmentId == assignmentId, ct);
            if (review?.Revision != material.RelationshipVersion) throw new DbUpdateConcurrencyException("The relationship review changed after preview.");
            var blockers = await ProviderRemovalBlockersAsync(db, systemId, assignment, ct);
            if (blockers.Count > 0)
                throw new DbUpdateConcurrencyException("Provider scope removal is blocked. " + string.Join(" ", blockers));
            if (material.Blockers.Count > 0)
                throw new DbUpdateConcurrencyException("Active adoption dependencies changed. Refresh the removal preview after explicitly retiring them.");
            if (selection is null)
            {
                selection = new() { TenantId = TenantId, SystemId = systemId, AssignmentId = assignmentId };
                db.Add(selection);
            }
            else selection.Version++;
            selection.State = "Removed"; selection.UpdatedAt = DateTimeOffset.UtcNow; selection.UpdatedBy = actor;
            selection.HistoryJson = AppendHistory(selection.HistoryJson, "Removed", actor, request.Rationale, selection.Version);
            foreach (var link in await HostingLinks(db, systemId).Where(x => x.AssignmentId == assignmentId && x.State == "Linked").ToListAsync(ct))
                Unlink(link, actor, request.Rationale);
            foreach (var attachment in await Attachments(db, systemId).Where(x => x.HostingAssignmentId == assignmentId).ToListAsync(ct))
                attachment.HostingAssignmentId = null;
        }, ct);

    private async Task<IReadOnlyList<string>> ProviderRemovalBlockersAsync(AtoCopilotContext db, string systemId,
        ProviderHostingAssignment assignment, CancellationToken ct)
    {
        var dependencies = await (from adoption in db.Set<CapabilityAdoptionSnapshot>().AsNoTracking()
            join subscription in db.CapabilitySubscriptions.AsNoTracking() on adoption.SubscriptionId equals subscription.Id
            where adoption.TenantId == TenantId && adoption.SystemId == systemId && adoption.AssignmentId == assignment.Id
                && adoption.ProviderId == assignment.ProviderId && adoption.OfferingId == assignment.OfferingId
                && subscription.RoutingTenantId == TenantId && subscription.RegisteredSystemId == systemId
                && subscription.IsActive && subscription.CurrentAdoptionSnapshotId == adoption.Id
            select new { subscription.Id, adoption.CapabilityId }).Distinct().OrderBy(x => x.Id).ToListAsync(ct);
        var ids = dependencies.Select(x => x.CapabilityId).ToArray();
        var names = await db.CspInheritedCapabilities.AsNoTracking().Where(x => ids.Contains(x.Id)
            && x.CspInheritedComponent.CspProfileId == assignment.ProviderId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return dependencies.Select(x =>
            $"Active adopted capability '{names.GetValueOrDefault(x.CapabilityId, "Unavailable capability")}' " +
            $"(capability {x.CapabilityId:D}, subscription {x.Id}) depends on this provider scope. " +
            "Explicitly retire it through the existing capability unsubscribe workflow before removing this relationship.").ToArray();
    }

    private async Task<EnvironmentImpactPreview> SaveIndependentPreviewAsync(AtoCopilotContext db, string systemId,
        long version, string actor, string kind, string material, IReadOnlyList<EnvironmentImpactSystem> systems,
        IReadOnlyList<string> warnings, CancellationToken ct)
    {
        var pending = new SystemEnvironmentPendingOperation { TenantId = TenantId, SystemId = systemId,
            Version = version, Actor = Text(actor, "actor", 254), Kind = kind, MaterialJson = material,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15) };
        db.Add(pending);
        await db.SaveChangesAsync(ct);
        return new(pending.Id.ToString(), systemId, null, version, pending.ExpiresAt, systems, false, warnings);
    }

    private async Task<SystemEnvironmentPendingOperation> RequireIndependentPreviewAsync(AtoCopilotContext db, string systemId,
        CommitEnvironmentChangeRequest request, string actor, string kind, CancellationToken ct)
    {
        if (!request.AcknowledgeImpact || !Guid.TryParse(request.PreviewId, out var id))
            throw new ArgumentException("Review and acknowledge the exact impact preview.");
        var row = await db.Set<SystemEnvironmentPendingOperation>().SingleOrDefaultAsync(x => x.Id == id
            && x.TenantId == TenantId && x.SystemId == systemId && x.Actor == actor && x.Kind == kind, ct);
        if (row is null || row.Version != request.ExpectedVersion || row.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new DbUpdateConcurrencyException("The reviewed change expired or belongs to another context.");
        return row;
    }
}

using System.Security.Cryptography;
using System.Text;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed class ProviderEvidenceSharingService(ProviderAuthorizationStore store, IFileStorageProvider storage,
    IProviderMissionService mission, ISystemWorkspaceAccessService access) : IProviderEvidenceSharingService
{
    // Each filter bypass is constrained by an already authorized offering or a mission's exact grant.
    private static IQueryable<ProviderHostingAssignment> Eligible(AtoCopilotContext db, Guid provider, Guid offering) =>
        db.Set<ProviderHostingAssignment>().IgnoreQueryFilters().Where(a =>
            a.ProviderId == provider && a.OfferingId == offering
            && db.Set<ProviderOffering>().IgnoreQueryFilters().Any(o => o.Id == a.OfferingId
                && o.ProviderId == a.ProviderId && o.Lifecycle != "Retired" && o.CurrentHostingScopeRevisionId == a.HostingScopeRevisionId)
            && db.RegisteredSystems.IgnoreQueryFilters().Any(s => s.Id == a.SystemId && s.TenantId == a.TargetTenantId && s.IsActive)
            && (db.Set<MissionProviderRelationshipReview>().IgnoreQueryFilters().Any(r =>
                r.ProviderId == a.ProviderId && r.OfferingId == a.OfferingId && r.TenantId == a.TargetTenantId
                && r.SystemId == a.SystemId && r.AssignmentId == a.Id && r.AssignmentRevision == a.Revision)
            || db.Set<CapabilityAdoptionSnapshot>().IgnoreQueryFilters().Any(r =>
                r.ProviderId == a.ProviderId && r.OfferingId == a.OfferingId && r.TenantId == a.TargetTenantId
                && r.SystemId == a.SystemId && r.AssignmentId == a.Id && r.AssignmentRevision == a.Revision
                && db.CapabilitySubscriptions.IgnoreQueryFilters().Any(s => s.Id == r.SubscriptionId
                    && s.RoutingTenantId == a.TargetTenantId && s.RegisteredSystemId == a.SystemId
                    && s.IsActive && s.CurrentAdoptionSnapshotId == r.Id))));

    public async Task<PagedResult<ProviderEvidenceShareTarget>> TargetsAsync(Guid offeringId, int page, int pageSize, CancellationToken ct = default)
    {
        store.Authorize();
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        var targets = await PageAsync(Eligible(db, offering.ProviderId, offering.Id).AsNoTracking().OrderBy(x => x.Id),
            page, pageSize, x => x, ct);
        var items = new List<ProviderEvidenceShareTarget>();
        foreach (var a in targets.Items)
        {
            var name = await db.RegisteredSystems.IgnoreQueryFilters().Where(s => s.TenantId == a.TargetTenantId && s.Id == a.SystemId)
                .Select(s => s.Name).SingleAsync(ct);
            items.Add(new(a.Id, a.Revision, a.TargetTenantId, a.SystemId, name));
        }
        return new(items, page, pageSize, targets.Total);
    }

    public async Task<PagedResult<ProviderEvidenceShareResponse>> ListProviderAsync(Guid offeringId, Guid evidenceId,
        int page, int pageSize, CancellationToken ct = default, Guid? assignmentId = null)
    {
        store.Authorize();
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        await EvidenceAsync(db, offering, evidenceId, ct);
        return await PageAsync(db.Set<ProviderEvidenceShare>().AsNoTracking().Where(x => x.ProviderId == offering.ProviderId
            && x.OfferingId == offering.Id && x.EvidenceId == evidenceId && (!assignmentId.HasValue || x.AssignmentId == assignmentId.Value))
            .OrderByDescending(x => x.Version).ThenBy(x => x.Id),
            page, pageSize, Project, ct);
    }

    public Task<ProviderEvidenceShareResponse> ApproveAsync(Guid offeringId, Guid evidenceId,
        ApproveProviderEvidenceShareRequest request, string key, string actor, CancellationToken ct = default)
    {
        store.Authorize();
        Text(key, "Idempotency-Key", 100);
        return store.WriteAsync(offeringId, $"ApproveEvidenceShare:{offeringId}", key, new { evidenceId, request, actor },
            actor, async (db, _, offering) =>
        {
            var source = await EvidenceAsync(db, offering!, evidenceId, ct);
            Expected(source, request.ExpectedEvidenceRevision);
            var assignment = await Eligible(db, offering!.ProviderId, offering.Id)
                .SingleOrDefaultAsync(x => x.Id == request.AssignmentId, ct)
                ?? throw new KeyNotFoundException("An active, explicitly associated or adopted mission allocation is required.");
            Expected(assignment, request.ExpectedAssignmentRevision);
            var history = db.Set<ProviderEvidenceShare>().Where(x => x.ProviderId == offering.ProviderId
                && x.OfferingId == offering.Id && x.EvidenceId == evidenceId && x.AssignmentId == assignment.Id);
            var previous = await history.OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
            if (request.PreviousVersionId != previous?.Id || request.Version != (previous?.Version ?? 0) + 1)
                throw new DbUpdateConcurrencyException("Review the latest sharing version before approving a replacement.");
            var summary = Text(request.Summary, "summary", 8000);
            await VerifySourceAsync(source, ct);
            var now = DateTimeOffset.UtcNow;
            if (previous is not null && previous.RevokedAt is null)
                Revoke(previous, actor, "Superseded by explicitly approved replacement", now);
            var row = new ProviderEvidenceShare
            {
                ProviderId = offering.ProviderId, OfferingId = offering.Id, EvidenceId = source.Id, EvidenceRevision = source.Revision,
                AssignmentId = assignment.Id, AssignmentRevision = assignment.Revision,
                TargetTenantId = assignment.TargetTenantId, SystemId = assignment.SystemId,
                Version = request.Version, PreviousVersionId = previous?.Id, Summary = summary,
                SourceSha256 = source.Sha256, ApprovedBy = actor, ApprovedAt = now, CreatedBy = actor
            };
            row.ContentJson = Json(new
            {
                row.Id, row.ProviderId, row.OfferingId, row.EvidenceId, row.EvidenceRevision, row.AssignmentId, row.AssignmentRevision,
                row.TargetTenantId, row.SystemId, row.Version, row.PreviousVersionId, row.Summary,
                row.SourceSha256, row.ApprovedBy, row.ApprovedAt, Permission = "ApprovedSummaryOnly",
                PrivateAttachmentAccess = false
            });
            row.ContentHash = Hash(row.ContentJson);
            db.Add(row);
            Audit(db, offering, row.Id, "EvidenceSummaryApproved", actor, Project(row));
            return Project(row);
        }, ct);
    }

    public Task<ProviderEvidenceShareResponse> RevokeAsync(Guid offeringId, Guid shareId,
        RevokeProviderEvidenceShareRequest request, string key, string actor, CancellationToken ct = default)
    {
        store.Authorize();
        Text(key, "Idempotency-Key", 100);
        return store.WriteAsync(offeringId, $"RevokeEvidenceShare:{offeringId}", key, new { shareId, request, actor },
            actor, async (db, _, offering) =>
        {
            var row = await db.Set<ProviderEvidenceShare>().SingleOrDefaultAsync(x => x.Id == shareId
                && x.ProviderId == offering!.ProviderId && x.OfferingId == offering.Id, ct)
                ?? throw new KeyNotFoundException("Sharing approval not found.");
            Expected(row, request.ExpectedRevision);
            if (row.RevokedAt is not null) throw new DbUpdateConcurrencyException("Sharing approval is already revoked.");
            Revoke(row, actor, Text(request.Rationale, "rationale", 2000), DateTimeOffset.UtcNow);
            Audit(db, offering!, row.Id, "EvidenceSummaryRevoked", actor, new { row.RevocationReason, row.Revision });
            return Project(row);
        }, ct);
    }

    private async Task<IQueryable<ProviderEvidenceShare>> MissionQueryAsync(AtoCopilotContext db, string systemId, CancellationToken ct)
    {
        if (store.Tenant.IsCspAdmin || store.Tenant.ImpersonatedTenantId.HasValue || store.Tenant.EffectiveTenantId == Guid.Empty)
            throw new UnauthorizedAccessException("An ordinary assigned mission workspace is required.");
        await mission.AuthorizeAsync(systemId, false, false, ct);
        return GrantedQuery(db, store.Tenant.EffectiveTenantId, systemId);
    }

    // Call only after fresh system-read authorization. Exact scope predicates also permit workers
    // with no ambient tenant to verify server-captured provenance without impersonating an HTTP user.
    private static IQueryable<ProviderEvidenceShare> GrantedQuery(AtoCopilotContext db, Guid tenantId, string systemId) =>
        db.Set<ProviderEvidenceShare>().IgnoreQueryFilters().AsNoTracking()
            .Where(g => g.TargetTenantId == tenantId && g.SystemId == systemId && g.RevokedAt == null
                && db.CspProfiles.Any(p => p.Id == g.ProviderId)
                && db.Set<ProviderHostingAssignment>().IgnoreQueryFilters().Any(a => a.Id == g.AssignmentId
                    && a.ProviderId == g.ProviderId && a.OfferingId == g.OfferingId
                    && a.TargetTenantId == tenantId && a.SystemId == systemId && a.Revision == g.AssignmentRevision
                    && db.Set<ProviderOffering>().IgnoreQueryFilters().Any(o => o.ProviderId == g.ProviderId
                        && o.Id == g.OfferingId && o.Lifecycle != "Retired" && o.CurrentHostingScopeRevisionId == a.HostingScopeRevisionId)
                    && (db.Set<MissionProviderRelationshipReview>().IgnoreQueryFilters().Any(r => r.TenantId == tenantId && r.SystemId == systemId
                        && r.ProviderId == g.ProviderId && r.OfferingId == g.OfferingId && r.AssignmentId == a.Id && r.AssignmentRevision == a.Revision)
                    || db.Set<CapabilityAdoptionSnapshot>().IgnoreQueryFilters().Any(r => r.TenantId == tenantId && r.SystemId == systemId
                        && r.ProviderId == g.ProviderId && r.OfferingId == g.OfferingId && r.AssignmentId == a.Id && r.AssignmentRevision == a.Revision
                        && db.CapabilitySubscriptions.IgnoreQueryFilters().Any(s => s.Id == r.SubscriptionId && s.RoutingTenantId == tenantId
                            && s.RegisteredSystemId == systemId && s.IsActive && s.CurrentAdoptionSnapshotId == r.Id)))));

    public async Task<PagedResult<ProviderEvidenceShareResponse>> ListMissionAsync(string systemId, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        return await PageAsync((await MissionQueryAsync(db, systemId, ct)).OrderBy(x => x.Id), page, pageSize, Project, ct);
    }

    public async Task<byte[]> SummaryContentAsync(string systemId, Guid shareId, CancellationToken ct = default)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var row = await (await MissionQueryAsync(db, systemId, ct)).SingleOrDefaultAsync(x => x.Id == shareId, ct)
            ?? throw new KeyNotFoundException("Approved evidence summary not found for this tenant and system.");
        if (Hash(row.ContentJson) != row.ContentHash) throw new IOException("Retained approved summary failed integrity verification.");
        return Encoding.UTF8.GetBytes(row.ContentJson);
    }

    public async Task VerifyForExportAsync(Guid tenantId, Guid personId, string systemId, Guid shareId,
        string expectedHash, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || personId == Guid.Empty)
            throw new UnauthorizedAccessException("Export verification requires a captured mission tenant and requester.");
        systemId = Text(systemId, "systemId", 36);
        expectedHash = Text(expectedHash, "expectedHash", 64);
        if (shareId == Guid.Empty || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
            throw new ArgumentException("Supply the exact retained summary ID and SHA-256 export pin.");
        var permission = await access.GetAccessAsync(tenantId, personId, systemId, false, ct);
        if (!permission.Permissions.CanRead)
            throw new UnauthorizedAccessException("The captured requester no longer has access to this mission system.");
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var row = await GrantedQuery(db, tenantId, systemId).SingleOrDefaultAsync(x => x.Id == shareId, ct)
            ?? throw new KeyNotFoundException("Approved evidence summary is no longer available for this tenant and system.");
        if (Hash(row.ContentJson) != row.ContentHash)
            throw new IOException("Retained approved summary failed integrity verification.");
        if (!string.Equals(row.ContentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new DbUpdateConcurrencyException("The approved summary does not match the retained export pin.");
    }

    private static async Task<ProviderFindingEvidence> EvidenceAsync(AtoCopilotContext db, ProviderOffering offering,
        Guid evidenceId, CancellationToken ct) => await db.Set<ProviderFindingEvidence>().SingleOrDefaultAsync(x =>
            x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id && x.Id == evidenceId, ct)
        ?? throw new KeyNotFoundException("Retained evidence was not found in this provider offering.");

    private async Task VerifySourceAsync(ProviderFindingEvidence source, CancellationToken ct)
    {
        await using var content = await storage.GetAsync(source.StorageKey, ct)
            ?? throw new IOException("Retained source is unavailable.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long size = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, ct)) > 0)
        {
            size += read;
            if (size > ProviderFindingService.MaximumEvidenceBytes) throw new IOException("Retained source exceeds evidence limit.");
            hash.AppendData(buffer, 0, read);
        }
        if (size != source.ByteLength || Convert.ToHexString(hash.GetHashAndReset()) != source.Sha256)
            throw new IOException("Retained source failed integrity verification.");
    }

    private static void Revoke(ProviderEvidenceShare row, string actor, string rationale, DateTimeOffset now)
    {
        row.RevokedAt = now; row.RevokedBy = actor; row.RevocationReason = rationale; row.Revision++;
    }

    private static ProviderEvidenceShareResponse Project(ProviderEvidenceShare row) => new(row.Id, row.ProviderId,
        row.OfferingId, row.EvidenceId, row.AssignmentId, row.TargetTenantId, row.SystemId, row.Version,
        row.PreviousVersionId, row.Summary, row.SourceSha256, row.ContentHash, row.ApprovedBy, row.ApprovedAt,
        row.Revision, row.RevokedAt) { EvidenceRevision = row.EvidenceRevision, AssignmentRevision = row.AssignmentRevision };
}

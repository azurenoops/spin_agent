using System.Globalization;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Workspaces;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderMonitoringService
{
    private static async Task<List<ProviderMonitoringSource>> SourcesAsync(AtoCopilotContext db, ProviderOffering offering, CancellationToken ct)
    {
        var sources = new List<ProviderMonitoringSource>();
        var decisions = await db.Set<ProviderAuthorizationRecord>().Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id)
            .Select(x => x.Id).ToListAsync(ct);
        foreach (var id in decisions)
        {
            sources.Add(await SourceAsync(db, offering, "AuthorizationExpiry", id, ct));
            sources.Add(await SourceAsync(db, offering, "AuthorizationWithdrawal", id, ct));
        }
        foreach (var id in await db.Set<ProviderFindingEvidence>().Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id).Select(x => x.Id).ToListAsync(ct))
            sources.Add(await SourceAsync(db, offering, "EvidenceFreshness", id, ct));
        foreach (var id in await db.Set<ProviderCatalogContextSnapshot>().Where(x => x.ProviderId == offering.ProviderId &&
            x.OfferingId == offering.Id && x.ReleaseId != null && x.CapabilityId != null).Select(x => x.CapabilityId!.Value).Distinct().ToListAsync(ct))
            sources.Add(await SourceAsync(db, offering, "PublishedReleaseChange", id, ct));
        return sources;
    }

    private static async Task<ProviderMonitoringSource> SourceAsync(AtoCopilotContext db, ProviderOffering offering,
        string signal, Guid sourceId, CancellationToken ct)
    {
        var missing = new ProviderMonitoringSource(sourceId, signal, "Source unavailable", "Missing", "", Field(signal), null, null, "{}");
        if (signal is "AuthorizationExpiry" or "AuthorizationWithdrawal")
        {
            var record = await db.Set<ProviderAuthorizationRecord>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == sourceId && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct);
            if (record is null) return missing;
            var revision = await db.Set<ProviderAuthorizationRevision>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == record.CurrentRevisionId && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct);
            if (revision is null) return missing;
            var body = Read<CreateProviderDecisionRequest>(revision.SnapshotJson);
            var events = await db.Set<ProviderAuthorizationLifecycleEvent>().AsNoTracking().Where(x =>
                x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id && x.RecordId == record.Id &&
                x.AuthorizationRevisionId == revision.Id).ToListAsync(ct);
            var withdrawn = events.Any(x => x.Kind == "Withdrawn" && Date(x.EffectiveOn) <= DateOnly.FromDateTime(DateTime.UtcNow));
            var snapshot = Json(new { record.Id, RevisionId = revision.Id, revision.SnapshotHash, revision.MetadataReviewState,
                revision.RecordedAt, body.Reference, body.ExpiresOn, withdrawn });
            var expiry = Date(body.ExpiresOn);
            var health = revision.MetadataReviewState != "Recorded" ? "Unreviewed"
                : signal == "AuthorizationExpiry" && expiry == null ? "Missing" : "Available";
            var value = signal == "AuthorizationExpiry" ? expiry.HasValue
                ? (expiry.Value.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber).ToString(CultureInfo.InvariantCulture) : null
                : withdrawn.ToString().ToLowerInvariant();
            return new(sourceId, signal, body.Reference, health, Hash(snapshot), Field(signal), value, revision.RecordedAt, snapshot);
        }
        if (signal == "EvidenceFreshness")
        {
            var evidence = await db.Set<ProviderFindingEvidence>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == sourceId && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct);
            if (evidence is null) return missing;
            var snapshot = Json(new { evidence.Id, evidence.FileName, evidence.Revision, evidence.Sha256, evidence.State, evidence.CreatedAt });
            var age = (DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - DateOnly.FromDateTime(evidence.CreatedAt.UtcDateTime).DayNumber).ToString(CultureInfo.InvariantCulture);
            return new(sourceId, signal, evidence.FileName, evidence.State == "Reviewed" ? "Available" : "Unreviewed",
                Hash(snapshot), Field(signal), age, evidence.CreatedAt, snapshot);
        }
        var releases = await (from context in db.Set<ProviderCatalogContextSnapshot>().AsNoTracking()
            join release in db.Set<ProviderCapabilityRelease>() on context.ReleaseId equals release.Id
            join review in db.Set<ProviderAuthorizationImpactReview>() on context.ImpactReviewId equals review.Id
            where context.ProviderId == offering.ProviderId && context.OfferingId == offering.Id &&
                context.CapabilityId == sourceId && release.CapabilityId == sourceId &&
                review.ProviderId == offering.ProviderId && review.OfferingId == offering.Id &&
                review.Disposition == "AcceptForPublication" && review.ReviewedAt != null
            orderby release.Revision descending
            select new { Release = release, context.SnapshotHash }).ToListAsync(ct);
        if (releases.Count == 0) return missing;
        var latest = releases[0];
        var capability = await db.CspInheritedCapabilities.Where(x => x.Id == sourceId).Select(x => x.Name).SingleOrDefaultAsync(ct);
        var releaseSnapshot = Json(new { latest.Release.Id, latest.Release.CapabilityId, latest.Release.Revision,
            ReleaseHash = latest.Release.SnapshotHash });
        return new(sourceId, signal, capability ?? "Published capability", "Available", Hash(releaseSnapshot), Field(signal), "false",
            latest.Release.PublishedAt, releaseSnapshot);
    }
}

using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderAuthorizationService
{
    public Task<ProviderDecisionResponse> CreateDecisionAsync(Guid id, CreateProviderDecisionRequest request,
        string key, string actor, CancellationToken ct) =>
        store.WriteAsync(id, $"DecisionCreated:{id:D}", key, request, actor, async (db, provider, offering) =>
        {
            Expected(offering!, request.ExpectedOfferingRevision);
            await ValidateDecisionAsync(db, offering!, request, ct);
            var record = new ProviderAuthorizationRecord { ProviderId = provider, OfferingId = id, CreatedBy = actor };
            var revision = NewDecisionRevision(record, request, actor);
            record.CurrentRevisionId = revision.Id;
            db.Set<ProviderAuthorizationRecord>().Add(record);
            db.Set<ProviderAuthorizationRevision>().Add(revision);
            offering!.Revision++;
            await InvalidateAsync(db, offering, actor, "Unconfirmed decision metadata was proposed", ct);
            return Decision(revision, []);
        }, ct);

    public Task<ProviderDecisionResponse> UpdateDecisionAsync(Guid id, Guid recordId,
        UpdateProviderDecisionRequest request, string actor, CancellationToken ct) =>
        store.WriteAsync(id, $"DecisionDrafted:{recordId:D}", null, request, actor, async (db, _, offering) =>
        {
            var record = await RecordAsync(db, offering!, recordId, ct);
            Expected(record, request.ExpectedRevision);
            var body = new CreateProviderDecisionRequest(offering!.Revision, request.BoundaryRevisionId,
                request.SourceCandidateRefs, request.RecordKind, request.Reference, request.IssuingAuthority,
                request.DecisionAsStated, request.IssuedOn, request.EffectiveOn, request.ExpiresOn, request.ExpiryBasis,
                request.ScopeStatement, request.Conditions, request.Citations);
            await ValidateDecisionAsync(db, offering, body, ct);
            record.Revision++;
            var revision = NewDecisionRevision(record, body, actor);
            record.CurrentRevisionId = revision.Id;
            db.Set<ProviderAuthorizationRevision>().Add(revision);
            offering.Revision++;
            await InvalidateAsync(db, offering, actor, "A successor decision draft requires review", ct);
            return Decision(revision, []);
        }, ct);

    public Task<ProviderDecisionResponse> RecordDecisionAsync(Guid id, Guid recordId,
        RecordProviderDecisionRequest request, string actor, CancellationToken ct) =>
        store.WriteAsync(id, $"DecisionRecorded:{recordId:D}", null, request, actor, async (db, _, offering) =>
        {
            var record = await RecordAsync(db, offering!, recordId, ct);
            Expected(record, request.ExpectedRevision);
            var revision = await db.Set<ProviderAuthorizationRevision>().SingleAsync(x => x.Id == record.CurrentRevisionId
                && x.ProviderId == offering!.ProviderId && x.OfferingId == id, ct);
            if (revision.Id != request.RevisionId || revision.SnapshotHash != request.SnapshotHash)
                throw new DbUpdateConcurrencyException("Only the exact current decision draft can be recorded.");
            var body = Read<CreateProviderDecisionRequest>(revision.SnapshotJson);
            Text(request.Rationale, "review rationale", 2000);
            Text(body.IssuingAuthority, "issuing authority", 2000);
            Text(body.DecisionAsStated, "source decision", 2000);
            await ValidateDecisionAsync(db, offering!, body, ct);
            await store.CitationsAsync(db, offering!.ProviderId, body.Citations, ct, required: true);
            if (revision.MetadataReviewState != "Unconfirmed")
                throw new DbUpdateConcurrencyException("This decision revision has already been reviewed.");
            revision.MetadataReviewState = "Recorded";
            revision.RecordedAt = DateTimeOffset.UtcNow;
            revision.RecordedBy = actor;
            revision.ReviewRationale = request.Rationale.Trim();
            record.Revision++;
            offering.Revision++;
            await InvalidateAsync(db, offering, actor, "Recorded external decision requires applicability review", ct);
            return Decision(revision, []) with { Revision = record.Revision };
        }, ct);

    public async Task<ProviderDecisionResponse> DecisionAsync(Guid id, Guid recordId, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, id, ct);
        var record = await RecordAsync(db, offering, recordId, ct);
        var revision = await db.Set<ProviderAuthorizationRevision>().AsNoTracking()
            .SingleAsync(x => x.Id == record.CurrentRevisionId && x.ProviderId == offering.ProviderId && x.OfferingId == id, ct);
        var result = Decision(revision, await EventsAsync(db, offering, recordId, ct)) with { Revision = record.Revision };
        return result with { ImpactReviewRequired = await RequiresImpactAsync(db, offering, result, ct) };
    }

    public async Task<Ato.Copilot.Core.Interfaces.Workspaces.PagedResult<ProviderDecisionResponse>> DecisionsAsync(Guid id, int page, int pageSize,
        Guid? recordId, CancellationToken ct, string? recordKind = null)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, id, ct);
        if (recordKind is not (null or "ProviderDecision" or "InheritedMicrosoftReference"))
            throw new ArgumentException("recordKind must be ProviderDecision or InheritedMicrosoftReference.");
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new ArgumentException("Use page >=1 and pageSize between1 and100.");
        var query = db.Set<ProviderAuthorizationRevision>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == id);
        if (recordId.HasValue)
        {
            await RecordAsync(db, offering, recordId.Value, ct);
            query = query.Where(x => x.RecordId == recordId.Value);
        }
        else query = query.Where(x => db.Set<ProviderAuthorizationRecord>().Any(r =>
            r.Id == x.RecordId && r.ProviderId == offering.ProviderId && r.OfferingId == id && r.CurrentRevisionId == x.Id));
        if (recordKind is not null)
        {
            // Kind is retained inside immutable JSON; filter before paging without provider-specific SQL JSON functions.
            var revisions = await query.Select(x => new { x.Id, x.SnapshotJson }).ToListAsync(ct);
            var matchingIds = revisions.Where(x => Read<CreateProviderDecisionRequest>(x.SnapshotJson).RecordKind == recordKind)
                .Select(x => x.Id).ToArray();
            query = query.Where(x => matchingIds.Contains(x.Id));
        }
        var events = await db.Set<ProviderAuthorizationLifecycleEvent>().Where(x =>
            x.ProviderId == offering.ProviderId && x.OfferingId == id).ToListAsync(ct);
        var result = await PageAsync(query.OrderByDescending(x => x.Revision).ThenBy(x => x.Id),
            page, pageSize, x => Decision(x, events.Where(e => e.RecordId == x.RecordId).ToArray()), ct);
        if (recordId.HasValue) return result;
        var ids = result.Items.Select(x => x.RecordId).ToArray();
        var fences = await db.Set<ProviderAuthorizationRecord>().Where(x => x.ProviderId == offering.ProviderId
            && x.OfferingId == id && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Revision, ct);
        var items = new List<ProviderDecisionResponse>();
        foreach (var item in result.Items)
            items.Add(item with { Revision = fences[item.RecordId], ImpactReviewRequired = await RequiresImpactAsync(db, offering, item, ct) });
        return result with { Items = items };
    }

    public Task<ProviderDecisionLifecycleResponse> LifecycleAsync(Guid id, Guid recordId,
        ProviderDecisionLifecycleRequest request, string key, string actor, CancellationToken ct) =>
        store.WriteAsync(id, $"DecisionLifecycle:{recordId:D}", key, request, actor, async (db, provider, offering) =>
        {
            var record = await RecordAsync(db, offering!, recordId, ct);
            Expected(record, request.ExpectedRevision);
            var revision = await db.Set<ProviderAuthorizationRevision>().SingleAsync(x => x.Id == record.CurrentRevisionId
                && x.ProviderId == provider && x.OfferingId == id, ct);
            if (revision.MetadataReviewState != "Recorded")
                throw new DbUpdateConcurrencyException("Lifecycle events require a recorded external decision.");
            if (request.Kind is not ("Withdrawn" or "Superseded")) throw new ArgumentException("Use Withdrawn or Superseded.");
            if (!Date(request.EffectiveOn).HasValue) throw new ArgumentException("A source-stated lifecycle effective date is required.");
            Text(request.Rationale, "lifecycle rationale", 2000);
            await store.CitationsAsync(db, provider, request.Citations, ct, required: true);
            if (request.Kind == "Superseded")
            {
                if (!request.ReplacementRevisionId.HasValue || request.ReplacementRevisionId == revision.Id)
                    throw new ArgumentException("Supersession requires a different recorded revision in this offering.");
                var replacement = await db.Set<ProviderAuthorizationRevision>().SingleOrDefaultAsync(x =>
                    x.Id == request.ReplacementRevisionId && x.ProviderId == provider && x.OfferingId == id, ct)
                    ?? throw new KeyNotFoundException("Replacement revision was not found in this offering.");
                var replacementRecord = await RecordAsync(db, offering!, replacement.RecordId, ct);
                if (replacementRecord.CurrentRevisionId != replacement.Id)
                    throw new DbUpdateConcurrencyException("Select the replacement record's current immutable revision.");
                var sourceKind = Read<CreateProviderDecisionRequest>(revision.SnapshotJson).RecordKind;
                var blocker = ProviderDecisionEligibility.Evaluate(replacement,
                    await EventsAsync(db, offering!, replacement.RecordId, ct), sourceKind);
                if (blocker is not null)
                    throw new ProviderPublicationConflictException(blocker.Code, blocker.Message);
                await store.CitationsAsync(db, provider, Read<CreateProviderDecisionRequest>(replacement.SnapshotJson).Citations, ct, required: true);
            }
            else if (request.ReplacementRevisionId.HasValue) throw new ArgumentException("Withdrawal has no replacement decision.");
            var item = new ProviderAuthorizationLifecycleEvent
            {
                ProviderId = provider, OfferingId = id, RecordId = recordId, AuthorizationRevisionId = revision.Id,
                Kind = request.Kind, EffectiveOn = request.EffectiveOn, ReplacementRevisionId = request.ReplacementRevisionId,
                Rationale = request.Rationale.Trim(), CitationsJson = Json(request.Citations), CreatedBy = actor
            };
            db.Set<ProviderAuthorizationLifecycleEvent>().Add(item);
            record.Revision++;
            offering!.Revision++;
            await InvalidateAsync(db, offering, actor, $"External decision {request.Kind}", ct);
            var impact = db.Set<ProviderAuthorizationImpactReview>().Local.Single(x => db.Entry(x).State == EntityState.Added);
            var events = await EventsAsync(db, offering, recordId, ct);
            events.Add(item);
            return new ProviderDecisionLifecycleResponse(item.Id, Decision(revision, events) with { Revision = record.Revision }, impact.Id);
        }, ct);

    private async Task ValidateDecisionAsync(AtoCopilotContext db, ProviderOffering offering,
        CreateProviderDecisionRequest request, CancellationToken ct)
    {
        await RequireBoundaryAsync(db, offering, request.BoundaryRevisionId, ct);
        if (request.RecordKind is not ("ProviderDecision" or "InheritedMicrosoftReference"))
            throw new ArgumentException("Record kind must distinguish provider decisions and inherited Microsoft references.");
        Text(request.Reference, "reference", 2000); Text(request.ScopeStatement, "scope", 8000);
        Text(request.IssuingAuthority, "authority", 2000, false); Text(request.DecisionAsStated, "decision", 2000, false);
        var issued = Date(request.IssuedOn); var effective = Date(request.EffectiveOn); var expires = Date(request.ExpiresOn);
        if (request.ExpiryBasis is not ("DateStated" or "NoExpiryStated" or "NotRecorded")
            || (request.ExpiryBasis == "DateStated") != expires.HasValue
            || (expires.HasValue && (effective ?? issued) is { } start && expires < start))
            throw new ArgumentException("The source dates and expiry basis are inconsistent.");
        Bounded(request.Conditions, "conditions"); Bounded(request.SourceCandidateRefs, "source candidates");
        foreach (var condition in request.Conditions) Text(condition, "condition", 2000);
        await store.CitationsAsync(db, offering.ProviderId, request.Citations, ct);
        foreach (var candidate in request.SourceCandidateRefs)
        {
            await store.CandidateAsync(db, offering.ProviderId, candidate, "AuthorizationDecisionClaim", ct);
            if (!await db.Set<ProviderPackageVersion>().AnyAsync(x => x.PackageId == candidate.PackageId
                && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct))
                throw new ArgumentException("Source claims must belong to an associated package of this offering.");
        }
    }

    private static ProviderAuthorizationRevision NewDecisionRevision(ProviderAuthorizationRecord record,
        CreateProviderDecisionRequest request, string actor) => new()
    {
        ProviderId = record.ProviderId, OfferingId = record.OfferingId, RecordId = record.Id, Revision = record.Revision,
        BoundaryRevisionId = request.BoundaryRevisionId, SnapshotJson = Json(request),
        SnapshotHash = Hash(Json(request)), CreatedBy = actor
    };

    private static async Task<ProviderAuthorizationRecord> RecordAsync(AtoCopilotContext db, ProviderOffering offering,
        Guid id, CancellationToken ct) => await db.Set<ProviderAuthorizationRecord>().SingleOrDefaultAsync(x =>
            x.Id == id && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct)
        ?? throw new KeyNotFoundException("External decision was not found in this offering.");

    private static Task<List<ProviderAuthorizationLifecycleEvent>> EventsAsync(AtoCopilotContext db,
        ProviderOffering offering, Guid record, CancellationToken ct) =>
        db.Set<ProviderAuthorizationLifecycleEvent>().Where(x => x.ProviderId == offering.ProviderId
            && x.OfferingId == offering.Id && x.RecordId == record).ToListAsync(ct);

    public static string Standing(ProviderAuthorizationRevision row, IEnumerable<ProviderAuthorizationLifecycleEvent> events)
    {
        if (row.MetadataReviewState != "Recorded") return "Undetermined";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var latest = events.Where(x => x.AuthorizationRevisionId == row.Id && Date(x.EffectiveOn) <= today)
            .OrderByDescending(x => x.EffectiveOn, StringComparer.Ordinal).ThenByDescending(x => x.CreatedAt).FirstOrDefault();
        if (latest is not null) return latest.Kind;
        var source = Read<CreateProviderDecisionRequest>(row.SnapshotJson);
        if (Date(source.EffectiveOn) > today) return "NotYetEffective";
        if (Date(source.ExpiresOn) < today) return "Expired";
        return source.ExpiryBasis == "NotRecorded" ? "Undetermined" : "CurrentAsRecorded";
    }

    private static async Task<bool> RequiresImpactAsync(AtoCopilotContext db, ProviderOffering offering,
        ProviderDecisionResponse decision, CancellationToken ct)
    {
        if (decision.CurrentStanding != "CurrentAsRecorded") return true;
        var accepted = await db.Set<ProviderAuthorizationImpactReview>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id
                && x.Disposition == "AcceptForPublication" && x.InvalidatedAt == null)
            .Select(x => new { x.ContextJson, x.ExpiresAt }).ToListAsync(ct);
        return !accepted.Where(x => x.ExpiresAt > DateTimeOffset.UtcNow)
            .Select(x => Read<ProviderPublicationContextMaterial>(x.ContextJson))
            .Any(x => x.OfferingRevision == offering.Revision
                && x.AuthorizationRevisions.Any(r => r.RecordId == decision.RecordId
                    && r.RevisionId == decision.RevisionId && r.SnapshotHash == decision.SnapshotHash));
    }

    private static ProviderDecisionResponse Decision(ProviderAuthorizationRevision row, IEnumerable<ProviderAuthorizationLifecycleEvent> events)
    {
        var body = Read<CreateProviderDecisionRequest>(row.SnapshotJson);
        var standing = Standing(row, events);
        return new(row.RecordId, row.OfferingId, row.Id, row.Revision, row.SnapshotHash, row.MetadataReviewState,
            standing, row.RecordedBy, row.RecordedAt, true, body.BoundaryRevisionId,
            body.SourceCandidateRefs, body.RecordKind, body.Reference, body.IssuingAuthority, body.DecisionAsStated,
            body.IssuedOn, body.EffectiveOn, body.ExpiresOn, body.ExpiryBasis, body.ScopeStatement, body.Conditions, body.Citations);
    }
}

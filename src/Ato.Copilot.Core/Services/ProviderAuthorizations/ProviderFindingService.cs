using System.Net.Http.Headers;
using System.Security.Cryptography;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

/// <summary>Offering-owned remediation with retained evidence and explicit, revision-fenced review.</summary>
public sealed class ProviderFindingService(ProviderAuthorizationStore store, IFileStorageProvider storage) : IProviderFindingService
{
    public const int MaximumEvidenceBytes = 10 * 1024 * 1024;

    public async Task<PagedResult<ProviderFindingResponse>> ListFindingsAsync(Guid offeringId, int page, int pageSize, CancellationToken ct = default)
    {
        store.Authorize();
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        return await PageAsync(Findings(db, offering).AsNoTracking().OrderBy(x => x.Id), page, pageSize, FindingResponse, ct);
    }

    public Task<ProviderFindingResponse> CreateFindingAsync(Guid offeringId, CreateProviderFindingRequest request,
        string key, string actor, CancellationToken ct = default) =>
        WriteAsync(offeringId, "CreateFinding", key, request, actor, async (db, offering) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            Expected(offering, request.ExpectedOfferingRevision);
            Bounded(request.ControlIds, "controlIds");
            var controls = request.ControlIds.Select(x => Text(x, "controlId", 100)).ToArray();
            await store.CandidateAsync(db, offering.ProviderId, request.SourceCandidateRef, "AssessmentFinding", ct);
            await store.CitationsAsync(db, offering.ProviderId, request.Citations, ct);
            var row = new ProviderFinding
            {
                ProviderId = offering.ProviderId, OfferingId = offering.Id, CreatedBy = actor,
                Title = Text(request.Title, "title", 256), Observation = Text(request.Observation, "observation", 8000),
                SeverityAsStated = OptionalText(request.SeverityAsStated, "severityAsStated", 2000),
                ControlIdsJson = Json(controls), CitationsJson = Json(request.Citations),
                SourceCandidateRefJson = request.SourceCandidateRef is null ? null : Json(request.SourceCandidateRef),
                WorkflowState = "Open"
            };
            db.Add(row);
            Audit(db, offering, row.Id, "FindingCreated", actor, FindingResponse(row));
            return FindingResponse(row);
        }, ct, requireKey: true);

    public async Task<PagedResult<ProviderPoamResponse>> ListPoamAsync(Guid offeringId, int page, int pageSize, CancellationToken ct = default)
    {
        store.Authorize();
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        return await PageAsync(PoamItems(db, offering).AsNoTracking().OrderBy(x => x.Id), page, pageSize, PoamResponse, ct);
    }

    public Task<ProviderPoamResponse> CreatePoamAsync(Guid offeringId, CreateProviderPoamRequest request,
        string key, string actor, CancellationToken ct = default) =>
        WriteAsync(offeringId, "CreatePoam", key, request, actor, async (db, offering) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            Expected(offering, request.ExpectedOfferingRevision);
            ValidateIds(request.FindingIds, "findingIds");
            if (await Findings(db, offering).CountAsync(x => request.FindingIds.Contains(x.Id), ct) != request.FindingIds.Count)
                throw new KeyNotFoundException("A linked finding was not found in this offering.");
            await store.CandidateAsync(db, offering.ProviderId, request.SourceCandidateRef, "PoamItem", ct);
            await store.CitationsAsync(db, offering.ProviderId, request.Citations, ct);
            var row = new ProviderPoamItem
            {
                ProviderId = offering.ProviderId, OfferingId = offering.Id, CreatedBy = actor,
                Title = Text(request.Title, "title", 256), FindingIdsJson = Json(request.FindingIds),
                CorrectiveAction = Text(request.CorrectiveAction, "correctiveAction", 8000),
                OwnerAsStated = OptionalText(request.OwnerAsStated, "ownerAsStated", 2000),
                MilestonesJson = Json(ValidateMilestones(request.Milestones)), CitationsJson = Json(request.Citations),
                SourceCandidateRefJson = request.SourceCandidateRef is null ? null : Json(request.SourceCandidateRef),
                WorkflowState = "Open"
            };
            db.Add(row);
            Audit(db, offering, row.Id, "PoamCreated", actor, PoamResponse(row));
            return PoamResponse(row);
        }, ct, requireKey: true);

    public Task<ProviderPoamResponse> UpdatePoamAsync(Guid offeringId, Guid poamId, UpdateProviderPoamRequest request,
        string actor, CancellationToken ct = default) =>
        WriteAsync(offeringId, "UpdatePoam", null, new { poamId, request }, actor, async (db, offering) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var row = await PoamItems(db, offering).SingleOrDefaultAsync(x => x.Id == poamId, ct)
                ?? throw new KeyNotFoundException("POA&M item was not found in this offering.");
            Expected(row, request.ExpectedRevision);
            if (request.WorkflowState is not ("Open" or "InProgress" or "ReadyForReview"))
                throw new ArgumentException("POA&M updates allow only Open, InProgress or ReadyForReview, not closure.");
            var previous = PoamResponse(row);
            row.CorrectiveAction = Text(request.CorrectiveAction, "correctiveAction", 8000);
            row.OwnerAsStated = OptionalText(request.OwnerAsStated, "ownerAsStated", 2000);
            row.MilestonesJson = Json(ValidateMilestones(request.Milestones));
            row.WorkflowState = request.WorkflowState;
            row.Revision++;
            var history = Read<List<PoamHistoryEntry>>(row.HistoryJson);
            history.Add(new(previous, actor, DateTimeOffset.UtcNow));
            row.HistoryJson = Json(history);
            Audit(db, offering, row.Id, "PoamUpdated", actor, new { Previous = previous, Current = PoamResponse(row) });
            return PoamResponse(row);
        }, ct);

    public async Task<ProviderFindingEvidenceResponse> SubmitEvidenceAsync(Guid offeringId, Guid findingId,
        SubmitProviderFindingEvidenceRequest request, string key, string actor, CancellationToken ct = default)
    {
        store.Authorize();
        ArgumentNullException.ThrowIfNull(request);
        Text(key, "Idempotency-Key", 100);
        Text(actor, "actor", 254);
        await using (var db = await store.Factory.CreateDbContextAsync(ct))
            await FindingAsync(db, await store.OfferingAsync(db, offeringId, ct), findingId, ct);
        var fileName = FileName(request.FileName);
        var mediaType = MediaType(request.MediaType);
        var description = Text(request.Description, "description", 8000);
        using var content = new MemoryStream();
        await ReadBoundedAsync(request.Content, content, ct);
        var hash = Convert.ToHexString(SHA256.HashData(content.GetBuffer().AsSpan(0, (int)content.Length)));
        var intent = new { findingId, request.ExpectedFindingRevision, request.Description, request.FileName,
            request.MediaType, ByteLength = content.Length, Sha256 = hash };
        return await WriteAsync(offeringId, "SubmitFindingEvidence", key, intent, actor, async (db, offering) =>
        {
            var finding = await FindingAsync(db, offering, findingId, ct);
            Expected(finding, request.ExpectedFindingRevision);
            var row = new ProviderFindingEvidence
            {
                ProviderId = offering.ProviderId, OfferingId = offering.Id, FindingId = finding.Id, CreatedBy = actor,
                FileName = fileName, MediaType = mediaType, ByteLength = content.Length, Sha256 = hash,
                Description = description, State = "PendingReview",
                StorageKey = $"provider-findings/{offering.ProviderId:D}/{offering.Id:D}/{finding.Id:D}/{Hash(key)}/{hash}"
            };
            content.Position = 0;
            await storage.SaveAsync(row.StorageKey, content, row.MediaType, ct);
            db.Add(row);
            finding.Revision++;
            Audit(db, offering, row.Id, "FindingEvidenceSubmitted", actor,
                new { findingId, FindingRevision = finding.Revision, row.Sha256, row.ByteLength, row.State });
            return EvidenceResponse(row, finding.Revision, null);
        }, ct, requireKey: true);
    }

    public async Task<PagedResult<ProviderFindingEvidenceResponse>> ListEvidenceAsync(Guid offeringId, Guid findingId,
        int page, int pageSize, CancellationToken ct = default)
    {
        store.Authorize();
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        var finding = await FindingAsync(db, offering, findingId, ct);
        var rows = await PageAsync(Evidence(db, offering, findingId).AsNoTracking().OrderBy(x => x.Id),
            page, pageSize, x => x, ct);
        var items = new List<ProviderFindingEvidenceResponse>();
        foreach (var row in rows.Items)
        {
            var reference = Json(row.Id);
            var review = await db.Set<ProviderFindingReview>().AsNoTracking()
                .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id
                    && x.FindingId == findingId && x.EvidenceIdsJson.Contains(reference))
                .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
            items.Add(EvidenceResponse(row, finding.Revision, review is null ? null : ReviewResponse(review)));
        }
        return new(items, rows.Page, rows.PageSize, rows.Total);
    }

    public async Task<ProviderFindingContent> ContentAsync(Guid offeringId, Guid findingId, Guid evidenceId,
        string actor, CancellationToken ct = default)
    {
        store.Authorize();
        Text(actor, "actor", 254);
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        await FindingAsync(db, offering, findingId, ct);
        var evidence = await Evidence(db, offering, findingId).SingleOrDefaultAsync(x => x.Id == evidenceId, ct)
            ?? throw new KeyNotFoundException("Evidence was not found for this offering and finding.");
        Audit(db, offering, evidenceId, "FindingEvidenceAccessed", actor, new { findingId, evidence.Sha256 });
        await db.SaveChangesAsync(ct);
        var content = await storage.GetAsync(evidence.StorageKey, ct)
            ?? throw new IOException("Retained finding evidence is unavailable.");
        return new(content, FileName(evidence.FileName), MediaType(evidence.MediaType));
    }

    public Task<ProviderFindingReviewResponse> ReviewAsync(Guid offeringId, Guid findingId,
        ReviewProviderFindingRequest request, string actor, CancellationToken ct = default) =>
        WriteAsync(offeringId, "ReviewFinding", null, new { findingId, request }, actor, async (db, offering) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var finding = await FindingAsync(db, offering, findingId, ct);
            Expected(finding, request.ExpectedRevision);
            ValidateIds(request.EvidenceIds, "evidenceIds");
            if (request.Disposition is not ("KeepOpen" or "AcceptClosure"))
                throw new ArgumentException("Use KeepOpen or AcceptClosure for an explicit finding review.");
            if (request.Disposition == "AcceptClosure" && request.EvidenceIds.Count == 0)
                throw new DbUpdateConcurrencyException("Explicit closure requires retained evidence for this finding.");
            var rationale = Text(request.Rationale, "rationale", 8000);
            var evidence = await Evidence(db, offering, findingId).Where(x => request.EvidenceIds.Contains(x.Id)).ToListAsync(ct);
            if (evidence.Count != request.EvidenceIds.Count)
                throw new KeyNotFoundException("A reviewed artifact was not found for this offering and finding.");
            foreach (var artifact in evidence)
            {
                if (!await storage.ExistsAsync(artifact.StorageKey, ct))
                    throw new IOException("Evidence must remain retained before it can be reviewed.");
                artifact.State = "Reviewed";
                artifact.Revision++;
            }
            var previousState = finding.WorkflowState;
            finding.WorkflowState = request.Disposition == "AcceptClosure" ? "Closed" : "Open";
            finding.Revision++;
            var review = new ProviderFindingReview
            {
                ProviderId = offering.ProviderId, OfferingId = offering.Id, FindingId = findingId,
                Revision = finding.Revision, CreatedBy = actor, EvidenceIdsJson = Json(request.EvidenceIds),
                Disposition = request.Disposition, Rationale = rationale
            };
            db.Add(review);
            Audit(db, offering, finding.Id, "FindingReviewed", actor,
                new { PreviousState = previousState, Review = ReviewResponse(review) });
            return ReviewResponse(review);
        }, ct);

    private async Task<T> WriteAsync<T>(Guid offeringId, string operation, string? key, object intent, string actor,
        Func<AtoCopilotContext, ProviderOffering, Task<T>> change, CancellationToken ct, bool requireKey = false)
    {
        store.Authorize();
        if (requireKey) Text(key, "Idempotency-Key", 100);
        await using (var db = await store.Factory.CreateDbContextAsync(ct))
            await store.OfferingAsync(db, offeringId, ct);
        return await store.WriteAsync(offeringId, $"{operation}:{offeringId:D}", key, intent, actor,
            (db, _, offering) => change(db, offering ?? throw new KeyNotFoundException("Offering was not found.")), ct);
    }

    private static IQueryable<ProviderFinding> Findings(AtoCopilotContext db, ProviderOffering offering) =>
        db.Set<ProviderFinding>().Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id);

    private static IQueryable<ProviderPoamItem> PoamItems(AtoCopilotContext db, ProviderOffering offering) =>
        db.Set<ProviderPoamItem>().Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id);

    private static IQueryable<ProviderFindingEvidence> Evidence(AtoCopilotContext db, ProviderOffering offering, Guid findingId) =>
        db.Set<ProviderFindingEvidence>().Where(x => x.ProviderId == offering.ProviderId
            && x.OfferingId == offering.Id && x.FindingId == findingId);

    private static async Task<ProviderFinding> FindingAsync(AtoCopilotContext db, ProviderOffering offering,
        Guid findingId, CancellationToken ct) =>
        await Findings(db, offering).SingleOrDefaultAsync(x => x.Id == findingId, ct)
            ?? throw new KeyNotFoundException("Finding was not found in this offering.");

    private static ProviderFindingResponse FindingResponse(ProviderFinding row) =>
        new(row.Id, row.OfferingId, row.Revision, row.Title, row.Observation, row.SeverityAsStated,
            row.WorkflowState, Source(row.SourceCandidateRefJson), Read<string[]>(row.ControlIdsJson),
            Read<ProviderCitation[]>(row.CitationsJson), row.CreatedAt);

    private static ProviderPoamResponse PoamResponse(ProviderPoamItem row) =>
        new(row.Id, row.OfferingId, row.Revision, row.Title, Read<Guid[]>(row.FindingIdsJson), row.CorrectiveAction,
            row.OwnerAsStated, Read<ProviderPoamMilestone[]>(row.MilestonesJson), row.WorkflowState,
            Source(row.SourceCandidateRefJson), Read<ProviderCitation[]>(row.CitationsJson), row.CreatedAt);

    private static ProviderFindingEvidenceResponse EvidenceResponse(ProviderFindingEvidence row, long findingRevision,
        ProviderFindingReviewResponse? review) =>
        new(row.Id, row.FindingId, row.OfferingId, findingRevision, row.FileName, row.MediaType,
            row.ByteLength, row.Sha256, row.Description, row.State, row.CreatedAt, review);

    private static ProviderFindingReviewResponse ReviewResponse(ProviderFindingReview row) =>
        new(row.Id, row.FindingId, row.OfferingId, row.Revision, Read<Guid[]>(row.EvidenceIdsJson),
            row.Disposition, row.Rationale, row.CreatedBy, row.CreatedAt,
            row.Disposition == "AcceptClosure" ? "Closed" : "Open");

    private static ProviderSourceCandidateRef? Source(string? value) =>
        value is null ? null : Read<ProviderSourceCandidateRef>(value);

    private static string? OptionalText(string? value, string field, int max) =>
        value is null ? null : Text(value, field, max, required: false);

    private static void ValidateIds(IReadOnlyList<Guid> values, string field)
    {
        Bounded(values, field);
        if (values.Any(x => x == Guid.Empty) || values.Distinct().Count() != values.Count)
            throw new ArgumentException($"{field} must contain unique non-empty identifiers.");
    }

    private static ProviderPoamMilestone[] ValidateMilestones(IReadOnlyList<ProviderPoamMilestone> values)
    {
        Bounded(values, "milestones");
        return values.Select(x =>
        {
            Date(x.DueDate);
            return new ProviderPoamMilestone(Text(x.Description, "milestone description", 2000), x.DueDate);
        }).ToArray();
    }

    private static string FileName(string value)
    {
        var name = Text(value, "fileName", 512);
        if (name.Any(char.IsControl)) throw new ArgumentException("File names cannot contain control characters.");
        name = Path.GetFileName(name.Replace('\\', '/'));
        if (name is "" or "." or "..") throw new ArgumentException("Supply a valid evidence file name.");
        return name;
    }

    private static string MediaType(string value)
    {
        var mediaType = Text(value, "mediaType", 256);
        if (mediaType.Any(char.IsControl) || !MediaTypeHeaderValue.TryParse(mediaType, out _))
            throw new ArgumentException("Supply a valid evidence media type.");
        return mediaType;
    }

    private static async Task ReadBoundedAsync(Stream content, MemoryStream buffer, CancellationToken ct)
    {
        if (content is null || !content.CanRead) throw new ArgumentException("Supply readable evidence content.");
        var chunk = new byte[81920];
        int count;
        while ((count = await content.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + count > MaximumEvidenceBytes)
                throw new ArgumentException("Evidence must not exceed 10 MiB.");
            await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
        }
        if (buffer.Length == 0) throw new ArgumentException("Evidence must not be empty.");
    }

    private sealed record PoamHistoryEntry(ProviderPoamResponse Previous, string ChangedBy, DateTimeOffset ChangedAt);
}

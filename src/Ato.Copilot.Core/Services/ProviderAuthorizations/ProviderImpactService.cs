using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderImpactService(ProviderAuthorizationStore store) : IProviderImpactService
{
    public Task<ProviderImpactPreviewResponse> PreviewAsync(Guid offeringId, CreateProviderImpactPreviewRequest request,
        string key, string actor, CancellationToken ct) =>
        store.WriteAsync<ProviderImpactPreviewResponse>(offeringId, $"ImpactPreview:{offeringId:D}",
            Text(key, "Idempotency-Key", 100), request, actor, async (db, _, offering) =>
        {
            Expected(offering!, request.ExpectedOfferingRevision);
            Validate(request);
            var material = await BuildAsync(db, offering!, request, ct);
            var input = JsonSerializer.SerializeToNode(request, JsonOptions)!.AsObject();
            input["dependencyGraph"] = JsonNode.Parse(material.DependencyGraphJson);
            var row = new ProviderAuthorizationImpactReview
            {
                ProviderId = offering!.ProviderId, OfferingId = offeringId, CreatedBy = actor,
                InputJson = input.ToJsonString(JsonOptions), ContextJson = Json(material.Context),
                ContextSnapshotHash = material.ContextHash, PreviewHash = material.PreviewHash,
                TargetsJson = Json(material.Targets), BlockersJson = Json(material.Blockers)
            };
            db.Add(row);
            return new ProviderImpactPreviewResponse(row.Id, row.Revision, row.PreviewId, row.PreviewHash, row.ContextSnapshotHash, row.ExpiresAt,
                material.Blockers, new(material.Targets.Count(x => x.Kind == "Component"),
                    material.Targets.Count(x => x.Kind == "Capability"), material.Targets.Count(x => x.Kind == "HostingScope"),
                    material.Targets.Count(x => x.Kind == "System")));
        }, ct);

    public Task<ProviderImpactReviewResponse> ReviewAsync(Guid offeringId, Guid reviewId, ReviewProviderImpactRequest request,
        string actor, CancellationToken ct) =>
        store.WriteAsync<ProviderImpactReviewResponse>(offeringId, $"ImpactReviewed:{reviewId:D}", null, request, actor, async (db, _, offering) =>
        {
            var row = await LoadAsync(db, offering!, reviewId, ct);
            Expected(row, request.ExpectedRevision);
            Text(request.Rationale, "impact review rationale", 2000);
            if (request.Disposition is not ("AcceptForPublication" or "RequestChanges" or "Reject"))
                throw new ArgumentException("Use AcceptForPublication, RequestChanges or Reject.");
            if (row.Disposition != "PendingReview" || row.PreviewId != request.PreviewId || row.PreviewHash != request.PreviewHash)
                throw Stale("Review the exact pending impact preview; prior dispositions are retained.");
            await EnsureFreshAsync(db, row, ct);
            if (request.Disposition == "AcceptForPublication" && Read<ProviderImpactBlocker[]>(row.BlockersJson).Length != 0)
                throw Stale("Resolve the impact blockers and generate a new preview before accepting publication.");
            row.Disposition = request.Disposition;
            row.Rationale = request.Rationale.Trim();
            row.ReviewedBy = Text(actor, "human reviewer", 254);
            row.ReviewedAt = DateTimeOffset.UtcNow;
            row.Revision++;
            return Project(row);
        }, ct);

    public async Task<PagedResult<ProviderImpactReviewResponse>> ListAsync(Guid offeringId, int page, int pageSize, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        return await PageAsync(db.Set<ProviderAuthorizationImpactReview>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offeringId).OrderBy(x => x.Id),
            page, pageSize, Project, ct);
    }

    public async Task<ProviderImpactReviewResponse> GetAsync(Guid offeringId, Guid reviewId, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        return Project(await LoadAsync(db, await store.OfferingAsync(db, offeringId, ct), reviewId, ct));
    }

    public async Task<PagedResult<ProviderImpactTargetResponse>> TargetsAsync(Guid offeringId, Guid reviewId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new ArgumentException("Use page >=1 and pageSize between 1 and 100.");
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var row = await LoadAsync(db, await store.OfferingAsync(db, offeringId, ct), reviewId, ct);
        var targets = Read<ProviderImpactTargetResponse[]>(row.TargetsJson);
        return new(targets.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), page, pageSize, targets.Length);
    }

    private static async Task<ProviderAuthorizationImpactReview> LoadAsync(AtoCopilotContext db, ProviderOffering offering, Guid id, CancellationToken ct) =>
        await db.Set<ProviderAuthorizationImpactReview>().SingleOrDefaultAsync(x =>
            x.Id == id && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct)
            ?? throw new KeyNotFoundException("Impact review was not found in this offering.");

    private static ProviderImpactReviewResponse Project(ProviderAuthorizationImpactReview row)
    {
        var input = ParseRetainedInput(row, []);
        var targets = ReadImpactTargets(row);
        string title;
        string summary;
        if (input is not null)
        {
            var kinds = input.Changes.GroupBy(x => x.Kind).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
            title = string.Join(", ", kinds.Select(x => ChangeKindLabel(x.Key))) + " change review";
            summary = string.Join(", ", kinds.Select(x => $"{x.Count()} {ChangeKindLabel(x.Key).ToLowerInvariant()} change(s)"))
                + $" retained for offering revision {input.ExpectedOfferingRevision}; "
                + $"{input.AuthorizationRevisionIds.Count} authorization revision(s), {input.PackageVersionIds.Count} package version(s).";
        }
        else
        {
            var reason = Read<ProviderImpactBlocker[]>(row.BlockersJson).FirstOrDefault(x =>
                x.Code is "SOURCE_CHANGE_REVIEW_REQUIRED" or "PRIVATE_CHANGE_REVIEW_REQUIRED");
            title = reason?.Code switch
            {
                "SOURCE_CHANGE_REVIEW_REQUIRED" => "Source or lifecycle change review",
                "PRIVATE_CHANGE_REVIEW_REQUIRED" => "Private catalog change review",
                _ => "Change review: retained input unavailable"
            };
            summary = reason is not null
                ? $"{reason.Message} This lifecycle-only review has no exact preview input; select current context before continuing."
                : "The retained change input is unavailable; open details and select exact context before continuing.";
        }
        return new(row.Id, row.Revision, row.Disposition, row.ReviewedBy, row.ReviewedAt, row.ContextSnapshotHash,
            row.InvalidatedAt.HasValue || row.ExpiresAt <= DateTimeOffset.UtcNow || row.InputJson == "{}")
        {
            Title = title,
            Summary = summary + " Counts describe retained relationship/dependency targets; no semantic coverage delta or mission authorization is inferred.",
            CreatedAt = row.CreatedAt,
            AffectedCounts = new(targets.Count(x => x.Kind == "Component"), targets.Count(x => x.Kind == "Capability"),
                targets.Count(x => x.Kind == "HostingScope"), targets.Count(x => x.Kind is "System" or "MissionSystem"))
        };
    }

    private static string ChangeKindLabel(string kind) => kind == "HostingScope" ? "Hosting scope" : kind;

    private static ProviderImpactTargetResponse[] ReadImpactTargets(ProviderAuthorizationImpactReview row)
    {
        var targets = Read<ProviderImpactTargetResponse[]>(row.TargetsJson);
        if (targets.Any(x => x is null || string.IsNullOrWhiteSpace(x.Kind) || string.IsNullOrWhiteSpace(x.RecordId)
            || string.IsNullOrWhiteSpace(x.ReviewState)))
            throw new InvalidDataException("The retained impact target projection is incomplete.");
        return targets;
    }

    internal static DbUpdateConcurrencyException Stale(string message) =>
        new ProviderPublicationConflictException("AUTHORIZATION_CONTEXT_STALE", message);

    internal static void Validate(CreateProviderImpactPreviewRequest request)
    {
        Bounded(request.Changes, "changes", 1);
        Bounded(request.AuthorizationRevisionIds, "authorization revisions", 0);
        Bounded(request.PackageVersionIds, "package versions");
        if (request.Changes.Select(x => (x.Kind, x.RecordId)).Distinct().Count() != request.Changes.Count
            || request.AuthorizationRevisionIds.Distinct().Count() != request.AuthorizationRevisionIds.Count
            || request.PackageVersionIds.Distinct().Count() != request.PackageVersionIds.Count)
            throw new ArgumentException("Use distinct exact changes and context revisions.");
        foreach (var change in request.Changes)
            if (change.RecordId == Guid.Empty || change.ExpectedRevision < 1 || change.ProposedSnapshotHash is null || change.ProposedSnapshotHash.Length != 64
                || !change.ProposedSnapshotHash.All(Uri.IsHexDigit)
                || change.Kind is not ("Component" or "Capability" or "Boundary" or "HostingScope"))
                throw new ArgumentException("Each change requires Component, Capability, Boundary or HostingScope, an ID, revision and SHA-256 snapshot hash.");
    }

    internal static async Task EnsureFreshAsync(AtoCopilotContext db, ProviderAuthorizationImpactReview row, CancellationToken ct)
    {
        if (row.InvalidatedAt.HasValue || row.ExpiresAt <= DateTimeOffset.UtcNow || row.InputJson == "{}")
            throw Stale("The impact preview expired or was invalidated. Generate and review a new preview.");
        var offering = await db.Set<ProviderOffering>().SingleAsync(x => x.Id == row.OfferingId && x.ProviderId == row.ProviderId, ct);
        var request = Read<CreateProviderImpactPreviewRequest>(row.InputJson);
        var current = await BuildAsync(db, offering, request, ct);
        if (current.ContextHash != row.ContextSnapshotHash || current.PreviewHash != row.PreviewHash)
            throw Stale("Offering, decision, scope, changes, dependencies or affected targets changed. Generate a new impact preview.");
    }
}

using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed record NarrativeProposalResponse(Guid Id, string ControlId, string NarrativeType, int BaseVersion,
    string BeforeContent, string ProposedContent, string StateHash, JsonElement Provenance,
    IReadOnlyList<string> Conflicts, IReadOnlyList<string> MissingEvidence, string Status, int Revision,
    DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset? ReviewedAt, string? ReviewedBy,
    string? ReviewNote, int? AcceptedVersion, bool IsStale, bool CanReview,
    string? ChangeSourceKind = null, string? ChangeSourceId = null, string? GenerationErrorCode = null);

public sealed partial class NarrativeProposalService(AtoCopilotContext db, ITenantContext tenant,
    NarrativeLibraryService library, IControlNarrativeService generator) : INarrativeChangeImpactService
{
    private Guid TenantId => tenant.EffectiveTenantId;

    public async Task<IReadOnlyList<NarrativeProposalResponse>> ListAsync(
        string systemId, string actor, CancellationToken cancellationToken = default)
    {
        await library.RequireSystemAsync(systemId, actor, false, cancellationToken);
        var proposals = await db.NarrativeProposals.AsNoTracking().Where(item => item.TenantId == TenantId && item.RegisteredSystemId == systemId)
            .OrderByDescending(item => item.CreatedAt).Take(500).ToListAsync(cancellationToken);
        var responses = new List<NarrativeProposalResponse>();
        var reviewer = await IsReviewerAsync(systemId, actor, cancellationToken);
        var actorAliases = await library.SelfApprovalAliasesAsync(actor, cancellationToken);
        foreach (var proposal in proposals)
            responses.Add(await ReadResponseAsync(proposal, actor, reviewer, actorAliases, cancellationToken));
        return responses;
    }

    public async Task<NarrativeProposalResponse> GetAsync(
        string systemId, Guid proposalId, string actor, CancellationToken cancellationToken = default)
    {
        await library.RequireSystemAsync(systemId, actor, false, cancellationToken);
        var proposal = await db.NarrativeProposals.AsNoTracking().SingleOrDefaultAsync(item => item.TenantId == TenantId &&
            item.RegisteredSystemId == systemId && item.Id == proposalId, cancellationToken)
            ?? throw new KeyNotFoundException("Proposal not found.");
        return await ReadResponseAsync(proposal, actor, await IsReviewerAsync(systemId, actor, cancellationToken),
            await library.SelfApprovalAliasesAsync(actor, cancellationToken), cancellationToken);
    }

    private async Task<NarrativeProposalResponse> ReadResponseAsync(NarrativeProposal proposal, string actor,
        bool reviewer, HashSet<string> actorAliases, CancellationToken ct)
    {
        var implementation = await ImplementationAsync(proposal.RegisteredSystemId, proposal.ControlId, ct);
        var snapshot = await SnapshotAsync(implementation, proposal.NarrativeType, actor, ct);
        var stale = StateHash(snapshot) != proposal.StateHash ||
            proposal.Status == "Approved" && Content(implementation, proposal.NarrativeType) != proposal.ProposedContent ||
            proposal.Status is "Draft" or "PendingGeneration" or "GenerationFailed" &&
                (implementation.CurrentVersion != proposal.BaseVersion || Content(implementation, proposal.NarrativeType) != proposal.BeforeContent);
        return Response(proposal, stale,
            !stale && reviewer && !actorAliases.Contains(proposal.CreatedBy) && proposal.Status == "Draft");
    }

    public async Task<NarrativeProposalResponse> GenerateAsync(string systemId, string controlId, string narrativeType,
        string actor, int expectedVersion, CancellationToken cancellationToken = default)
    {
        await library.RequireNarrativeAuthorAsync(systemId, actor, cancellationToken);
        if (narrativeType is not ("Policy" or "Technical")) throw new ArgumentException("Choose Policy or Technical.");
        var implementation = await ImplementationAsync(systemId, controlId, cancellationToken);
        EnsureVersion(implementation, expectedVersion);
        var snapshot = await SnapshotAsync(implementation, narrativeType, actor, cancellationToken);
        var hash = StateHash(snapshot);
        var existing = await db.NarrativeProposals.AsNoTracking().FirstOrDefaultAsync(item => item.TenantId == TenantId &&
            item.RegisteredSystemId == systemId && item.ControlId == controlId && item.NarrativeType == narrativeType &&
            item.BaseVersion == expectedVersion && item.StateHash == hash && item.Status == "Draft", cancellationToken);
        if (existing is not null) return Response(existing, false, false);
        var generated = await generator.GenerateGroundedDraftAsync(narrativeType, snapshot, cancellationToken);
        await library.RequireNarrativeAuthorAsync(systemId, actor, cancellationToken);
        implementation = await ImplementationAsync(systemId, controlId, cancellationToken);
        EnsureVersion(implementation, expectedVersion);
        if (StateHash(await SnapshotAsync(implementation, narrativeType, actor, cancellationToken)) != hash)
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Source state changed during generation. Generate again.");
        var returned = await db.NarrativeProposals.CountAsync(item => item.TenantId == TenantId && item.RegisteredSystemId == systemId &&
            item.ControlId == controlId && item.NarrativeType == narrativeType && item.Status == "NeedsRevision", cancellationToken);
        var row = new NarrativeProposal
        {
            TenantId = TenantId, RegisteredSystemId = systemId, ControlId = controlId, NarrativeType = narrativeType,
            BaseVersion = expectedVersion, BeforeContent = Content(implementation, narrativeType), ProposedContent = generated.Narrative,
            StateHash = hash, DeduplicationKey = Hash($"{systemId}:{controlId}:{narrativeType}:{expectedVersion}:{hash}:{returned}"),
            ProvenanceJson = snapshot, ConflictsJson = JsonSerializer.Serialize(generated.Conflicts),
            MissingEvidenceJson = JsonSerializer.Serialize(generated.MissingEvidence.Concat(SourceGaps(snapshot)).Distinct()), CreatedBy = actor,
        };
        db.NarrativeProposals.Add(row);
        await NarrativePersistence.SaveAsync(db, cancellationToken);
        return Response(row, false, false);
    }

    public async Task<NarrativeProposalResponse> ReviewAsync(string systemId, Guid id, string actor, int expectedRevision,
        string decision, string? note, CancellationToken cancellationToken = default)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        await library.RequireSystemAsync(systemId, actor, true, cancellationToken);
        var row = await db.NarrativeProposals.FirstOrDefaultAsync(item => item.Id == id && item.TenantId == TenantId &&
            item.RegisteredSystemId == systemId, cancellationToken) ?? throw new KeyNotFoundException("Proposal not found.");
        if ((await library.SelfApprovalAliasesAsync(actor, cancellationToken)).Contains(row.CreatedBy)
            || !await IsReviewerAsync(systemId, actor, cancellationToken))
            throw new UnauthorizedAccessException("A different authorized ISSM must review the proposal.");
        if (decision is not ("Approve" or "RequestRevision") || note?.Length > 2000 ||
            decision == "RequestRevision" && string.IsNullOrWhiteSpace(note)) throw new ArgumentException("Provide a valid review decision and note.");
        if (row.Revision != expectedRevision || row.Status != "Draft")
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Proposal was already reviewed or changed.");
        var implementation = await db.ControlImplementations.SingleAsync(item => item.TenantId == TenantId &&
            item.RegisteredSystemId == systemId && item.ControlId == row.ControlId, cancellationToken);
        if (decision == "Approve")
        {
            EnsureVersion(implementation, row.BaseVersion);
            if (Content(implementation, row.NarrativeType) != row.BeforeContent ||
                StateHash(await SnapshotAsync(implementation, row.NarrativeType, actor, cancellationToken)) != row.StateHash)
                throw new InvalidOperationException("CONCURRENCY_CONFLICT: Narrative or source state changed. Generate a new proposal.");
            await AcceptAsync(implementation, row, actor, note, cancellationToken);
        }
        row.Status = decision == "Approve" ? "Approved" : "NeedsRevision";
        row.ReviewedAt = DateTime.UtcNow;
        row.ReviewedBy = actor;
        row.ReviewNote = note?.Trim();
        row.Revision++;
        await NarrativePersistence.SaveAsync(db, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Response(row, false, false);
    }

    private async Task AcceptAsync(ControlImplementation implementation, NarrativeProposal row, string actor,
        string? note, CancellationToken cancellationToken)
    {
        if (!await db.NarrativeVersions.AnyAsync(version => version.TenantId == TenantId && version.ControlImplementationId == implementation.Id &&
            version.VersionNumber == implementation.CurrentVersion, cancellationToken))
            db.NarrativeVersions.Add(new NarrativeVersion
            {
                TenantId = TenantId, ControlImplementationId = implementation.Id, VersionNumber = implementation.CurrentVersion,
                Content = implementation.Narrative ?? implementation.TechnicalNarrative ?? "", SnapshotJson = NarrativeContentSnapshot.Capture(implementation),
                Status = implementation.ApprovalStatus, AuthoredBy = implementation.AuthoredBy, AuthoredAt = implementation.AuthoredAt,
            });
        var approvedReplacement = await ReplaceApprovedHalfAsync(implementation, row, cancellationToken);
        if (row.NarrativeType == "Policy") implementation.PolicyNarrative = row.ProposedContent;
        else
        {
            implementation.SetCombinedNarrative(row.ProposedContent);
            implementation.AiSuggested = true;
            implementation.IsAutoPopulated = true;
            implementation.IsManuallyCustomized = false;
        }
        implementation.CurrentVersion++;
        implementation.AuthoredBy = row.CreatedBy;
        implementation.AuthoredAt = row.CreatedAt;
        implementation.ReviewedBy = actor;
        implementation.ReviewedAt = DateTime.UtcNow;
        implementation.ModifiedAt = DateTime.UtcNow;
        var version = new NarrativeVersion
        {
            TenantId = TenantId, ControlImplementationId = implementation.Id, VersionNumber = implementation.CurrentVersion,
            Content = implementation.Narrative ?? implementation.TechnicalNarrative ?? "", SnapshotJson = NarrativeContentSnapshot.Capture(implementation),
            Status = implementation.ApprovalStatus, AuthoredBy = row.CreatedBy, AuthoredAt = row.CreatedAt,
            ChangeReason = $"Accepted {row.NarrativeType} proposal {row.Id}; source state {row.StateHash}",
        };
        db.NarrativeVersions.Add(version);
        if (implementation.ApprovalStatus == SspSectionStatus.Approved) implementation.ApprovedVersionId = version.Id;
        var accepted = approvedReplacement ?? version;
        row.AcceptedVersion = accepted.VersionNumber;
        db.NarrativeReviews.Add(new NarrativeReview
        {
            TenantId = TenantId, NarrativeVersionId = accepted.Id, ReviewedBy = actor,
            Decision = ReviewDecision.Approve, ReviewerComments = note?.Trim(), ReviewedAt = DateTime.UtcNow
        });
    }

    private async Task<NarrativeVersion?> ReplaceApprovedHalfAsync(
        ControlImplementation implementation, NarrativeProposal row, CancellationToken ct)
    {
        if (implementation.ApprovedVersionId is null || implementation.ApprovalStatus == SspSectionStatus.Approved) return null;
        var approved = await db.NarrativeVersions.AsNoTracking().SingleAsync(item => item.TenantId == TenantId &&
            item.Id == implementation.ApprovedVersionId && item.ControlImplementationId == implementation.Id, ct);
        if (approved.SnapshotJson is null)
            throw new InvalidOperationException("Approved dual-narrative snapshot is unavailable; review the baseline before accepting a replacement.");
        var snapshot = new ControlImplementation();
        NarrativeContentSnapshot.Restore(snapshot, approved.SnapshotJson);
        if (row.NarrativeType == "Policy") snapshot.PolicyNarrative = row.ProposedContent;
        else
        {
            snapshot.SetCombinedNarrative(row.ProposedContent);
            snapshot.AiSuggested = true;
            snapshot.IsAutoPopulated = true;
            snapshot.IsManuallyCustomized = false;
        }
        var replacement = new NarrativeVersion
        {
            TenantId = TenantId, ControlImplementationId = implementation.Id,
            VersionNumber = ++implementation.CurrentVersion, Status = SspSectionStatus.Approved,
            Content = snapshot.Narrative ?? snapshot.TechnicalNarrative ?? "",
            SnapshotJson = NarrativeContentSnapshot.Capture(snapshot), AuthoredBy = row.CreatedBy, AuthoredAt = row.CreatedAt,
            ChangeReason = $"Accepted {row.NarrativeType} proposal {row.Id}; preserved the approved companion half."
        };
        db.NarrativeVersions.Add(replacement);
        implementation.ApprovedVersionId = replacement.Id;
        return replacement;
    }
    private async Task<ControlImplementation> ImplementationAsync(string systemId, string controlId, CancellationToken cancellationToken) =>
        await db.ControlImplementations.AsNoTracking().FirstOrDefaultAsync(item => item.TenantId == TenantId &&
            item.RegisteredSystemId == systemId && item.ControlId == controlId, cancellationToken)
            ?? throw new KeyNotFoundException("Control not found.");

    private static void EnsureVersion(ControlImplementation implementation, int expectedVersion)
    {
        if (implementation.CurrentVersion != expectedVersion || implementation.ApprovalStatus == SspSectionStatus.UnderReview)
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Narrative changed or is under review.");
    }

    private async Task<bool> IsReviewerAsync(string systemId, string actor, CancellationToken cancellationToken) =>
        await library.CanReviewProposalAsync(systemId, actor, cancellationToken);

    private Task<string> SnapshotAsync(ControlImplementation implementation, string narrativeType, string actor, CancellationToken cancellationToken) =>
        new NarrativeGroundingService(db, TenantId, library).CaptureAsync(implementation, narrativeType, cancellationToken);

    private static IReadOnlyList<string> SourceGaps(string snapshot)
    {
        using var document = JsonDocument.Parse(snapshot);
        var gaps = new List<string> { "Reference claims and linked metadata do not independently verify control execution." };
        if (!document.RootElement.TryGetProperty("observedEvidence", out var observations) ||
            !observations.EnumerateArray().Any(item => item.TryGetProperty("Content", out var content) &&
                content.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(content.GetString())))
            gaps.Add("No execution observations are available for this narrative; implementation remains unknown.");
        if (document.RootElement.GetProperty("declaredResponsibilities").GetArrayLength() == 0)
            gaps.Add("Control responsibility is undesignated; capability mappings do not establish an allocation.");
        if (!document.RootElement.TryGetProperty("controlDefinition", out var definition) || definition.ValueKind == JsonValueKind.Null)
            gaps.Add("The control catalog definition is unavailable; verify the control requirements before relying on this draft.");
        return gaps;
    }

    private static string Content(ControlImplementation implementation, string type) =>
        type == "Policy" ? implementation.PolicyNarrative ?? "" : implementation.TechnicalNarrative ?? implementation.Narrative ?? "";
    private static string StateHash(string snapshot)
    {
        using var document = JsonDocument.Parse(snapshot);
        return Hash(CanonicalState(document.RootElement));
    }

    private static string CanonicalState(JsonElement element, string? section = null)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return "[" + string.Join(",", element.EnumerateArray().Select(item => CanonicalState(item, section))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) + "]";
        if (element.ValueKind != JsonValueKind.Object) return element.GetRawText();
        return "{" + string.Join(",", element.EnumerateObject()
            .Where(property => property.Name is not ("CollectedAt" or "DiscoveredAt" or "UploadedAt" or "ValidatedAt" or "IntegrityVerifiedAt") &&
                !(property.Name == "Id" && section is "observedEvidence" or "observedFindings"))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => JsonSerializer.Serialize(property.Name) + ":" +
                CanonicalState(property.Value, section ?? property.Name))) + "}";
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static NarrativeProposalResponse Response(NarrativeProposal row, bool stale, bool canReview) => new(
        row.Id, row.ControlId, row.NarrativeType, row.BaseVersion, row.BeforeContent, row.ProposedContent, row.StateHash,
        JsonSerializer.Deserialize<JsonElement>(row.ProvenanceJson), JsonSerializer.Deserialize<List<string>>(row.ConflictsJson) ?? [],
        JsonSerializer.Deserialize<List<string>>(row.MissingEvidenceJson) ?? [], row.Status, row.Revision,
        new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)), row.CreatedBy,
        row.ReviewedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.ReviewedAt.Value, DateTimeKind.Utc)) : null,
        row.ReviewedBy, row.ReviewNote, row.AcceptedVersion, stale, canReview,
        row.ChangeSourceKind, row.ChangeSourceId, row.GenerationErrorCode);
}
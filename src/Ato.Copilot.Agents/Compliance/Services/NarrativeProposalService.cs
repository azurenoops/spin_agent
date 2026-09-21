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
    string? ReviewNote, int? AcceptedVersion, bool IsStale, bool CanReview);

public sealed class NarrativeProposalService(AtoCopilotContext db, ITenantContext tenant,
    NarrativeLibraryService library, IControlNarrativeService generator)
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
        foreach (var proposal in proposals)
        {
            var implementation = await ImplementationAsync(systemId, proposal.ControlId, cancellationToken);
            var snapshot = await SnapshotAsync(implementation, proposal.NarrativeType, actor, cancellationToken);
            responses.Add(Response(proposal, Hash(snapshot) != proposal.StateHash || proposal.Status == "Draft" &&
                (implementation.CurrentVersion != proposal.BaseVersion || Content(implementation, proposal.NarrativeType) != proposal.BeforeContent),
                reviewer && proposal.CreatedBy != actor && proposal.Status == "Draft"));
        }
        return responses;
    }

    public async Task<NarrativeProposalResponse> GenerateAsync(string systemId, string controlId, string narrativeType,
        string actor, int expectedVersion, CancellationToken cancellationToken = default)
    {
        await library.RequireSystemAsync(systemId, actor, true, cancellationToken);
        if (narrativeType is not ("Policy" or "Technical")) throw new ArgumentException("Choose Policy or Technical.");
        var implementation = await ImplementationAsync(systemId, controlId, cancellationToken);
        EnsureVersion(implementation, expectedVersion);
        var snapshot = await SnapshotAsync(implementation, narrativeType, actor, cancellationToken);
        var hash = Hash(snapshot);
        var existing = await db.NarrativeProposals.AsNoTracking().FirstOrDefaultAsync(item => item.TenantId == TenantId &&
            item.RegisteredSystemId == systemId && item.ControlId == controlId && item.NarrativeType == narrativeType &&
            item.BaseVersion == expectedVersion && item.StateHash == hash && item.Status == "Draft", cancellationToken);
        if (existing is not null) return Response(existing, false, false);
        var generated = await generator.GenerateGroundedDraftAsync(narrativeType, snapshot, cancellationToken);
        await library.RequireSystemAsync(systemId, actor, true, cancellationToken);
        implementation = await ImplementationAsync(systemId, controlId, cancellationToken);
        EnsureVersion(implementation, expectedVersion);
        if (Hash(await SnapshotAsync(implementation, narrativeType, actor, cancellationToken)) != hash)
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Source state changed during generation. Generate again.");
        var returned = await db.NarrativeProposals.CountAsync(item => item.TenantId == TenantId && item.RegisteredSystemId == systemId &&
            item.ControlId == controlId && item.NarrativeType == narrativeType && item.Status == "NeedsRevision", cancellationToken);
        var row = new NarrativeProposal
        {
            TenantId = TenantId, RegisteredSystemId = systemId, ControlId = controlId, NarrativeType = narrativeType,
            BaseVersion = expectedVersion, BeforeContent = Content(implementation, narrativeType), ProposedContent = generated.Narrative,
            StateHash = hash, DeduplicationKey = Hash($"{systemId}:{controlId}:{narrativeType}:{expectedVersion}:{hash}:{returned}"),
            ProvenanceJson = snapshot, ConflictsJson = JsonSerializer.Serialize(generated.Conflicts),
            MissingEvidenceJson = JsonSerializer.Serialize(generated.MissingEvidence.Concat(
                ["Reference claims and linked metadata do not independently verify control execution."]).Distinct()), CreatedBy = actor,
        };
        db.NarrativeProposals.Add(row);
        await db.SaveChangesAsync(cancellationToken);
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
        if (row.CreatedBy == actor || !await IsReviewerAsync(systemId, actor, cancellationToken))
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
                Hash(await SnapshotAsync(implementation, row.NarrativeType, actor, cancellationToken)) != row.StateHash)
                throw new InvalidOperationException("CONCURRENCY_CONFLICT: Narrative or source state changed. Generate a new proposal.");
            await AcceptAsync(implementation, row, actor, cancellationToken);
        }
        row.Status = decision == "Approve" ? "Approved" : "NeedsRevision";
        row.ReviewedAt = DateTime.UtcNow;
        row.ReviewedBy = actor;
        row.ReviewNote = note?.Trim();
        row.Revision++;
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Response(row, false, false);
    }

    private async Task AcceptAsync(ControlImplementation implementation, NarrativeProposal row, string actor, CancellationToken cancellationToken)
    {
        if (!await db.NarrativeVersions.AnyAsync(version => version.TenantId == TenantId && version.ControlImplementationId == implementation.Id &&
            version.VersionNumber == implementation.CurrentVersion, cancellationToken))
            db.NarrativeVersions.Add(new NarrativeVersion
            {
                TenantId = TenantId, ControlImplementationId = implementation.Id, VersionNumber = implementation.CurrentVersion,
                Content = implementation.Narrative ?? implementation.TechnicalNarrative ?? "", SnapshotJson = NarrativeContentSnapshot.Capture(implementation),
                Status = implementation.ApprovalStatus, AuthoredBy = implementation.AuthoredBy, AuthoredAt = implementation.AuthoredAt,
            });
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
        row.AcceptedVersion = version.VersionNumber;
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
        await library.HasSharedAuthorityAsync(actor, cancellationToken) || await db.RmfRoleAssignments.AnyAsync(assignment =>
            assignment.TenantId == TenantId && assignment.RegisteredSystemId == systemId && assignment.UserId == actor &&
            assignment.IsActive && assignment.RmfRole == RmfRole.Issm, cancellationToken);

    private async Task<string> SnapshotAsync(ControlImplementation implementation, string narrativeType, string actor, CancellationToken cancellationToken)
    {
        var references = (await library.ListAsync(implementation.RegisteredSystemId, actor, cancellationToken))
            .Where(item => item.IsPublished).GroupBy(item => item.ReferenceKey).Select(group => group.MaxBy(item => item.Version)!)
            .SelectMany(reference => reference.Passages.Where(passage => passage.ControlId == implementation.ControlId.ToUpperInvariant() &&
                passage.NarrativeType == narrativeType).Select(passage => new
                { reference.Id, reference.Title, reference.Version, reference.Scope, reference.SourceSha256, passage.Content }))
            .OrderBy(item => item.Id).ThenBy(item => item.Content).ToList();
        var system = await db.RegisteredSystems.AsNoTracking().SingleAsync(item => item.TenantId == TenantId && item.Id == implementation.RegisteredSystemId, cancellationToken);
        if (narrativeType == "Policy") return JsonSerializer.Serialize(new
        { implementation.ControlId, declaredSystem = new { system.Name, system.Description, system.MissionCriticality }, referenceClaims = references });
        var capability = await db.SecurityCapabilities.AsNoTracking().Where(item => item.TenantId == TenantId && item.Id == implementation.SecurityCapabilityId)
            .Select(item => new { item.Id, item.Name, item.Provider, item.Description, item.ImplementationStatus }).FirstOrDefaultAsync(cancellationToken);
        var assignments = db.ComponentSystemAssignments.Where(item => item.TenantId == TenantId && item.RegisteredSystemId == system.Id).Select(item => item.SystemComponentId);
        var components = await db.SystemComponents.AsNoTracking().Where(item => item.TenantId == TenantId &&
            (item.RegisteredSystemId == system.Id || assignments.Contains(item.Id))).OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.Name, item.Description, item.ComponentType, item.Status, item.AzureResourceId, item.AzureResourceType }).ToListAsync(cancellationToken);
        var boundaries = await db.AuthorizationBoundaryDefinitions.AsNoTracking().Where(item => item.TenantId == TenantId && item.RegisteredSystemId == system.Id)
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken);
        var validation = await db.ControlValidationLinks.AsNoTracking().Where(item => item.TenantId == TenantId && item.ControlImplementationId == implementation.Id)
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.LinkType, item.LinkTarget, item.Description, item.ValidatedAt, item.IsAutomated }).ToListAsync(cancellationToken);
        var assessmentIds = db.Assessments.Where(item => item.TenantId == TenantId && item.RegisteredSystemId == system.Id).Select(item => item.Id);
        var observations = await db.Evidence.AsNoTracking().Where(item => item.TenantId == TenantId &&
                item.ControlId == implementation.ControlId && item.AssessmentId != null && assessmentIds.Contains(item.AssessmentId))
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.EvidenceType, item.Content, item.ContentHash,
                item.ResourceId, item.CollectedAt, item.CollectionMethod, item.IntegrityVerifiedAt }).ToListAsync(cancellationToken);
        var findings = await db.Findings.AsNoTracking().Where(item => item.TenantId == TenantId &&
                item.ControlId == implementation.ControlId && assessmentIds.Contains(item.AssessmentId))
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.Title, item.Description, item.Status,
                item.Severity, item.ResourceId, item.Source, item.DiscoveredAt }).ToListAsync(cancellationToken);
        var artifacts = await db.EvidenceArtifacts.AsNoTracking().Where(item => item.TenantId == TenantId &&
                item.RegisteredSystemId == system.Id && item.ControlImplementationId == implementation.Id && !item.IsDeleted &&
                item.NarrativeType == EvidenceNarrativeType.Technical)
            .OrderBy(item => item.Id).Select(item => new { item.Id, item.FileName, item.ContentHash, item.Description,
                item.UploadedAt, item.CollectionMethod }).ToListAsync(cancellationToken);
        return JsonSerializer.Serialize(new { implementation.ControlId, declaredSystem = new { system.Name, system.HostingEnvironment },
            declaredCapability = capability, declaredComponents = components, declaredBoundaries = boundaries,
            linkedValidationMetadata = validation, observedEvidence = observations, observedFindings = findings,
            evidenceArtifactMetadata = artifacts, referenceClaims = references });
    }

    private static string Content(ControlImplementation implementation, string type) =>
        type == "Policy" ? implementation.PolicyNarrative ?? "" : implementation.TechnicalNarrative ?? implementation.Narrative ?? "";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static NarrativeProposalResponse Response(NarrativeProposal row, bool stale, bool canReview) => new(
        row.Id, row.ControlId, row.NarrativeType, row.BaseVersion, row.BeforeContent, row.ProposedContent, row.StateHash,
        JsonSerializer.Deserialize<JsonElement>(row.ProvenanceJson), JsonSerializer.Deserialize<List<string>>(row.ConflictsJson) ?? [],
        JsonSerializer.Deserialize<List<string>>(row.MissingEvidenceJson) ?? [], row.Status, row.Revision,
        new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)), row.CreatedBy,
        row.ReviewedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.ReviewedAt.Value, DateTimeKind.Utc)) : null,
        row.ReviewedBy, row.ReviewNote, row.AcceptedVersion, stale, canReview);
}
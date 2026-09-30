using System.Text.Json;
using System.Text.Json.Serialization;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public static partial class DashboardEndpoints
{
    private static void MapNarrativeWorkspaceRoutes(IEndpointRouteBuilder group)
    {
        group.MapGet("/systems/{systemId}/narrative-workspace", async (
                string systemId,
                AtoCopilotContext db,
                ITenantContext tenant,
                ISystemWorkspaceAccessService accessService,
                INistControlsService nistControlsService,
                string? view,
                string? search,
                string? family,
                string? status,
                int page = 1,
                int pageSize = 50,
                CancellationToken ct = default) =>
            {
                if (page < 1 || pageSize is < 1 or > 200)
                    return Results.BadRequest(new { error = "Page must be positive and pageSize must be between 1 and 200." });
                var selectedView = string.IsNullOrWhiteSpace(view) ? "all-controls" : view.Trim().ToLowerInvariant();
                if (selectedView is not ("needs-attention" or "all-controls" or "approved-statements"))
                    return Results.BadRequest(new { error = "Choose needs-attention, all-controls, or approved-statements." });

                var access = await accessService.GetAccessAsync(
                    tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
                var projection = await BuildNarrativeWorkspaceAsync(
                    db, systemId, access, nistControlsService, ct);
                IEnumerable<NarrativeWorkspaceRow> filtered = projection;
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var value = search.Trim();
                    filtered = filtered.Where(row => row.ControlId.Contains(value, StringComparison.OrdinalIgnoreCase)
                        || row.ControlTitle.Contains(value, StringComparison.OrdinalIgnoreCase));
                }
                if (!string.IsNullOrWhiteSpace(family))
                    filtered = filtered.Where(row => row.Family.Equals(family.Trim(), StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(status))
                    filtered = filtered.Where(row => row.ImplementationStatus.Equals(
                        status.Trim(), StringComparison.OrdinalIgnoreCase));

                var filteredRows = filtered.ToList();
                var counts = new NarrativeWorkspaceCounts(
                    filteredRows.Count(row => row.NeedsAttention),
                    filteredRows.Count,
                    filteredRows.Count(row => row.HasApprovedStatements),
                    filteredRows.Count(row => row.HasProposedUpdate));
                var selectedRows = selectedView switch
                {
                    "needs-attention" => filteredRows.Where(row => row.NeedsAttention),
                    "approved-statements" => filteredRows.Where(row => row.HasApprovedStatements),
                    _ => filteredRows,
                };
                var materialized = selectedRows.ToList();
                return Results.Ok(new NarrativeWorkspaceResponse(
                    systemId,
                    counts,
                    materialized.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
                    new NarrativeWorkspaceListPermissions(
                        access.Permissions.CanAuthorNarratives,
                        access.Permissions.CanReviewNarratives,
                        access.Permissions.CanManageEvidence)));
            })
            .WithName("GetControlNarrativeWorkspace")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);

        group.MapGet("/systems/{systemId}/narrative-workspace/{controlId}", async (
                string systemId,
                string controlId,
                AtoCopilotContext db,
                ITenantContext tenant,
                ISystemWorkspaceAccessService accessService,
                INistControlsService nistControlsService,
                CancellationToken ct) =>
            {
                var implementation = await db.ControlImplementations.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.RegisteredSystemId == systemId && item.ControlId == controlId, ct);
                if (implementation is null)
                    return Results.NotFound(new { error = "Control narrative was not found in this system." });

                var access = await accessService.GetAccessAsync(
                    tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
                var versions = await db.NarrativeVersions.AsNoTracking()
                    .Where(item => item.ControlImplementationId == implementation.Id)
                    .OrderByDescending(item => item.VersionNumber).ToListAsync(ct);
                var versionIds = versions.Select(item => item.Id).ToArray();
                var reviews = await db.NarrativeReviews.AsNoTracking()
                    .Where(item => versionIds.Contains(item.NarrativeVersionId))
                    .OrderByDescending(item => item.ReviewedAt).ToListAsync(ct);
                var proposals = await db.NarrativeProposals.AsNoTracking()
                    .Where(item => item.RegisteredSystemId == systemId && item.ControlId == controlId)
                    .OrderByDescending(item => item.CreatedAt).ToListAsync(ct);
                var evidence = await db.ControlValidationLinks.AsNoTracking()
                    .Where(item => item.ControlImplementationId == implementation.Id)
                    .OrderByDescending(item => item.AddedAt).ToListAsync(ct);
                var responsibilities = await (
                    from baseline in db.ControlBaselines.AsNoTracking()
                    join responsibility in db.ControlInheritances.AsNoTracking()
                        on baseline.Id equals responsibility.ControlBaselineId
                    where baseline.RegisteredSystemId == systemId && responsibility.ControlId == controlId
                    orderby responsibility.SetAt descending
                    select responsibility).ToListAsync(ct);
                var nist = await db.NistControls.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.Id == controlId, ct);
                var catalogControl = (await nistControlsService.GetAllControlsAsync(ct))
                    .FirstOrDefault(item => item.Id.Equals(CatalogControlId(controlId), StringComparison.OrdinalIgnoreCase));
                var approvedVersion = implementation.ApprovedVersionId is null
                    ? null
                    : versions.SingleOrDefault(item => item.Id == implementation.ApprovedVersionId);
                var approved = ParseSnapshot(approvedVersion);
                var approvalReview = approvedVersion is null
                    ? null
                    : reviews.FirstOrDefault(item => item.NarrativeVersionId == approvedVersion.Id
                        && item.Decision == ReviewDecision.Approve);
                var permissions = Permissions(access.Permissions);

                return Results.Ok(new NarrativeWorkspaceDetail(
                    systemId,
                    implementation.Id,
                    implementation.ControlId,
                    catalogControl?.Title ?? nist?.Title ?? implementation.ControlId,
                    catalogControl?.Family ?? nist?.Family ?? ControlFamily(implementation.ControlId),
                    implementation.ImplementationStatus.ToString(),
                    implementation.ApprovalStatus.ToString(),
                    implementation.CurrentVersion,
                    new NarrativeStatements(
                        StatementDetail("Policy", implementation.PolicyNarrative, approved?.PolicyNarrative,
                            implementation, approvedVersion, proposals),
                        StatementDetail("Technical", implementation.TechnicalNarrative, approved?.TechnicalNarrative,
                            implementation, approvedVersion, proposals)),
                    approvedVersion is null || approved is null
                        ? null
                        : new ApprovedNarrativeSnapshot(approvedVersion.Id, approvedVersion.VersionNumber,
                            approved.PolicyNarrative ?? string.Empty, approved.TechnicalNarrative ?? string.Empty,
                            approvedVersion.AuthoredBy, approvedVersion.AuthoredAt,
                            approvalReview?.ReviewedBy, approvalReview?.ReviewedAt),
                    proposals.Select(item => Proposal(item, implementation, access.Permissions, tenant.PersonId)).ToArray(),
                    evidence.Select(item => new NarrativeEvidence(
                        item.Id, item.LinkType.ToString(), item.LinkTarget, item.Description,
                        item.AddedBy, item.AddedAt, item.ValidatedAt, item.IsAutomated)).ToArray(),
                    responsibilities.Select(item => new NarrativeResponsibility(
                        item.Id, item.InheritanceType.ToString(), item.Provider, item.CustomerResponsibility,
                        item.DesignationSource, item.SetBy, item.SetAt)).ToArray(),
                    versions.Select(version => new NarrativeHistoryEntry(
                        version.Id,
                        version.VersionNumber,
                        version.Status.ToString(),
                        version.Content,
                        ParseSnapshot(version)?.PolicyNarrative,
                        ParseSnapshot(version)?.TechnicalNarrative,
                        version.AuthoredBy,
                        version.AuthoredAt,
                        version.ChangeReason,
                        reviews.Where(review => review.NarrativeVersionId == version.Id)
                            .Select(review => new NarrativeHistoryReview(
                                review.Id, review.Decision.ToString(), review.ReviewedBy,
                                review.ReviewedAt, review.ReviewerComments)).ToArray())).ToArray(),
                    permissions));
            })
            .WithName("GetControlNarrativeWorkspaceDetail")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem);
    }

    private static async Task<List<NarrativeWorkspaceRow>> BuildNarrativeWorkspaceAsync(
        AtoCopilotContext db,
        string systemId,
        SystemWorkspaceAccessResponse access,
        INistControlsService nistControlsService,
        CancellationToken ct)
    {
        var implementations = await db.ControlImplementations.AsNoTracking()
            .Where(item => item.RegisteredSystemId == systemId)
            .OrderBy(item => item.ControlId).ToListAsync(ct);
        var approvedIds = implementations.Where(item => item.ApprovedVersionId != null)
            .Select(item => item.ApprovedVersionId!).ToArray();
        var approvedVersions = await db.NarrativeVersions.AsNoTracking()
            .Where(item => approvedIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);
        var proposals = await db.NarrativeProposals.AsNoTracking()
            .Where(item => item.RegisteredSystemId == systemId)
            .OrderByDescending(item => item.CreatedAt).ToListAsync(ct);
        var controls = await db.NistControls.AsNoTracking()
            .Where(item => implementations.Select(implementation => implementation.ControlId).Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, StringComparer.OrdinalIgnoreCase, ct);
        var catalogControls = (await nistControlsService.GetAllControlsAsync(ct))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rows = new List<NarrativeWorkspaceRow>(implementations.Count);

        foreach (var implementation in implementations)
        {
            approvedVersions.TryGetValue(implementation.ApprovedVersionId ?? string.Empty, out var approvedVersion);
            var approved = ParseSnapshot(approvedVersion);
            var controlProposals = proposals.Where(item =>
                item.ControlId.Equals(implementation.ControlId, StringComparison.OrdinalIgnoreCase)).ToArray();
            var proposalStates = controlProposals.Select(item => ProposalState(item, implementation)).ToArray();
            var proposalAttention = proposalStates.Any(item => item.IsActionable || item.IsStale
                || item.HasConflicts || item.HasMissingEvidence);
            var hasProposedUpdate = controlProposals.Any(item =>
                item.Status is "Draft" or "PendingGeneration" or "GenerationFailed");
            var hasApprovedStatements = HasContent(approved?.PolicyNarrative) && HasContent(approved?.TechnicalNarrative);
            var needsAttention = !HasContent(implementation.PolicyNarrative)
                || !HasContent(implementation.TechnicalNarrative)
                || implementation.ApprovalStatus != SspSectionStatus.Approved
                || proposalAttention;
            controls.TryGetValue(implementation.ControlId, out var control);
            catalogControls.TryGetValue(CatalogControlId(implementation.ControlId), out var catalogControl);
            var policyProposal = controlProposals.FirstOrDefault(item => item.NarrativeType == "Policy");
            var technicalProposal = controlProposals.FirstOrDefault(item => item.NarrativeType == "Technical");
            var nextAction = NextAction(implementation, controlProposals, access.Permissions);
            rows.Add(new NarrativeWorkspaceRow(
                implementation.Id,
                implementation.ControlId,
                catalogControl?.Title ?? control?.Title ?? implementation.ControlId,
                catalogControl?.Family ?? control?.Family ?? ControlFamily(implementation.ControlId),
                implementation.ImplementationStatus.ToString(),
                implementation.ApprovalStatus.ToString(),
                implementation.CurrentVersion,
                ListStatementState(implementation.PolicyNarrative, approved?.PolicyNarrative,
                    implementation, policyProposal),
                ListStatementState(implementation.TechnicalNarrative, approved?.TechnicalNarrative,
                    implementation, technicalProposal),
                nextAction.Action,
                nextAction.Label,
                nextAction.Reason,
                needsAttention,
                hasApprovedStatements,
                hasProposedUpdate));
        }
        return rows;
    }

    private static NarrativeStatementState ListStatementState(
        string? current,
        string? approved,
        ControlImplementation implementation,
        NarrativeProposal? proposal)
    {
        var proposalState = proposal is null ? null : ProposalState(proposal, implementation);
        return new(
            DisplayState(current, approved, implementation.ApprovalStatus),
            HasContent(current),
            HasContent(approved),
            proposal?.Id,
            proposal?.Status,
            proposalState?.IsStale ?? false);
    }

    private static NarrativeNextAction NextAction(
        ControlImplementation implementation,
        IReadOnlyCollection<NarrativeProposal> proposals,
        SystemWorkspacePermissions permissions)
    {
        var reviewable = proposals.FirstOrDefault(proposal =>
            ProposalState(proposal, implementation) is { IsActionable: true, IsStale: false });
        if (reviewable is not null)
            return permissions.CanReviewNarratives
                ? new("review-proposal", "Review proposed update", null)
                : new("view-proposal", "View proposed update", "Narrative review permission is required.");
        if (!HasContent(implementation.PolicyNarrative) || !HasContent(implementation.TechnicalNarrative))
            return permissions.CanAuthorNarratives
                ? new("complete-statements", "Complete missing statements", null)
                : new("view-details", "View statement details", "Narrative author permission is required.");
        if (implementation.ApprovalStatus != SspSectionStatus.Approved)
            return permissions.CanAuthorNarratives
                ? new("edit-statements", "Continue statement work", null)
                : new("view-details", "View statement details", "Narrative author permission is required.");
        return new("view-details", "View statement details", null);
    }

    private static NarrativeStatementDetail StatementDetail(
        string type,
        string? current,
        string? approved,
        ControlImplementation implementation,
        NarrativeVersion? approvedVersion,
        IReadOnlyCollection<NarrativeProposal> proposals) =>
        new(type,
            current ?? string.Empty,
            approved ?? string.Empty,
            DisplayState(current, approved, implementation.ApprovalStatus),
            HasContent(current),
            HasContent(approved),
            implementation.AuthoredBy,
            implementation.ModifiedAt ?? implementation.AuthoredAt,
            approvedVersion?.Id,
            approvedVersion?.VersionNumber,
            SourceFreshness(proposals.Where(item => item.NarrativeType == type), proposals
                .Where(item => item.NarrativeType == type).Any(item => ProposalState(item, implementation).IsStale)),
            implementation.AiSuggested,
            implementation.IsAutoPopulated,
            implementation.IsManuallyCustomized);

    private static NarrativeWorkspaceProposal Proposal(
        NarrativeProposal proposal,
        ControlImplementation implementation,
        SystemWorkspacePermissions permissions,
        Guid? personId)
    {
        var state = ProposalState(proposal, implementation);
        var selfAuthored = personId.HasValue
            && proposal.CreatedBy.Equals(personId.Value.ToString(), StringComparison.OrdinalIgnoreCase);
        var canReview = permissions.CanReviewNarratives && state.IsActionable && !state.IsStale && !selfAuthored;
        var reason = canReview ? null
            : !permissions.CanReviewNarratives ? "Narrative review permission is required."
            : selfAuthored ? "A different authorized reviewer must review this proposal."
            : state.IsStale ? "The proposal is stale and must be regenerated."
            : "Only a current draft proposal can be reviewed.";
        return new(
            proposal.Id,
            proposal.NarrativeType,
            proposal.BeforeContent,
            proposal.ProposedContent,
            proposal.Status,
            proposal.Revision,
            proposal.BaseVersion,
            state.IsStale,
            proposal.ChangeSourceKind ?? "Unknown",
            proposal.ChangeSourceId,
            ParseJson(proposal.ProvenanceJson, JsonValueKind.Object),
            ParseStringList(proposal.ConflictsJson),
            ParseStringList(proposal.MissingEvidenceJson),
            proposal.CreatedAt,
            proposal.CreatedBy,
            canReview,
            reason);
    }

    private static ProposalProjectionState ProposalState(
        NarrativeProposal proposal,
        ControlImplementation implementation)
    {
        var current = proposal.NarrativeType switch
        {
            "Policy" => implementation.PolicyNarrative ?? string.Empty,
            "Technical" => implementation.TechnicalNarrative ?? string.Empty,
            _ => string.Empty,
        };
        var stale = proposal.NarrativeType is not ("Policy" or "Technical")
            || proposal.Status is "Draft" or "PendingGeneration" or "GenerationFailed"
                && (proposal.BaseVersion != implementation.CurrentVersion || proposal.BeforeContent != current)
            || proposal.Status == "Approved" && proposal.ProposedContent != current;
        return new(
            proposal.Status == "Draft" && !stale,
            stale,
            ParseStringList(proposal.ConflictsJson).Count > 0,
            ParseStringList(proposal.MissingEvidenceJson).Count > 0);
    }

    private static NarrativeWorkspacePermissions Permissions(SystemWorkspacePermissions permissions) =>
        new(
            permissions.CanAuthorNarratives,
            permissions.CanAuthorNarratives ? null : "Narrative author permission is required.",
            permissions.CanReviewNarratives,
            permissions.CanReviewNarratives ? null : "Narrative review permission is required.",
            permissions.CanManageEvidence,
            permissions.CanManageEvidence ? null : "Evidence management permission is required.");

    private static NarrativeContentSnapshot? ParseSnapshot(NarrativeVersion? version) =>
        version?.SnapshotJson is { Length: > 0 } json
            ? JsonSerializer.Deserialize<NarrativeContentSnapshot>(json)
                ?? throw new JsonException($"Narrative version '{version.Id}' has an invalid snapshot.")
            : null;

    private static JsonElement ParseJson(string json, JsonValueKind requiredKind)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != requiredKind)
            throw new JsonException($"Expected stored narrative metadata to be a {requiredKind}.");
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<string> ParseStringList(string json) =>
        JsonSerializer.Deserialize<string[]>(json)
        ?? throw new JsonException("Stored narrative metadata must be a string array.");

    private static bool HasContent(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string DisplayState(string? current, string? approved, SspSectionStatus lifecycle) =>
        !HasContent(current) ? "Missing"
        : lifecycle == SspSectionStatus.Approved && HasContent(approved) ? "Approved"
        : lifecycle.ToString();

    private static string SourceFreshness(IEnumerable<NarrativeProposal> proposals, bool stale)
    {
        var materialized = proposals.ToArray();
        return stale ? "Stale" : materialized.Length > 0 ? "Current" : "NoProposal";
    }

    private static string ControlFamily(string controlId) =>
        controlId.Split('-', StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();

    private static string CatalogControlId(string controlId)
    {
        var enhancementStart = controlId.IndexOf('(');
        return enhancementStart > 0 && controlId.EndsWith(')')
            ? $"{controlId[..enhancementStart]}.{controlId[(enhancementStart + 1)..^1]}"
            : controlId;
    }

    private sealed record NarrativeWorkspaceResponse(
        string SystemId,
        NarrativeWorkspaceCounts Counts,
        IReadOnlyList<NarrativeWorkspaceRow> Items,
        NarrativeWorkspaceListPermissions Permissions);

    private sealed record NarrativeWorkspaceCounts(
        int NeedsAttention,
        int AllControls,
        int ApprovedStatements,
        int ProposedUpdates);

    private sealed record NarrativeWorkspaceRow(
        string Id,
        string ControlId,
        string ControlTitle,
        string Family,
        string ImplementationStatus,
        string ApprovalStatus,
        int CurrentVersion,
        NarrativeStatementState Policy,
        NarrativeStatementState Technical,
        string NextAction,
        string NextActionLabel,
        string? NextActionReason,
        [property: JsonIgnore] bool NeedsAttention,
        [property: JsonIgnore] bool HasApprovedStatements,
        [property: JsonIgnore] bool HasProposedUpdate);

    private sealed record NarrativeStatementState(
        string State,
        bool HasContent,
        bool HasApprovedContent,
        Guid? ProposalId,
        string? ProposalStatus,
        bool IsStale);

    private sealed record NarrativeWorkspaceDetail(
        string SystemId,
        string Id,
        string ControlId,
        string ControlTitle,
        string Family,
        string ImplementationStatus,
        string ApprovalStatus,
        int CurrentVersion,
        NarrativeStatements Statements,
        ApprovedNarrativeSnapshot? ApprovedSnapshot,
        IReadOnlyList<NarrativeWorkspaceProposal> Proposals,
        IReadOnlyList<NarrativeEvidence> Evidence,
        IReadOnlyList<NarrativeResponsibility> Responsibilities,
        IReadOnlyList<NarrativeHistoryEntry> History,
        NarrativeWorkspacePermissions Permissions);

    private sealed record NarrativeStatements(NarrativeStatementDetail Policy, NarrativeStatementDetail Technical);

    private sealed record NarrativeStatementDetail(
        string Type,
        string CurrentContent,
        string ApprovedContent,
        string State,
        bool HasCurrentContent,
        bool HasApprovedContent,
        string LastModifiedBy,
        DateTime LastModifiedAt,
        string? ApprovedVersionId,
        int? ApprovedVersionNumber,
        string SourceFreshness,
        bool AiSuggested,
        bool IsAutoPopulated,
        bool IsManuallyCustomized);

    private sealed record ApprovedNarrativeSnapshot(
        string VersionId,
        int VersionNumber,
        string PolicyContent,
        string TechnicalContent,
        string AuthoredBy,
        DateTime AuthoredAt,
        string? ApprovedBy,
        DateTime? ApprovedAt);

    private sealed record NarrativeWorkspaceProposal(
        Guid Id,
        string NarrativeType,
        string BeforeContent,
        string ProposedContent,
        string Status,
        int Revision,
        int BaseVersion,
        bool IsStale,
        string Cause,
        string? SourceId,
        JsonElement Provenance,
        IReadOnlyList<string> Conflicts,
        IReadOnlyList<string> MissingEvidence,
        DateTime CreatedAt,
        string CreatedBy,
        bool CanReview,
        string? ReviewReason);

    private sealed record NarrativeEvidence(
        string Id,
        string Type,
        string Target,
        string? Description,
        string AddedBy,
        DateTime AddedAt,
        DateTime? ValidatedAt,
        bool IsAutomated);

    private sealed record NarrativeResponsibility(
        string Id,
        string InheritanceType,
        string? Provider,
        string? CustomerResponsibility,
        string? Source,
        string SetBy,
        DateTime SetAt);

    private sealed record NarrativeHistoryEntry(
        string VersionId,
        int VersionNumber,
        string Status,
        string Content,
        string? PolicyContent,
        string? TechnicalContent,
        string AuthoredBy,
        DateTime AuthoredAt,
        string? ChangeReason,
        IReadOnlyList<NarrativeHistoryReview> Reviews);

    private sealed record NarrativeHistoryReview(
        string Id,
        string Decision,
        string ReviewedBy,
        DateTime ReviewedAt,
        string? Comments);

    private sealed record NarrativeWorkspacePermissions(
        bool CanAuthor,
        string? AuthorReason,
        bool CanReview,
        string? ReviewReason,
        bool CanManageEvidence,
        string? EvidenceReason);

    private sealed record NarrativeWorkspaceListPermissions(
        bool CanAuthor,
        bool CanReview,
        bool CanManageEvidence);

    private sealed record NarrativeNextAction(string Action, string Label, string? Reason);

    private sealed record ProposalProjectionState(
        bool IsActionable,
        bool IsStale,
        bool HasConflicts,
        bool HasMissingEvidence);
}

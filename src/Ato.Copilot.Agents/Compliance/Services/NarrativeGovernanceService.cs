using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements narrative governance: version history, diffing, rollback,
/// submission, review, batch operations, and approval progress tracking.
/// </summary>
/// <remarks>Feature 024 – Narrative Governance.</remarks>
public class NarrativeGovernanceService : INarrativeGovernanceService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NarrativeGovernanceService> _logger;

    public NarrativeGovernanceService(
        IServiceScopeFactory scopeFactory,
        ILogger<NarrativeGovernanceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ─── US1: Version History ────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<(List<NarrativeVersion> Versions, int TotalCount)> GetNarrativeHistoryAsync(
        string systemId,
        string controlId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var impl = await GetControlImplementationAsync(db, systemId, controlId, cancellationToken);

        var query = db.NarrativeVersions
            .Where(v => v.ControlImplementationId == impl.Id)
            .OrderByDescending(v => v.VersionNumber);

        var totalCount = await query.CountAsync(cancellationToken);

        var versions = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (versions, totalCount);
    }

    /// <inheritdoc />
    public async Task<NarrativeDiff> GetNarrativeDiffAsync(
        string systemId,
        string controlId,
        int fromVersion,
        int toVersion,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var impl = await GetControlImplementationAsync(db, systemId, controlId, cancellationToken);

        var fromV = await db.NarrativeVersions
            .FirstOrDefaultAsync(v => v.ControlImplementationId == impl.Id && v.VersionNumber == fromVersion, cancellationToken)
            ?? throw new InvalidOperationException($"VERSION_NOT_FOUND: Version {fromVersion} not found for control '{controlId}'.");

        var toV = await db.NarrativeVersions
            .FirstOrDefaultAsync(v => v.ControlImplementationId == impl.Id && v.VersionNumber == toVersion, cancellationToken)
            ?? throw new InvalidOperationException($"VERSION_NOT_FOUND: Version {toVersion} not found for control '{controlId}'.");

        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diffResult = diffBuilder.BuildDiffModel(fromV.Content, toV.Content);

        var linesAdded = 0;
        var linesRemoved = 0;
        var diffLines = new System.Text.StringBuilder();
        diffLines.AppendLine($"--- Version {fromVersion}");
        diffLines.AppendLine($"+++ Version {toVersion}");

        foreach (var line in diffResult.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Inserted:
                    diffLines.AppendLine($"+{line.Text}");
                    linesAdded++;
                    break;
                case ChangeType.Deleted:
                    diffLines.AppendLine($"-{line.Text}");
                    linesRemoved++;
                    break;
                default:
                    diffLines.AppendLine($" {line.Text}");
                    break;
            }
        }

        return new NarrativeDiff
        {
            FromVersion = fromVersion,
            ToVersion = toVersion,
            UnifiedDiff = diffLines.ToString().TrimEnd(),
            LinesAdded = linesAdded,
            LinesRemoved = linesRemoved
        };
    }

    /// <inheritdoc />
    public async Task<NarrativeVersion> RollbackNarrativeAsync(
        string systemId,
        string controlId,
        int targetVersion,
        string authoredBy = "mcp-user",
        string? changeReason = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var impl = await GetControlImplementationAsync(db, systemId, controlId, cancellationToken);

        // Guard: reject rollback when under review
        if (impl.ApprovalStatus == SspSectionStatus.UnderReview)
            throw new InvalidOperationException(
                $"UNDER_REVIEW: Cannot rollback narrative for control '{controlId}' while it is under review.");

        var target = await db.NarrativeVersions
            .FirstOrDefaultAsync(v => v.ControlImplementationId == impl.Id && v.VersionNumber == targetVersion, cancellationToken)
            ?? throw new InvalidOperationException($"VERSION_NOT_FOUND: Version {targetVersion} not found for control '{controlId}'.");

        // Create new version with the old content (copy-forward)
        var newVersionNumber = impl.CurrentVersion + 1;
        var rollbackVersion = new NarrativeVersion
        {
            ControlImplementationId = impl.Id,
            VersionNumber = newVersionNumber,
            Content = target.Content,
            Status = SspSectionStatus.Draft,
            AuthoredBy = authoredBy,
            AuthoredAt = DateTime.UtcNow,
            ChangeReason = changeReason ?? $"Rolled back to version {targetVersion}"
        };

        db.NarrativeVersions.Add(rollbackVersion);

        // Update ControlImplementation
        impl.CurrentVersion = newVersionNumber;
        impl.SetCombinedNarrative(target.Content);
        impl.ApprovalStatus = SspSectionStatus.Draft;
        impl.ModifiedAt = DateTime.UtcNow;
        impl.AuthoredBy = authoredBy;

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Rolled back narrative for '{ControlId}' in '{SystemId}' from v{From} to v{To} (new version: v{New})",
            controlId, systemId, impl.CurrentVersion - 1, targetVersion, newVersionNumber);

        return rollbackVersion;
    }

    // ─── US2: Approval Workflow ──────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<NarrativeVersion> SubmitNarrativeAsync(
        string systemId,
        string controlId,
        string submittedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var impl = await GetControlImplementationAsync(db, systemId, controlId, cancellationToken);

        if (impl.ApprovalStatus != SspSectionStatus.Draft && impl.ApprovalStatus != SspSectionStatus.NeedsRevision)
            throw new InvalidOperationException(
                $"INVALID_STATUS: Cannot submit narrative for control '{controlId}' — current status is '{impl.ApprovalStatus}'. Only Draft or NeedsRevision narratives can be submitted.");

        // Get the latest version
        var latestVersion = await db.NarrativeVersions
            .Where(v => v.ControlImplementationId == impl.Id && v.VersionNumber == impl.CurrentVersion)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"VERSION_NOT_FOUND: No version {impl.CurrentVersion} found for control '{controlId}'.");

        // Transition to UnderReview
        latestVersion.Status = SspSectionStatus.UnderReview;
        latestVersion.SubmittedBy = submittedBy;
        latestVersion.SubmittedAt = DateTime.UtcNow;

        impl.ApprovalStatus = SspSectionStatus.UnderReview;

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Submitted narrative for '{ControlId}' in '{SystemId}' for review (v{Version})",
            controlId, systemId, latestVersion.VersionNumber);

        return latestVersion;
    }

    /// <inheritdoc />
    public async Task<NarrativeReview> ReviewNarrativeAsync(
        string systemId,
        string controlId,
        ReviewDecision decision,
        string reviewedBy = "mcp-user",
        string? comments = null,
        CancellationToken cancellationToken = default)
    {
        if (decision == ReviewDecision.RequestRevision && string.IsNullOrWhiteSpace(comments))
            throw new InvalidOperationException(
                "COMMENTS_REQUIRED: Reviewer comments are required when requesting revision.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var impl = await GetControlImplementationAsync(db, systemId, controlId, cancellationToken);

        if (impl.ApprovalStatus != SspSectionStatus.UnderReview)
            throw new InvalidOperationException(
                $"INVALID_STATUS: Cannot review narrative for control '{controlId}' — current status is '{impl.ApprovalStatus}'. Only UnderReview narratives can be reviewed.");

        var latestVersion = await db.NarrativeVersions
            .Where(v => v.ControlImplementationId == impl.Id && v.VersionNumber == impl.CurrentVersion)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"VERSION_NOT_FOUND: No version {impl.CurrentVersion} found for control '{controlId}'.");

        // Record review
        var review = new NarrativeReview
        {
            NarrativeVersionId = latestVersion.Id,
            ReviewedBy = reviewedBy,
            Decision = decision,
            ReviewerComments = comments,
            ReviewedAt = DateTime.UtcNow
        };
        db.NarrativeReviews.Add(review);

        if (decision == ReviewDecision.Approve)
        {
            latestVersion.Status = SspSectionStatus.Approved;
            impl.ApprovalStatus = SspSectionStatus.Approved;
            impl.ApprovedVersionId = latestVersion.Id;
            impl.ReviewedBy = reviewedBy;
            impl.ReviewedAt = DateTime.UtcNow;
        }
        else
        {
            latestVersion.Status = SspSectionStatus.NeedsRevision;
            impl.ApprovalStatus = SspSectionStatus.NeedsRevision;
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "{Decision} narrative for '{ControlId}' in '{SystemId}' (v{Version})",
            decision, controlId, systemId, latestVersion.VersionNumber);

        return review;
    }

    /// <inheritdoc />
    public async Task<(List<string> ReviewedControlIds, List<string> SkippedReasons)> BatchReviewNarrativesAsync(
        string systemId,
        ReviewDecision decision,
        string reviewedBy = "mcp-user",
        string? comments = null,
        string? familyFilter = null,
        IEnumerable<string>? controlIds = null,
        CancellationToken cancellationToken = default)
    {
        if (familyFilter != null && controlIds != null)
            throw new InvalidOperationException(
                "MUTUALLY_EXCLUSIVE_FILTERS: Provide either 'family_filter' or 'control_ids', not both.");

        if (decision == ReviewDecision.RequestRevision && string.IsNullOrWhiteSpace(comments))
            throw new InvalidOperationException(
                "COMMENTS_REQUIRED: Reviewer comments are required when requesting revision.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Validate system
        var system = await db.RegisteredSystems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"SYSTEM_NOT_FOUND: System '{systemId}' not found.");

        // Query implementations in UnderReview status
        var query = db.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId && ci.ApprovalStatus == SspSectionStatus.UnderReview);

        if (familyFilter != null)
            query = query.Where(ci => ci.ControlId.StartsWith(familyFilter + "-"));

        if (controlIds != null)
        {
            var controlIdList = controlIds.ToList();
            query = query.Where(ci => controlIdList.Contains(ci.ControlId));
        }

        var implementations = await query.ToListAsync(cancellationToken);
        var reviewed = new List<string>();
        var skipped = new List<string>();

        if (implementations.Count == 0)
            throw new InvalidOperationException(
                "NO_REVIEWABLE_NARRATIVES: No narratives in UnderReview status found matching the filter.");

        foreach (var impl in implementations)
        {
            var latestVersion = await db.NarrativeVersions
                .Where(v => v.ControlImplementationId == impl.Id && v.VersionNumber == impl.CurrentVersion)
                .FirstOrDefaultAsync(cancellationToken);

            if (latestVersion == null)
            {
                skipped.Add($"{impl.ControlId}: No version found");
                continue;
            }

            var review = new NarrativeReview
            {
                NarrativeVersionId = latestVersion.Id,
                ReviewedBy = reviewedBy,
                Decision = decision,
                ReviewerComments = comments,
                ReviewedAt = DateTime.UtcNow
            };
            db.NarrativeReviews.Add(review);

            if (decision == ReviewDecision.Approve)
            {
                latestVersion.Status = SspSectionStatus.Approved;
                impl.ApprovalStatus = SspSectionStatus.Approved;
                impl.ApprovedVersionId = latestVersion.Id;
                impl.ReviewedBy = reviewedBy;
                impl.ReviewedAt = DateTime.UtcNow;
            }
            else
            {
                latestVersion.Status = SspSectionStatus.NeedsRevision;
                impl.ApprovalStatus = SspSectionStatus.NeedsRevision;
            }

            reviewed.Add(impl.ControlId);
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Batch {Decision} {Count} narratives in '{SystemId}' ({Skipped} skipped)",
            decision, reviewed.Count, systemId, skipped.Count);

        return (reviewed, skipped);
    }

    // ─── US3: Approval Progress Dashboard ────────────────────────────────────

    /// <inheritdoc />
    public async Task<GovernanceProgressReport> GetNarrativeApprovalProgressAsync(
        string systemId,
        string? familyFilter = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Validate system exists
        var systemExists = await db.RegisteredSystems
            .AsNoTracking()
            .AnyAsync(s => s.Id == systemId, cancellationToken);

        if (!systemExists)
            throw new InvalidOperationException($"SYSTEM_NOT_FOUND: System '{systemId}' not found.");

        // Query all ControlImplementations for this system
        var query = db.ControlImplementations
            .AsNoTracking()
            .Where(ci => ci.RegisteredSystemId == systemId);

        if (!string.IsNullOrWhiteSpace(familyFilter))
            query = query.Where(ci => ci.ControlId.StartsWith(familyFilter + "-"));

        var implementations = await query
            .Select(ci => new { ci.ControlId, ci.ApprovalStatus, ci.PolicyNarrative, ci.TechnicalNarrative })
            .ToListAsync(cancellationToken);

        // Group by family prefix (text before first hyphen)
        var families = implementations
            .GroupBy(ci => ci.ControlId.Contains('-') ? ci.ControlId[..ci.ControlId.IndexOf('-')] : ci.ControlId)
            .Select(g => new GovernanceFamilyProgress
            {
                Family = g.Key,
                Total = g.Count(),
                Approved = g.Count(ci => ci.ApprovalStatus == SspSectionStatus.Approved),
                UnderReview = g.Count(ci => ci.ApprovalStatus == SspSectionStatus.UnderReview),
                Draft = g.Count(ci => ci.ApprovalStatus == SspSectionStatus.Draft),
                NeedsRevision = g.Count(ci => ci.ApprovalStatus == SspSectionStatus.NeedsRevision),
                NotStarted = g.Count(ci => ci.ApprovalStatus == SspSectionStatus.NotStarted)
            })
            .OrderBy(f => f.Family)
            .ToList();

        // Review queue: controls currently UnderReview
        var reviewQueue = implementations
            .Where(ci => ci.ApprovalStatus == SspSectionStatus.UnderReview)
            .Select(ci => ci.ControlId)
            .OrderBy(id => id)
            .ToList();

        // Staleness warnings: controls not yet approved with non-null narrative content
        // In a system-wide context, these are Draft/NeedsRevision narratives that have content but aren't approved
        var stalenessWarnings = implementations
            .Where(ci => ci.ApprovalStatus is SspSectionStatus.Draft or SspSectionStatus.NeedsRevision
                         && (!string.IsNullOrWhiteSpace(ci.PolicyNarrative)
                             || !string.IsNullOrWhiteSpace(ci.TechnicalNarrative)))
            .Select(ci => new StalenessWarning
            {
                ControlId = ci.ControlId,
                Message = $"Unapproved {ci.ApprovalStatus} narrative exists — consider submitting for review"
            })
            .OrderBy(w => w.ControlId)
            .ToList();

        var totalControls = implementations.Count;
        var totalApproved = families.Sum(f => f.Approved);

        return new GovernanceProgressReport
        {
            SystemId = systemId,
            TotalControls = totalControls,
            TotalApproved = totalApproved,
            TotalUnderReview = families.Sum(f => f.UnderReview),
            TotalDraft = families.Sum(f => f.Draft),
            TotalNeedsRevision = families.Sum(f => f.NeedsRevision),
            TotalNotStarted = families.Sum(f => f.NotStarted),
            OverallApprovalPercent = totalControls > 0 ? Math.Round(100.0 * totalApproved / totalControls, 1) : 0,
            FamilyBreakdowns = families,
            ReviewQueue = reviewQueue,
            StalenessWarnings = stalenessWarnings
        };
    }

    // ─── US4: Batch Submit ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<BatchSubmitResult> BatchSubmitNarrativesAsync(
        string systemId,
        string? familyFilter = null,
        string submittedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Validate system exists
        var systemExists = await db.RegisteredSystems
            .AsNoTracking()
            .AnyAsync(s => s.Id == systemId, cancellationToken);

        if (!systemExists)
            throw new InvalidOperationException($"SYSTEM_NOT_FOUND: System '{systemId}' not found.");

        // Query ControlImplementations — only those in Draft or NeedsRevision status
        var query = db.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId);

        if (!string.IsNullOrWhiteSpace(familyFilter))
            query = query.Where(ci => ci.ControlId.StartsWith(familyFilter + "-"));

        var implementations = await query.ToListAsync(cancellationToken);

        var submitted = new List<string>();
        var skipped = new List<string>();
        var now = DateTime.UtcNow;

        foreach (var impl in implementations)
        {
            if (impl.ApprovalStatus is SspSectionStatus.Draft or SspSectionStatus.NeedsRevision)
            {
                impl.ApprovalStatus = SspSectionStatus.UnderReview;

                // Update the latest NarrativeVersion
                var latestVersion = await db.Set<NarrativeVersion>()
                    .Where(nv => nv.ControlImplementationId == impl.Id)
                    .OrderByDescending(nv => nv.VersionNumber)
                    .FirstOrDefaultAsync(cancellationToken);

                if (latestVersion != null)
                {
                    latestVersion.Status = SspSectionStatus.UnderReview;
                    latestVersion.SubmittedBy = submittedBy;
                    latestVersion.SubmittedAt = now;
                }

                submitted.Add(impl.ControlId);
            }
            else
            {
                skipped.Add(impl.ControlId);
            }
        }

        if (submitted.Count == 0)
            throw new InvalidOperationException("NO_DRAFT_NARRATIVES: No Draft narratives found matching the filter.");

        await db.SaveChangesAsync(cancellationToken);

        return new BatchSubmitResult
        {
            SubmittedCount = submitted.Count,
            SkippedCount = skipped.Count,
            SubmittedControlIds = submitted,
            SkippedReasons = skipped
        };
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────

    /// <summary>Resolve a ControlImplementation by (systemId, controlId) or throw.</summary>
    private async Task<ControlImplementation> GetControlImplementationAsync(
        AtoCopilotContext db,
        string systemId,
        string controlId,
        CancellationToken cancellationToken)
    {
        var system = await db.RegisteredSystems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"SYSTEM_NOT_FOUND: System '{systemId}' not found.");

        var impl = await db.ControlImplementations
            .FirstOrDefaultAsync(ci => ci.RegisteredSystemId == systemId && ci.ControlId == controlId, cancellationToken)
            ?? throw new InvalidOperationException($"CONTROL_NOT_FOUND: No narrative exists for control '{controlId}' in system '{systemId}'.");

        return impl;
    }
}

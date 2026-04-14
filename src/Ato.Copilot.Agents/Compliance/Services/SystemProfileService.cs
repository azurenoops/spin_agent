using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements system profile section management: CRUD, governance workflow,
/// completeness metrics, business-context drafts, and profile audit trail.
/// </summary>
/// <remarks>Feature 046 – Mission System Details.</remarks>
public class SystemProfileService : ISystemProfileService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SystemProfileService> _logger;

    /// <summary>The 5 mandatory section types that count toward completeness.</summary>
    private static readonly ProfileSectionType[] MandatorySections =
    [
        ProfileSectionType.MissionAndPurpose,
        ProfileSectionType.UsersAndAccess,
        ProfileSectionType.EnvironmentAndDeployment,
        ProfileSectionType.DataTypes,
        ProfileSectionType.PortsProtocolsAndServices
    ];

    /// <summary>All 6 section types including optional.</summary>
    private static readonly ProfileSectionType[] AllSectionTypes =
        Enum.GetValues<ProfileSectionType>();

    /// <summary>Roles permitted to save draft content (FR-016).</summary>
    private static readonly RmfRole[] SaveRoles =
        [RmfRole.MissionOwner, RmfRole.SystemOwner, RmfRole.Issm];

    public SystemProfileService(
        IServiceScopeFactory scopeFactory,
        ILogger<SystemProfileService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ─── Profile Overview & Section Detail ────────────────────────────────

    /// <inheritdoc />
    public async Task<ProfileOverviewResult> GetProfileOverviewAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, cancellationToken)
            ?? throw new InvalidOperationException($"SYSTEM_NOT_FOUND: System '{systemId}' not found.");

        var sections = await db.SystemProfileSections
            .Where(s => s.RegisteredSystemId == systemId)
            .ToListAsync(cancellationToken);

        var sectionMap = sections.ToDictionary(s => s.SectionType);

        // Synthesize NotStarted for missing section types (R10)
        var summaries = AllSectionTypes.Select(type =>
        {
            var exists = sectionMap.TryGetValue(type, out var section);
            return new SectionSummary
            {
                SectionType = type,
                GovernanceStatus = exists ? section!.GovernanceStatus : SspSectionStatus.NotStarted,
                CompletionPercentage = exists ? section!.CompletionPercentage : 0,
                LastEditedBy = exists ? section!.LastEditedBy : null,
                LastEditedAt = exists ? section!.LastEditedAt : null,
                ReviewerComments = exists ? section!.ReviewerComments : null
            };
        }).ToList();

        // Mission Owner info
        var moAssignment = await db.RmfRoleAssignments
            .FirstOrDefaultAsync(r => r.RegisteredSystemId == systemId
                && r.RmfRole == RmfRole.MissionOwner
                && r.IsActive, cancellationToken);

        var approvedCount = summaries.Count(s => MandatorySections.Contains(s.SectionType)
            && s.GovernanceStatus == SspSectionStatus.Approved);

        return new ProfileOverviewResult
        {
            SystemId = systemId,
            SystemName = system.Name,
            MissionOwner = moAssignment != null
                ? new MissionOwnerInfo
                {
                    UserId = moAssignment.UserId,
                    DisplayName = moAssignment.UserDisplayName ?? moAssignment.UserId
                }
                : null,
            OverallCompleteness = new OverallCompleteness
            {
                CompletedCount = summaries.Count(s => s.GovernanceStatus != SspSectionStatus.NotStarted),
                ApprovedCount = approvedCount,
                ApprovedPercentage = MandatorySections.Length > 0
                    ? approvedCount * 100 / MandatorySections.Length
                    : 0
            },
            Sections = summaries
        };
    }

    /// <inheritdoc />
    public async Task<SystemProfileSection?> GetSectionDetailAsync(
        string systemId,
        ProfileSectionType sectionType,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        return await db.SystemProfileSections
            .Include(s => s.UserCategories.OrderBy(c => c.SortOrder))
            .Include(s => s.DataTypeEntries.OrderBy(d => d.SortOrder))
            .Include(s => s.PpsEntries.OrderBy(p => p.SortOrder))
            .Include(s => s.LeveragedAuthorizations.OrderBy(l => l.SortOrder))
            .FirstOrDefaultAsync(s => s.RegisteredSystemId == systemId
                && s.SectionType == sectionType, cancellationToken);
    }

    // ─── Draft Save ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<SystemProfileSection> SaveDraftAsync(
        string systemId,
        ProfileSectionType sectionType,
        string? draftContent,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        await EnsureSystemExistsAsync(db, systemId, cancellationToken);
        await RequireRoleAsync(db, systemId, userId, SaveRoles, cancellationToken);

        var section = await db.SystemProfileSections
            .FirstOrDefaultAsync(s => s.RegisteredSystemId == systemId
                && s.SectionType == sectionType, cancellationToken);

        if (section == null)
        {
            // First save creates the record (R10)
            section = new SystemProfileSection
            {
                RegisteredSystemId = systemId,
                SectionType = sectionType,
                GovernanceStatus = SspSectionStatus.Draft,
                DraftContent = draftContent,
                LastEditedBy = userId,
                LastEditedAt = DateTime.UtcNow
            };
            db.SystemProfileSections.Add(section);

            AddAuditEntry(db, section, "Drafted", userId, null, SspSectionStatus.Draft);
        }
        else
        {
            if (section.GovernanceStatus == SspSectionStatus.UnderReview)
                throw new InvalidOperationException(
                    $"INVALID_STATUS: Cannot edit section '{sectionType}' — it is currently under review.");

            var previousStatus = section.GovernanceStatus;

            // Re-edit of approved section → transition to Draft, preserve ApprovedContent (R2)
            if (section.GovernanceStatus == SspSectionStatus.Approved)
            {
                section.ApprovedContent ??= section.DraftContent;
                section.GovernanceStatus = SspSectionStatus.Draft;
                AddAuditEntry(db, section, "Drafted", userId, previousStatus, SspSectionStatus.Draft);
            }

            section.DraftContent = draftContent;
            section.LastEditedBy = userId;
            section.LastEditedAt = DateTime.UtcNow;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(
                "CONCURRENCY_CONFLICT: The section was modified by another user. Please refresh and try again.");
        }

        _logger.LogInformation(
            "Saved draft for section '{SectionType}' on system '{SystemId}' by '{UserId}'",
            sectionType, systemId, userId);

        return section;
    }

    // ─── Governance Workflow ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<SubmitResult> SubmitForReviewAsync(
        string systemId,
        IEnumerable<ProfileSectionType>? sectionTypes,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        await EnsureSystemExistsAsync(db, systemId, cancellationToken);
        await RequireRoleAsync(db, systemId, userId, [RmfRole.MissionOwner], cancellationToken);

        var sections = await db.SystemProfileSections
            .Where(s => s.RegisteredSystemId == systemId)
            .ToListAsync(cancellationToken);

        var targets = sectionTypes != null
            ? sections.Where(s => sectionTypes.Contains(s.SectionType)).ToList()
            : sections;

        var submitted = new List<ProfileSectionType>();
        var skipped = new List<SkippedSection>();

        foreach (var section in targets)
        {
            if (section.GovernanceStatus is SspSectionStatus.Draft or SspSectionStatus.NeedsRevision)
            {
                var prev = section.GovernanceStatus;
                section.GovernanceStatus = SspSectionStatus.UnderReview;
                section.SubmittedBy = userId;
                section.SubmittedAt = DateTime.UtcNow;
                submitted.Add(section.SectionType);
                AddAuditEntry(db, section, "Submitted", userId, prev, SspSectionStatus.UnderReview);
            }
            else
            {
                skipped.Add(new SkippedSection
                {
                    SectionType = section.SectionType,
                    Reason = $"Current status '{section.GovernanceStatus}' is not submittable (must be Draft or NeedsRevision)."
                });
            }
        }

        if (submitted.Count == 0 && targets.Any())
            throw new InvalidOperationException(
                "NO_SUBMITTABLE_SECTIONS: No sections are in Draft or NeedsRevision status.");

        if (submitted.Count == 0 && !targets.Any())
            throw new InvalidOperationException(
                "NO_SUBMITTABLE_SECTIONS: No profile sections exist for this system.");

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Submitted {Count} section(s) for review on system '{SystemId}' by '{UserId}'",
            submitted.Count, systemId, userId);

        return new SubmitResult
        {
            SubmittedSections = submitted,
            SkippedSections = skipped,
            SubmittedBy = userId,
            SubmittedAt = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task<WithdrawResult> WithdrawSectionAsync(
        string systemId,
        IEnumerable<ProfileSectionType>? sectionTypes,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        await EnsureSystemExistsAsync(db, systemId, cancellationToken);
        await RequireRoleAsync(db, systemId, userId, [RmfRole.MissionOwner], cancellationToken);

        var sections = await db.SystemProfileSections
            .Where(s => s.RegisteredSystemId == systemId)
            .ToListAsync(cancellationToken);

        var targets = sectionTypes != null
            ? sections.Where(s => sectionTypes.Contains(s.SectionType)).ToList()
            : sections;

        var withdrawn = new List<ProfileSectionType>();
        var skipped = new List<SkippedSection>();

        foreach (var section in targets)
        {
            if (section.GovernanceStatus == SspSectionStatus.UnderReview)
            {
                section.GovernanceStatus = SspSectionStatus.Draft;
                withdrawn.Add(section.SectionType);
                AddAuditEntry(db, section, "Withdrawn", userId, SspSectionStatus.UnderReview, SspSectionStatus.Draft);
            }
            else
            {
                skipped.Add(new SkippedSection
                {
                    SectionType = section.SectionType,
                    Reason = $"Current status '{section.GovernanceStatus}' is not withdrawable (must be UnderReview)."
                });
            }
        }

        if (withdrawn.Count == 0 && targets.Any())
            throw new InvalidOperationException(
                "NO_WITHDRAWABLE_SECTIONS: No sections are currently under review.");

        if (withdrawn.Count == 0 && !targets.Any())
            throw new InvalidOperationException(
                "NO_WITHDRAWABLE_SECTIONS: No profile sections exist for this system.");

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Withdrew {Count} section(s) from review on system '{SystemId}' by '{UserId}'",
            withdrawn.Count, systemId, userId);

        return new WithdrawResult
        {
            WithdrawnSections = withdrawn,
            SkippedSections = skipped,
            WithdrawnBy = userId,
            WithdrawnAt = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task<SystemProfileSection> ReviewSectionAsync(
        string systemId,
        ProfileSectionType sectionType,
        ReviewDecision decision,
        string reviewerId,
        string? comments = null,
        CancellationToken cancellationToken = default)
    {
        if (decision == ReviewDecision.RequestRevision && string.IsNullOrWhiteSpace(comments))
            throw new InvalidOperationException(
                "COMMENTS_REQUIRED: Reviewer comments are required when requesting revision.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        await EnsureSystemExistsAsync(db, systemId, cancellationToken);
        await RequireRoleAsync(db, systemId, reviewerId, [RmfRole.Issm], cancellationToken);

        var section = await db.SystemProfileSections
            .FirstOrDefaultAsync(s => s.RegisteredSystemId == systemId
                && s.SectionType == sectionType, cancellationToken)
            ?? throw new InvalidOperationException(
                $"INVALID_STATUS: No profile section '{sectionType}' found for system '{systemId}'.");

        if (section.GovernanceStatus != SspSectionStatus.UnderReview)
            throw new InvalidOperationException(
                $"INVALID_STATUS: Cannot review section '{sectionType}' — current status is '{section.GovernanceStatus}'. Only UnderReview sections can be reviewed.");

        var previousStatus = section.GovernanceStatus;

        if (decision == ReviewDecision.Approve)
        {
            section.GovernanceStatus = SspSectionStatus.Approved;
            section.ApprovedContent = section.DraftContent;
            section.ReviewedBy = reviewerId;
            section.ReviewedAt = DateTime.UtcNow;
            section.ReviewerComments = null;
            AddAuditEntry(db, section, "Approved", reviewerId, previousStatus, SspSectionStatus.Approved, comments);
        }
        else
        {
            section.GovernanceStatus = SspSectionStatus.NeedsRevision;
            section.ReviewedBy = reviewerId;
            section.ReviewedAt = DateTime.UtcNow;
            section.ReviewerComments = comments;
            AddAuditEntry(db, section, "RevisionRequested", reviewerId, previousStatus, SspSectionStatus.NeedsRevision, comments);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(
                "CONCURRENCY_CONFLICT: The section was modified by another user. Please refresh and try again.");
        }

        _logger.LogInformation(
            "Reviewed section '{SectionType}' on system '{SystemId}' — decision: {Decision} by '{ReviewerId}'",
            sectionType, systemId, decision, reviewerId);

        return section;
    }

    /// <inheritdoc />
    public async Task<BatchApproveResult> BatchApproveSectionsAsync(
        string systemId,
        string reviewerId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        await EnsureSystemExistsAsync(db, systemId, cancellationToken);
        await RequireRoleAsync(db, systemId, reviewerId, [RmfRole.Issm], cancellationToken);

        var sections = await db.SystemProfileSections
            .Where(s => s.RegisteredSystemId == systemId
                && s.GovernanceStatus == SspSectionStatus.UnderReview)
            .ToListAsync(cancellationToken);

        var approved = new List<ProfileSectionType>();
        var skipped = new List<SkippedSection>();

        foreach (var section in sections)
        {
            section.GovernanceStatus = SspSectionStatus.Approved;
            section.ApprovedContent = section.DraftContent;
            section.ReviewedBy = reviewerId;
            section.ReviewedAt = DateTime.UtcNow;
            section.ReviewerComments = null;
            approved.Add(section.SectionType);
            AddAuditEntry(db, section, "Approved", reviewerId, SspSectionStatus.UnderReview, SspSectionStatus.Approved);
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Batch-approved {Count} section(s) on system '{SystemId}' by '{ReviewerId}'",
            approved.Count, systemId, reviewerId);

        return new BatchApproveResult
        {
            ApprovedSections = approved,
            SkippedSections = skipped,
            ApprovedCount = approved.Count,
            ReviewedBy = reviewerId,
            ReviewedAt = DateTime.UtcNow
        };
    }

    // ─── Completeness & Todos ────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ProfileCompletenessResult> GetCompletenessAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, cancellationToken)
            ?? throw new InvalidOperationException($"SYSTEM_NOT_FOUND: System '{systemId}' not found.");

        var sections = await db.SystemProfileSections
            .Where(s => s.RegisteredSystemId == systemId)
            .ToListAsync(cancellationToken);

        var sectionMap = sections.ToDictionary(s => s.SectionType);

        // 5-mandatory denominator (R11) — LeveragedAuthorizations excluded
        var statusCounts = new Dictionary<string, int>();
        var incompleteSections = new List<IncompleteSectionInfo>();
        var approvedCount = 0;

        foreach (var mandatoryType in MandatorySections)
        {
            var status = sectionMap.TryGetValue(mandatoryType, out var s)
                ? s.GovernanceStatus
                : SspSectionStatus.NotStarted;

            var statusKey = status.ToString();
            statusCounts[statusKey] = statusCounts.GetValueOrDefault(statusKey) + 1;

            if (status == SspSectionStatus.Approved)
            {
                approvedCount++;
            }
            else
            {
                incompleteSections.Add(new IncompleteSectionInfo
                {
                    SectionType = mandatoryType,
                    Status = status
                });
            }
        }

        var moAssignment = await db.RmfRoleAssignments
            .FirstOrDefaultAsync(r => r.RegisteredSystemId == systemId
                && r.RmfRole == RmfRole.MissionOwner
                && r.IsActive, cancellationToken);

        return new ProfileCompletenessResult
        {
            SystemId = systemId,
            TotalSections = MandatorySections.Length,
            StatusCounts = statusCounts,
            ApprovedPercentage = MandatorySections.Length > 0
                ? approvedCount * 100 / MandatorySections.Length
                : 0,
            IsProfileComplete = approvedCount == MandatorySections.Length,
            IncompleteSections = incompleteSections,
            MissionOwnerAssigned = moAssignment != null,
            MissionOwnerName = moAssignment?.UserDisplayName ?? moAssignment?.UserId,
            DaysSinceRegistration = (int)(DateTime.UtcNow - system.CreatedAt).TotalDays
        };
    }

    /// <inheritdoc />
    public async Task<ProfileTodosResult> GetProfileTodosAsync(
        string systemId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var sections = await db.SystemProfileSections
            .Where(s => s.RegisteredSystemId == systemId)
            .ToListAsync(cancellationToken);

        var sectionMap = sections.ToDictionary(s => s.SectionType);

        var incompleteSections = new List<ProfileTodoItem>();
        var revisionSections = new List<ProfileTodoItem>();

        foreach (var type in AllSectionTypes)
        {
            var status = sectionMap.TryGetValue(type, out var section)
                ? section.GovernanceStatus
                : SspSectionStatus.NotStarted;

            if (status is SspSectionStatus.NotStarted or SspSectionStatus.Draft)
            {
                incompleteSections.Add(new ProfileTodoItem
                {
                    SectionType = type,
                    Label = FormatSectionLabel(type),
                    Status = status
                });
            }
            else if (status == SspSectionStatus.NeedsRevision)
            {
                revisionSections.Add(new ProfileTodoItem
                {
                    SectionType = type,
                    Label = FormatSectionLabel(type),
                    Status = status,
                    ReviewerComments = section?.ReviewerComments
                });
            }
        }

        // Flagged controls needing business context
        var flaggedControls = await GetFlaggedControlsAsync(systemId, cancellationToken);

        return new ProfileTodosResult
        {
            HasProfileTasks = incompleteSections.Count > 0
                || revisionSections.Count > 0
                || flaggedControls.Count > 0,
            IncompleteSections = incompleteSections,
            RevisionSections = revisionSections,
            FlaggedControls = flaggedControls
        };
    }

    // ─── Cross-System Review Queue (FR-027) ──────────────────────────────

    /// <inheritdoc />
    public async Task<List<PendingReviewItem>> GetPendingReviewsAsync(
        string issmUserId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Find all systems where the caller has ISSM role
        var issmSystemIds = await db.RmfRoleAssignments
            .Where(r => r.UserId == issmUserId
                && r.RmfRole == RmfRole.Issm
                && r.IsActive)
            .Select(r => r.RegisteredSystemId)
            .ToListAsync(cancellationToken);

        if (issmSystemIds.Count == 0)
            return [];

        var pendingSections = await db.SystemProfileSections
            .Where(s => issmSystemIds.Contains(s.RegisteredSystemId)
                && s.GovernanceStatus == SspSectionStatus.UnderReview)
            .Include(s => s.RegisteredSystem)
            .OrderBy(s => s.SubmittedAt)
            .ToListAsync(cancellationToken);

        return pendingSections.Select(s => new PendingReviewItem
        {
            SystemId = s.RegisteredSystemId,
            SystemName = s.RegisteredSystem.Name,
            SectionType = s.SectionType,
            SubmittedBy = s.SubmittedBy ?? "unknown",
            SubmittedAt = s.SubmittedAt ?? DateTime.UtcNow
        }).ToList();
    }

    // ─── Business Context ────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<BusinessContextDraft> SaveBusinessContextAsync(
        string systemId,
        string controlId,
        string content,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        await EnsureSystemExistsAsync(db, systemId, cancellationToken);
        await RequireRoleAsync(db, systemId, userId, [RmfRole.MissionOwner], cancellationToken);

        // Find the ControlImplementation for this system + control
        var impl = await db.ControlImplementations
            .FirstOrDefaultAsync(c => c.RegisteredSystemId == systemId
                && c.ControlId == controlId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"CONTROL_NOT_FOUND: Control '{controlId}' not found for system '{systemId}'.");

        var draft = await db.BusinessContextDrafts
            .FirstOrDefaultAsync(d => d.ControlImplementationId == impl.Id, cancellationToken);

        if (draft == null)
        {
            draft = new BusinessContextDraft
            {
                ControlImplementationId = impl.Id,
                Content = content,
                AuthoredBy = userId,
                AuthoredAt = DateTime.UtcNow
            };
            db.BusinessContextDrafts.Add(draft);
        }
        else
        {
            draft.Content = content;
            draft.AuthoredBy = userId;
            draft.AuthoredAt = DateTime.UtcNow;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(
                "CONCURRENCY_CONFLICT: The draft was modified by another user. Please refresh and try again.");
        }

        _logger.LogInformation(
            "Saved business context for control '{ControlId}' on system '{SystemId}' by '{UserId}'",
            controlId, systemId, userId);

        return draft;
    }

    /// <inheritdoc />
    public async Task<BusinessContextDraft?> GetBusinessContextAsync(
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var impl = await db.ControlImplementations
            .FirstOrDefaultAsync(c => c.RegisteredSystemId == systemId
                && c.ControlId == controlId, cancellationToken);

        if (impl == null) return null;

        return await db.BusinessContextDrafts
            .FirstOrDefaultAsync(d => d.ControlImplementationId == impl.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<FlaggedControlItem>> GetFlaggedControlsAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var flags = await db.BusinessContextControlFlags
            .Where(f => f.RegisteredSystemId == systemId && f.IsFlagged)
            .ToListAsync(cancellationToken);

        // Get all ControlImplementations for the system to check for existing drafts
        var controlIds = flags.Select(f => f.ControlId).ToList();

        var implsWithDrafts = await db.ControlImplementations
            .Where(c => c.RegisteredSystemId == systemId && controlIds.Contains(c.ControlId))
            .Select(c => new
            {
                c.ControlId,
                HasDraft = db.BusinessContextDrafts.Any(d => d.ControlImplementationId == c.Id)
            })
            .ToListAsync(cancellationToken);

        var draftMap = implsWithDrafts.ToDictionary(x => x.ControlId, x => x.HasDraft);

        // Get control titles from NistControls
        var nistControls = await db.NistControls
            .Where(n => controlIds.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, n => n.Title ?? n.Id, cancellationToken);

        return flags.Select(f => new FlaggedControlItem
        {
            ControlId = f.ControlId,
            ControlTitle = nistControls.GetValueOrDefault(f.ControlId, f.ControlId),
            HasDraft = draftMap.GetValueOrDefault(f.ControlId, false)
        }).ToList();
    }

    /// <inheritdoc />
    public async Task SetControlFlagAsync(
        string systemId,
        string controlId,
        bool isFlagged,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        await EnsureSystemExistsAsync(db, systemId, cancellationToken);
        await RequireRoleAsync(db, systemId, userId, [RmfRole.Issm], cancellationToken);

        var flag = await db.BusinessContextControlFlags
            .FirstOrDefaultAsync(f => f.RegisteredSystemId == systemId
                && f.ControlId == controlId, cancellationToken);

        if (flag == null)
        {
            flag = new BusinessContextControlFlag
            {
                RegisteredSystemId = systemId,
                ControlId = controlId,
                IsFlagged = isFlagged,
                FlaggedBy = userId,
                FlaggedAt = DateTime.UtcNow
            };
            db.BusinessContextControlFlags.Add(flag);
        }
        else
        {
            flag.IsFlagged = isFlagged;
            flag.FlaggedBy = userId;
            flag.FlaggedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Set control flag '{ControlId}' on system '{SystemId}' to {IsFlagged} by '{UserId}'",
            controlId, systemId, isFlagged, userId);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static async Task EnsureSystemExistsAsync(
        AtoCopilotContext db,
        string systemId,
        CancellationToken cancellationToken)
    {
        var exists = await db.RegisteredSystems
            .AnyAsync(s => s.Id == systemId && s.IsActive, cancellationToken);

        if (!exists)
            throw new InvalidOperationException($"SYSTEM_NOT_FOUND: System '{systemId}' not found.");
    }

    private static async Task RequireRoleAsync(
        AtoCopilotContext db,
        string systemId,
        string userId,
        RmfRole[] allowedRoles,
        CancellationToken cancellationToken)
    {
        var hasRole = await db.RmfRoleAssignments
            .AnyAsync(r => r.RegisteredSystemId == systemId
                && r.UserId == userId
                && r.IsActive
                && allowedRoles.Contains(r.RmfRole),
                cancellationToken);

        if (!hasRole)
            throw new InvalidOperationException(
                $"UNAUTHORIZED: User '{userId}' does not have a required role ({string.Join(", ", allowedRoles)}) for system '{systemId}'.");
    }

    private static void AddAuditEntry(
        AtoCopilotContext db,
        SystemProfileSection section,
        string action,
        string userId,
        SspSectionStatus? previousStatus,
        SspSectionStatus newStatus,
        string? comments = null)
    {
        db.ProfileAuditEntries.Add(new ProfileAuditEntry
        {
            SystemProfileSectionId = section.Id,
            Action = action,
            PerformedBy = userId,
            PerformedAt = DateTime.UtcNow,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Comments = comments
        });
    }

    private static string FormatSectionLabel(ProfileSectionType type) => type switch
    {
        ProfileSectionType.MissionAndPurpose => "Mission & Purpose",
        ProfileSectionType.UsersAndAccess => "Users & Access",
        ProfileSectionType.EnvironmentAndDeployment => "Environment & Deployment",
        ProfileSectionType.DataTypes => "Data Types & Sensitivity",
        ProfileSectionType.PortsProtocolsAndServices => "Ports, Protocols & Services",
        ProfileSectionType.LeveragedAuthorizations => "Leveraged Authorizations",
        _ => type.ToString()
    };
}

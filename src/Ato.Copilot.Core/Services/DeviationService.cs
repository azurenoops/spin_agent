using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Service for deviation management: creating, reviewing, revoking, extending,
/// listing, and querying compliance deviations.
/// </summary>
public class DeviationService : IDeviationService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly ILogger<DeviationService> _logger;
    private readonly INotificationBroadcaster? _broadcaster;

    /// <summary>Initializes a new instance of <see cref="DeviationService"/>.</summary>
    public DeviationService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILogger<DeviationService> logger,
        INotificationBroadcaster? broadcaster = null)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _broadcaster = broadcaster;
    }

    // ─── CRUD ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<Deviation> CreateDeviationAsync(
        string systemId,
        CreateDeviationRequest request,
        string requestedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Validate system exists
        var system = await _db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // Parse enums
        if (!Enum.TryParse<DeviationType>(request.DeviationType, ignoreCase: true, out var deviationType))
            throw new ArgumentException($"Invalid DeviationType: '{request.DeviationType}'.");

        if (!Enum.TryParse<CatSeverity>(request.CatSeverity, ignoreCase: true, out var catSeverity))
            throw new ArgumentException($"Invalid CatSeverity: '{request.CatSeverity}'.");

        // Validate review cycle
        if (!DeviationConstants.ValidReviewCycles.Contains(request.ReviewCycle))
            throw new ArgumentException($"Invalid ReviewCycle: '{request.ReviewCycle}'. Must be one of: {string.Join(", ", DeviationConstants.ValidReviewCycles)}.");

        // Validate expiration date
        if (request.ExpirationDate <= DateTime.UtcNow)
            throw new ArgumentException("ExpirationDate must be in the future.");

        if ((request.ExpirationDate - DateTime.UtcNow).TotalDays > DeviationConstants.MaxReviewCycleDays)
            throw new ArgumentException($"ExpirationDate cannot exceed {DeviationConstants.MaxReviewCycleDays} days from today.");

        // Check for duplicate active deviation on same finding
        if (!string.IsNullOrEmpty(request.FindingId))
        {
            var existingActive = await _db.Deviations
                .AnyAsync(d => d.FindingId == request.FindingId
                    && (d.Status == DeviationStatus.Pending || d.Status == DeviationStatus.Approved),
                    cancellationToken);

            if (existingActive)
                throw new InvalidOperationException("DUPLICATE_DEVIATION: An active deviation already exists for this finding.");
        }

        var deviation = new Deviation
        {
            RegisteredSystemId = systemId,
            DeviationType = deviationType,
            Status = DeviationStatus.Pending,
            ControlId = request.ControlId,
            CatSeverity = catSeverity,
            Justification = request.Justification,
            CompensatingControls = request.CompensatingControls,
            EvidenceReferences = request.EvidenceIds != null
                ? JsonSerializer.Serialize(request.EvidenceIds)
                : "[]",
            ExpirationDate = request.ExpirationDate,
            ReviewCycle = request.ReviewCycle,
            FindingId = request.FindingId,
            PoamEntryId = request.PoamEntryId,
            BoundaryDefinitionId = request.BoundaryDefinitionId,
            RequestedBy = requestedBy,
            RequestedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Deviations.Add(deviation);

        // Link finding and POA&M back to deviation
        if (!string.IsNullOrEmpty(request.FindingId))
        {
            var finding = await _db.Findings
                .FirstOrDefaultAsync(f => f.Id == request.FindingId, cancellationToken);
            if (finding != null)
                finding.DeviationId = deviation.Id;
        }

        if (!string.IsNullOrEmpty(request.PoamEntryId))
        {
            var poam = await _db.PoamItems
                .FirstOrDefaultAsync(p => p.Id == request.PoamEntryId, cancellationToken);
            if (poam != null)
                poam.DeviationId = deviation.Id;
        }

        // Audit trail
        _db.DashboardActivities.Add(new DashboardActivity
        {
            RegisteredSystemId = systemId,
            EventType = "DeviationCreated",
            Actor = requestedBy,
            Summary = $"Deviation requested for {request.ControlId} ({deviationType}, {catSeverity})",
            RelatedEntityType = "Deviation",
            RelatedEntityId = deviation.Id,
        });

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deviation {DeviationId} created for system {SystemId}, control {ControlId}, type {Type}",
            deviation.Id, systemId, request.ControlId, deviationType);

        // Notify reviewers about new deviation request
        await NotifyAsync(requestedBy,
            $"Deviation Request Submitted: {request.ControlId}",
            $"Your {deviationType} deviation request for {request.ControlId} has been submitted and is pending review.",
            cancellationToken);

        return deviation;
    }

    /// <inheritdoc />
    public async Task<Deviation?> GetDeviationAsync(
        string deviationId,
        CancellationToken cancellationToken = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await _db.Deviations
            .Include(d => d.Finding)
            .Include(d => d.PoamEntry)
            .Include(d => d.BoundaryDefinition)
            .FirstOrDefaultAsync(d => d.Id == deviationId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DeviationDetail?> GetDeviationDetailAsync(
        string deviationId,
        CancellationToken cancellationToken = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var deviation = await _db.Deviations
            .Include(d => d.Finding)
            .Include(d => d.PoamEntry)
            .Include(d => d.BoundaryDefinition)
            .FirstOrDefaultAsync(d => d.Id == deviationId, cancellationToken);

        if (deviation == null) return null;

        var evidenceIds = ParseEvidenceReferences(deviation.EvidenceReferences);

        // Hydrate evidence details
        var evidence = new List<DeviationEvidenceRef>();
        if (evidenceIds.Count > 0)
        {
            var scanRecords = await _db.ScanImportRecords
                .Where(s => evidenceIds.Contains(s.Id))
                .ToListAsync(cancellationToken);

            evidence = scanRecords.Select(s => new DeviationEvidenceRef
            {
                ScanImportRecordId = s.Id,
                FileName = s.FileName,
                ScanType = s.ImportType.ToString(),
                ScanDate = s.ScanTimestamp,
                BenchmarkTitle = s.BenchmarkTitle,
            }).ToList();
        }

        // Audit trail
        var auditTrail = await _db.DashboardActivities
            .Where(a => a.RelatedEntityType == "Deviation" && a.RelatedEntityId == deviationId)
            .OrderBy(a => a.Timestamp)
            .Select(a => new DeviationAuditEntry
            {
                EventType = a.EventType,
                Actor = a.Actor,
                Timestamp = a.Timestamp,
                Summary = a.Summary,
            })
            .ToListAsync(cancellationToken);

        return new DeviationDetail
        {
            Id = deviation.Id,
            DeviationType = deviation.DeviationType.ToString(),
            ControlId = deviation.ControlId,
            CatSeverity = (int)deviation.CatSeverity,
            Status = deviation.Status.ToString(),
            Justification = deviation.Justification,
            CompensatingControls = deviation.CompensatingControls,
            EvidenceReferences = evidenceIds,
            ExpirationDate = deviation.ExpirationDate,
            ReviewCycle = deviation.ReviewCycle,
            RequestedBy = deviation.RequestedBy,
            RequestedAt = deviation.RequestedAt,
            ReviewedBy = deviation.ReviewedBy,
            ReviewedAt = deviation.ReviewedAt,
            ReviewerRole = deviation.ReviewerRole,
            ReviewerComments = deviation.ReviewerComments,
            ISSMRecommendation = deviation.ISSMRecommendation,
            ISSMRecommendedBy = deviation.ISSMRecommendedBy,
            ISSMRecommendedAt = deviation.ISSMRecommendedAt,
            RevokedBy = deviation.RevokedBy,
            RevokedAt = deviation.RevokedAt,
            RevocationReason = deviation.RevocationReason,
            BoundaryDefinitionId = deviation.BoundaryDefinitionId,
            BoundaryDefinitionName = deviation.BoundaryDefinition?.Name,
            Finding = deviation.Finding != null ? new DeviationFindingRef
            {
                Id = deviation.Finding.Id,
                ControlId = deviation.Finding.ControlId,
                Status = deviation.Finding.Status.ToString(),
                Severity = deviation.Finding.CatSeverity?.ToString() ?? deviation.Finding.Severity.ToString(),
            } : null,
            PoamEntry = deviation.PoamEntry != null ? new DeviationPoamRef
            {
                Id = deviation.PoamEntry.Id,
                Weakness = deviation.PoamEntry.Weakness,
                Status = deviation.PoamEntry.Status.ToString(),
            } : null,
            Evidence = evidence,
            AuditTrail = auditTrail,
        };
    }

    // ─── Listing & Summary ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<DeviationListResponse> ListDeviationsAsync(
        string systemId,
        string? typeFilter = null,
        string? statusFilter = null,
        string? severityFilter = null,
        string? search = null,
        int? expiringWithinDays = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = _db.Deviations
            .Where(d => d.RegisteredSystemId == systemId);

        if (!string.IsNullOrEmpty(typeFilter) && Enum.TryParse<DeviationType>(typeFilter, ignoreCase: true, out var dt))
            query = query.Where(d => d.DeviationType == dt);

        if (!string.IsNullOrEmpty(statusFilter) && Enum.TryParse<DeviationStatus>(statusFilter, ignoreCase: true, out var ds))
            query = query.Where(d => d.Status == ds);

        if (!string.IsNullOrEmpty(severityFilter) && Enum.TryParse<CatSeverity>(severityFilter, ignoreCase: true, out var cs))
            query = query.Where(d => d.CatSeverity == cs);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(d => d.ControlId.Contains(search) || d.Justification.Contains(search));

        if (expiringWithinDays.HasValue)
        {
            var cutoff = DateTime.UtcNow.AddDays(expiringWithinDays.Value);
            query = query.Where(d => d.Status == DeviationStatus.Approved && d.ExpirationDate <= cutoff);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var deviationRows = await query
            .OrderByDescending(d => d.RequestedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new
            {
                d.Id,
                DeviationType = d.DeviationType.ToString(),
                d.ControlId,
                CatSeverity = (int)d.CatSeverity,
                Status = d.Status.ToString(),
                d.Justification,
                d.ExpirationDate,
                d.RequestedBy,
                d.RequestedAt,
                d.ReviewedBy,
                d.ReviewedAt,
                d.EvidenceReferences,
                d.FindingId,
                d.PoamEntryId,
                d.BoundaryDefinitionId,
            })
            .ToListAsync(cancellationToken);

        var items = deviationRows.Select(d => new DeviationListItem
        {
            Id = d.Id,
            DeviationType = d.DeviationType,
            ControlId = d.ControlId,
            CatSeverity = d.CatSeverity,
            Status = d.Status,
            Justification = d.Justification,
            ExpirationDate = d.ExpirationDate,
            DaysUntilExpiration = (int)(d.ExpirationDate - DateTime.UtcNow).TotalDays,
            RequestedBy = d.RequestedBy,
            RequestedAt = d.RequestedAt,
            ReviewedBy = d.ReviewedBy,
            ReviewedAt = d.ReviewedAt,
            EvidenceCount = ParseEvidenceReferences(d.EvidenceReferences).Count,
            FindingId = d.FindingId,
            PoamEntryId = d.PoamEntryId,
            BoundaryDefinitionId = d.BoundaryDefinitionId,
        }).ToList();

        return new DeviationListResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <inheritdoc />
    public async Task<DeviationSummary> GetDeviationSummaryAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var deviations = await _db.Deviations
            .Where(d => d.RegisteredSystemId == systemId)
            .Select(d => new
            {
                d.Status,
                d.CatSeverity,
                d.ExpirationDate,
                d.EvidenceReferences,
            })
            .ToListAsync(cancellationToken);

        var thirtyDayCutoff = DateTime.UtcNow.AddDays(30);

        return new DeviationSummary
        {
            Total = deviations.Count,
            Pending = deviations.Count(d => d.Status == DeviationStatus.Pending),
            Approved = deviations.Count(d => d.Status == DeviationStatus.Approved),
            Denied = deviations.Count(d => d.Status == DeviationStatus.Denied),
            Expired = deviations.Count(d => d.Status == DeviationStatus.Expired),
            Revoked = deviations.Count(d => d.Status == DeviationStatus.Revoked),
            ExpiringWithin30d = deviations.Count(d => d.Status == DeviationStatus.Approved && d.ExpirationDate <= thirtyDayCutoff),
            CatI = deviations.Count(d => d.CatSeverity == CatSeverity.CatI),
            CatII = deviations.Count(d => d.CatSeverity == CatSeverity.CatII),
            CatIII = deviations.Count(d => d.CatSeverity == CatSeverity.CatIII),
            WithoutEvidence = deviations.Count(d => d.EvidenceReferences == "[]"),
        };
    }

    // ─── Workflow ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<Deviation> ReviewDeviationAsync(
        string deviationId,
        ReviewDeviationRequest request,
        string reviewedBy = "mcp-user",
        string reviewerRole = "ISSM",
        CancellationToken cancellationToken = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var deviation = await _db.Deviations
            .Include(d => d.Finding)
            .Include(d => d.PoamEntry)
            .FirstOrDefaultAsync(d => d.Id == deviationId, cancellationToken)
            ?? throw new InvalidOperationException($"Deviation '{deviationId}' not found.");

        if (deviation.Status != DeviationStatus.Pending)
            throw new InvalidOperationException("NOT_PENDING: Deviation is not in Pending status.");

        var decision = request.Decision?.Trim();
        if (decision != "Approve" && decision != "Deny")
            throw new ArgumentException("INVALID_DECISION: Decision must be 'Approve' or 'Deny'.");

        // CAT I two-step: ISSM records recommendation, AO renders final decision
        var isCatI = deviation.CatSeverity == CatSeverity.CatI;
        var isIssm = reviewerRole.Equals("ISSM", StringComparison.OrdinalIgnoreCase);

        if (isCatI && isIssm)
        {
            // Record ISSM recommendation; deviation stays Pending
            deviation.ISSMRecommendation = decision;
            deviation.ISSMRecommendedBy = reviewedBy;
            deviation.ISSMRecommendedAt = DateTime.UtcNow;
            deviation.ModifiedAt = DateTime.UtcNow;

            _db.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = deviation.RegisteredSystemId,
                EventType = "DeviationISSMRecommendation",
                Actor = reviewedBy,
                Summary = $"ISSM recommended '{decision}' for CAT I deviation on {deviation.ControlId}",
                RelatedEntityType = "Deviation",
                RelatedEntityId = deviation.Id,
            });

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("ISSM recommendation '{Decision}' recorded for CAT I deviation {DeviationId}",
                decision, deviationId);

            // Notify AO that ISSM recommendation is ready
            await NotifyAsync(deviation.RequestedBy,
                $"CAT I Deviation ISSM Recommendation: {deviation.ControlId}",
                $"ISSM has recommended '{decision}' for your CAT I deviation on {deviation.ControlId}. Awaiting AO final decision.",
                cancellationToken);

            return deviation;
        }

        // Final decision (ISSM for CAT II/III, or AO for CAT I)
        if (decision == "Approve")
        {
            deviation.Status = DeviationStatus.Approved;
            ApplyApprovalCascade(deviation);
        }
        else
        {
            deviation.Status = DeviationStatus.Denied;
        }

        deviation.ReviewedBy = reviewedBy;
        deviation.ReviewedAt = DateTime.UtcNow;
        deviation.ReviewerRole = reviewerRole;
        deviation.ReviewerComments = request.Comments;
        deviation.ModifiedAt = DateTime.UtcNow;

        _db.DashboardActivities.Add(new DashboardActivity
        {
            RegisteredSystemId = deviation.RegisteredSystemId,
            EventType = decision == "Approve" ? "DeviationApproved" : "DeviationDenied",
            Actor = reviewedBy,
            Summary = $"Deviation {decision.ToLowerInvariant()}d for {deviation.ControlId} ({deviation.DeviationType}) by {reviewerRole}",
            RelatedEntityType = "Deviation",
            RelatedEntityId = deviation.Id,
        });

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deviation {DeviationId} {Decision} by {ReviewerRole} {ReviewedBy}",
            deviationId, decision, reviewerRole, reviewedBy);

        // Notify requestor of the decision
        await NotifyAsync(deviation.RequestedBy,
            $"Deviation {decision}d: {deviation.ControlId}",
            $"Your {deviation.DeviationType} deviation for {deviation.ControlId} has been {decision.ToLowerInvariant()}d by {reviewerRole} {reviewedBy}." +
            (request.Comments != null ? $" Comments: {request.Comments}" : ""),
            cancellationToken);

        return deviation;
    }

    /// <inheritdoc />
    public async Task<Deviation> RevokeDeviationAsync(
        string deviationId,
        RevokeDeviationRequest request,
        string revokedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var deviation = await _db.Deviations
            .Include(d => d.Finding)
            .Include(d => d.PoamEntry)
            .FirstOrDefaultAsync(d => d.Id == deviationId, cancellationToken)
            ?? throw new InvalidOperationException($"Deviation '{deviationId}' not found.");

        if (deviation.Status != DeviationStatus.Approved)
            throw new InvalidOperationException("NOT_APPROVED: Deviation is not in Approved status.");

        deviation.Status = DeviationStatus.Revoked;
        deviation.RevokedBy = revokedBy;
        deviation.RevokedAt = DateTime.UtcNow;
        deviation.RevocationReason = request.Reason;
        deviation.ModifiedAt = DateTime.UtcNow;

        RevertLinkedEntities(deviation);

        _db.DashboardActivities.Add(new DashboardActivity
        {
            RegisteredSystemId = deviation.RegisteredSystemId,
            EventType = "DeviationRevoked",
            Actor = revokedBy,
            Summary = $"Deviation revoked for {deviation.ControlId}: {request.Reason}",
            RelatedEntityType = "Deviation",
            RelatedEntityId = deviation.Id,
        });

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deviation {DeviationId} revoked by {RevokedBy}: {Reason}",
            deviationId, revokedBy, request.Reason);

        // Notify requestor of revocation
        await NotifyAsync(deviation.RequestedBy,
            $"Deviation Revoked: {deviation.ControlId}",
            $"Your {deviation.DeviationType} deviation for {deviation.ControlId} has been revoked by {revokedBy}. Reason: {request.Reason}",
            cancellationToken);

        return deviation;
    }

    /// <inheritdoc />
    public async Task<Deviation> ExtendDeviationAsync(
        string deviationId,
        ExtendDeviationRequest request,
        string extendedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var deviation = await _db.Deviations
            .FirstOrDefaultAsync(d => d.Id == deviationId, cancellationToken)
            ?? throw new InvalidOperationException($"Deviation '{deviationId}' not found.");

        if (deviation.Status != DeviationStatus.Approved)
            throw new InvalidOperationException("NOT_APPROVED: Deviation must be Approved to extend.");

        if (request.NewExpirationDate <= DateTime.UtcNow)
            throw new ArgumentException("New expiration date must be in the future.");

        if ((request.NewExpirationDate - DateTime.UtcNow).TotalDays > DeviationConstants.MaxReviewCycleDays)
            throw new ArgumentException($"Extension cannot exceed {DeviationConstants.MaxReviewCycleDays} days from today.");

        var oldExpiration = deviation.ExpirationDate;
        deviation.ExpirationDate = request.NewExpirationDate;
        if (!string.IsNullOrEmpty(request.Justification))
            deviation.Justification = request.Justification;
        deviation.ModifiedAt = DateTime.UtcNow;

        _db.DashboardActivities.Add(new DashboardActivity
        {
            RegisteredSystemId = deviation.RegisteredSystemId,
            EventType = "DeviationExtended",
            Actor = extendedBy,
            Summary = $"Deviation for {deviation.ControlId} extended from {oldExpiration:yyyy-MM-dd} to {request.NewExpirationDate:yyyy-MM-dd}",
            RelatedEntityType = "Deviation",
            RelatedEntityId = deviation.Id,
        });

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deviation {DeviationId} extended to {NewExpiration} by {ExtendedBy}",
            deviationId, request.NewExpirationDate, extendedBy);

        // Notify requestor of extension
        await NotifyAsync(deviation.RequestedBy,
            $"Deviation Extended: {deviation.ControlId}",
            $"Your {deviation.DeviationType} deviation for {deviation.ControlId} has been extended to {request.NewExpirationDate:yyyy-MM-dd} by {extendedBy}.",
            cancellationToken);

        return deviation;
    }

    // ─── Boundary-Scoped Waiver Queries ──────────────────────────────────────

    /// <inheritdoc />
    public async Task<List<string>> GetWaivedControlsForBoundaryAsync(
        string systemId,
        string boundaryDefinitionId,
        CancellationToken cancellationToken = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await _db.Deviations
            .Where(d => d.RegisteredSystemId == systemId
                && d.DeviationType == DeviationType.Waiver
                && d.Status == DeviationStatus.Approved
                && d.BoundaryDefinitionId == boundaryDefinitionId)
            .Select(d => d.ControlId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> GetActiveDeviationCountAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await _db.Deviations
            .CountAsync(d => d.RegisteredSystemId == systemId
                && d.Status == DeviationStatus.Approved,
                cancellationToken);
    }

    // ─── Expiration & Orphan Handling ────────────────────────────────────────

    /// <inheritdoc />
    public async Task<int> ExpireDeviationsAsync(CancellationToken cancellationToken = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var expiredDeviations = await _db.Deviations
            .Include(d => d.Finding)
            .Include(d => d.PoamEntry)
            .Where(d => d.Status == DeviationStatus.Approved && d.ExpirationDate < DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var deviation in expiredDeviations)
        {
            deviation.Status = DeviationStatus.Expired;
            deviation.ModifiedAt = DateTime.UtcNow;

            RevertLinkedEntities(deviation);

            _db.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = deviation.RegisteredSystemId,
                EventType = "DeviationExpired",
                Actor = "system",
                Summary = $"Deviation for {deviation.ControlId} expired; linked finding/POA&M reverted",
                RelatedEntityType = "Deviation",
                RelatedEntityId = deviation.Id,
            });
        }

        if (expiredDeviations.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Expired {Count} deviations", expiredDeviations.Count);
        }

        return expiredDeviations.Count;
    }

    /// <inheritdoc />
    public async Task<int> HandleOrphanedDeviationsAsync(
        string findingId,
        CancellationToken cancellationToken = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var orphanedDeviations = await _db.Deviations
            .Where(d => d.FindingId == findingId
                && (d.Status == DeviationStatus.Pending || d.Status == DeviationStatus.Approved))
            .ToListAsync(cancellationToken);

        foreach (var deviation in orphanedDeviations)
        {
            deviation.Status = DeviationStatus.Revoked;
            deviation.RevocationReason = "Linked finding was deleted";
            deviation.ModifiedAt = DateTime.UtcNow;

            _db.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = deviation.RegisteredSystemId,
                EventType = "DeviationOrphaned",
                Actor = "system",
                Summary = $"Deviation for {deviation.ControlId} orphaned — linked finding '{findingId}' was deleted",
                RelatedEntityType = "Deviation",
                RelatedEntityId = deviation.Id,
            });
        }

        if (orphanedDeviations.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Orphaned {Count} deviations for deleted finding {FindingId}",
                orphanedDeviations.Count, findingId);
        }

        return orphanedDeviations.Count;
    }

    /// <inheritdoc />
    public async Task<int> HandleBoundaryDeletionAsync(
        string deletedBoundaryId,
        string systemId,
        CancellationToken cancellationToken = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        // Find the Primary boundary for this system to reassign waivers to
        var primaryBoundary = await _db.AuthorizationBoundaryDefinitions
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId && b.IsPrimary, cancellationToken);

        if (primaryBoundary is null)
        {
            _logger.LogWarning("No primary boundary found for system {SystemId}; cannot reassign waivers", systemId);
            return 0;
        }

        var scopedWaivers = await _db.Deviations
            .Where(d => d.BoundaryDefinitionId == deletedBoundaryId
                && d.DeviationType == DeviationType.Waiver
                && (d.Status == DeviationStatus.Pending || d.Status == DeviationStatus.Approved))
            .ToListAsync(cancellationToken);

        foreach (var waiver in scopedWaivers)
        {
            waiver.BoundaryDefinitionId = primaryBoundary.Id;
            waiver.Status = DeviationStatus.Pending;
            waiver.ModifiedAt = DateTime.UtcNow;

            _db.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "DeviationBoundaryReassigned",
                Actor = "system",
                Summary = $"Waiver for {waiver.ControlId} reassigned from deleted boundary to '{primaryBoundary.Name}'; set to Pending for re-review",
                RelatedEntityType = "Deviation",
                RelatedEntityId = waiver.Id,
            });
        }

        if (scopedWaivers.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Reassigned {Count} waivers from deleted boundary {BoundaryId} to primary boundary {PrimaryId}",
                scopedWaivers.Count, deletedBoundaryId, primaryBoundary.Id);
        }

        return scopedWaivers.Count;
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Apply status cascade when a deviation is approved:
    /// finding → FalsePositive (for FP type) or Accepted (for RA/Waiver);
    /// POA&amp;M → RiskAccepted.
    /// </summary>
    private void ApplyApprovalCascade(Deviation deviation)
    {
        if (deviation.Finding != null)
        {
            deviation.Finding.Status = deviation.DeviationType == DeviationType.FalsePositive
                ? FindingStatus.FalsePositive
                : FindingStatus.Accepted;
        }

        if (deviation.PoamEntry != null)
        {
            deviation.PoamEntry.Status = PoamStatus.RiskAccepted;
        }
    }

    /// <summary>
    /// Revert linked entities when a deviation is expired or revoked:
    /// finding → Open; POA&amp;M → Ongoing.
    /// </summary>
    private static void RevertLinkedEntities(Deviation deviation)
    {
        if (deviation.Finding != null)
            deviation.Finding.Status = FindingStatus.Open;

        if (deviation.PoamEntry != null)
            deviation.PoamEntry.Status = PoamStatus.Ongoing;
    }

    /// <summary>Parse the JSON evidence references string into a list.</summary>
    private static List<string> ParseEvidenceReferences(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Broadcast an in-app notification for deviation lifecycle events.</summary>
    private async Task NotifyAsync(
        string userId, string subject, string body, CancellationToken ct)
    {
        if (_broadcaster is null || string.IsNullOrEmpty(userId)) return;

        await using var _db = await _dbFactory.CreateDbContextAsync(ct);

        var notification = new AlertNotification
        {
            Id = Guid.NewGuid(),
            AlertId = Guid.Empty,
            Channel = NotificationChannel.Chat,
            Recipient = userId,
            Subject = subject,
            Body = body,
            IsDelivered = true,
            SentAt = DateTimeOffset.UtcNow,
            DeliveredAt = DateTimeOffset.UtcNow,
            UserId = userId,
        };

        _db.AlertNotifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        try
        {
            await _broadcaster.BroadcastToUserAsync(userId, notification, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to broadcast deviation notification — client may not be connected");
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Core service for POA&amp;M item CRUD, lifecycle management, pagination, metrics,
/// and component linkage operations.
/// </summary>
public class PoamService
{
    private readonly AtoCopilotContext _db;
    private readonly ILogger<PoamService> _logger;

    public PoamService(AtoCopilotContext db, ILogger<PoamService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ─── CRUD ────────────────────────────────────────────────────────────────

    /// <summary>Creates a new POA&amp;M item with optional component links and milestones.</summary>
    public async Task<PoamItem> CreateAsync(
        string systemId,
        string weakness,
        string weaknessSource,
        string controlId,
        CatSeverity catSeverity,
        string poc,
        DateTime scheduledCompletionDate,
        string? pocEmail = null,
        string? resourcesRequired = null,
        decimal? costEstimate = null,
        string? comments = null,
        string? findingId = null,
        string? createdBy = null,
        IEnumerable<string>? componentIds = null,
        IEnumerable<(string Description, DateTime TargetDate)>? milestones = null,
        CancellationToken ct = default)
    {
        var system = await _db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, ct)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var poam = new PoamItem
        {
            RegisteredSystemId = systemId,
            Weakness = weakness,
            WeaknessSource = weaknessSource,
            SecurityControlNumber = controlId,
            CatSeverity = catSeverity,
            PointOfContact = poc,
            PocEmail = pocEmail,
            ScheduledCompletionDate = scheduledCompletionDate,
            ResourcesRequired = resourcesRequired,
            CostEstimate = costEstimate,
            Comments = comments,
            FindingId = findingId,
            CreatedBy = createdBy,
            Status = PoamStatus.Ongoing
        };

        // Add milestones
        if (milestones != null)
        {
            var seq = 1;
            foreach (var (desc, target) in milestones)
            {
                poam.Milestones.Add(new PoamMilestone
                {
                    PoamItemId = poam.Id,
                    Description = desc,
                    TargetDate = target,
                    Sequence = seq++
                });
            }
        }

        _db.PoamItems.Add(poam);

        // Add component links
        if (componentIds != null)
        {
            foreach (var compId in componentIds.Distinct())
            {
                _db.PoamComponentLinks.Add(new PoamComponentLink
                {
                    PoamItemId = poam.Id,
                    SystemComponentId = compId,
                    LinkedBy = createdBy ?? "system"
                });
            }
        }

        // Add creation history entry
        _db.PoamHistoryEntries.Add(new PoamHistoryEntry
        {
            PoamItemId = poam.Id,
            EventType = PoamHistoryEventType.Created,
            NewValue = PoamStatus.Ongoing.ToString(),
            ActingUserId = createdBy ?? "system",
            ActingUserName = createdBy ?? "system",
            Details = $"POA&M created for {controlId}: {weakness[..Math.Min(100, weakness.Length)]}"
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created POA&M {PoamId} for system {SystemId}, control {ControlId}", poam.Id, systemId, controlId);
        return poam;
    }

    /// <summary>Gets a POA&amp;M item by ID with milestones, component links, history, and ticket sync.</summary>
    public async Task<PoamItem?> GetByIdAsync(string poamId, bool includeHistory = true, CancellationToken ct = default)
    {
        var query = _db.PoamItems
            .Include(p => p.Milestones.OrderBy(m => m.Sequence))
            .Include(p => p.ComponentLinks)
                .ThenInclude(cl => cl.SystemComponent)
            .AsQueryable();

        if (includeHistory)
        {
            query = query.Include(p => p.History.OrderByDescending(h => h.Timestamp));
        }

        return await query.FirstOrDefaultAsync(p => p.Id == poamId, ct);
    }

    /// <summary>Lists POA&amp;M items with server-side pagination, sorting, and filtering.</summary>
    public async Task<(List<PoamItem> Items, int TotalCount)> ListAsync(
        string? systemId = null,
        int page = 1,
        int pageSize = 25,
        string sortBy = "scheduledCompletionDate",
        string sortDirection = "asc",
        PoamStatus? statusFilter = null,
        CatSeverity? severityFilter = null,
        bool? overdueOnly = null,
        string? componentId = null,
        string? search = null,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var query = _db.PoamItems
            .AsNoTracking()
            .Include(p => p.Milestones)
            .Include(p => p.ComponentLinks)
                .ThenInclude(cl => cl.SystemComponent)
            .AsQueryable();

        // Filters
        if (!string.IsNullOrEmpty(systemId))
            query = query.Where(p => p.RegisteredSystemId == systemId);

        if (statusFilter.HasValue)
            query = query.Where(p => p.Status == statusFilter.Value);

        if (severityFilter.HasValue)
            query = query.Where(p => p.CatSeverity == severityFilter.Value);

        if (overdueOnly == true)
            query = query.Where(p =>
                p.ScheduledCompletionDate < DateTime.UtcNow &&
                p.Status != PoamStatus.Completed &&
                p.Status != PoamStatus.RiskAccepted);

        if (!string.IsNullOrEmpty(componentId))
            query = query.Where(p => p.ComponentLinks.Any(cl => cl.SystemComponentId == componentId));

        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            query = query.Where(p =>
                p.SecurityControlNumber.ToLower().Contains(s) ||
                p.Weakness.ToLower().Contains(s) ||
                p.PointOfContact.ToLower().Contains(s) ||
                p.ComponentLinks.Any(cl => cl.SystemComponent != null && cl.SystemComponent.Name.ToLower().Contains(s)));
        }

        var totalCount = await query.CountAsync(ct);

        // Sorting
        query = sortBy.ToLowerInvariant() switch
        {
            "controlid" or "securitycontrolnumber" => sortDirection == "desc"
                ? query.OrderByDescending(p => p.SecurityControlNumber)
                : query.OrderBy(p => p.SecurityControlNumber),
            "severity" or "catseverity" => sortDirection == "desc"
                ? query.OrderByDescending(p => p.CatSeverity)
                : query.OrderBy(p => p.CatSeverity),
            "status" => sortDirection == "desc"
                ? query.OrderByDescending(p => p.Status)
                : query.OrderBy(p => p.Status),
            "poc" or "pointofcontact" => sortDirection == "desc"
                ? query.OrderByDescending(p => p.PointOfContact)
                : query.OrderBy(p => p.PointOfContact),
            "weakness" => sortDirection == "desc"
                ? query.OrderByDescending(p => p.Weakness)
                : query.OrderBy(p => p.Weakness),
            _ => sortDirection == "desc"
                ? query.OrderByDescending(p => p.ScheduledCompletionDate)
                : query.OrderBy(p => p.ScheduledCompletionDate),
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    /// <summary>Updates a POA&amp;M item with optimistic concurrency.</summary>
    public async Task<PoamItem> UpdateAsync(
        string poamId,
        Guid rowVersion,
        Action<PoamItem> applyChanges,
        string modifiedBy = "mcp-user",
        CancellationToken ct = default)
    {
        var poam = await _db.PoamItems
            .Include(p => p.Milestones)
            .FirstOrDefaultAsync(p => p.Id == poamId, ct)
            ?? throw new InvalidOperationException($"POA&M '{poamId}' not found.");

        if (poam.RowVersion != rowVersion)
            throw new InvalidOperationException("POAM_CONCURRENCY_CONFLICT: The item was modified by another user. Reload and try again.");

        applyChanges(poam);
        poam.ModifiedAt = DateTime.UtcNow;
        poam.ModifiedBy = modifiedBy;

        await _db.SaveChangesAsync(ct);
        return poam;
    }

    /// <summary>Deletes a POA&amp;M item by ID.</summary>
    public async Task DeleteAsync(string poamId, CancellationToken ct = default)
    {
        var poam = await _db.PoamItems.FindAsync(new object[] { poamId }, ct)
            ?? throw new InvalidOperationException($"POA&M '{poamId}' not found.");

        _db.PoamItems.Remove(poam);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Deleted POA&M {PoamId}", poamId);
    }

    // ─── Lifecycle Transitions (FR-007/FR-008) ──────────────────────────────

    /// <summary>Updates POA&amp;M status with lifecycle enforcement, audit trail, and optional cascade.</summary>
    public async Task<PoamItem> UpdateStatusAsync(
        string poamId,
        PoamStatus newStatus,
        Guid rowVersion,
        string actingUserId,
        string? delayReason = null,
        DateTime? revisedDate = null,
        string? deviationId = null,
        string? comments = null,
        bool cascadeToTask = false,
        CancellationToken ct = default)
    {
        var poam = await _db.PoamItems
            .Include(p => p.Milestones)
            .Include(p => p.ComponentLinks)
            .Include(p => p.History)
            .FirstOrDefaultAsync(p => p.Id == poamId, ct)
            ?? throw new InvalidOperationException($"POA&M '{poamId}' not found.");

        if (poam.RowVersion != rowVersion)
            throw new InvalidOperationException("POAM_CONCURRENCY_CONFLICT: The item was modified by another user. Reload and try again.");

        var oldStatus = poam.Status;

        // Validate transitions per FR-007
        ValidateTransition(poam, newStatus, delayReason, revisedDate, deviationId);

        poam.Status = newStatus;
        poam.ModifiedAt = DateTime.UtcNow;
        poam.ModifiedBy = actingUserId;

        if (newStatus == PoamStatus.Delayed && revisedDate.HasValue)
            poam.ScheduledCompletionDate = revisedDate.Value;

        if (newStatus == PoamStatus.Ongoing && oldStatus == PoamStatus.Delayed && revisedDate.HasValue)
            poam.ScheduledCompletionDate = revisedDate.Value;

        if (newStatus == PoamStatus.Completed)
            poam.ActualCompletionDate = DateTime.UtcNow;

        if (newStatus == PoamStatus.RiskAccepted && !string.IsNullOrEmpty(deviationId))
            poam.DeviationId = deviationId;

        if (comments != null)
            poam.Comments = comments;

        // Add audit history entry
        AddHistoryEntry(poam, PoamHistoryEventType.StatusChanged, oldStatus.ToString(), newStatus.ToString(),
            actingUserId, actingUserId, BuildTransitionDetails(oldStatus, newStatus, delayReason, deviationId));

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("POA&M {PoamId} status: {OldStatus} → {NewStatus} by {User}", poamId, oldStatus, newStatus, actingUserId);
        return poam;
    }

    /// <summary>Validates lifecycle transition rules per FR-007.</summary>
    private static void ValidateTransition(PoamItem poam, PoamStatus newStatus, string? delayReason, DateTime? revisedDate, string? deviationId)
    {
        var old = poam.Status;

        // Valid transitions
        var valid = (old, newStatus) switch
        {
            (PoamStatus.Ongoing, PoamStatus.Delayed) => true,
            (PoamStatus.Ongoing, PoamStatus.Completed) => true,
            (PoamStatus.Ongoing, PoamStatus.RiskAccepted) => true,
            (PoamStatus.Delayed, PoamStatus.Ongoing) => true,       // Resume
            (PoamStatus.Delayed, PoamStatus.Completed) => true,
            (PoamStatus.Delayed, PoamStatus.RiskAccepted) => true,
            _ => false
        };

        if (!valid)
            throw new InvalidOperationException($"POAM_INVALID_TRANSITION: Cannot transition from {old} to {newStatus}.");

        // Delayed requires delay_reason + revised_date
        if (newStatus == PoamStatus.Delayed)
        {
            if (string.IsNullOrWhiteSpace(delayReason))
                throw new InvalidOperationException("POAM_DELAY_REASON_REQUIRED: A delay reason is required when marking a POA&M as Delayed.");
            if (!revisedDate.HasValue)
                throw new InvalidOperationException("POAM_REVISED_DATE_REQUIRED: A revised completion date is required when marking a POA&M as Delayed.");
        }

        // Resume (Delayed → Ongoing) requires revised completion date
        if (old == PoamStatus.Delayed && newStatus == PoamStatus.Ongoing && !revisedDate.HasValue)
            throw new InvalidOperationException("POAM_REVISED_DATE_REQUIRED: A revised completion date is required when resuming a delayed POA&M.");

        // Risk Accepted requires deviation_id
        if (newStatus == PoamStatus.RiskAccepted && string.IsNullOrWhiteSpace(deviationId))
            throw new InvalidOperationException("POAM_DEVIATION_REQUIRED: A deviation record ID is required for Risk Accepted status.");
    }

    /// <summary>Adds an audit history entry to a POA&amp;M item.</summary>
    public void AddHistoryEntry(
        PoamItem poam,
        PoamHistoryEventType eventType,
        string? oldValue,
        string? newValue,
        string actingUserId,
        string actingUserName,
        string? details = null,
        CascadeOrigin? cascadeOrigin = null)
    {
        _db.PoamHistoryEntries.Add(new PoamHistoryEntry
        {
            PoamItemId = poam.Id,
            EventType = eventType,
            OldValue = oldValue,
            NewValue = newValue,
            ActingUserId = actingUserId,
            ActingUserName = actingUserName,
            Details = details,
            CascadeOrigin = cascadeOrigin
        });
    }

    /// <summary>Updates a POA&amp;M milestone.</summary>
    public async Task<PoamItem> UpdateMilestoneAsync(
        string poamId,
        string milestoneId,
        Guid rowVersion,
        string actingUserId,
        bool markComplete = false,
        string? description = null,
        DateTime? targetDate = null,
        CancellationToken ct = default)
    {
        var poam = await _db.PoamItems
            .Include(p => p.Milestones)
            .FirstOrDefaultAsync(p => p.Id == poamId, ct)
            ?? throw new InvalidOperationException($"POA&M '{poamId}' not found.");

        if (poam.RowVersion != rowVersion)
            throw new InvalidOperationException("POAM_CONCURRENCY_CONFLICT: The item was modified by another user. Reload and try again.");

        var milestone = poam.Milestones.FirstOrDefault(m => m.Id == milestoneId)
            ?? throw new InvalidOperationException($"Milestone '{milestoneId}' not found.");

        if (markComplete && !milestone.CompletedDate.HasValue)
        {
            milestone.CompletedDate = DateTime.UtcNow;
            AddHistoryEntry(poam, PoamHistoryEventType.MilestoneUpdated, null, milestone.Description, actingUserId, actingUserId);
        }

        if (description != null) milestone.Description = description;
        if (targetDate.HasValue) milestone.TargetDate = targetDate.Value;

        poam.ModifiedAt = DateTime.UtcNow;
        poam.ModifiedBy = actingUserId;
        await _db.SaveChangesAsync(ct);
        return poam;
    }

    /// <summary>Bulk updates POA&amp;M statuses.</summary>
    public async Task<List<(string PoamId, bool Success, string? Error)>> BulkUpdateStatusAsync(
        IEnumerable<string> poamIds,
        PoamStatus newStatus,
        string actingUserId,
        string? delayReason = null,
        DateTime? revisedDate = null,
        string? comments = null,
        CancellationToken ct = default)
    {
        var results = new List<(string, bool, string?)>();
        foreach (var poamId in poamIds)
        {
            try
            {
                var poam = await _db.PoamItems.FirstOrDefaultAsync(p => p.Id == poamId, ct);
                if (poam == null)
                {
                    results.Add((poamId, false, "Not found"));
                    continue;
                }

                await UpdateStatusAsync(poamId, newStatus, poam.RowVersion, actingUserId,
                    delayReason, revisedDate, comments: comments, ct: ct);
                results.Add((poamId, true, null));
            }
            catch (InvalidOperationException ex)
            {
                results.Add((poamId, false, ex.Message));
            }
        }
        return results;
    }

    private static string BuildTransitionDetails(PoamStatus old, PoamStatus newStatus, string? delayReason, string? deviationId) =>
        (old, newStatus) switch
        {
            (_, PoamStatus.Delayed) => $"Delayed: {delayReason}",
            (PoamStatus.Delayed, PoamStatus.Ongoing) => "Resumed from Delayed",
            (_, PoamStatus.RiskAccepted) => $"Risk Accepted with deviation: {deviationId}",
            (_, PoamStatus.Completed) => "Marked as Completed",
            _ => $"{old} → {newStatus}"
        };

    // ─── Metrics ─────────────────────────────────────────────────────────────

    /// <summary>Calculates POA&amp;M metrics for a system or org-wide.</summary>
    public async Task<PoamMetricsResult> GetMetricsAsync(string? systemId = null, CancellationToken ct = default)
    {
        var query = _db.PoamItems.AsQueryable();
        if (!string.IsNullOrEmpty(systemId))
            query = query.Where(p => p.RegisteredSystemId == systemId);

        var now = DateTime.UtcNow;
        var thirtyDaysOut = now.AddDays(30);

        var items = await query.Select(p => new
        {
            p.Status,
            p.CatSeverity,
            p.ScheduledCompletionDate,
            p.ActualCompletionDate,
            p.CreatedAt
        }).ToListAsync(ct);

        var open = items.Where(i => i.Status != PoamStatus.Completed && i.Status != PoamStatus.RiskAccepted).ToList();
        var completed = items.Where(i => i.Status == PoamStatus.Completed && i.ActualCompletionDate.HasValue).ToList();

        var avgDaysToClose = completed.Count > 0
            ? completed.Average(c => (c.ActualCompletionDate!.Value - c.CreatedAt).TotalDays)
            : 0;

        return new PoamMetricsResult
        {
            TotalOpen = open.Count,
            Overdue = open.Count(i => i.ScheduledCompletionDate < now),
            CatICount = open.Count(i => i.CatSeverity == CatSeverity.CatI),
            CatIICount = open.Count(i => i.CatSeverity == CatSeverity.CatII),
            CatIIICount = open.Count(i => i.CatSeverity == CatSeverity.CatIII),
            ExpiringWithin30Days = open.Count(i => i.ScheduledCompletionDate <= thirtyDaysOut && i.ScheduledCompletionDate >= now),
            AvgDaysToClose = Math.Round(avgDaysToClose, 1),
            ByStatus = items.GroupBy(i => i.Status.ToString())
                .Select(g => new StatusCount { Status = g.Key, Count = g.Count() })
                .ToList()
        };
    }

    // ─── Component Linkage ───────────────────────────────────────────────────

    /// <summary>Links components to a POA&amp;M item.</summary>
    public async Task LinkComponentsAsync(
        string poamId, IEnumerable<string> componentIds, string linkedBy = "mcp-user", CancellationToken ct = default)
    {
        var poam = await _db.PoamItems.FindAsync(new object[] { poamId }, ct)
            ?? throw new InvalidOperationException($"POA&M '{poamId}' not found.");

        foreach (var compId in componentIds.Distinct())
        {
            var exists = await _db.PoamComponentLinks
                .AnyAsync(cl => cl.PoamItemId == poamId && cl.SystemComponentId == compId, ct);

            if (exists) continue;

            _db.PoamComponentLinks.Add(new PoamComponentLink
            {
                PoamItemId = poamId,
                SystemComponentId = compId,
                LinkedBy = linkedBy
            });

            _db.PoamHistoryEntries.Add(new PoamHistoryEntry
            {
                PoamItemId = poamId,
                EventType = PoamHistoryEventType.ComponentLinked,
                NewValue = compId,
                ActingUserId = linkedBy,
                ActingUserName = linkedBy
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Unlinks components from a POA&amp;M item.</summary>
    public async Task UnlinkComponentsAsync(
        string poamId, IEnumerable<string> componentIds, string unlinkedBy = "mcp-user", CancellationToken ct = default)
    {
        var links = await _db.PoamComponentLinks
            .Where(cl => cl.PoamItemId == poamId && componentIds.Contains(cl.SystemComponentId))
            .ToListAsync(ct);

        foreach (var link in links)
        {
            _db.PoamComponentLinks.Remove(link);
            _db.PoamHistoryEntries.Add(new PoamHistoryEntry
            {
                PoamItemId = poamId,
                EventType = PoamHistoryEventType.ComponentUnlinked,
                OldValue = link.SystemComponentId,
                ActingUserId = unlinkedBy,
                ActingUserName = unlinkedBy
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Gets POA&amp;M items linked to a component with aggregate risk summary.</summary>
    public async Task<ComponentPoamSummary> GetPoamsByComponentAsync(string componentId, CancellationToken ct = default)
    {
        var poamIds = await _db.PoamComponentLinks
            .Where(cl => cl.SystemComponentId == componentId)
            .Select(cl => cl.PoamItemId)
            .ToListAsync(ct);

        var poams = await _db.PoamItems
            .AsNoTracking()
            .Where(p => poamIds.Contains(p.Id))
            .ToListAsync(ct);

        var open = poams.Where(p => p.Status != PoamStatus.Completed && p.Status != PoamStatus.RiskAccepted).ToList();

        return new ComponentPoamSummary
        {
            ComponentId = componentId,
            TotalPoams = poams.Count,
            OpenCount = open.Count,
            OverdueCount = open.Count(p => p.ScheduledCompletionDate < DateTime.UtcNow),
            HighestSeverity = open.Any() ? open.Min(p => p.CatSeverity) : null, // CatI < CatII < CatIII
            Items = poams
        };
    }

    // ─── Bulk Create ─────────────────────────────────────────────────────────

    /// <summary>Bulk-create POA&amp;M items from finding IDs with 3-field duplicate detection
    /// (findingRef + controlId + componentId per FR-006).</summary>
    public async Task<BulkCreateResult> BulkCreateFromFindingsAsync(
        string systemId,
        IEnumerable<string> findingIds,
        IEnumerable<string>? componentIds = null,
        bool linkRemediationTasks = false,
        string createdBy = "system",
        CancellationToken ct = default)
    {
        var result = new BulkCreateResult();
        var findings = await _db.Findings
            .Where(f => findingIds.Contains(f.Id))
            .ToListAsync(ct);

        // Load existing active POA&Ms for duplicate detection
        var existingPoams = await _db.PoamItems
            .Include(p => p.ComponentLinks)
            .Where(p => p.RegisteredSystemId == systemId &&
                        p.Status != PoamStatus.Completed &&
                        p.Status != PoamStatus.RiskAccepted)
            .ToListAsync(ct);

        var compIdList = componentIds?.ToList() ?? new List<string>();

        foreach (var finding in findings)
        {
            // 3-field duplicate detection: findingRef + controlId + componentId
            var controlId = finding.ControlId ?? finding.Id;
            var findingRef = finding.Id;
            var effectiveComponentId = compIdList.FirstOrDefault() ?? "";

            var isDuplicate = existingPoams.Any(p =>
                p.FindingId == findingRef &&
                p.SecurityControlNumber == controlId &&
                (string.IsNullOrEmpty(effectiveComponentId) ||
                 p.ComponentLinks.Any(cl => cl.SystemComponentId == effectiveComponentId)));

            if (isDuplicate)
            {
                result.SkippedDuplicates++;
                result.Results.Add(new BulkCreateItemResult
                {
                    FindingId = finding.Id,
                    Status = "duplicate"
                });
                continue;
            }

            try
            {
                var severity = MapFindingSeverity(finding.Severity);
                var poam = await CreateAsync(
                    systemId,
                    finding.Title ?? finding.Description ?? "Finding-based POA&M",
                    "Assessment",
                    controlId,
                    severity,
                    createdBy,
                    DateTime.UtcNow.AddDays(severity == CatSeverity.CatI ? 30 : severity == CatSeverity.CatII ? 90 : 180),
                    findingId: finding.Id,
                    createdBy: createdBy,
                    componentIds: compIdList.Count > 0 ? compIdList : null,
                    ct: ct);

                result.Created++;
                result.Results.Add(new BulkCreateItemResult
                {
                    FindingId = finding.Id,
                    PoamId = poam.Id,
                    Status = "created"
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create POA&M from finding {FindingId}", finding.Id);
                result.Results.Add(new BulkCreateItemResult
                {
                    FindingId = finding.Id,
                    Status = "error"
                });
            }
        }

        return result;
    }

    private static CatSeverity MapFindingSeverity(FindingSeverity severity) =>
        severity switch
        {
            FindingSeverity.Critical or FindingSeverity.High => CatSeverity.CatI,
            FindingSeverity.Medium => CatSeverity.CatII,
            _ => CatSeverity.CatIII
        };

    // ─── T068: Trend Analysis ────────────────────────────────────────────────

    public async Task<PoamTrendResult> GetTrendDataAsync(
        string systemId,
        string period = "monthly",
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken ct = default)
    {
        var end = endDate ?? DateTime.UtcNow;
        var start = startDate ?? end.AddMonths(-12);

        var poams = await _db.PoamItems
            .Where(p => p.RegisteredSystemId == systemId && p.CreatedAt >= start)
            .Include(p => p.History)
            .AsNoTracking()
            .ToListAsync(ct);

        // Build open-over-time series
        var openOverTime = BuildOpenOverTimeSeries(poams, start, end, period);

        // Closure rate per period
        var closureRates = BuildClosureRateSeries(poams, start, end, period);

        // Aging breakdown by severity
        var agingBreakdown = BuildAgingBreakdown(poams);

        // Time-to-close distribution
        var timeToClose = BuildTimeToCloseDistribution(poams);

        return new PoamTrendResult
        {
            SystemId = systemId,
            Period = period,
            StartDate = start,
            EndDate = end,
            OpenOverTime = openOverTime,
            ClosureRates = closureRates,
            AgingBreakdown = agingBreakdown,
            TimeToCloseDistribution = timeToClose,
        };
    }

    public async Task<byte[]> ExportTrendReportPdfAsync(
        string systemId,
        string period = "monthly",
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken ct = default)
    {
        var trend = await GetTrendDataAsync(systemId, period, startDate, endDate, ct);
        var metrics = await GetMetricsAsync(systemId, ct);

        using var ms = new MemoryStream();
        // PDF generation with QuestPDF
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(QuestPDF.Helpers.PageSizes.Letter);
                page.Margin(40);

                page.Header().Text("POA&M Trend Report").FontSize(20).Bold();

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    // Summary
                    col.Item().Text($"System: {systemId}").FontSize(12);
                    col.Item().Text($"Period: {trend.StartDate:MMM yyyy} — {trend.EndDate:MMM yyyy} ({trend.Period})").FontSize(10);
                    col.Item().Text($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(9);

                    col.Item().LineHorizontal(1);

                    // Metrics summary table
                    col.Item().Text("Summary Metrics").FontSize(14).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        void AddRow(string label, string value)
                        {
                            table.Cell().Padding(4).Text(label).FontSize(10);
                            table.Cell().Padding(4).Text(value).FontSize(10).Bold();
                        }

                        AddRow("Total Open", metrics.TotalOpen.ToString());
                        AddRow("Overdue", metrics.Overdue.ToString());
                        AddRow("CAT I", metrics.CatICount.ToString());
                        AddRow("CAT II", metrics.CatIICount.ToString());
                        AddRow("CAT III", metrics.CatIIICount.ToString());
                        AddRow("Avg Days to Close", $"{metrics.AvgDaysToClose:F1}");
                    });

                    // Open Over Time
                    col.Item().Text("Open POA&Ms Over Time").FontSize(14).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().Padding(4).Text("Period").FontSize(9).Bold();
                        table.Cell().Padding(4).Text("Open Count").FontSize(9).Bold();

                        foreach (var pt in trend.OpenOverTime)
                        {
                            table.Cell().Padding(4).Text(pt.Label).FontSize(9);
                            table.Cell().Padding(4).Text(pt.Value.ToString()).FontSize(9);
                        }
                    });

                    // Closure Rates
                    col.Item().Text("Closure Rates").FontSize(14).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().Padding(4).Text("Period").FontSize(9).Bold();
                        table.Cell().Padding(4).Text("Closed Count").FontSize(9).Bold();

                        foreach (var pt in trend.ClosureRates)
                        {
                            table.Cell().Padding(4).Text(pt.Label).FontSize(9);
                            table.Cell().Padding(4).Text(pt.Value.ToString()).FontSize(9);
                        }
                    });

                    // Aging Breakdown
                    col.Item().Text("Aging Breakdown by Severity").FontSize(14).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().Padding(4).Text("Range").FontSize(9).Bold();
                        table.Cell().Padding(4).Text("CAT I").FontSize(9).Bold();
                        table.Cell().Padding(4).Text("CAT II").FontSize(9).Bold();
                        table.Cell().Padding(4).Text("CAT III").FontSize(9).Bold();

                        foreach (var bucket in trend.AgingBreakdown)
                        {
                            table.Cell().Padding(4).Text(bucket.Label).FontSize(9);
                            table.Cell().Padding(4).Text(bucket.CatI.ToString()).FontSize(9);
                            table.Cell().Padding(4).Text(bucket.CatII.ToString()).FontSize(9);
                            table.Cell().Padding(4).Text(bucket.CatIII.ToString()).FontSize(9);
                        }
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page ").FontSize(8);
                    t.CurrentPageNumber().FontSize(8);
                    t.Span(" of ").FontSize(8);
                    t.TotalPages().FontSize(8);
                });
            });
        }).GeneratePdf(ms);

        return ms.ToArray();
    }

    private static List<TrendDataPoint> BuildOpenOverTimeSeries(
        List<PoamItem> poams, DateTime start, DateTime end, string period)
    {
        var points = new List<TrendDataPoint>();
        var intervals = GetIntervals(start, end, period);

        foreach (var (label, periodStart, periodEnd) in intervals)
        {
            var openCount = poams.Count(p =>
                p.CreatedAt <= periodEnd &&
                (p.Status != PoamStatus.Completed && p.Status != PoamStatus.RiskAccepted ||
                 (p.History?.Any(h => h.EventType == PoamHistoryEventType.StatusChanged &&
                                     h.Timestamp > periodEnd) ?? false)));
            points.Add(new TrendDataPoint { Label = label, Value = openCount });
        }

        return points;
    }

    private static List<TrendDataPoint> BuildClosureRateSeries(
        List<PoamItem> poams, DateTime start, DateTime end, string period)
    {
        var points = new List<TrendDataPoint>();
        var intervals = GetIntervals(start, end, period);

        foreach (var (label, periodStart, periodEnd) in intervals)
        {
            var closedInPeriod = poams.Count(p =>
                p.History?.Any(h =>
                    h.EventType == PoamHistoryEventType.StatusChanged &&
                    h.NewValue == PoamStatus.Completed.ToString() &&
                    h.Timestamp >= periodStart && h.Timestamp <= periodEnd) ?? false);
            points.Add(new TrendDataPoint { Label = label, Value = closedInPeriod });
        }

        return points;
    }

    private static List<AgingBucket> BuildAgingBreakdown(List<PoamItem> poams)
    {
        var openPoams = poams.Where(p => p.Status == PoamStatus.Ongoing || p.Status == PoamStatus.Delayed).ToList();
        var buckets = new (string Label, int Min, int Max)[]
        {
            ("0-30 days", 0, 30),
            ("31-60 days", 31, 60),
            ("61-90 days", 61, 90),
            ("90+ days", 91, int.MaxValue),
        };

        return buckets.Select(b =>
        {
            var inRange = openPoams.Where(p =>
            {
                var age = (DateTime.UtcNow - p.CreatedAt).Days;
                return age >= b.Min && age <= b.Max;
            }).ToList();

            return new AgingBucket
            {
                Label = b.Label,
                CatI = inRange.Count(p => p.CatSeverity == CatSeverity.CatI),
                CatII = inRange.Count(p => p.CatSeverity == CatSeverity.CatII),
                CatIII = inRange.Count(p => p.CatSeverity == CatSeverity.CatIII),
            };
        }).ToList();
    }

    private static List<TrendDataPoint> BuildTimeToCloseDistribution(List<PoamItem> poams)
    {
        var closedPoams = poams
            .Where(p => p.Status == PoamStatus.Completed)
            .Select(p =>
            {
                var closedEvent = p.History?
                    .Where(h => h.EventType == PoamHistoryEventType.StatusChanged &&
                                h.NewValue == PoamStatus.Completed.ToString())
                    .OrderByDescending(h => h.Timestamp)
                    .FirstOrDefault();
                return closedEvent != null ? (closedEvent.Timestamp - p.CreatedAt).Days : (int?)null;
            })
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        var ranges = new (string Label, int Min, int Max)[]
        {
            ("< 30 days", 0, 29),
            ("30-60 days", 30, 60),
            ("61-90 days", 61, 90),
            ("91-180 days", 91, 180),
            ("180+ days", 181, int.MaxValue),
        };

        return ranges.Select(r => new TrendDataPoint
        {
            Label = r.Label,
            Value = closedPoams.Count(d => d >= r.Min && d <= r.Max),
        }).ToList();
    }

    private static List<(string Label, DateTime Start, DateTime End)> GetIntervals(
        DateTime start, DateTime end, string period)
    {
        var intervals = new List<(string, DateTime, DateTime)>();
        var current = period == "daily" ? start.Date
            : period == "weekly" ? start.Date.AddDays(-(int)start.DayOfWeek)
            : new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        while (current <= end)
        {
            var next = period switch
            {
                "daily" => current.AddDays(1),
                "weekly" => current.AddDays(7),
                _ => current.AddMonths(1),
            };
            var label = period switch
            {
                "daily" => current.ToString("MMM dd"),
                "weekly" => $"Wk {current:MMM dd}",
                _ => current.ToString("MMM yyyy"),
            };
            intervals.Add((label, current, next.AddSeconds(-1)));
            current = next;
        }

        return intervals;
    }

    // ─── Export Methods ──────────────────────────────────────────────────────

    public async Task<byte[]> ExportEmassExcelAsync(
        string systemId,
        string? statusFilter = null,
        string? severityFilter = null,
        bool includeAll = false,
        CancellationToken ct = default)
    {
        var items = await GetFilteredPoamsForExport(systemId, statusFilter, severityFilter, includeAll, ct);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("POA&M");

        // eMASS 24-column template headers
        var headers = new[]
        {
            "Control Vulnerability Description", "Security Control Number (NC/NA)",
            "Office/Org", "Security Checks", "Resources Required",
            "Scheduled Completion Date", "Milestone with Completion Dates",
            "Milestone Changes", "Source Identifying Vulnerability",
            "Status", "Comments", "Raw Severity", "Relevance of Threat",
            "Likelihood", "Impact", "Impact Description", "Residual Risk Level",
            "Recommendations", "Resulting Residual Risk after Proposed Mitigations",
            "Point of Contact", "Cost Estimate", "Actual Completion Date",
            "Deviation Type", "External Ticket Ref"
        };

        for (var c = 0; c < headers.Length; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightBlue;
        }

        var row = 2;
        foreach (var p in items)
        {
            var milestoneText = string.Join("; ", p.Milestones?.Select(m =>
                $"{m.Description} (Target: {m.TargetDate:yyyy-MM-dd}{(m.CompletedDate.HasValue ? $", Done: {m.CompletedDate:yyyy-MM-dd}" : "")})") ?? []);

            sheet.Cell(row, 1).Value = p.Weakness;
            sheet.Cell(row, 2).Value = p.SecurityControlNumber;
            sheet.Cell(row, 3).Value = "";
            sheet.Cell(row, 4).Value = "";
            sheet.Cell(row, 5).Value = p.ResourcesRequired ?? "";
            sheet.Cell(row, 6).Value = p.ScheduledCompletionDate.ToString("yyyy-MM-dd");
            sheet.Cell(row, 7).Value = milestoneText;
            sheet.Cell(row, 8).Value = "";
            sheet.Cell(row, 9).Value = p.WeaknessSource;
            sheet.Cell(row, 10).Value = p.Status.ToString();
            sheet.Cell(row, 11).Value = p.Comments ?? "";
            sheet.Cell(row, 12).Value = p.CatSeverity.ToString();
            sheet.Cell(row, 13).Value = "";
            sheet.Cell(row, 14).Value = "";
            sheet.Cell(row, 15).Value = "";
            sheet.Cell(row, 16).Value = "";
            sheet.Cell(row, 17).Value = "";
            sheet.Cell(row, 18).Value = "";
            sheet.Cell(row, 19).Value = "";
            sheet.Cell(row, 20).Value = p.PointOfContact;
            sheet.Cell(row, 21).Value = p.CostEstimate?.ToString("C") ?? "";
            sheet.Cell(row, 22).Value = p.ActualCompletionDate?.ToString("yyyy-MM-dd") ?? "";
            sheet.Cell(row, 23).Value = "";
            sheet.Cell(row, 24).Value = p.ExternalTicketRef ?? "";
            row++;
        }

        sheet.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportOscalJsonAsync(
        string systemId,
        string? statusFilter = null,
        string? severityFilter = null,
        bool includeAll = false,
        CancellationToken ct = default)
    {
        var items = await GetFilteredPoamsForExport(systemId, statusFilter, severityFilter, includeAll, ct);

        var oscalPoams = items.Select(p => new
        {
            uuid = p.Id,
            title = p.Weakness,
            description = p.Weakness,
            status = p.Status.ToString().ToLowerInvariant(),
            props = new object[]
            {
                new { name = "security-control-number", value = p.SecurityControlNumber },
                new { name = "cat-severity", value = p.CatSeverity.ToString() },
                new { name = "weakness-source", value = p.WeaknessSource },
                new { name = "poc", value = p.PointOfContact },
            },
            start = p.CreatedAt.ToString("o"),
            end = p.ActualCompletionDate?.ToString("o"),
            milestones = p.Milestones?.Select(m => new
            {
                uuid = m.Id,
                title = m.Description,
                schedule = new
                {
                    tasks = new[]
                    {
                        new { type = "milestone", timing = new { within_date_range = new { start = m.TargetDate.ToString("o"), end = m.CompletedDate?.ToString("o") } } }
                    }
                }
            }) ?? Enumerable.Empty<object>(),
        }).ToList();

        var oscalDoc = new
        {
            plan_of_action_and_milestones = new
            {
                uuid = Guid.NewGuid().ToString(),
                metadata = new
                {
                    title = $"POA&M Export — System {systemId}",
                    last_modified = DateTime.UtcNow.ToString("o"),
                    version = "1.0.0",
                    oscal_version = "1.1.2",
                },
                poam_items = oscalPoams,
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(oscalDoc, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        });
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    public async Task<byte[]> ExportCsvAsync(
        string systemId,
        string? statusFilter = null,
        string? severityFilter = null,
        bool includeAll = false,
        CancellationToken ct = default)
    {
        var items = await GetFilteredPoamsForExport(systemId, statusFilter, severityFilter, includeAll, ct);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("ID,SecurityControlNumber,Weakness,WeaknessSource,CatSeverity,Status,POC,POCEmail,ScheduledCompletionDate,ActualCompletionDate,ResourcesRequired,CostEstimate,ExternalTicketRef,Comments");

        foreach (var p in items)
        {
            sb.AppendLine(string.Join(",",
                CsvEscape(p.Id),
                CsvEscape(p.SecurityControlNumber),
                CsvEscape(p.Weakness),
                CsvEscape(p.WeaknessSource),
                CsvEscape(p.CatSeverity.ToString()),
                CsvEscape(p.Status.ToString()),
                CsvEscape(p.PointOfContact),
                CsvEscape(p.PocEmail ?? ""),
                CsvEscape(p.ScheduledCompletionDate.ToString("yyyy-MM-dd")),
                CsvEscape(p.ActualCompletionDate?.ToString("yyyy-MM-dd") ?? ""),
                CsvEscape(p.ResourcesRequired ?? ""),
                CsvEscape(p.CostEstimate?.ToString("F2") ?? ""),
                CsvEscape(p.ExternalTicketRef ?? ""),
                CsvEscape(p.Comments ?? "")));
        }

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    private async Task<List<PoamItem>> GetFilteredPoamsForExport(
        string systemId,
        string? statusFilter,
        string? severityFilter,
        bool includeAll,
        CancellationToken ct)
    {
        var query = _db.PoamItems
            .AsNoTracking()
            .Include(p => p.Milestones)
            .Where(p => p.RegisteredSystemId == systemId);

        if (!includeAll)
        {
            if (!string.IsNullOrEmpty(statusFilter) && Enum.TryParse<PoamStatus>(statusFilter, true, out var status))
                query = query.Where(p => p.Status == status);
            if (!string.IsNullOrEmpty(severityFilter) && Enum.TryParse<CatSeverity>(severityFilter, true, out var severity))
                query = query.Where(p => p.CatSeverity == severity);
        }

        return await query.OrderBy(p => p.SecurityControlNumber).ToListAsync(ct);
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}

// ─── Result Types ────────────────────────────────────────────────────────────

public class BulkCreateResult
{
    public int Created { get; set; }
    public int SkippedDuplicates { get; set; }
    public List<BulkCreateItemResult> Results { get; set; } = new();
}

public class BulkCreateItemResult
{
    public string FindingId { get; set; } = "";
    public string? PoamId { get; set; }
    public string Status { get; set; } = "";
}

// ─── Result DTOs ────────────────────────────────────────────────────────────

public class PoamMetricsResult
{
    public int TotalOpen { get; set; }
    public int Overdue { get; set; }
    public int CatICount { get; set; }
    public int CatIICount { get; set; }
    public int CatIIICount { get; set; }
    public int ExpiringWithin30Days { get; set; }
    public double AvgDaysToClose { get; set; }
    public List<StatusCount> ByStatus { get; set; } = new();
}

public class StatusCount
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ComponentPoamSummary
{
    public string ComponentId { get; set; } = string.Empty;
    public int TotalPoams { get; set; }
    public int OpenCount { get; set; }
    public int OverdueCount { get; set; }
    public CatSeverity? HighestSeverity { get; set; }
    public List<PoamItem> Items { get; set; } = new();
}

// ─── Trend Result DTOs ──────────────────────────────────────────────────────

public class PoamTrendResult
{
    public string SystemId { get; set; } = string.Empty;
    public string Period { get; set; } = "monthly";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<TrendDataPoint> OpenOverTime { get; set; } = new();
    public List<TrendDataPoint> ClosureRates { get; set; } = new();
    public List<AgingBucket> AgingBreakdown { get; set; } = new();
    public List<TrendDataPoint> TimeToCloseDistribution { get; set; } = new();
}

public class TrendDataPoint
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}

public class AgingBucket
{
    public string Label { get; set; } = string.Empty;
    public int CatI { get; set; }
    public int CatII { get; set; }
    public int CatIII { get; set; }
}

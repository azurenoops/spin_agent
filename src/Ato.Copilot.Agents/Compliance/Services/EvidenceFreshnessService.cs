using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Tracks evidence collection freshness per control+subscription pair.
/// AutomatedAzure TTL: 24 hours. ManualUpload TTL: 2160 hours (90 days).
/// Fires <see cref="EvidenceAuditEventType.StaleAlertFired"/> events when records expire.
/// </summary>
public class EvidenceFreshnessService : IEvidenceFreshnessService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IEvidenceAuditService _auditService;
    private readonly ILogger<EvidenceFreshnessService> _logger;

    /// <summary>Automated evidence TTL — 24 hours.</summary>
    public const int AutomatedFreshnessWindowHours = 24;
    /// <summary>Manual upload TTL — 90 days.</summary>
    public const int ManualFreshnessWindowHours = 2160;

    public EvidenceFreshnessService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IEvidenceAuditService auditService,
        ILogger<EvidenceFreshnessService> logger)
    {
        _dbFactory = dbFactory;
        _auditService = auditService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordCollectionAsync(
        string controlId,
        string subscriptionId,
        EvidenceSourceType sourceType,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.Set<EvidenceFreshnessRecord>()
            .FirstOrDefaultAsync(r =>
                r.ControlId == controlId && r.SubscriptionId == subscriptionId,
                cancellationToken);

        var windowHours = sourceType == EvidenceSourceType.ManualUpload
            ? ManualFreshnessWindowHours
            : AutomatedFreshnessWindowHours;

        if (existing != null)
        {
            existing.LastCollectedAt = DateTime.UtcNow;
            existing.FreshnessWindowHours = windowHours;
            existing.EvidenceSourceType = sourceType;
        }
        else
        {
            var record = new EvidenceFreshnessRecord
            {
                ControlId = controlId,
                SubscriptionId = subscriptionId,
                EvidenceSourceType = sourceType,
                FreshnessWindowHours = windowHours,
                LastCollectedAt = DateTime.UtcNow
            };
            db.Set<EvidenceFreshnessRecord>().Add(record);
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Freshness recorded for {ControlId} / {Sub} (window: {Hours}h, source: {Source})",
            controlId, subscriptionId, windowHours, sourceType);
    }

    /// <inheritdoc />
    public async Task<EvidenceFreshnessRecord?> GetFreshnessAsync(
        string controlId,
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.Set<EvidenceFreshnessRecord>()
            .FirstOrDefaultAsync(r =>
                r.ControlId == controlId && r.SubscriptionId == subscriptionId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EvidenceFreshnessRecord>> GetStaleEvidenceAsync(
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;

        var query = db.Set<EvidenceFreshnessRecord>().AsQueryable();

        if (!string.IsNullOrEmpty(subscriptionId))
            query = query.Where(r => r.SubscriptionId == subscriptionId);

        // Load all records and evaluate IsStale in memory (computed property)
        var all = await query.ToListAsync(cancellationToken);
        return all.Where(r => r.IsStale).ToList();
    }

    /// <inheritdoc />
    public async Task<int> FireStalenessAlertsAsync(
        CancellationToken cancellationToken = default)
    {
        var staleRecords = await GetStaleEvidenceAsync(cancellationToken: cancellationToken);
        var fired = 0;

        foreach (var record in staleRecords)
        {
            try
            {
                await _auditService.RecordEventAsync(
                    EvidenceAuditEventType.StaleAlertFired,
                    record.ControlId,
                    "system",
                    $"Evidence for control {record.ControlId} is stale. " +
                    $"Last collected: {record.LastCollectedAt:u}. " +
                    $"Expired: {record.StaleAfter:u}.",
                    record.SubscriptionId,
                    cancellationToken: cancellationToken);

                fired++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to fire staleness alert for {ControlId} / {Sub}",
                    record.ControlId, record.SubscriptionId);
            }
        }

        if (fired > 0)
        {
            _logger.LogInformation(
                "Fired {Count} staleness alerts for overdue evidence records", fired);
        }

        return fired;
    }
}

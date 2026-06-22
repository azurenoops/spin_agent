using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Immutable evidence audit trail. Records every evidence lifecycle event:
/// collection, mapping, archival, staleness alerts, and manual uploads.
/// Fulfills §418 AC-5 (every automated collection writes an audit event) and
/// §5 Mission Integrity (evidence traceability is constitutional).
/// </summary>
public class EvidenceAuditService : IEvidenceAuditService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly ILogger<EvidenceAuditService> _logger;

    public EvidenceAuditService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILogger<EvidenceAuditService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordEventAsync(
        EvidenceAuditEventType eventType,
        string controlId,
        string actorId,
        string description,
        string? subscriptionId = null,
        string? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var auditEvent = new EvidenceAuditEvent
        {
            EventType = eventType,
            ControlId = controlId,
            ActorId = actorId,
            Description = description,
            SubscriptionId = subscriptionId,
            Metadata = metadata,
            OccurredAt = DateTime.UtcNow
        };

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.Set<EvidenceAuditEvent>().Add(auditEvent);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Audit event recorded: {EventType} for {ControlId} by {Actor}",
            eventType, controlId, actorId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EvidenceAuditEvent>> GetAuditTrailAsync(
        string? controlId = null,
        string? subscriptionId = null,
        int days = 30,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var cutoff = DateTime.UtcNow.AddDays(-days);

        var query = db.Set<EvidenceAuditEvent>()
            .Where(e => e.OccurredAt >= cutoff)
            .AsQueryable();

        if (!string.IsNullOrEmpty(controlId))
            query = query.Where(e => e.ControlId == controlId);

        if (!string.IsNullOrEmpty(subscriptionId))
            query = query.Where(e => e.SubscriptionId == subscriptionId);

        return await query
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
    }
}

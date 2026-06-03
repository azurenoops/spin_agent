using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements <see cref="ICapabilityHistoryService"/> using <see cref="IDbContextFactory{AtoCopilotContext}"/>
/// to provide thread-safe access to capability lifecycle history.
/// </summary>
public class CapabilityHistoryService : ICapabilityHistoryService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly ILogger<CapabilityHistoryService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CapabilityHistoryService"/>.
    /// </summary>
    public CapabilityHistoryService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILogger<CapabilityHistoryService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordEventAsync(
        string capabilityId,
        CapabilityHistoryEventType eventType,
        string? previousValue = null,
        string? newValue = null,
        string? notes = null,
        string actorId = "system",
        string actorName = "System",
        CancellationToken cancellationToken = default)
    {
        var evt = new CapabilityHistoryEvent
        {
            Id = Guid.NewGuid(),
            CapabilityId = capabilityId,
            EventType = eventType,
            PreviousValue = previousValue,
            NewValue = newValue,
            Notes = notes,
            ActorId = actorId,
            ActorName = actorName,
            OccurredAt = DateTimeOffset.UtcNow
        };

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.CapabilityHistoryEvents.Add(evt);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Recorded {EventType} history event for capability {CapabilityId} by {ActorId}",
            eventType, capabilityId, actorId);
    }

    /// <inheritdoc />
    public async Task<List<CapabilityHistoryEvent>> GetHistoryAsync(
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CapabilityHistoryEvents
            .Where(e => e.CapabilityId == capabilityId)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<CapabilityHistoryEvent>> GetRecentEventsAsync(
        int maxCount = 50,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CapabilityHistoryEvents
            .OrderByDescending(e => e.OccurredAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }
}

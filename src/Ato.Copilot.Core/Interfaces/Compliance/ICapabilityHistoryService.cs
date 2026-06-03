using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for recording and querying CSP capability lifecycle history events.
/// Provides a full audit trail for capability create, update, remap, and review operations.
/// </summary>
public interface ICapabilityHistoryService
{
    /// <summary>
    /// Records a lifecycle event for the specified capability.
    /// </summary>
    /// <param name="capabilityId">The capability this event belongs to.</param>
    /// <param name="eventType">Type of lifecycle event.</param>
    /// <param name="previousValue">JSON snapshot of value before change (null for Created).</param>
    /// <param name="newValue">JSON snapshot of new value after change.</param>
    /// <param name="notes">Optional human-readable notes.</param>
    /// <param name="actorId">ID of the user or system triggering the event.</param>
    /// <param name="actorName">Display name of the actor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordEventAsync(
        string capabilityId,
        CapabilityHistoryEventType eventType,
        string? previousValue = null,
        string? newValue = null,
        string? notes = null,
        string actorId = "system",
        string actorName = "System",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all history events for a given capability, ordered by <see cref="CapabilityHistoryEvent.OccurredAt"/> descending.
    /// </summary>
    /// <param name="capabilityId">The capability to query history for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<List<CapabilityHistoryEvent>> GetHistoryAsync(
        string capabilityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent N events across all capabilities, ordered by <see cref="CapabilityHistoryEvent.OccurredAt"/> descending.
    /// </summary>
    /// <param name="maxCount">Maximum number of events to return (default 50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<List<CapabilityHistoryEvent>> GetRecentEventsAsync(
        int maxCount = 50,
        CancellationToken cancellationToken = default);
}

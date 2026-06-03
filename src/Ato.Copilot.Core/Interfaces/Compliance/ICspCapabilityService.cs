using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for managing CSP capability lifecycle: create, review gate, and parent remapping.
/// </summary>
public interface ICspCapabilityService
{
    /// <summary>
    /// Creates a new CSP capability.
    /// Manual creates always set <c>NeedsReview = true</c> and
    /// <c>Status = <see cref="CapabilityStatus.NeedsReview"/></c> (#160 gate).
    /// </summary>
    /// <param name="name">Display name of the capability.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="parentCapabilityId">Optional parent capability ID for hierarchical mapping (#161).</param>
    /// <param name="createdBy">ID of the creating user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created capability.</returns>
    Task<CspCapability> CreateCapabilityAsync(
        string name,
        string? description,
        string? parentCapabilityId,
        string createdBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a capability by its ID. Returns null if not found.
    /// </summary>
    Task<CspCapability?> GetCapabilityAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the NeedsReview flag and sets the capability status to Active.
    /// Records a <see cref="CapabilityHistoryEventType.ReviewCleared"/> history event.
    /// </summary>
    /// <param name="id">The capability ID.</param>
    /// <param name="clearedBy">ID of the user clearing the review.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated capability.</returns>
    Task<CspCapability> ClearReviewAsync(string id, string clearedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remaps a capability to a new parent (or makes it a root by passing null).
    /// Records a <see cref="CapabilityHistoryEventType.ParentChanged"/> history event (#161).
    /// </summary>
    /// <param name="id">The capability ID to remap.</param>
    /// <param name="newParentId">The new parent capability ID, or null to make root.</param>
    /// <param name="remappedBy">ID of the user performing the remap.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated capability.</returns>
    Task<CspCapability> RemapParentAsync(string id, string? newParentId, string remappedBy, CancellationToken cancellationToken = default);
}

using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for deviation management: creating, reviewing, revoking, extending,
/// listing, and querying compliance deviations (false positives, risk acceptances, waivers).
/// </summary>
public interface IDeviationService
{
    // ─── CRUD ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a new deviation request in Pending status.
    /// Validates: no duplicate active deviation for the same finding (409 Conflict),
    /// valid enum values, expiration in the future, review cycle within max.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="request">Deviation creation input.</param>
    /// <param name="requestedBy">User ID of the requestor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created Deviation entity.</returns>
    Task<Deviation> CreateDeviationAsync(
        string systemId,
        CreateDeviationRequest request,
        string requestedBy = "mcp-user",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single deviation by ID with navigation properties loaded.
    /// </summary>
    /// <param name="deviationId">Deviation ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Deviation entity, or null if not found.</returns>
    Task<Deviation?> GetDeviationAsync(
        string deviationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get full deviation detail including finding/POA&amp;M refs, evidence, and audit trail.
    /// </summary>
    /// <param name="deviationId">Deviation ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>DeviationDetail DTO, or null if not found.</returns>
    Task<DeviationDetail?> GetDeviationDetailAsync(
        string deviationId,
        CancellationToken cancellationToken = default);

    // ─── Listing & Summary ───────────────────────────────────────────────────

    /// <summary>
    /// List deviations for a system with optional filtering and pagination.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="typeFilter">Optional DeviationType filter.</param>
    /// <param name="statusFilter">Optional DeviationStatus filter.</param>
    /// <param name="severityFilter">Optional CatSeverity filter.</param>
    /// <param name="search">Optional text search (control ID, justification).</param>
    /// <param name="expiringWithinDays">Optional: only show deviations expiring within N days.</param>
    /// <param name="page">Page number (1-based, default 1).</param>
    /// <param name="pageSize">Items per page (default 50, max 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated deviation list.</returns>
    Task<DeviationListResponse> ListDeviationsAsync(
        string systemId,
        string? typeFilter = null,
        string? statusFilter = null,
        string? severityFilter = null,
        string? search = null,
        int? expiringWithinDays = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get summary counts for deviation metric cards on the dashboard.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>DeviationSummary with counts by status, severity, and expiration window.</returns>
    Task<DeviationSummary> GetDeviationSummaryAsync(
        string systemId,
        CancellationToken cancellationToken = default);

    // ─── Workflow ────────────────────────────────────────────────────────────

    /// <summary>
    /// Review (approve or deny) a pending deviation.
    /// For CAT I deviations: ISSM calls record a recommendation (two-step);
    /// AO calls render the final decision.
    /// On approval: linked finding status transitions to FalsePositive or Accepted;
    /// linked POA&amp;M status transitions to RiskAccepted.
    /// Logs a DashboardActivity audit record.
    /// </summary>
    /// <param name="deviationId">Deviation ID.</param>
    /// <param name="request">Review decision input.</param>
    /// <param name="reviewedBy">User ID of the reviewer.</param>
    /// <param name="reviewerRole">Role of the reviewer: "ISSM" or "AO".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated Deviation entity.</returns>
    Task<Deviation> ReviewDeviationAsync(
        string deviationId,
        ReviewDeviationRequest request,
        string reviewedBy = "mcp-user",
        string reviewerRole = "ISSM",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke an approved deviation, reverting linked finding to Open and POA&amp;M to Ongoing.
    /// Logs a DashboardActivity audit record.
    /// </summary>
    /// <param name="deviationId">Deviation ID.</param>
    /// <param name="request">Revocation reason input.</param>
    /// <param name="revokedBy">User ID performing the revocation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated Deviation entity.</returns>
    Task<Deviation> RevokeDeviationAsync(
        string deviationId,
        RevokeDeviationRequest request,
        string revokedBy = "mcp-user",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extend the expiration date of an approved deviation.
    /// Validates that the new date does not exceed MaxReviewCycleDays (365) from today.
    /// Logs a DashboardActivity audit record.
    /// </summary>
    /// <param name="deviationId">Deviation ID.</param>
    /// <param name="request">Extension input with new expiration date.</param>
    /// <param name="extendedBy">User ID performing the extension.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated Deviation entity.</returns>
    Task<Deviation> ExtendDeviationAsync(
        string deviationId,
        ExtendDeviationRequest request,
        string extendedBy = "mcp-user",
        CancellationToken cancellationToken = default);

    // ─── Boundary-Scoped Waiver Queries ──────────────────────────────────────

    /// <summary>
    /// Get the list of control IDs that have active approved waivers for a specific boundary.
    /// Used by gap analysis to exclude waived controls from coverage calculations.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="boundaryDefinitionId">AuthorizationBoundaryDefinition ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of waived control IDs.</returns>
    Task<List<string>> GetWaivedControlsForBoundaryAsync(
        string systemId,
        string boundaryDefinitionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the count of active (approved) deviations for a system. Used by the System Detail
    /// metric card.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Active deviation count.</returns>
    Task<int> GetActiveDeviationCountAsync(
        string systemId,
        CancellationToken cancellationToken = default);

    // ─── Expiration & Orphan Handling ────────────────────────────────────────

    /// <summary>
    /// Expire all approved deviations past their expiration date:
    /// set status to Expired, revert linked finding to Open, revert linked POA&amp;M to Ongoing,
    /// and log DashboardActivity audit records.
    /// Called by DeviationExpirationService (daily 06:00 UTC).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of deviations expired.</returns>
    Task<int> ExpireDeviationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Handle orphaned deviations when a linked finding is deleted: transition the
    /// deviation to a terminal state and log an audit record.
    /// </summary>
    /// <param name="findingId">The deleted finding ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of deviations orphaned.</returns>
    Task<int> HandleOrphanedDeviationsAsync(
        string findingId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handle boundary deletion by reassigning scoped waivers to the Primary boundary
    /// and setting them to Pending for re-review.
    /// </summary>
    /// <param name="deletedBoundaryId">The deleted boundary definition ID.</param>
    /// <param name="systemId">The system owning the boundary.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of waivers reassigned.</returns>
    Task<int> HandleBoundaryDeletionAsync(
        string deletedBoundaryId,
        string systemId,
        CancellationToken cancellationToken = default);
}

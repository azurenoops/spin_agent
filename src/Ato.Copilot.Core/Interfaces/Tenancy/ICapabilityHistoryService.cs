using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Append-only writer + paginated reader for
/// <see cref="CapabilityHistoryEvent"/> rows. Feature 050 FR-004 / FR-005 /
/// FR-014 / FR-015 / FR-016.
/// </summary>
/// <remarks>
/// <para>
/// All callers MUST be scoped to a CSP-Admin tenant context. The service
/// is intentionally devoid of update / delete methods — history rows are
/// immutable once written (FR-004). A unit test asserts the interface
/// surface is exactly <c>{ AppendAsync, ListAsync }</c>.
/// </para>
/// <para>
/// <c>AppendAsync</c> deliberately does NOT call <c>SaveChangesAsync</c>;
/// the caller (always <c>CspInheritedComponentService</c> or the Remap
/// pipeline) owns the enclosing transaction so the audit row and the
/// state change commit atomically.
/// </para>
/// </remarks>
public interface ICapabilityHistoryService
{
    /// <summary>
    /// Append a new history row inside the caller's ambient transaction.
    /// Caller is responsible for opening / committing the transaction;
    /// this method calls <c>AddAsync</c> only — it does NOT call
    /// <c>SaveChangesAsync</c>. This is what makes the write atomic with
    /// the state change that triggered it.
    /// </summary>
    /// <param name="db">
    /// The <see cref="AtoCopilotContext"/> instance hosting the open
    /// transaction. The service does not own the lifetime.
    /// </param>
    /// <param name="capabilityId">FK to the parent capability.</param>
    /// <param name="tenantId">CSP tenant performing the operation.</param>
    /// <param name="eventType">One of six lifecycle events.</param>
    /// <param name="actorOid">Caller's <c>oid</c> claim.</param>
    /// <param name="summary">Human-readable description, ≤ 500 chars.</param>
    /// <param name="metadata">
    /// Optional structured payload, serialized to JSON. Pass <c>null</c>
    /// when no metadata applies. Shape rules per <c>data-model.md § 1.4</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly-created event row (not yet persisted).</returns>
    Task<CapabilityHistoryEvent> AppendAsync(
        AtoCopilotContext db,
        Guid capabilityId,
        Guid tenantId,
        CapabilityHistoryEventType eventType,
        string actorOid,
        string summary,
        object? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// List events for one capability scoped to one tenant, ordered by
    /// <see cref="CapabilityHistoryEvent.OccurredAt"/> descending then
    /// <see cref="CapabilityHistoryEvent.Id"/> descending for stable
    /// pagination.
    /// </summary>
    /// <param name="capabilityId">Capability whose history to fetch.</param>
    /// <param name="tenantId">Caller's tenant (filter, FR-013).</param>
    /// <param name="page">1-based page index. Clamped to <c>≥ 1</c>.</param>
    /// <param name="pageSize">
    /// Page size. Clamped to <c>[1, 200]</c>. Default 50 enforced at the
    /// endpoint layer, not here.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Page of events + total count.</returns>
    Task<CapabilityHistoryPage> ListAsync(
        Guid capabilityId,
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken ct = default);
}

/// <summary>
/// Page-of-events DTO returned by
/// <see cref="ICapabilityHistoryService.ListAsync"/>.
/// </summary>
/// <param name="Items">Events for the requested page.</param>
/// <param name="Page">1-based index of the page returned.</param>
/// <param name="PageSize">Page size actually used (after clamping).</param>
/// <param name="Total">Total event count across all pages.</param>
public sealed record CapabilityHistoryPage(
    IReadOnlyList<CapabilityHistoryEvent> Items,
    int Page,
    int PageSize,
    int Total);

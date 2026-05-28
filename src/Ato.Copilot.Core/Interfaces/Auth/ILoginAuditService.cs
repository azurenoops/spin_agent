using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Auth;

namespace Ato.Copilot.Core.Interfaces.Auth;

/// <summary>
/// Feature 051 — single SRP boundary for writing and reading
/// <see cref="LoginAuditEvent"/> rows. All endpoints (HTTP, VS Code,
/// Teams) route through this service — never call
/// <c>AtoCopilotContext.LoginAuditEvents.AddAsync</c> directly.
/// </summary>
/// <remarks>
/// Per <c>contracts/internal-services.md § 1</c>. The interface is
/// intentionally exactly three methods (<see cref="AppendAsync"/>,
/// <see cref="ListAsync"/>, <see cref="ListSystemTenantAsync"/>); the
/// reflection test
/// <c>LoginAuditServiceTests.InterfaceMethods_Are_Exactly_Three</c>
/// guards against accidental addition.
/// </remarks>
public interface ILoginAuditService
{
    /// <summary>
    /// Append a new audit row to the caller's <paramref name="db"/>
    /// instance. MUST NOT call <c>SaveChangesAsync</c> — the caller owns
    /// the enclosing transaction so the audit row and any neighbouring
    /// state change commit atomically (R6 / Feature-050 SRP parity, same
    /// shape as <c>CapabilityHistoryService.AppendAsync</c>). The
    /// returned <see cref="LoginAuditEvent"/> has its <c>Id</c> and
    /// <c>OccurredAt</c> populated and is tracked by <paramref name="db"/>.
    /// </summary>
    Task<LoginAuditEvent> AppendAsync(
        AtoCopilotContext db,
        LoginAuditEventDraft draft,
        CancellationToken ct = default);

    /// <summary>
    /// List audit rows for a tenant in reverse chronological order.
    /// Subject to the automatic <c>[TenantScoped]</c> query filter, so
    /// callers without tenant context get no rows.
    /// </summary>
    Task<IReadOnlyList<LoginAuditEvent>> ListAsync(
        Guid tenantId,
        DateTimeOffset? since = null,
        int take = 100,
        CancellationToken ct = default);

    /// <summary>
    /// SOC-analyst read path for <c>SYSTEM_TENANT_ID</c> rows
    /// (per <c>research.md § R9</c>). Requires the <c>Auth.SocAnalyst</c>
    /// claim on the calling identity. Throws
    /// <see cref="UnauthorizedAccessException"/> otherwise.
    /// </summary>
    Task<IReadOnlyList<LoginAuditEvent>> ListSystemTenantAsync(
        DateTimeOffset? since = null,
        int take = 100,
        CancellationToken ct = default);
}

/// <summary>
/// Input draft for <see cref="ILoginAuditService.AppendAsync"/>.
/// The service populates <see cref="LoginAuditEvent.Id"/> and
/// <see cref="LoginAuditEvent.OccurredAt"/> server-side.
/// </summary>
/// <remarks>
/// Per <c>contracts/internal-services.md § 1.2</c>. <see cref="Oid"/> is
/// constrained to 254 chars per data-model.md § 1.6; the service rejects
/// longer values with <see cref="ArgumentException"/>.
/// </remarks>
public sealed record LoginAuditEventDraft(
    LoginAuditEventType EventType,
    string? Oid,
    string? Tid,
    Guid EffectiveTenantId,
    string CorrelationId,
    string SourceIp,
    string UserAgent,
    LoginSurface Surface,
    LoginErrorClass? ErrorClass = null,
    string? MetadataJson = null);

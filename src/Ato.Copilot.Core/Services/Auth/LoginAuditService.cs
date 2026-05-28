using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Auth;

/// <summary>
/// Feature 051 (FR-032 / FR-033 / FR-034 / FR-036a) — append-only writer
/// and (eventually) paginated reader for <see cref="LoginAuditEvent"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AppendAsync"/> deliberately does NOT call
/// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>. Per
/// <c>contracts/internal-services.md § 1.3</c> and the R6 / Feature-050
/// SRP parity, the caller owns the enclosing transaction so the audit
/// row and any neighbouring state change commit atomically. Failing to
/// call <c>SaveChangesAsync</c> here is the contract — not a bug.
/// </para>
/// <para>
/// <see cref="ListAsync"/> and <see cref="ListSystemTenantAsync"/> are
/// intentionally stubbed pending Phase 7 tasks T085 / T086 — the Phase 2
/// foundational work only needs the write path to be live so Phases 3–6
/// can begin appending rows. Calling them today throws
/// <see cref="NotImplementedException"/>.
/// </para>
/// </remarks>
public sealed class LoginAuditService : ILoginAuditService
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ILogger<LoginAuditService> _logger;

    public LoginAuditService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ILogger<LoginAuditService> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LoginAuditEvent> AppendAsync(LoginAuditEventDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        // Validation mirrors data-model.md § 1.6 — fail fast on the
        // properties that would otherwise overflow the column caps.
        if (draft.Oid is { Length: > 254 })
        {
            throw new ArgumentException(
                "Oid exceeds 254 characters.", nameof(draft));
        }
        if (draft.Tid is { Length: > 254 })
        {
            throw new ArgumentException(
                "Tid exceeds 254 characters.", nameof(draft));
        }
        if (draft.CorrelationId.Length > 64)
        {
            throw new ArgumentException(
                "CorrelationId exceeds 64 characters.", nameof(draft));
        }
        if (draft.SourceIp.Length > 45)
        {
            throw new ArgumentException(
                "SourceIp exceeds 45 characters.", nameof(draft));
        }
        if (draft.UserAgent.Length > 512)
        {
            throw new ArgumentException(
                "UserAgent exceeds 512 characters.", nameof(draft));
        }
        if (draft.MetadataJson is { Length: > 2000 })
        {
            throw new ArgumentException(
                "MetadataJson exceeds 2000 characters.", nameof(draft));
        }

        var entity = new LoginAuditEvent
        {
            Id = Guid.NewGuid(),
            EventType = draft.EventType,
            Oid = draft.Oid,
            Tid = draft.Tid,
            EffectiveTenantId = draft.EffectiveTenantId,
            CorrelationId = draft.CorrelationId,
            SourceIp = draft.SourceIp,
            UserAgent = draft.UserAgent,
            Surface = draft.Surface,
            OccurredAt = DateTimeOffset.UtcNow,
            ErrorClass = draft.ErrorClass,
            MetadataJson = draft.MetadataJson,
        };

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await db.LoginAuditEvents.AddAsync(entity, ct).ConfigureAwait(false);
        // Intentionally NO SaveChangesAsync — caller owns the transaction.
        // Phases 3+ will refactor the endpoint flow to share a DbContext so
        // the row actually persists; Phase 2 just pins the contract.

        _logger.LogDebug(
            "LoginAuditService.AppendAsync queued {EventType} (Surface={Surface}, Tenant={TenantId})",
            entity.EventType, entity.Surface, entity.EffectiveTenantId);

        return entity;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LoginAuditEvent>> ListAsync(
        Guid tenantId,
        DateTimeOffset? since = null,
        int take = 100,
        CancellationToken ct = default)
        => throw new NotImplementedException(
            "T085 (Phase 7) — per-tenant audit query lands in the SOC-analyst story.");

    /// <inheritdoc />
    public Task<IReadOnlyList<LoginAuditEvent>> ListSystemTenantAsync(
        DateTimeOffset? since = null,
        int take = 100,
        CancellationToken ct = default)
        => throw new NotImplementedException(
            "T086 (Phase 7) — SYSTEM_TENANT_ID forensic read with claim gate.");
}

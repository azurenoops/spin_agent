using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Auth;

/// <summary>
/// Append-only audit-trail row for a single authentication-related event
/// across the four ATO Copilot surfaces. Feature 051 FR-032 (schema),
/// FR-033 (privacy-preserving failure logging), FR-034 / FR-035 (throttle
/// audit), FR-036a (13-month hot + indefinite cold archive retention).
/// </summary>
/// <remarks>
/// Rows are tenant-scoped via <see cref="EffectiveTenantId"/>; pre-session
/// and unmapped events use <c>SYSTEM_TENANT_ID</c> per clarification Q2
/// (2026-05-28) so the automatic tenant query filter applies uniformly.
///
/// The entity is immutable in practice — <see cref="Interfaces.Auth.ILoginAuditService"/>
/// exposes only <c>AppendAsync</c> / <c>ListAsync</c> / <c>ListSystemTenantAsync</c>;
/// there is no update or delete path. The daily
/// <see cref="Interfaces.Auth.ILoginAuditArchiveService"/> hosted service moves
/// rows older than 13 months to immutable cold storage.
/// </remarks>
[TenantScoped]
public class LoginAuditEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public LoginAuditEventType EventType { get; set; }

    /// <summary>Entra <c>oid</c> claim, when present. Null for pre-Entra events.</summary>
    [MaxLength(254)]
    public string? Oid { get; set; }

    /// <summary>
    /// Entra <c>tid</c> claim (the user's home directory). Captured for
    /// forensic context only; does NOT determine tenant ownership per Q2.
    /// </summary>
    [MaxLength(254)]
    public string? Tid { get; set; }

    /// <summary>
    /// Tenant that owns this audit row. <c>SYSTEM_TENANT_ID</c> for
    /// pre-session and <see cref="LoginErrorClass.NoTenantAssignment"/>
    /// rows per clarification Q2. Cascade-deleted on tenant offboarding.
    /// </summary>
    public Guid EffectiveTenantId { get; set; }

    /// <summary>
    /// Correlates events across surfaces (e.g. the same OAuthPrompt flow
    /// in Teams and the callback to the dashboard). Sourced from the
    /// existing <c>CorrelationIdMiddleware</c>.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string CorrelationId { get; set; } = string.Empty;

    [Required]
    [MaxLength(45)]
    public string SourceIp { get; set; } = string.Empty;

    [Required]
    [MaxLength(512)]
    public string UserAgent { get; set; } = string.Empty;

    [Required]
    public LoginSurface Surface { get; set; }

    /// <summary>
    /// Server-side UTC timestamp at write time. Never accepts a
    /// client-supplied value.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set only when <see cref="EventType"/> is <see cref="LoginAuditEventType.LoginFailure"/>.</summary>
    public LoginErrorClass? ErrorClass { get; set; }

    /// <summary>
    /// Structured payload per event type. Shape per
    /// <c>specs/051-login/data-model.md § 1.5</c>. Null when no
    /// structured metadata is required. NEVER the literal string
    /// <c>"null"</c>.
    /// </summary>
    [MaxLength(2000)]
    public string? MetadataJson { get; set; }
}

using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Two-track audit writer (Serilog + persisted <see cref="WizardAuditEntry"/> rows)
/// for FR-097 — research §R12. Every mutating wizard action MUST be recorded
/// through this service.
/// </summary>
public interface IWizardAuditService
{
    /// <summary>
    /// Append a wizard audit entry. The implementation writes both:
    /// <list type="bullet">
    /// <item><description>A structured Serilog event under the <c>WizardAudit</c> enricher.</description></item>
    /// <item><description>A persistent <see cref="WizardAuditEntry"/> row.</description></item>
    /// </list>
    /// </summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="actorUserId">Acting user (matches <see cref="Person.Id"/> or <see cref="Person.EntraObjectId"/>).</param>
    /// <param name="action">Audit-action discriminator.</param>
    /// <param name="resourceType">Short discriminator for the affected resource type.</param>
    /// <param name="resourceId">Affected resource id (null for bulk actions).</param>
    /// <param name="beforeJson">JSON snapshot of the resource before the action (null on create).</param>
    /// <param name="afterJson">JSON snapshot of the resource after the action (null on delete).</param>
    /// <param name="effectsJson">Optional dependency-effect snapshot (cascade summary).</param>
    /// <param name="correlationId">Correlation id (matches the parent HTTP request when available).</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordAsync(
        Guid tenantId,
        Guid actorUserId,
        WizardAuditAction action,
        string resourceType,
        Guid? resourceId,
        string? beforeJson,
        string? afterJson,
        string? effectsJson,
        Guid correlationId,
        CancellationToken ct = default);
}

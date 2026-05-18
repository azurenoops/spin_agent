using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Singleton record describing the hosting Cloud Service Provider (CSP)
/// itself, NOT any per-customer tenant. At most one row exists per
/// deployment. Created lazily on first <c>CSP.Admin</c> sign-in in
/// <c>MultiTenant</c> mode; never created in <c>SingleTenant</c> mode.
/// See feature 048 spec FR-006 and US7.
/// </summary>
/// <remarks>
/// Marked <see cref="GlobalReferenceAttribute"/> because the row lives
/// outside any tenant scope and must be readable by every hosted tenant
/// (the dashboard renders <c>DisplayName</c> + <c>LogoUrl</c> in the header
/// and on inherited control source labels per FR-083).
/// </remarks>
[GlobalReference]
public class CspProfile
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Legal entity name (used on official ATO documents).</summary>
    [Required]
    [MaxLength(256)]
    public string LegalEntityName { get; set; } = string.Empty;

    /// <summary>Short name shown to all hosted tenants in the dashboard header.</summary>
    [Required]
    [MaxLength(64)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional logo URL rendered in the header.</summary>
    [MaxLength(2048)]
    public string? LogoUrl { get; set; }

    /// <summary>Primary support email for tenant-side issues.</summary>
    [MaxLength(254)]
    public string? PrimarySupportEmail { get; set; }

    /// <summary>Optional phone number, free-format.</summary>
    [MaxLength(40)]
    public string? SupportPhone { get; set; }

    /// <summary>
    /// Default classification floor enforced when a tenant has not set its own.
    /// Defaults to <see cref="ClassificationLevel.Unclassified"/>.
    /// </summary>
    public ClassificationLevel DefaultClassificationFloor { get; set; }
        = ClassificationLevel.Unclassified;

    /// <summary>
    /// Wizard step machine. <c>Pending</c> = row created but no fields filled,
    /// <c>InWizard</c> = at least one step submitted, <c>Active</c> = wizard
    /// finalized. The CSP-onboarding gate (FR-090) lifts only when this
    /// reaches <see cref="OnboardingState.Active"/>.
    /// </summary>
    public OnboardingState OnboardingState { get; set; } = OnboardingState.Pending;

    /// <summary>Set when <see cref="OnboardingState"/> transitions to <c>Active</c>.</summary>
    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    /// <summary>
    /// Set when the actor first POSTs to <c>/api/csp/onboarding/identity</c>
    /// with valid input. Drives the wizard's "currentStep" cursor (FR-090);
    /// re-posting the same step does NOT reset this column.
    /// </summary>
    public DateTimeOffset? IdentityCompletedAt { get; set; }

    /// <summary>Set when the actor first POSTs to <c>/api/csp/onboarding/support</c>.</summary>
    public DateTimeOffset? SupportCompletedAt { get; set; }

    /// <summary>Set when the actor first POSTs to <c>/api/csp/onboarding/classification</c>.</summary>
    public DateTimeOffset? ClassificationCompletedAt { get; set; }

    /// <summary>Audit: when the row was first inserted.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Audit: oid / sub of the actor that created the row.</summary>
    [Required]
    [MaxLength(254)]
    public string CreatedBy { get; set; } = "system";

    /// <summary>Audit: most-recent update.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Audit: oid / sub of the actor for the most-recent update.</summary>
    [MaxLength(254)]
    public string? UpdatedBy { get; set; }

    /// <summary>Concurrency token (rowversion / timestamp).</summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}

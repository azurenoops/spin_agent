namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Persistent audit row for FR-097. Written for every mutating wizard action and
/// independent of the Serilog audit stream (research §R12). One row per action; the
/// before/after JSON snapshots provide replay-grade history.
/// </summary>
public class WizardAuditEntry
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant.</summary>
    public Guid TenantId { get; set; }

    /// <summary>UTC timestamp.</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Acting user id.</summary>
    public Guid ActorUserId { get; set; }

    /// <summary>Action discriminator.</summary>
    public WizardAuditAction Action { get; set; }

    /// <summary>Short string discriminator for the affected resource type.</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>Affected resource id (null for bulk actions).</summary>
    public Guid? ResourceId { get; set; }

    /// <summary>JSON: serialized "before" snapshot of the resource (null on create).</summary>
    public string? BeforeJson { get; set; }

    /// <summary>JSON: serialized "after" snapshot of the resource (null on delete).</summary>
    public string? AfterJson { get; set; }

    /// <summary>JSON: dependency-effect snapshot (FR-097 cascade summary).</summary>
    public string? EffectsJson { get; set; }

    /// <summary>Correlation id (matches the parent HTTP request when available).</summary>
    public Guid CorrelationId { get; set; }
}

/// <summary>Audit-action discriminator (FR-097).</summary>
public enum WizardAuditAction
{
    OrganizationContextSaved,
    RoleAssigned,
    RoleRemoved,
    PersonCreated,
    PersonPromoted,
    EmassUploaded,
    EmassParsed,
    EmassCommitted,
    SspPdfUploaded,
    SspPdfExtracted,
    SspPdfImported,
    SubscriptionsSelected,
    SubscriptionRemoved,
    TemplateUploaded,
    TemplateRenamed,
    TemplateReplaced,
    TemplateMarkedDefault,
    TemplateDeleted,
    NarrativeSeedUploaded,
    NarrativeSeedReplaced,
    NarrativeSeedDeleted,
    WizardBootstrapAdminGranted,
    WizardAccessDenied,
}

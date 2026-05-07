namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Per-tenant Azure subscription registration (Step 5). Recorded after the admin grants
/// delegated ARM consent and selects subscriptions to bring under the wizard's scope.
/// </summary>
public class AzureSubscriptionRegistration
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Azure subscription guid.</summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>Last-known display name (refreshed each time visibility is confirmed).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Azure (Entra) tenant id of the subscription's directory.</summary>
    public Guid ParentTenantId { get; set; }

    /// <summary>Cloud environment (commercial vs. US Government).</summary>
    public AzureEnvironment Environment { get; set; } = AzureEnvironment.AzureCloud;

    /// <summary>Selection status (FR-074: <c>Unavailable</c> means the admin can no longer see it).</summary>
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Selected;

    /// <summary>Most recent confirmation that the admin can still see this subscription via ARM.</summary>
    public DateTimeOffset LastSeenVisibleAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UpdatedBy { get; set; }
}

/// <summary>Azure cloud environment.</summary>
public enum AzureEnvironment
{
    /// <summary>Commercial (public) Azure cloud.</summary>
    AzureCloud,
    /// <summary>Azure US Government cloud.</summary>
    AzureUSGovernment,
}

/// <summary>Selection status of a registered Azure subscription (FR-074).</summary>
public enum SubscriptionStatus
{
    /// <summary>Currently selected and visible to the admin.</summary>
    Selected,
    /// <summary>Selected but no longer visible via ARM; preserved (not deleted) until admin acts.</summary>
    Unavailable,
}

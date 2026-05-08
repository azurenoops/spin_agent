using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Poam;

/// <summary>
/// External ticketing system configuration per registered system.
/// Credentials are stored in Azure Key Vault — only the URI is persisted here.
/// </summary>
[TenantScoped]
public class TicketingIntegration
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK → RegisteredSystem.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>External provider type (Jira or ServiceNow).</summary>
    public TicketingProvider Provider { get; set; }

    /// <summary>Base URL of the ticketing system instance.</summary>
    [Required]
    [MaxLength(500)]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Jira project key or ServiceNow table name.</summary>
    [MaxLength(200)]
    public string? ProjectKeyOrTableName { get; set; }

    /// <summary>Jira issue type (e.g., "Task", "Bug").</summary>
    [MaxLength(100)]
    public string? IssueType { get; set; }

    /// <summary>Azure Key Vault secret URI for authentication credentials. NEVER the credential itself.</summary>
    [Required]
    [MaxLength(500)]
    public string KeyVaultSecretUri { get; set; } = string.Empty;

    /// <summary>JSON field mapping configuration.</summary>
    [MaxLength(4000)]
    public string? FieldMappingJson { get; set; }

    /// <summary>Whether periodic sync is enabled.</summary>
    public bool SyncEnabled { get; set; } = true;

    /// <summary>Sync reconciliation interval in minutes (default: 15).</summary>
    public int SyncIntervalMinutes { get; set; } = 15;

    /// <summary>UTC timestamp of last successful sync.</summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>Error message from last failed sync attempt.</summary>
    [MaxLength(1000)]
    public string? LastSyncError { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC last modification timestamp.</summary>
    public DateTime? ModifiedAt { get; set; }

    // ─── Navigation Properties ───────────────────────────────────────────────

    /// <summary>Navigation to parent RegisteredSystem.</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }
}

/// <summary>
/// Tracks sync state between a <see cref="PoamItem"/> and its external ticket.
/// </summary>
[TenantScoped]
public class PoamTicketSync
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK → PoamItem.</summary>
    [Required]
    [MaxLength(36)]
    public string PoamItemId { get; set; } = string.Empty;

    /// <summary>FK → TicketingIntegration.</summary>
    [Required]
    [MaxLength(36)]
    public string TicketingIntegrationId { get; set; } = string.Empty;

    /// <summary>External ticket ID (e.g., JIRA-123).</summary>
    [Required]
    [MaxLength(200)]
    public string ExternalTicketId { get; set; } = string.Empty;

    /// <summary>URL to the external ticket.</summary>
    [MaxLength(500)]
    public string? ExternalTicketUrl { get; set; }

    /// <summary>Current sync status.</summary>
    public TicketSyncStatus SyncStatus { get; set; } = TicketSyncStatus.Synced;

    /// <summary>UTC timestamp of last sync.</summary>
    public DateTime LastSyncAt { get; set; } = DateTime.UtcNow;

    /// <summary>Error from last failed sync.</summary>
    [MaxLength(1000)]
    public string? LastSyncError { get; set; }

    /// <summary>Raw status value from the external system.</summary>
    [MaxLength(100)]
    public string? ExternalStatusRaw { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ─── Navigation Properties ───────────────────────────────────────────────

    /// <summary>Navigation to the linked POA&amp;M item.</summary>
    public PoamItem? PoamItem { get; set; }

    /// <summary>Navigation to the ticketing integration configuration.</summary>
    public TicketingIntegration? TicketingIntegration { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Records that an org-user system has subscribed to a
/// CspInheritedCapability published by the CSP (UF-CSP-01 / spec-070).
/// Soft-deleted via <see cref="IsActive"/> = false rather than row removal
/// so the audit trail is preserved.
/// </summary>
public class CapabilitySubscription
{
    /// <summary>Surrogate PK (GUID string for consistency with rest of schema).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the tenant's RegisteredSystem (string id).</summary>
    [Required, MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>FK to the published CspInheritedCapability (Guid stored as string).</summary>
    [Required, MaxLength(36)]
    public string CspInheritedCapabilityId { get; set; } = string.Empty;

    /// <summary>UPN or OID of the user that created the subscription.</summary>
    [MaxLength(254)]
    public string SubscribedBy { get; set; } = "dashboard-user";

    /// <summary>UTC timestamp when the subscription was created.</summary>
    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// False when the user has unsubscribed (UF-CSP-03 / #228).
    /// Soft-delete preserves the audit trail.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

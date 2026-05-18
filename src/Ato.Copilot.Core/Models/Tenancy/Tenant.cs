using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Authoritative root of an authorization boundary. The <c>Tenants</c> table
/// itself cannot be tenant-scoped (chicken-and-egg), so it is marked
/// <see cref="GlobalReferenceAttribute"/>. All other tenant-scoped entities
/// reference this entity via <c>TenantId</c>.
/// See feature 048 spec FR-001 and data-model.md §1.1.
/// </summary>
[GlobalReference]
public class Tenant
{
    /// <summary>Surrogate key. Referenced by all tenant-scoped <c>TenantId</c> FKs.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Mapped to Entra <c>tid</c> claim for SSO. Null permitted for lab /
    /// air-gapped tenants. Unique when not null (filtered unique index).
    /// </summary>
    public Guid? EntraTenantId { get; set; }

    /// <summary>Human-readable name shown in UI.</summary>
    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Captured during onboarding; used on official documents.</summary>
    [MaxLength(300)]
    public string? LegalEntityName { get; set; }

    /// <summary>DoD service / agency (Army, Navy, Air Force, USCG, ISA, etc.).</summary>
    [MaxLength(120)]
    public string? DoDComponent { get; set; }

    [MaxLength(200)]
    public string? PrimaryPocName { get; set; }

    [MaxLength(254)]
    public string? PrimaryPocEmail { get; set; }

    [MaxLength(40)]
    public string? PrimaryPocPhone { get; set; }

    [MaxLength(200)]
    public string? HqAddressLine1 { get; set; }

    [MaxLength(200)]
    public string? HqAddressLine2 { get; set; }

    [MaxLength(120)]
    public string? HqCity { get; set; }

    [MaxLength(120)]
    public string? HqStateOrProvince { get; set; }

    [MaxLength(20)]
    public string? HqPostalCode { get; set; }

    /// <summary>ISO 3166-1 alpha-2 or alpha-3 country code expected.</summary>
    [MaxLength(80)]
    public string? HqCountry { get; set; }

    /// <summary>Drives default classification markings on generated documents.</summary>
    public ClassificationLevel DefaultClassificationLevel { get; set; } = ClassificationLevel.Unclassified;

    [MaxLength(200)]
    public string? AuthorizingOfficialName { get; set; }

    [MaxLength(254)]
    public string? AuthorizingOfficialEmail { get; set; }

    /// <summary>IANA timezone (e.g., <c>America/New_York</c>). Defaults to <c>UTC</c>.</summary>
    [Required]
    [MaxLength(64)]
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Lifecycle status. See FR-057..FR-059.</summary>
    public TenantStatus Status { get; set; } = TenantStatus.Active;

    /// <summary>Drives onboarding wizard routing.</summary>
    public OnboardingState OnboardingState { get; set; } = OnboardingState.Pending;

    /// <summary>UTC timestamp when the tenant row was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>OID of the actor who pre-provisioned, or <c>"system"</c> for self-onboard.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = "system";

    public DateTimeOffset? UpdatedAt { get; set; }

    [MaxLength(200)]
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Concurrency token. SQL Server <c>rowversion</c>; SQLite uses a
    /// trigger-managed shadow column.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}

namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Per-tenant organization profile captured during Step 1 of the onboarding wizard.
/// Singleton — one row per <see cref="TenantId"/>; replaced via update.
/// </summary>
public class OrganizationContext
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant (unique — one row per tenant).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Organization display name (FR-010, required).</summary>
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>Branch / service affiliation (FR-011).</summary>
    public BranchAffiliation Branch { get; set; } = BranchAffiliation.CivilAgency;

    /// <summary>Free-text qualifier; required when <see cref="Branch"/> = <c>IndustryPartnerOther</c>.</summary>
    public string? BranchQualifier { get; set; }

    /// <summary>Sub-organization (e.g., command, division, agency).</summary>
    public string? SubOrganization { get; set; }

    /// <summary>Default classification posture for systems onboarded under this tenant.</summary>
    public ClassificationPosture? ClassificationPosture { get; set; }

    /// <summary>External authoritative repository URL (e.g., eMASS, RSA Archer).</summary>
    public string? AuthoritativeRepositoryUrl { get; set; }

    /// <summary>Primary point-of-contact email (validated as RFC-5322 email).</summary>
    public string? PrimaryPocEmail { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UpdatedBy { get; set; }
}

/// <summary>Branch / service affiliation (FR-011).</summary>
public enum BranchAffiliation
{
    Army,
    Navy,
    AirForce,
    MarineCorps,
    SpaceForce,
    CoastGuard,
    CivilAgency,
    IndustryPartnerOther,
}

/// <summary>Classification posture (highest classification handled).</summary>
public enum ClassificationPosture
{
    Unclassified,
    CUI,
    Secret,
    TopSecret,
}

using System.ComponentModel.DataAnnotations;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// A compliance framework catalog (e.g., NIST 800-53 Rev. 5, Rev. 4, FedRAMP Rev. 5).
/// Each row represents a distinct versioned catalog whose controls were imported from OSCAL JSON.
/// </summary>
public class ComplianceFramework
{
    [Key] [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Short stable identifier used in settings (e.g., "NIST-800-53-R5").</summary>
    [Required] [MaxLength(100)]
    public string Identifier { get; set; } = string.Empty;

    /// <summary>Human-readable display name (e.g., "NIST 800-53 Rev. 5").</summary>
    [Required] [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Version string (e.g., "5.1.1", "4.0").</summary>
    [MaxLength(50)]
    public string Version { get; set; } = string.Empty;

    /// <summary>Publishing authority (e.g., "NIST", "GSA/FedRAMP", "DISA").</summary>
    [MaxLength(100)]
    public string Publisher { get; set; } = string.Empty;

    /// <summary>Raw GitHub URL for the OSCAL catalog JSON.</summary>
    [MaxLength(500)]
    public string? CatalogUrl { get; set; }

    /// <summary>OSCAL model type: "catalog" or "profile".</summary>
    [MaxLength(50)]
    public string OscalModelType { get; set; } = "catalog";

    /// <summary>When the catalog was last imported from the source.</summary>
    public DateTime? ImportedAt { get; set; }

    /// <summary>Total number of controls (base + enhancements) after import.</summary>
    public int ControlCount { get; set; }

    /// <summary>Whether this framework is active/visible in the UI.</summary>
    public bool IsActive { get; set; } = true;

    // ─── Navigation ─────────────────────────────────────────────────────────
    public ICollection<FrameworkControl> Controls { get; set; } = new List<FrameworkControl>();
    public ICollection<FrameworkBaseline> Baselines { get; set; } = new List<FrameworkBaseline>();
}

/// <summary>
/// A single control within a versioned compliance framework catalog.
/// Stores the OSCAL-parsed control data without any baseline-specific information.
/// </summary>
public class FrameworkControl
{
    [Key] [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required] [MaxLength(36)]
    public string FrameworkId { get; set; } = string.Empty;

    /// <summary>Control identifier within its framework (e.g., "AC-2", "AC-2(1)", "SP_800_171_03.01.01").</summary>
    [Required] [MaxLength(50)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Family abbreviation (e.g., "AC" for 800-53, "SP_800_171_03.01" for 800-171).</summary>
    [Required] [MaxLength(50)]
    public string Family { get; set; } = string.Empty;

    /// <summary>Control title (e.g., "Account Management").</summary>
    [Required] [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Full description / statement prose.</summary>
    public string? Description { get; set; }

    /// <summary>Parent control ID for enhancements (null for base controls).</summary>
    [MaxLength(50)]
    public string? ParentControlId { get; set; }

    /// <summary>True if this is a control enhancement, not a base control.</summary>
    public bool IsEnhancement { get; set; }

    /// <summary>Natural sort position within the framework.</summary>
    public int SortOrder { get; set; }

    /// <summary>True if this control was withdrawn in this revision.</summary>
    public bool Withdrawn { get; set; }

    /// <summary>If withdrawn, the control ID it was incorporated into.</summary>
    [MaxLength(50)]
    public string? WithdrawnTo { get; set; }

    // ─── Navigation ─────────────────────────────────────────────────────────
    public ComplianceFramework Framework { get; set; } = null!;
}

/// <summary>
/// A named baseline or profile within a framework (e.g., "Low", "Moderate", "High", "Li-SaaS").
/// Baselines are imported from OSCAL profile documents or embedded reference data.
/// </summary>
public class FrameworkBaseline
{
    [Key] [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required] [MaxLength(36)]
    public string FrameworkId { get; set; } = string.Empty;

    /// <summary>Baseline display name (e.g., "Low", "Moderate", "High", "Li-SaaS").</summary>
    [Required] [MaxLength(50)]
    public string Level { get; set; } = string.Empty;

    /// <summary>Raw GitHub URL for the OSCAL profile JSON (if applicable).</summary>
    [MaxLength(500)]
    public string? SourceUrl { get; set; }

    /// <summary>Number of controls included in this baseline.</summary>
    public int ControlCount { get; set; }

    /// <summary>When this baseline was last imported.</summary>
    public DateTime? ImportedAt { get; set; }

    // ─── Navigation ─────────────────────────────────────────────────────────
    public ComplianceFramework Framework { get; set; } = null!;
    public ICollection<BaselineControlEntry> Controls { get; set; } = new List<BaselineControlEntry>();
}

/// <summary>
/// Junction entry linking a baseline to a specific control ID.
/// Uses a composite key of (BaselineId, ControlId).
/// </summary>
public class BaselineControlEntry
{
    [Required] [MaxLength(36)]
    public string BaselineId { get; set; } = string.Empty;

    /// <summary>Control ID included in this baseline (e.g., "AC-2", "SP_800_171_03.01.01").</summary>
    [Required] [MaxLength(50)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Optional JSON blob for FedRAMP/DoD parameter assignments.</summary>
    public string? Parameters { get; set; }

    // ─── Navigation ─────────────────────────────────────────────────────────
    public FrameworkBaseline Baseline { get; set; } = null!;
}

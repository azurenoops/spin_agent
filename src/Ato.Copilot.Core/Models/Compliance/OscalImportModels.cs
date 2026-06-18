using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

// ─── Enumerations ─────────────────────────────────────────────────────────────

public enum DecompositionStatus { Pending, Approved, Discarded }
public enum OscalImportMode { Preview, Full }

// ─── OscalImportRun ───────────────────────────────────────────────────────────

/// <summary>Log of an OSCAL SSP import operation (Feature 076 — T009).</summary>
[TenantScoped]
public class OscalImportRun
{
    public Guid TenantId { get; set; }

    [Key, MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required, MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string ImportedBy { get; set; } = string.Empty;

    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool SchemaValid { get; set; }

    [MaxLength(10)]
    public string OscalVersion { get; set; } = "1.1.2";

    [MaxLength(36)]
    public string? SourceDocumentUuid { get; set; }

    public OscalImportMode Mode { get; set; } = OscalImportMode.Full;

    public int ControlsCreated { get; set; }
    public int ControlsUpdated { get; set; }
    public int ControlsSkipped { get; set; }
    public int ControlsFailed { get; set; }

    [MaxLength(65535)]
    public string? WarningsJson { get; set; }

    [MaxLength(65535)]
    public string? ErrorsJson { get; set; }

    public RegisteredSystem? RegisteredSystem { get; set; }
}

// ─── OscalDecompositionDraft ─────────────────────────────────────────────────

/// <summary>
/// AI-generated OSCAL control statement decomposition pending human approval
/// (Feature 076 — T012). One active Pending draft per (system, controlId).
/// </summary>
[TenantScoped]
public class OscalDecompositionDraft
{
    public Guid TenantId { get; set; }

    [Key, MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required, MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string ControlId { get; set; } = string.Empty;

    [MaxLength(36)]
    public string? SourceNarrativeVersionId { get; set; }

    public DecompositionStatus Status { get; set; } = DecompositionStatus.Pending;

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    [Required, MaxLength(200)]
    public string GeneratedBy { get; set; } = string.Empty;

    public DateTimeOffset? ApprovedAt { get; set; }

    [MaxLength(200)]
    public string? ApprovedBy { get; set; }

    public RegisteredSystem? RegisteredSystem { get; set; }
    public ICollection<OscalDecompositionFragment> Fragments { get; set; } = new List<OscalDecompositionFragment>();
}

// ─── OscalDecompositionFragment ───────────────────────────────────────────────

/// <summary>
/// Individual statement-level fragment within a decomposition draft (Feature 076 — T012).
/// </summary>
[TenantScoped]
public class OscalDecompositionFragment
{
    public Guid TenantId { get; set; }

    [Key, MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required, MaxLength(36)]
    public string DraftId { get; set; } = string.Empty;

    /// <summary>OSCAL statement ID, e.g. "ac-1_smt.a".</summary>
    [Required, MaxLength(100)]
    public string StatementId { get; set; } = string.Empty;

    [MaxLength(36)]
    public string? ComponentUuid { get; set; }

    [Required, MaxLength(8000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? SuggestedParamsJson { get; set; }

    public double ConfidenceScore { get; set; }

    public int SortOrder { get; set; }

    public OscalDecompositionDraft? Draft { get; set; }
}

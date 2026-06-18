using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

// ─── Enumerations ─────────────────────────────────────────────────────────────

/// <summary>Import mode for OSCAL SSP import operations.</summary>
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

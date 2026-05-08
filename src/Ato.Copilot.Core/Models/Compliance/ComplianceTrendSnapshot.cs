using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Point-in-time record of a system's compliance metrics for trend visualization.
/// Captured daily by the background snapshot service and on-demand after assessments.
/// </summary>
[TenantScoped]
public class ComplianceTrendSnapshot
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to RegisteredSystem.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>UTC snapshot timestamp.</summary>
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Compliance score as percentage (0–100).</summary>
    public double ComplianceScore { get; set; }

    /// <summary>Open CAT I findings at snapshot time.</summary>
    public int CatICount { get; set; }

    /// <summary>Open CAT II findings at snapshot time.</summary>
    public int CatIICount { get; set; }

    /// <summary>Open CAT III findings at snapshot time.</summary>
    public int CatIIICount { get; set; }

    /// <summary>Total open POA&amp;M items at snapshot time.</summary>
    public int OpenPoamCount { get; set; }

    /// <summary>POA&amp;Ms past scheduled completion date at snapshot time.</summary>
    public int OverduePoamCount { get; set; }

    /// <summary>Percentage of baseline controls with narratives (0–100).</summary>
    public double NarrativeCoverage { get; set; }

    /// <summary>How the snapshot was triggered ("Scheduled" or "Assessment").</summary>
    [Required]
    [MaxLength(50)]
    public string Source { get; set; } = "Scheduled";

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent registered system.</summary>
    public RegisteredSystem RegisteredSystem { get; set; } = null!;
}

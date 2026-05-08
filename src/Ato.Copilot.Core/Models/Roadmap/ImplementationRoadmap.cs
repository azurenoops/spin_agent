using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Roadmap;

/// <summary>
/// Root entity representing a phased action plan for closing compliance gaps on a system.
/// Only one roadmap may have <see cref="RoadmapStatus.Active"/> per system at any time.
/// </summary>
[TenantScoped]
public class ImplementationRoadmap : ConcurrentEntity
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique roadmap identifier (GUID format).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the registered system this roadmap belongs to.</summary>
    public string SystemId { get; set; } = "";

    /// <summary>Human-readable name (e.g., "Eagle Eye Moderate Baseline Roadmap").</summary>
    public string Name { get; set; } = "";

    /// <summary>Roadmap lifecycle status.</summary>
    public RoadmapStatus Status { get; set; } = RoadmapStatus.Draft;

    /// <summary>Sum of all item efforts in person-days.</summary>
    public double TotalEstimatedEffort { get; set; }

    /// <summary>Sum of all weighted severity points.</summary>
    public double TotalRiskPoints { get; set; }

    /// <summary>Projected total risk reduction (always 100% if all gaps addressed).</summary>
    public double ProjectedRiskReduction { get; set; }

    /// <summary>Baseline level at time of generation (Low/Moderate/High).</summary>
    public string BaselineLevel { get; set; } = "";

    /// <summary>Total number of gaps when generated.</summary>
    public int TotalGaps { get; set; }

    /// <summary>Linked Kanban board FK (if created via bridge).</summary>
    public string? LinkedBoardId { get; set; }

    /// <summary>Generation method: "AI" or "Manual".</summary>
    public string GenerationMethod { get; set; } = "AI";

    /// <summary>User who generated the roadmap.</summary>
    public string CreatedBy { get; set; } = "";

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC last modification timestamp.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Version number for generation history.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Child phases within this roadmap.</summary>
    public List<RoadmapPhase> Phases { get; set; } = new();
}

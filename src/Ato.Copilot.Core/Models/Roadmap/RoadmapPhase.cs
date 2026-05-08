using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Roadmap;

/// <summary>
/// A logical grouping of related controls within a roadmap.
/// Phase status transitions: NotStarted → InProgress → Complete.
/// </summary>
[TenantScoped]
public class RoadmapPhase : ConcurrentEntity
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique phase identifier (GUID format).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the parent roadmap.</summary>
    public string RoadmapId { get; set; } = "";

    /// <summary>Phase display name (e.g., "Critical Controls").</summary>
    public string Name { get; set; } = "";

    /// <summary>Sort order (1-based).</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Sum of item efforts in person-days.</summary>
    public double EstimatedEffort { get; set; }

    /// <summary>Sum of weighted severity points for items in this phase.</summary>
    public double RiskPoints { get; set; }

    /// <summary>Phase risk reduction = RiskPoints / Roadmap.TotalRiskPoints × 100.</summary>
    public double RiskReductionPercent { get; set; }

    /// <summary>Target start week (1-based, relative to roadmap start).</summary>
    public int? TargetStartWeek { get; set; }

    /// <summary>Target end week.</summary>
    public int? TargetEndWeek { get; set; }

    /// <summary>Absolute target completion date.</summary>
    public DateTime? TargetCompletionDate { get; set; }

    /// <summary>Phase lifecycle status.</summary>
    public PhaseStatus Status { get; set; } = PhaseStatus.NotStarted;

    /// <summary>Cached count of completed items.</summary>
    public int CompletedItemCount { get; set; }

    /// <summary>Cached count of total items.</summary>
    public int TotalItemCount { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC last modification timestamp.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Navigation to parent roadmap.</summary>
    public ImplementationRoadmap? Roadmap { get; set; }

    /// <summary>Child items within this phase.</summary>
    public List<RoadmapItem> Items { get; set; } = new();
}

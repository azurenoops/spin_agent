using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Roadmap;

/// <summary>
/// An individual control gap assigned to a phase.
/// Status is synced bi-directionally with linked Kanban tasks.
/// </summary>
[TenantScoped]
public class RoadmapItem : ConcurrentEntity
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique item identifier (GUID format).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the parent phase.</summary>
    public string PhaseId { get; set; } = "";

    /// <summary>FK to the parent roadmap (denormalized for efficient queries).</summary>
    public string RoadmapId { get; set; } = "";

    /// <summary>NIST 800-53 control identifier (e.g., "AC-2").</summary>
    public string ControlId { get; set; } = "";

    /// <summary>Control title (e.g., "Account Management").</summary>
    public string ControlTitle { get; set; } = "";

    /// <summary>Family code (e.g., "AC").</summary>
    public string ControlFamily { get; set; } = "";

    /// <summary>Type of compliance gap.</summary>
    public GapType GapType { get; set; }

    /// <summary>Severity level mapped to CAT risk points.</summary>
    public ItemSeverity Severity { get; set; } = ItemSeverity.Medium;

    /// <summary>Severity-based risk points (10, 5, or 1).</summary>
    public double RiskPoints { get; set; }

    /// <summary>Estimated effort in person-days.</summary>
    public double EstimatedEffortDays { get; set; } = 1;

    /// <summary>Estimation source: "AI", "Historical", or "Manual".</summary>
    public string EstimationSource { get; set; } = "AI";

    /// <summary>Assigned role: ISSO, Engineer, or ISSM.</summary>
    public string AssignedRole { get; set; } = "Engineer";

    /// <summary>Comma-separated control IDs this item depends on.</summary>
    public string? DependsOn { get; set; }

    /// <summary>Item lifecycle status.</summary>
    public ItemStatus Status { get; set; } = ItemStatus.NotStarted;

    /// <summary>FK to the linked Kanban task (nullable).</summary>
    public string? LinkedTaskId { get; set; }

    /// <summary>Sort order within phase.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC last modification timestamp.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Navigation to parent phase.</summary>
    public RoadmapPhase? Phase { get; set; }

    /// <summary>Navigation to parent roadmap.</summary>
    public ImplementationRoadmap? Roadmap { get; set; }
}

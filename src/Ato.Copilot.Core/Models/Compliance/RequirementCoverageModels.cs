using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>Immutable, system-scoped source reconciliation for an existing baseline.</summary>
[TenantScoped]
public sealed class BaselineCatalogBinding
{
    [Key, MaxLength(36)] public string Id { get; set; } = Guid.NewGuid().ToString();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string ControlBaselineId { get; set; } = string.Empty;
    public ControlBaseline ControlBaseline { get; set; } = null!;
    [MaxLength(36)] public string FrameworkId { get; set; } = string.Empty;
    [MaxLength(100)] public string FrameworkIdentifier { get; set; } = string.Empty;
    [MaxLength(100)] public string CatalogVersion { get; set; } = string.Empty;
    [MaxLength(500)] public string SourceUri { get; set; } = string.Empty;
    [MaxLength(100)] public string Publisher { get; set; } = string.Empty;
    [MaxLength(64)] public string ContentHash { get; set; } = string.Empty;
    public string CatalogJson { get; set; } = string.Empty;
    [MaxLength(2000)] public string Rationale { get; set; } = string.Empty;
    [MaxLength(200)] public string BoundBy { get; set; } = string.Empty;
    public DateTime BoundAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A proposed baseline addition; draft text is not an active implementation.</summary>
[TenantScoped]
[Index(nameof(TenantId), nameof(ActiveControlKey), IsUnique = true)]
public sealed class RequirementEnhancementProposal
{
    [Key, MaxLength(36)] public string Id { get; set; } = Guid.NewGuid().ToString();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string ControlBaselineId { get; set; } = string.Empty;
    public ControlBaseline ControlBaseline { get; set; } = null!;
    [MaxLength(36)] public string CatalogBindingId { get; set; } = string.Empty;
    [MaxLength(100)] public string? ActiveControlKey { get; set; }
    [MaxLength(64)] public string BaselineHash { get; set; } = string.Empty;
    public int BaseRevision { get; set; }
    public int? ExistingNarrativeVersion { get; set; }
    [MaxLength(64)] public string? ExistingNarrativeHash { get; set; }
    [MaxLength(50)] public string ControlId { get; set; } = string.Empty;
    [MaxLength(50)] public string ParentControlId { get; set; } = string.Empty;
    [MaxLength(2000)] public string Rationale { get; set; } = string.Empty;
    [MaxLength(8000)] public string? PolicyDraft { get; set; }
    [MaxLength(8000)] public string? TechnicalDraft { get; set; }
    [MaxLength(32)] public string Status { get; set; } = "Pending";
    [ConcurrencyCheck] public int Revision { get; set; } = 1;
    public Guid AuthorPersonId { get; set; }
    [MaxLength(200)] public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? ReviewerPersonId { get; set; }
    [MaxLength(200)] public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    [MaxLength(2000)] public string? ReviewNote { get; set; }
}

/// <summary>An explicitly selected supporting artifact version.</summary>
public sealed record RequirementEvidencePin(string ArtifactId, string ContentHash);

/// <summary>A response associated with one authoritative requirement and narrative half.</summary>
public sealed record RequirementResponse(
    string StatementId, string Kind, string Response, IReadOnlyList<RequirementEvidencePin> Evidence);

/// <summary>Immutable serialized response/review data retained in narrative version history.</summary>
public sealed record RequirementCoverageSnapshot(
    string BindingId, string CatalogHash, IReadOnlyList<RequirementResponse> Responses,
    IReadOnlyDictionary<string, string> Parameters, string NarrativeHash,
    Guid AuthorPersonId, string AuthoredBy, DateTime AuthoredAt,
    Guid? ReviewerPersonId = null, string? ReviewedBy = null, DateTime? ReviewedAt = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RequirementFirstPassProvenance? FirstPass { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RequirementFirstPassProvenance>? FirstPasses { get; init; }
}

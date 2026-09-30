using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>Immutable evaluation history independent of package generation and current caller permissions.</summary>
[TenantScoped]
public sealed class PackageReadinessRun
{
    [Key, MaxLength(36)] public string Id { get; set; } = Guid.NewGuid().ToString();
    public Guid TenantId { get; set; }
    [Required, MaxLength(36)] public string RegisteredSystemId { get; set; } = "";
    public PackagePurpose Purpose { get; set; }
    [Required, MaxLength(64)] public string SelectionHash { get; set; } = "";
    public string? RetainedSelectionJson { get; set; }
    [Required, MaxLength(32)] public string Outcome { get; set; } = "Failed";
    public DateTime StartedAt { get; set; }
    public DateTime EvaluatedAt { get; set; }
    [Required, MaxLength(200)] public string EvaluatedBy { get; set; } = "";
    public Guid? EvaluatedPersonId { get; set; }
    [MaxLength(64)] public string? SourceHash { get; set; }
    [MaxLength(64)] public string? SourceHashAfter { get; set; }
    [Required, MaxLength(80)] public string RuleVersion { get; set; } = "";
    [Required] public string ChecksJson { get; set; } = "[]";
    [Required] public string SourcesJson { get; set; } = "[]";
    public string? FailureJson { get; set; }
    public DateTime? ValidUntil { get; set; }
}

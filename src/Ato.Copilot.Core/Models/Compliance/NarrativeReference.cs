using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

[TenantScoped]
[Microsoft.EntityFrameworkCore.Index(nameof(TenantId), nameof(ReferenceKey), nameof(Version), IsUnique = true)]
public class NarrativeReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ReferenceKey { get; set; } = Guid.NewGuid();
    [MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(20)] public string Scope { get; set; } = "System";
    [MaxLength(36)] public string ScopeId { get; set; } = string.Empty;
    [MaxLength(36)] public string ImportedForSystemId { get; set; } = string.Empty;
    [MaxLength(200)] public string SourceName { get; set; } = string.Empty;
    [MaxLength(64)] public string SourceSha256 { get; set; } = string.Empty;
    public string OriginalPassagesJson { get; set; } = "[]";
    public string PassagesJson { get; set; } = "[]";
    public int Version { get; set; } = 1;
    [ConcurrencyCheck] public int Revision { get; set; } = 1;
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)] public string CreatedBy { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
    [MaxLength(200)] public string? PublishedBy { get; set; }
}
using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Workspaces;

[TenantScoped]
public sealed class OrganizationCatalogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(16)] public string Source { get; set; } = string.Empty;
    [MaxLength(16)] public string RecordType { get; set; } = string.Empty;
    [MaxLength(36)] public string RecordId { get; set; } = string.Empty;
    [MaxLength(2000)] public string OrganizationContribution { get; set; } = string.Empty;
    [MaxLength(200)] public string OrganizationOwner { get; set; } = string.Empty;
    public string SupportingComponentsJson { get; set; } = "[]";
    [ConcurrencyCheck] public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(200)] public string CreatedBy { get; set; } = string.Empty;
    [MaxLength(200)] public string UpdatedBy { get; set; } = string.Empty;
}

[TenantScoped]
public sealed class OrganizationCatalogAddition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(100)] public string IdempotencyKey { get; set; } = string.Empty;
    public string IntentJson { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(200)] public string CreatedBy { get; set; } = string.Empty;
}

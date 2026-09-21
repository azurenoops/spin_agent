using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Explicit access grant from a validated directory identity to an organization-local Person.
/// This authorization index must be readable before tenant resolution; callers must constrain
/// discovery by both directory and object ID. It is not an RMF role assignment.
/// </summary>
[GlobalReference]
public sealed class OrganizationMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DirectoryTenantId { get; set; }
    public Guid ObjectId { get; set; }
    public Guid PersonId { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public string GrantedBy { get; set; } = string.Empty;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
}

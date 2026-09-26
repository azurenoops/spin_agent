using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>Durable authorization record for a signed, actor-bound support session.</summary>
[GlobalReference]
public sealed class TenantSupportSession
{
    public Guid Id { get; set; }
    public Guid DirectoryTenantId { get; set; }
    public Guid ObjectId { get; set; }
    public Guid TargetTenantId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public bool Acknowledged { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

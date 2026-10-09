using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

[ProviderScoped]
public sealed class ProviderPrincipal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    [MaxLength(256)] public string DisplayName { get; set; } = "";
    [MaxLength(320)] public string? Email { get; set; }
    [MaxLength(32)] public string State { get; set; } = "Active";
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string CreatedBy { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    [MaxLength(254)] public string? UpdatedBy { get; set; }
}

[ProviderScoped]
public sealed class ProviderDirectoryMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public Guid PrincipalId { get; set; }
    public Guid DirectoryTenantId { get; set; }
    public Guid ObjectId { get; set; }
    [MaxLength(32)] public string State { get; set; } = "Verified";
    [MaxLength(64)] public string Provenance { get; set; } = "";
    public DateTimeOffset VerifiedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string VerifiedBy { get; set; } = "";
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
}

[ProviderScoped]
public sealed class ProviderMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public Guid PrincipalId { get; set; }
    public Guid DirectoryMatchId { get; set; }
    [MaxLength(32)] public string State { get; set; } = "Active";
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string GrantedBy { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    [MaxLength(254)] public string? RevokedBy { get; set; }
    [MaxLength(2000)] public string? RevocationReason { get; set; }
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
}

[ProviderScoped]
public sealed class ProviderRoleAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public Guid MembershipId { get; set; }
    [MaxLength(32)] public string Role { get; set; } = "";
    public Guid? PortfolioId { get; set; }
    public Guid? OfferingId { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string GrantedBy { get; set; } = "";
    public DateTimeOffset? RemovedAt { get; set; }
    [MaxLength(254)] public string? RemovedBy { get; set; }
    [MaxLength(2000)] public string? RemovalReason { get; set; }
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
}

[ProviderScoped]
public sealed class ProviderInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    [MaxLength(64)] public string TokenHash { get; set; } = "";
    [MaxLength(320)] public string? TargetEmail { get; set; }
    public Guid? TargetDirectoryTenantId { get; set; }
    public Guid? TargetObjectId { get; set; }
    public string RequestedRolesJson { get; set; } = "[]";
    public Guid? PortfolioId { get; set; }
    public Guid? OfferingId { get; set; }
    [MaxLength(32)] public string Status { get; set; } = "Pending";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string CreatedBy { get; set; } = "";
    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? AcceptedPrincipalId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    [MaxLength(254)] public string? RevokedBy { get; set; }
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
}

[ProviderScoped]
public sealed class ProviderAccessRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public Guid DirectoryTenantId { get; set; }
    public Guid ObjectId { get; set; }
    [MaxLength(320)] public string? Email { get; set; }
    [MaxLength(2000)] public string Justification { get; set; } = "";
    [MaxLength(32)] public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
    [MaxLength(254)] public string? DecidedBy { get; set; }
    [MaxLength(2000)] public string? DecisionReason { get; set; }
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
}

[ProviderScoped]
public sealed class ProviderContact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public Guid? PrincipalId { get; set; }
    [MaxLength(32)] public string Category { get; set; } = "";
    [MaxLength(256)] public string? DisplayName { get; set; }
    [MaxLength(320)] public string? Email { get; set; }
    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(32)] public string State { get; set; } = "Active";
    [MaxLength(64)] public string Provenance { get; set; } = "ProviderSetup";
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string CreatedBy { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    [MaxLength(254)] public string? UpdatedBy { get; set; }
}

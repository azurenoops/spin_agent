using Ato.Copilot.Core.Models.Tenancy.Attributes;
namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Per-tenant identity record for RMF role assignees (research §R1). Local-first records
/// can be promoted to directory-linked Entra accounts; promotion is one-way and preserves
/// the same <see cref="Id"/>.
/// </summary>
[TenantScoped]
public class Person
{
    /// <summary>Primary key — stable across promotion (local → directory-linked).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Display name (required).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Email (required; indexed for de-duplication search).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Optional phone number.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Entra (Azure AD) object id when directory-linked; null for local-only.</summary>
    public Guid? EntraObjectId { get; set; }

    /// <summary>Convenience flag (<see cref="EntraObjectId"/> not null ⇔ true).</summary>
    public bool IsLinkedToDirectory { get; set; }

    /// <summary>UTC timestamp of the local→directory promotion (null if never promoted).</summary>
    public DateTimeOffset? LastPromotedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UpdatedBy { get; set; }
}

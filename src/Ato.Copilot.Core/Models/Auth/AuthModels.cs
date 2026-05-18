using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Auth;

/// <summary>
/// Represents a CAC/PIV authenticated session with configurable timeout.
/// Tracks user identity, token hash, and session lifecycle.
/// </summary>
[TenantScoped]
public class CacSession
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique session identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Azure AD object ID of the authenticated user.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Display name of the authenticated user.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Email address of the authenticated user.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the JWT token for validation (never store raw token).</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Timestamp when the session was created.</summary>
    public DateTimeOffset SessionStart { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Timestamp when the session expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Client surface type (VSCode, Teams, Web, CLI).</summary>
    public ClientType ClientType { get; set; } = ClientType.Web;

    /// <summary>IP address of the client that established the session.</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>Current status of the session (Active, Expired, Terminated).</summary>
    public SessionStatus Status { get; set; } = SessionStatus.Active;

    /// <summary>Timestamp when the record was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Timestamp when the record was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents a JIT access request for PIM role activation or VM network access.
/// Tracks the full lifecycle from request through activation to deactivation.
/// </summary>
[TenantScoped]
public class JitRequestEntity : ConcurrentEntity
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique request identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Type of JIT access request (PIM role or VM access).</summary>
    public JitRequestType RequestType { get; set; }

    /// <summary>PIM request ID from Azure AD (for PIM role activations).</summary>
    public string? PimRequestId { get; set; }

    /// <summary>Azure AD object ID of the requesting user.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Display name of the requesting user.</summary>
    public string UserDisplayName { get; set; } = string.Empty;

    /// <summary>Conversation ID for audit traceability.</summary>
    public string? ConversationId { get; set; }

    /// <summary>Foreign key to the CacSession under which this request was made.</summary>
    public Guid? SessionId { get; set; }

    /// <summary>Navigation property to the associated CacSession.</summary>
    public CacSession? Session { get; set; }

    /// <summary>Role name being activated (for PIM) or empty (for JIT VM).</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>Scope of the role activation (subscription, resource group, or resource ID).</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Display-friendly name for the scope.</summary>
    public string? ScopeDisplayName { get; set; }

    /// <summary>User-provided justification for the access request.</summary>
    public string Justification { get; set; } = string.Empty;

    /// <summary>Optional ticket number from an approved ticketing system.</summary>
    public string? TicketNumber { get; set; }

    /// <summary>Optional ticketing system name (ServiceNow, Jira, Remedy, AzureDevOps).</summary>
    public string? TicketSystem { get; set; }

    /// <summary>Current status of the request.</summary>
    public JitRequestStatus Status { get; set; } = JitRequestStatus.PendingApproval;

    /// <summary>Requested duration in hours.</summary>
    public int DurationHours { get; set; }

    /// <summary>Timestamp when the request was submitted.</summary>
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Timestamp when the role/access was activated.</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>Timestamp when the role/access expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Timestamp when the role/access was deactivated.</summary>
    public DateTimeOffset? DeactivatedAt { get; set; }

    /// <summary>Actual duration the access was active.</summary>
    public TimeSpan? ActualDuration { get; set; }

    /// <summary>Azure AD object ID of the approver (for high-privilege roles).</summary>
    public string? ApproverId { get; set; }

    /// <summary>Display name of the approver.</summary>
    public string? ApproverDisplayName { get; set; }

    /// <summary>Comments from the approver during approval/denial.</summary>
    public string? ApproverComments { get; set; }

    /// <summary>Timestamp when the approval/denial decision was made.</summary>
    public DateTimeOffset? ApprovalDecisionAt { get; set; }

    // ── JIT VM Access specific fields ──

    /// <summary>VM name for JIT VM access requests.</summary>
    public string? VmName { get; set; }

    /// <summary>Resource group for JIT VM access requests.</summary>
    public string? ResourceGroup { get; set; }

    /// <summary>Subscription ID for JIT VM access requests.</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Port number for JIT VM access (e.g., 22 for SSH, 3389 for RDP).</summary>
    public int? Port { get; set; }

    /// <summary>Protocol for JIT VM access (SSH/RDP).</summary>
    public string? Protocol { get; set; }

    /// <summary>Source IP address allowed through the JIT NSG rule.</summary>
    public string? SourceIp { get; set; }
}

/// <summary>
/// Maps a CAC certificate thumbprint/subject to a platform role for automatic role resolution.
/// </summary>
[TenantScoped]
public class CertificateRoleMapping
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique mapping identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>SHA-1 thumbprint of the CAC certificate.</summary>
    public string CertificateThumbprint { get; set; } = string.Empty;

    /// <summary>Subject (CN) of the CAC certificate.</summary>
    public string CertificateSubject { get; set; } = string.Empty;

    /// <summary>Platform role mapped to this certificate (ComplianceRoles constant).</summary>
    public string MappedRole { get; set; } = string.Empty;

    /// <summary>User ID who created this mapping.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Timestamp when the mapping was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Whether this mapping is currently active.</summary>
    public bool IsActive { get; set; } = true;
}

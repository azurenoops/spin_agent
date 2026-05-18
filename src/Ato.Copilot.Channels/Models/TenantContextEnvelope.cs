namespace Ato.Copilot.Channels.Models;

/// <summary>
/// Plain-data tenant scope carried on inbound channel messages so MCP tools
/// invoked via <see cref="Ato.Copilot.Channels"/> from the VS Code extension or
/// M365 Teams bot see the same tenant identity as direct HTTP callers.
/// Bridged into the MCP host's <c>ITenantContextAccessor</c> by a host-supplied
/// <see cref="Ato.Copilot.Channels.Abstractions.ITenantScopeBinder"/>.
/// See feature 048 spec FR-021/FR-024 and research.md §10.
/// </summary>
public sealed class TenantContextEnvelope
{
    /// <summary>The caller's home tenant id.</summary>
    public Guid TenantId { get; init; }

    /// <summary>Optional organization id within the tenant.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>True when the caller is a CSP-Admin operator.</summary>
    public bool IsCspAdmin { get; init; }

    /// <summary>
    /// When set, the caller is impersonating this tenant. <c>EffectiveTenantId</c>
    /// for tool execution is <c>ImpersonatedTenantId ?? TenantId</c>.
    /// </summary>
    public Guid? ImpersonatedTenantId { get; init; }
}

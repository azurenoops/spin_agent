using Ato.Copilot.Channels.Abstractions;
using Ato.Copilot.Channels.Models;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;

namespace Ato.Copilot.Chat.Channels;

/// <summary>
/// Bridges a <see cref="TenantContextEnvelope"/> attached to an inbound channel
/// message into the host's ambient <see cref="ITenantContextAccessor"/> so MCP
/// tools invoked in-process via <see cref="Ato.Copilot.Channels"/> from VS Code
/// or M365 Teams see the same identity as direct HTTP callers.
/// Implements feature 048 spec FR-021/FR-024 and research.md §10.
/// </summary>
public sealed class AccessorTenantScopeBinder : ITenantScopeBinder
{
    private static readonly IDisposable s_noop = new NoopScope();
    private readonly ITenantContextAccessor _accessor;

    /// <summary>Initializes a new instance.</summary>
    public AccessorTenantScopeBinder(ITenantContextAccessor accessor)
    {
        _accessor = accessor;
    }

    /// <inheritdoc />
    public IDisposable Bind(TenantContextEnvelope? envelope)
    {
        if (envelope is null)
        {
            return s_noop;
        }

        var ctx = new TenantContext(
            tenantId: envelope.TenantId,
            organizationId: envelope.OrganizationId,
            isCspAdmin: envelope.IsCspAdmin,
            impersonatedTenantId: envelope.ImpersonatedTenantId,
            status: TenantStatus.Active);

        return _accessor.Push(ctx);
    }

    private sealed class NoopScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

using Ato.Copilot.Channels.Models;

namespace Ato.Copilot.Channels.Abstractions;

/// <summary>
/// Bridges a <see cref="TenantContextEnvelope"/> attached to an inbound channel
/// message into the host's ambient tenant context (e.g. <c>ITenantContextAccessor</c>
/// in MCP). The Channels library has no dependency on the Core tenancy types,
/// so the host registers a binder that performs the push. The default
/// registration is <see cref="Implementations.NullTenantScopeBinder"/>, which is
/// a no-op suitable for tests and stand-alone Channels consumers.
/// See feature 048 spec FR-021/FR-024.
/// </summary>
public interface ITenantScopeBinder
{
    /// <summary>
    /// Pushes <paramref name="envelope"/> as the ambient tenant context for the
    /// duration of the returned scope. Disposing the returned object pops the
    /// context. When <paramref name="envelope"/> is <c>null</c>, the binder may
    /// return a no-op disposable.
    /// </summary>
    /// <param name="envelope">The tenant scope to bind, or <c>null</c>.</param>
    /// <returns>A disposable that pops the bound context when disposed.</returns>
    IDisposable Bind(TenantContextEnvelope? envelope);
}

using Ato.Copilot.Channels.Abstractions;
using Ato.Copilot.Channels.Models;

namespace Ato.Copilot.Channels.Implementations;

/// <summary>
/// Default <see cref="ITenantScopeBinder"/> registered by the Channels library.
/// Returns a no-op disposable. The MCP host overrides this registration with
/// a binder that pushes into <c>ITenantContextAccessor</c>.
/// </summary>
public sealed class NullTenantScopeBinder : ITenantScopeBinder
{
    private static readonly IDisposable s_noop = new NoopScope();

    /// <inheritdoc />
    public IDisposable Bind(TenantContextEnvelope? envelope) => s_noop;

    private sealed class NoopScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

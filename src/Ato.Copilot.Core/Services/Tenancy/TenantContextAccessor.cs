using Ato.Copilot.Core.Interfaces.Tenancy;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// <see cref="System.Threading.AsyncLocal{T}"/>-backed singleton
/// implementation of <see cref="ITenantContextAccessor"/>. Used by background
/// services and <c>Ato.Copilot.Channels</c> consumers (VS Code extension,
/// M365 Teams bot) that have no <c>HttpContext</c> available.
/// See feature 048 spec FR-024.
/// </summary>
public sealed class TenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<ContextHolder?> AsyncLocal = new();

    /// <inheritdoc />
    public ITenantContext? Current => AsyncLocal.Value?.Context;

    /// <inheritdoc />
    public IDisposable Push(ITenantContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var previous = AsyncLocal.Value;
        AsyncLocal.Value = new ContextHolder(context);
        return new PopOnDispose(previous);
    }

    /// <summary>
    /// Holder class so we can detect "no value pushed" (null Value) vs
    /// "explicit null pushed" (ContextHolder with null Context). Currently
    /// only the former is used, but this keeps the door open.
    /// </summary>
    private sealed class ContextHolder
    {
        public ContextHolder(ITenantContext context)
        {
            Context = context;
        }

        public ITenantContext Context { get; }
    }

    private sealed class PopOnDispose : IDisposable
    {
        private readonly ContextHolder? _previous;
        private bool _disposed;

        public PopOnDispose(ContextHolder? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            AsyncLocal.Value = _previous;
        }
    }
}

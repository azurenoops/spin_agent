namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Accessor for code paths that have no <c>HttpContext</c> — for example,
/// MCP tools invoked through <c>Ato.Copilot.Channels</c> from the VS Code
/// extension or M365 Teams bot, and background services such as the
/// compliance watch worker. Lifetime:
/// <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton"/>.
/// Backed by <see cref="System.Threading.AsyncLocal{T}"/>.
/// See feature 048 spec FR-024 and contracts/itenantcontext.cs.md.
/// </summary>
public interface ITenantContextAccessor
{
    /// <summary>The current ambient context, or <c>null</c> if none has been pushed.</summary>
    ITenantContext? Current { get; }

    /// <summary>
    /// Pushes <paramref name="context"/> as the ambient scope. Disposing the
    /// returned <see cref="IDisposable"/> pops it. Implemented over
    /// <see cref="System.Threading.AsyncLocal{T}"/>.
    /// </summary>
    /// <param name="context">The tenant context to push.</param>
    /// <returns>A disposable that pops the context when disposed.</returns>
    IDisposable Push(ITenantContext context);
}

using Ato.Copilot.Chat.Services.Auth;
using Microsoft.AspNetCore.SignalR;

namespace Ato.Copilot.Chat.Hubs;

/// <summary>Supplies the actual handshake context to the same per-operation service guard.</summary>
public sealed class ChatWorkspaceHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocation,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var previous = ChatWorkspaceResolver.HubRequest.Value;
        try
        {
            ChatWorkspaceResolver.HubRequest.Value = invocation.Context.GetHttpContext()
                ?? throw new HubException("An authenticated HTTP context is required.");
            return await next(invocation);
        }
        catch (ChatWorkspaceException ex) { throw new HubException($"{ex.Code}: {ex.Message}"); }
        finally { ChatWorkspaceResolver.HubRequest.Value = previous; }
    }
}

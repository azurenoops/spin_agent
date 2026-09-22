using Ato.Copilot.Mcp.Services.Tenancy;
using Ato.Copilot.State.Abstractions;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Ato.Copilot.Mcp.Server;

/// <summary>Uses only the workspace already validated by request middleware, never raw selectors.</summary>
public sealed class WorkspaceChatScope(IHttpContextAccessor httpContextAccessor) : IConversationIdentityAccessor
{
    internal static readonly object IdentityItem = new();

    public ConversationIdentity? Current => httpContextAccessor.HttpContext is { } http
        ? http.Items.TryGetValue(IdentityItem, out var identity) && identity is ConversationIdentity current
            ? current : Resolve(http)
        : null;

    public static ConversationIdentity Resolve(HttpContext http)
    {
        var actor = WorkspaceService.Identity(http.User);
        var workspace = http.RequestServices.GetService<IWorkspaceService>()?.Current;
        if (workspace is null)
        {
            var tenant = http.RequestServices.GetService<ITenantContextAccessor>()?.Current;
            var deployment = http.RequestServices.GetService<IOptions<DeploymentOptions>>()?.Value;
            if (deployment?.Mode == DeploymentMode.SingleTenant
                && tenant is { IsWorkspaceRequest: false, ImpersonatedTenantId: null, Status: TenantStatus.Active }
                && tenant.EffectiveTenantId != Guid.Empty
                && ReferenceEquals(tenant, http.RequestServices.GetService<ITenantContext>())
                && !http.Request.Headers.Keys.Any(key => key.StartsWith("X-Workspace-", StringComparison.OrdinalIgnoreCase)))
                return new(actor.DirectoryId, actor.ObjectId, "legacy", tenant.EffectiveTenantId, "ordinary", null);
            throw new WorkspaceException(409, "WORKSPACE_REQUIRED", "Select an explicitly authorized workspace.");
        }
        return new(actor.DirectoryId, actor.ObjectId, workspace.Kind, workspace.TenantId,
            workspace.Mode, workspace.PersonId);
    }

    public static async Task<ConversationIdentity> ResolveAsync(HttpContext http,
        Dictionary<string, object>? context, Dictionary<string, object>? actionContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = Resolve(http);
        var systems = new List<string>();
        foreach (var values in new[] { context, actionContext })
        {
            if (values is null) continue;
            foreach (var (key, value) in values)
            {
                if (!key.Equals("systemId", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("system_id", StringComparison.OrdinalIgnoreCase)) continue;
                if (value is null || value is JsonElement { ValueKind: JsonValueKind.Null }) continue;
                var system = value switch
                {
                    string text => text.Trim(),
                    JsonElement { ValueKind: JsonValueKind.String } json => json.GetString()?.Trim(),
                    _ => throw new WorkspaceException(400, "INVALID_SYSTEM_CONTEXT", "System context must be a string.")
                };
                if (!string.IsNullOrEmpty(system)) systems.Add(system);
            }
        }
        var distinct = systems.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length > 1)
            throw new WorkspaceException(400, "INVALID_SYSTEM_CONTEXT", "Conflicting system references are not allowed.");
        if (distinct.Length == 1)
        {
            var readable = identity.Kind == "legacy"
                ? await http.RequestServices.GetRequiredService<AtoCopilotContext>().RegisteredSystems
                    .AnyAsync(system => system.Id == distinct[0] && system.TenantId == identity.TenantId && system.IsActive,
                        cancellationToken)
                : await http.RequestServices.GetRequiredService<ISystemWorkspaceAccessService>()
                    .CanReadAsync(identity.TenantId ?? Guid.Empty, identity.PersonId,
                        distinct[0], identity.Kind == "csp" || identity.Mode == "support", cancellationToken);
            if (!readable)
                throw new WorkspaceException(403, "SYSTEM_ACCESS_DENIED", "The selected system is not accessible in this workspace.");
            identity = identity with { SystemId = distinct[0] };
        }
        http.Items[IdentityItem] = identity;
        return identity;
    }

    public static Dictionary<string, object> BindContext(
        Dictionary<string, object>? source, ConversationIdentity identity)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (source is not null)
        {
            foreach (var (key, value) in source)
            {
                var name = key.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
                if (name is "userid" or "userrole" or "tenantid" or "organizationid" or "personid"
                    or "tid" or "oid" or "roles" or "permissions" or "iscspadmin" or "isadmin"
                    or "isimpersonating" or "workspace" or "systemid"
                    || name.StartsWith("pending", StringComparison.Ordinal)
                    || name.StartsWith("inlineactivated", StringComparison.Ordinal))
                    continue;
                result[key] = value;
            }
        }
        result["user_id"] = identity.ActorId;
        result["userId"] = identity.ActorId;
        if (identity.SystemId is not null)
        {
            result["system_id"] = identity.SystemId;
            result["systemId"] = identity.SystemId;
        }
        return result;
    }
}

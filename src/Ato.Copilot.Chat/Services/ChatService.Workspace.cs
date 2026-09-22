using System.Text.Json;
using Ato.Copilot.Chat.Models;
using Ato.Copilot.Chat.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Chat.Services;

public partial class ChatService
{
    private CancellationToken RequestAborted => _workspaces.RequestAborted;

    private async Task<Conversation> RequireConversationAsync(string id, ChatWorkspace workspace, CancellationToken ct)
    {
        var conversation = await _dbContext.Conversations.SingleOrDefaultAsync(
            c => c.Id == id && c.OwnerKey == workspace.OwnerKey, ct)
            ?? throw new ChatWorkspaceException(404, "CONVERSATION_NOT_FOUND", "Conversation not found.");
        await _workspaces.RequireSystemAsync(workspace, conversation.SystemId, ct);
        return conversation;
    }

    private async Task BindSystemAsync(Conversation conversation, Dictionary<string, object>? context,
        ChatWorkspace workspace, CancellationToken ct)
    {
        var systems = new List<string>();
        foreach (var (key, value) in context ?? new())
        {
            if (key.Replace("_", "", StringComparison.Ordinal).Equals("systemId", StringComparison.OrdinalIgnoreCase))
            {
                if (value is null || value is JsonElement { ValueKind: JsonValueKind.Null }) continue;
                var id = value switch
                {
                    string text => text.Trim(),
                    JsonElement { ValueKind: JsonValueKind.String } json => json.GetString()?.Trim(),
                    _ => throw new ChatWorkspaceException(400, "INVALID_SYSTEM_CONTEXT", "System context must be a string.")
                };
                if (!string.IsNullOrEmpty(id)) systems.Add(id);
            }
        }
        var selected = systems.Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length > 1)
            throw new ChatWorkspaceException(400, "INVALID_SYSTEM_CONTEXT", "Conflicting system references are not allowed.");
        if (selected.Length == 0 || selected[0] == conversation.SystemId) return;
        if (conversation.SystemId is not null
            || await _dbContext.Messages.AnyAsync(m => m.ConversationId == conversation.Id, ct))
            throw new ChatWorkspaceException(409, "CONVERSATION_SYSTEM_MISMATCH", "Create a new conversation to change system scope.");
        await _workspaces.RequireSystemAsync(workspace, selected[0], ct);
        conversation.SystemId = selected[0];
    }

    private static Dictionary<string, object> BindContext(Dictionary<string, object>? source,
        ChatWorkspace workspace, string? systemId)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source ?? new())
        {
            var name = key.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
            if (name is "userid" or "userrole" or "tenantid" or "organizationid" or "personid"
                or "tid" or "oid" or "roles" or "permissions" or "iscspadmin" or "isadmin"
                or "isimpersonating" or "workspace" or "systemid" or "conversationid" or "conversationhistory"
                || name.StartsWith("pending", StringComparison.Ordinal)
                || name.StartsWith("inlineactivated", StringComparison.Ordinal))
                continue;
            result[key] = value;
        }
        result["userId"] = workspace.ActorId;
        result["user_id"] = workspace.ActorId;
        if (systemId is not null)
        {
            result["systemId"] = systemId;
            result["system_id"] = systemId;
        }
        return result;
    }
}

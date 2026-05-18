namespace Ato.Copilot.Channels.Models;

/// <summary>
/// An inbound message from any client channel.
/// </summary>
public class IncomingMessage
{
    /// <summary>Originating connection ID.</summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>Target conversation ID.</summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>Message text content.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Explicit agent routing hint (null for intent-based routing).</summary>
    public string? TargetAgentType { get; set; }

    /// <summary>File attachments.</summary>
    public List<MessageAttachment> Attachments { get; set; } = new();

    /// <summary>Request metadata (e.g., source platform).</summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Tenant scope attached to this message by the channel transport (VS Code
    /// extension, M365 Teams bot, etc.). When set, the host binds it to the
    /// ambient tenant context for the duration of message handling so MCP
    /// tools see the same identity as direct HTTP callers. See feature 048
    /// FR-021/FR-024.
    /// </summary>
    public TenantContextEnvelope? TenantContext { get; set; }

    /// <summary>When the message was sent.</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

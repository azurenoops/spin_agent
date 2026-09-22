using System.Text.Json.Serialization;

namespace Ato.Copilot.Chat.Models;

// ─── Enums ───────────────────────────────────────────────────────────────

/// <summary>
/// Represents the role of a message sender in a conversation.
/// </summary>
public enum MessageRole
{
    /// <summary>Message from a human user.</summary>
    User,
    /// <summary>AI-generated response.</summary>
    Assistant,
    /// <summary>System-level notification.</summary>
    System,
    /// <summary>Tool execution output.</summary>
    Tool
}

/// <summary>
/// Represents the lifecycle status of a chat message.
/// </summary>
public enum MessageStatus
{
    /// <summary>Client has submitted, not yet confirmed by server.</summary>
    Sending,
    /// <summary>Server has persisted the message.</summary>
    Sent,
    /// <summary>MCP Server is processing the message.</summary>
    Processing,
    /// <summary>AI response received and saved.</summary>
    Completed,
    /// <summary>Processing failed.</summary>
    Error,
    /// <summary>Scheduled for retry.</summary>
    Retry
}

/// <summary>
/// Categorizes attached files by their content type.
/// </summary>
public enum AttachmentType
{
    /// <summary>Default / unrecognized MIME types.</summary>
    Document,
    /// <summary>Image files (image/*).</summary>
    Image,
    /// <summary>Source code files.</summary>
    Code,
    /// <summary>Configuration files (JSON, YAML, XML).</summary>
    Configuration,
    /// <summary>Log files.</summary>
    Log
}

// ─── Entities ────────────────────────────────────────────────────────────

/// <summary>
/// The top-level container for a chat session.
/// </summary>
public class Conversation
{
    /// <summary>Primary key — auto-generated GUID.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Conversation title — auto-generated from first message or user-provided.</summary>
    public string Title { get; set; } = "New Conversation";

    /// <summary>Display identity only; never an authorization selector.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Server-derived qualified workspace/actor hash. Null legacy rows are inaccessible.</summary>
    [JsonIgnore]
    public string? OwnerKey { get; set; }

    /// <summary>Immutable system scope once a conversation has history or a selected system.</summary>
    public string? SystemId { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC last-modified timestamp.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft-archive flag.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Arbitrary key-value metadata (JSON-serialized).</summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    // ─── Navigation Properties ───────────────────────────────────────

    /// <summary>Messages in this conversation.</summary>
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>Optional contextual metadata for this conversation.</summary>
    public ConversationContext? Context { get; set; }
}

/// <summary>
/// An individual message within a conversation.
/// </summary>
public class ChatMessage
{
    /// <summary>Primary key — auto-generated GUID.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Foreign key to the parent Conversation.</summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>Message text content.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>The role of the message sender.</summary>
    public MessageRole Role { get; set; } = MessageRole.User;

    /// <summary>Message creation timestamp (UTC).</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Lifecycle status of the message.</summary>
    public MessageStatus Status { get; set; } = MessageStatus.Sending;

    /// <summary>Arbitrary metadata — intent, tool results, suggestions, errors (JSON-serialized).</summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>For threaded replies (nullable).</summary>
    public string? ParentMessageId { get; set; }

    /// <summary>Tool names involved in processing (JSON-serialized).</summary>
    public List<string> Tools { get; set; } = new();

    /// <summary>Tool execution output (JSON-serialized, nullable).</summary>
    public ToolExecutionResult? ToolResult { get; set; }

    // ─── Navigation Properties ───────────────────────────────────────

    /// <summary>Parent conversation.</summary>
    [JsonIgnore]
    public Conversation? Conversation { get; set; }

    /// <summary>File attachments for this message.</summary>
    public List<MessageAttachment> Attachments { get; set; } = new();
}

/// <summary>
/// Contextual metadata for a conversation — tracks analysis scope, workflow type, and tags.
/// </summary>
public class ConversationContext
{
    /// <summary>Primary key — auto-generated GUID.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Foreign key to the parent Conversation.</summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>Context type: "ato_scan", "deployment", "cost_analysis".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Display title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Summary text.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Arbitrary context data (JSON-serialized).</summary>
    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last access timestamp (UTC).</summary>
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Categorization tags (JSON-serialized).</summary>
    public List<string> Tags { get; set; } = new();

    // ─── Navigation Properties ───────────────────────────────────────

    /// <summary>Parent conversation.</summary>
    [JsonIgnore]
    public Conversation? Conversation { get; set; }
}

/// <summary>
/// A file attached to a message.
/// </summary>
public class MessageAttachment
{
    /// <summary>Primary key — auto-generated GUID.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Foreign key to the parent ChatMessage.</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Original filename.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME type of the file.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Filesystem path to the stored file.</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>Categorized file type.</summary>
    public AttachmentType Type { get; set; } = AttachmentType.Document;

    /// <summary>Upload timestamp (UTC).</summary>
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Arbitrary metadata (JSON-serialized).</summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    // ─── Navigation Properties ───────────────────────────────────────

    /// <summary>Parent message.</summary>
    [JsonIgnore]
    public ChatMessage? Message { get; set; }
}

// ─── Value Objects ───────────────────────────────────────────────────────

/// <summary>
/// Result of a tool invocation — serialized as JSON within ChatMessage.ToolResult.
/// </summary>
public class ToolExecutionResult
{
    /// <summary>Which tool was executed.</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Whether the tool execution succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>The tool's output payload.</summary>
    public object? Result { get; set; }

    /// <summary>Error message if the tool failed.</summary>
    public string? Error { get; set; }

    /// <summary>Input parameters passed to the tool.</summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>Execution timestamp.</summary>
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    /// <summary>How long the tool execution took.</summary>
    public TimeSpan Duration { get; set; }
}

// ─── DTOs ────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for sending a new message.
/// </summary>
public class SendMessageRequest
{
    /// <summary>Target conversation ID.</summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>User's message text (max 10,000 chars).</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Content field alias — maps to Message for SignalR compatibility.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>Pre-uploaded attachment IDs.</summary>
    public List<string>? AttachmentIds { get; set; }

    /// <summary>Additional request context.</summary>
    public Dictionary<string, object>? Context { get; set; }

    /// <summary>Gets the effective message content (Message or Content).</summary>
    public string GetContent() => !string.IsNullOrEmpty(Message) ? Message : Content ?? string.Empty;
}

/// <summary>
/// Response returned after processing a message.
/// </summary>
public class ChatResponse
{
    /// <summary>The AI response message ID.</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>AI response content.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Whether processing was successful.</summary>
    public bool Success { get; set; }

    /// <summary>Error message if processing failed.</summary>
    public string? Error { get; set; }

    /// <summary>Proactive suggestion actions.</summary>
    public List<SuggestedAction>? SuggestedActions { get; set; }

    /// <summary>Tools recommended for follow-up.</summary>
    public List<string>? RecommendedTools { get; set; }

    /// <summary>Processing metadata (intent, confidence, tool results, timing).</summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Request body for creating a new conversation.
/// </summary>
public class CreateConversationRequest
{
    /// <summary>Optional conversation title — defaults to "New Conversation".</summary>
    public string? Title { get; set; }

    /// <summary>Ignored legacy compatibility field; never selects conversation ownership.</summary>
    public string? UserId { get; set; }
}

/// <summary>
/// Standardized error response per Constitution Principle VII.
/// </summary>
public class ErrorResponse
{
    /// <summary>Human-readable error description.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Error category code.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Corrective guidance for the user.</summary>
    public string Suggestion { get; set; } = string.Empty;
}

/// <summary>
/// A proactive action suggested by the AI for follow-up.
/// </summary>
public class SuggestedAction
{
    /// <summary>Short display title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Action description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Pre-filled prompt text for this action.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Priority badge label (e.g. "High", "Medium").</summary>
    public string? Priority { get; set; }
}

using System.Text.Json.Serialization;
using Ato.Copilot.Agents.Common;

namespace Ato.Copilot.Mcp.Models;

public class McpRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("id")] public object Id { get; set; } = 0;
    [JsonPropertyName("method")] public string Method { get; set; } = string.Empty;
    [JsonPropertyName("params")] public object? Params { get; set; }
}

public class McpResponse
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("id")] public object Id { get; set; } = 0;
    [JsonPropertyName("result")] public object? Result { get; set; }
    [JsonPropertyName("error")] public McpError? Error { get; set; }
}

public class McpError
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("data")] public object? Data { get; set; }
}

public class McpTool
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("inputSchema")] public object? InputSchema { get; set; }
}

public class McpToolCall
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("arguments")] public Dictionary<string, object>? Arguments { get; set; }
}

public class McpToolResult
{
    [JsonPropertyName("content")] public List<McpContent> Content { get; set; } = new();
    [JsonPropertyName("isError")] public bool IsError { get; set; }

    public static McpToolResult Success(string text) => new()
    {
        Content = new List<McpContent> { new() { Type = "text", Text = text } },
        IsError = false
    };

    public static McpToolResult Error(string errorMessage) => new()
    {
        Content = new List<McpContent> { new() { Type = "text", Text = errorMessage } },
        IsError = true
    };
}

public class McpContent
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("mimeType")] public string? MimeType { get; set; }
    [JsonPropertyName("data")] public string? Data { get; set; }
}

public class McpChatResponse
{
    public bool Success { get; set; }
    public string Response { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// The agent that processed this request. Serialized as "agentUsed" in JSON
    /// to align with TypeScript client expectations (FR-003, R-003).
    /// </summary>
    [JsonPropertyName("agentUsed")]
    public string? AgentName { get; set; }

    /// <summary>
    /// Classified intent type derived from the selected agent
    /// (e.g., "compliance", "knowledgebase", "configuration", "general").
    /// </summary>
    public string? IntentType { get; set; }

    public double ProcessingTimeMs { get; set; }
    public List<ToolExecution> ToolsExecuted { get; set; } = new();

    /// <summary>
    /// Structured error details with machine-readable code, message, and corrective suggestion.
    /// </summary>
    public List<ErrorDetail> Errors { get; set; } = new();

    /// <summary>
    /// Suggested follow-up actions with display title and pre-filled prompt.
    /// Serialized as "suggestedActions" to match ChatService expectations.
    /// </summary>
    [JsonPropertyName("suggestedActions")]
    public List<AgentSuggestedAction> SuggestedActions { get; set; } = new();
    public bool RequiresFollowUp { get; set; }

    /// <summary>
    /// Human-readable prompt asking the user for missing information.
    /// </summary>
    public string? FollowUpPrompt { get; set; }

    /// <summary>
    /// List of field names or descriptions the agent needs from the user.
    /// </summary>
    public List<string> MissingFields { get; set; } = new();

    /// <summary>
    /// Intent-specific structured data payload. The "type" key determines
    /// Adaptive Card routing on the client side.
    /// </summary>
    public Dictionary<string, object>? Data { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Count of unresolvable outcomes in this response: tool calls to unknown tools
    /// plus any rounds where the agent produced no text (#628 empty-results counter).
    /// Non-zero here means the LLM requested capabilities that are not registered.
    /// </summary>
    public int EmptyResultsCount { get; set; }
}

/// <summary>
/// Structured error detail with machine-readable error code, human-readable message,
/// and corrective suggestion per Constitution Principle VII.
/// </summary>
/// <param name="ErrorCode">Machine-readable error code (e.g., "AGENT_TIMEOUT", "UNAUTHORIZED", "TOOL_FAILURE").</param>
/// <param name="Message">Human-readable error description.</param>
/// <param name="Suggestion">Corrective guidance for the user or client.</param>
public class ErrorDetail
{
    /// <summary>Machine-readable error code (e.g., "AGENT_TIMEOUT", "UNAUTHORIZED", "TOOL_FAILURE").</summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>Human-readable error description.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Corrective guidance for the user or client.</summary>
    public string? Suggestion { get; set; }

    /// <summary>Correlation/trace ID for log correlation (Activity.TraceId or conversationId).</summary>
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Records the outcome of a single tool execution within an agent processing cycle.
/// Included in <see cref="McpChatResponse.ToolsExecuted"/> for client-side rendering.
/// </summary>
public class ToolExecution
{
    /// <summary>Name of the executed tool.</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Whether the tool completed without error.</summary>
    public bool Success { get; set; }

    /// <summary>Wall-clock execution time in milliseconds.</summary>
    public double ExecutionTimeMs { get; set; }
}

// ─── SSE Streaming Event Models (FR-029a) ───

/// <summary>
/// Base class for all Server-Sent Event payloads emitted by /mcp/chat/stream.
/// </summary>
public abstract class SseEvent
{
    /// <summary>The event type discriminator (e.g., "agentRouted", "thinking", "complete").</summary>
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    /// <summary>UTC timestamp when the event was emitted.</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>Emitted when the orchestrator selects an agent to handle the request.</summary>
public class SseAgentRoutedEvent : SseEvent
{
    /// <inheritdoc />
    public override string Type => "agentRouted";

    /// <summary>Name of the selected agent.</summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>Confidence score that led to agent selection (0.0–1.0).</summary>
    public double Confidence { get; set; }
}

/// <summary>Emitted while the agent is reasoning before executing tools.</summary>
public class SseThinkingEvent : SseEvent
{
    /// <inheritdoc />
    public override string Type => "thinking";

    /// <summary>Human-readable thinking step description.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Emitted when a tool begins execution.</summary>
public class SseToolStartEvent : SseEvent
{
    /// <inheritdoc />
    public override string Type => "toolStart";

    /// <summary>Name of the tool being executed.</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Zero-based index of this tool in the execution sequence.</summary>
    public int ToolIndex { get; set; }
}

/// <summary>Emitted periodically during tool execution to report progress.</summary>
public class SseToolProgressEvent : SseEvent
{
    /// <inheritdoc />
    public override string Type => "toolProgress";

    /// <summary>Name of the tool reporting progress.</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Progress percentage (0–100).</summary>
    public int PercentComplete { get; set; }
}

/// <summary>Emitted when a tool finishes execution.</summary>
public class SseToolCompleteEvent : SseEvent
{
    /// <inheritdoc />
    public override string Type => "toolComplete";

    /// <summary>Name of the completed tool.</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Whether the tool succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Tool execution time in milliseconds.</summary>
    public double ExecutionTimeMs { get; set; }
}

/// <summary>Emitted to stream partial response text as it becomes available.</summary>
public class SsePartialEvent : SseEvent
{
    /// <inheritdoc />
    public override string Type => "partial";

    /// <summary>Partial response text chunk.</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>Emitted while the agent validates generated output (e.g., compliance checks).</summary>
public class SseValidatingEvent : SseEvent
{
    /// <inheritdoc />
    public override string Type => "validating";

    /// <summary>Validation step description.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Emitted once with the final full McpChatResponse payload when processing is complete.</summary>
public class SseCompleteEvent : SseEvent
{
    /// <inheritdoc />
    public override string Type => "complete";

    /// <summary>The complete enriched response payload.</summary>
    public McpChatResponse? Result { get; set; }
}

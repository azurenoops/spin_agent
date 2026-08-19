namespace Ato.Copilot.Core.Models.Provenance;

/// <summary>
/// Durable record of a single LLM invocation (#941 — Epic 10 provenance audit trail).
///
/// One row per model call, keyed by (ConversationId, CallIndex).  Stores hashes
/// of prompt text, never raw content, to satisfy privacy policy.  Additive schema:
/// legacy rows may have NULL for columns added in future slices (#940, #939).
/// </summary>
public class ModelCall
{
    /// <summary>Primary key — auto-generated GUID.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The conversation that triggered this call.</summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// 0-based ordinal within the conversation run.  Combined with ConversationId
    /// this uniquely identifies each LLM turn for replay purposes.
    /// </summary>
    public int CallIndex { get; set; }

    /// <summary>Provider identifier, e.g. "azure-openai", "foundry".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Model deployment name, e.g. "gpt-4o".</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Model version / snapshot string returned by the API, if available.</summary>
    public string? ModelVersion { get; set; }

    /// <summary>
    /// JSON object capturing sampled generation parameters:
    /// { temperature, top_p, max_tokens, seed }.
    /// Stored as text to remain schema-stable across provider changes.
    /// </summary>
    public string ParamsJson { get; set; } = "{}";

    /// <summary>
    /// SHA-256 (full hex, lowercase) of the system prompt text.
    /// Allows replay verification without storing raw prompts.
    /// </summary>
    public string? SystemPromptHash { get; set; }

    /// <summary>
    /// SHA-256 (full hex, lowercase) of the concatenated user prompt text.
    /// </summary>
    public string? UserPromptHash { get; set; }

    /// <summary>
    /// JSON array of tool calls issued during this invocation, e.g.
    /// [{"name":"get_control","callId":"...","args":{...}}].
    /// Empty array ("[]") when no tools were called.
    /// </summary>
    public string ToolCallsJson { get; set; } = "[]";

    /// <summary>Input prompt token count reported by the API; NULL if unavailable.</summary>
    public int? PromptTokens { get; set; }

    /// <summary>Completion token count reported by the API; NULL if unavailable.</summary>
    public int? CompletionTokens { get; set; }

    /// <summary>Wall-clock latency in milliseconds for this single API round-trip.</summary>
    public long LatencyMs { get; set; }

    /// <summary>
    /// SHA-256 (full hex, lowercase) of the final output text.
    /// Integrity check: on export this hash must match SHA-256(exported_response_text).
    /// </summary>
    public string? OutputContentHash { get; set; }

    /// <summary>UTC timestamp when this record was written.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

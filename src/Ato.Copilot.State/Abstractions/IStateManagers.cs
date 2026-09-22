namespace Ato.Copilot.State.Abstractions;

/// <summary>
/// Manages per-agent state for compliance operations
/// </summary>
public interface IAgentStateManager
{
    Task<T?> GetStateAsync<T>(string agentId, string key, CancellationToken cancellationToken = default);
    Task SetStateAsync<T>(string agentId, string key, T value, CancellationToken cancellationToken = default);
    Task ClearStateAsync(string agentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Manages conversation state across agent interactions
/// </summary>
public interface IConversationStateManager
{
    Task<ConversationState?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default);
    Task SaveConversationAsync(ConversationState state, CancellationToken cancellationToken = default);
    Task<string> CreateConversationAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Conversation state model
/// </summary>
public class ConversationState
{
    internal string? StorageKey { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public List<ConversationMessage> Messages { get; set; } = new();
    public Dictionary<string, object> Variables { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
}

public class ConversationMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

using System.Collections.Concurrent;
using Ato.Copilot.State.Abstractions;

namespace Ato.Copilot.State.Implementations;

/// <summary>
/// In-memory implementation of agent state management
/// </summary>
public class InMemoryAgentStateManager : IAgentStateManager
{
    private readonly ConcurrentDictionary<string, object> _state = new();

    /// <inheritdoc />
    public Task<T?> GetStateAsync<T>(string agentId, string key, CancellationToken cancellationToken = default)
    {
        var compositeKey = $"{agentId}:{key}";
        if (_state.TryGetValue(compositeKey, out var value) && value is T typedValue)
        {
            return Task.FromResult<T?>(typedValue);
        }
        return Task.FromResult<T?>(default);
    }

    /// <inheritdoc />
    public Task SetStateAsync<T>(string agentId, string key, T value, CancellationToken cancellationToken = default)
    {
        var compositeKey = $"{agentId}:{key}";
        _state[compositeKey] = value!;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearStateAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var prefix = $"{agentId}:";
        var keysToRemove = _state.Keys.Where(k => k.StartsWith(prefix)).ToList();
        foreach (var key in keysToRemove)
        {
            _state.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory implementation of conversation state management
/// </summary>
public class InMemoryConversationStateManager(IConversationIdentityAccessor? identityAccessor = null) : IConversationStateManager
{
    private readonly ConcurrentDictionary<string, ConversationState> _conversations = new();

    /// <inheritdoc />
    public Task<ConversationState?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = StorageKey(conversationId);
        _conversations.TryGetValue(key, out var state);
        return Task.FromResult(state is null ? null : Snapshot(state));
    }

    /// <inheritdoc />
    public Task SaveConversationAsync(ConversationState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = identityAccessor?.Current;
        var key = identity?.StorageKey(state.Id) ?? $"local:{state.Id}";
        if (state.StorageKey is not null && state.StorageKey != key)
            throw new UnauthorizedAccessException("Conversation belongs to another workspace or actor.");
        state.StorageKey = key;
        if (identity is not null)
            state.UserId = identity.ActorId;
        state.LastActivityAt = DateTime.UtcNow;
        _conversations[key] = Snapshot(state);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<string> CreateConversationAsync(CancellationToken cancellationToken = default)
    {
        var state = new ConversationState();
        await SaveConversationAsync(state, cancellationToken);
        return state.Id;
    }

    private string StorageKey(string id) => identityAccessor?.Current?.StorageKey(id) ?? $"local:{id}";

    private static ConversationState Snapshot(ConversationState state) => new()
    {
        Id = state.Id,
        StorageKey = state.StorageKey,
        UserId = state.UserId,
        CreatedAt = state.CreatedAt,
        LastActivityAt = state.LastActivityAt,
        Messages = state.Messages.Select(m => new ConversationMessage
        {
            Role = m.Role, Content = m.Content, Timestamp = m.Timestamp
        }).ToList(),
        Variables = new(state.Variables)
    };
}

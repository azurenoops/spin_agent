using Ato.Copilot.Channels.Abstractions;
using Ato.Copilot.Channels.Configuration;
using Ato.Copilot.Channels.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Channels.Implementations;

/// <summary>
/// Default message handler with optional AgentInvoker delegate.
/// When an AgentInvoker is configured, stores user messages, sends AgentThinking notifications,
/// invokes the agent, and stores the response. When no invoker is configured,
/// behavior depends on ChannelOptions.DefaultHandlerBehavior (Echo or Error).
/// </summary>
public class DefaultMessageHandler : IMessageHandler
{
    private readonly IChannelManager _channelManager;
    private readonly IConversationStateManager _conversationState;
    private readonly Func<IncomingMessage, CancellationToken, Task<ChannelMessage>>? _agentInvoker;
    private readonly IOptions<ChannelOptions> _options;
    private readonly ILogger<DefaultMessageHandler> _logger;
    private readonly ITenantScopeBinder _tenantScopeBinder;

    /// <summary>
    /// Initializes a new instance of DefaultMessageHandler.
    /// </summary>
    public DefaultMessageHandler(
        IChannelManager channelManager,
        IConversationStateManager conversationState,
        IOptions<ChannelOptions> options,
        ILogger<DefaultMessageHandler> logger,
        ITenantScopeBinder tenantScopeBinder,
        Func<IncomingMessage, CancellationToken, Task<ChannelMessage>>? agentInvoker = null)
    {
        _channelManager = channelManager;
        _conversationState = conversationState;
        _options = options;
        _logger = logger;
        _tenantScopeBinder = tenantScopeBinder;
        _agentInvoker = agentInvoker;
    }

    /// <inheritdoc />
    public async Task<ChannelMessage> HandleMessageAsync(IncomingMessage message, CancellationToken ct = default)
    {
        _logger.LogInformation("Handling message from connection {ConnectionId} in conversation {ConversationId}",
            message.ConnectionId, message.ConversationId);

        // Bind the inbound tenant envelope (if any) for the duration of message
        // processing so persistence + agent invocation see the same ambient
        // tenant context (FR-021/FR-024). NullTenantScopeBinder is a safe no-op.
        using var tenantScope = _tenantScopeBinder.Bind(message.TenantContext);

        try
        {
            // Store user message via IConversationStateManager
            await StoreUserMessageAsync(message, ct);

            if (_agentInvoker is not null)
            {
                return await HandleWithAgentInvokerAsync(message, ct);
            }

            return HandleWithDefaultBehavior(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message from connection {ConnectionId}", message.ConnectionId);

            // Exception always returns Error regardless of DefaultHandlerBehavior setting (FR-012)
            return new ChannelMessage
            {
                ConversationId = message.ConversationId,
                Type = MessageType.Error,
                Content = $"Error processing message: {ex.Message}",
                IsComplete = true
            };
        }
    }

    private async Task<ChannelMessage> HandleWithAgentInvokerAsync(IncomingMessage message, CancellationToken ct)
    {
        // Send AgentThinking notification (FR-011)
        var thinkingMessage = new ChannelMessage
        {
            ConversationId = message.ConversationId,
            Type = MessageType.AgentThinking,
            Content = string.Empty,
            IsComplete = false
        };
        await _channelManager.SendToConversationAsync(message.ConversationId, thinkingMessage, ct);

        // Invoke the configured agent
        var response = await _agentInvoker!(message, ct);

        // Store the response (FR-011)
        await StoreAssistantMessageAsync(message.ConversationId, response, ct);

        _logger.LogInformation("Agent invocation completed for conversation {ConversationId}, agent: {AgentType}",
            message.ConversationId, response.AgentType ?? "unknown");

        return response;
    }

    private ChannelMessage HandleWithDefaultBehavior(IncomingMessage message)
    {
        var behavior = _options.Value.DefaultHandlerBehavior;

        return behavior switch
        {
            DefaultHandlerBehavior.Echo => new ChannelMessage
            {
                ConversationId = message.ConversationId,
                Type = MessageType.AgentResponse,
                Content = message.Content,
                IsComplete = true
            },
            DefaultHandlerBehavior.Error => new ChannelMessage
            {
                ConversationId = message.ConversationId,
                Type = MessageType.Error,
                Content = "No agent invoker configured",
                IsComplete = true
            },
            _ => throw new InvalidOperationException($"Unknown DefaultHandlerBehavior: {behavior}")
        };
    }

    private async Task StoreUserMessageAsync(IncomingMessage message, CancellationToken ct)
    {
        var conversationState = await _conversationState.GetConversationAsync(message.ConversationId, ct)
                                ?? new ConversationState { Id = message.ConversationId };

        conversationState.Messages.Add(new ConversationMessage
        {
            Role = "user",
            Content = message.Content,
            Timestamp = DateTime.UtcNow
        });
        conversationState.LastActivityAt = DateTime.UtcNow;

        await _conversationState.SaveConversationAsync(conversationState, ct);
    }

    private async Task StoreAssistantMessageAsync(string conversationId, ChannelMessage response, CancellationToken ct)
    {
        var conversationState = await _conversationState.GetConversationAsync(conversationId, ct);
        if (conversationState is null) return;

        conversationState.Messages.Add(new ConversationMessage
        {
            Role = "assistant",
            Content = response.Content,
            Timestamp = DateTime.UtcNow
        });
        conversationState.LastActivityAt = DateTime.UtcNow;

        await _conversationState.SaveConversationAsync(conversationState, ct);
    }
}

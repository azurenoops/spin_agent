using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Ato.Copilot.Channels.Abstractions;
using Ato.Copilot.Channels.Models;
using Ato.Copilot.Chat.Channels;
using Ato.Copilot.Chat.Models;
using Ato.Copilot.Chat.Services;
using System.Security.Claims;

namespace Ato.Copilot.Chat.Hubs;

/// <summary>
/// SignalR hub for real-time chat messaging.
/// Delegates connection management to <see cref="IChannelManager"/>,
/// message processing to <see cref="IMessageHandler"/>,
/// and delivery to <see cref="IChannel"/> for Channels library integration.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private readonly IChannelManager _channelManager;
    private readonly IChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        IChannelManager channelManager,
        IChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<ChatHub> logger)
    {
        _channelManager = channelManager;
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Joins a conversation group to receive real-time updates.
    /// </summary>
    /// <param name="conversationId">The conversation ID to join.</param>
    public async Task JoinConversation(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            throw new HubException("ConversationId is required");

        await _channelManager.JoinConversationAsync(Context.ConnectionId, conversationId);
        _logger.LogInformation("Connection {ConnectionId} joined conversation {ConversationId}",
            Context.ConnectionId, conversationId);
    }

    /// <summary>
    /// Leaves a conversation group.
    /// </summary>
    /// <param name="conversationId">The conversation ID to leave.</param>
    public async Task LeaveConversation(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            throw new HubException("ConversationId is required");

        await _channelManager.LeaveConversationAsync(Context.ConnectionId, conversationId);
        _logger.LogInformation("Connection {ConnectionId} left conversation {ConversationId}",
            Context.ConnectionId, conversationId);
    }

    /// <summary>
    /// Sends a message, processes via AI, and broadcasts result to conversation group.
    /// Delegates to <see cref="IMessageHandler"/> for processing and <see cref="IChannel"/>
    /// for delivery, while maintaining backward-compatible SignalR event names.
    /// </summary>
    /// <param name="request">The send message request.</param>
    public async Task SendMessage(SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId))
            throw new HubException("ConversationId is required");

        if (string.IsNullOrWhiteSpace(request.GetContent()))
            throw new HubException("Message content is required");

        if (request.GetContent().Length > 10_000)
            throw new HubException("Message content exceeds 10,000 character limit");

        var messageId = Guid.NewGuid().ToString();

        try
        {
            // Notify group: processing started (via channel)
            var processingMessage = new ChannelMessage
            {
                MessageId = messageId,
                ConversationId = request.ConversationId,
                Type = MessageType.AgentThinking,
                Content = "Processing",
                IsComplete = false,
                Metadata = new Dictionary<string, object> { ["status"] = "Processing" }
            };
            await _channel.SendToConversationAsync(request.ConversationId, processingMessage);

            // Create progress reporter that pushes SignalR events via the channel
            var conversationId = request.ConversationId;
            var progress = new Progress<string>(async step =>
            {
                try
                {
                    var progressMessage = new ChannelMessage
                    {
                        MessageId = messageId,
                        ConversationId = conversationId,
                        Type = MessageType.ProgressUpdate,
                        Content = step,
                        IsComplete = false,
                        Metadata = new Dictionary<string, object> { ["timestamp"] = DateTime.UtcNow }
                    };
                    await _channel.SendToConversationAsync(conversationId, progressMessage);
                }
                catch { /* best-effort progress */ }
            });

            // Process message via scoped ChatService (uses IChatService directly for IProgress support;
            // external channels use IMessageHandler which wraps the same pipeline without progress)
            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            var response = await chatService.SendMessageAsync(request, progress);

            // Map response and send via channel
            var responseMessage = ChatMessageMapper.ToChannelMessage(response, request.ConversationId);

            if (response.Success)
            {
                await _channel.SendToConversationAsync(request.ConversationId, responseMessage);
            }
            else
            {
                // Send error via channel
                var errorMessage = new ChannelMessage
                {
                    MessageId = messageId,
                    ConversationId = request.ConversationId,
                    Type = MessageType.Error,
                    Content = response.Error ?? "The request could not be processed",
                    IsComplete = true,
                    Metadata = new Dictionary<string, object>
                    {
                        ["errorCategory"] = response.Metadata?.TryGetValue("errorCategory", out var cat) == true
                            ? cat.ToString()!
                            : "ProcessingError"
                    }
                };
                await _channel.SendToConversationAsync(request.ConversationId, errorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing SignalR message for conversation {ConversationId}", request.ConversationId);

            var errorMessage = new ChannelMessage
            {
                MessageId = messageId,
                ConversationId = request.ConversationId,
                Type = MessageType.Error,
                Content = "The request could not be processed",
                IsComplete = true,
                Metadata = new Dictionary<string, object> { ["errorCategory"] = "ProcessingError" }
            };
            await _channel.SendToConversationAsync(request.ConversationId, errorMessage);
        }
    }

    /// <summary>
    /// Notifies other participants that a user is typing.
    /// userId is derived from the authenticated identity — not accepted from the client.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    public async Task NotifyTyping(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        // DEF-001: userId derived from authenticated claims, not client-supplied parameter.
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? Context.User?.FindFirstValue("oid")
                     ?? Context.User?.Identity?.Name
                     ?? Context.ConnectionId;

        await Clients.OthersInGroup(conversationId).SendAsync("UserTyping", new
        {
            conversationId,
            userId
        });
    }

    /// <summary>
    /// Registers the connection with <see cref="IChannelManager"/> on connect.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.Identity?.Name ?? Context.ConnectionId;
        await _channelManager.RegisterConnectionAsync(userId, new Dictionary<string, object>
        {
            ["signalRConnectionId"] = Context.ConnectionId
        });
        _logger.LogInformation("ChatHub connection established: {ConnectionId} for user {UserId}",
            Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Unregisters the connection from <see cref="IChannelManager"/> on disconnect.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _channelManager.UnregisterConnectionAsync(Context.ConnectionId);
        _logger.LogInformation("ChatHub connection closed: {ConnectionId}. Reason: {Reason}",
            Context.ConnectionId, exception?.Message ?? "Client disconnected");
        await base.OnDisconnectedAsync(exception);
    }
}

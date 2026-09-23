using Ato.Copilot.Channels.Abstractions;
using Ato.Copilot.Channels.Models;
using Ato.Copilot.Chat.Models;
using Ato.Copilot.Chat.Services;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Chat.Channels;

/// <summary>
/// <see cref="IMessageHandler"/> adapter that delegates message processing
/// to the existing <see cref="IChatService"/> pipeline (DB persistence → MCP HTTP call).
/// This bridges the Channels abstraction with the Chat app's established processing flow.
/// </summary>
public sealed class ChatServiceMessageHandler : IMessageHandler
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatServiceMessageHandler> _logger;

    public ChatServiceMessageHandler(IChatService chatService, ILogger<ChatServiceMessageHandler> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChannelMessage> HandleMessageAsync(IncomingMessage message, CancellationToken ct = default)
    {
        _logger.LogDebug("Handling message from connection {ConnectionId} in conversation {ConversationId}",
            message.ConnectionId, message.ConversationId);

        try
        {
            // Map IncomingMessage → SendMessageRequest
            var request = new SendMessageRequest
            {
                ConversationId = message.ConversationId,
                Message = message.Content,
                Context = message.Metadata.Count > 0
                    ? new Dictionary<string, object>(message.Metadata)
                    : null,
                AttachmentIds = message.Metadata.TryGetValue("attachmentIds", out var ids) && ids is List<string> attachmentIds
                    ? attachmentIds
                    : null
            };

            // Delegate to existing ChatService pipeline (DB persist → MCP HTTP → DB persist)
            ct.ThrowIfCancellationRequested();
            var response = ct.CanBeCanceled
                ? await _chatService.SendMessageAsync(request, null, ct)
                : await _chatService.SendMessageAsync(request);

            // Map ChatResponse → ChannelMessage
            var channelMessage = ChatMessageMapper.ToChannelMessage(response, message.ConversationId);

            _logger.LogDebug("Message handled successfully for conversation {ConversationId}: {Success}",
                message.ConversationId, response.Success);

            return channelMessage;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message for conversation {ConversationId}", message.ConversationId);

            return new ChannelMessage
            {
                ConversationId = message.ConversationId,
                Type = MessageType.Error,
                Content = "The request could not be processed",
                IsComplete = true,
                Metadata = new Dictionary<string, object>
                {
                    ["errorCategory"] = "ProcessingError",
                    ["errorMessage"] = ex.Message
                }
            };
        }
    }
}

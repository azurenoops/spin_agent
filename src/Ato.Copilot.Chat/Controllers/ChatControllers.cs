using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Ato.Copilot.Chat.Models;
using Ato.Copilot.Chat.Services;

namespace Ato.Copilot.Chat.Controllers;

/// <summary>
/// REST API controller for chat messages.
/// </summary>
[Authorize]
[ApiController]
[Route("api/messages")]
public class MessagesController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(IChatService chatService, ILogger<MessagesController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>
    /// Gets messages for a conversation with pagination.
    /// GET /api/messages?conversationId={id}&amp;skip=0&amp;take=100
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMessages(
        [FromQuery] string conversationId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return BadRequest(new ErrorResponse
            {
                Message = "ConversationId is required",
                Error = "Validation",
                Suggestion = "Provide a valid conversationId query parameter"
            });

        var messages = await _chatService.GetMessagesAsync(conversationId, skip, take);
        return Ok(messages);
    }

    /// <summary>
    /// Sends a message and receives an AI response.
    /// POST /api/messages
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> PostMessage([FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId))
            return BadRequest(new ErrorResponse
            {
                Message = "ConversationId is required",
                Error = "Validation",
                Suggestion = "Provide a valid conversationId"
            });

        if (string.IsNullOrWhiteSpace(request.GetContent()))
            return BadRequest(new ErrorResponse
            {
                Message = "Message content is required",
                Error = "Validation",
                Suggestion = "Provide a non-empty message"
            });

        _logger.LogInformation("Sending message to conversation {ConversationId}", request.ConversationId);
        var response = await _chatService.SendMessageAsync(request);
        return Ok(response);
    }

    /// <summary>
    /// Uploads a file attachment to a message.
    /// POST /api/messages/{messageId}/attachments
    /// </summary>
    [HttpPost("{messageId}/attachments")]
    public async Task<IActionResult> UploadAttachment(string messageId, IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new ErrorResponse
            {
                Message = "File is required",
                Error = "Validation",
                Suggestion = "Attach a non-empty file"
            });

        if (file.Length > 10_485_760) // 10 MB
            return BadRequest(new ErrorResponse
            {
                Message = "File size exceeds the 10 MB limit",
                Error = "FileTooLarge",
                Suggestion = "Reduce the file size or split into smaller files"
            });

        _logger.LogInformation("Uploading attachment {FileName} ({Size} bytes) to message {MessageId}",
            file.FileName, file.Length, messageId);

        await using var stream = file.OpenReadStream();
        var attachment = await _chatService.SaveAttachmentAsync(messageId, file.FileName, file.ContentType, stream);
        return Ok(attachment);
    }
}

/// <summary>
/// REST API controller for conversations.
/// </summary>
[Authorize]
[ApiController]
[Route("api/conversations")]
public class ConversationsController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<ConversationsController> _logger;

    public ConversationsController(IChatService chatService, ILogger<ConversationsController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>
    /// Gets conversations for a user with pagination.
    /// GET /api/conversations?skip=0&amp;take=50
    /// userId is derived from the authenticated identity — not accepted from the client.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetConversations(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        // DEF-001: userId derived from authenticated claims, not client-supplied query string.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("oid")
                     ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var conversations = await _chatService.GetConversationsAsync(userId, skip, take);
        return Ok(conversations);
    }

    /// <summary>
    /// Gets a single conversation with messages and context.
    /// GET /api/conversations/{conversationId}
    /// </summary>
    [HttpGet("{conversationId}")]
    public async Task<IActionResult> GetConversation(string conversationId)
    {
        var conversation = await _chatService.GetConversationAsync(conversationId);
        if (conversation == null)
            return NotFound(new ErrorResponse
            {
                Message = "Conversation not found",
                Error = "NotFound",
                Suggestion = "Verify the conversation ID or create a new conversation"
            });

        return Ok(conversation);
    }

    /// <summary>
    /// Creates a new conversation.
    /// POST /api/conversations
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateConversation([FromBody] CreateConversationRequest request)
    {
        var conversation = await _chatService.CreateConversationAsync(request);
        return Ok(conversation);
    }

    /// <summary>
    /// Deletes a conversation and all associated data.
    /// DELETE /api/conversations/{conversationId}
    /// </summary>
    [HttpDelete("{conversationId}")]
    public async Task<IActionResult> DeleteConversation(string conversationId)
    {
        try
        {
            await _chatService.DeleteConversationAsync(conversationId);
            return Ok(new { message = "Conversation deleted successfully" });
        }
        catch (InvalidOperationException)
        {
            return NotFound(new ErrorResponse
            {
                Message = "Conversation not found",
                Error = "NotFound",
                Suggestion = "Verify the conversation ID"
            });
        }
    }

    /// <summary>
    /// Searches conversations by title or message content.
    /// GET /api/conversations/search?query=keyword
    /// userId is derived from the authenticated identity — not accepted from the client.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchConversations(
        [FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new ErrorResponse
            {
                Message = "Search query is required",
                Error = "Validation",
                Suggestion = "Provide a non-empty search query"
            });

        // DEF-001: userId derived from authenticated claims, not client-supplied query string.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("oid")
                     ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var conversations = await _chatService.SearchConversationsAsync(query, userId);
        return Ok(conversations);
    }
}

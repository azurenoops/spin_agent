using Ato.Copilot.Chat.Models;

namespace Ato.Copilot.Chat.Services;

/// <summary>
/// Core chat service interface for message handling and conversation management.
/// Persistence methods require the authenticated HTTP user, exact bearer, and authoritative
/// workspace resolution, and honor HttpContext.RequestAborted even for direct consumers.
/// Legacy userId arguments are compatibility-only and never select ownership.
/// </summary>
public interface IChatService
{
    // ─── Messaging (US1) ─────────────────────────────────────────

    /// <summary>
    /// Sends a user message and receives an AI response via MCP Server.
    /// </summary>
    /// <param name="request">The send message request.</param>
    /// <param name="progress">Optional progress reporter for status updates.</param>
    /// <returns>The AI chat response.</returns>
    Task<ChatResponse> SendMessageAsync(SendMessageRequest request, IProgress<string>? progress = null);

    /// <summary>Sends with explicit cancellation, linked to the authenticated HTTP request lifetime.</summary>
    Task<ChatResponse> SendMessageAsync(SendMessageRequest request, IProgress<string>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves messages for a conversation with pagination.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="skip">Number of messages to skip.</param>
    /// <param name="take">Number of messages to return.</param>
    /// <returns>List of chat messages ordered by timestamp ascending.</returns>
    Task<List<ChatMessage>> GetMessagesAsync(string conversationId, int skip = 0, int take = 100);

    /// <summary>
    /// Retrieves the last N messages for context window construction.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="maxMessages">Maximum messages to retrieve (default 20).</param>
    /// <returns>List of recent messages ordered by timestamp ascending.</returns>
    Task<List<ChatMessage>> GetConversationHistoryAsync(string conversationId, int maxMessages = 20);

    // ─── Conversations (US2) ─────────────────────────────────────

    /// <summary>
    /// Creates a new conversation.
    /// </summary>
    /// <param name="request">The create conversation request.</param>
    /// <returns>The created conversation.</returns>
    Task<Conversation> CreateConversationAsync(CreateConversationRequest request);

    /// <summary>
    /// Retrieves conversations for a user with pagination.
    /// </summary>
    /// <param name="userId">Ignored compatibility field; ownership comes from the validated actor/workspace.</param>
    /// <param name="skip">Number of conversations to skip.</param>
    /// <param name="take">Number of conversations to return.</param>
    /// <returns>List of conversations sorted by UpdatedAt descending.</returns>
    Task<List<Conversation>> GetConversationsAsync(string userId = "default-user", int skip = 0, int take = 50);

    /// <summary>
    /// Retrieves a single conversation with its messages and context.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <returns>The conversation with messages and context, or null if not found.</returns>
    Task<Conversation?> GetConversationAsync(string conversationId);

    /// <summary>
    /// Searches conversations by title or message content.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="userId">Ignored compatibility field; ownership comes from the validated actor/workspace.</param>
    /// <returns>List of matching conversations (max 20).</returns>
    Task<List<Conversation>> SearchConversationsAsync(string query, string userId = "default-user");

    /// <summary>
    /// Deletes a conversation and all related data (messages, attachments, files).
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    Task DeleteConversationAsync(string conversationId);

    // ─── Context (US2) ───────────────────────────────────────────

    /// <summary>
    /// Creates or updates the conversation context.
    /// </summary>
    /// <param name="context">The conversation context to upsert.</param>
    /// <returns>The upserted conversation context.</returns>
    Task<ConversationContext> CreateOrUpdateContextAsync(ConversationContext context);

    // ─── Attachments (US5) ───────────────────────────────────────

    /// <summary>
    /// Saves a file attachment to disk and creates a database record.
    /// </summary>
    /// <param name="messageId">The message ID to attach the file to.</param>
    /// <param name="fileName">The original file name.</param>
    /// <param name="contentType">The MIME content type.</param>
    /// <param name="stream">The file data stream.</param>
    /// <returns>The created message attachment record.</returns>
    Task<MessageAttachment> SaveAttachmentAsync(string messageId, string fileName, string contentType, Stream stream);

    /// <summary>
    /// Determines the attachment type from MIME content type.
    /// </summary>
    /// <param name="contentType">The MIME content type.</param>
    /// <returns>The classified attachment type.</returns>
    AttachmentType GetAttachmentTypeFromContentType(string contentType);

    /// <summary>
    /// Generates an auto-analysis prompt based on file extension.
    /// </summary>
    /// <param name="fileName">The file name with extension.</param>
    /// <returns>An analysis prompt string.</returns>
    string GenerateAnalysisPrompt(string fileName);
}

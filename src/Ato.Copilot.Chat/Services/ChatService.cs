using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Chat.Data;
using Ato.Copilot.Chat.Models;
using Ato.Copilot.Core.Interfaces;
using Ato.Copilot.Chat.Services.Auth;

namespace Ato.Copilot.Chat.Services;

/// <summary>
/// Core chat service implementation for message handling and conversation management.
/// </summary>
public partial class ChatService : IChatService
{
    private readonly ChatDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChatService> _logger;
    private readonly IPathSanitizationService _pathSanitizer;
    private readonly ChatWorkspaceResolver _workspaces;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ChatService(
        ChatDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        ILogger<ChatService> logger,
        IPathSanitizationService pathSanitizer,
        ChatWorkspaceResolver workspaces)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _pathSanitizer = pathSanitizer;
        _workspaces = workspaces;
    }

    // ─── Messaging (US1) ─────────────────────────────────────────

    /// <inheritdoc/>
    public Task<ChatResponse> SendMessageAsync(SendMessageRequest request, IProgress<string>? progress = null)
        => SendMessageAsync(request, progress, RequestAborted);

    /// <inheritdoc/>
    public async Task<ChatResponse> SendMessageAsync(SendMessageRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, RequestAborted);
        var ct = cancellation.Token;
        var workspace = await _workspaces.ResolveAsync(ct);
        var ownedConversation = await RequireConversationAsync(request.ConversationId, workspace, ct);
        if (request.AttachmentIds is { Count: > 0 })
        {
            var ids = request.AttachmentIds.Distinct().ToArray();
            if (await _dbContext.Attachments.CountAsync(a => ids.Contains(a.Id)
                && a.Message!.ConversationId == ownedConversation.Id, ct) != ids.Length)
                throw new ChatWorkspaceException(404, "ATTACHMENT_NOT_FOUND", "Attachment not found in this conversation.");
        }
        await BindSystemAsync(ownedConversation, request.Context, workspace, ct);
        var stopwatch = Stopwatch.StartNew();
        var messageId = Guid.NewGuid().ToString();

        try
        {
            progress?.Report("Saving message...");

            // Persist user message with status Sent
            var userMessage = new ChatMessage
            {
                Id = messageId,
                ConversationId = request.ConversationId,
                Content = request.GetContent(),
                Role = MessageRole.User,
                Timestamp = DateTime.UtcNow,
                Status = MessageStatus.Sent
            };
            _dbContext.Messages.Add(userMessage);
            await _dbContext.SaveChangesAsync(ct);

            // Transition to Processing
            userMessage.Status = MessageStatus.Processing;
            await _dbContext.SaveChangesAsync(ct);

            progress?.Report("Building conversation context...");

            // Build context window (last 20 messages)
            var history = await _dbContext.Messages
                .Where(m => m.ConversationId == ownedConversation.Id && m.Conversation!.OwnerKey == workspace.OwnerKey)
                .OrderByDescending(m => m.Timestamp).Take(20).OrderBy(m => m.Timestamp).ToListAsync(ct);

            // Call MCP Server via SSE streaming endpoint for real-time progress
            var client = _httpClientFactory.CreateClient("McpServer");
            var mcpRequest = new
            {
                conversationId = request.ConversationId,
                message = request.GetContent(),
                conversationHistory = history.Select(m => new { role = m.Role.ToString(), content = m.Content }),
                context = BindContext(request.Context, workspace, ownedConversation.SystemId)
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(mcpRequest, JsonOptions),
                Encoding.UTF8,
                "application/json");

            progress?.Report("Connecting to ATO Copilot...");

            JsonElement? mcpResult = null;
            string? streamError = null;

            try
            {
                // Use streaming endpoint for real-time progress
                using var httpRequest = workspace.Request(HttpMethod.Post, "/mcp/chat/stream", jsonContent);
                using var mcpResponse = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

                if (mcpResponse.IsSuccessStatusCode)
                {
                    using var stream = await mcpResponse.Content.ReadAsStreamAsync(ct);
                    using var reader = new StreamReader(stream);

                    var dataBuffer = new System.Text.StringBuilder();
                    var inDataBlock = false;

                    while (await reader.ReadLineAsync(ct) is { } line)
                    {

                        // SSE spec: empty line terminates an event
                        if (string.IsNullOrEmpty(line))
                        {
                            if (inDataBlock && dataBuffer.Length > 0)
                            {
                                // Process the accumulated data block
                                var eventJson = dataBuffer.ToString();
                                dataBuffer.Clear();
                                inDataBlock = false;

                                try
                                {
                                    var evt = JsonSerializer.Deserialize<JsonElement>(eventJson);
                                    var eventType = evt.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

                                    switch (eventType)
                                    {
                                        case "progress":
                                            if (evt.TryGetProperty("step", out var stepProp))
                                                progress?.Report(stepProp.GetString() ?? "Processing...");
                                            break;
                                        case "result":
                                            if (evt.TryGetProperty("data", out var dataProp))
                                                mcpResult = dataProp;
                                            break;
                                        case "error":
                                            streamError = evt.TryGetProperty("error", out var errorProp)
                                                ? errorProp.GetString()
                                                : "Unknown streaming error";
                                            break;
                                    }
                                }
                                catch (JsonException ex)
                                {
                                    _logger.LogWarning("Failed to parse SSE event: {Error} | Data: {Data}", ex.Message, eventJson);
                                }
                            }
                            continue;
                        }

                        if (line.StartsWith("data: "))
                        {
                            dataBuffer.Append(line[6..]);
                            inDataBlock = true;
                        }
                        else if (inDataBlock)
                        {
                            // Continuation line for multi-line data
                            dataBuffer.Append(line);
                        }
                    }

                    // Handle last event if stream ends without trailing blank line
                    if (inDataBlock && dataBuffer.Length > 0)
                    {
                        try
                        {
                            var evt = JsonSerializer.Deserialize<JsonElement>(dataBuffer.ToString());
                            var eventType = evt.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

                            if (eventType == "result" && evt.TryGetProperty("data", out var dataProp))
                                mcpResult = dataProp;
                            else if (eventType == "error")
                                streamError = evt.TryGetProperty("error", out var errorProp)
                                    ? errorProp.GetString() : "Unknown streaming error";
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning("Failed to parse final SSE event: {Error}", ex.Message);
                        }
                    }
                }
                else
                {
                    // Fallback: call non-streaming endpoint
                    _logger.LogWarning("Streaming endpoint returned {StatusCode}, falling back to sync endpoint",
                        mcpResponse.StatusCode);

                    var fallbackContent = new StringContent(
                        JsonSerializer.Serialize(mcpRequest, JsonOptions),
                        Encoding.UTF8,
                        "application/json");
                    if (mcpResponse.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                        throw new ChatWorkspaceException((int)mcpResponse.StatusCode, "CHAT_UPSTREAM_DENIED", "Upstream chat authorization denied.");
                    using var fallbackRequest = workspace.Request(HttpMethod.Post, "/mcp/chat", fallbackContent);
                    using var fallbackResponse = await client.SendAsync(fallbackRequest, ct);
                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        var responseContent = await fallbackResponse.Content.ReadAsStringAsync(ct);
                        mcpResult = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    }
                    else
                    {
                        streamError = $"MCP server returned status {fallbackResponse.StatusCode}";
                    }
                }
            }
            catch (Exception ex) when (ex is not ChatWorkspaceException && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Streaming MCP endpoint failed, trying fallback");
                try
                {
                    var fallbackContent = new StringContent(
                        JsonSerializer.Serialize(mcpRequest, JsonOptions),
                        Encoding.UTF8,
                        "application/json");
                    using var fallbackRequest = workspace.Request(HttpMethod.Post, "/mcp/chat", fallbackContent);
                    using var fallbackResponse = await client.SendAsync(fallbackRequest, ct);
                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        var responseContent = await fallbackResponse.Content.ReadAsStringAsync(ct);
                        mcpResult = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    }
                    else
                    {
                        streamError = $"Fallback MCP endpoint returned status {fallbackResponse.StatusCode}";
                    }
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Fallback MCP endpoint also failed");
                    throw;
                }
            }

            stopwatch.Stop();

            progress?.Report("Finalizing response...");

            if (streamError != null)
            {
                _logger.LogError("MCP Server streaming error: {Error}", streamError);

                userMessage.Status = MessageStatus.Error;
                userMessage.Metadata = new Dictionary<string, object>
                {
                    ["error"] = streamError,
                    ["errorCategory"] = "McpError"
                };
                await _dbContext.SaveChangesAsync(ct);

                return new ChatResponse
                {
                    MessageId = messageId,
                    Content = "",
                    Success = false,
                    Error = streamError
                };
            }

            if (mcpResult.HasValue)
            {
                var result = mcpResult.Value;

                // MCP server returns "response" property; fall back to "content" for compatibility
                var aiContent = result.TryGetProperty("response", out var responseProp)
                    ? responseProp.GetString()
                    : result.TryGetProperty("content", out var contentProp)
                        ? contentProp.GetString()
                        : null;
                aiContent ??= "I processed your request but have no specific response.";

                // Build metadata
                var metadata = new Dictionary<string, object>
                {
                    ["processingTimeMs"] = stopwatch.ElapsedMilliseconds
                };

                if (result.TryGetProperty("metadata", out var metadataProp))
                {
                    foreach (var prop in metadataProp.EnumerateObject())
                    {
                        metadata[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString()!,
                            JsonValueKind.Number => prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.Value.ToString()
                        };
                    }
                }

                // Persist AI response
                var assistantMessage = new ChatMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    ConversationId = request.ConversationId,
                    Content = aiContent,
                    Role = MessageRole.Assistant,
                    Timestamp = DateTime.UtcNow,
                    Status = MessageStatus.Completed,
                    Metadata = metadata
                };
                _dbContext.Messages.Add(assistantMessage);

                // Update user message to Completed
                userMessage.Status = MessageStatus.Completed;

                // Update conversation timestamp
                var conversation = ownedConversation;
                if (conversation != null)
                {
                    conversation.UpdatedAt = DateTime.UtcNow;
                    // Auto-title from first user message
                    if (conversation.Title == "New Conversation")
                    {
                        conversation.Title = GenerateTitle(request.GetContent());
                    }
                }

                await _dbContext.SaveChangesAsync(ct);

                // Extract suggestions
                List<SuggestedAction>? suggestions = null;
                if (result.TryGetProperty("suggestedActions", out var suggestionsProp))
                {
                    suggestions = JsonSerializer.Deserialize<List<SuggestedAction>>(suggestionsProp.GetRawText(), JsonOptions);
                }

                List<string>? recommendedTools = null;
                if (result.TryGetProperty("recommendedTools", out var toolsProp))
                {
                    recommendedTools = JsonSerializer.Deserialize<List<string>>(toolsProp.GetRawText(), JsonOptions);
                }

                return new ChatResponse
                {
                    MessageId = assistantMessage.Id,
                    Content = aiContent,
                    Success = true,
                    SuggestedActions = suggestions,
                    RecommendedTools = recommendedTools,
                    Metadata = metadata
                };
            }
            else
            {
                // No result received
                _logger.LogError("MCP Server returned no result");

                userMessage.Status = MessageStatus.Error;
                userMessage.Metadata = new Dictionary<string, object>
                {
                    ["error"] = "No response received from AI service",
                    ["errorCategory"] = "NoResponse"
                };
                await _dbContext.SaveChangesAsync(ct);

                return new ChatResponse
                {
                    MessageId = messageId,
                    Content = "",
                    Success = false,
                    Error = "No response received from AI service"
                };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ChatWorkspaceException) { throw; }
        catch (TaskCanceledException)
        {
            _logger.LogError("MCP Server request timed out for message {MessageId}", messageId);

            await SetMessageError(messageId, "The request timed out — try a shorter question", "Timeout", workspace, ct);

            return new ChatResponse
            {
                MessageId = messageId,
                Content = "",
                Success = false,
                Error = "The request timed out — try a shorter question"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "MCP Server connection failed for message {MessageId}", messageId);

            await SetMessageError(messageId, "The AI service is temporarily unavailable", "ServiceUnavailable", workspace, ct);

            return new ChatResponse
            {
                MessageId = messageId,
                Content = "",
                Success = false,
                Error = "The AI service is temporarily unavailable"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing message {MessageId}", messageId);

            await SetMessageError(messageId, "The request could not be processed", "ProcessingError", workspace, ct);

            return new ChatResponse
            {
                MessageId = messageId,
                Content = "",
                Success = false,
                Error = "The request could not be processed"
            };
        }
    }

    /// <inheritdoc/>
    public async Task<List<ChatMessage>> GetMessagesAsync(string conversationId, int skip = 0, int take = 100)
    {
        var workspace = await _workspaces.ResolveAsync(RequestAborted);
        await RequireConversationAsync(conversationId, workspace, RequestAborted);
        return await _dbContext.Messages
            .Where(m => m.ConversationId == conversationId && m.Conversation!.OwnerKey == workspace.OwnerKey)
            .Include(m => m.Attachments)
            .OrderBy(m => m.Timestamp)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(RequestAborted);
    }

    /// <inheritdoc/>
    public async Task<List<ChatMessage>> GetConversationHistoryAsync(string conversationId, int maxMessages = 20)
    {
        var workspace = await _workspaces.ResolveAsync(RequestAborted);
        await RequireConversationAsync(conversationId, workspace, RequestAborted);
        return await _dbContext.Messages
            .Where(m => m.ConversationId == conversationId && m.Conversation!.OwnerKey == workspace.OwnerKey)
            .OrderByDescending(m => m.Timestamp)
            .Take(Math.Clamp(maxMessages, 1, 100))
            .OrderBy(m => m.Timestamp)
            .ToListAsync(RequestAborted);
    }

    // ─── Conversations (US2) ─────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Conversation> CreateConversationAsync(CreateConversationRequest request)
    {
        var workspace = await _workspaces.ResolveAsync(RequestAborted);
        var conversation = new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            Title = string.IsNullOrWhiteSpace(request.Title) ? "New Conversation" : request.Title,
            UserId = workspace.ActorId,
            OwnerKey = workspace.OwnerKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Conversations.Add(conversation);
        await _dbContext.SaveChangesAsync(RequestAborted);

        _logger.LogInformation("Created conversation {ConversationId}", conversation.Id);
        return conversation;
    }

    /// <inheritdoc/>
    public async Task<List<Conversation>> GetConversationsAsync(string userId = "default-user", int skip = 0, int take = 50)
    {
        var workspace = await _workspaces.ResolveAsync(RequestAborted);
        var conversations = await _dbContext.Conversations
            .Where(c => c.OwnerKey == workspace.OwnerKey && !c.IsArchived)
            .OrderByDescending(c => c.UpdatedAt)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(RequestAborted);
        foreach (var conversation in conversations)
            await _workspaces.RequireSystemAsync(workspace, conversation.SystemId, RequestAborted);
        return conversations;
    }

    /// <inheritdoc/>
    public async Task<Conversation?> GetConversationAsync(string conversationId)
    {
        var workspace = await _workspaces.ResolveAsync(RequestAborted);
        var owned = await _dbContext.Conversations.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == conversationId && c.OwnerKey == workspace.OwnerKey, RequestAborted);
        if (owned is null) return null;
        await _workspaces.RequireSystemAsync(workspace, owned.SystemId, RequestAborted);
        return await _dbContext.Conversations.AsNoTracking()
            .Include(c => c.Messages!.OrderBy(m => m.Timestamp))
                .ThenInclude(m => m.Attachments)
            .Include(c => c.Context)
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.OwnerKey == workspace.OwnerKey, RequestAborted);
    }

    /// <inheritdoc/>
    public async Task<List<Conversation>> SearchConversationsAsync(string query, string userId = "default-user")
    {
        var workspace = await _workspaces.ResolveAsync(RequestAborted);
        var lowerQuery = query.ToLower();

        var conversations = await _dbContext.Conversations
            .Where(c => c.OwnerKey == workspace.OwnerKey && !c.IsArchived)
            .Where(c => c.Title.ToLower().Contains(lowerQuery) ||
                        c.Messages!.Any(m => m.Content.ToLower().Contains(lowerQuery)))
            .OrderByDescending(c => c.UpdatedAt)
            .Take(20)
            .ToListAsync(RequestAborted);
        foreach (var conversation in conversations)
            await _workspaces.RequireSystemAsync(workspace, conversation.SystemId, RequestAborted);
        return conversations;
    }

    /// <inheritdoc/>
    public async Task DeleteConversationAsync(string conversationId)
    {
        var workspace = await _workspaces.ResolveAsync(RequestAborted);
        await RequireConversationAsync(conversationId, workspace, RequestAborted);
        var conversation = await _dbContext.Conversations
            .Include(c => c.Messages!)
                .ThenInclude(m => m.Attachments)
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.OwnerKey == workspace.OwnerKey, RequestAborted);

        if (conversation == null)
            throw new InvalidOperationException($"Conversation {conversationId} not found");

        // Delete attachment files from disk
        if (conversation.Messages != null)
        {
            foreach (var message in conversation.Messages)
            {
                if (message.Attachments != null)
                {
                    foreach (var attachment in message.Attachments)
                    {
                        RequestAborted.ThrowIfCancellationRequested();
                        try
                        {
                            if (File.Exists(attachment.StoragePath))
                            {
                                File.Delete(attachment.StoragePath);
                                _logger.LogInformation("Deleted attachment file {Path}", attachment.StoragePath);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete attachment file {Path}", attachment.StoragePath);
                        }
                    }
                }
            }
        }

        _dbContext.Conversations.Remove(conversation);
        await _dbContext.SaveChangesAsync(RequestAborted);

        _logger.LogInformation("Deleted conversation {ConversationId}", conversationId);
    }

    // ─── Context (US2) ───────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ConversationContext> CreateOrUpdateContextAsync(ConversationContext context)
    {
        var workspace = await _workspaces.ResolveAsync(RequestAborted);
        var owned = await RequireConversationAsync(context.ConversationId, workspace, RequestAborted);
        await BindSystemAsync(owned, context.Data, workspace, RequestAborted);
        context = new ConversationContext
        {
            ConversationId = owned.Id, Type = context.Type, Title = context.Title, Summary = context.Summary,
            Data = BindContext(context.Data, workspace, owned.SystemId), Tags = context.Tags.ToList()
        };
        var existing = await _dbContext.ConversationContexts
            .FirstOrDefaultAsync(c => c.ConversationId == owned.Id && c.Conversation!.OwnerKey == workspace.OwnerKey, RequestAborted);

        if (existing != null)
        {
            existing.Type = context.Type;
            existing.Title = context.Title;
            existing.Summary = context.Summary;
            existing.Data = context.Data;
            existing.Tags = context.Tags;
            existing.LastAccessedAt = DateTime.UtcNow;
        }
        else
        {
            context.Id = Guid.NewGuid().ToString();
            context.CreatedAt = DateTime.UtcNow;
            context.LastAccessedAt = DateTime.UtcNow;
            _dbContext.ConversationContexts.Add(context);
        }

        await _dbContext.SaveChangesAsync(RequestAborted);
        return existing ?? context;
    }

    // ─── Attachments (US5) ───────────────────────────────────────

    /// <inheritdoc/>
    public async Task<MessageAttachment> SaveAttachmentAsync(string messageId, string fileName, string contentType, Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name is required", nameof(fileName));

        var workspace = await _workspaces.ResolveAsync(RequestAborted);
        var message = await _dbContext.Messages.AsNoTracking().SingleOrDefaultAsync(
            m => m.Id == messageId && m.Conversation!.OwnerKey == workspace.OwnerKey, RequestAborted)
            ?? throw new ChatWorkspaceException(404, "MESSAGE_NOT_FOUND", "Message not found.");
        await RequireConversationAsync(message.ConversationId, workspace, RequestAborted);
        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        Directory.CreateDirectory(uploadsDir);

        var extension = Path.GetExtension(fileName);
        var storageName = $"{Guid.NewGuid()}{extension}";
        var storagePath = Path.Combine(uploadsDir, storageName);

        // Validate resolved path against uploads base directory (FR-012)
        var pathValidation = _pathSanitizer.ValidatePathWithinBase(storagePath, uploadsDir);
        if (!pathValidation.IsValid)
        {
            _logger.LogWarning("Path traversal blocked for attachment {FileName}: {Reason}", fileName, pathValidation.Reason);
            throw new InvalidOperationException($"PATH_TRAVERSAL_BLOCKED: {pathValidation.Reason}");
        }

        await using (var fileStream = new FileStream(pathValidation.CanonicalPath!, FileMode.Create))
        {
            await stream.CopyToAsync(fileStream, RequestAborted);
        }

        var fileInfo = new FileInfo(storagePath);

        var attachment = new MessageAttachment
        {
            Id = Guid.NewGuid().ToString(),
            MessageId = messageId,
            FileName = fileName,
            ContentType = contentType,
            Size = fileInfo.Length,
            StoragePath = storagePath,
            Type = GetAttachmentTypeFromContentType(contentType),
            UploadedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>()
        };

        _dbContext.Attachments.Add(attachment);
        await _dbContext.SaveChangesAsync(RequestAborted);

        _logger.LogInformation("Saved attachment {AttachmentId} for message {MessageId}: {FileName} ({Size} bytes)", attachment.Id, messageId, fileName, attachment.Size);
        return attachment;
    }

    /// <inheritdoc/>
    public AttachmentType GetAttachmentTypeFromContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return AttachmentType.Document;

        var lower = contentType.ToLower();

        if (lower.StartsWith("image/"))
            return AttachmentType.Image;
        if (lower.Contains("code") || lower.Contains("javascript") || lower.Contains("python"))
            return AttachmentType.Code;
        if (lower.Contains("json") || lower.Contains("yaml") || lower.Contains("xml"))
            return AttachmentType.Configuration;
        if (lower.Contains("log") || lower == "text/plain")
            return AttachmentType.Log;

        return AttachmentType.Document;
    }

    /// <inheritdoc/>
    public string GenerateAnalysisPrompt(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLower();

        return extension switch
        {
            ".yaml" or ".yml" => $"Please analyze this YAML configuration file ({fileName}) and provide a summary of its structure and settings.",
            ".json" => $"Please analyze this JSON file ({fileName}) and explain its structure and key data points.",
            ".xml" => $"Please analyze this XML file ({fileName}) and describe its schema and content.",
            ".cs" or ".java" or ".py" or ".ts" or ".js" => $"Please review this source code file ({fileName}) and provide a summary of its functionality, key classes/functions, and any notable patterns.",
            ".md" or ".txt" => $"Please summarize the content of this document ({fileName}).",
            ".log" => $"Please analyze this log file ({fileName}) and highlight any errors, warnings, or notable patterns.",
            ".csv" or ".tsv" => $"Please analyze this data file ({fileName}) and summarize its columns, row count, and key data insights.",
            ".pdf" or ".docx" or ".doc" => $"Please analyze the content of this document ({fileName}).",
            ".tf" or ".bicep" => $"Please analyze this infrastructure-as-code file ({fileName}) and describe the resources being defined.",
            ".dockerfile" or ".docker" => $"Please analyze this Dockerfile ({fileName}) and describe the build stages and configuration.",
            _ => $"Please analyze the attached file ({fileName}) and provide a summary of its contents."
        };
    }

    // ─── Private Helpers ─────────────────────────────────────────

    private async Task SetMessageError(string messageId, string errorMessage, string category, ChatWorkspace workspace, CancellationToken ct)
    {
        try
        {
            var message = await _dbContext.Messages.SingleOrDefaultAsync(
                m => m.Id == messageId && m.Conversation!.OwnerKey == workspace.OwnerKey, ct);
            if (message != null)
            {
                message.Status = MessageStatus.Error;
                message.Metadata = new Dictionary<string, object>
                {
                    ["error"] = errorMessage,
                    ["errorCategory"] = category
                };
                await _dbContext.SaveChangesAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update message {MessageId} error status", messageId);
        }
    }

    private static (string message, string category) CategorizeMcpError(System.Net.HttpStatusCode? statusCode, string errorContent)
    {
        return statusCode switch
        {
            System.Net.HttpStatusCode.GatewayTimeout or System.Net.HttpStatusCode.RequestTimeout =>
                ("The request timed out — try a shorter question", "Timeout"),
            System.Net.HttpStatusCode.BadGateway or System.Net.HttpStatusCode.ServiceUnavailable =>
                ("The AI service is temporarily unavailable", "ServiceUnavailable"),
            System.Net.HttpStatusCode.BadRequest =>
                ("The request is invalid", "ValidationError"),
            _ =>
                ("The request could not be processed", "ProcessingError")
        };
    }

    private static string GenerateTitle(string firstMessage)
    {
        if (string.IsNullOrWhiteSpace(firstMessage))
            return "New Conversation";

        // Use first 50 chars, truncated at word boundary
        if (firstMessage.Length <= 50)
            return firstMessage;

        var truncated = firstMessage[..50];
        var lastSpace = truncated.LastIndexOf(' ');
        return lastSpace > 20 ? truncated[..lastSpace] + "..." : truncated + "...";
    }
}

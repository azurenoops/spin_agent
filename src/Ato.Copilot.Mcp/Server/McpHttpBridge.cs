using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.RateLimiting;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Observability;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Models;
using Ato.Copilot.Mcp.Resilience;
using Ato.Copilot.Mcp.Services.Tenancy;
using Ato.Copilot.Mcp.Authorization;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Ato.Copilot.Mcp.Server;

/// <summary>
/// HTTP bridge for MCP protocol — exposes compliance tools as REST endpoints
/// </summary>
public class McpHttpBridge
{
    private readonly McpServer _mcpServer;
    private readonly IEnumerable<BaseTool> _tools;
    private readonly HttpMetrics _httpMetrics;
    private readonly OfflineModeService _offlineModeService;
    private readonly SseEventBuffer _sseEventBuffer;
    private readonly ILogger<McpHttpBridge> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly JsonSerializerOptions _sseJsonOptions;
    private static readonly DateTime ServerStartTime = DateTime.UtcNow;

    /// <summary>Initializes a new instance of the <see cref="McpHttpBridge"/> class.</summary>
    /// <param name="mcpServer">The MCP server to delegate requests to.</param>
    /// <param name="tools">All registered BaseTool instances for dynamic tool listing.</param>
    /// <param name="httpMetrics">HTTP metrics for health endpoint reporting.</param>
    /// <param name="logger">Logger instance.</param>
    public McpHttpBridge(McpServer mcpServer, IEnumerable<BaseTool> tools, HttpMetrics httpMetrics, OfflineModeService offlineModeService, SseEventBuffer sseEventBuffer, ILogger<McpHttpBridge> logger)
    {
        _mcpServer = mcpServer;
        _tools = tools;
        _httpMetrics = httpMetrics;
        _offlineModeService = offlineModeService;
        _sseEventBuffer = sseEventBuffer;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        // SSE events MUST be single-line — no indentation
        _sseJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Map all MCP HTTP endpoints
    /// </summary>
    public void MapEndpoints(WebApplication app)
    {
        // Cast to Delegate so minimal API correctly handles Task<IResult> return
        // Without the cast, the framework treats these as RequestDelegate and discards the IResult (ASP0016)

        // MCP JSON-RPC endpoint
        app.MapPost("/mcp", (Delegate)HandleMcpRequestAsync)
            .WithName("McpJsonRpc")
            .WithMetadata(new WorkspaceAuthorizedEndpoint())
            .WithTags("MCP")
            .WithDescription("MCP JSON-RPC endpoint for tool invocations")
            .RequireRateLimiting("jsonrpc");

        // Compliance chat endpoint
        app.MapPost("/mcp/chat", (Delegate)HandleChatRequestAsync)
            .WithName("McpChat")
            .WithMetadata(new WorkspaceAuthorizedEndpoint())
            .WithTags("MCP")
            .WithDescription("Process compliance requests via AI agent")
            .RequireRateLimiting("chat");

        // Streaming chat endpoint with SSE progress events
        app.MapPost("/mcp/chat/stream", (Delegate)HandleChatStreamRequestAsync)
            .WithName("McpChatStream")
            .WithMetadata(new WorkspaceAuthorizedEndpoint())
            .WithTags("MCP")
            .WithDescription("Process compliance requests with real-time progress via SSE")
            .RequireRateLimiting("stream");

        // Health endpoint
        app.MapGet("/health", (Delegate)HandleHealthAsync)
            .WithName("Health")
            .WithTags("Health")
            .WithDescription("Health check")
            .DisableRateLimiting();

        // Tools listing endpoint
        app.MapGet("/mcp/tools", (Delegate)HandleToolsListAsync)
            .WithName("McpToolsList")
            .WithTags("MCP")
            .WithDescription("List available compliance tools")
            .DisableRateLimiting();

        _logger.LogInformation("MCP HTTP endpoints mapped");
    }

    /// <summary>Handles MCP JSON-RPC requests (tools/list, tools/call).</summary>
    private async Task<IResult> HandleMcpRequestAsync(HttpContext context)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<McpRequest>(
                context.Request.Body, _jsonOptions, context.RequestAborted);

            if (request == null)
                return Results.BadRequest(new { error = "Invalid request" });

            _logger.LogInformation("HTTP MCP request: {Method}", request.Method);

            // Use reflection to call HandleRequestAsync via the server's StartAsync pipeline
            // For HTTP, we directly process through the chat endpoint or tool calls
            if (request.Method == "tools/call")
            {
                var toolCall = JsonSerializer.Deserialize<McpToolCall>(
                    JsonSerializer.Serialize(request.Params, _jsonOptions), _jsonOptions);

                if (toolCall?.Name == "compliance_chat")
                {
                    var message = toolCall.Arguments?.GetValueOrDefault("message")?.ToString() ?? "";
                    var convId = toolCall.Arguments?.GetValueOrDefault("conversation_id")?.ToString();
                    var result = await _mcpServer.ProcessChatRequestAsync(message, convId, toolCall.Arguments,
                        cancellationToken: context.RequestAborted);
                    return result.Success ? Results.Json(result, _jsonOptions) : MapFailureResult(result, _jsonOptions);
                }
                if (toolCall is null || string.IsNullOrWhiteSpace(toolCall.Name))
                    return Results.BadRequest(new { success = false, code = "INVALID_TOOL_REQUEST" });
                var toolResult = await _mcpServer.ProcessWorkspaceToolRequestAsync(
                    toolCall.Name, toolCall.Arguments, context.RequestAborted);
                return Results.Json(new McpResponse { Id = request.Id, Result = toolResult }, _jsonOptions);
            }

            // For other methods, wrap in chat
            var chatResult = await _mcpServer.ProcessChatRequestAsync(
                JsonSerializer.Serialize(request.Params, _jsonOptions), cancellationToken: context.RequestAborted);
            return chatResult.Success ? Results.Json(chatResult, _jsonOptions) : MapFailureResult(chatResult, _jsonOptions);
        }
        catch (WorkspaceException ex)
        {
            return WorkspaceFailure(ex, context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing HTTP MCP request");
            return Results.Problem("An internal error occurred.", statusCode: 500);
        }
    }

    /// <summary>Handles natural language chat requests routed to the appropriate agent.</summary>
    private async Task<IResult> HandleChatRequestAsync(HttpContext context)
    {
        try
        {
            var chatRequest = await JsonSerializer.DeserializeAsync<ChatRequest>(
                context.Request.Body, _jsonOptions, context.RequestAborted);

            if (chatRequest == null || string.IsNullOrEmpty(chatRequest.Message))
                return Results.BadRequest(new { error = "Message is required" });

            _logger.LogInformation("Chat request | ConvId: {ConvId}", chatRequest.ConversationId);

            var result = await _mcpServer.ProcessChatRequestAsync(
                chatRequest.Message,
                chatRequest.ConversationId,
                RequestContext(chatRequest),
                chatRequest.ConversationHistory?.Select(m => (m.Role, m.Content)).ToList(),
                cancellationToken: context.RequestAborted,
                action: chatRequest.Action,
                actionContext: chatRequest.ActionContext);

            // Add cache headers from response metadata (FR-017)
            if (result.Metadata.TryGetValue("cacheStatus", out var cacheStatus))
            {
                context.Response.Headers["X-Cache"] = cacheStatus?.ToString() ?? "MISS";
            }

            // #679 / #791: map Success=false → HTTP >=400 with structured error body.
            // A blank or failed response must never ship as HTTP 200.
            if (!result.Success)
                return MapFailureResult(result, _jsonOptions);

            return Results.Json(result, _jsonOptions);
        }
        catch (WorkspaceException ex)
        {
            return WorkspaceFailure(ex, context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat request");
            return Results.Problem("An internal error occurred.", statusCode: 500);
        }
    }

    /// <summary>Handles chat requests with SSE streaming for real-time progress.</summary>
    // T006 (#141) / #201: Allowed MIME types for chat file attachments — reconciled with frontend
    private static readonly HashSet<string> _allowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "text/plain",
        "text/csv",
        "application/json",
        "application/xml",
        "text/xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",  // .xlsx
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",  // .docx
    };

    private async Task HandleChatStreamRequestAsync(HttpContext context)
    {
        try
        {
            ChatRequest? chatRequest;

            // T006 (052-api-mismatch-fixes #141): detect multipart/form-data for file attachments
            if (context.Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true)
            {
                var form = await context.Request.ReadFormAsync(context.RequestAborted);

                // MIME validation
                if (form.Files.Count > 0)
                {
                    var rejected = form.Files
                        .Where(f => f.Length == 0 || !_allowedMimeTypes.Contains(f.ContentType))
                        .Select(f => f.FileName)
                        .ToList();

                    if (rejected.Count > 0)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            ok = false,
                            code = "UNSUPPORTED_ATTACHMENT_TYPE",
                            message = "One or more attachments have unsupported types or are empty.",
                            rejectedFiles = rejected
                        });
                        return;
                    }
                }

                chatRequest = new ChatRequest
                {
                    Message = form["message"].FirstOrDefault() ?? string.Empty,
                    ConversationId = form["conversationId"].FirstOrDefault(),
                    Attachments = form.Files.Count > 0 ? form.Files : null,
                    // fix(#722): deserialise context forwarded by the frontend so that
                    // system_id / systemId reaches the agent even in multipart requests.
                    Context = TryParseContextField(form["context"].FirstOrDefault()),
                    SystemId = form["systemId"].FirstOrDefault(),
                };
            }
            else
            {
                chatRequest = await JsonSerializer.DeserializeAsync<ChatRequest>(
                    context.Request.Body, _jsonOptions, context.RequestAborted);
            }

            if (chatRequest == null || string.IsNullOrEmpty(chatRequest.Message))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Message is required" });
                return;
            }

            var conversationId = chatRequest.ConversationId ?? Guid.NewGuid().ToString();
            var requestContext = RequestContext(chatRequest);
            var identity = await WorkspaceChatScope.ResolveAsync(context, requestContext,
                chatRequest.ActionContext, context.RequestAborted);
            var sessionKey = identity.StorageKey(conversationId);
            _logger.LogInformation("Streaming chat request | ConvId: {ConvId}", conversationId);

            // Set up SSE headers
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["Connection"] = "keep-alive";
            await context.Response.Body.FlushAsync(context.RequestAborted);

            // T059: Replay buffered events on reconnection (FR-040)
            var lastEventIdHeader = context.Request.Headers["Last-Event-ID"].FirstOrDefault();
            if (long.TryParse(lastEventIdHeader, out var lastEventId) && lastEventId > 0)
            {
                var replayEvents = _sseEventBuffer.GetEventsForReplay(sessionKey, lastEventId);
                foreach (var evt in replayEvents)
                {
                    await context.Response.WriteAsync($"id: {evt.Id}\ndata: {evt.Data}\n\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
                _logger.LogInformation("Replayed {Count} events from buffer | ConvId: {ConvId} | LastEventId: {LastId}",
                    replayEvents.Count, conversationId, lastEventId);
            }

            // T059: Start keepalive timer (FR-042)
            await using var progress = new ChatSseWriter(context, _sseEventBuffer, sessionKey, _sseJsonOptions);
            using var keepaliveCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            var keepaliveTask = RunKeepaliveAsync(progress, keepaliveCts.Token);

            try
            {
                // #201: Extract text content from uploaded files and inject into the message before the LLM call.
                // StreamReader works correctly for plain-text formats (csv, json, xml, txt, ckl, xccdf).
                // For binary formats (pdf, docx, xlsx) the output will be garbled UTF-8 from raw bytes;
                // this is still better than silently dropping the attachment. A proper implementation
                // would use a dedicated parser (e.g. PdfPig for PDF, DocumentFormat.OpenXml for DOCX/XLSX)
                // but that is out of scope for this fix. See GitHub Issue #201 for follow-up work.
                string messageWithAttachments = chatRequest.Message;
                if (chatRequest.Attachments != null && chatRequest.Attachments.Count > 0)
                {
                    var attachmentTexts = new List<string>();
                    foreach (var file in chatRequest.Attachments)
                    {
                        try
                        {
                            using var stream = file.OpenReadStream();
                            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                            var content = await reader.ReadToEndAsync(context.RequestAborted);
                            // Truncate to 50 KB per file to stay within token limits
                            if (content.Length > 51200)
                                content = content[..51200] + "\n[... content truncated ...]";
                            attachmentTexts.Add($"<attachment name=\"{file.FileName}\" type=\"{file.ContentType}\">\n{content}\n</attachment>");
                        }
                        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to read attachment {FileName}", file.FileName);
                        }
                    }
                    if (attachmentTexts.Count > 0)
                    {
                        messageWithAttachments = $"{chatRequest.Message}\n\n--- Attached Files ---\n{string.Join("\n\n", attachmentTexts)}";
                    }
                }

                var result = await _mcpServer.ProcessChatRequestAsync(
                    messageWithAttachments,
                    conversationId,
                    requestContext,
                    chatRequest.ConversationHistory?.Select(m => (m.Role, m.Content)).ToList(),
                    context.RequestAborted,
                    progress,
                    chatRequest.Action,
                    chatRequest.ActionContext);

                // #679 / #791: emit typed 'error' SSE event on streaming failure path.
                // A blank or failed response must never stream as a success.
                if (!result.Success)
                {
                    var errorPayload = MapFailureSsePayload(result);
                    var errorData = JsonSerializer.Serialize(errorPayload, _sseJsonOptions);
                    progress.Enqueue(errorData, "error");
                    _logger.LogWarning("Streaming chat failed | ConvId: {ConvId} | Code: {Code}",
                        conversationId, result.Errors.FirstOrDefault()?.ErrorCode ?? "UNKNOWN");
                }
                else
                {
                    // Write final result as SSE event with ID
                    var resultData = JsonSerializer.Serialize(new { type = "result", data = result }, _sseJsonOptions);
                    progress.Enqueue(resultData);
                }

            }
            finally
            {
                await keepaliveCts.CancelAsync();
                await keepaliveTask;
            }
        }
        catch (WorkspaceException ex) when (!context.Response.HasStarted)
        {
            await WorkspaceFailure(ex, context).ExecuteAsync(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("Streaming chat cancelled by client");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing streaming chat request");
            try
            {
                if (!context.Response.HasStarted)
                    context.Response.StatusCode = 500;
                var errorData = JsonSerializer.Serialize(new
                {
                    type = "error",
                    code = "PROCESSING_ERROR",
                    error = "An internal error occurred."
                }, _sseJsonOptions);
                await context.Response.WriteAsync($"data: {errorData}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
            catch (Exception writeError) when (writeError is IOException or OperationCanceledException)
            {
                _logger.LogDebug(writeError, "Unable to write chat error to disconnected client");
            }
        }
    }

    /// <summary>Sends keepalive comments at configured intervals to prevent proxy timeouts (FR-042).</summary>
    private async Task RunKeepaliveAsync(ChatSseWriter writer, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_sseEventBuffer.KeepaliveInterval, cancellationToken);
                writer.Enqueue(null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Chat keepalive stopped");
        }
    }

    /// <summary>
    /// Parses the optional JSON <c>context</c> field forwarded in multipart chat requests.
    /// Malformed context is rejected rather than silently dropping its system scope.
    /// fix(#722): multipart path previously discarded context, causing SYSTEM_REQUIRED errors.
    /// </summary>
    private static Dictionary<string, object>? TryParseContextField(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch (JsonException)
        {
            throw new WorkspaceException(400, "INVALID_SYSTEM_CONTEXT", "Context must be a JSON object.");
        }
    }

    private static Dictionary<string, object>? RequestContext(ChatRequest request)
    {
        if (request.SystemId is null) return request.Context;
        var context = request.Context is null ? new Dictionary<string, object>() : new(request.Context);
        if (context.Any(p => (p.Key.Equals("systemId", StringComparison.OrdinalIgnoreCase)
                || p.Key.Equals("system_id", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(p.Value?.ToString())
            && p.Value.ToString()?.Trim() != request.SystemId.Trim()))
            throw new WorkspaceException(400, "INVALID_SYSTEM_CONTEXT", "Conflicting system references are not allowed.");
        context["systemId"] = request.SystemId;
        return context;
    }

    private static IResult WorkspaceFailure(WorkspaceException exception, HttpContext http) =>
        Results.Json(new
        {
            success = false,
            code = exception.Code,
            reason = exception.Message,
            correlationId = http.TraceIdentifier
        }, statusCode: exception.StatusCode);

    /// <summary>
    /// Maps a failed <see cref="McpChatResponse"/> to the appropriate HTTP error result.
    /// Maps machine-readable <c>ErrorCode</c> values to HTTP status codes per #791 contract:
    /// TENANT_UNRESOLVED → 400, EMPTY_AGENT_RESPONSE/validation → 422,
    /// OFFLINE_UNAVAILABLE → 503, anything else → 500.
    /// </summary>
    private static IResult MapFailureResult(McpChatResponse result, System.Text.Json.JsonSerializerOptions jsonOptions)
    {
        var primaryError = result.Errors.FirstOrDefault();
        var errorCode = primaryError?.ErrorCode ?? "PROCESSING_ERROR";
        var reason = primaryError?.Message ?? "An error occurred.";
        var correlationId = primaryError?.CorrelationId ?? result.ConversationId;

        var statusCode = errorCode switch
        {
            "TENANT_UNRESOLVED" => 400,
            "INVALID_SYSTEM_CONTEXT" => 400,
            "INVALID_WORKSPACE_IDENTITY" => 401,
            "SYSTEM_ACCESS_DENIED" => 403,
            "WORKSPACE_ACCESS_DENIED" => 403,
            "WORKSPACE_REQUIRED" => 409,
            "SYSTEM_CONTEXT_REQUIRED" => 400,
            "SYSTEM_TARGET_REQUIRED" => 400,
            "INVALID_TOOL_TARGET" => 400,
            "WORKSPACE_RESOURCE_NOT_FOUND" => 404,
            "WORKSPACE_AUTHORIZATION_UNAVAILABLE" => 503,
            "WORKSPACE_TOOL_NOT_SUPPORTED" => 403,
            "WORKSPACE_TOOL_TARGET_MISMATCH" => 403,
            "WORKSPACE_OPERATION_NOT_AUTHORIZED" => 403,
            "WORKSPACE_CONTEXT_INVALID" => 403,
            "EMPTY_AGENT_RESPONSE" => 422,
            "PROCESSING_ERROR" => 422,
            "NO_TOOL_MATCHED" => 422,
            "OFFLINE_UNAVAILABLE" => 503,
            _ => 500
        };

        var body = new
        {
            success = false,
            code = errorCode,
            reason = reason,
            correlationId,
            errors = result.Errors
        };

        return Results.Json(body, jsonOptions, statusCode: statusCode);
    }

    /// <summary>
    /// Builds the SSE error event payload for the streaming failure path.
    /// </summary>
    private static object MapFailureSsePayload(McpChatResponse result)
    {
        var primaryError = result.Errors.FirstOrDefault();
        return new
        {
            type = "error",
            code = primaryError?.ErrorCode ?? "PROCESSING_ERROR",
            reason = primaryError?.Message ?? "An error occurred.",
            correlationId = primaryError?.CorrelationId ?? result.ConversationId,
            errors = result.Errors
        };
    }

    /// <summary>Returns server health status, capabilities, and agent health check results.</summary>
    private async Task<IResult> HandleHealthAsync(HttpContext context)
    {
        var healthCheckSw = Stopwatch.StartNew();

        // Run ASP.NET Core health checks if registered
        var healthCheckService = context.RequestServices.GetService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
        var agentHealthEntries = new List<object>();
        var overallStatus = "healthy";

        if (healthCheckService is not null)
        {
            var report = await healthCheckService.CheckHealthAsync(context.RequestAborted);
            overallStatus = report.Status switch
            {
                Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy => "healthy",
                Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded => "degraded",
                _ => "unhealthy"
            };

            foreach (var entry in report.Entries)
            {
                agentHealthEntries.Add(new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description ?? string.Empty,
                    durationMs = entry.Value.Duration.TotalMilliseconds,
                    data = entry.Value.Data
                });
            }
        }

        healthCheckSw.Stop();

        // T056: When offline, override status to Degraded (FR-038)
        if (_offlineModeService.IsOffline && overallStatus == "healthy")
            overallStatus = "degraded";

        var buildVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";

        var health = new Dictionary<string, object>
        {
            ["status"] = overallStatus,
            ["service"] = "Security Posture Intelligence Navigator MCP",
            ["version"] = buildVersion,
            ["timestamp"] = DateTime.UtcNow,
            ["uptimeSeconds"] = (DateTime.UtcNow - ServerStartTime).TotalSeconds,
            ["capabilities"] = new[] { "compliance-assessment", "nist-800-53", "fedramp", "remediation", "evidence-collection" },
            ["agents"] = agentHealthEntries,
            ["totalDurationMs"] = healthCheckSw.ElapsedMilliseconds
        };

        if (_offlineModeService.IsOffline)
        {
            health["offlineMode"] = true;
            health["availableCapabilities"] = _offlineModeService.GetAvailableCapabilities()
                .Select(c => new { name = c.CapabilityName, fallback = c.FallbackDescription }).ToArray();
            health["unavailableCapabilities"] = _offlineModeService.GetUnavailableCapabilities()
                .Select(c => new { name = c.CapabilityName, reason = c.FallbackDescription }).ToArray();
        }

        return Results.Json(health, _jsonOptions);
    }

    /// <summary>Returns the list of available compliance tools (dynamically generated from registered BaseTool instances).</summary>
    private Task<IResult> HandleToolsListAsync(HttpContext context)
    {
        var allTools = _tools
            .Select(t => new
            {
                name = t.Name,
                description = t.Description,
                inputSchema = new
                {
                    type = "object",
                    properties = t.Parameters.ToDictionary(
                        p => p.Key,
                        p => new { type = p.Value.Type, description = p.Value.Description }),
                    required = t.Parameters
                        .Where(p => p.Value.Required)
                        .Select(p => p.Key)
                        .ToArray()
                }
            })
            .OrderBy(t => t.name)
            .ToArray();

        // T048: Pagination support for /mcp/tools (FR-032)
        var pageParam = context.Request.Query["page"].FirstOrDefault();
        var pageSizeParam = context.Request.Query["pageSize"].FirstOrDefault();

        if (pageParam != null || pageSizeParam != null)
        {
            var page = Math.Max(1, int.TryParse(pageParam, out var p) ? p : 1);
            var pageSize = Math.Clamp(
                int.TryParse(pageSizeParam, out var ps) ? ps : 50,
                1, 100);

            var totalItems = allTools.Length;
            var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            var offset = (page - 1) * pageSize;
            var pagedTools = allTools.Skip(offset).Take(pageSize).ToArray();

            return Task.FromResult(Results.Json(new
            {
                tools = pagedTools,
                count = pagedTools.Length,
                pagination = new
                {
                    page,
                    pageSize,
                    totalItems,
                    totalPages,
                    hasNextPage = page < totalPages
                }
            }, _jsonOptions));
        }

        return Task.FromResult(Results.Json(new { tools = allTools, count = allTools.Length }, _jsonOptions));
    }
}

/// <summary>
/// Chat request model for HTTP endpoint
/// </summary>
public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    /// <summary>Optional system context; must agree with context/actionContext and pass server authorization.</summary>
    public string? SystemId { get; set; }
    /// <summary>Files attached via multipart/form-data. Null when request body is application/json. T006 (#141)</summary>
    public IFormFileCollection? Attachments { get; set; }
    public Dictionary<string, object>? Context { get; set; }
    public List<ChatMessage>? ConversationHistory { get; set; }

    /// <summary>
    /// Optional action identifier for drill-down and tool invocation routing.
    /// When present, the server routes to the corresponding MCP tool instead of normal agent processing.
    /// Examples: "remediate", "drillDown", "collectEvidence", "showKanban" (FR-014a, R-006).
    /// </summary>
    public string? Action { get; set; }

    /// <summary>
    /// Contextual data for the specified <see cref="Action"/>.
    /// Contains action-specific parameters (e.g., controlId for drillDown, findingId for remediate).
    /// </summary>
    public Dictionary<string, object>? ActionContext { get; set; }
}

/// <summary>
/// Chat message model for conversation history.
/// </summary>
public class ChatMessage
{
    /// <summary>The role of the message sender (user or assistant).</summary>
    public string Role { get; set; } = "user";
    /// <summary>The message content text.</summary>
    public string Content { get; set; } = string.Empty;
}

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
            .WithTags("MCP")
            .WithDescription("MCP JSON-RPC endpoint for tool invocations")
            .RequireRateLimiting("jsonrpc");

        // Compliance chat endpoint
        app.MapPost("/mcp/chat", (Delegate)HandleChatRequestAsync)
            .WithName("McpChat")
            .WithTags("MCP")
            .WithDescription("Process compliance requests via AI agent")
            .RequireRateLimiting("chat");

        // Streaming chat endpoint with SSE progress events
        app.MapPost("/mcp/chat/stream", (Delegate)HandleChatStreamRequestAsync)
            .WithName("McpChatStream")
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
                context.Request.Body, _jsonOptions);

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
                    var result = await _mcpServer.ProcessChatRequestAsync(message, convId);
                    return Results.Json(result, _jsonOptions);
                }
            }

            // For other methods, wrap in chat
            var chatResult = await _mcpServer.ProcessChatRequestAsync(
                JsonSerializer.Serialize(request.Params, _jsonOptions));
            return Results.Json(chatResult, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing HTTP MCP request");
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>Handles natural language chat requests routed to the appropriate agent.</summary>
    private async Task<IResult> HandleChatRequestAsync(HttpContext context)
    {
        try
        {
            var chatRequest = await JsonSerializer.DeserializeAsync<ChatRequest>(
                context.Request.Body, _jsonOptions);

            if (chatRequest == null || string.IsNullOrEmpty(chatRequest.Message))
                return Results.BadRequest(new { error = "Message is required" });

            _logger.LogInformation("Chat request | ConvId: {ConvId}", chatRequest.ConversationId);

            var result = await _mcpServer.ProcessChatRequestAsync(
                chatRequest.Message,
                chatRequest.ConversationId,
                chatRequest.Context,
                chatRequest.ConversationHistory?.Select(m => (m.Role, m.Content)).ToList(),
                action: chatRequest.Action,
                actionContext: chatRequest.ActionContext);

            // Add cache headers from response metadata (FR-017)
            if (result.Metadata.TryGetValue("cacheStatus", out var cacheStatus))
            {
                context.Response.Headers["X-Cache"] = cacheStatus?.ToString() ?? "MISS";
            }

            return Results.Json(result, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat request");
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>Handles chat requests with SSE streaming for real-time progress.</summary>
    // T006 (052-api-mismatch-fixes #141): Allowed MIME types for chat file attachments
    private static readonly HashSet<string> _allowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "text/plain", "text/csv",
        "application/json", "application/xml"
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
                };
            }
            else
            {
                chatRequest = await JsonSerializer.DeserializeAsync<ChatRequest>(
                    context.Request.Body, _jsonOptions);
            }

            if (chatRequest == null || string.IsNullOrEmpty(chatRequest.Message))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Message is required" });
                return;
            }

            var conversationId = chatRequest.ConversationId ?? Guid.NewGuid().ToString();
            _logger.LogInformation("Streaming chat request | ConvId: {ConvId}", conversationId);

            // Set up SSE headers
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["Connection"] = "keep-alive";
            await context.Response.Body.FlushAsync();

            // T059: Replay buffered events on reconnection (FR-040)
            var lastEventIdHeader = context.Request.Headers["Last-Event-ID"].FirstOrDefault();
            if (long.TryParse(lastEventIdHeader, out var lastEventId) && lastEventId > 0)
            {
                var replayEvents = _sseEventBuffer.GetEventsForReplay(conversationId, lastEventId);
                foreach (var evt in replayEvents)
                {
                    await context.Response.WriteAsync($"id: {evt.Id}\ndata: {evt.Data}\n\n");
                    await context.Response.Body.FlushAsync();
                }
                _logger.LogInformation("Replayed {Count} events from buffer | ConvId: {ConvId} | LastEventId: {LastId}",
                    replayEvents.Count, conversationId, lastEventId);
            }

            // T059: Start keepalive timer (FR-042)
            using var keepaliveCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            var keepaliveTask = RunKeepaliveAsync(context, keepaliveCts.Token);

            // Create progress reporter that writes SSE events with IDs (T018: typed events, FR-040: event IDs)
            var progress = new Progress<string>(async step =>
            {
                try
                {
                    string eventData;
                    if (step.StartsWith("{") && step.Contains("\"type\""))
                    {
                        eventData = step;
                    }
                    else
                    {
                        eventData = JsonSerializer.Serialize(new { type = "progress", step }, _sseJsonOptions);
                    }

                    var sseEvent = _sseEventBuffer.AddEvent(conversationId, eventData);
                    await context.Response.WriteAsync($"id: {sseEvent.Id}\ndata: {eventData}\n\n");
                    await context.Response.Body.FlushAsync();
                }
                catch { /* client disconnected */ }
            });

            var result = await _mcpServer.ProcessChatRequestAsync(
                chatRequest.Message,
                conversationId,
                chatRequest.Context,
                chatRequest.ConversationHistory?.Select(m => (m.Role, m.Content)).ToList(),
                context.RequestAborted,
                progress,
                chatRequest.Action,
                chatRequest.ActionContext);

            // Write final result as SSE event with ID
            var resultData = JsonSerializer.Serialize(new { type = "result", data = result }, _sseJsonOptions);
            var finalEvent = _sseEventBuffer.AddEvent(conversationId, resultData);
            await context.Response.WriteAsync($"id: {finalEvent.Id}\ndata: {resultData}\n\n");
            await context.Response.Body.FlushAsync();

            // Mark session complete for cleanup (FR-043)
            _sseEventBuffer.CompleteSession(conversationId);
            await keepaliveCts.CancelAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing streaming chat request");
            try
            {
                var errorData = JsonSerializer.Serialize(new { type = "error", error = ex.Message }, _sseJsonOptions);
                await context.Response.WriteAsync($"data: {errorData}\n\n");
                await context.Response.Body.FlushAsync();
            }
            catch { /* client disconnected */ }
        }
    }

    /// <summary>Sends keepalive comments at configured intervals to prevent proxy timeouts (FR-042).</summary>
    private async Task RunKeepaliveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_sseEventBuffer.KeepaliveInterval, cancellationToken);
                await context.Response.WriteAsync(": keepalive\n\n");
                await context.Response.Body.FlushAsync();
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch { /* client disconnected */ }
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
                    durationMs = entry.Value.Duration.TotalMilliseconds
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
            ["service"] = "ATO Copilot MCP",
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

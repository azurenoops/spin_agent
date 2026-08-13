using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Ato.Copilot.Mcp.Tools;
using Ato.Copilot.Mcp.Models;
using Ato.Copilot.Mcp.Prompts;
using Ato.Copilot.Agents.Compliance.Agents;
using Ato.Copilot.Agents.Configuration.Agents;
using Ato.Copilot.Agents.Configuration.Tools;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Models;
using ErrorDetail = Ato.Copilot.Mcp.Models.ErrorDetail;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Ato.Copilot.Mcp.Server;

/// <summary>
/// MCP server for the ATO Copilot - compliance-only agent.
/// Exposes compliance tools via stdio/HTTP for GitHub Copilot, Claude Desktop, etc.
/// </summary>
public class McpServer
{
    private static readonly ActivitySource ActivitySource = new("Ato.Copilot.Mcp", "1.0.0");

    private readonly ComplianceMcpTools _complianceTools;
    private readonly KnowledgeBaseMcpTools _knowledgeBaseTools;
    private readonly ComplianceAgent _complianceAgent;
    private readonly ConfigurationAgent _configurationAgent;
    private readonly ConfigurationTool _configurationTool;
    private readonly AgentOrchestrator _orchestrator;
    private readonly IEnumerable<BaseTool> _allTools;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPathSanitizationService _pathSanitizer;
    private readonly ResponseCacheService _cacheService;
    private readonly PaginationOptions _paginationOptions;
    private readonly OfflineModeService _offlineModeService;
    private readonly ILogger<McpServer> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public McpServer(
        ComplianceMcpTools complianceTools,
        KnowledgeBaseMcpTools knowledgeBaseTools,
        ComplianceAgent complianceAgent,
        ConfigurationAgent configurationAgent,
        ConfigurationTool configurationTool,
        AgentOrchestrator orchestrator,
        IEnumerable<BaseTool> allTools,
        IHttpContextAccessor httpContextAccessor,
        IPathSanitizationService pathSanitizer,
        ResponseCacheService cacheService,
        IOptions<PaginationOptions> paginationOptions,
        OfflineModeService offlineModeService,
        ILogger<McpServer> logger)
    {
        _complianceTools = complianceTools;
        _knowledgeBaseTools = knowledgeBaseTools;
        _complianceAgent = complianceAgent;
        _configurationAgent = configurationAgent;
        _configurationTool = configurationTool;
        _orchestrator = orchestrator;
        _allTools = allTools;
        _httpContextAccessor = httpContextAccessor;
        _pathSanitizer = pathSanitizer;
        _cacheService = cacheService;
        _paginationOptions = paginationOptions.Value;
        _offlineModeService = offlineModeService;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Resolves the current user ID from the ambient HTTP context.
    /// Falls back to "mcp-user" when no authenticated user is present.
    /// </summary>
    private string ResolveCurrentUserId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return "mcp-user";

        var oid = user.FindFirst("oid")?.Value;
        if (!string.IsNullOrEmpty(oid))
            return oid;

        var sub = user.FindFirst("sub")?.Value;
        return !string.IsNullOrEmpty(sub) ? sub : "mcp-user";
    }

    /// <summary>
    /// Process a chat request through the compliance agent
    /// </summary>
    public async Task<McpChatResponse> ProcessChatRequestAsync(
        string message,
        string? conversationId = null,
        Dictionary<string, object>? context = null,
        List<(string Role, string Content)>? conversationHistory = null,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null,
        string? action = null,
        Dictionary<string, object>? actionContext = null)
    {
        conversationId ??= Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        using var activity = ActivitySource.StartActivity("ProcessChatRequest", ActivityKind.Server);
        activity?.SetTag("mcp.conversation_id", conversationId);
        activity?.SetTag("mcp.user_id", ResolveCurrentUserId());

        _logger.LogInformation("Processing compliance chat | ConvId: {ConvId}", conversationId);

        try
        {
            var agentContext = new AgentConversationContext
            {
                ConversationId = conversationId,
                UserId = ResolveCurrentUserId()
            };

            if (conversationHistory != null)
            {
                foreach (var (role, content) in conversationHistory)
                    agentContext.AddMessage(content, isUser: role.Equals("user", StringComparison.OrdinalIgnoreCase));
            }

            if (context != null)
            {
                foreach (var kvp in context)
                    agentContext.WorkflowState[kvp.Key] = kvp.Value;
            }

            // T016: Action routing — when Action is present, route to tool directly
            if (!string.IsNullOrEmpty(action))
            {
                return await HandleActionRoutingAsync(
                    action, actionContext, message, conversationId, agentContext,
                    stopwatch, cancellationToken, progress);
            }

            // T052: Offline guard — AI chat requires network (FR-035)
            if (_offlineModeService.IsOffline)
            {
                stopwatch.Stop();
                var available = _offlineModeService.GetAvailableCapabilities();
                return new McpChatResponse
                {
                    Success = false,
                    Response = "This operation requires network connectivity and is unavailable in offline mode.",
                    ConversationId = conversationId,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                    Errors = new List<ErrorDetail>
                    {
                        new ErrorDetail
                        {
                            ErrorCode = "OFFLINE_UNAVAILABLE",
                            Message = "AI chat requires network connectivity.",
                            Suggestion = $"Available offline capabilities: {string.Join(", ", available.Select(c => c.CapabilityName))}"
                        }
                    }
                };
            }

            // Route to appropriate agent via confidence-scored orchestrator
            // T019: Emit typed SSE events for agent routing and thinking phases
            progress?.Report(JsonSerializer.Serialize(
                new SseThinkingEvent { Message = "Selecting agent..." }, _jsonOptions));

            // Honor client routing hint (e.g., slash command → targetAgent) when present
            BaseAgent? targetAgent = null;
            if (context != null &&
                context.TryGetValue("targetAgent", out var targetAgentHint) &&
                targetAgentHint is string agentHint &&
                !string.IsNullOrEmpty(agentHint))
            {
                targetAgent = ResolveAgentByHint(agentHint);
                if (targetAgent != null)
                {
                    _logger.LogInformation("Routing to {Agent} via client hint | ConvId: {ConvId}",
                        targetAgent.AgentName, conversationId);
                }
            }

            targetAgent ??= _orchestrator.SelectAgent(message, context) ?? _complianceAgent;

            progress?.Report(JsonSerializer.Serialize(
                new SseAgentRoutedEvent { AgentName = targetAgent.AgentName, Confidence = targetAgent.CanHandle(message) }, _jsonOptions));

            // Check cache before agent dispatch (FR-016)
            var subscriptionId = context?.TryGetValue("subscriptionId", out var subId) == true
                ? subId?.ToString() ?? "default" : "default";
            var contextJson = context != null ? JsonSerializer.Serialize(context, _jsonOptions) : "{}";
            var paramsJson = $"{message}::{contextJson}";
            var cacheStatus = _cacheService.GetCacheStatus(targetAgent.AgentName, paramsJson, subscriptionId);
            string cachedResponse;
            using (var agentActivity = ActivitySource.StartActivity("AgentDispatch", ActivityKind.Internal))
            {
                agentActivity?.SetTag("mcp.agent", targetAgent.AgentName);
                agentActivity?.SetTag("mcp.cache_status", cacheStatus);
                cachedResponse = await _cacheService.GetOrSetAsync(
                    targetAgent.AgentName, paramsJson, subscriptionId,
                    async () =>
                    {
                        var resp = await targetAgent.ProcessAsync(message, agentContext, cancellationToken, progress);
                        return JsonSerializer.Serialize(resp, _jsonOptions);
                    });
            }

            var response = JsonSerializer.Deserialize<AgentResponse>(cachedResponse, _jsonOptions)!;
            stopwatch.Stop();

            _logger.LogInformation("Routed to {Agent} | ConvId: {ConvId} | Cache: {CacheStatus}",
                targetAgent.AgentName, conversationId, cacheStatus);

            // T012: Intent type mapping
            var intentType = MapIntentType(targetAgent.AgentId);

            var chatResponse = new McpChatResponse
            {
                Success = response.Success,
                Response = response.Response,
                ConversationId = conversationId,
                AgentName = response.AgentName,
                IntentType = intentType,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                // T013: Map ToolsExecuted from AgentResponse
                ToolsExecuted = response.ToolsExecuted.Select(t => new ToolExecution
                {
                    ToolName = t.ToolName,
                    Success = t.Success,
                    ExecutionTimeMs = t.ExecutionTimeMs
                }).ToList(),
                // T014: Map enrichment fields from AgentResponse
                SuggestedActions = response.Suggestions,
                RequiresFollowUp = response.RequiresFollowUp,
                FollowUpPrompt = response.FollowUpPrompt,
                MissingFields = response.MissingFields,
                Data = response.ResponseData,
                Metadata = new Dictionary<string, object>
                {
                    ["cacheStatus"] = cacheStatus,
                }
            };

            // T047: Server-side pagination enforcement (FR-029/FR-030/FR-031)
            ApplyPagination(chatResponse, context);

            return chatResponse;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error processing compliance chat request");

            return new McpChatResponse
            {
                Success = false,
                Response = $"Error processing request: {ex.Message}",
                ConversationId = conversationId,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Errors = new List<ErrorDetail>
                {
                    new ErrorDetail
                    {
                        ErrorCode = "PROCESSING_ERROR",
                        Message = ex.Message,
                        Suggestion = "Please try again or rephrase your request."
                    }
                }
            };
        }
    }

    /// <summary>
    /// Applies server-side pagination enforcement to collection data in the response.
    /// If response.Data contains a collection exceeding the configured page size,
    /// slices it to the requested page and populates PaginationInfo metadata.
    /// </summary>
    private void ApplyPagination(McpChatResponse response, Dictionary<string, object>? context)
    {
        if (response.Data == null) return;

        // Find the first collection in Data
        string? collectionKey = null;
        IList? collection = null;
        foreach (var kvp in response.Data)
        {
            if (kvp.Value is IList list && list.Count > _paginationOptions.DefaultPageSize)
            {
                collectionKey = kvp.Key;
                collection = list;
                break;
            }
            if (kvp.Value is JsonElement je && je.ValueKind == JsonValueKind.Array && je.GetArrayLength() > _paginationOptions.DefaultPageSize)
            {
                collectionKey = kvp.Key;
                // Materialize JsonElement array to list of objects
                var items = new List<object>();
                foreach (var item in je.EnumerateArray())
                    items.Add(item);
                collection = items;
                break;
            }
        }

        if (collectionKey == null || collection == null) return;

        // Parse page/pageSize from context
        var page = 1;
        var requestedPageSize = _paginationOptions.DefaultPageSize;

        if (context != null)
        {
            if (context.TryGetValue("page", out var pageObj))
                int.TryParse(pageObj?.ToString(), out page);
            if (context.TryGetValue("pageSize", out var pageSizeObj))
                int.TryParse(pageSizeObj?.ToString(), out requestedPageSize);
        }

        page = Math.Max(1, page);
        var clamped = requestedPageSize > _paginationOptions.MaxPageSize;
        var pageSize = Math.Clamp(requestedPageSize, 1, _paginationOptions.MaxPageSize);
        var totalItems = collection.Count;
        var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
        var offset = (page - 1) * pageSize;

        // Slice the collection
        var paged = new List<object>();
        for (var i = offset; i < Math.Min(offset + pageSize, totalItems); i++)
            paged.Add(collection[i]!);

        response.Data[collectionKey] = paged;

        // Add PaginationInfo to metadata
        var hasNextPage = page < totalPages;
        response.Metadata["pagination"] = new PaginationInfo
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            HasNextPage = hasNextPage,
            NextPageToken = hasNextPage
                ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"offset:{offset + pageSize}"))
                : null
        };

        if (clamped)
            response.Metadata["pageSizeClamped"] = true;
    }

    /// <summary>
    /// Detects PagedResult&lt;T&gt;-shaped data in the response and maps it to PaginationInfo (T049/FR-033).
    /// Returns true if a PagedResult pattern was found and mapped.
    /// </summary>
    private bool ApplyPagedResultPagination(McpChatResponse response)
    {
        if (response.Data == null) return false;

        // Look for PagedResult<T> shape: an entry with items, totalCount, page, pageSize, hasMore
        foreach (var kvp in response.Data)
        {
            if (kvp.Value is not JsonElement je || je.ValueKind != JsonValueKind.Object)
                continue;

            if (!je.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array)
                continue;
            if (!je.TryGetProperty("totalCount", out var totalCountEl) || !totalCountEl.TryGetInt32(out var totalCount))
                continue;
            if (!je.TryGetProperty("page", out var pageEl) || !pageEl.TryGetInt32(out var page))
                continue;
            if (!je.TryGetProperty("pageSize", out var pageSizeEl) || !pageSizeEl.TryGetInt32(out var pageSize))
                continue;

            var hasMore = je.TryGetProperty("hasMore", out var hasMoreEl) && hasMoreEl.GetBoolean();
            var totalPages = pageSize > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0;

            // Replace the PagedResult entry with just the items
            var items = new List<object>();
            foreach (var item in itemsEl.EnumerateArray())
                items.Add(item);
            response.Data[kvp.Key] = items;

            // Map to PaginationInfo with opaque cursor token
            response.Metadata ??= new Dictionary<string, object>();
            var nextOffset = page * pageSize;
            response.Metadata["pagination"] = new PaginationInfo
            {
                Page = page,
                PageSize = pageSize,
                TotalItems = totalCount,
                TotalPages = totalPages,
                HasNextPage = hasMore,
                NextPageToken = hasMore
                    ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"offset:{nextOffset}"))
                    : null
            };
            return true;
        }

        return false;
    }

    /// <summary>
    /// Maps an agent's ID to a client-facing intent type string (T012, FR-001, R-002).
    /// </summary>
    private static readonly Dictionary<string, string> IntentTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["compliance-agent"] = "compliance",
        ["knowledgebase-agent"] = "knowledgebase",
        ["configuration-agent"] = "configuration"
    };

    private static string MapIntentType(string agentId)
        => IntentTypeMap.TryGetValue(agentId, out var intentType) ? intentType : "general";

    /// <summary>
    /// Resolves an agent by client routing hint (e.g., "ComplianceAgent", "ConfigurationAgent").
    /// Matches against AgentId or AgentName, case-insensitive.
    /// </summary>
    private BaseAgent? ResolveAgentByHint(string hint)
    {
        // Try exact match on AgentId first (e.g., "compliance-agent")
        if (string.Equals(hint, _complianceAgent.AgentId, StringComparison.OrdinalIgnoreCase))
            return _complianceAgent;
        if (string.Equals(hint, _configurationAgent.AgentId, StringComparison.OrdinalIgnoreCase))
            return _configurationAgent;

        // Try class-name style match (e.g., "ComplianceAgent", "ConfigurationAgent")
        if (hint.Contains("compliance", StringComparison.OrdinalIgnoreCase))
            return _complianceAgent;
        if (hint.Contains("configuration", StringComparison.OrdinalIgnoreCase) ||
            hint.Contains("config", StringComparison.OrdinalIgnoreCase))
            return _configurationAgent;
        if (hint.Contains("knowledge", StringComparison.OrdinalIgnoreCase))
        {
            // KnowledgeBase agent may be registered via orchestrator
            return _orchestrator.SelectAgent("knowledge base") ?? _complianceAgent;
        }

        return null;
    }

    /// <summary>
    /// Routes an action request to the corresponding MCP tool (T016, FR-014a, R-006).
    /// </summary>
    private static readonly Dictionary<string, string> ActionToToolMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["remediate"] = "compliance_remediate",
        ["drillDown"] = "kb_search_nist_controls",
        ["collectEvidence"] = "compliance_collect_evidence",
        ["acknowledgeAlert"] = "compliance_monitoring",
        ["dismissAlert"] = "compliance_monitoring",
        ["escalateAlert"] = "compliance_monitoring",
        ["updateFindingStatus"] = "compliance_status",
        ["showKanban"] = "kanban_board_show",
        ["moveKanbanTask"] = "kanban_move_task",
        ["checkPimStatus"] = "pim_list_active",
        ["activatePim"] = "pim_activate_role",
        ["listEligiblePimRoles"] = "pim_list_eligible"
    };

    private async Task<McpChatResponse> HandleActionRoutingAsync(
        string action,
        Dictionary<string, object>? actionContext,
        string message,
        string conversationId,
        AgentConversationContext agentContext,
        Stopwatch stopwatch,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        if (!ActionToToolMap.TryGetValue(action, out var toolName))
        {
            stopwatch.Stop();
            return new McpChatResponse
            {
                Success = false,
                Response = $"Unknown action: {action}",
                ConversationId = conversationId,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                Errors = new List<ErrorDetail>
                {
                    new ErrorDetail
                    {
                        ErrorCode = "UNKNOWN_ACTION",
                        Message = $"The action '{action}' is not recognized.",
                        Suggestion = $"Available actions: {string.Join(", ", ActionToToolMap.Keys)}"
                    }
                }
            };
        }

        progress?.Report($"Executing action: {action}...");
        _logger.LogInformation("Action routing: {Action} → {Tool} | ConvId: {ConvId}",
            action, toolName, conversationId);

        // Build tool arguments from actionContext
        var toolArgs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (actionContext != null)
        {
            foreach (var kvp in actionContext)
                toolArgs[kvp.Key] = kvp.Value;
        }

        // Validate any file path parameters against base directory (FR-014)
        var pathKeys = new[] { "filePath", "path", "file", "outputPath", "inputPath" };
        var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
        foreach (var key in pathKeys)
        {
            if (toolArgs.TryGetValue(key, out var pathValue) && pathValue is string pathStr && !string.IsNullOrEmpty(pathStr))
            {
                var validation = _pathSanitizer.ValidatePathWithinBase(pathStr, baseDir);
                if (!validation.IsValid)
                {
                    _logger.LogWarning("Path traversal blocked in tool {Tool} param {Param}: {Reason}",
                        toolName, key, validation.Reason);
                    stopwatch.Stop();
                    return new McpChatResponse
                    {
                        Success = false,
                        Response = $"Invalid file path in parameter '{key}'.",
                        ConversationId = conversationId,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                        Errors = new List<ErrorDetail>
                        {
                            new ErrorDetail
                            {
                                ErrorCode = "PATH_TRAVERSAL_BLOCKED",
                                Message = $"Path validation failed for parameter '{key}'.",
                                Suggestion = "Provide a valid file path within the allowed directory."
                            }
                        }
                    };
                }
                toolArgs[key] = validation.CanonicalPath!;
            }
        }

        // Add the message as a fallback argument
        if (!string.IsNullOrEmpty(message))
            toolArgs.TryAdd("message", message);

        // Route to compliance agent to execute the tool
        AgentResponse response;
        using (var toolActivity = ActivitySource.StartActivity($"ToolExecution:{toolName}", ActivityKind.Internal))
        {
            toolActivity?.SetTag("mcp.tool", toolName);
            toolActivity?.SetTag("mcp.action", action);
            response = await _complianceAgent.ProcessAsync(
                $"Execute tool '{toolName}' with context: {JsonSerializer.Serialize(toolArgs, _jsonOptions)}",
                agentContext, cancellationToken, progress);
        }

        stopwatch.Stop();

        var chatResponse = new McpChatResponse
        {
            Success = response.Success,
            Response = response.Response,
            ConversationId = conversationId,
            AgentName = response.AgentName,
            IntentType = "compliance",
            ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
            ToolsExecuted = response.ToolsExecuted.Select(t => new ToolExecution
            {
                ToolName = t.ToolName,
                Success = t.Success,
                ExecutionTimeMs = t.ExecutionTimeMs
            }).ToList(),
            SuggestedActions = response.Suggestions,
            RequiresFollowUp = response.RequiresFollowUp,
            FollowUpPrompt = response.FollowUpPrompt,
            MissingFields = response.MissingFields,
            Data = response.ResponseData,
            Metadata = new Dictionary<string, object>()
        };

        // T049: Cursor-based pagination — detect PagedResult<T> shaped data (FR-033)
        if (!ApplyPagedResultPagination(chatResponse))
        {
            // Fall back to generic collection pagination
            ApplyPagination(chatResponse, actionContext);
        }

        return chatResponse;
    }

    /// <summary>
    /// Start the MCP server in stdio mode
    /// </summary>
    public async Task StartAsync()
    {
        _logger.LogInformation("Starting ATO Copilot MCP Server (compliance-only)");

        try
        {
            using var reader = new StreamReader(Console.OpenStandardInput());
            using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                try
                {
                    var request = JsonSerializer.Deserialize<McpRequest>(line, _jsonOptions);
                    if (request != null)
                    {
                        var response = await HandleRequestAsync(request);
                        var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                        await writer.WriteLineAsync(responseJson);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Invalid JSON received");
                    var errorResponse = CreateErrorResponse(0, -32700, "Parse error");
                    await writer.WriteLineAsync(JsonSerializer.Serialize(errorResponse, _jsonOptions));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing request");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in MCP server");
            throw;
        }
    }

    private async Task<McpResponse> HandleRequestAsync(McpRequest request)
    {
        try
        {
            return request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "tools/list" => HandleToolsList(request),
                "tools/call" => await HandleToolCallAsync(request),
                "prompts/list" => HandlePromptsList(request),
                "prompts/get" => HandlePromptsGet(request),
                "ping" => HandlePing(request),
                _ => CreateErrorResponse(request.Id, -32601, $"Method not found: {request.Method}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request {Method}", request.Method);
            return CreateErrorResponse(request.Id, -32603, "Internal error", ex.Message);
        }
    }

    private McpResponse HandleInitialize(McpRequest request)
    {
        _logger.LogInformation("Client initialized MCP connection");

        return new McpResponse
        {
            Id = request.Id,
            Result = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new { listChanged = false },
                    prompts = new { listChanged = false }
                },
                serverInfo = new
                {
                    name = "ATO Copilot",
                    version = "1.0.0"
                }
            }
        };
    }

    private McpResponse HandleToolsList(McpRequest request)
    {
        // Dynamically generate tool list from all registered BaseTool instances
        var tools = _allTools
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => CreateTool(
                t.Name,
                t.Description,
                new
                {
                    type = "object",
                    properties = t.Parameters.ToDictionary(
                        p => p.Key,
                        p => new { type = p.Value.Type, description = p.Value.Description }),
                    required = t.Parameters
                        .Where(p => p.Value.Required)
                        .Select(p => p.Key)
                        .ToArray()
                }))
            .ToList();

        return new McpResponse { Id = request.Id, Result = new { tools } };
    }

    private async Task<McpResponse> HandleToolCallAsync(McpRequest request)
    {
        try
        {
            var toolCall = JsonSerializer.Deserialize<McpToolCall>(
                JsonSerializer.Serialize(request.Params, _jsonOptions), _jsonOptions);

            if (toolCall == null)
                return CreateErrorResponse(request.Id, -32602, "Invalid tool call parameters");

            _logger.LogInformation("Executing tool: {ToolName}", toolCall.Name);
            var args = toolCall.Arguments ?? new Dictionary<string, object>();
            var result = await ExecuteToolAsync(toolCall.Name, args);

            return new McpResponse { Id = request.Id, Result = result };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool call");
            return CreateErrorResponse(request.Id, -32603, "Tool execution failed", ex.Message);
        }
    }

    private async Task<McpToolResult> ExecuteToolAsync(string toolName, Dictionary<string, object> args)
    {
        try
        {
            string result = toolName switch
            {
                "compliance_assess" => await _complianceTools.RunComplianceAssessmentAsync(
                    GetArg<string>(args, "subscription_id"),
                    GetArg<string>(args, "framework"),
                    GetArg<string>(args, "control_families"),
                    GetArg<string>(args, "resource_types"),
                    GetArg<string>(args, "scan_type"),
                    GetArg<bool?>(args, "include_passed") ?? false),

                "compliance_get_control_family" => await _complianceTools.GetControlFamilyInfoAsync(
                    GetArg<string>(args, "family") ?? "",
                    GetArg<bool?>(args, "include_controls") ?? true),

                "compliance_generate_document" => await _complianceTools.GenerateComplianceDocumentAsync(
                    GetArg<string>(args, "document_type") ?? "ssp",
                    GetArg<string>(args, "subscription_id"),
                    GetArg<string>(args, "framework"),
                    GetArg<string>(args, "system_name")),

                "compliance_collect_evidence" => await _complianceTools.CollectComplianceEvidenceAsync(
                    GetArg<string>(args, "control_id") ?? "",
                    GetArg<string>(args, "subscription_id"),
                    GetArg<string>(args, "resource_group")),

                "compliance_remediate" => await _complianceTools.RemediateComplianceFindingAsync(
                    GetArg<string>(args, "finding_id") ?? "",
                    GetArg<bool?>(args, "apply_remediation") ?? false,
                    GetArg<bool?>(args, "dry_run") ?? true),

                "compliance_validate_remediation" => await _complianceTools.ValidateRemediationAsync(
                    GetArg<string>(args, "finding_id") ?? "",
                    GetArg<string>(args, "execution_id"),
                    GetArg<string>(args, "subscription_id")),

                "compliance_generate_plan" => await _complianceTools.GenerateRemediationPlanAsync(
                    GetArg<string>(args, "subscription_id"),
                    GetArg<string>(args, "resource_group")),

                "compliance_audit_log" => await _complianceTools.GetAssessmentAuditLogAsync(
                    GetArg<string>(args, "subscription_id"),
                    GetArg<int?>(args, "days") ?? 7),

                "compliance_history" => await _complianceTools.GetComplianceHistoryAsync(
                    GetArg<string>(args, "subscription_id"),
                    GetArg<int?>(args, "days") ?? 30),

                "compliance_status" => await _complianceTools.GetComplianceStatusAsync(
                    GetArg<string>(args, "subscription_id"),
                    GetArg<string>(args, "framework")),

                "compliance_monitoring" => await _complianceTools.GetComplianceMonitoringAsync(
                    GetArg<string>(args, "action") ?? "status",
                    GetArg<string>(args, "subscription_id"),
                    GetArg<int?>(args, "days") ?? 30),

                "compliance_chat" => await ExecuteChatAsync(
                    GetArg<string>(args, "message") ?? "",
                    GetArg<string>(args, "conversation_id")),

                "configuration_manage" => await ExecuteConfigurationToolAsync(args),

                "configuration_chat" => await ExecuteConfigurationChatAsync(
                    GetArg<string>(args, "message") ?? "",
                    GetArg<string>(args, "conversation_id")),

                // KnowledgeBase tools
                "kb_explain_nist_control" => await _knowledgeBaseTools.ExplainNistControlAsync(
                    GetArg<string>(args, "control_id") ?? ""),

                "kb_search_nist_controls" => await _knowledgeBaseTools.SearchNistControlsAsync(
                    GetArg<string>(args, "search_term") ?? "",
                    GetArg<string>(args, "family"),
                    GetArg<int?>(args, "max_results")),

                "kb_explain_stig" => await _knowledgeBaseTools.ExplainStigAsync(
                    GetArg<string>(args, "stig_id") ?? ""),

                "kb_search_stigs" => await _knowledgeBaseTools.SearchStigsAsync(
                    GetArg<string>(args, "search_term") ?? "",
                    GetArg<string>(args, "severity"),
                    GetArg<int?>(args, "max_results")),

                "kb_explain_rmf" => await _knowledgeBaseTools.ExplainRmfAsync(
                    GetArg<string>(args, "topic"),
                    GetArg<int?>(args, "step_number"),
                    GetArg<string>(args, "organization"),
                    GetArg<string>(args, "instruction_id")),

                "kb_explain_impact_level" => await _knowledgeBaseTools.ExplainImpactLevelAsync(
                    GetArg<string>(args, "level") ?? "compare"),

                "kb_get_fedramp_template_guidance" => await _knowledgeBaseTools.GetFedRampTemplateGuidanceAsync(
                    GetArg<string>(args, "template_type"),
                    GetArg<string>(args, "baseline")),

                // Fallback: dynamically route to any registered BaseTool by name
                _ => await ExecuteDynamicToolAsync(toolName, args)
            };

            return McpToolResult.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return McpToolResult.Error($"Error: {ex.Message}");
        }
    }

    private async Task<string> ExecuteChatAsync(string message, string? conversationId)
    {
        var result = await ProcessChatRequestAsync(message, conversationId);
        return result.Response;
    }

    /// <summary>
    /// Dynamically routes a tool call to a registered BaseTool instance by name.
    /// Used as a fallback for tools not in the hardcoded dispatch table
    /// (e.g., RMF, ConMon, eMASS, Document Template tools from Feature 015).
    /// </summary>
    private async Task<string> ExecuteDynamicToolAsync(string toolName, Dictionary<string, object> args)
    {
        var tool = _allTools.FirstOrDefault(t =>
            string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));

        if (tool == null)
            return $"Unknown tool: {toolName}";

        var nullableArgs = args.ToDictionary(
            kvp => kvp.Key,
            kvp => (object?)kvp.Value);

        return await tool.ExecuteAsync(nullableArgs);
    }

    private async Task<string> ExecuteConfigurationToolAsync(Dictionary<string, object> args)
    {
        // Convert to nullable dictionary expected by BaseTool
        var toolArgs = args.ToDictionary(
            kvp => kvp.Key,
            kvp => (object?)kvp.Value);

        return await _configurationTool.ExecuteAsync(toolArgs);
    }

    private async Task<string> ExecuteConfigurationChatAsync(string message, string? conversationId)
    {
        conversationId ??= Guid.NewGuid().ToString();

        var agentContext = new AgentConversationContext
        {
            ConversationId = conversationId,
            UserId = ResolveCurrentUserId()
        };

        var response = await _configurationAgent.ProcessAsync(message, agentContext);
        return response.Response;
    }

    private McpResponse HandlePromptsList(McpRequest request)
    {
        var prompts = PromptRegistry.GetAllPrompts().Select(p => new
        {
            name = p.Name,
            description = p.Description,
            arguments = p.Arguments.Select(a => new { name = a.Name, description = a.Description, required = a.Required }).ToList()
        }).ToList();

        return new McpResponse { Id = request.Id, Result = new { prompts } };
    }

    private McpResponse HandlePromptsGet(McpRequest request)
    {
        var paramsJson = JsonSerializer.Serialize(request.Params, _jsonOptions);
        var promptRequest = JsonSerializer.Deserialize<Dictionary<string, object>>(paramsJson, _jsonOptions);
        var promptName = promptRequest?.GetValueOrDefault("name")?.ToString();

        if (string.IsNullOrEmpty(promptName))
            return CreateErrorResponse(request.Id, -32602, "Prompt name required");

        var prompt = PromptRegistry.FindPrompt(promptName);
        if (prompt == null)
            return CreateErrorResponse(request.Id, -32602, $"Prompt not found: {promptName}");

        return new McpResponse
        {
            Id = request.Id,
            Result = new
            {
                description = prompt.Description,
                messages = new[] { new { role = "user", content = new { type = "text", text = $"Execute the {prompt.Name} prompt with the provided arguments." } } }
            }
        };
    }

    private McpResponse HandlePing(McpRequest request) =>
        new() { Id = request.Id, Result = new { status = "ok", timestamp = DateTime.UtcNow } };

    private static McpTool CreateTool(string name, string description, object inputSchema) =>
        new() { Name = name, Description = description, InputSchema = inputSchema };

    private static McpResponse CreateErrorResponse(object id, int code, string message, string? data = null) =>
        new() { Id = id, Error = new McpError { Code = code, Message = message, Data = data } };

    private static T? GetArg<T>(Dictionary<string, object> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value == null) return default;
        if (value is T typedValue) return typedValue;
        if (value is JsonElement jsonElement)
        {
            try { return JsonSerializer.Deserialize<T>(jsonElement.GetRawText()); }
            catch { return default; }
        }
        try { return (T)Convert.ChangeType(value, typeof(T)); }
        catch { return default; }
    }

    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}

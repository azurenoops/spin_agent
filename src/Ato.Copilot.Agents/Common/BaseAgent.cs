using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.AI.Agents.Persistent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Configuration;

namespace Ato.Copilot.Agents.Common;

/// <summary>
/// Base class for all agents in the ATO Copilot.
/// All agents MUST extend this class (Constitution Principle II).
/// </summary>
public abstract class BaseAgent
{
    protected readonly ILogger Logger;
    private readonly IChatClient? _chatClient;
    private protected readonly AzureAiOptions? _azureAiOptions;
    private readonly IToolRanker _toolRanker;
    private protected readonly PersistentAgentsClient? _foundryClient;
    protected string? _foundryAgentId;
    private readonly ConcurrentDictionary<string, string> _threadMap = new();

    protected BaseAgent(ILogger logger)
    {
        Logger = logger;
        _toolRanker = new TfIdfToolRanker();
    }

    /// <summary>
    /// Constructor for AI-enabled agents using the unified AzureAiOptions configuration.
    /// </summary>
    protected BaseAgent(
        ILogger logger,
        IChatClient? chatClient,
        PersistentAgentsClient? foundryClient,
        AzureAiOptions? azureAiOptions,
        IToolRanker? toolRanker = null)
        : this(logger)
    {
        _chatClient = chatClient;
        _foundryClient = foundryClient;
        _azureAiOptions = azureAiOptions;
        _toolRanker = toolRanker ?? new TfIdfToolRanker();
    }

    /// <summary>
    /// Unique identifier for this agent
    /// </summary>
    public abstract string AgentId { get; }

    /// <summary>
    /// Display name of the agent
    /// </summary>
    public abstract string AgentName { get; }

    /// <summary>
    /// Description of the agent's capabilities
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Get the system prompt for this agent
    /// </summary>
    public abstract string GetSystemPrompt();

    /// <summary>
    /// Evaluate confidence that this agent can handle the given message.
    /// Returns a score between 0.0 (cannot handle) and 1.0 (perfect match).
    /// The orchestrator routes to the agent with the highest score above the
    /// configurable minimum threshold (default: 0.3).
    /// </summary>
    /// <param name="message">The user's input message.</param>
    /// <returns>Confidence score from 0.0 to 1.0.</returns>
    public abstract double CanHandle(string message);

    /// <summary>
    /// Process a user message through this agent
    /// </summary>
    public abstract Task<AgentResponse> ProcessAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null);

    /// <summary>
    /// Registered tools for this agent
    /// </summary>
    protected List<BaseTool> Tools { get; } = new();

    /// <summary>
    /// Register a tool for use by this agent
    /// </summary>
    protected void RegisterTool(BaseTool tool)
    {
        if (tool == null) return;
        Tools.Add(tool);
        Logger?.LogDebug("Registered tool {ToolName} for agent {AgentName}", tool.Name, AgentName);
    }

    /// <summary>
    /// Dispatches AI processing based on the configured <see cref="AzureAiOptions.Provider"/>.
    /// Routes to Foundry, OpenAI, or deterministic based on provider setting.
    /// Returns null if the selected backend is unavailable or fails, allowing the caller
    /// to fall back to deterministic tool routing.
    /// </summary>
    protected async Task<AgentResponse?> TryProcessWithBackendAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        if (_azureAiOptions is not { Enabled: true })
            return await TryProcessWithAiAsync(message, context, cancellationToken, progress);

        switch (_azureAiOptions.Provider)
        {
            case AiProvider.Foundry:
                try
                {
                    var foundryResponse = await TryProcessWithFoundryAsync(message, context, cancellationToken, progress);
                    if (foundryResponse != null)
                        return foundryResponse;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex,
                        "Foundry processing threw exception for agent {AgentName}, falling back to IChatClient",
                        AgentName);
                }

                Logger.LogWarning(
                    "Foundry processing returned null for agent {AgentName}, falling back to IChatClient",
                    AgentName);

                // Fallback to IChatClient if Foundry fails
                return await TryProcessWithAiAsync(message, context, cancellationToken, progress);

            case AiProvider.OpenAi:
            default:
                return await TryProcessWithAiAsync(message, context, cancellationToken, progress);
        }
    }

    /// <summary>
    /// Attempt AI-powered processing via Azure AI Foundry Agents.
    /// Creates a thread, adds user message, creates a run, polls to completion,
    /// dispatches tool calls locally, and returns the assistant response.
    /// Returns null if Foundry is unavailable, not provisioned, or fails — signaling
    /// the caller to try the next backend in the fallback chain.
    /// </summary>
    protected virtual async Task<AgentResponse?> TryProcessWithFoundryAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        if (_foundryClient is null || _foundryAgentId is null)
            return null;

        var stopwatch = Stopwatch.StartNew();
        var toolsExecuted = new List<ToolExecutionResult>();

        try
        {
            Logger.LogInformation(
                "Foundry processing started for agent {AgentName}, conversation {ConversationId}",
                AgentName, context.ConversationId);

            // Reuse existing thread for conversation or create new one (US5)
            string threadId;
            if (_threadMap.TryGetValue(context.ConversationId, out var existingThreadId))
            {
                threadId = existingThreadId;
                Logger.LogDebug(
                    "Reusing Foundry thread {ThreadId} for conversation {ConversationId}",
                    threadId, context.ConversationId);
            }
            else
            {
                var thread = await _foundryClient.Threads.CreateThreadAsync(cancellationToken: cancellationToken);
                threadId = thread.Value.Id;
                _threadMap[context.ConversationId] = threadId;
                Logger.LogDebug(
                    "Created Foundry thread {ThreadId} for conversation {ConversationId}",
                    threadId, context.ConversationId);
            }

            // Add user message to thread
            await _foundryClient.Messages.CreateMessageAsync(
                threadId,
                MessageRole.User,
                message,
                cancellationToken: cancellationToken);

            // Create run with the provisioned agent
            var run = (await _foundryClient.Runs.CreateRunAsync(
                threadId,
                _foundryAgentId,
                cancellationToken: cancellationToken)).Value;

            Logger.LogInformation(
                "Foundry run created: threadId={ThreadId}, runId={RunId}, agent={AgentName}",
                threadId, run.Id, AgentName);

            // Poll until terminal status with timeout enforcement (FR-006)
            var maxRounds = _azureAiOptions?.RunTimeoutSeconds > 0
                ? _azureAiOptions.RunTimeoutSeconds
                : 60;
            var timeoutMs = maxRounds * 1000;
            var toolRounds = 0;
            var maxToolRounds = _azureAiOptions?.MaxToolIterations ?? 5;

            while (true)
            {
                // Timeout enforcement (T018)
                if (stopwatch.ElapsedMilliseconds > timeoutMs)
                {
                    Logger.LogWarning(
                        "Foundry run timed out after {ElapsedMs}ms for agent {AgentName}, cancelling run {RunId}",
                        stopwatch.ElapsedMilliseconds, AgentName, run.Id);

                    await _foundryClient.Runs.CancelRunAsync(threadId, run.Id, cancellationToken);
                    return null;
                }

                run = (await _foundryClient.Runs.GetRunAsync(threadId, run.Id, cancellationToken)).Value;

                if (run.Status == RunStatus.Completed)
                {
                    // Read the assistant's response — last assistant message
                    string? responseText = null;
                    await foreach (var msg in _foundryClient.Messages.GetMessagesAsync(
                        threadId, order: ListSortOrder.Descending, cancellationToken: cancellationToken))
                    {
                        if (msg.Role == MessageRole.Agent)
                        {
                            responseText = string.Join("", msg.ContentItems
                                .OfType<MessageTextContent>()
                                .Select(c => c.Text));
                            break;
                        }
                    }

                    stopwatch.Stop();
                    Logger.LogInformation(
                        "Foundry processing completed for agent {AgentName} in {ElapsedMs}ms, {ToolCount} tools executed",
                        AgentName, stopwatch.ElapsedMilliseconds, toolsExecuted.Count);

                    return new AgentResponse
                    {
                        Success = true,
                        Response = responseText ?? "I processed your request but have no additional details to share.",
                        AgentName = AgentName,
                        ToolsExecuted = toolsExecuted,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }
                else if (run.Status == RunStatus.RequiresAction)
                {
                    toolRounds++;
                    if (toolRounds > maxToolRounds)
                    {
                        Logger.LogWarning(
                            "Foundry run hit max tool rounds ({MaxRounds}) for agent {AgentName}, cancelling",
                            maxToolRounds, AgentName);

                        await _foundryClient.Runs.CancelRunAsync(threadId, run.Id, cancellationToken);

                        var lastTool = toolsExecuted.LastOrDefault(t => t.Success);
                        var summary = $"Completed {toolsExecuted.Count(t => t.Success)} of {toolsExecuted.Count} tool calls before reaching the maximum iteration limit. Last tool executed: {lastTool?.ToolName ?? "none"}.";
                        if (lastTool?.Result is { Length: > 0 })
                            summary += $" {lastTool.Result}";

                        return new AgentResponse
                        {
                            Success = true,
                            Response = summary,
                            AgentName = AgentName,
                            ToolsExecuted = toolsExecuted,
                            ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                        };
                    }

                    // Dispatch tool calls locally (R-005)
                    if (run.RequiredAction is SubmitToolOutputsAction submitAction)
                    {
                        var toolOutputs = new List<ToolOutput>();
                        var toolCallCount = submitAction.ToolCalls.Count;

                        Logger.LogDebug(
                            "Foundry run requires action: {ToolCallCount} tool calls, round {Round}, agent {AgentName}",
                            toolCallCount, toolRounds, AgentName);

                        progress?.Report($"ATO Copilot is executing {toolCallCount} tool(s) (round {toolRounds})...");

                        foreach (var requiredCall in submitAction.ToolCalls)
                        {
                            if (requiredCall is RequiredFunctionToolCall functionCall)
                            {
                                var toolStopwatch = Stopwatch.StartNew();
                                var tool = Tools.FirstOrDefault(t =>
                                    t.Name.Equals(functionCall.Name, StringComparison.OrdinalIgnoreCase));

                                if (tool is null)
                                {
                                    Logger.LogWarning(
                                        "Unknown tool {ToolName} requested by Foundry in agent {AgentName}",
                                        functionCall.Name, AgentName);

                                    toolOutputs.Add(new ToolOutput(functionCall.Id,
                                        $"Error: Tool '{functionCall.Name}' is not available. " +
                                        $"Available tools: {string.Join(", ", Tools.Select(t => t.Name))}"));

                                    toolsExecuted.Add(new ToolExecutionResult
                                    {
                                        ToolName = functionCall.Name,
                                        Success = false,
                                        Result = "Unknown tool",
                                        ExecutionTimeMs = 0
                                    });
                                    continue;
                                }

                                try
                                {
                                    // Parse arguments from JSON string
                                    var args = new Dictionary<string, object?>();
                                    if (!string.IsNullOrEmpty(functionCall.Arguments))
                                    {
                                        var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(functionCall.Arguments);
                                        if (jsonArgs != null)
                                        {
                                            foreach (var kvp in jsonArgs)
                                                args[kvp.Key] = kvp.Value;
                                        }
                                    }

                                    Logger.LogDebug(
                                        "Executing tool {ToolName} for agent {AgentName}, round {Round}",
                                        tool.Name, AgentName, toolRounds);

                                    progress?.Report($"Running {tool.Description ?? tool.Name}...");

                                    var toolResult = await tool.ExecuteAsync(args, cancellationToken);
                                    toolStopwatch.Stop();

                                    toolOutputs.Add(new ToolOutput(functionCall.Id, toolResult ?? string.Empty));

                                    toolsExecuted.Add(new ToolExecutionResult
                                    {
                                        ToolName = tool.Name,
                                        Success = true,
                                        Result = toolResult?.Length > 200
                                            ? toolResult[..200] + "..."
                                            : toolResult ?? string.Empty,
                                        ExecutionTimeMs = toolStopwatch.ElapsedMilliseconds
                                    });

                                    Logger.LogDebug(
                                        "Tool {ToolName} completed in {ElapsedMs}ms for agent {AgentName}",
                                        tool.Name, toolStopwatch.ElapsedMilliseconds, AgentName);
                                }
                                catch (Exception ex)
                                {
                                    toolStopwatch.Stop();
                                    Logger.LogError(ex,
                                        "Tool {ToolName} failed in agent {AgentName}: {Error}",
                                        tool.Name, AgentName, ex.Message);

                                    toolOutputs.Add(new ToolOutput(functionCall.Id,
                                        $"Error executing tool: {ex.Message}"));

                                    toolsExecuted.Add(new ToolExecutionResult
                                    {
                                        ToolName = tool.Name,
                                        Success = false,
                                        Result = ex.Message,
                                        ExecutionTimeMs = toolStopwatch.ElapsedMilliseconds
                                    });
                                }
                            }
                        }

                        // Submit tool outputs back to the run
                        run = (await _foundryClient.Runs.SubmitToolOutputsToRunAsync(
                            run, toolOutputs, cancellationToken)).Value;
                    }
                }
                else if (run.Status == RunStatus.Failed
                    || run.Status == RunStatus.Cancelled
                    || run.Status == RunStatus.Expired)
                {
                    // Terminal failure statuses (T024)
                    stopwatch.Stop();
                    Logger.LogError(
                        "Foundry run terminal failure: status={RunStatus}, error={LastError}, agent={AgentName}, runId={RunId}, elapsed={ElapsedMs}ms",
                        run.Status, run.LastError?.Message ?? "none", AgentName, run.Id, stopwatch.ElapsedMilliseconds);
                    return null;
                }
                else
                {
                    // Queued, InProgress — poll again after 1 second
                    progress?.Report("ATO Copilot is thinking...");
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex,
                "Foundry processing failed for agent {AgentName} after {ElapsedMs}ms: {Error}",
                AgentName, stopwatch.ElapsedMilliseconds, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Provisions (or reuses) a Foundry agent with the given name, instructions, and tool definitions.
    /// Lists existing agents by name and creates or updates as needed (FR-018 idempotency).
    /// Per research R-007.
    /// </summary>
    protected async Task ProvisionFoundryAgentAsync(CancellationToken cancellationToken = default)
    {
        if (_foundryClient is null || _azureAiOptions is not { IsFoundry: true })
            return;

        try
        {
            var agentName = AgentName;
            var instructions = GetSystemPrompt();
            var toolDefinitions = BuildFoundryToolDefinitions();

            Logger.LogInformation(
                "Provisioning Foundry agent \"{AgentName}\" with {ToolCount} tools...",
                agentName, toolDefinitions.Count);

            // List existing agents and find by name (R-007)
            PersistentAgent? existing = null;
            await foreach (var agent in _foundryClient.Administration.GetAgentsAsync(cancellationToken: cancellationToken))
            {
                if (string.Equals(agent.Name, agentName, StringComparison.OrdinalIgnoreCase))
                {
                    existing = agent;
                    break;
                }
            }

            if (existing != null)
            {
                // Update existing agent with current instructions and tools
                var updated = await _foundryClient.Administration.UpdateAgentAsync(
                    existing.Id,
                    _azureAiOptions!.DeploymentName,
                    name: agentName,
                    instructions: instructions,
                    tools: toolDefinitions,
                    cancellationToken: cancellationToken);
                _foundryAgentId = updated.Value.Id;

                Logger.LogInformation(
                    "Foundry agent updated: id={AgentId}, name={AgentName}",
                    _foundryAgentId, agentName);
            }
            else
            {
                // Create new agent
                var created = await _foundryClient.Administration.CreateAgentAsync(
                    _azureAiOptions!.DeploymentName,
                    name: agentName,
                    instructions: instructions,
                    tools: toolDefinitions,
                    cancellationToken: cancellationToken);
                _foundryAgentId = created.Value.Id;

                Logger.LogInformation(
                    "Foundry agent provisioned: id={AgentId}, name={AgentName}",
                    _foundryAgentId, agentName);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "Failed to provision Foundry agent \"{AgentName}\" — Foundry processing will be unavailable",
                AgentName);
            _foundryAgentId = null;
        }
    }

    /// <summary>
    /// Converts registered <see cref="BaseTool"/> instances to Foundry <see cref="FunctionToolDefinition"/>
    /// objects with JSON Schema parameters. Per research R-003.
    /// </summary>
    private List<ToolDefinition> BuildFoundryToolDefinitions()
    {
        var definitions = new List<ToolDefinition>();
        foreach (var tool in Tools)
        {
            try
            {
                var schema = BuildToolJsonSchema(tool);
                var functionTool = new FunctionToolDefinition(
                    name: tool.Name,
                    description: tool.Description,
                    parameters: BinaryData.FromString(schema));
                definitions.Add(functionTool);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    "Failed to build Foundry tool definition for {ToolName} — skipping",
                    tool.Name);
            }
        }
        return definitions;
    }

    /// <summary>
    /// Builds a JSON Schema string from a BaseTool's Parameters metadata.
    /// Reuses the same parameter metadata that Feature 011's ToolAIFunction uses.
    /// </summary>
    private static string BuildToolJsonSchema(BaseTool tool)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var kvp in tool.Parameters)
        {
            var param = kvp.Value;
            var prop = new Dictionary<string, object>
            {
                ["type"] = param.Type.ToLowerInvariant(),
                ["description"] = param.Description
            };
            properties[kvp.Key] = prop;

            if (param.Required)
                required.Add(kvp.Key);
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };

        return JsonSerializer.Serialize(schema);
    }

    /// <summary>
    /// Attempt AI-powered processing via Azure OpenAI. Returns null if AI is unavailable
    /// or disabled, signaling the caller to fall back to deterministic tool routing.
    /// Implements manual tool-calling loop per research decision R4.
    /// </summary>
    protected async Task<AgentResponse?> TryProcessWithAiAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        if (_chatClient is null || _azureAiOptions is not { Enabled: true })
            return null;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger.LogInformation(
                "AI processing started for agent {AgentName}, conversation {ConversationId}",
                AgentName, context.ConversationId);

            var chatMessages = BuildChatContext(message, context);
            var toolDefinitions = BuildToolDefinitions(message);

            var chatOptions = new ChatOptions
            {
                Tools = toolDefinitions,
                Temperature = (float)_azureAiOptions.Temperature,
                // #638: Cap model output tokens when configured (0 = no cap, model default applies).
                MaxOutputTokens = _azureAiOptions.MaxOutputTokens > 0
                    ? _azureAiOptions.MaxOutputTokens
                    : null
            };

            // ── BUG-5 / #693 — Proactive token-budget guard ──────────────────────
            // Estimate prompt size BEFORE sending any request. Prevents reactive
            // server-side 400 errors and enforces cost/DoS cap at the LLM chokepoint.
            chatMessages = EnforceTokenBudget(chatMessages, _azureAiOptions, AgentName, Logger);
            // ─────────────────────────────────────────────────────────────────────

            var toolsExecuted = new List<ToolExecutionResult>();
            var modelCallRecords = new List<ModelCallRecord>();
            var maxRounds = _azureAiOptions.MaxToolIterations;
            // #628 — tally tool calls the LLM requested but no registered tool matched.
            var skippedToolCallCount = 0;

            // Pre-compute system-prompt hash once (same system message every round).
            var systemPromptHash = chatMessages
                .Where(m => m.Role == ChatRole.System)
                .Select(m => string.Join("", m.Contents.OfType<TextContent>().Select(t => t.Text)))
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(t => ComputeSha256Hex(t))
                .FirstOrDefault();

            for (var round = 0; round < maxRounds; round++)
            {
                progress?.Report($"ATO Copilot is thinking (round {round + 1})...");

                // Capture user prompt hash at the start of each round (last user message).
                var userPromptHash = chatMessages
                    .Where(m => m.Role == ChatRole.User)
                    .Select(m => string.Join("", m.Contents.OfType<TextContent>().Select(t => t.Text)))
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Select(ComputeSha256Hex)
                    .LastOrDefault();

                var roundStart = Stopwatch.GetTimestamp();
                var response = await _chatClient.GetResponseAsync(
                    chatMessages, chatOptions, cancellationToken);
                var roundLatencyMs = (long)Stopwatch.GetElapsedTime(roundStart).TotalMilliseconds;

                // ── Epic 10 / #941 — capture provenance record for this LLM call ──
                var outputText = string.Join("",
                    response.Messages.SelectMany(m => m.Contents).OfType<TextContent>().Select(t => t.Text));
                var toolCallsInRound = response.Messages
                    .SelectMany(m => m.Contents).OfType<FunctionCallContent>()
                    .Select(fc => new { fc.Name, fc.CallId })
                    .ToList();

                modelCallRecords.Add(new ModelCallRecord
                {
                    CallIndex = round,
                    Provider = _azureAiOptions.Provider.ToString(),
                    ModelId = _azureAiOptions.DeploymentName,
                    ParamsJson = JsonSerializer.Serialize(new
                    {
                        temperature = _azureAiOptions.Temperature,
                        max_tool_iterations = maxRounds
                    }),
                    SystemPromptHash = systemPromptHash,
                    UserPromptHash = userPromptHash,
                    ToolCallsJson = JsonSerializer.Serialize(toolCallsInRound),
                    PromptTokens = (int?)response.Usage?.InputTokenCount,
                    CompletionTokens = (int?)response.Usage?.OutputTokenCount,
                    LatencyMs = roundLatencyMs,
                    OutputContentHash = string.IsNullOrEmpty(outputText) ? null : ComputeSha256Hex(outputText),
                    CreatedAt = DateTime.UtcNow
                });
                // ─────────────────────────────────────────────────────────────────

                // Check if the response contains tool calls
                var toolCalls = response.Messages
                    .SelectMany(m => m.Contents)
                    .OfType<FunctionCallContent>()
                    .ToList();

                if (toolCalls.Count == 0)
                {
                    // No tool calls — extract final text response
                    var textContent = response.Messages
                        .SelectMany(m => m.Contents)
                        .OfType<TextContent>()
                        .Select(t => t.Text)
                        .Where(t => !string.IsNullOrWhiteSpace(t));

                    var finalText = string.Join("\n", textContent);
                    if (string.IsNullOrWhiteSpace(finalText))
                        finalText = "I processed your request but have no additional details to share.";

                    // Add response messages to chat context for multi-turn
                    foreach (var msg in response.Messages)
                        chatMessages.Add(msg);

                    stopwatch.Stop();
                    Logger.LogInformation(
                        "AI processing completed for agent {AgentName} in {ElapsedMs}ms, {ToolCount} tools executed, {Rounds} rounds",
                        AgentName, stopwatch.ElapsedMilliseconds, toolsExecuted.Count, round + 1);

                    return new AgentResponse
                    {
                        Success = true,
                        Response = finalText,
                        AgentName = AgentName,
                        ToolsExecuted = toolsExecuted,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                        ModelCallRecords = modelCallRecords,
                        SkippedToolCallCount = skippedToolCallCount
                    };
                }

                // Add response messages (including tool call requests) to context
                foreach (var msg in response.Messages)
                    chatMessages.Add(msg);

                // Execute each tool call
                foreach (var toolCall in toolCalls)
                {
                    var toolStopwatch = Stopwatch.StartNew();
                    var tool = Tools.FirstOrDefault(t =>
                        t.Name.Equals(toolCall.Name, StringComparison.OrdinalIgnoreCase));

                    if (tool is null)
                    {
                        Logger.LogWarning(
                            "Unknown tool {ToolName} requested by LLM in agent {AgentName}",
                            toolCall.Name, AgentName);

                        progress?.Report($"Tool '{toolCall.Name}' not found, skipping...");

                        chatMessages.Add(new ChatMessage(ChatRole.Tool,
                            [new FunctionResultContent(toolCall.CallId,
                                $"Error: Tool '{toolCall.Name}' is not available. Available tools: {string.Join(", ", Tools.Select(t => t.Name))}")]));

                        toolsExecuted.Add(new ToolExecutionResult
                        {
                            ToolName = toolCall.Name,
                            Success = false,
                            Result = "Unknown tool",
                            ExecutionTimeMs = 0
                        });
                        skippedToolCallCount++; // #628 — track unresolvable LLM tool requests
                        continue;
                    }

                    try
                    {
                        // Convert tool call arguments to Dictionary<string, object?>
                        var args = new Dictionary<string, object?>();
                        if (toolCall.Arguments is not null)
                        {
                            foreach (var kvp in toolCall.Arguments)
                                args[kvp.Key] = kvp.Value;
                        }

                        Logger.LogDebug(
                            "Executing tool {ToolName} for agent {AgentName}, round {Round}",
                            tool.Name, AgentName, round + 1);

                        progress?.Report($"Running {tool.Description ?? tool.Name}...");

                        var toolResult = await tool.ExecuteAsync(args!, cancellationToken);
                        toolStopwatch.Stop();

                        chatMessages.Add(new ChatMessage(ChatRole.Tool,
                            [new FunctionResultContent(toolCall.CallId, toolResult)]));

                        toolsExecuted.Add(new ToolExecutionResult
                        {
                            ToolName = tool.Name,
                            Success = true,
                            Result = toolResult?.ToString()?.Length > 200
                                ? toolResult.ToString()![..200] + "..."
                                : toolResult?.ToString() ?? string.Empty,
                            ExecutionTimeMs = toolStopwatch.ElapsedMilliseconds
                        });

                        Logger.LogDebug(
                            "Tool {ToolName} completed in {ElapsedMs}ms for agent {AgentName}",
                            tool.Name, toolStopwatch.ElapsedMilliseconds, AgentName);
                    }
                    catch (Exception ex)
                    {
                        toolStopwatch.Stop();
                        Logger.LogError(ex,
                            "Tool {ToolName} failed in agent {AgentName}: {Error}",
                            tool.Name, AgentName, ex.Message);

                        chatMessages.Add(new ChatMessage(ChatRole.Tool,
                            [new FunctionResultContent(toolCall.CallId,
                                $"Error executing tool: {ex.Message}")]));

                        toolsExecuted.Add(new ToolExecutionResult
                        {
                            ToolName = tool.Name,
                            Success = false,
                            Result = ex.Message,
                            ExecutionTimeMs = toolStopwatch.ElapsedMilliseconds
                        });
                    }
                }
            }

            // Max rounds exceeded — return summary
            stopwatch.Stop();
            Logger.LogWarning(
                "AI processing hit max rounds ({MaxRounds}) for agent {AgentName} in {ElapsedMs}ms",
                maxRounds, AgentName, stopwatch.ElapsedMilliseconds);

            return new AgentResponse
            {
                Success = true,
                Response = $"I executed {toolsExecuted.Count} tool operations but reached the maximum processing rounds ({maxRounds}). " +
                           $"Here's what was completed: {string.Join("; ", toolsExecuted.Where(t => t.Success).Select(t => t.ToolName))}",
                AgentName = AgentName,
                ToolsExecuted = toolsExecuted,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                ModelCallRecords = modelCallRecords
            };
        }
        catch (TokenBudgetExceededException)
        {
            // ── BUG-5 / #693 ──────────────────────────────────────────────────────
            // Re-throw without catching: the token budget guard is an explicit,
            // typed enforcement policy, not an unexpected failure. Swallowing it
            // here would silently discard the guard (AGENTS.md rule #6).
            // Callers receive this as a clear, typed error to handle or surface.
            // ─────────────────────────────────────────────────────────────────────
            stopwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex,
                "AI processing failed for agent {AgentName} after {ElapsedMs}ms, falling back to deterministic routing: {Error}",
                AgentName, stopwatch.ElapsedMilliseconds, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Builds the chat message context for an LLM call: system prompt + conversation history + user message.
    /// </summary>
    private List<ChatMessage> BuildChatContext(string message, AgentConversationContext context)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, GetSystemPrompt())
        };

        // Add conversation history
        foreach (var (role, content) in context.MessageHistory)
        {
            var chatRole = role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Add(new ChatMessage(chatRole, content));
        }

        // Add current user message
        messages.Add(new ChatMessage(ChatRole.User, message));

        return messages;
    }

    /// <summary>
    /// Maximum number of tools that can be sent in a single Azure OpenAI request.
    /// The API returns HTTP 400 if this limit is exceeded (array_above_max_length).
    /// </summary>
    private const int MaxToolsPerRequest = 128;

    /// <summary>
    /// Converts registered <see cref="BaseTool"/> instances into <see cref="AITool"/> definitions
    /// for the LLM.  When the total tool count exceeds <see cref="MaxToolsPerRequest"/>, uses
    /// <see cref="IToolRanker"/> (embedding-based with TF-IDF fallback) to select the most
    /// relevant tools for the message, replacing the previous brittle keyword-table approach.
    /// Every excluded tool is recorded in a structured audit event so exclusions are never silent.
    /// </summary>
    private List<AITool> BuildToolDefinitions(string? message = null)
    {
        if (Tools.Count <= MaxToolsPerRequest)
            return Tools.Select(tool => (AITool)new ToolAIFunction(tool)).ToList();

        var selected = SelectToolsForMessageAsync(message).GetAwaiter().GetResult();
        return selected.Select(tool => (AITool)new ToolAIFunction(tool)).ToList();
    }

    /// <summary>
    /// Selects a subset of tools relevant to the user message using <see cref="IToolRanker"/>
    /// (embedding-based with TF-IDF fallback).  Emits a structured <c>TOOL_EXCLUDED</c> audit
    /// event for every tool dropped from the request so the Authorizing Official can inspect
    /// which capabilities were available for each agent turn.
    ///
    /// Audit event shape (structured log):
    ///   EventId=4001 (ToolExcluded)
    ///   Fields: ToolName, Reason, Score, MessageHash, AgentName, MaxTools, TotalTools
    /// </summary>
    private async Task<List<BaseTool>> SelectToolsForMessageAsync(string? message)
    {
        var messageHash = ComputeMessageHash(message ?? string.Empty);
        var ranked = await _toolRanker.RankAsync(
            message ?? string.Empty,
            Tools,
            CancellationToken.None);

        var selected = ranked
            .Take(MaxToolsPerRequest)
            .Select(r => r.Tool)
            .ToList();

        var selectedSet = new HashSet<string>(
            selected.Select(t => t.Name),
            StringComparer.OrdinalIgnoreCase);

        // Hard cap guard: if ranker returns fewer than MaxToolsPerRequest, we're fine.
        // Log audit event for every excluded tool.
        foreach (var r in ranked.Skip(MaxToolsPerRequest))
        {
            ToolExcludedAuditLog(Logger, r.Tool.Name, r.Reason, r.Score,
                messageHash, AgentName, MaxToolsPerRequest, Tools.Count);
        }

        // Also audit tools not returned by the ranker at all (edge case: ranker drops tools)
        foreach (var tool in Tools)
        {
            if (!selectedSet.Contains(tool.Name) &&
                ranked.All(r => !string.Equals(r.Tool.Name, tool.Name, StringComparison.OrdinalIgnoreCase)))
            {
                ToolExcludedAuditLog(Logger, tool.Name, "not-ranked:ranker-omission", 0.0,
                    messageHash, AgentName, MaxToolsPerRequest, Tools.Count);
            }
        }

        Logger.LogInformation(
            "Tool selection: {SelectedCount}/{TotalCount} tools selected for message (max {Max})",
            selected.Count, Tools.Count, MaxToolsPerRequest);

        return selected;
    }

    /// <summary>
    /// Computes a short SHA-256 prefix of the message for the audit trail.
    /// The full message text is NOT logged to prevent PII leakage.
    /// </summary>
    private static string ComputeMessageHash(string message)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(bytes)[..16];
    }

    /// <summary>
    /// Returns the full SHA-256 hex digest of <paramref name="value"/> (UTF-8 encoded).
    /// Used for provenance hashing of prompt/output content in ModelCallRecord.
    /// </summary>
    private static string ComputeSha256Hex(string value)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // EventId 4001 reserved for tool-exclusion audit events.
    // AO-visible: captured by ILogger sinks with minimum-level Warning.
    // Note: LoggerMessage.Define supports at most 6 type params (pre-.NET 9).
    // Using direct structured log to stay compatible.
    private static void ToolExcludedAuditLog(
        ILogger logger, string toolName, string reason, double score,
        string messageHash, string agentName, int maxTools, int totalTools)
    {
        logger.Log(
            LogLevel.Warning,
            new EventId(4001, "ToolExcluded"),
            "TOOL_EXCLUDED | Tool={ToolName} | Reason={Reason} | Score={Score} | " +
            "MessageHash={MessageHash} | Agent={AgentName} | Selected={MaxTools} of {TotalTools}",
            toolName, reason, score.ToString("F4"), messageHash, agentName, maxTools, totalTools);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Token-budget enforcement (BUG-5 / #693)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Estimates the number of tokens in a list of chat messages using the
    /// ~4 characters-per-token heuristic plus per-message overhead.
    /// This is intentionally conservative — over-counting is safer than under-counting
    /// because it triggers the budget guard earlier, preventing server-side 400s.
    /// </summary>
    /// <param name="messages">The messages to estimate.</param>
    /// <returns>Estimated token count (always ≥ 0).</returns>
    internal static int EstimatePromptTokens(IReadOnlyList<ChatMessage> messages)
    {
        // Heuristic: ~4 chars per token (GPT tokenizer average) + 4 token overhead per message.
        const int CharsPerToken = 4;
        const int PerMessageOverhead = 4;

        var total = 0;
        foreach (var msg in messages)
        {
            total += PerMessageOverhead;
            foreach (var content in msg.Contents)
            {
                var text = content switch
                {
                    TextContent tc => tc.Text ?? string.Empty,
                    _ => string.Empty
                };
                total += (text.Length + CharsPerToken - 1) / CharsPerToken; // ceiling division
            }
        }
        return total;
    }

    /// <summary>
    /// Enforces the configured token budget against the given message list.
    ///
    /// Behaviour depends on <see cref="Ato.Copilot.Core.Configuration.AzureAiOptions.BudgetMode"/>:
    /// <list type="bullet">
    ///   <item><see cref="Ato.Copilot.Core.Configuration.TokenBudgetMode.Reject"/> — throws
    ///     <see cref="TokenBudgetExceededException"/> when over budget; the LLM is never called.</item>
    ///   <item><see cref="Ato.Copilot.Core.Configuration.TokenBudgetMode.Truncate"/> — removes the
    ///     oldest non-system turns (preserving the system prompt and the most-recent user turn)
    ///     until the estimate fits within the cap, then returns the trimmed list.</item>
    /// </list>
    ///
    /// A warning is emitted at <see cref="AzureAiOptions.TokenAlertRatio"/> of the cap
    /// regardless of mode, giving operators advance notice before the hard limit fires.
    ///
    /// When <c>MaxInputTokens</c> is ≤ 0 the guard is disabled and the original list is
    /// returned unchanged — this is intentional for local dev / testing.
    /// </summary>
    /// <param name="messages">The message list to check. May be mutated (truncate mode).</param>
    /// <param name="options">The AI options carrying MaxInputTokens, BudgetMode, TokenAlertRatio.</param>
    /// <param name="agentName">Agent name for error messages and log events.</param>
    /// <param name="logger">Logger for per-request token usage and threshold warnings.</param>
    /// <returns>
    /// The (possibly trimmed) message list to use for the LLM call.
    /// In Reject mode, always returns the original list or throws — never trims.
    /// </returns>
    /// <exception cref="TokenBudgetExceededException">
    /// Thrown in Reject mode when the estimated prompt token count exceeds <c>MaxInputTokens</c>.
    /// </exception>
    internal static List<ChatMessage> EnforceTokenBudget(
        List<ChatMessage> messages,
        Ato.Copilot.Core.Configuration.AzureAiOptions options,
        string agentName,
        ILogger logger)
    {
        var maxInputTokens = options.MaxInputTokens;

        // Guard disabled — pass through unchanged.
        if (maxInputTokens <= 0)
            return messages;

        var estimated = EstimatePromptTokens(messages);

        // Per-request token usage log (AC #3: usage logging).
        logger.LogInformation(
            "[TokenBudget] Agent={AgentName} EstimatedTokens={Estimated} MaxInputTokens={Max} Mode={Mode}",
            agentName, estimated, maxInputTokens, options.BudgetMode);

        // Alert at configurable ratio threshold (AC #4: threshold alerts).
        var alertThreshold = (int)(maxInputTokens * options.TokenAlertRatio);
        if (estimated >= alertThreshold && estimated < maxInputTokens)
        {
            logger.LogWarning(
                "[TokenBudget] ALERT Agent={AgentName} EstimatedTokens={Estimated} is near the " +
                "MaxInputTokens cap ({Max}). Utilisation={Ratio:P0}. Consider trimming context " +
                "or raising AzureAiOptions.MaxInputTokens.",
                agentName, estimated, maxInputTokens,
                (double)estimated / maxInputTokens);
        }

        if (estimated <= maxInputTokens)
            return messages;

        // Over budget — apply mode.
        if (options.BudgetMode == Ato.Copilot.Core.Configuration.TokenBudgetMode.Reject)
        {
            logger.LogError(
                "[TokenBudget] REJECTED Agent={AgentName} EstimatedTokens={Estimated} exceeds " +
                "MaxInputTokens={Max}. Request blocked before LLM call (BUG-5/#693).",
                agentName, estimated, maxInputTokens);

            throw new TokenBudgetExceededException(estimated, maxInputTokens, agentName);
        }

        // Truncate mode: remove oldest non-system, non-latest-user turns.
        // Invariant: always preserve the system prompt (first message) and the
        //            last user turn (most recent human input).
        logger.LogWarning(
            "[TokenBudget] TRUNCATING Agent={AgentName} EstimatedTokens={Estimated} exceeds " +
            "MaxInputTokens={Max}. Removing oldest conversation turns.",
            agentName, estimated, maxInputTokens);

        var trimmed = new List<ChatMessage>(messages);

        while (trimmed.Count > 2 && EstimatePromptTokens(trimmed) > maxInputTokens)
        {
            // Index 0 = system prompt (preserved). Index 1 = oldest non-system turn (removed).
            trimmed.RemoveAt(1);
        }

        var trimmedEstimate = EstimatePromptTokens(trimmed);
        logger.LogInformation(
            "[TokenBudget] TRUNCATED Agent={AgentName} TrimmedTokens={Trimmed} MaxInputTokens={Max} " +
            "MessagesDropped={Dropped}",
            agentName, trimmedEstimate, maxInputTokens, messages.Count - trimmed.Count);

        return trimmed;
    }


}

/// <summary>
/// Wraps a <see cref="BaseTool"/> as an <see cref="AIFunction"/> with an
/// Azure OpenAI-compliant JSON schema (additionalProperties: false).
/// </summary>
internal sealed class ToolAIFunction : AIFunction
{
    private readonly BaseTool _tool;
    private readonly JsonElement _schema;

    public ToolAIFunction(BaseTool tool)
    {
        _tool = tool;
        _schema = BuildSchema();
    }

    public override string Name => _tool.Name;
    public override string Description => _tool.Description;
    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var args = new Dictionary<string, object?>();
        foreach (var kvp in arguments)
            args[kvp.Key] = kvp.Value;

        return await _tool.ExecuteAsync(args!, cancellationToken);
    }

    private JsonElement BuildSchema()
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in _tool.Parameters)
        {
            var prop = new Dictionary<string, object>();
            var typeStr = param.Value.Type?.ToLowerInvariant() switch
            {
                "integer" or "int" => "integer",
                "number" or "double" or "float" => "number",
                "boolean" or "bool" => "boolean",
                "array" => "array",
                _ => "string"
            };

            prop["type"] = typeStr;
            prop["description"] = param.Value.Description ?? param.Key;

            // Azure OpenAI requires array schemas to have an "items" definition
            if (typeStr == "array")
            {
                prop["items"] = new Dictionary<string, object> { ["type"] = "string" };
            }

            properties[param.Key] = prop;

            // Azure OpenAI strict mode requires ALL properties in `required`
            required.Add(param.Key);
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };

        return System.Text.Json.JsonSerializer.SerializeToElement(schema);
    }
}

/// <summary>
/// Response from an agent processing operation
/// </summary>
public class AgentResponse
{
    public bool Success { get; set; }
    public string Response { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public List<ToolExecutionResult> ToolsExecuted { get; set; } = new();
    public double ProcessingTimeMs { get; set; }

    /// <summary>
    /// Suggested follow-up actions the UI can present as clickable buttons.
    /// Each action has a display title and a pre-filled prompt sent when clicked.
    /// Populated by agents based on the result context (e.g., failing scan → "Generate remediation plan").
    /// </summary>
    public List<AgentSuggestedAction> Suggestions { get; set; } = new();

    /// <summary>
    /// Indicates whether the agent needs additional information from the user to complete the request.
    /// When true, <see cref="FollowUpPrompt"/> and/or <see cref="MissingFields"/> should be populated.
    /// </summary>
    public bool RequiresFollowUp { get; set; }

    /// <summary>
    /// A human-readable prompt asking the user for missing information when <see cref="RequiresFollowUp"/> is true.
    /// </summary>
    public string? FollowUpPrompt { get; set; }

    /// <summary>
    /// List of field names or descriptions that the agent needs from the user to proceed.
    /// Used by the UI to render input fields or quick-reply buttons.
    /// </summary>
    public List<string> MissingFields { get; set; } = new();

    /// <summary>
    /// Intent-specific structured data payload (e.g., assessment results, finding details, kanban board).
    /// The <c>type</c> key in the dictionary determines the Adaptive Card routing on the client side.
    /// </summary>
    public Dictionary<string, object>? ResponseData { get; set; }

    /// <summary>
    /// Provenance records for every LLM call made during this response (#941 — Epic 10).
    /// Populated by <see cref="BaseAgent"/> in <c>TryProcessWithAiAsync</c>.
    /// The Chat layer persists these via <c>IModelCallLedger</c> after receiving the response.
    /// </summary>
    public List<ModelCallRecord> ModelCallRecords { get; set; } = new();

    /// <summary>
    /// Number of tool calls the LLM requested that had no matching registered tool (#628).
    /// A non-zero value surfaces in <c>McpChatResponse.EmptyResultsCount</c> for caller observability.
    /// </summary>
    public int SkippedToolCallCount { get; set; }
}

/// <summary>
/// Result of a tool execution
/// </summary>
public class ToolExecutionResult
{
    public string ToolName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Result { get; set; } = string.Empty;
    public double ExecutionTimeMs { get; set; }
}

/// <summary>
/// Value object carrying one LLM invocation's provenance data (#941 — Epic 10).
///
/// Populated inside <see cref="BaseAgent.TryProcessWithAiAsync"/> immediately after
/// each <c>GetResponseAsync</c> call.  The Chat layer maps this to a <c>ModelCall</c>
/// EF entity and persists it via <c>IModelCallLedger</c>.
///
/// Fields mirror the <c>ModelCall</c> table exactly so the mapping is trivial.
/// Raw prompt text is NEVER stored — only SHA-256 hashes (privacy policy).
/// </summary>
public class ModelCallRecord
{
    /// <summary>0-based ordinal within the current agent turn.</summary>
    public int CallIndex { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ModelVersion { get; set; }
    /// <summary>JSON: { temperature, top_p, max_tokens, seed }</summary>
    public string ParamsJson { get; set; } = "{}";
    public string? SystemPromptHash { get; set; }
    public string? UserPromptHash { get; set; }
    /// <summary>JSON array of tool calls made in this round.</summary>
    public string ToolCallsJson { get; set; } = "[]";
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public long LatencyMs { get; set; }
    public string? OutputContentHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Conversation context passed to agents
/// </summary>
public class AgentConversationContext
{
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public List<(string Role, string Content)> MessageHistory { get; set; } = new();
    public Dictionary<string, object> WorkflowState { get; set; } = new();

    public void AddMessage(string content, bool isUser = true)
    {
        MessageHistory.Add((isUser ? "user" : "assistant", content));
    }
}

/// <summary>
/// A suggested follow-up action with a display title and a pre-filled prompt.
/// Serialized as <c>{ "title": "…", "prompt": "…" }</c> for the frontend.
/// </summary>
public class AgentSuggestedAction
{
    /// <summary>Short display label shown on the button.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Pre-filled prompt text sent when the user clicks the button.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Creates a new suggested action.</summary>
    public AgentSuggestedAction() { }

    /// <summary>Creates a new suggested action with the given title and prompt.</summary>
    public AgentSuggestedAction(string title, string prompt)
    {
        Title = title;
        Prompt = prompt;
    }

    /// <summary>Creates a suggested action where the title is also used as the prompt.</summary>
    public AgentSuggestedAction(string title) : this(title, title) { }
}

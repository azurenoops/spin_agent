using System.Diagnostics;
using System.Reflection;
using Azure.AI.Agents.Persistent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.KnowledgeBase.Configuration;
using Ato.Copilot.Agents.KnowledgeBase.Tools;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.State.Abstractions;

namespace Ato.Copilot.Agents.KnowledgeBase.Agents;

/// <summary>
/// KnowledgeBase Agent — provides always-available compliance education and reference
/// capabilities. Covers NIST 800-53, DISA STIGs, RMF, DoD Impact Levels, and FedRAMP templates.
/// Extends BaseAgent per Constitution Principle II.
/// </summary>
public class KnowledgeBaseAgent : BaseAgent
{
    private readonly KnowledgeBaseAgentOptions _options;
    private readonly IAgentStateManager _stateManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeBaseAgent"/> class.
    /// </summary>
    /// <param name="options">Configuration options for the KnowledgeBase agent.</param>
    /// <param name="stateManager">Agent state manager for cross-agent state sharing.</param>
    /// <param name="logger">Logger instance.</param>
    public KnowledgeBaseAgent(
        IOptions<KnowledgeBaseAgentOptions> options,
        IAgentStateManager stateManager,
        ExplainNistControlTool explainNistControlTool,
        SearchNistControlsTool searchNistControlsTool,
        ExplainStigTool explainStigTool,
        SearchStigsTool searchStigsTool,
        ExplainRmfTool explainRmfTool,
        ExplainImpactLevelTool explainImpactLevelTool,
        GetFedRampTemplateGuidanceTool getFedRampTemplateGuidanceTool,
        ILogger<KnowledgeBaseAgent> logger,
        IChatClient? chatClient = null,
        PersistentAgentsClient? foundryClient = null,
        IOptions<AzureAiOptions>? azureAiOptions = null)
        : base(logger, chatClient, foundryClient, azureAiOptions?.Value)
    {
        _options = options.Value;
        _stateManager = stateManager;

        // Register tools so ProcessAsync can dispatch via FindToolForQueryType
        RegisterTool(explainNistControlTool);
        RegisterTool(searchNistControlsTool);
        RegisterTool(explainStigTool);
        RegisterTool(searchStigsTool);
        RegisterTool(explainRmfTool);
        RegisterTool(explainImpactLevelTool);
        RegisterTool(getFedRampTemplateGuidanceTool);

        // Provision Foundry agent in background when enabled
        if (_azureAiOptions?.IsFoundry == true)
            _ = Task.Run(async () => await ProvisionFoundryAgentAsync());
    }

    /// <inheritdoc />
    public override string AgentId => "knowledgebase-agent";

    /// <inheritdoc />
    public override string AgentName => "KnowledgeBase Agent";

    /// <inheritdoc />
    public override string Description =>
        "Provides compliance knowledge and education — explains NIST 800-53 controls, " +
        "DISA STIGs, RMF process, DoD Impact Levels, and FedRAMP templates. " +
        "Informational-only: does not scan, assess, or modify resources.";

    /// <summary>
    /// Evaluates confidence that this agent can handle the given message.
    /// Knowledge-intent keywords ("explain", "what is", "tell me about") with domain terms score high.
    /// Domain terms alone score medium. Action keywords ("scan", "assess") score low or zero.
    /// </summary>
    public override double CanHandle(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return 0.0;

        var lower = message.ToLowerInvariant();

        // Action keywords indicate compliance agent intent — return low
        string[] actionKeywords = ["scan", "assess", "check compliance", "validate", "run ",
            "execute", "monitor ", "remediate", "fix ", "generate report", "create document",
            "inherited from", "as inherited", "inherit from", "set inheritance",
            "remove control", "add control", "tailor baseline", "generate the customer",
            "register system", "register a new", "define boundary", "categorize system",
            "select baseline", "set controls", "write narrative", "generate crm",
            "generate sar", "issue ato", "create poam", "assign rmf",
            "list open", "show open", "list all", "show all", "show me the",
            "get the", "run assessment", "create conmon", "generate conmon"];
        foreach (var keyword in actionKeywords)
        {
            if (lower.Contains(keyword))
                return 0.1;
        }

        // Strong knowledge-intent: action verb + domain term
        string[] knowledgeVerbs = ["explain", "what is", "what are", "tell me about",
            "define", "describe", "how does", "how do", "show me"];
        string[] domainTerms = ["nist", "stig", "rmf", "cci", "fedramp", "fed-ramp",
            "impact level", "il2", "il4", "il5", "il6", "dod", "cat i", "cat ii",
            "cat iii", "800-53", "poam", "poa&m", "ssp", "conmon", "ato process",
            "authorization"];

        bool hasKnowledgeVerb = false;
        foreach (var verb in knowledgeVerbs)
        {
            if (lower.Contains(verb))
            {
                hasKnowledgeVerb = true;
                break;
            }
        }

        bool hasDomainTerm = false;
        foreach (var term in domainTerms)
        {
            if (lower.Contains(term))
            {
                hasDomainTerm = true;
                break;
            }
        }

        // Knowledge verb + domain term → highest confidence
        if (hasKnowledgeVerb && hasDomainTerm)
            return 0.9;

        // Control ID pattern (e.g., "AC-2", "SI-3") with knowledge verb
        if (hasKnowledgeVerb && System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b[a-z]{2}-\d+"))
            return 0.8;

        // STIG ID pattern (e.g., "V-12345", "SV-12345")
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(v|sv)-\d+"))
            return 0.8;

        // Domain terms alone (no verb)
        if (hasDomainTerm)
            return 0.5;

        // Knowledge verb alone without domain context
        if (hasKnowledgeVerb)
            return 0.3;

        // Default — knowledge agent has broad catch but low
        return 0.1;
    }

    /// <inheritdoc />
    public override string GetSystemPrompt()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Ato.Copilot.Agents.KnowledgeBase.Prompts.KnowledgeBaseAgent.prompt.txt";
            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                Logger.LogWarning("KnowledgeBase agent prompt resource not found: {Resource}", resourceName);
                return GetFallbackPrompt();
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load knowledge base agent system prompt");
            return GetFallbackPrompt();
        }
    }

    /// <inheritdoc />
    public override async Task<AgentResponse> ProcessAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        var sw = Stopwatch.StartNew();

        Logger.LogInformation("KnowledgeBase agent processing: {Message}", message);

        if (string.IsNullOrWhiteSpace(message))
        {
            sw.Stop();
            return new AgentResponse
            {
                Success = true,
                Response = "I'm the KnowledgeBase Agent — I can help you understand NIST 800-53 controls, " +
                           "DISA STIGs, the RMF process, DoD Impact Levels, and FedRAMP templates. " +
                           "What would you like to know?",
                AgentName = AgentName,
                ProcessingTimeMs = sw.Elapsed.TotalMilliseconds
            };
        }
        // \u2500\u2500 AI-powered processing path (Feature 011) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
        var aiResponse = await TryProcessWithBackendAsync(message, context, cancellationToken, progress);
        if (aiResponse != null)
        {
            // Store cross-agent state for AI responses too
            await StoreQueryStateAsync(KnowledgeQueryType.GeneralKnowledge, message, aiResponse.Response, cancellationToken);
            await TrackOperationAsync(KnowledgeQueryType.GeneralKnowledge, message, true, sw.Elapsed.TotalMilliseconds, cancellationToken);
            return aiResponse;
        }
        var queryType = AnalyzeQueryType(message);

        // Find the appropriate tool for this query type
        var tool = FindToolForQueryType(queryType);
        if (tool == null)
        {
            sw.Stop();
            return new AgentResponse
            {
                Success = true,
                Response = $"I understand you're asking about {queryType}. " +
                           "I can help with NIST 800-53 controls, DISA STIGs, RMF process, " +
                           "DoD Impact Levels, and FedRAMP templates. " +
                           "Please ask a more specific question about one of these topics.\n\n" +
                           "_Disclaimer: This information is for educational purposes only " +
                           "and should be verified against authoritative sources._",
                AgentName = AgentName,
                ProcessingTimeMs = sw.Elapsed.TotalMilliseconds
            };
        }

        try
        {
            var args = ExtractToolArguments(message, queryType);
            var result = await tool.ExecuteAsync(args, cancellationToken);

            sw.Stop();

            // US9: Cross-agent state sharing — store successful query results
            await StoreQueryStateAsync(queryType, message, result, cancellationToken);

            // US11: Operation metrics
            await TrackOperationAsync(queryType, message, true, sw.Elapsed.TotalMilliseconds, cancellationToken);

            return new AgentResponse
            {
                Success = true,
                Response = result,
                AgentName = AgentName,
                ProcessingTimeMs = sw.Elapsed.TotalMilliseconds,
                ToolsExecuted =
                [
                    new ToolExecutionResult
                    {
                        ToolName = tool.Name,
                        Success = true,
                        Result = result,
                        ExecutionTimeMs = sw.Elapsed.TotalMilliseconds
                    }
                ],
                // T022c: Populate ResponseData with knowledge base answer data (FR-007b)
                ResponseData = new Dictionary<string, object>
                {
                    ["type"] = "answer",
                    ["answer"] = result,
                    ["queryType"] = queryType.ToString()
                },
                // T022c: Contextual follow-up suggestions (FR-007d)
                Suggestions = GetKnowledgeBaseSuggestions(queryType)
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing tool {ToolName} for query type {QueryType}", tool.Name, queryType);
            sw.Stop();

            // US11: Track failed operations
            await TrackOperationAsync(queryType, message, false, sw.Elapsed.TotalMilliseconds, cancellationToken);

            return new AgentResponse
            {
                Success = false,
                Response = $"I encountered an error while looking up information: {ex.Message}",
                AgentName = AgentName,
                ProcessingTimeMs = sw.Elapsed.TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Returns contextual follow-up suggestions based on the knowledge query type (T022c, FR-007d).
    /// </summary>
    private static List<AgentSuggestedAction> GetKnowledgeBaseSuggestions(KnowledgeQueryType queryType)
    {
        static AgentSuggestedAction S(string title, string? prompt = null) => new(title, prompt ?? title);

        return queryType switch
        {
            KnowledgeQueryType.NistControl or KnowledgeQueryType.NistSearch =>
                new List<AgentSuggestedAction> { S("View Related Controls", "View related controls"), S("Run Assessment", "Run compliance assessment"), S("Implementation Guidance", "Show implementation guidance") },
            KnowledgeQueryType.Stig or KnowledgeQueryType.StigSearch =>
                new List<AgentSuggestedAction> { S("View STIG Fix Guidance", "View STIG fix guidance"), S("Run Assessment", "Run compliance assessment"), S("Related NIST Controls", "Show related NIST controls") },
            KnowledgeQueryType.Rmf =>
                new List<AgentSuggestedAction> { S("View RMF Step Details", "View RMF step details"), S("Register a System", "Register a new system"), S("Show RMF Status", "Show RMF status") },
            KnowledgeQueryType.ImpactLevel =>
                new List<AgentSuggestedAction> { S("Compare Impact Levels", "Compare impact levels"), S("View FedRAMP Baseline", "View FedRAMP baseline"), S("Run Assessment", "Run compliance assessment") },
            KnowledgeQueryType.FedRamp =>
                new List<AgentSuggestedAction> { S("Generate SSP", "Generate SSP document"), S("View FedRAMP Requirements", "View FedRAMP requirements"), S("Run Assessment", "Run compliance assessment") },
            _ =>
                new List<AgentSuggestedAction> { S("Search NIST Controls", "Search NIST controls"), S("Show RMF Status", "Show RMF status"), S("Run Assessment", "Run compliance assessment") }
        };
    }

    /// <summary>
    /// US9: Stores successful query results in IAgentStateManager for cross-agent access.
    /// NIST queries store under "kb_last_nist_control"; STIG queries store under "kb_last_stig".
    /// </summary>
    private async Task StoreQueryStateAsync(
        KnowledgeQueryType queryType, string query, string result, CancellationToken cancellationToken)
    {
        try
        {
            var stateKey = queryType switch
            {
                KnowledgeQueryType.NistControl or KnowledgeQueryType.NistSearch => "kb_last_nist_control",
                KnowledgeQueryType.Stig or KnowledgeQueryType.StigSearch => "kb_last_stig",
                _ => null
            };

            if (stateKey != null)
            {
                var stateValue = new Dictionary<string, string>
                {
                    ["query"] = query,
                    ["result"] = result
                };
                await _stateManager.SetStateAsync(AgentId, stateKey, stateValue, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to store cross-agent state for query type {QueryType}", queryType);
        }
    }

    /// <summary>
    /// US11: Tracks operation metrics — type, duration, success, and counter.
    /// </summary>
    private async Task TrackOperationAsync(
        KnowledgeQueryType queryType, string query, bool success, double durationMs, CancellationToken cancellationToken)
    {
        try
        {
            await _stateManager.SetStateAsync(AgentId, "last_operation", queryType.ToString(), cancellationToken);
            await _stateManager.SetStateAsync(AgentId, "last_operation_at", DateTime.UtcNow.ToString("o"), cancellationToken);
            await _stateManager.SetStateAsync(AgentId, "last_query", query, cancellationToken);
            await _stateManager.SetStateAsync(AgentId, "last_query_success", success, cancellationToken);
            await _stateManager.SetStateAsync(AgentId, "last_query_duration_ms", durationMs, cancellationToken);

            // Increment operation counter
            var currentCount = await _stateManager.GetStateAsync<int>(AgentId, "operation_count", cancellationToken);
            await _stateManager.SetStateAsync(AgentId, "operation_count", currentCount + 1, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to track operation metrics for query type {QueryType}", queryType);
        }
    }

    /// <summary>
    /// Analyzes the user message and classifies it into a <see cref="KnowledgeQueryType"/>.
    /// </summary>
    /// <param name="message">User message to classify.</param>
    /// <returns>The classified query type.</returns>
    public KnowledgeQueryType AnalyzeQueryType(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return KnowledgeQueryType.GeneralKnowledge;

        var lower = message.ToLowerInvariant();

        // FedRAMP template queries
        if (ContainsAny(lower, "fedramp", "fed-ramp", "ssp template", "poam template",
            "poa&m template", "conmon", "authorization package"))
            return KnowledgeQueryType.FedRamp;

        // Impact level queries
        if (ContainsAny(lower, "impact level", "il2", "il4", "il5", "il6",
            "compare impact", "data classification"))
            return KnowledgeQueryType.ImpactLevel;

        // RMF process queries
        if (ContainsAny(lower, "rmf", "risk management framework", "rmf step",
            "rmf process", "authorization process"))
            return KnowledgeQueryType.Rmf;

        // STIG search queries (search before specific lookup)
        if (ContainsAny(lower, "search stig", "find stig", "stig findings",
            "stig related to", "stigs for"))
            return KnowledgeQueryType.StigSearch;

        // Specific STIG control queries
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(v|sv)-\d+") ||
            ContainsAny(lower, "stig rule", "stig control", "stig v-", "what is stig",
            "explain stig"))
            return KnowledgeQueryType.Stig;

        // NIST search queries (search before specific lookup)
        if (ContainsAny(lower, "search nist", "find control", "find nist",
            "controls related to", "controls for", "search control"))
            return KnowledgeQueryType.NistSearch;

        // Specific NIST control queries (check for control ID pattern like AC-2, SI-3)
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b[a-z]{2}-\d+") ||
            ContainsAny(lower, "nist control", "800-53 control", "what is ac",
            "what is si", "what is au", "what is sc", "what is cm", "what is ia",
            "explain control"))
            return KnowledgeQueryType.NistControl;

        // General knowledge fallback
        return KnowledgeQueryType.GeneralKnowledge;
    }

    /// <summary>
    /// Finds the registered tool that handles the given query type.
    /// </summary>
    private BaseTool? FindToolForQueryType(KnowledgeQueryType queryType)
    {
        var toolName = queryType switch
        {
            KnowledgeQueryType.NistControl => "kb_explain_nist_control",
            KnowledgeQueryType.NistSearch => "kb_search_nist_controls",
            KnowledgeQueryType.Stig => "kb_explain_stig",
            KnowledgeQueryType.StigSearch => "kb_search_stigs",
            KnowledgeQueryType.Rmf => "kb_explain_rmf",
            KnowledgeQueryType.ImpactLevel => "kb_explain_impact_level",
            KnowledgeQueryType.FedRamp => "kb_get_fedramp_template_guidance",
            _ => null
        };

        if (toolName == null)
            return null;

        return Tools.FirstOrDefault(t => t.Name == toolName);
    }

    /// <summary>
    /// Extracts tool arguments from the user message based on the query type.
    /// </summary>
    private static Dictionary<string, object?> ExtractToolArguments(string message, KnowledgeQueryType queryType)
    {
        var args = new Dictionary<string, object?>();
        var lower = message.ToLowerInvariant();

        switch (queryType)
        {
            case KnowledgeQueryType.NistControl:
                // Extract control ID (e.g., "AC-2", "SI-3")
                var controlMatch = System.Text.RegularExpressions.Regex.Match(
                    message, @"\b([A-Za-z]{2}-\d+(?:\(\d+\))?)\b");
                if (controlMatch.Success)
                    args["control_id"] = controlMatch.Groups[1].Value.ToUpperInvariant();
                break;

            case KnowledgeQueryType.NistSearch:
                // Extract search term (everything after "related to", "for", etc.)
                var searchMatch = System.Text.RegularExpressions.Regex.Match(
                    lower, @"(?:related to|for|about|search|find)\s+(.+)$");
                args["search_term"] = searchMatch.Success
                    ? searchMatch.Groups[1].Value.Trim()
                    : message.Trim();
                break;

            case KnowledgeQueryType.Stig:
                // Extract STIG ID (e.g., "V-12345", "SV-12345r1")
                var stigMatch = System.Text.RegularExpressions.Regex.Match(
                    message, @"\b((?:S?V)-\d+(?:r\d+)?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (stigMatch.Success)
                    args["stig_id"] = stigMatch.Groups[1].Value.ToUpperInvariant();
                break;

            case KnowledgeQueryType.StigSearch:
                var stigSearchMatch = System.Text.RegularExpressions.Regex.Match(
                    lower, @"(?:search|find|related to|for)\s+(.+)$");
                args["search_term"] = stigSearchMatch.Success
                    ? stigSearchMatch.Groups[1].Value.Trim()
                    : message.Trim();
                break;

            case KnowledgeQueryType.Rmf:
                // Extract optional step number
                var stepMatch = System.Text.RegularExpressions.Regex.Match(lower, @"step\s*(\d)");
                if (stepMatch.Success)
                    args["step"] = int.Parse(stepMatch.Groups[1].Value);
                break;

            case KnowledgeQueryType.ImpactLevel:
                // Extract level (e.g., "IL5", "5")
                var levelMatch = System.Text.RegularExpressions.Regex.Match(
                    message, @"\b(?:IL)?([2456])\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (levelMatch.Success)
                    args["level"] = $"IL{levelMatch.Groups[1].Value}";
                else if (lower.Contains("compare") || lower.Contains("all"))
                    args["level"] = "all";
                break;

            case KnowledgeQueryType.FedRamp:
                if (ContainsAny(lower, "ssp"))
                    args["template_type"] = "SSP";
                else if (ContainsAny(lower, "poam", "poa&m"))
                    args["template_type"] = "POAM";
                else if (ContainsAny(lower, "conmon", "crm", "continuous monitoring"))
                    args["template_type"] = "CRM";
                break;
        }

        return args;
    }

    /// <summary>
    /// Fallback system prompt if the embedded resource cannot be loaded.
    /// </summary>
    private static string GetFallbackPrompt() =>
        "You are the KnowledgeBase Agent for the ATO Copilot. " +
        "You provide informational compliance guidance about NIST 800-53, DISA STIGs, " +
        "RMF, DoD Impact Levels, and FedRAMP templates. " +
        "You do NOT scan, assess, or modify any resources.";

    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}

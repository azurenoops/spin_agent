using System.Diagnostics;
using System.Reflection;
using Azure.AI.Agents.Persistent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Document.Tools;
using Ato.Copilot.Core.Configuration;

namespace Ato.Copilot.Agents.Document.Agents;

/// <summary>
/// Document Agent routes document-centric chat intents while delegating execution
/// to existing canonical compliance services through thin adapter tools.
/// </summary>
public class DocumentAgent : BaseAgent
{
    private readonly DocumentStatusTool _statusTool;
    private readonly DocumentContextSourceTool _contextSourceTool;
    private readonly DocumentTemplateSelectorTool _templateSelectorTool;
    private readonly DocumentNarrativeGenerateAdapterTool _narrativeGenerateTool;

    public DocumentAgent(
        DocumentStatusTool statusTool,
        DocumentContextSourceTool contextSourceTool,
        DocumentTemplateSelectorTool templateSelectorTool,
        DocumentNarrativeGenerateAdapterTool narrativeGenerateTool,
        ILogger<DocumentAgent> logger,
        IChatClient? chatClient = null,
        PersistentAgentsClient? foundryClient = null,
        IOptions<AzureAiOptions>? azureAiOptions = null)
        : base(logger, chatClient, foundryClient, azureAiOptions?.Value)
    {
        _statusTool = statusTool;
        _contextSourceTool = contextSourceTool;
        _templateSelectorTool = templateSelectorTool;
        _narrativeGenerateTool = narrativeGenerateTool;

        RegisterTool(_statusTool);
        RegisterTool(_contextSourceTool);
        RegisterTool(_templateSelectorTool);
        RegisterTool(_narrativeGenerateTool);

        if (_azureAiOptions?.IsFoundry == true)
            _ = Task.Run(async () => await ProvisionFoundryAgentAsync());
    }

    public override string AgentId => "document-agent";

    public override string AgentName => "Document Agent";

    public override string Description =>
        "Manages RMF document-centric workflows for dashboard users by orchestrating existing SSP/governance/template services.";

    public override double CanHandle(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return 0.0;

        var lower = message.ToLowerInvariant();

        if (ContainsAny(lower,
            "documents page", "document page", "ssp narrative", "generate narrative",
            "draft narrative", "narrative template", "custom rmf template",
            "reference document", "source document", "sharepoint document", "document workflow"))
            return 0.9;

        if (ContainsAny(lower,
            "ssp", "sar", "poam", "document", "template", "narrative", "sharepoint"))
            return 0.55;

        return 0.0;
    }

    public override string GetSystemPrompt()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Ato.Copilot.Agents.Document.Prompts.DocumentAgent.prompt.txt";
            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                Logger.LogWarning("Document agent prompt resource not found: {Resource}", resourceName);
                return GetFallbackPrompt();
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load document agent system prompt");
            return GetFallbackPrompt();
        }
    }

    public override async Task<AgentResponse> ProcessAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        var sw = Stopwatch.StartNew();

        var aiResponse = await TryProcessWithBackendAsync(message, context, cancellationToken, progress);
        if (aiResponse != null)
            return aiResponse;

        var action = ClassifyIntent(message);
        var args = ExtractArguments(message, context, action);

        BaseTool tool = action switch
        {
            "select_template" => _templateSelectorTool,
            "parse_sources" => _contextSourceTool,
            "generate_narrative" => _narrativeGenerateTool,
            _ => _statusTool
        };

        var result = await tool.ExecuteAsync(args, cancellationToken);
        sw.Stop();

        return new AgentResponse
        {
            Success = true,
            AgentName = AgentName,
            Response = result,
            ProcessingTimeMs = sw.Elapsed.TotalMilliseconds,
            ToolsExecuted = new List<ToolExecutionResult>
            {
                new()
                {
                    ToolName = tool.Name,
                    Success = true,
                    Result = result,
                    ExecutionTimeMs = sw.Elapsed.TotalMilliseconds
                }
            },
            Suggestions = new List<AgentSuggestedAction>
            {
                new("Open Documents Page", "Show document status for this system"),
                new("Generate Narrative With Source", "Generate AC-2 narrative using this SharePoint source and custom template"),
                new("List RMF Templates", "List custom RMF templates for SSP")
            }
        };
    }

    private static string ClassifyIntent(string message)
    {
        var lower = message.ToLowerInvariant();

        if (ContainsAny(lower, "template", "custom rmf template", "select template", "list templates"))
            return "select_template";

        if (ContainsAny(lower, "sharepoint", "source document", "reference document", "source url"))
            return "parse_sources";

        if (ContainsAny(lower, "generate narrative", "draft narrative", "write narrative", "narrative"))
            return "generate_narrative";

        return "status";
    }

    private static Dictionary<string, object?> ExtractArguments(string message, AgentConversationContext context, string action)
    {
        var args = new Dictionary<string, object?>();

        var systemId = GetContextValue(context, "systemId") ?? GetContextValue(context, "system_id");
        if (!string.IsNullOrWhiteSpace(systemId))
            args["system_id"] = systemId;

        if (action == "generate_narrative")
        {
            var control = ExtractControlId(message);
            if (!string.IsNullOrWhiteSpace(control))
                args["control_id"] = control;

            var url = ExtractFirstUrl(message);
            if (!string.IsNullOrWhiteSpace(url))
                args["source_url"] = url;
        }
        else if (action == "parse_sources")
        {
            var url = ExtractFirstUrl(message);
            if (!string.IsNullOrWhiteSpace(url))
                args["source_url"] = url;
        }

        return args;
    }

    private static string? GetContextValue(AgentConversationContext context, string key)
    {
        if (context.WorkflowState.TryGetValue(key, out var value) && value != null)
            return value.ToString();
        return null;
    }

    private static string? ExtractControlId(string message)
    {
        var m = System.Text.RegularExpressions.Regex.Match(message, @"\b([A-Za-z]{2}-\d{1,3}(?:\([0-9]+\))?)\b");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static string? ExtractFirstUrl(string message)
    {
        var m = System.Text.RegularExpressions.Regex.Match(message, @"https?://\S+");
        return m.Success ? m.Value.TrimEnd('.', ',', ';', ')', ']') : null;
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        foreach (var p in patterns)
        {
            if (text.Contains(p, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string GetFallbackPrompt() =>
        "You are the Document Agent. Focus on RMF document workflows and dashboard integration. " +
        "Delegate narrative generation, governance, and template operations to existing compliance services via adapter tools. " +
        "Avoid creating duplicate logic already owned by Compliance services.";
}

using System.Diagnostics;
using System.Reflection;
using Azure.AI.Agents.Persistent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Configuration.Tools;
using Ato.Copilot.Core.Configuration;

namespace Ato.Copilot.Agents.Configuration.Agents;
/// <summary>
/// Configuration Agent — manages Security Posture Intelligence Navigator settings including subscription,
/// framework, baseline, and environment preferences.
/// Routes configuration intents to the ConfigurationTool.
/// Extends BaseAgent per Constitution Principle II.
/// </summary>
public class ConfigurationAgent : BaseAgent
{
    /// <summary>The single configuration tool managed by this agent.</summary>
    private readonly ConfigurationTool _configurationTool;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationAgent"/> class.
    /// </summary>
    /// <param name="configurationTool">Configuration management tool.</param>
    /// <param name="logger">Logger instance.</param>
    public ConfigurationAgent(
        ConfigurationTool configurationTool,
        ILogger<ConfigurationAgent> logger,
        IChatClient? chatClient = null,
        PersistentAgentsClient? foundryClient = null,
        IOptions<AzureAiOptions>? azureAiOptions = null)
        : base(logger, chatClient, foundryClient, azureAiOptions?.Value)
    {
        _configurationTool = configurationTool;
        RegisterTool(_configurationTool);

        // Provision Foundry agent in background when enabled
        if (_azureAiOptions?.IsFoundry == true)
            _ = Task.Run(async () => await ProvisionFoundryAgentAsync());
    }

    /// <inheritdoc />
    public override string AgentId => "configuration-agent";

    /// <inheritdoc />
    public override string AgentName => "Configuration Agent";

    /// <inheritdoc />
    public override string Description =>
        "Manages Security Posture Intelligence Navigator settings: subscription, framework, baseline, environment, and preferences";

    /// <summary>
    /// Evaluates confidence that this agent can handle the given message.
    /// Configuration-intent keywords (configure, set, subscription, framework, settings) score high (0.8).
    /// Returns 0.0 for unrecognized queries since configuration is a narrow domain.
    /// </summary>
    public override double CanHandle(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return 0.0;

        var lower = message.ToLowerInvariant();

        // Strong configuration intent
        string[] configKeywords = ["configure", "configuration", "set subscription", "set framework",
            "change setting", "update setting", "my settings", "show settings",
            "switch subscription", "select subscription", "select framework"];
        foreach (var keyword in configKeywords)
        {
            if (lower.Contains(keyword))
                return 0.8;
        }

        // Moderate configuration intent
        string[] moderateKeywords = ["subscription", "framework", "baseline", "preferences", "environment"];
        foreach (var keyword in moderateKeywords)
        {
            if (lower.Contains(keyword))
                return 0.5;
        }

        // Configuration agent has a narrow domain — return 0.0 for unrecognized
        return 0.0;
    }

    /// <inheritdoc />
    public override string GetSystemPrompt()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Ato.Copilot.Agents.Configuration.Prompts.ConfigurationAgent.prompt.txt";
            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                Logger.LogWarning("Configuration agent prompt resource not found: {Resource}", resourceName);
                return GetFallbackPrompt();
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load configuration agent system prompt");
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

        Logger.LogInformation("Configuration agent processing: {Message}", message);

        // ── AI-powered processing path (Feature 011 / Feature 028) ──────────
        var aiResponse = await TryProcessWithBackendAsync(message, context, cancellationToken, progress);
        if (aiResponse != null)
            return aiResponse;

        // Route the message to the appropriate configuration action
        var action = ClassifyIntent(message);
        var arguments = ExtractArguments(message, action);

        var result = await _configurationTool.ExecuteAsync(arguments, cancellationToken);

        sw.Stop();

        return new AgentResponse
        {
            Success = true,
            Response = result,
            AgentName = AgentName,
            ProcessingTimeMs = sw.Elapsed.TotalMilliseconds,
            ToolsExecuted = new List<ToolExecutionResult>
            {
                new()
                {
                    ToolName = _configurationTool.Name,
                    Success = true,
                    Result = result,
                    ExecutionTimeMs = sw.Elapsed.TotalMilliseconds
                }
            },
            // T022d: Populate ResponseData with configuration data (FR-007c)
            ResponseData = new Dictionary<string, object>
            {
                ["type"] = "configuration",
                ["action"] = action
            },
            // T022d: Contextual follow-up suggestions (FR-007d)
            Suggestions = action switch
            {
                "get_configuration" => new List<AgentSuggestedAction>
                {
                    new("Update Framework", "Update framework"),
                    new("Change Subscription", "Change subscription"),
                    new("Run Assessment", "Run compliance assessment")
                },
                "set_subscription" or "set_framework" or "set_baseline" =>
                    new List<AgentSuggestedAction>
                    {
                        new("Show Current Settings", "Show current settings"),
                        new("Run Assessment", "Run compliance assessment")
                    },
                _ => new List<AgentSuggestedAction>
                {
                    new("Show Current Settings", "Show current settings"),
                    new("Run Assessment", "Run compliance assessment")
                }
            }
        };
    }

    /// <summary>
    /// Classifies user intent into a configuration action.
    /// </summary>
    /// <param name="message">User message to classify.</param>
    /// <returns>Configuration action string.</returns>
    private static string ClassifyIntent(string message)
    {
        var lower = message.ToLowerInvariant();

        if (ContainsAny(lower, "show settings", "what's configured", "get configuration",
            "show configuration", "current settings", "my settings", "display settings"))
            return "get_configuration";

        if (ContainsAny(lower, "set subscription", "configure subscription", "use subscription",
            "switch subscription", "change subscription"))
            return "set_subscription";

        if (ContainsAny(lower, "set framework", "use framework", "switch framework",
            "change framework", "use fedramp", "use nist", "use dod"))
            return "set_framework";

        if (ContainsAny(lower, "set baseline", "change baseline", "use baseline"))
            return "set_baseline";

        if (ContainsAny(lower, "set preference", "set dry", "enable dry", "disable dry",
            "switch to government", "switch to commercial", "set scan type",
            "set cloud", "set region", "change region"))
            return "set_preference";

        // Default to showing current configuration
        return "get_configuration";
    }

    /// <summary>
    /// Extracts tool arguments from the user message based on the classified action.
    /// </summary>
    /// <param name="message">Original user message.</param>
    /// <param name="action">Classified action.</param>
    /// <returns>Arguments dictionary for the tool.</returns>
    private static Dictionary<string, object?> ExtractArguments(string message, string action)
    {
        var args = new Dictionary<string, object?> { ["action"] = action };
        var lower = message.ToLowerInvariant();

        switch (action)
        {
            case "set_subscription":
                // Try to extract GUID from message
                var words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in words)
                {
                    if (Guid.TryParse(word.Trim('\'', '"'), out _))
                    {
                        args["subscriptionId"] = word.Trim('\'', '"');
                        break;
                    }
                }
                break;

            case "set_framework":
                if (ContainsAny(lower, "fedramp high", "fedramp-high", "fedramphigh"))
                    args["framework"] = "FedRAMPHigh";
                else if (ContainsAny(lower, "fedramp moderate", "fedramp-moderate", "fedrampmoderate"))
                    args["framework"] = "FedRAMPModerate";
                else if (ContainsAny(lower, "dod", "il5", "dodil5"))
                    args["framework"] = "DoDIL5";
                else if (ContainsAny(lower, "nist", "800-53", "80053"))
                    args["framework"] = "NIST80053";
                else
                {
                    // Try last word as framework
                    var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                        args["framework"] = parts[^1];
                }
                break;

            case "set_baseline":
                if (lower.Contains("high"))
                    args["baseline"] = "High";
                else if (lower.Contains("moderate"))
                    args["baseline"] = "Moderate";
                else if (lower.Contains("low"))
                    args["baseline"] = "Low";
                break;

            case "set_preference":
                if (ContainsAny(lower, "dry run", "dryrun", "dry-run"))
                {
                    args["preferenceName"] = "dryRunDefault";
                    args["preferenceValue"] = lower.Contains("disable") || lower.Contains("off") ? "false" : "true";
                }
                else if (ContainsAny(lower, "government", "gov"))
                {
                    args["preferenceName"] = "cloudEnvironment";
                    args["preferenceValue"] = "AzureGovernment";
                }
                else if (ContainsAny(lower, "commercial", "public"))
                {
                    args["preferenceName"] = "cloudEnvironment";
                    args["preferenceValue"] = "AzureCloud";
                }
                else if (ContainsAny(lower, "scan type"))
                {
                    args["preferenceName"] = "defaultScanType";
                    if (lower.Contains("resource")) args["preferenceValue"] = "resource";
                    else if (lower.Contains("policy")) args["preferenceValue"] = "policy";
                    else args["preferenceValue"] = "combined";
                }
                else if (ContainsAny(lower, "region"))
                {
                    args["preferenceName"] = "region";
                    var regionWords = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (regionWords.Length > 0)
                        args["preferenceValue"] = regionWords[^1];
                }
                break;
        }

        return args;
    }

    /// <summary>
    /// Returns true if the text contains any of the specified phrases.
    /// </summary>
    /// <param name="text">Text to search.</param>
    /// <param name="phrases">Phrases to look for.</param>
    /// <returns>True if any phrase is found.</returns>
    private static bool ContainsAny(string text, params string[] phrases) =>
        phrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns a fallback system prompt when the embedded resource cannot be loaded.
    /// </summary>
    /// <returns>Fallback prompt string.</returns>
    private static string GetFallbackPrompt() =>
        "You are the Security Posture Intelligence Navigator Configuration Agent. You help users configure their " +
        "Azure compliance settings including subscription, framework, baseline, and environment.";
}

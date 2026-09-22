using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Observability;

namespace Ato.Copilot.Agents.Common;

/// <summary>
/// Base class for all tools in the ATO Copilot.
/// All tools MUST extend this class (Constitution Principle II).
/// </summary>
public abstract class BaseTool
{
    protected readonly ILogger Logger;

    protected BaseTool(ILogger logger)
    {
        Logger = logger;
    }

    /// <summary>
    /// Unique name of the tool
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Human-readable description of what this tool does
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Parameter definitions for this tool
    /// </summary>
    public abstract IReadOnlyDictionary<string, ToolParameter> Parameters { get; }

    /// <summary>
    /// The PIM tier required to execute this tool (per R-010).
    /// Tier 1 (None): No PIM elevation required — local/cached operations.
    /// Tier 2a (Read): Reader-level PIM role required — read-only Azure operations.
    /// Tier 2b (Write): Contributor-level (or higher) PIM role required — write operations.
    /// Override in derived classes to declare the tool's required PIM tier.
    /// </summary>
    public virtual PimTier RequiredPimTier => PimTier.None;

    /// <summary>
    /// The name of the agent that owns this tool. Override in derived classes.
    /// Used for metrics tagging (per FR-046).
    /// </summary>
    public virtual string AgentName => "compliance";

    /// <summary>
    /// Optional resolver that auto-converts system names/acronyms to GUIDs.
    /// Set via property injection during DI registration — no constructor changes needed.
    /// When set, any tool with a "system_id" parameter will automatically resolve
    /// non-GUID values (system names, acronyms) to the canonical system GUID
    /// before <see cref="ExecuteCoreAsync"/> is called.
    /// </summary>
    public ISystemIdResolver? SystemIdResolver { get; set; }

    /// <summary>
    /// Implement tool logic in derived classes. Called by <see cref="ExecuteAsync"/>.
    /// </summary>
    public abstract Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Instrumented entry point that wraps <see cref="ExecuteCoreAsync"/> with
    /// Stopwatch timing and metrics recording (latency, errors, throughput) per FR-046.
    /// All callers (ComplianceAgent, ComplianceMcpTools, McpServer) invoke this method.
    /// </summary>
    public async Task<string> ExecuteAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        // Auto-resolve system_id from name/acronym to GUID if needed
        await TryResolveSystemIdAsync(arguments, cancellationToken);
        await Ato.Copilot.State.Abstractions.ToolExecutionAuthorization
            .DemandAsync(Name, arguments, cancellationToken);

        ToolMetrics.RecordStart(Name, AgentName);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await ExecuteCoreAsync(arguments, cancellationToken);
            sw.Stop();
            ToolMetrics.RecordSuccess(sw.Elapsed.TotalMilliseconds, Name, AgentName);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var errorCode = ex is OperationCanceledException ? "cancelled" : "unhandled";
            ToolMetrics.RecordError(Name, AgentName, errorCode);
            throw;
        }
    }

    /// <summary>
    /// If this tool has a "system_id" parameter and the caller provided a non-GUID value
    /// (e.g., system name or acronym), resolve it to the canonical GUID transparently.
    /// This eliminates the need for the LLM to call compliance_list_systems first.
    /// </summary>
    private async Task TryResolveSystemIdAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        if (SystemIdResolver == null)
            return;

        if (!Parameters.ContainsKey("system_id"))
            return;

        if (!arguments.TryGetValue("system_id", out var rawValue) || rawValue == null)
            return;

        var systemIdStr = rawValue is System.Text.Json.JsonElement je
            ? je.GetString()
            : rawValue as string ?? rawValue.ToString();

        if (string.IsNullOrWhiteSpace(systemIdStr))
            return;

        // Already a GUID — no resolution needed
        if (Guid.TryParse(systemIdStr.Trim(), out _))
            return;

        // Resolve name/acronym → GUID
        var resolvedId = await SystemIdResolver.ResolveAsync(systemIdStr, cancellationToken);
        arguments["system_id"] = resolvedId;
    }

    /// <summary>
    /// Get a typed argument value from the arguments dictionary
    /// </summary>
    protected T? GetArg<T>(Dictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value == null)
            return default;

        if (value is T typedValue)
            return typedValue;

        if (value is System.Text.Json.JsonElement jsonElement)
        {
            try
            {
                var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return System.Text.Json.JsonSerializer.Deserialize<T>(jsonElement.GetRawText(), opts);
            }
            catch { return default; }
        }

        try { return (T)Convert.ChangeType(value, typeof(T)); }
        catch { return default; }
    }
}

/// <summary>
/// Defines a parameter for a tool
/// </summary>
public class ToolParameter
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
}

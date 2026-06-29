using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.KnowledgeBase.Configuration;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.KnowledgeBase.Tools;

/// <summary>
/// Tool that searches DISA STIGs by keyword and/or severity with automatic normalization
/// of severity inputs (high/cat1/cati → High, medium/cat2/catii → Medium, low/cat3/catiii → Low).
/// </summary>
public class SearchStigsTool : BaseTool
{
    private readonly IStigKnowledgeService _stigService;
    private readonly IMemoryCache _cache;
    private readonly KnowledgeBaseAgentOptions _options;

    public SearchStigsTool(
        IStigKnowledgeService stigService,
        IMemoryCache cache,
        IOptions<KnowledgeBaseAgentOptions> options,
        ILogger<SearchStigsTool> logger) : base(logger)
    {
        _stigService = stigService;
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc />
    public override string Name => "kb_search_stigs";

    /// <inheritdoc />
    public override string Description =>
        "Search DISA STIG findings by keyword and/or severity. " +
        "Severity accepts: high/cat1/cati, medium/cat2/catii, low/cat3/catiii.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters { get; } =
        new Dictionary<string, ToolParameter>
        {
            ["search_term"] = new()
            {
                Name = "search_term",
                Description = "Search keyword or topic",
                Type = "string",
                Required = true
            },
            ["severity"] = new()
            {
                Name = "severity",
                Description = "Optional severity filter: high/cat1/cati, medium/cat2/catii, low/cat3/catiii",
                Type = "string",
                Required = false
            },
            ["max_results"] = new()
            {
                Name = "max_results",
                Description = "Maximum number of results (default: 10)",
                Type = "integer",
                Required = false
            }
        };

    /// <inheritdoc />
    public override string AgentName => "KnowledgeBase Agent";

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> args,
        CancellationToken cancellationToken = default)
    {
        var searchTerm = GetArg<string>(args, "search_term");
        if (string.IsNullOrWhiteSpace(searchTerm))
            return "Error: search_term is required.";

        var severityInput = GetArg<string>(args, "severity");
        var severity = NormalizeSeverity(severityInput);
        var maxResults = GetArg<int?>(args, "max_results") ?? 10;

        var cacheKey = $"kb:search_stigs:{searchTerm.ToLowerInvariant()}:{severity?.ToString() ?? "all"}:{maxResults}";
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached != null)
            return cached;

        var stigs = await _stigService.SearchStigsAsync(searchTerm, severity, maxResults, cancellationToken);

        string result;
        if (stigs.Count == 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"## No STIG findings found for \"{searchTerm}\"");
            sb.AppendLine();
            if (severity.HasValue)
                sb.AppendLine($"No findings matched with severity **{severity.Value}**.");
            else
                sb.AppendLine("No STIG findings matched your search term.");
            sb.AppendLine();
            sb.AppendLine("**Suggestions**:");
            sb.AppendLine("- Try broader keywords");
            sb.AppendLine("- Remove the severity filter to search all categories");
            sb.AppendLine("- Severity options: high (CAT I), medium (CAT II), low (CAT III)");
            sb.AppendLine();
            sb.AppendLine("_Disclaimer: This information is for educational purposes only " +
                          "and should be verified against authoritative sources._");
            result = sb.ToString();
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine($"## STIG Search Results — {stigs.Count} results for \"{searchTerm}\"");
            if (severity.HasValue)
                sb.AppendLine($"Filtered to severity: **{FormatSeverity(severity.Value)}**");
            sb.AppendLine();

            foreach (var stig in stigs)
            {
                sb.AppendLine($"### {stig.StigId} — {stig.Title}");
                sb.AppendLine($"**Severity**: {FormatSeverity(stig.Severity)} | **Category**: {stig.Category}");
                if (!string.IsNullOrWhiteSpace(stig.Description))
                {
                    var desc = stig.Description.Length > 150
                        ? stig.Description[..150] + "..."
                        : stig.Description;
                    sb.AppendLine($"**Description**: {desc}");
                }
                if (stig.NistControls.Count > 0)
                    sb.AppendLine($"**NIST Controls**: {string.Join(", ", stig.NistControls)}");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine("Use `kb_explain_stig` with a specific STIG ID for full details.");
            sb.AppendLine();
            sb.AppendLine("_Disclaimer: This information is for educational purposes only " +
                          "and should be verified against authoritative sources._");
            result = sb.ToString();
        }

        _cache.Set(cacheKey, result, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CacheDurationMinutes), Size = 1 });
        return result;
    }

    /// <summary>
    /// Normalizes severity input strings to <see cref="StigSeverity"/> enum values.
    /// Supports: high/cat1/cati/cat i → High, medium/cat2/catii/cat ii → Medium, low/cat3/catiii/cat iii → Low.
    /// </summary>
    internal static StigSeverity? NormalizeSeverity(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var normalized = Regex.Replace(input.Trim().ToLowerInvariant(), @"\s+", "");

        return normalized switch
        {
            "high" or "cat1" or "cati" => StigSeverity.High,
            "medium" or "cat2" or "catii" => StigSeverity.Medium,
            "low" or "cat3" or "catiii" => StigSeverity.Low,
            _ => null
        };
    }

    private static string FormatSeverity(StigSeverity severity) => severity switch
    {
        StigSeverity.High => "CAT I (High)",
        StigSeverity.Medium => "CAT II (Medium)",
        StigSeverity.Low => "CAT III (Low)",
        _ => severity.ToString()
    };
}

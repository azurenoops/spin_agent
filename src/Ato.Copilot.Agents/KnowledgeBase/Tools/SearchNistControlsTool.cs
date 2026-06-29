using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.KnowledgeBase.Configuration;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Agents.KnowledgeBase.Tools;

/// <summary>
/// Tool that searches NIST 800-53 controls by keyword with optional family filtering.
/// Delegates to <see cref="INistControlsService.SearchControlsAsync"/>.
/// </summary>
public class SearchNistControlsTool : BaseTool
{
    private readonly INistControlsService _nistService;
    private readonly IMemoryCache _cache;
    private readonly KnowledgeBaseAgentOptions _options;

    public SearchNistControlsTool(
        INistControlsService nistService,
        IMemoryCache cache,
        IOptions<KnowledgeBaseAgentOptions> options,
        ILogger<SearchNistControlsTool> logger) : base(logger)
    {
        _nistService = nistService;
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc />
    public override string Name => "kb_search_nist_controls";

    /// <inheritdoc />
    public override string Description =>
        "Search NIST 800-53 controls by keyword or topic with optional family filtering. " +
        "Returns matching control IDs, titles, and descriptions.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters { get; } =
        new Dictionary<string, ToolParameter>
        {
            ["search_term"] = new()
            {
                Name = "search_term",
                Description = "Search term or keyword (e.g., 'encryption', 'access control', 'logging')",
                Type = "string",
                Required = true
            },
            ["family"] = new()
            {
                Name = "family",
                Description = "Optional control family filter (e.g., AC, AU, SC)",
                Type = "string",
                Required = false
            },
            ["max_results"] = new()
            {
                Name = "max_results",
                Description = "Maximum number of results to return (default: 10)",
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

        var family = GetArg<string>(args, "family")?.ToUpperInvariant();
        var maxResults = GetArg<int?>(args, "max_results") ?? 10;

        // Cache key based on search parameters
        var cacheKey = $"kb:search_nist:{searchTerm.ToLowerInvariant()}:{family ?? "all"}:{maxResults}";
        if (_cache.TryGetValue(cacheKey, out string? cachedResult) && cachedResult != null)
        {
            Logger.LogDebug("Returning cached search result for '{SearchTerm}'", searchTerm);
            return cachedResult;
        }

        var controls = await _nistService.SearchControlsAsync(
            searchTerm, family, null, maxResults, cancellationToken);

        // Fix #538: if no controls returned, check whether catalog is loaded or still warming up.
        // Only return the "still loading" message when we can POSITIVELY confirm the catalog
        // is not yet loaded (i.e. the concrete NistControlsService reports CatalogSource ==
        // "none").  When the service is mocked or is a different implementation, treat "no
        // results" as a genuine empty search result, not a warmup condition.
        if (controls.Count == 0)
        {
            var isConfirmedNotLoaded = _nistService is Ato.Copilot.Agents.Compliance.Services.NistControlsService nistSvc2
                && nistSvc2.CatalogSource == "none";

            if (isConfirmedNotLoaded)
            {
                return "⚠️ The NIST 800-53 catalog is still loading at startup. Please wait a few seconds and retry your search.";
            }
        }

        string result;
        if (controls.Count == 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"## No controls found for \"{searchTerm}\"");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(family))
                sb.AppendLine($"No controls in the **{family}** family matched your search.");
            else
                sb.AppendLine("No NIST 800-53 controls matched your search term.");
            sb.AppendLine();
            sb.AppendLine("**Suggestions**:");
            sb.AppendLine("- Try broader keywords (e.g., \"access\" instead of \"user account lockout\")");
            sb.AppendLine("- Remove the family filter to search all families");
            sb.AppendLine("- Use control family abbreviations: AC, AU, CM, IA, SC, SI, etc.");
            sb.AppendLine();
            sb.AppendLine("_Disclaimer: This information is for educational purposes only " +
                          "and should be verified against authoritative sources._");
            result = sb.ToString();
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine($"## NIST 800-53 Search Results — {controls.Count} results for \"{searchTerm}\"");
            if (!string.IsNullOrEmpty(family))
                sb.AppendLine($"Filtered to family: **{family}**");
            sb.AppendLine();

            foreach (var control in controls)
            {
                sb.AppendLine($"### {control.Id.ToUpperInvariant()} — {control.Title}");
                sb.AppendLine($"**Family**: {control.Family}");
                if (!string.IsNullOrWhiteSpace(control.Description))
                {
                    // Truncate long descriptions for search results
                    var desc = control.Description.Length > 200
                        ? control.Description[..200] + "..."
                        : control.Description;
                    sb.AppendLine($"**Description**: {desc}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine("Use `kb_explain_nist_control` with a specific control ID for full details.");
            sb.AppendLine();
            sb.AppendLine("_Disclaimer: This information is for educational purposes only " +
                          "and should be verified against authoritative sources._");
            result = sb.ToString();
        }

        _cache.Set(cacheKey, result, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CacheDurationMinutes), Size = 1 });
        return result;
    }
}

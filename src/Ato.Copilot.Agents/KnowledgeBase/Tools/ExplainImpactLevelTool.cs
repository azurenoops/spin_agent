using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.KnowledgeBase.Configuration;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.KnowledgeBase.Tools;

/// <summary>
/// Tool that explains DoD Impact Levels (IL2-IL6) and FedRAMP baselines with
/// security requirements, Azure guidance, and comparison capabilities.
/// </summary>
public class ExplainImpactLevelTool : BaseTool
{
    private readonly IImpactLevelService _impactLevelService;
    private readonly IMemoryCache _cache;
    private readonly KnowledgeBaseAgentOptions _options;

    public ExplainImpactLevelTool(
        IImpactLevelService impactLevelService,
        IMemoryCache cache,
        IOptions<KnowledgeBaseAgentOptions> options,
        ILogger<ExplainImpactLevelTool> logger) : base(logger)
    {
        _impactLevelService = impactLevelService;
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc />
    public override string Name => "kb_explain_impact_level";

    /// <inheritdoc />
    public override string Description =>
        "Explain DoD Impact Levels (IL2-IL6) and FedRAMP baselines with data classification, " +
        "security requirements, Azure implementation guidance, and comparison tables.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters { get; } =
        new Dictionary<string, ToolParameter>
        {
            ["level"] = new()
            {
                Name = "level",
                Description = "Impact level (e.g., 'IL5', 'IL-5', '5') or FedRAMP baseline " +
                              "(e.g., 'FedRAMP-High', 'High'). Use 'compare' or 'all' for comparison table.",
                Type = "string",
                Required = false
            }
        };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> args,
        CancellationToken cancellationToken = default)
    {
        var level = GetArg<string>(args, "level") ?? "all";

        var cacheKey = $"kb:impact:explain:{level.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached != null)
            return cached;

        var result = level.ToLowerInvariant() switch
        {
            "compare" or "all" or "comparison" => await ExplainComparisonAsync(cancellationToken),
            _ when level.StartsWith("fedramp", StringComparison.OrdinalIgnoreCase) ||
                   level.Equals("low", StringComparison.OrdinalIgnoreCase) ||
                   level.Equals("moderate", StringComparison.OrdinalIgnoreCase) ||
                   level.Equals("high", StringComparison.OrdinalIgnoreCase) =>
                await ExplainFedRampBaselineAsync(level, cancellationToken),
            _ => await ExplainSingleLevelAsync(level, cancellationToken)
        };

        _cache.Set(cacheKey, result, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CacheDurationMinutes), Size = 1 });
        return result;
    }

    private async Task<string> ExplainSingleLevelAsync(string level, CancellationToken cancellationToken)
    {
        var impactLevel = await _impactLevelService.GetImpactLevelAsync(level, cancellationToken);
        if (impactLevel == null)
            return $"Impact level '{level}' not found. Available levels: IL2, IL4, IL5, IL6, FedRAMP-Low, FedRAMP-Moderate, FedRAMP-High.";

        return FormatImpactLevel(impactLevel);
    }

    private async Task<string> ExplainFedRampBaselineAsync(string baseline, CancellationToken cancellationToken)
    {
        var level = await _impactLevelService.GetFedRampBaselineAsync(baseline, cancellationToken);
        if (level == null)
            return $"FedRAMP baseline '{baseline}' not found. Available baselines: Low, Moderate, High.";

        return FormatImpactLevel(level);
    }

    private async Task<string> ExplainComparisonAsync(CancellationToken cancellationToken)
    {
        var levels = await _impactLevelService.GetAllImpactLevelsAsync(cancellationToken);
        if (levels.Count == 0)
            return "Impact level data is not currently available.";

        var sb = new StringBuilder();
        sb.AppendLine("# DoD Impact Level Comparison");
        sb.AppendLine();

        // Summary table
        sb.AppendLine("| Level | Data Classification | Encryption | Network | Azure Region |");
        sb.AppendLine("|-------|-------------------|------------|---------|--------------|");

        foreach (var il in levels.OrderBy(l => l.Level))
        {
            var classification = Truncate(il.DataClassification, 50);
            var encryption = Truncate(il.SecurityRequirements.Encryption, 40);
            var network = Truncate(il.SecurityRequirements.Network, 40);
            var region = Truncate(il.AzureImplementation.Region, 40);
            sb.AppendLine($"| {il.Level} | {classification} | {encryption} | {network} | {region} |");
        }

        sb.AppendLine();

        // Detailed breakdown
        foreach (var il in levels.OrderBy(l => l.Level))
        {
            sb.AppendLine($"## {il.Level}: {il.Name}");
            sb.AppendLine(il.DataClassification);
            sb.AppendLine();
            sb.AppendLine($"- **Encryption**: {il.SecurityRequirements.Encryption}");
            sb.AppendLine($"- **Network**: {il.SecurityRequirements.Network}");
            sb.AppendLine($"- **Personnel**: {il.SecurityRequirements.Personnel}");
            sb.AppendLine($"- **Azure Region**: {il.AzureImplementation.Region}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatImpactLevel(ImpactLevel il)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {il.Level}: {il.Name}");
        sb.AppendLine();
        sb.AppendLine("## Data Classification");
        sb.AppendLine(il.DataClassification);
        sb.AppendLine();

        sb.AppendLine("## Security Requirements");
        sb.AppendLine($"- **Encryption**: {il.SecurityRequirements.Encryption}");
        sb.AppendLine($"- **Network**: {il.SecurityRequirements.Network}");
        sb.AppendLine($"- **Personnel**: {il.SecurityRequirements.Personnel}");
        sb.AppendLine($"- **Physical Security**: {il.SecurityRequirements.PhysicalSecurity}");
        sb.AppendLine();

        sb.AppendLine("## Azure Implementation");
        sb.AppendLine($"- **Region**: {il.AzureImplementation.Region}");
        sb.AppendLine($"- **Network**: {il.AzureImplementation.Network}");
        sb.AppendLine($"- **Identity**: {il.AzureImplementation.Identity}");
        sb.AppendLine($"- **Encryption**: {il.AzureImplementation.Encryption}");
        sb.AppendLine();

        sb.AppendLine("## Recommended Azure Services");
        foreach (var svc in il.AzureImplementation.Services)
            sb.AppendLine($"- {svc}");
        sb.AppendLine();

        if (il.AdditionalControls.Count > 0)
        {
            sb.AppendLine("## Additional Required Controls");
            sb.AppendLine(string.Join(", ", il.AdditionalControls));
        }

        return sb.ToString();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..(maxLength - 3)] + "...";
    }
}

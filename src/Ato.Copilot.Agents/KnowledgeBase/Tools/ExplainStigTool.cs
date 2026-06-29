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
/// Tool that explains a STIG finding with severity, check/fix text, NIST mappings,
/// CCI references, and Azure implementation guidance.
/// </summary>
public class ExplainStigTool : BaseTool
{
    private readonly IStigKnowledgeService _stigService;
    private readonly IMemoryCache _cache;
    private readonly KnowledgeBaseAgentOptions _options;

    public ExplainStigTool(
        IStigKnowledgeService stigService,
        IMemoryCache cache,
        IOptions<KnowledgeBaseAgentOptions> options,
        ILogger<ExplainStigTool> logger) : base(logger)
    {
        _stigService = stigService;
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc />
    public override string Name => "kb_explain_stig";

    /// <inheritdoc />
    public override string Description =>
        "Explain a DISA STIG finding with severity, check/fix text, NIST control mappings, " +
        "CCI references, and Azure implementation guidance.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters { get; } =
        new Dictionary<string, ToolParameter>
        {
            ["stig_id"] = new()
            {
                Name = "stig_id",
                Description = "STIG identifier (e.g., V-12345, SV-12345r1)",
                Type = "string",
                Required = true
            }
        };

    /// <inheritdoc />
    public override string AgentName => "KnowledgeBase Agent";

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> args,
        CancellationToken cancellationToken = default)
    {
        var stigId = GetArg<string>(args, "stig_id")?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(stigId))
            return "Error: stig_id is required.";

        // Normalize — strip rule suffix if present (e.g., "SV-12345R1_RULE" → "V-12345")
        if (stigId.StartsWith("SV-"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(stigId, @"SV-(\d+)");
            if (match.Success)
                stigId = $"V-{match.Groups[1].Value}";
        }

        var cacheKey = $"kb:stig:{stigId}";
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached != null)
            return cached;

        var control = await _stigService.GetStigControlAsync(stigId, cancellationToken);
        if (control == null)
        {
            return $"## STIG {stigId} — Not Found\n\n" +
                   "The requested STIG finding was not found in the knowledge base.\n\n" +
                   "**Suggestions**:\n" +
                   "- Verify the STIG ID format (e.g., V-12345)\n" +
                   "- Use `kb_search_stigs` to search by keyword\n\n" +
                   "_Disclaimer: This information is for educational purposes only " +
                   "and should be verified against authoritative sources._";
        }

        var sb = new StringBuilder();
        var severityCategory = control.Severity switch
        {
            StigSeverity.High => "CAT I (High)",
            StigSeverity.Medium => "CAT II (Medium)",
            StigSeverity.Low => "CAT III (Low)",
            _ => control.Severity.ToString()
        };

        sb.AppendLine($"## {control.StigId} — {control.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Severity**: {severityCategory}");
        sb.AppendLine($"**Category**: {control.Category}");
        sb.AppendLine($"**STIG Family**: {control.StigFamily}");
        sb.AppendLine($"**Rule ID**: {control.RuleId}");
        sb.AppendLine();

        // Description
        sb.AppendLine("### Description");
        sb.AppendLine(control.Description);
        sb.AppendLine();

        // NIST Control Mappings
        if (control.NistControls.Count > 0)
        {
            sb.AppendLine("### NIST 800-53 Control Mappings");
            foreach (var nist in control.NistControls)
                sb.AppendLine($"- **{nist}**");
            sb.AppendLine();
        }

        // CCI References
        if (control.CciRefs.Count > 0)
        {
            sb.AppendLine("### CCI References");
            sb.AppendLine(string.Join(", ", control.CciRefs));
            sb.AppendLine();
        }

        // Check Text
        if (!string.IsNullOrWhiteSpace(control.CheckText))
        {
            sb.AppendLine("### Check Procedure");
            sb.AppendLine(control.CheckText);
            sb.AppendLine();
        }

        // Fix Text
        if (!string.IsNullOrWhiteSpace(control.FixText))
        {
            sb.AppendLine("### Fix / Remediation");
            sb.AppendLine(control.FixText);
            sb.AppendLine();
        }

        // Azure Implementation
        if (control.AzureImplementation.Count > 0)
        {
            sb.AppendLine("### Azure Implementation Guidance");
            foreach (var (aspect, guidance) in control.AzureImplementation)
                sb.AppendLine($"- **{aspect}**: {guidance}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("_Disclaimer: This information is for educational purposes only " +
                      "and should be verified against authoritative sources._");

        var result = sb.ToString();
        _cache.Set(cacheKey, result, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CacheDurationMinutes), Size = 1 });
        return result;
    }
}

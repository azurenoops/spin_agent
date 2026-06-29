using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.KnowledgeBase.Configuration;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.KnowledgeBase.Tools;

/// <summary>
/// Tool that explains a NIST 800-53 control with supplemental guidance,
/// Azure implementation advice, related controls, and informational disclaimer.
/// Caches results per FR-026.
/// </summary>
public class ExplainNistControlTool : BaseTool
{
    private readonly INistControlsService _nistService;
    private readonly IMemoryCache _cache;
    private readonly KnowledgeBaseAgentOptions _options;

    /// <summary>Azure family guidance loaded from embedded data file.</summary>
    private static readonly Lazy<Dictionary<string, FamilyGuidance>> FamilyGuidanceData = new(LoadFamilyGuidance);

    /// <summary>
    /// Initializes a new instance of the <see cref="ExplainNistControlTool"/> class.
    /// </summary>
    public ExplainNistControlTool(
        INistControlsService nistService,
        IMemoryCache cache,
        IOptions<KnowledgeBaseAgentOptions> options,
        ILogger<ExplainNistControlTool> logger) : base(logger)
    {
        _nistService = nistService;
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc />
    public override string Name => "kb_explain_nist_control";

    /// <inheritdoc />
    public override string Description =>
        "Explain a NIST 800-53 control with statement, guidance, Azure implementation advice, " +
        "related controls, and informational disclaimer.";

    /// <inheritdoc />
    public override string AgentName => "knowledgebase";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["control_id"] = new()
        {
            Name = "control_id",
            Description = "NIST 800-53 control ID (e.g., AC-2, SI-3, AC-2(1))",
            Type = "string",
            Required = true
        }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var controlId = GetArg<string>(arguments, "control_id");
        if (string.IsNullOrWhiteSpace(controlId))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                errorCode = "missing_parameter",
                suggestion = "Please provide a control_id parameter (e.g., AC-2, SI-3)."
            });
        }

        // Normalize to uppercase
        controlId = controlId.Trim().ToUpperInvariant();

        // Check cache first
        var cacheKey = $"kb_explain_nist_{controlId}";
        if (_cache.TryGetValue(cacheKey, out string? cachedResult) && cachedResult != null)
        {
            Logger.LogDebug("Cache hit for control {ControlId}", controlId);
            return cachedResult;
        }

        // Look up the control
        var control = await _nistService.GetControlAsync(controlId, cancellationToken);

        // Enhancement fallback: if not found and looks like enhancement "AC-2(1)", try base control
        if (control == null && controlId.Contains('('))
        {
            var baseId = controlId[..controlId.IndexOf('(')];
            Logger.LogDebug("Enhancement {ControlId} not found, falling back to base {BaseId}", controlId, baseId);

            var baseControl = await _nistService.GetControlAsync(baseId, cancellationToken);
            if (baseControl != null)
            {
                // Try to find the enhancement within the base control
                var enhancement = baseControl.ControlEnhancements
                    .FirstOrDefault(e => e.Id.Equals(controlId, StringComparison.OrdinalIgnoreCase));

                if (enhancement != null)
                {
                    control = enhancement;
                    control.Family = baseControl.Family;
                }
                else
                {
                    // Return base control info with note about enhancement
                    control = baseControl;
                }
            }
        }

        if (control == null)
        {
            var result = FormatNotFound(controlId);
            return result;
        }

        var response = FormatControlResponse(controlId, control);

        // Cache the result
        _cache.Set(cacheKey, response, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CacheDurationMinutes), Size = 1 });

        return response;
    }

    /// <summary>
    /// Formats a full response for a found control.
    /// </summary>
    private string FormatControlResponse(string controlId, NistControl control)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## NIST 800-53 Control: {controlId.ToUpperInvariant()}");
        sb.AppendLine($"**Title**: {control.Title}");
        sb.AppendLine($"**Family**: {control.Family}");

        if (!string.IsNullOrEmpty(control.ImpactLevel))
            sb.AppendLine($"**Impact Level**: {control.ImpactLevel}");

        sb.AppendLine();
        sb.AppendLine("### Control Statement");
        sb.AppendLine(control.Description);

        // Azure implementation guidance
        if (!string.IsNullOrEmpty(control.AzureImplementation))
        {
            sb.AppendLine();
            sb.AppendLine("### Azure Implementation Guidance");
            sb.AppendLine(control.AzureImplementation);
        }

        // Family-specific Azure guidance from data file
        var familyId = control.Family?.ToUpperInvariant() ?? controlId[..2].ToUpperInvariant();
        if (FamilyGuidanceData.Value.TryGetValue(familyId, out var familyGuidance))
        {
            if (string.IsNullOrEmpty(control.AzureImplementation))
            {
                sb.AppendLine();
                sb.AppendLine("### Azure Implementation Guidance");
            }
            sb.AppendLine();
            sb.AppendLine($"**Relevant Azure Services**: {string.Join(", ", familyGuidance.AzureServices)}");
            sb.AppendLine();
            sb.AppendLine(familyGuidance.AzureGuidance);
        }

        // Baselines
        if (control.Baselines?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"**FedRAMP Baselines**: {string.Join(", ", control.Baselines)}");
        }

        // Related controls / enhancements
        if (control.ControlEnhancements?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Related Enhancements");
            foreach (var enhancement in control.ControlEnhancements.Take(5))
            {
                sb.AppendLine($"- **{enhancement.Id.ToUpperInvariant()}**: {enhancement.Title}");
            }
            if (control.ControlEnhancements.Count > 5)
                sb.AppendLine($"  _...and {control.ControlEnhancements.Count - 5} more enhancements_");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("_Disclaimer: This information is for educational purposes only " +
                       "and should be verified against the official NIST SP 800-53 Rev. 5 publication._");

        return sb.ToString();
    }

    /// <summary>
    /// Formats a not-found response with family suggestion.
    /// </summary>
    private static string FormatNotFound(string controlId)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Control **{controlId}** was not found in the NIST 800-53 catalog.");

        // Suggest the family if we can parse it
        if (controlId.Length >= 2)
        {
            var familyPrefix = controlId[..2].ToUpperInvariant();
            if (FamilyGuidanceData.Value.TryGetValue(familyPrefix, out var familyInfo))
            {
                sb.AppendLine();
                sb.AppendLine($"The **{familyPrefix}** family ({familyInfo.Name}) is a valid NIST family. " +
                              $"Try a specific control like **{familyPrefix}-1** or **{familyPrefix}-2**.");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Loads Azure family guidance from the embedded JSON data file.
    /// </summary>
    private static Dictionary<string, FamilyGuidance> LoadFamilyGuidance()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var basePath = Path.GetDirectoryName(assembly.Location) ?? ".";
            var filePath = Path.Combine(basePath, "KnowledgeBase", "Data", "nist-800-53-controls.json");

            if (!File.Exists(filePath))
                return new Dictionary<string, FamilyGuidance>();

            var json = File.ReadAllText(filePath);
            var doc = JsonDocument.Parse(json);

            var result = new Dictionary<string, FamilyGuidance>(StringComparer.OrdinalIgnoreCase);

            if (doc.RootElement.TryGetProperty("families", out var families))
            {
                foreach (var family in families.EnumerateObject())
                {
                    var name = family.Value.GetProperty("name").GetString() ?? "";
                    var guidance = family.Value.GetProperty("azureGuidance").GetString() ?? "";
                    var services = new List<string>();

                    if (family.Value.TryGetProperty("azureServices", out var svcs))
                    {
                        foreach (var svc in svcs.EnumerateArray())
                            services.Add(svc.GetString() ?? "");
                    }

                    result[family.Name] = new FamilyGuidance(name, guidance, services);
                }
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, FamilyGuidance>();
        }
    }

    /// <summary>
    /// Azure family-level guidance record.
    /// </summary>
    private sealed record FamilyGuidance(string Name, string AzureGuidance, List<string> AzureServices);
}

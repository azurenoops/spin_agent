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
/// Tool that provides FedRAMP authorization package template guidance,
/// including SSP sections, POA&amp;M fields, CRM/ConMon requirements,
/// and authorization checklists with Azure integration mappings.
/// </summary>
public class GetFedRampTemplateGuidanceTool : BaseTool
{
    private readonly IFedRampTemplateService _templateService;
    private readonly IMemoryCache _cache;
    private readonly KnowledgeBaseAgentOptions _options;

    public GetFedRampTemplateGuidanceTool(
        IFedRampTemplateService templateService,
        IMemoryCache cache,
        IOptions<KnowledgeBaseAgentOptions> options,
        ILogger<GetFedRampTemplateGuidanceTool> logger) : base(logger)
    {
        _templateService = templateService;
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc />
    public override string Name => "kb_get_fedramp_template_guidance";

    /// <inheritdoc />
    public override string Description =>
        "Get FedRAMP authorization package template guidance including SSP sections, " +
        "POA&M field definitions, CRM/ConMon requirements, and Azure integration mappings.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters { get; } =
        new Dictionary<string, ToolParameter>
        {
            ["template_type"] = new()
            {
                Name = "template_type",
                Description = "Template type: 'SSP', 'POAM' (or 'POA&M'), 'CRM' (or 'CONMON'). " +
                              "Omit for package overview of all templates.",
                Type = "string",
                Required = false
            },
            ["baseline"] = new()
            {
                Name = "baseline",
                Description = "FedRAMP baseline filter: 'Low', 'Moderate', 'High' (default: 'High').",
                Type = "string",
                Required = false
            }
        };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> args,
        CancellationToken cancellationToken = default)
    {
        var templateType = GetArg<string>(args, "template_type");
        var baseline = GetArg<string>(args, "baseline") ?? "High";

        var cacheKey = $"kb:fedramp:explain:{(templateType ?? "overview").ToLowerInvariant()}:{baseline.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached != null)
            return cached;

        var result = string.IsNullOrWhiteSpace(templateType)
            ? await ExplainPackageOverviewAsync(cancellationToken)
            : await ExplainTemplateAsync(templateType, baseline, cancellationToken);

        _cache.Set(cacheKey, result, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CacheDurationMinutes), Size = 1 });
        return result;
    }

    private async Task<string> ExplainTemplateAsync(string templateType, string baseline, CancellationToken cancellationToken)
    {
        var template = await _templateService.GetTemplateGuidanceAsync(templateType, baseline, cancellationToken);
        if (template == null)
            return $"FedRAMP template '{templateType}' not found. Available templates: SSP, POAM, CRM.";

        return FormatTemplate(template);
    }

    private async Task<string> ExplainPackageOverviewAsync(CancellationToken cancellationToken)
    {
        var templates = await _templateService.GetAllTemplatesAsync(cancellationToken);
        if (templates.Count == 0)
            return "FedRAMP template data is not currently available.";

        var sb = new StringBuilder();
        sb.AppendLine("# FedRAMP Authorization Package Overview");
        sb.AppendLine();
        sb.AppendLine("The FedRAMP authorization package consists of multiple documents that together " +
                       "demonstrate a cloud service provider's security posture.");
        sb.AppendLine();

        // Summary table
        sb.AppendLine("| Template | Type | Sections | Required Fields |");
        sb.AppendLine("|----------|------|----------|-----------------|");
        foreach (var t in templates)
        {
            sb.AppendLine($"| {t.Title} | {t.TemplateType} | {t.Sections.Count} | {t.RequiredFields.Count} |");
        }
        sb.AppendLine();

        // Brief description of each
        foreach (var t in templates)
        {
            sb.AppendLine($"## {t.TemplateType}: {t.Title}");
            sb.AppendLine(t.Description);
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("Use `template_type` parameter (SSP, POAM, CRM) for detailed guidance on a specific template.");

        return sb.ToString();
    }

    private static string FormatTemplate(FedRampTemplate template)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {template.TemplateType}: {template.Title}");
        sb.AppendLine();
        sb.AppendLine(template.Description);
        sb.AppendLine();

        // Sections
        sb.AppendLine("## Required Sections");
        foreach (var section in template.Sections)
        {
            sb.AppendLine($"### {section.Name}");
            sb.AppendLine(section.Description);
            sb.AppendLine();
            foreach (var element in section.RequiredElements)
                sb.AppendLine($"- {element}");
            sb.AppendLine();
        }

        // Required Fields
        sb.AppendLine("## Required Fields");
        sb.AppendLine();
        foreach (var field in template.RequiredFields)
        {
            sb.AppendLine($"### {field.Name}");
            sb.AppendLine(field.Description);
            sb.AppendLine($"- **Example**: {field.Example}");
            sb.AppendLine($"- **Azure Source**: {field.AzureSource}");
            sb.AppendLine();
        }

        // Azure Mappings
        if (template.AzureMappings.Count > 0)
        {
            sb.AppendLine("## Azure Integration Mappings");
            sb.AppendLine();
            sb.AppendLine("| Capability | Azure Service |");
            sb.AppendLine("|-----------|---------------|");
            foreach (var mapping in template.AzureMappings)
            {
                sb.AppendLine($"| {mapping.Key} | {mapping.Value} |");
            }
            sb.AppendLine();
        }

        // Authorization Checklist
        if (template.AuthorizationChecklist.Count > 0)
        {
            sb.AppendLine("## Authorization Checklist");
            sb.AppendLine();
            foreach (var item in template.AuthorizationChecklist)
            {
                var marker = item.Required ? "[Required]" : "[Optional]";
                sb.AppendLine($"- {marker} **{item.Item}**: {item.Description}");
            }
        }

        return sb.ToString();
    }
}

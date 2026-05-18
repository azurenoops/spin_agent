using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Generates deterministic narrative text for control implementations
/// using template-based string interpolation with family-specific contextual wrappers.
/// </summary>
/// <remarks>
/// Implements <see cref="IControlNarrativeService"/> as part of the Feature 048
/// FR-110 reuse-first audit (T218): there is exactly ONE concrete narrative
/// generator in the codebase. The interface registration is added alongside
/// the existing concrete singleton; concrete-typed callers continue to work
/// unchanged (back-compat per the audit's "surgical extension" mandate).
/// </remarks>
public class NarrativeTemplateService : IControlNarrativeService
{
    private readonly IChatClient? _chatClient;
    private readonly AzureAiOptions? _aiOptions;
    private readonly ILogger<NarrativeTemplateService>? _logger;
    private readonly string? _systemPrompt;

    /// <summary>Default constructor — deterministic-only mode.</summary>
    public NarrativeTemplateService() { }

    /// <summary>AI-enabled constructor — uses IChatClient when available.</summary>
    public NarrativeTemplateService(
        IChatClient? chatClient,
        AzureAiOptions? aiOptions,
        ILogger<NarrativeTemplateService>? logger)
    {
        _chatClient = chatClient;
        _aiOptions = aiOptions;
        _logger = logger;
        _systemPrompt = LoadPromptResource();
    }

    private bool IsAiEnabled => _chatClient is not null
                             && _aiOptions is { Enabled: true, IsConfigured: true };

    private static string? LoadPromptResource()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Ato.Copilot.Core.Prompts.NarrativeGeneration.prompt.txt";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// AI-assisted narrative generation. Returns null if AI is disabled or fails.
    /// </summary>
    public async Task<string?> GenerateNarrativeWithAiAsync(
        string capabilityName,
        string provider,
        string description,
        string controlId,
        string controlTitle,
        IReadOnlyList<ComponentContext>? components,
        string? boundaryName,
        CancellationToken cancellationToken = default)
    {
        if (!IsAiEnabled || _systemPrompt is null)
            return null;

        try
        {
            var userPrompt = BuildAiUserPrompt(capabilityName, provider, description,
                controlId, controlTitle, components, boundaryName);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, _systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var options = new ChatOptions { Temperature = (float?)_aiOptions!.Temperature };

            var response = await _chatClient!.GetResponseAsync(messages, options, cancellationToken);
            var narrative = response.Text?.Trim();

            if (string.IsNullOrWhiteSpace(narrative))
            {
                _logger?.LogWarning("AI returned empty narrative for {ControlId}", controlId);
                return null;
            }

            _logger?.LogInformation("AI-generated narrative for {ControlId} ({Length} chars)", controlId, narrative.Length);
            return narrative;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AI narrative generation failed for {ControlId} — falling back to deterministic", controlId);
            return null;
        }
    }

    private static string BuildAiUserPrompt(
        string capabilityName,
        string provider,
        string description,
        string controlId,
        string controlTitle,
        IReadOnlyList<ComponentContext>? components,
        string? boundaryName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Control: {controlId} — {controlTitle}");
        sb.AppendLine($"Capability: {capabilityName}");
        sb.AppendLine($"Provider: {provider}");
        sb.AppendLine($"Description: {description}");

        if (!string.IsNullOrWhiteSpace(boundaryName))
            sb.AppendLine($"Authorization Boundary: {boundaryName}");

        if (components is { Count: > 0 })
        {
            var things = components.Where(c => c.ComponentType == "Thing").ToList();
            var persons = components.Where(c => c.ComponentType == "Person").ToList();
            var places = components.Where(c => c.ComponentType == "Place").ToList();

            if (things.Count > 0)
                sb.AppendLine($"Technology Components: {string.Join(", ", things.Select(c => c.Name))}");
            if (persons.Count > 0)
                sb.AppendLine($"Responsible Personnel: {string.Join(", ", persons.Select(FormatPerson))}");
            if (places.Count > 0)
                sb.AppendLine($"Infrastructure/Facilities: {string.Join(", ", places.Select(c => c.Name))}");
        }

        var familyCode = ExtractFamilyCode(controlId);
        var familyContext = GetFamilyContext(familyCode);
        sb.AppendLine($"Control Family Guidance: This control addresses {familyContext}.");

        return sb.ToString();
    }
    /// <summary>
    /// Generates a single enriched narrative with component and boundary context.
    /// Falls back to simple <see cref="GenerateNarrative"/> when no components are provided.
    /// </summary>
    public string GenerateEnrichedNarrative(
        string capabilityName,
        string provider,
        string description,
        string controlId,
        string controlTitle,
        IReadOnlyList<ComponentContext>? components,
        string? boundaryName)
    {
        if (components is null or { Count: 0 })
            return GenerateNarrative(capabilityName, provider, description, controlId, controlTitle);

        var familyCode = ExtractFamilyCode(controlId);
        var familyContext = GetFamilyContext(familyCode);

        var sb = new StringBuilder();
        sb.AppendLine($"The organization implements {capabilityName} using {provider}. {description}");
        sb.AppendLine();
        sb.AppendLine($"This capability addresses {controlTitle} ({controlId}) by providing {familyContext}.");
        sb.AppendLine();

        AppendComponentDetails(sb, components);

        if (!string.IsNullOrWhiteSpace(boundaryName))
        {
            sb.AppendLine($"This implementation operates within the {boundaryName} authorization boundary.");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Generates a narrative for a control implementation based on the capability and control metadata.
    /// </summary>
    /// <param name="capabilityName">Name of the security capability.</param>
    /// <param name="provider">Provider/vendor of the capability.</param>
    /// <param name="description">Description of how the capability works.</param>
    /// <param name="controlId">NIST control ID (e.g., "AC-2").</param>
    /// <param name="controlTitle">NIST control title.</param>
    /// <returns>Generated narrative text.</returns>
    public string GenerateNarrative(
        string capabilityName,
        string provider,
        string description,
        string controlId,
        string controlTitle)
    {
        var familyCode = ExtractFamilyCode(controlId);
        var familyContext = GetFamilyContext(familyCode);

        return $"""
            The organization implements {capabilityName} using {provider}. {description}

            This capability addresses {controlTitle} ({controlId}) by providing {familyContext}.
            """.Trim();
    }

    /// <summary>
    /// Generates a composite narrative for a control that has mappings across multiple boundaries.
    /// Org-wide mappings (null boundary FK) appear first, then per-boundary sections.
    /// Single-mapping scenarios produce a simple narrative (passthrough to <see cref="GenerateNarrative"/>).
    /// </summary>
    public string GenerateCompositeNarrative(
        string controlId,
        string controlTitle,
        IReadOnlyList<BoundaryMappingContext> mappings)
    {
        if (mappings.Count == 0)
            return string.Empty;

        // Single mapping — use enriched narrative if components available
        if (mappings.Count == 1)
        {
            var m = mappings[0];
            return GenerateEnrichedNarrative(m.CapabilityName, m.Provider, m.Description,
                controlId, controlTitle, m.Components, m.BoundaryName);
        }

        var familyCode = ExtractFamilyCode(controlId);
        var familyContext = GetFamilyContext(familyCode);

        var sb = new StringBuilder();
        sb.AppendLine($"The organization implements {controlTitle} ({controlId}) through the following capabilities:");
        sb.AppendLine();

        // Org-wide mappings first
        foreach (var m in mappings.Where(m => m.BoundaryName is null))
        {
            sb.AppendLine($"Organization-Wide: {m.CapabilityName} using {m.Provider}. {m.Description}. This capability provides {familyContext}.");
            if (m.Components is { Count: > 0 })
            {
                AppendComponentDetails(sb, m.Components);
            }
            sb.AppendLine();
        }

        // Per-boundary mappings
        foreach (var m in mappings.Where(m => m.BoundaryName is not null).OrderBy(m => m.BoundaryName))
        {
            sb.AppendLine($"Within the {m.BoundaryName} boundary: {m.CapabilityName} using {m.Provider}. {m.Description}. This capability provides {familyContext}.");
            if (m.Components is { Count: > 0 })
            {
                AppendComponentDetails(sb, m.Components);
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string ExtractFamilyCode(string controlId)
    {
        var dashIndex = controlId.IndexOf('-');
        return dashIndex > 0 ? controlId[..dashIndex].ToUpperInvariant() : controlId.ToUpperInvariant();
    }

    private static string GetFamilyContext(string familyCode) => familyCode switch
    {
        "AC" => "access control enforcement and account management mechanisms",
        "AT" => "security awareness and training capabilities for organizational personnel",
        "AU" => "audit logging, event monitoring, and accountability mechanisms",
        "CA" => "continuous assessment, authorization support, and plan of action management",
        "CM" => "configuration management, baseline enforcement, and change control",
        "CP" => "contingency planning, backup, and disaster recovery capabilities",
        "IA" => "identification and authentication mechanisms for users and devices",
        "IR" => "incident response detection, handling, and reporting capabilities",
        "MA" => "system maintenance controls and authorized maintenance procedures",
        "MP" => "media protection, sanitization, and transport controls",
        "PE" => "physical and environmental protection mechanisms",
        "PL" => "security planning, rules of behavior, and policy documentation",
        "PM" => "program management oversight and risk management framework support",
        "PS" => "personnel security screening, termination, and transfer controls",
        "PT" => "personally identifiable information processing and transparency controls",
        "RA" => "risk assessment, vulnerability scanning, and threat analysis capabilities",
        "SA" => "system and services acquisition lifecycle and supply chain protections",
        "SC" => "system and communications protection including encryption and boundary defense",
        "SI" => "system and information integrity monitoring, flaw remediation, and malware protection",
        "SR" => "supply chain risk management and component authenticity verification",
        _ => "security controls and organizational risk mitigation measures",
    };

    private static void AppendComponentDetails(StringBuilder sb, IReadOnlyList<ComponentContext> components)
    {
        var things = components.Where(c => c.ComponentType == "Thing").ToList();
        var persons = components.Where(c => c.ComponentType == "Person").ToList();
        var places = components.Where(c => c.ComponentType == "Place").ToList();

        if (things.Count > 0)
            sb.AppendLine($"Technology: {string.Join(", ", things.Select(c => c.Name))}.");
        if (persons.Count > 0)
            sb.AppendLine($"Responsible personnel: {string.Join(", ", persons.Select(FormatPerson))}.");
        if (places.Count > 0)
            sb.AppendLine($"Infrastructure: {string.Join(", ", places.Select(c => c.Name))}.");
    }

    private static string FormatPerson(ComponentContext c)
    {
        if (!string.IsNullOrWhiteSpace(c.PersonName))
            return $"{c.PersonName}, {c.Name}";
        return c.Name;
    }
}

/// <summary>
/// Context for a single capability-to-control mapping within a boundary (or org-wide).
/// </summary>
public record BoundaryMappingContext(
    string CapabilityName,
    string Provider,
    string Description,
    string? BoundaryName,
    IReadOnlyList<ComponentContext>? Components = null);

/// <summary>
/// Component metadata for narrative template enrichment.
/// </summary>
public record ComponentContext(
    string Name,
    string ComponentType,
    string? Owner,
    string? PersonName = null);

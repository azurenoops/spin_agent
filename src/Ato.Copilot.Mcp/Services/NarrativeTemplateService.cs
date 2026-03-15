using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// Generates deterministic narrative text for control implementations
/// using template-based string interpolation with family-specific contextual wrappers.
/// </summary>
public class NarrativeTemplateService
{
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
}

using System.Text.Json;
using Ato.Copilot.Core.Interfaces.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Advisory-only FedRAMP business-rule validation for OSCAL documents.
/// Runs C#-based constraint checks mirroring the most critical FedRAMP Schematron rules
/// from github.com/GSA/fedramp-automation.
/// Never blocks export — all violations are advisory.
/// Feature 076 — T007.
/// TODO: integrate Saxon.HE XSL transform against pinned GSA/fedramp-automation commit
///       when the Schematron XSL rules are stabilised for OSCAL 1.1.2.
/// </summary>
public class FedRampSchematronService : IFedRampSchematronService
{
    private const string FedRampNs = "https://fedramp.gov/ns/oscal";
    private readonly ILogger<FedRampSchematronService> _logger;

    public FedRampSchematronService(ILogger<FedRampSchematronService> logger)
        => _logger = logger;

    public Task<FedRampSchematronResult> ValidateAsync(
        string oscalJson,
        string documentType,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<SchematronViolation>();

        try
        {
            using var doc = JsonDocument.Parse(oscalJson);
            var root = doc.RootElement;

            ValidateOscalVersion(root, documentType, violations);
            ValidateImplementationStatus(root, documentType, violations);
            ValidateSystemIdPresent(root, documentType, violations);
            ValidateNoEmptyByComponents(root, documentType, violations);
        }
        catch (JsonException ex)
        {
            violations.Add(new SchematronViolation
            {
                Severity = "high",
                Path     = "$",
                Message  = $"Invalid JSON — cannot run Schematron checks: {ex.Message}",
                RuleId   = "FEDRAMP-PARSE-001"
            });
        }

        var result = new FedRampSchematronResult
        {
            IsCompliant  = violations.All(v => v.Severity != "high"),
            AdvisoryOnly = true,
            DocumentType = documentType,
            Violations   = violations
        };

        if (violations.Count > 0)
            _logger.LogWarning("FedRAMP Schematron advisory: {Count} violations on {Type} document",
                violations.Count, documentType);

        return Task.FromResult(result);
    }

    private static void ValidateOscalVersion(JsonElement root, string docType, List<SchematronViolation> v)
    {
        var key = docType switch
        {
            "poam" => "plan-of-action-and-milestones",
            "sar"  => "assessment-results",
            "sap"  => "assessment-plan",
            _      => "system-security-plan"
        };

        if (root.TryGetProperty(key, out var doc) &&
            doc.TryGetProperty("metadata", out var meta) &&
            meta.TryGetProperty("oscal-version", out var ver))
        {
            if (ver.GetString() != "1.1.2")
                v.Add(new SchematronViolation
                {
                    Severity = "medium",
                    Path     = $"{key}.metadata.oscal-version",
                    Message  = $"oscal-version should be '1.1.2', found '{ver.GetString()}'.",
                    RuleId   = "FEDRAMP-META-001"
                });
        }
    }

    private static void ValidateImplementationStatus(JsonElement root, string docType, List<SchematronViolation> v)
    {
        if (docType != "ssp") return;
        if (!root.TryGetProperty("system-security-plan", out var ssp)) return;
        if (!ssp.TryGetProperty("control-implementation", out var ci)) return;
        if (!ci.TryGetProperty("implemented-requirements", out var reqs)) return;

        foreach (var req in reqs.EnumerateArray())
        {
            var controlId = req.TryGetProperty("control-id", out var cid) ? cid.GetString() ?? "?" : "?";
            var hasStatus = false;

            if (req.TryGetProperty("props", out var props))
            {
                foreach (var prop in props.EnumerateArray())
                {
                    if (prop.TryGetProperty("name", out var name) &&
                        name.GetString() == "implementation-status" &&
                        prop.TryGetProperty("ns", out var ns) &&
                        ns.GetString() == FedRampNs)
                    {
                        hasStatus = true;
                        break;
                    }
                }
            }

            if (!hasStatus)
                v.Add(new SchematronViolation
                {
                    Severity = "high",
                    Path     = $"control-implementation.implemented-requirements[control-id={controlId}].props",
                    Message  = $"Control {controlId} is missing required FedRAMP 'implementation-status' prop (ns={FedRampNs}).",
                    RuleId   = "FEDRAMP-SSP-001"
                });
        }
    }

    private static void ValidateSystemIdPresent(JsonElement root, string docType, List<SchematronViolation> v)
    {
        if (docType != "poam") return;
        if (!root.TryGetProperty("plan-of-action-and-milestones", out var poam)) return;

        if (!poam.TryGetProperty("system-id", out _))
            v.Add(new SchematronViolation
            {
                Severity = "high",
                Path     = "plan-of-action-and-milestones.system-id",
                Message  = "POA&M is missing required system-id element.",
                RuleId   = "FEDRAMP-POAM-001"
            });
    }

    private static void ValidateNoEmptyByComponents(JsonElement root, string docType, List<SchematronViolation> v)
    {
        if (docType != "ssp") return;
        if (!root.TryGetProperty("system-security-plan", out var ssp)) return;
        if (!ssp.TryGetProperty("control-implementation", out var ci)) return;
        if (!ci.TryGetProperty("implemented-requirements", out var reqs)) return;

        foreach (var req in reqs.EnumerateArray())
        {
            var controlId = req.TryGetProperty("control-id", out var cid) ? cid.GetString() ?? "?" : "?";
            if (!req.TryGetProperty("statements", out var stmts)) continue;

            foreach (var stmt in stmts.EnumerateArray())
            {
                if (stmt.TryGetProperty("by-components", out var byComp) &&
                    byComp.ValueKind == JsonValueKind.Array &&
                    byComp.GetArrayLength() == 0)
                {
                    v.Add(new SchematronViolation
                    {
                        Severity = "medium",
                        Path     = $"implemented-requirements[{controlId}].statements.by-components",
                        Message  = $"Control {controlId} has an empty by-components array — add at least one component implementation.",
                        RuleId   = "FEDRAMP-SSP-002"
                    });
                }
            }
        }
    }
}

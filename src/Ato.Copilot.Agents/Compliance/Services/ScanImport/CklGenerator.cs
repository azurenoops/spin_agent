// ═══════════════════════════════════════════════════════════════════════════
// Feature 017 — SCAP/STIG Import: CKL Generator (T043)
// Generates DISA STIG Viewer CKL XML from assessment data.
// ═══════════════════════════════════════════════════════════════════════════

using System.Xml.Linq;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services.ScanImport;

/// <summary>
/// Generates DISA STIG Viewer CKL XML from STIG controls and assessment findings.
/// </summary>
public interface ICklGenerator
{
    /// <summary>
    /// Generate a CKL XML document for the given system, benchmark controls, and findings.
    /// </summary>
    /// <param name="system">Registered system for ASSET section.</param>
    /// <param name="stigControls">All STIG controls for the benchmark (full checklist).</param>
    /// <param name="findings">Findings keyed by VulnId/StigId for STATUS lookup.</param>
    /// <param name="benchmarkId">STIG benchmark identifier.</param>
    /// <param name="benchmarkVersion">STIG version number.</param>
    /// <param name="benchmarkTitle">Human-readable STIG title.</param>
    /// <returns>CKL XML as a string.</returns>
    string Generate(
        RegisteredSystem system,
        List<StigControl> stigControls,
        Dictionary<string, ComplianceFinding> findings,
        string benchmarkId,
        string? benchmarkVersion,
        string? benchmarkTitle);
}

/// <summary>
/// Implementation of <see cref="ICklGenerator"/>.
/// Produces well-formed DISA STIG Viewer-compatible CHECKLIST XML.
/// </summary>
public class CklGenerator : ICklGenerator
{
    private readonly ILogger<CklGenerator> _logger;

    public CklGenerator(ILogger<CklGenerator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Generate(
        RegisteredSystem system,
        List<StigControl> stigControls,
        Dictionary<string, ComplianceFinding> findings,
        string benchmarkId,
        string? benchmarkVersion,
        string? benchmarkTitle)
    {
        var checklist = new XElement("CHECKLIST",
            GenerateAsset(system),
            new XElement("STIGS",
                new XElement("iSTIG",
                    GenerateStigInfo(benchmarkId, benchmarkVersion, benchmarkTitle),
                    stigControls.Select(sc => GenerateVuln(sc, findings)))));

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XComment("DISA STIG Viewer :: Security Posture Intelligence Navigator Export"),
            checklist);

        _logger.LogInformation(
            "Generated CKL for benchmark '{BenchmarkId}': {VulnCount} VULNs, {FindingCount} findings present",
            benchmarkId, stigControls.Count, findings.Count);

        return doc.Declaration + Environment.NewLine + doc.ToString();
    }

    /// <summary>Generate the ASSET element from RegisteredSystem metadata.</summary>
    private static XElement GenerateAsset(RegisteredSystem system)
    {
        return new XElement("ASSET",
            new XElement("ROLE", "None"),
            new XElement("ASSET_TYPE", "Computing"),
            new XElement("HOST_NAME", system.Name ?? string.Empty),
            new XElement("HOST_IP", string.Empty),
            new XElement("HOST_MAC", string.Empty),
            new XElement("HOST_FQDN", string.Empty),
            new XElement("TARGET_COMMENT", string.Empty),
            new XElement("TECH_AREA", string.Empty),
            new XElement("TARGET_KEY", string.Empty),
            new XElement("WEB_OR_DATABASE", "false"),
            new XElement("WEB_DB_SITE", string.Empty),
            new XElement("WEB_DB_INSTANCE", string.Empty));
    }

    /// <summary>Generate the STIG_INFO element.</summary>
    private static XElement GenerateStigInfo(string benchmarkId, string? version, string? title)
    {
        return new XElement("STIG_INFO",
            SiData("version", version ?? "1"),
            SiData("stigid", benchmarkId),
            SiData("releaseinfo", string.Empty),
            SiData("title", title ?? benchmarkId));
    }

    /// <summary>Create a STIG_INFO SI_DATA element.</summary>
    private static XElement SiData(string name, string value)
    {
        return new XElement("SI_DATA",
            new XElement("SID_NAME", name),
            new XElement("SID_DATA", value));
    }

    /// <summary>Generate a VULN element from a StigControl and optional matching finding.</summary>
    private static XElement GenerateVuln(StigControl stigControl, Dictionary<string, ComplianceFinding> findings)
    {
        // Try to find a matching finding by StigId/VulnId
        findings.TryGetValue(stigControl.VulnId ?? stigControl.StigId, out var finding);

        var status = MapFindingStatus(finding);
        var findingDetails = finding?.Description ?? string.Empty;
        var comments = string.Empty;

        var vuln = new XElement("VULN",
            StigData("Vuln_Num", stigControl.VulnId ?? stigControl.StigId),
            StigData("Severity", MapSeverity(stigControl.Severity)),
            StigData("Group_Title", string.Empty),
            StigData("Rule_ID", stigControl.RuleId ?? string.Empty),
            StigData("Rule_Ver", stigControl.StigVersion ?? string.Empty),
            StigData("Rule_Title", stigControl.Title ?? string.Empty));

        // Add CCI_REF entries
        if (stigControl.CciRefs is { Count: > 0 })
        {
            foreach (var cci in stigControl.CciRefs)
            {
                vuln.Add(StigData("CCI_REF", cci));
            }
        }

        vuln.Add(
            new XElement("STATUS", status),
            new XElement("FINDING_DETAILS", findingDetails),
            new XElement("COMMENTS", comments),
            new XElement("SEVERITY_OVERRIDE", string.Empty),
            new XElement("SEVERITY_JUSTIFICATION", string.Empty));

        return vuln;
    }

    /// <summary>Create a STIG_DATA element for a VULN entry.</summary>
    private static XElement StigData(string attribute, string value)
    {
        return new XElement("STIG_DATA",
            new XElement("VULN_ATTRIBUTE", attribute),
            new XElement("ATTRIBUTE_DATA", value));
    }

    /// <summary>
    /// Map <see cref="FindingStatus"/> to CKL STATUS string.
    /// </summary>
    private static string MapFindingStatus(ComplianceFinding? finding)
    {
        if (finding is null)
            return "Not_Reviewed";

        return finding.Status switch
        {
            FindingStatus.Open => "Open",
            FindingStatus.InProgress => "Open",
            FindingStatus.Remediated => "NotAFinding",
            FindingStatus.Accepted => "Not_Applicable",
            _ => "Not_Reviewed"
        };
    }

    /// <summary>
    /// Map <see cref="StigSeverity"/> to CKL severity string.
    /// </summary>
    private static string MapSeverity(StigSeverity severity)
    {
        return severity switch
        {
            StigSeverity.High => "high",
            StigSeverity.Medium => "medium",
            StigSeverity.Low => "low",
            _ => "medium"
        };
    }
}

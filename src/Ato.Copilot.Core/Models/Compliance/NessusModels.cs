// ═══════════════════════════════════════════════════════════════════════════
// Feature 026 — ACAS/Nessus Scan Import: DTOs and Enums
// See specs/026-acas-nessus-import/data-model.md for full specification.
// ═══════════════════════════════════════════════════════════════════════════

namespace Ato.Copilot.Core.Models.Compliance;

// ─── Enums ───────────────────────────────────────────────────────────────────

/// <summary>
/// Source of the NIST 800-53 control mapping for a Nessus finding.
/// </summary>
public enum NessusControlMappingSource
{
    /// <summary>Mapped via STIG-ID xref → CCI → NIST chain (Definitive confidence).</summary>
    StigXref,

    /// <summary>Mapped via curated plugin-family → NIST control-family table (Heuristic confidence).</summary>
    PluginFamilyHeuristic
}

// ─── Parser Output DTOs ──────────────────────────────────────────────────────

/// <summary>
/// Top-level parse result from a .nessus file.
/// </summary>
/// <param name="ReportName">Report name from the <c>&lt;Report&gt;</c> element.</param>
/// <param name="Hosts">Parsed hosts with their plugin results.</param>
/// <param name="TotalPluginResults">Total number of ReportItem entries across all hosts.</param>
/// <param name="InformationalCount">Count of severity-0 (informational) plugins excluded from findings.</param>
public record ParsedNessusFile(
    string ReportName,
    List<NessusReportHost> Hosts,
    int TotalPluginResults,
    int InformationalCount);

/// <summary>
/// A scanned host extracted from a .nessus file's <c>&lt;ReportHost&gt;</c> element.
/// </summary>
/// <param name="Name">ReportHost name attribute (hostname or IP).</param>
/// <param name="HostIp">IP address from HostProperties.</param>
/// <param name="Hostname">Hostname from HostProperties (fallback to <paramref name="Name"/>).</param>
/// <param name="OperatingSystem">Detected OS.</param>
/// <param name="MacAddress">MAC address.</param>
/// <param name="CredentialedScan">Whether the scan was credentialed for this host.</param>
/// <param name="ScanStart">HOST_START timestamp.</param>
/// <param name="ScanEnd">HOST_END timestamp.</param>
/// <param name="PluginResults">All plugin results for this host.</param>
public record NessusReportHost(
    string Name,
    string? HostIp,
    string? Hostname,
    string? OperatingSystem,
    string? MacAddress,
    bool CredentialedScan,
    DateTime? ScanStart,
    DateTime? ScanEnd,
    List<NessusPluginResult> PluginResults);

/// <summary>
/// A single vulnerability finding for a host-port combination.
/// </summary>
/// <param name="PluginId">Unique Nessus plugin identifier.</param>
/// <param name="PluginName">Human-readable plugin name.</param>
/// <param name="PluginFamily">Plugin family category (maps to NIST controls).</param>
/// <param name="Severity">Severity integer: 0=Info, 1=Low, 2=Medium, 3=High, 4=Critical.</param>
/// <param name="RiskFactor">Risk factor label: None, Low, Medium, High, Critical.</param>
/// <param name="Port">Port number (0 = host-level finding).</param>
/// <param name="Protocol">Protocol: tcp, udp, icmp, or null.</param>
/// <param name="ServiceName">Service name (e.g., www, ssh, cifs).</param>
/// <param name="Synopsis">Brief vulnerability summary.</param>
/// <param name="Description">Full vulnerability description.</param>
/// <param name="Solution">Recommended remediation.</param>
/// <param name="PluginOutput">Scan-specific evidence output.</param>
/// <param name="Cves">All CVE identifiers.</param>
/// <param name="Xrefs">Cross-references (STIG-ID, IAVA, CWE, etc.).</param>
/// <param name="CvssV2BaseScore">CVSS v2 base score (fallback).</param>
/// <param name="CvssV3BaseScore">CVSS v3 base score (preferred).</param>
/// <param name="CvssV3Vector">Full CVSS v3 vector string.</param>
/// <param name="VprScore">Tenable Vulnerability Priority Rating.</param>
/// <param name="ExploitAvailable">Whether a public exploit exists.</param>
/// <param name="StigSeverity">STIG severity from stig_severity element: I, II, III.</param>
public record NessusPluginResult(
    int PluginId,
    string PluginName,
    string PluginFamily,
    int Severity,
    string RiskFactor,
    int Port,
    string? Protocol,
    string? ServiceName,
    string? Synopsis,
    string? Description,
    string? Solution,
    string? PluginOutput,
    List<string> Cves,
    List<string> Xrefs,
    double? CvssV2BaseScore,
    double? CvssV3BaseScore,
    string? CvssV3Vector,
    double? VprScore,
    bool ExploitAvailable,
    string? StigSeverity);

// ─── Control Mapping DTOs ────────────────────────────────────────────────────

/// <summary>
/// Curated mapping entry from plugin family to NIST 800-53 controls.
/// </summary>
/// <param name="PluginFamily">Nessus plugin family name.</param>
/// <param name="PrimaryControl">Primary NIST 800-53 control (e.g., SI-2).</param>
/// <param name="SecondaryControls">Additional related NIST controls.</param>
public record PluginFamilyMapping(
    string PluginFamily,
    string PrimaryControl,
    string[] SecondaryControls);

/// <summary>
/// Result of mapping a Nessus plugin to NIST 800-53 controls.
/// </summary>
/// <param name="NistControlIds">Resolved NIST 800-53 control identifiers.</param>
/// <param name="CciRefs">CCI references (populated only for STIG-ID xref mappings).</param>
/// <param name="MappingSource">How the mapping was derived.</param>
public record NessusControlMappingResult(
    List<string> NistControlIds,
    List<string> CciRefs,
    NessusControlMappingSource MappingSource);

// ─── Import Result DTO ───────────────────────────────────────────────────────

/// <summary>
/// Return value from Nessus import operations. Contains all counts, warnings, and status.
/// </summary>
/// <param name="ImportRecordId">ID of the created <see cref="ScanImportRecord"/> (empty for dry-run).</param>
/// <param name="Status">Final import status.</param>
/// <param name="ReportName">Report name from the .nessus file.</param>
/// <param name="TotalPluginResults">Total plugins processed (all severities).</param>
/// <param name="InformationalCount">Severity 0 plugins (counted but not persisted).</param>
/// <param name="CriticalCount">Severity 4 findings.</param>
/// <param name="HighCount">Severity 3 findings.</param>
/// <param name="MediumCount">Severity 2 findings.</param>
/// <param name="LowCount">Severity 1 findings.</param>
/// <param name="HostCount">Distinct hosts scanned.</param>
/// <param name="FindingsCreated">New ScanImportFinding records created.</param>
/// <param name="FindingsUpdated">Existing findings updated (overwrite/merge).</param>
/// <param name="SkippedCount">Duplicate findings skipped.</param>
/// <param name="PoamWeaknessesCreated">POA&amp;M weakness entries created.</param>
/// <param name="EffectivenessRecordsCreated">New ControlEffectiveness records.</param>
/// <param name="EffectivenessRecordsUpdated">Updated ControlEffectiveness records.</param>
/// <param name="NistControlsAffected">Unique NIST controls touched.</param>
/// <param name="CredentialedScan">Whether scan was credentialed.</param>
/// <param name="IsDryRun">Whether this was a preview-only import.</param>
/// <param name="Warnings">Non-fatal warning messages.</param>
/// <param name="ErrorMessage">Error details if import failed.</param>
public record NessusImportResult(
    string ImportRecordId,
    ScanImportStatus Status,
    string ReportName,
    int TotalPluginResults,
    int InformationalCount,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    int HostCount,
    int FindingsCreated,
    int FindingsUpdated,
    int SkippedCount,
    int PoamWeaknessesCreated,
    int EffectivenessRecordsCreated,
    int EffectivenessRecordsUpdated,
    int NistControlsAffected,
    bool CredentialedScan,
    bool IsDryRun,
    List<string> Warnings,
    string? ErrorMessage,
    int PoamItemsCreated = 0,
    int PoamItemsDeduplicated = 0,
    int ComponentLinksCreated = 0);

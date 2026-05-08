using Ato.Copilot.Core.Models.Tenancy.Attributes;
// ═══════════════════════════════════════════════════════════════════════════
// Feature 017 — SCAP/STIG Viewer Import: Entities, Enums, and DTOs
// See specs/017-scap-stig-import/data-model.md for full specification.
// ═══════════════════════════════════════════════════════════════════════════

namespace Ato.Copilot.Core.Models.Compliance;

// ─── Enums ───────────────────────────────────────────────────────────────────

/// <summary>
/// Type of scan import file. Determines which parser is used.
/// </summary>
public enum ScanImportType
{
    /// <summary>DISA STIG Viewer checklist (.ckl) — XML format with manual assessments.</summary>
    Ckl,

    /// <summary>SCAP Compliance Checker XCCDF results (.xml) — automated scan output.</summary>
    Xccdf,

    /// <summary>Prisma Cloud CSPM compliance CSV export.</summary>
    PrismaCsv,

    /// <summary>Prisma Cloud API JSON (RQL alert response).</summary>
    PrismaApi,

    /// <summary>ACAS/Nessus vulnerability scan (.nessus) — NessusClientData_v2 XML format.</summary>
    NessusXml
}

/// <summary>
/// Final status of a scan import operation.
/// </summary>
public enum ScanImportStatus
{
    /// <summary>All entries processed successfully with no warnings.</summary>
    Completed,

    /// <summary>Processed but with unmatched rules or baseline mismatches.</summary>
    CompletedWithWarnings,

    /// <summary>Fatal error — malformed XML, system not found, etc.</summary>
    Failed
}

/// <summary>
/// Strategy for handling duplicate findings during re-import.
/// </summary>
public enum ImportConflictResolution
{
    /// <summary>Keep existing findings, skip duplicates.</summary>
    Skip,

    /// <summary>Replace existing findings with imported data.</summary>
    Overwrite,

    /// <summary>Keep more-recent data, append details if different.</summary>
    Merge
}

/// <summary>
/// Action taken for each individual finding during import.
/// Stored on <see cref="ScanImportFinding"/> for audit trail.
/// </summary>
public enum ImportFindingAction
{
    /// <summary>New <see cref="ComplianceFinding"/> created.</summary>
    Created,

    /// <summary>Existing finding updated via overwrite or merge.</summary>
    Updated,

    /// <summary>Duplicate skipped (skip conflict resolution).</summary>
    Skipped,

    /// <summary>STIG rule not found in curated library.</summary>
    Unmatched,

    /// <summary>CKL Not_Applicable or XCCDF notapplicable — no ComplianceFinding created.</summary>
    NotApplicable,

    /// <summary>CKL Not_Reviewed or XCCDF notchecked — ComplianceFinding created as Open.</summary>
    NotReviewed,

    /// <summary>XCCDF error/unknown result — flagged for manual review, no finding created.</summary>
    Error
}

// ─── Entities ────────────────────────────────────────────────────────────────

/// <summary>
/// Tracks each file import operation. One record per imported file.
/// See data-model.md §ScanImportRecord for field descriptions.
/// </summary>
[TenantScoped]
public class ScanImportRecord
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to <see cref="RegisteredSystem"/> this import belongs to.</summary>
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>FK to <see cref="ComplianceAssessment"/> providing context for findings.</summary>
    public string AssessmentId { get; set; } = string.Empty;

    /// <summary>Type of import file (CKL or XCCDF).</summary>
    public ScanImportType ImportType { get; set; }

    /// <summary>Original file name as uploaded.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of raw file content (before base64 encoding).</summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>File size in bytes before base64 encoding.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>STIG benchmark ID (e.g., <c>Windows_Server_2022_STIG</c>).</summary>
    public string? BenchmarkId { get; set; }

    /// <summary>STIG release version.</summary>
    public string? BenchmarkVersion { get; set; }

    /// <summary>STIG benchmark display title.</summary>
    public string? BenchmarkTitle { get; set; }

    /// <summary>Target system hostname from scan.</summary>
    public string? TargetHostName { get; set; }

    /// <summary>Target IP address.</summary>
    public string? TargetIpAddress { get; set; }

    /// <summary>
    /// When the scan was performed (UTC).
    /// XCCDF: from <c>start-time</c> attribute.
    /// CKL: <c>null</c> (CKL format has no scan timestamp; STIG_INFO releaseinfo is the benchmark release date).
    /// </summary>
    public DateTime? ScanTimestamp { get; set; }

    /// <summary>Total rules/VULNs in the file.</summary>
    public int TotalEntries { get; set; }

    /// <summary>Findings with status Open/Fail.</summary>
    public int OpenCount { get; set; }

    /// <summary>Findings with status NotAFinding/Pass.</summary>
    public int PassCount { get; set; }

    /// <summary>Findings with status Not_Applicable.</summary>
    public int NotApplicableCount { get; set; }

    /// <summary>Findings not evaluated (CKL Not_Reviewed / XCCDF notchecked).</summary>
    public int NotReviewedCount { get; set; }

    /// <summary>XCCDF error/unknown results.</summary>
    public int ErrorCount { get; set; }

    /// <summary>Entries skipped due to conflict resolution.</summary>
    public int SkippedCount { get; set; }

    /// <summary>STIG rules not found in curated library.</summary>
    public int UnmatchedCount { get; set; }

    /// <summary>New <see cref="ComplianceFinding"/> records created.</summary>
    public int FindingsCreated { get; set; }

    /// <summary>Existing findings updated (overwrite/merge).</summary>
    public int FindingsUpdated { get; set; }

    /// <summary>New <see cref="ControlEffectiveness"/> records created.</summary>
    public int EffectivenessRecordsCreated { get; set; }

    /// <summary>Existing effectiveness records updated.</summary>
    public int EffectivenessRecordsUpdated { get; set; }

    /// <summary>Unique NIST 800-53 controls touched by this import.</summary>
    public int NistControlsAffected { get; set; }

    /// <summary>Conflict resolution strategy applied during this import.</summary>
    public ImportConflictResolution ConflictResolution { get; set; }

    /// <summary>Whether this was a preview-only import (no DB writes).</summary>
    public bool IsDryRun { get; set; }

    /// <summary>XCCDF compliance score (null for CKL imports).</summary>
    public decimal? XccdfScore { get; set; }

    /// <summary>Final import status.</summary>
    public ScanImportStatus ImportStatus { get; set; }

    /// <summary>Error details if import failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Non-fatal warnings (unmatched rules, baseline mismatches). Stored as JSON column.</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Identity of the user who performed the import.</summary>
    public string ImportedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the import was performed.</summary>
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    // ─── Nessus/ACAS-specific counters (Feature 026) ─────────────────────────

    /// <summary>Count of informational (severity 0) plugins — not persisted as findings.</summary>
    public int? NessusInformationalCount { get; set; }

    /// <summary>Count of critical (severity 4) findings.</summary>
    public int? NessusCriticalCount { get; set; }

    /// <summary>Count of high (severity 3) findings.</summary>
    public int? NessusHighCount { get; set; }

    /// <summary>Count of medium (severity 2) findings.</summary>
    public int? NessusMediumCount { get; set; }

    /// <summary>Count of low (severity 1) findings.</summary>
    public int? NessusLowCount { get; set; }

    /// <summary>Number of distinct hosts scanned in the .nessus file.</summary>
    public int? NessusHostCount { get; set; }

    /// <summary>Number of POA&amp;M weakness entries created during this import.</summary>
    public int? NessusPoamCreatedCount { get; set; }

    /// <summary>Whether the scan was a credentialed (authenticated) scan.</summary>
    public bool? NessusCredentialedScan { get; set; }
}

/// <summary>
/// Per-finding audit trail for each import. Links the raw parsed data
/// to the resulting <see cref="ComplianceFinding"/>.
/// See data-model.md §ScanImportFinding for field descriptions.
/// </summary>
[TenantScoped]
public class ScanImportFinding
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to parent <see cref="ScanImportRecord"/>.</summary>
    public string ScanImportRecordId { get; set; } = string.Empty;

    /// <summary>STIG Vulnerability ID (e.g., <c>V-254239</c>).</summary>
    public string VulnId { get; set; } = string.Empty;

    /// <summary>STIG Rule ID (e.g., <c>SV-254239r849090_rule</c>).</summary>
    public string? RuleId { get; set; }

    /// <summary>Rule version (e.g., <c>WN22-AU-000010</c>).</summary>
    public string? StigVersion { get; set; }

    /// <summary>Original CKL STATUS or XCCDF result value.</summary>
    public string RawStatus { get; set; } = string.Empty;

    /// <summary>Original severity text (<c>high</c>/<c>medium</c>/<c>low</c>).</summary>
    public string RawSeverity { get; set; } = string.Empty;

    /// <summary>Resolved CAT severity (null if severity could not be mapped).</summary>
    public CatSeverity? MappedSeverity { get; set; }

    /// <summary>CKL FINDING_DETAILS or XCCDF message.</summary>
    public string? FindingDetails { get; set; }

    /// <summary>CKL COMMENTS field.</summary>
    public string? Comments { get; set; }

    /// <summary>CKL SEVERITY_OVERRIDE value.</summary>
    public string? SeverityOverride { get; set; }

    /// <summary>CKL SEVERITY_JUSTIFICATION value.</summary>
    public string? SeverityJustification { get; set; }

    /// <summary>Matched <c>StigControl.StigId</c> (null if unmatched).</summary>
    public string? ResolvedStigControlId { get; set; }

    /// <summary>NIST 800-53 control IDs resolved via CCI chain. Stored as JSON column.</summary>
    public List<string> ResolvedNistControlIds { get; set; } = new();

    /// <summary>CCI references from the CKL/XCCDF entry. Stored as JSON column.</summary>
    public List<string> ResolvedCciRefs { get; set; } = new();

    // ─── Prisma Cloud-specific fields (nullable — only populated for Prisma imports) ──

    /// <summary>Prisma alert ID (e.g., <c>P-12345</c>). Used as conflict resolution matching key.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? PrismaAlertId { get; set; }

    /// <summary>Prisma policy UUID from API JSON.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? PrismaPolicyId { get; set; }

    /// <summary>Policy display name (e.g., "Azure Storage encryption not enabled").</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? PrismaPolicyName { get; set; }

    /// <summary>Full ARM resource ID for Azure resources.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(1000)]
    public string? CloudResourceId { get; set; }

    /// <summary>ARM resource type (e.g., <c>Microsoft.Storage/storageAccounts</c>).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? CloudResourceType { get; set; }

    /// <summary>Cloud region (e.g., <c>eastus</c>, <c>usgovvirginia</c>).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? CloudRegion { get; set; }

    /// <summary>Azure subscription GUID or AWS account ID.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? CloudAccountId { get; set; }

    // ─── Nessus/ACAS-specific fields (Feature 026 — nullable, only populated for Nessus imports) ──

    /// <summary>Nessus plugin ID (e.g., <c>97833</c>).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string? NessusPluginId { get; set; }

    /// <summary>Plugin display name (e.g., <c>MS17-010: EternalBlue</c>).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? NessusPluginName { get; set; }

    /// <summary>Plugin family category (e.g., <c>Windows : Microsoft Bulletins</c>).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? NessusPluginFamily { get; set; }

    /// <summary>Target hostname from the scan.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? NessusHostname { get; set; }

    /// <summary>Target IP address (IPv4/IPv6).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(45)]
    public string? NessusHostIp { get; set; }

    /// <summary>Port number (0 = host-level finding).</summary>
    public int? NessusPort { get; set; }

    /// <summary>Protocol (e.g., <c>tcp</c>, <c>udp</c>, <c>icmp</c>).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(10)]
    public string? NessusProtocol { get; set; }

    /// <summary>Service name (e.g., <c>www</c>, <c>ssh</c>, <c>cifs</c>).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(50)]
    public string? NessusServiceName { get; set; }

    /// <summary>CVSS v3.x base score (0.0–10.0).</summary>
    public double? NessusCvssV3BaseScore { get; set; }

    /// <summary>Full CVSS v3 vector string.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? NessusCvssV3Vector { get; set; }

    /// <summary>CVSS v2 base score (fallback if v3 unavailable).</summary>
    public double? NessusCvssV2BaseScore { get; set; }

    /// <summary>Tenable Vulnerability Priority Rating score.</summary>
    public double? NessusVprScore { get; set; }

    /// <summary>CVE identifiers associated with this plugin. Stored as JSON column.</summary>
    public List<string> NessusCves { get; set; } = new();

    /// <summary>Whether a public exploit is available for this vulnerability.</summary>
    public bool? NessusExploitAvailable { get; set; }

    /// <summary>How the NIST control mapping was derived: <c>StigXref</c> or <c>PluginFamilyHeuristic</c>.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(30)]
    public string? NessusControlMappingSource { get; set; }

    /// <summary>Action taken for this finding during import.</summary>
    public ImportFindingAction ImportAction { get; set; }

    /// <summary>FK to created/updated <see cref="ComplianceFinding"/> (null if skipped/unmatched).</summary>
    public string? ComplianceFindingId { get; set; }
}

// ─── DTOs (Not Persisted) ────────────────────────────────────────────────────

/// <summary>
/// Intermediate DTO from CKL parser — a single VULN entry.
/// Not stored in database.
/// </summary>
/// <param name="VulnId">STIG Vulnerability ID (e.g., <c>V-254239</c>).</param>
/// <param name="RuleId">STIG Rule ID (e.g., <c>SV-254239r849090_rule</c>).</param>
/// <param name="StigVersion">Rule version (e.g., <c>WN22-AU-000010</c>).</param>
/// <param name="RuleTitle">Descriptive title of the STIG rule.</param>
/// <param name="Severity">Severity string: <c>high</c>, <c>medium</c>, or <c>low</c>.</param>
/// <param name="Status">CKL status: <c>Open</c>, <c>NotAFinding</c>, <c>Not_Applicable</c>, <c>Not_Reviewed</c>.</param>
/// <param name="FindingDetails">CKL FINDING_DETAILS field.</param>
/// <param name="Comments">CKL COMMENTS field.</param>
/// <param name="SeverityOverride">CKL SEVERITY_OVERRIDE value.</param>
/// <param name="SeverityJustification">CKL SEVERITY_JUSTIFICATION value.</param>
/// <param name="CciRefs">CCI references from the CKL entry.</param>
/// <param name="GroupTitle">SRG reference (group title).</param>
public record ParsedCklEntry(
    string VulnId,
    string? RuleId,
    string? StigVersion,
    string? RuleTitle,
    string Severity,
    string Status,
    string? FindingDetails,
    string? Comments,
    string? SeverityOverride,
    string? SeverityJustification,
    List<string> CciRefs,
    string? GroupTitle);

/// <summary>
/// Top-level CKL parse result containing asset info, STIG metadata, and all VULN entries.
/// </summary>
/// <param name="Asset">Parsed ASSET section.</param>
/// <param name="StigInfo">Parsed STIG_INFO section.</param>
/// <param name="Entries">All parsed VULN entries.</param>
public record ParsedCklFile(
    CklAssetInfo Asset,
    CklStigInfo StigInfo,
    List<ParsedCklEntry> Entries);

/// <summary>
/// CKL ASSET section — target system identification.
/// </summary>
/// <param name="HostName">Target hostname.</param>
/// <param name="HostIp">Target IP address.</param>
/// <param name="HostFqdn">Fully-qualified domain name.</param>
/// <param name="HostMac">MAC address.</param>
/// <param name="AssetType">Asset type (e.g., <c>Computing</c>).</param>
/// <param name="TargetKey">DISA target key.</param>
public record CklAssetInfo(
    string? HostName,
    string? HostIp,
    string? HostFqdn,
    string? HostMac,
    string? AssetType,
    string? TargetKey);

/// <summary>
/// CKL STIG_INFO section — benchmark metadata.
/// </summary>
/// <param name="StigId">Benchmark ID (e.g., <c>Windows_Server_2022_STIG</c>).</param>
/// <param name="Version">STIG release version.</param>
/// <param name="ReleaseInfo">Release info string (benchmark release date, not scan date).</param>
/// <param name="Title">Benchmark display title.</param>
public record CklStigInfo(
    string? StigId,
    string? Version,
    string? ReleaseInfo,
    string? Title);

/// <summary>
/// Intermediate DTO from XCCDF parser — a single rule-result.
/// Not stored in database.
/// </summary>
/// <param name="RuleIdRef">Full XCCDF idref string.</param>
/// <param name="ExtractedRuleId">Extracted <c>SV-XXXXX</c> portion from the XCCDF idref.</param>
/// <param name="Result">XCCDF result: <c>pass</c>, <c>fail</c>, <c>error</c>, <c>notapplicable</c>, <c>notchecked</c>, etc.</param>
/// <param name="Severity">Severity string from the rule-result.</param>
/// <param name="Weight">Rule weight.</param>
/// <param name="Timestamp">Result timestamp (if available).</param>
/// <param name="Message">XCCDF message element content.</param>
/// <param name="CheckRef">OVAL check reference.</param>
public record ParsedXccdfResult(
    string RuleIdRef,
    string ExtractedRuleId,
    string Result,
    string Severity,
    decimal Weight,
    DateTime? Timestamp,
    string? Message,
    string? CheckRef);

/// <summary>
/// Top-level XCCDF parse result containing benchmark/target info, scores, and all rule-results.
/// </summary>
/// <param name="BenchmarkHref">Benchmark reference URI.</param>
/// <param name="Title">Test result title.</param>
/// <param name="Target">Target system identifier.</param>
/// <param name="TargetAddress">Target IP or hostname.</param>
/// <param name="StartTime">Scan start time (UTC).</param>
/// <param name="EndTime">Scan end time (UTC).</param>
/// <param name="Score">Achieved compliance score.</param>
/// <param name="MaxScore">Maximum possible score.</param>
/// <param name="TargetFacts">Additional target facts (OS, FQDN, etc.).</param>
/// <param name="Results">All parsed rule-results.</param>
public record ParsedXccdfFile(
    string? BenchmarkHref,
    string? Title,
    string? Target,
    string? TargetAddress,
    DateTime? StartTime,
    DateTime? EndTime,
    decimal? Score,
    decimal? MaxScore,
    Dictionary<string, string> TargetFacts,
    List<ParsedXccdfResult> Results);

/// <summary>
/// Return value from import operations. Contains all counts, warnings, and unmatched rule details.
/// </summary>
/// <param name="ImportRecordId">ID of the created <see cref="ScanImportRecord"/>.</param>
/// <param name="Status">Final import status.</param>
/// <param name="BenchmarkId">Detected benchmark ID.</param>
/// <param name="BenchmarkTitle">Detected benchmark title.</param>
/// <param name="TotalEntries">Total rules/VULNs in the file.</param>
/// <param name="OpenCount">Findings with status Open/Fail.</param>
/// <param name="PassCount">Findings with status NotAFinding/Pass.</param>
/// <param name="NotApplicableCount">Findings marked Not_Applicable.</param>
/// <param name="NotReviewedCount">Findings not evaluated.</param>
/// <param name="ErrorCount">XCCDF error/unknown results.</param>
/// <param name="SkippedCount">Entries skipped due to conflict resolution.</param>
/// <param name="UnmatchedCount">STIG rules not found in curated library.</param>
/// <param name="FindingsCreated">New ComplianceFinding records created.</param>
/// <param name="FindingsUpdated">Existing findings updated.</param>
/// <param name="EffectivenessRecordsCreated">New ControlEffectiveness records created.</param>
/// <param name="EffectivenessRecordsUpdated">Existing effectiveness records updated.</param>
/// <param name="NistControlsAffected">Unique NIST controls touched.</param>
/// <param name="Warnings">Non-fatal warning messages.</param>
/// <param name="UnmatchedRules">Details of unmatched STIG rules.</param>
/// <param name="ErrorMessage">Error details if import failed.</param>
public record ImportResult(
    string ImportRecordId,
    ScanImportStatus Status,
    string BenchmarkId,
    string? BenchmarkTitle,
    int TotalEntries,
    int OpenCount,
    int PassCount,
    int NotApplicableCount,
    int NotReviewedCount,
    int ErrorCount,
    int SkippedCount,
    int UnmatchedCount,
    int FindingsCreated,
    int FindingsUpdated,
    int EffectivenessRecordsCreated,
    int EffectivenessRecordsUpdated,
    int NistControlsAffected,
    List<string> Warnings,
    List<UnmatchedRuleInfo> UnmatchedRules,
    string? ErrorMessage);

/// <summary>
/// Details of a STIG rule that could not be matched to the curated library.
/// </summary>
/// <param name="VulnId">STIG Vulnerability ID.</param>
/// <param name="RuleId">STIG Rule ID (if available).</param>
/// <param name="RuleTitle">Rule title (if available).</param>
/// <param name="Severity">Severity string from the source file.</param>
public record UnmatchedRuleInfo(
    string VulnId,
    string? RuleId,
    string? RuleTitle,
    string Severity);

// ─── Prisma Cloud DTOs (Not Persisted) ───────────────────────────────────────

/// <summary>
/// State change history entry from Prisma Cloud API JSON <c>history[]</c> array.
/// </summary>
/// <param name="ModifiedBy">Who made the change (e.g., "System", user email).</param>
/// <param name="ModifiedOn">When the change occurred (UTC, converted from Unix epoch ms).</param>
/// <param name="Reason">Change reason (e.g., "NEW_ALERT", "RESOURCE_UPDATED", "DISMISSED").</param>
/// <param name="Status">Alert status after change (<c>open</c>, <c>resolved</c>, <c>dismissed</c>).</param>
public record PrismaAlertHistoryEntry(
    string ModifiedBy,
    DateTime ModifiedOn,
    string Reason,
    string Status);

/// <summary>
/// Intermediate DTO representing a single consolidated Prisma alert after CSV/JSON parsing.
/// One alert may map to multiple NIST controls (via <see cref="NistControlIds"/>).
/// Not stored in database.
/// </summary>
/// <param name="AlertId">Prisma alert ID (grouping key for CSV multi-row alerts).</param>
/// <param name="Status">Alert status: <c>open</c>, <c>resolved</c>, <c>dismissed</c>, <c>snoozed</c>.</param>
/// <param name="PolicyName">Policy display name.</param>
/// <param name="PolicyType">Policy classification: <c>config</c>, <c>network</c>, <c>audit_event</c>, <c>anomaly</c>.</param>
/// <param name="Severity">Prisma severity: <c>critical</c>, <c>high</c>, <c>medium</c>, <c>low</c>, <c>informational</c>.</param>
/// <param name="CloudType">Cloud provider: <c>azure</c>, <c>aws</c>, <c>gcp</c>.</param>
/// <param name="AccountName">Cloud account display name.</param>
/// <param name="AccountId">Azure subscription GUID or AWS/GCP account ID.</param>
/// <param name="Region">Cloud region.</param>
/// <param name="ResourceName">Resource display name.</param>
/// <param name="ResourceId">Full ARM resource ID (Azure) or ARN (AWS).</param>
/// <param name="ResourceType">ARM resource type (Azure) or AWS resource type.</param>
/// <param name="AlertTime">When the alert was first triggered (UTC).</param>
/// <param name="ResolutionReason">Reason for resolution/dismissal (null if still open).</param>
/// <param name="ResolutionTime">When resolved/dismissed (UTC).</param>
/// <param name="NistControlIds">Extracted NIST 800-53 control IDs (e.g., ["SC-28", "SC-12"]).</param>
/// <param name="Description">Full policy description (API JSON only).</param>
/// <param name="Recommendation">Remediation guidance (API JSON only).</param>
/// <param name="RemediationScript">CLI script template (API JSON only).</param>
/// <param name="PolicyLabels">Policy classification tags (API JSON only).</param>
/// <param name="Remediable">Whether auto-remediation is possible (API JSON only, default false for CSV).</param>
/// <param name="AlertHistory">State change history (API JSON only).</param>
public record ParsedPrismaAlert(
    string AlertId,
    string Status,
    string PolicyName,
    string PolicyType,
    string Severity,
    string CloudType,
    string AccountName,
    string AccountId,
    string Region,
    string ResourceName,
    string ResourceId,
    string ResourceType,
    DateTime AlertTime,
    string? ResolutionReason,
    DateTime? ResolutionTime,
    List<string> NistControlIds,
    string? Description = null,
    string? Recommendation = null,
    string? RemediationScript = null,
    List<string>? PolicyLabels = null,
    bool Remediable = false,
    List<PrismaAlertHistoryEntry>? AlertHistory = null);

/// <summary>
/// Container DTO for a fully parsed Prisma import file (CSV or API JSON).
/// Not stored in database.
/// </summary>
/// <param name="SourceType"><see cref="ScanImportType.PrismaCsv"/> or <see cref="ScanImportType.PrismaApi"/>.</param>
/// <param name="Alerts">All parsed alerts (grouped by Alert ID for CSV).</param>
/// <param name="TotalAlerts">Total unique alert count after grouping.</param>
/// <param name="TotalRows">Total CSV rows or JSON objects before grouping.</param>
/// <param name="AccountIds">Unique cloud account IDs found in the file.</param>
public record ParsedPrismaFile(
    ScanImportType SourceType,
    List<ParsedPrismaAlert> Alerts,
    int TotalAlerts,
    int TotalRows,
    List<string> AccountIds);

// ─── Prisma Trend Analysis DTOs ──────────────────────────────────────────────

/// <summary>
/// Individual import snapshot within a trend analysis.
/// </summary>
/// <param name="ImportId"><see cref="ScanImportRecord"/> ID.</param>
/// <param name="ImportedAt">When the import was performed.</param>
/// <param name="FileName">Original file name.</param>
/// <param name="TotalAlerts">Total alerts in this import.</param>
/// <param name="OpenCount">Open findings.</param>
/// <param name="ResolvedCount">Resolved findings.</param>
/// <param name="DismissedCount">Dismissed findings.</param>
public record PrismaTrendImport(
    string ImportId,
    DateTime ImportedAt,
    string FileName,
    int TotalAlerts,
    int OpenCount,
    int ResolvedCount,
    int DismissedCount);

/// <summary>
/// Trend analysis output comparing findings across scan imports for a system.
/// </summary>
/// <param name="SystemId"><see cref="RegisteredSystem"/> ID.</param>
/// <param name="Imports">Import snapshots with date and counts.</param>
/// <param name="NewFindings">Findings in latest import not in any previous.</param>
/// <param name="ResolvedFindings">Findings in previous imports now resolved/missing.</param>
/// <param name="PersistentFindings">Findings present in both latest and previous imports.</param>
/// <param name="RemediationRate">Percentage of resolved findings: resolved / (resolved + persistent).</param>
/// <param name="ResourceTypeBreakdown">Finding count by ARM resource type (optional, via <c>group_by</c>).</param>
/// <param name="NistControlBreakdown">Finding count by NIST control ID (optional, via <c>group_by</c>).</param>
public record PrismaTrendResult(
    string SystemId,
    List<PrismaTrendImport> Imports,
    int NewFindings,
    int ResolvedFindings,
    int PersistentFindings,
    decimal RemediationRate,
    Dictionary<string, int>? ResourceTypeBreakdown,
    Dictionary<string, int>? NistControlBreakdown);

// ─── Prisma Import Result DTOs ───────────────────────────────────────────────

/// <summary>
/// Per-system import result within a Prisma import operation.
/// </summary>
/// <param name="ImportRecordId">ID of the created <see cref="ScanImportRecord"/>.</param>
/// <param name="SystemId">Resolved RegisteredSystem ID.</param>
/// <param name="SystemName">Resolved system display name.</param>
/// <param name="Status">Final import status.</param>
/// <param name="TotalAlerts">Total alerts processed for this system.</param>
/// <param name="OpenCount">Open alerts.</param>
/// <param name="ResolvedCount">Resolved alerts.</param>
/// <param name="DismissedCount">Dismissed alerts.</param>
/// <param name="SnoozedCount">Snoozed alerts (treated as open).</param>
/// <param name="FindingsCreated">New ComplianceFinding records created.</param>
/// <param name="FindingsUpdated">Existing findings updated.</param>
/// <param name="SkippedCount">Findings skipped by conflict resolution.</param>
/// <param name="UnmappedPolicies">Policies with no NIST 800-53 mapping.</param>
/// <param name="EffectivenessRecordsCreated">New ControlEffectiveness records.</param>
/// <param name="EffectivenessRecordsUpdated">Updated ControlEffectiveness records.</param>
/// <param name="NistControlsAffected">Unique NIST controls touched.</param>
/// <param name="EvidenceCreated">Whether evidence was created.</param>
/// <param name="FileHash">SHA-256 hash of import file.</param>
/// <param name="IsDryRun">Whether this was a dry-run preview.</param>
/// <param name="Warnings">Non-fatal warning messages.</param>
/// <param name="RemediableCount">Count of alerts with remediable policies (API JSON only).</param>
/// <param name="CliScriptsExtracted">Count of CLI scripts extracted (API JSON only).</param>
/// <param name="PolicyLabelsFound">Unique policy labels found (API JSON only).</param>
/// <param name="AlertsWithHistory">Count of alerts with history data (API JSON only).</param>
public record PrismaSystemImportResult(
    string ImportRecordId,
    string SystemId,
    string SystemName,
    ScanImportStatus Status,
    int TotalAlerts,
    int OpenCount,
    int ResolvedCount,
    int DismissedCount,
    int SnoozedCount,
    int FindingsCreated,
    int FindingsUpdated,
    int SkippedCount,
    int UnmappedPolicies,
    int EffectivenessRecordsCreated,
    int EffectivenessRecordsUpdated,
    int NistControlsAffected,
    bool EvidenceCreated,
    string FileHash,
    bool IsDryRun,
    List<string> Warnings,
    int RemediableCount = 0,
    int CliScriptsExtracted = 0,
    List<string>? PolicyLabelsFound = null,
    int AlertsWithHistory = 0);

/// <summary>
/// Info about an unresolved Azure subscription encountered during Prisma import.
/// </summary>
/// <param name="AccountId">Azure subscription GUID.</param>
/// <param name="AccountName">Account display name from the CSV/JSON.</param>
/// <param name="AlertCount">Number of alerts for this subscription.</param>
/// <param name="Message">Actionable message for the user.</param>
public record UnresolvedSubscriptionInfo(
    string AccountId,
    string AccountName,
    int AlertCount,
    string Message);

/// <summary>
/// Summary of non-Azure alerts that were skipped during auto-resolution.
/// </summary>
/// <param name="Count">Total skipped alerts.</param>
/// <param name="CloudTypes">Unique non-Azure cloud types encountered.</param>
/// <param name="Message">Actionable message for the user.</param>
public record SkippedNonAzureInfo(
    int Count,
    List<string> CloudTypes,
    string Message);

/// <summary>
/// Top-level result from a Prisma import operation. May contain multiple per-system results
/// when a multi-subscription CSV is auto-split.
/// </summary>
/// <param name="Imports">Per-system import results.</param>
/// <param name="UnresolvedSubscriptions">Subscriptions that could not be resolved.</param>
/// <param name="SkippedNonAzure">Non-Azure alerts skipped during auto-resolution.</param>
/// <param name="TotalProcessed">Total alerts processed across all systems.</param>
/// <param name="TotalSkipped">Total alerts skipped.</param>
/// <param name="DurationMs">Total import duration in milliseconds.</param>
/// <param name="ErrorMessage">Error details if import failed entirely.</param>
public record PrismaImportResult(
    List<PrismaSystemImportResult> Imports,
    List<UnresolvedSubscriptionInfo> UnresolvedSubscriptions,
    SkippedNonAzureInfo? SkippedNonAzure,
    int TotalProcessed,
    int TotalSkipped,
    long DurationMs,
    string? ErrorMessage = null);

/// <summary>
/// Individual policy entry in the Prisma policy catalog.
/// </summary>
/// <param name="PolicyName">Unique policy name.</param>
/// <param name="PolicyType">Policy classification.</param>
/// <param name="Severity">Highest observed severity.</param>
/// <param name="NistControlIds">NIST controls this policy maps to.</param>
/// <param name="OpenCount">Open finding count.</param>
/// <param name="ResolvedCount">Resolved finding count.</param>
/// <param name="DismissedCount">Dismissed finding count.</param>
/// <param name="AffectedResourceTypes">Resource types affected by this policy.</param>
/// <param name="LastSeenImportId">Most recent import that included this policy.</param>
/// <param name="LastSeenAt">When this policy was last seen.</param>
public record PrismaPolicyEntry(
    string PolicyName,
    string PolicyType,
    string Severity,
    List<string> NistControlIds,
    int OpenCount,
    int ResolvedCount,
    int DismissedCount,
    List<string> AffectedResourceTypes,
    string LastSeenImportId,
    DateTime LastSeenAt);

/// <summary>
/// Result of querying the Prisma policy catalog for a system.
/// </summary>
/// <param name="SystemId">RegisteredSystem ID.</param>
/// <param name="TotalPolicies">Total unique policies.</param>
/// <param name="Policies">Policy entries with NIST mappings and counts.</param>
public record PrismaPolicyListResult(
    string SystemId,
    int TotalPolicies,
    List<PrismaPolicyEntry> Policies);

// =============================================================================
//  CweNistMappings.cs
//  Ato.Copilot.Agents — Compliance / Services / ScanImport
//  Issue #422 — SarifParserService (Phase 4 spec, ready for use in Phase 2+)
//
//  Static CWE → NIST 800-53 Rev.5 mapping table (39 CWEs).
//  Source: Oracle spec document, Issue #422 Comment 3/3.
// =============================================================================

namespace Ato.Copilot.Agents.Compliance.Services.ScanImport;

/// <summary>
/// Static CWE → NIST 800-53 Rev.5 primary control mapping table.
/// <para>
/// Each entry lists the primary control(s) for the weakness, ordered by
/// directness of coverage. Multi-control entries produce N ComplianceFinding
/// rows — one per control — all sharing the same fingerprint, ruleId, location,
/// and CatSeverity.
/// </para>
/// <para>
/// Source: Oracle SARIF Mapping Strategy, Issue #422 Comment 3/3 (2026-06-19).
/// </para>
/// </summary>
internal static class CweNistMappings
{
    /// <summary>
    /// Lookup table: CWE ID (case-insensitive) → NIST 800-53 Rev.5 control IDs.
    /// Returns primary + secondary controls — ordered from most direct to supporting coverage.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Map =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["CWE-20"]   = ["SI-10", "SI-3",  "CM-6"],
        ["CWE-22"]   = ["AC-3",  "SI-10", "SC-28", "CM-6"],
        ["CWE-78"]   = ["SI-10", "CM-6",  "AC-2"],
        ["CWE-79"]   = ["SI-10", "CM-6"],
        ["CWE-89"]   = ["SI-10", "SC-28", "CM-6"],
        ["CWE-94"]   = ["SI-10", "CM-6",  "SI-3"],
        ["CWE-119"]  = ["SI-16", "CM-6",  "SI-3"],
        ["CWE-200"]  = ["AC-3",  "SC-28", "AU-9",  "AC-2"],
        ["CWE-259"]  = ["IA-5",  "CM-6",  "IA-2"],
        ["CWE-269"]  = ["AC-2",  "AC-6"],
        ["CWE-276"]  = ["AC-3",  "CM-6",  "AC-2"],
        ["CWE-284"]  = ["AC-2",  "AC-3",  "SC-7"],
        ["CWE-285"]  = ["AC-3",  "AC-2",  "IA-2"],
        ["CWE-287"]  = ["IA-2",  "IA-5",  "CM-6"],
        ["CWE-295"]  = ["SC-17", "IA-5",  "SC-7"],
        ["CWE-306"]  = ["IA-2",  "AC-2"],
        ["CWE-310"]  = ["SC-28", "SC-13", "IA-5"],
        ["CWE-311"]  = ["SC-28", "SC-8",  "SC-13"],
        ["CWE-312"]  = ["SC-28", "SC-13", "IA-5"],
        ["CWE-326"]  = ["SC-28", "SC-13", "IA-5"],
        ["CWE-327"]  = ["SC-13", "SC-28", "IA-5",  "CM-6"],
        ["CWE-330"]  = ["SC-13", "IA-5",  "CM-6"],
        ["CWE-352"]  = ["SC-8",  "IA-2",  "SC-7"],
        ["CWE-400"]  = ["SC-5",  "CM-6",  "SC-7"],
        ["CWE-434"]  = ["SI-3",  "CM-6",  "SC-28"],
        ["CWE-489"]  = ["CM-6",  "AC-2",  "AU-2"],
        ["CWE-502"]  = ["SI-10", "CM-6",  "SI-3",  "SC-28"],
        ["CWE-521"]  = ["IA-5",  "IA-2",  "CM-6"],
        ["CWE-532"]  = ["AU-9",  "AU-2",  "SC-28"],
        ["CWE-601"]  = ["SI-10", "SC-7",  "CM-6"],
        ["CWE-611"]  = ["SI-10", "SC-7",  "CM-6",  "SC-28"],
        ["CWE-639"]  = ["AC-3",  "AC-2",  "IA-2",  "AU-2"],
        ["CWE-676"]  = ["CM-6",  "SI-3",  "SI-2"],
        ["CWE-732"]  = ["AC-3",  "CM-6",  "AC-2",  "SC-28"],
        ["CWE-798"]  = ["IA-5",  "IA-2",  "CM-6"],
        ["CWE-862"]  = ["AC-3",  "AC-2",  "IA-2"],
        ["CWE-863"]  = ["AC-3",  "AC-2",  "IA-2",  "AU-2"],
        ["CWE-918"]  = ["SC-7",  "AC-17", "CM-6"],
        ["CWE-1004"] = ["SC-28", "IA-5",  "SC-8",  "CM-6"],
    };

    /// <summary>
    /// OWASP Top 10 (A01–A10) to primary CWE references for Semgrep owasp.A{N} pattern resolution.
    /// Maps OWASP category → CWE IDs → then resolve through <see cref="Map"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> OwaspToCwe =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["A01"] = ["CWE-284", "CWE-285", "CWE-639"],  // Broken Access Control
        ["A02"] = ["CWE-311", "CWE-312", "CWE-327"],  // Crypto Failures
        ["A03"] = ["CWE-79",  "CWE-89",  "CWE-78"],   // Injection
        ["A05"] = ["CWE-276"],                          // Security Misconfiguration
        ["A06"] = ["CWE-1035"],                         // Vulnerable Components (fallback: SI-2)
        ["A07"] = ["CWE-287", "CWE-306"],               // Auth Failures
        ["A08"] = ["CWE-502"],                          // Software Integrity
        ["A09"] = ["CWE-532"],                          // Logging Failures
        ["A10"] = ["CWE-918"],                          // SSRF
    };

    /// <summary>
    /// CodeQL rule-name keyword → CWE ID mapping for rule-name segment heuristic.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CodeQlKeywordToCwe =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["sql-injection"]         = "CWE-89",
        ["sqli"]                  = "CWE-89",
        ["xss"]                   = "CWE-79",
        ["cross-site-scripting"]  = "CWE-79",
        ["command-injection"]     = "CWE-78",
        ["path-injection"]        = "CWE-22",
        ["path-traversal"]        = "CWE-22",
        ["ssrf"]                  = "CWE-918",
        ["xxe"]                   = "CWE-611",
        ["csrf"]                  = "CWE-352",
        ["deserialization"]       = "CWE-502",
        ["hard-coded"]            = "CWE-798",
        ["hardcoded"]             = "CWE-798",
        ["credentials"]           = "CWE-798",
        ["cleartext"]             = "CWE-312",
        ["unencrypted"]           = "CWE-312",
        ["weak-crypto"]           = "CWE-327",
        ["insecure-algorithm"]    = "CWE-327",
        ["insecure-randomness"]   = "CWE-330",
        ["redirect"]              = "CWE-601",
        ["open-redirect"]         = "CWE-601",
        ["idor"]                  = "CWE-639",
        ["insecure-direct"]       = "CWE-639",
        ["missing-auth"]          = "CWE-306",
        ["improper-auth"]         = "CWE-287",
        ["upload"]                = "CWE-434",
        ["log-injection"]         = "CWE-532",
        ["sensitive-log"]         = "CWE-532",
        ["information-disclosure"]= "CWE-200",
        ["privilege-escalation"]  = "CWE-269",
        ["buffer-overflow"]       = "CWE-119",
    };

    /// <summary>
    /// Checkov provider → default NIST control IDs (Tier 3 heuristic when Tier 1/2 absent).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> CheckovProviderDefaults =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["AZURE"]     = ["CM-6", "SC-7",  "SC-28"],
        ["K8S"]       = ["CM-6", "SC-7",  "AC-2"],
        ["DOCKER"]    = ["CM-6", "SI-3"],
        ["GCP"]       = ["CM-6", "SC-7"],
        ["TERRAFORM"]  = ["CM-6"],
    };

    /// <summary>
    /// Checkov rule-ID overrides for specific high-confidence rules.
    /// Key: uppercase rule ID prefix (e.g., "CKV_AZURE_36").
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> CheckovRuleOverrides =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["CKV_AZURE_36"]  = ["SC-7"],
        ["CKV_AZURE_39"]  = ["SC-7"],
        ["CKV_AZURE_5"]   = ["IA-2"],
        ["CKV_AZURE_8"]   = ["IA-2"],
        ["CKV_AZURE_13"]  = ["SC-28"],
        ["CKV_AZURE_16"]  = ["SC-28"],
        ["CKV_AZURE_41"]  = ["AU-2", "AU-9"],
        ["CKV_AZURE_42"]  = ["AU-2", "AU-9"],
    };

    /// <summary>
    /// tfsec ID-prefix → default NIST control IDs.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> TfsecPrefixDefaults =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["azure-network-"]             = ["SC-7",  "CM-6"],
        ["azure-storage-"]             = ["SC-28", "CM-6"],
        ["azure-keyvault-"]            = ["IA-5",  "SC-28"],
        ["azure-monitor-"]             = ["AU-2",  "AU-9"],
        ["azure-security-center-"]     = ["AU-2",  "AU-9"],
        ["azure-authorization-"]       = ["AC-2",  "IA-2"],
        ["azure-active-directory-"]    = ["AC-2",  "IA-2"],
        ["kubernetes-"]                = ["CM-6",  "AC-2",  "SC-7"],
    };

    /// <summary>
    /// Microsoft Defender for Cloud category → default NIST control IDs.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> DefenderCategoryDefaults =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["StorageAccounts"]  = ["SC-28"],
        ["VirtualMachines"]  = ["CM-6", "SI-2"],
        ["Identity"]         = ["IA-2", "AC-2"],
        ["Network"]          = ["SC-7", "AC-17"],
        ["KeyVaults"]        = ["IA-5", "AU-2"],
        ["Monitoring"]       = ["AU-2", "AU-9"],
    };

    /// <summary>
    /// OWASP ZAP numeric rule ID → NIST control IDs.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ZapRuleOverrides =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["10010"] = ["SC-28", "IA-5"],
        ["10038"] = ["CM-6",  "SI-10"],
        ["10055"] = ["CM-6"],
        ["40012"] = ["IA-2",  "SC-8"],
        ["40014"] = ["SI-10", "SC-28"],
        ["40016"] = ["SI-10"],
        ["40017"] = ["SI-10"],
        ["40034"] = ["IA-5",  "CM-6"],
        ["90023"] = ["SC-7"],
        ["90034"] = ["SC-7",  "AC-17"],
    };
}

// =============================================================================
//  SarifTagPatterns.cs
//  Ato.Copilot.Agents — Compliance / Services / ScanImport
//  Issue #422 — SarifParserService (Phase 4 spec, patterns ready for Phase 2+)
//
//  Compiled Regex patterns for SARIF tag → NIST 800-53 control extraction.
//  All patterns in RegexOptions.Compiled | CultureInvariant for high-throughput use.
// =============================================================================

using System.Text.RegularExpressions;

namespace Ato.Copilot.Agents.Compliance.Services.ScanImport;

/// <summary>
/// Compiled regex patterns for SARIF Tier 2 tag-based NIST control extraction.
/// <para>
/// All patterns are applied against each string in <c>rule.properties["tags"]</c>.
/// First match wins per tag. Apply in order: NistDotted → NistColon → ControlColon
/// → Dashed800 → NistDefenderFormat → DirectId → CweInTag.
/// </para>
/// <para>
/// Source: Oracle SARIF Mapping Strategy, Issue #422 Comment 3/3 §3 (2026-06-19).
/// </para>
/// </summary>
internal static partial class SarifTagPatterns
{
    // NIST.SP.800-53.AC-2  or  NIST.SP.800-53.Rev.5.SI-10(1)
    [GeneratedRegex(
        @"NIST\.SP\.800-53\.(?:Rev\.\d+\.)?([A-Z]{2}-\d+(?:\(\d+\))?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    public static partial Regex NistDotted();

    // nist:AC-2  or  NIST:SC-7(1)
    [GeneratedRegex(
        @"(?:nist|NIST):([A-Z]{2}-\d+(?:\(\d+\))?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    public static partial Regex NistColon();

    // control:CM-6  or  ctrl:IA-5
    [GeneratedRegex(
        @"(?:control|ctrl):([A-Z]{2}-\d+(?:\(\d+\))?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    public static partial Regex ControlColon();

    // 800-53:SC-28
    [GeneratedRegex(
        @"800-53:([A-Z]{2}-\d+(?:\(\d+\))?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    public static partial Regex Dashed800();

    // Bare control ID as entire tag: "SC-28", "IA-5(1)"
    [GeneratedRegex(
        @"^([A-Z]{2}-\d+(?:\(\d+\))?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    public static partial Regex DirectId();

    // CWE-79 anywhere in a string (for CWE extraction from tags)
    [GeneratedRegex(
        @"CWE-(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    public static partial Regex CweInTag();

    // Microsoft Defender for Cloud tag format: "NIST SP 800-53 R4: AC-2"
    [GeneratedRegex(
        @"NIST SP 800-53 R[45]:\s*([A-Z]{2}-\d+(?:\(\d+\))?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    public static partial Regex NistDefenderFormat();

    // ─────────────────────────────────────────────────────────────────────────
    //  Tier 1 — rule.properties["nist"] explicit array
    // ─────────────────────────────────────────────────────────────────────────

    // Validates a single NIST control ID: AC-2, SI-10(1), etc.
    [GeneratedRegex(
        @"^[A-Z]{2}-\d+(?:\(\d+\))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    public static partial Regex NistControlId();

    // ─────────────────────────────────────────────────────────────────────────
    //  Tier 3 — CWE from rule.id
    // ─────────────────────────────────────────────────────────────────────────

    // CWE-{n} anywhere in a rule ID string
    [GeneratedRegex(
        @"CWE-(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    public static partial Regex CweInRuleId();

    // OWASP A-number in Semgrep rule IDs: "owasp.A01", "owasp.A07"
    [GeneratedRegex(
        @"owasp\.?(A\d{2})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    public static partial Regex OwaspInTag();

    // ─────────────────────────────────────────────────────────────────────────
    //  Normalization helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes a raw NIST control ID candidate to standard form:
    /// uppercase, no trailing revision qualifiers, spacing normalized in enhancements.
    /// Examples: "ac-2" → "AC-2"; "SI-2 (2)" → "SI-2(2)"; "AC-2.Rev.5" → "AC-2"
    /// </summary>
    public static string? NormalizeControlId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Uppercase
        var normalized = raw.Trim().ToUpperInvariant();

        // Remove trailing ".Rev.N" qualifiers
        var dotRev = normalized.IndexOf(".REV.", StringComparison.OrdinalIgnoreCase);
        if (dotRev >= 0) normalized = normalized[..dotRev];

        // Normalize "AC-2 (1)" → "AC-2(1)"
        normalized = normalized.Replace(" (", "(");

        // Final validation
        return NistControlId().IsMatch(normalized) ? normalized : null;
    }

    /// <summary>
    /// tfsec keyword refinements: maps keyword fragments in rule IDs to additional control IDs.
    /// Applied after provider-prefix defaults to add specificity.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> TfsecKeywordRefinements =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["encryption"]  = ["SC-28"],
        ["encrypted"]   = ["SC-28"],
        ["cmk"]         = ["SC-28"],
        ["auth"]        = ["IA-2"],
        ["identity"]    = ["IA-2"],
        ["password"]    = ["IA-5"],
        ["key"]         = ["IA-5"],
        ["secret"]      = ["IA-5"],
        ["logging"]     = ["AU-2"],
        ["audit"]       = ["AU-2"],
        ["ingress"]     = ["SC-7"],
        ["firewall"]    = ["SC-7"],
        ["public"]      = ["SC-7"],
        ["remote"]      = ["AC-17"],
        ["vpn"]         = ["AC-17"],
        ["bastion"]     = ["AC-17"],
    };
}

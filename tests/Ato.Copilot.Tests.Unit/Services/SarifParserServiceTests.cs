// =============================================================================
//  SarifParserServiceTests.cs
//  Ato.Copilot.Tests.Unit — Services
//  Issue #422 — AO Posture API (W10 cATO Gap Closure)
//
//  Tests the 3-tier CWE→NIST mapping, CAT severity derivation, deduplication,
//  unmapped rule handling, and multi-control expansion (Oracle spec §9.5).
// =============================================================================

using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="SarifParserService"/> — covering the 3-tier CWE→NIST mapping,
/// CAT severity derivation edge cases, fingerprint dedup, unmapped rule accumulation,
/// and multi-control expansion.
/// </summary>
public sealed class SarifParserServiceTests
{
    private static readonly Guid SystemId = Guid.Parse("b1b2c3d4-0000-0000-0000-000000000002");
    private readonly SarifParserService _sut;

    public SarifParserServiceTests()
    {
        _sut = new SarifParserService(NullLogger<SarifParserService>.Instance);
    }

    // ─── Version enforcement ─────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_WrongSarifVersion_ThrowsPayloadValidationException()
    {
        var doc = BuildSarifDoc(version: "2.0.0", runs: []);
        var act = async () => await _sut.ParseAsync(doc, SystemId, "pipe1", "run1");
        await act.Should().ThrowAsync<PayloadValidationException>()
            .WithMessage("*2.1.0*");
    }

    [Fact]
    public async Task ParseAsync_MissingVersion_ThrowsPayloadValidationException()
    {
        using var doc = JsonDocument.Parse("""{"runs":[]}""");
        var act = async () => await _sut.ParseAsync(doc, SystemId, "pipe1", "run1");
        await act.Should().ThrowAsync<PayloadValidationException>();
    }

    [Fact]
    public async Task ParseAsync_MissingRuns_ThrowsPayloadValidationException()
    {
        using var doc = JsonDocument.Parse("""{"version":"2.1.0"}""");
        var act = async () => await _sut.ParseAsync(doc, SystemId, "pipe1", "run1");
        await act.Should().ThrowAsync<PayloadValidationException>()
            .WithMessage("*runs*");
    }

    // ─── Tier 1: explicit nist property ─────────────────────────────────────

    [Fact]
    public async Task ParseAsync_Tier1_NistArray_MapsToExplicitControls()
    {
        var doc = BuildSarifWithRule(
            ruleId: "T1-RULE",
            nistArray: ["AC-2", "SI-10"],
            tags: null,
            level: "warning",
            securitySeverity: "5.0");

        var result = await _sut.ParseAsync(doc, SystemId, "pipe", "run1");

        result.FindingsImported.Should().Be(2, "one finding per NIST control");
        result.Findings.Should().AllSatisfy(f =>
            f.NistControlIds.Should().HaveCount(1));
        result.Findings.Select(f => f.NistControlIds.First())
            .Should().BeEquivalentTo(["AC-2", "SI-10"]);
    }

    [Fact]
    public async Task ParseAsync_Tier1_MalformedNistEntry_SkipsAndContinues()
    {
        // "invalid-format" should be skipped, "SC-7" should pass
        var doc = BuildSarifWithRule(
            ruleId: "T1-MALFORMED",
            nistArray: ["invalid-format", "SC-7"],
            tags: null,
            level: "warning",
            securitySeverity: "5.0");

        var result = await _sut.ParseAsync(doc, SystemId, "pipe", "run1");

        result.FindingsImported.Should().Be(1);
        result.Findings.Single().NistControlIds.Should().ContainSingle("SC-7");
    }

    // ─── Tier 2: tag pattern matching ────────────────────────────────────────

    [Theory]
    [InlineData("NIST.SP.800-53.AC-2",      "AC-2")]
    [InlineData("NIST.SP.800-53.Rev.5.SI-10", "SI-10")]
    [InlineData("nist:AC-2",                "AC-2")]
    [InlineData("control:CM-6",             "CM-6")]
    [InlineData("ctrl:IA-5",                "IA-5")]
    [InlineData("800-53:SC-7",              "SC-7")]
    [InlineData("AC-2",                     "AC-2")]    // bare control ID tag
    [InlineData("NIST SP 800-53 R4: AC-2",  "AC-2")]    // Defender format
    public async Task ParseAsync_Tier2_TagPattern_ResolvesToExpectedControl(
        string tag, string expectedControl)
    {
        var doc = BuildSarifWithRule(
            ruleId: "T2-RULE",
            nistArray: null,
            tags: [tag],
            level: "warning",
            securitySeverity: "5.0");

        var result = await _sut.ParseAsync(doc, SystemId, "pipe", "run1");

        result.FindingsImported.Should().BeGreaterThan(0, "tag matched a control");
        result.Findings.Should().Contain(f => f.NistControlIds.Contains(expectedControl));
    }

    // ─── Tier 3: CWE lookup ──────────────────────────────────────────────────

    [Theory]
    [InlineData("CWE-89",  "SI-10")]   // SQL Injection → primary
    [InlineData("CWE-79",  "SI-10")]   // XSS → primary
    [InlineData("CWE-287", "IA-2")]    // Improper Authentication → primary
    [InlineData("CWE-259", "IA-5")]    // Hard-coded Password → primary
    [InlineData("CWE-200", "AC-3")]    // Sensitive Info Exposure → primary
    [InlineData("CWE-918", "SC-7")]    // SSRF → primary
    public async Task ParseAsync_Tier3_CweInRuleId_MapsToExpectedPrimaryControl(
        string cweId, string expectedPrimaryControl)
    {
        // Embed CWE in rule ID — triggers Tier 3 extraction
        var doc = BuildSarifWithRule(
            ruleId: cweId,
            nistArray: null,
            tags: null,
            level: "warning",
            securitySeverity: "6.0");

        var result = await _sut.ParseAsync(doc, SystemId, "pipe", "run1");

        result.FindingsImported.Should().BeGreaterThan(0);
        result.Findings.Should().Contain(f =>
            f.NistControlIds.Contains(expectedPrimaryControl),
            $"CWE-mapped rule should produce finding for {expectedPrimaryControl}");
    }

    [Fact]
    public async Task ParseAsync_UnknownCwe_ProducesUnmappedRuleEntry()
    {
        // CWE-9999 is not in the map
        var doc = BuildSarifWithRule(
            ruleId: "CWE-9999",
            nistArray: null,
            tags: null,
            level: "warning",
            securitySeverity: "5.0");

        var result = await _sut.ParseAsync(doc, SystemId, "pipe", "run1");

        result.UnmappedRuleCount.Should().Be(1);
        result.UnmappedRules.Should().ContainSingle(u => u.RuleId == "CWE-9999");
        result.Findings.Should().ContainSingle(f => f.NistControlIds.Count == 0,
            "unmapped rules still produce a finding row (with empty NistControlIds)");
    }

    // ─── CAT severity derivation (Oracle spec §5) ───────────────────────────

    [Theory]
    [InlineData(9.1, "error",   CatSeverity.CatI)]   // P1: CVSS>=9.0 AND error
    [InlineData(9.1, "warning", CatSeverity.CatII)]  // P1 fails (not error); P2: 9.1>=7.0
    [InlineData(0.0, "error",   CatSeverity.CatII)]  // P2: error
    [InlineData(7.5, "warning", CatSeverity.CatII)]  // P2: 7.5>=7.0
    [InlineData(5.0, "note",    CatSeverity.CatIII)] // P3: 5.0>=4.0
    [InlineData(0.0, "warning", CatSeverity.CatIII)] // P3: warning
    public async Task ParseAsync_CatTierDerivation_MatchesOracleSpec(
        double cvss, string level, CatSeverity expectedCat)
    {
        var doc = BuildSarifWithRule(
            ruleId: "SC-7",  // Tier 1: explicit nist
            nistArray: ["SC-7"],
            tags: null,
            level: level,
            securitySeverity: cvss.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));

        var result = await _sut.ParseAsync(doc, SystemId, "pipe", "run1");

        result.Findings.Should().ContainSingle()
            .Which.CatTier.Should().Be(expectedCat);
    }

    [Theory]
    [InlineData("note")]
    [InlineData("none")]
    public async Task ParseAsync_Informational_IsDiscarded(string level)
    {
        var doc = BuildSarifWithRule(
            ruleId: "SC-7",
            nistArray: ["SC-7"],
            tags: null,
            level: level,
            securitySeverity: null); // no score + note/none → discard

        var result = await _sut.ParseAsync(doc, SystemId, "pipe", "run1");

        result.FindingsImported.Should().Be(0,
            "informational results (note/none with no CVSS score) must be discarded");
    }

    [Fact]
    public async Task ParseAsync_LowCvssAndNoteLevel_IsDiscarded()
    {
        // 3.1 CVSS + note → P3 fails (3.1 < 4.0) → discard
        var doc = BuildSarifWithRule(
            ruleId: "SC-7",
            nistArray: ["SC-7"],
            tags: null,
            level: "note",
            securitySeverity: "3.1");

        var result = await _sut.ParseAsync(doc, SystemId, "pipe", "run1");

        result.FindingsImported.Should().Be(0);
    }

    // ─── Deduplication ───────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_ExistingFingerprint_DeduplicatesInstead()
    {
        var doc = BuildSarifWithRule(
            ruleId: "AC-2",
            nistArray: ["AC-2"],
            tags: null,
            level: "error",
            securitySeverity: "7.5",
            fingerprintKey: "primaryLocationLineHash/v1",
            fingerprintValue: "known-fingerprint-abc123");

        // Pre-populate existing fingerprints (simulating DB prefetch)
        var existingFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "known-fingerprint-abc123",
        };

        var result = await _sut.ParseAsync(
            doc, SystemId, "pipe", "run1", existingFingerprints);

        result.FindingsDeduplicated.Should().Be(1,
            "finding with known fingerprint must be counted as deduplicated");
        result.FindingsImported.Should().Be(0);
        result.Findings.Should().ContainSingle(f => f.IsNew == false);
    }

    [Fact]
    public async Task ParseAsync_WithinRunDuplicate_CountedOnce()
    {
        // Two results with the same fingerprint in same run → first is kept, second discarded
        var run = BuildRunJson(
            toolName: "TestTool",
            rules: [BuildRuleJson("AC-2", nistArray: ["AC-2"])],
            results:
            [
                BuildResultJson("AC-2", level: "error", securitySeverity: "8.0", fingerprint: "fp-dup"),
                BuildResultJson("AC-2", level: "error", securitySeverity: "8.0", fingerprint: "fp-dup"),
            ]);

        using var doc = JsonDocument.Parse($$"""{"version":"2.1.0","runs":[{{run}}]}""");
        var result = await _sut.ParseAsync(doc, SystemId, "pipe", "run1");

        result.FindingsImported.Should().Be(1, "within-run duplicate must be deduplicated");
        result.FindingsDeduplicated.Should().Be(1);
    }

    // ─── Multi-control expansion ─────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_MultipleNistControls_ProducesOneFindingPerControl()
    {
        var doc = BuildSarifWithRule(
            ruleId: "MULTI-CTRL",
            nistArray: ["AC-2", "SC-7", "IA-2"],
            tags: null,
            level: "warning",
            securitySeverity: "5.0");

        var result = await _sut.ParseAsync(doc, SystemId, "pipe", "run1");

        result.FindingsImported.Should().Be(3, "one finding row per NIST control");
        result.Findings.Select(f => f.NistControlIds.Single())
            .Should().BeEquivalentTo(["AC-2", "SC-7", "IA-2"]);

        // All siblings share the same fingerprint
        result.Findings.Select(f => f.FingerprintHash).Distinct().Should().HaveCount(1);
    }

    // ─── Unmapped rule handling ───────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_UnmappedRule_DoesNotThrow_AccumulatesInResult()
    {
        var doc = BuildSarifWithRule(
            ruleId: "UNKNOWN-TOOL-RULE-12345",
            nistArray: null,
            tags: null,
            level: "warning",
            securitySeverity: "6.0");

        var result = await _sut.ParseAsync(doc, SystemId, "pipe", "run1");

        result.UnmappedRuleCount.Should().Be(1);
        result.UnmappedRules.Single().RuleId.Should().Be("UNKNOWN-TOOL-RULE-12345");
        result.UnmappedFindingCount.Should().Be(1);
        // Finding still created (with empty NistControlIds) — not discarded
        result.Findings.Should().ContainSingle(f => f.NistControlIds.Count == 0);
    }

    // ─── CweNistMappings — all 39 entries covered by spot-tests ─────────────

    [Theory]
    [InlineData("CWE-20",   "SI-10")]
    [InlineData("CWE-22",   "AC-3")]
    [InlineData("CWE-78",   "SI-10")]
    [InlineData("CWE-94",   "SI-10")]
    [InlineData("CWE-119",  "SI-16")]
    [InlineData("CWE-269",  "AC-2")]
    [InlineData("CWE-276",  "AC-3")]
    [InlineData("CWE-284",  "AC-2")]
    [InlineData("CWE-285",  "AC-3")]
    [InlineData("CWE-295",  "SC-17")]
    [InlineData("CWE-306",  "IA-2")]
    [InlineData("CWE-310",  "SC-28")]
    [InlineData("CWE-311",  "SC-28")]
    [InlineData("CWE-312",  "SC-28")]
    [InlineData("CWE-326",  "SC-28")]
    [InlineData("CWE-327",  "SC-13")]
    [InlineData("CWE-330",  "SC-13")]
    [InlineData("CWE-352",  "SC-8")]
    [InlineData("CWE-400",  "SC-5")]
    [InlineData("CWE-434",  "SI-3")]
    [InlineData("CWE-489",  "CM-6")]
    [InlineData("CWE-502",  "SI-10")]
    [InlineData("CWE-521",  "IA-5")]
    [InlineData("CWE-532",  "AU-9")]
    [InlineData("CWE-601",  "SI-10")]
    [InlineData("CWE-611",  "SI-10")]
    [InlineData("CWE-639",  "AC-3")]
    [InlineData("CWE-676",  "CM-6")]
    [InlineData("CWE-732",  "AC-3")]
    [InlineData("CWE-798",  "IA-5")]
    [InlineData("CWE-862",  "AC-3")]
    [InlineData("CWE-863",  "AC-3")]
    [InlineData("CWE-1004", "SC-28")]
    public void CweNistMappings_AllCwes_ContainExpectedPrimaryControl(
        string cweId, string expectedPrimary)
    {
        CweNistMappings.Map.Should().ContainKey(cweId);
        CweNistMappings.Map[cweId].Should().Contain(expectedPrimary,
            $"{cweId} should map to {expectedPrimary} as primary control");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static JsonDocument BuildSarifWithRule(
        string ruleId,
        string[]? nistArray,
        string[]? tags,
        string level,
        string? securitySeverity,
        string? fingerprintKey = null,
        string? fingerprintValue = null)
    {
        var ruleJson = BuildRuleJson(ruleId, nistArray, tags, securitySeverity);
        var resultJson = BuildResultJson(ruleId, level, securitySeverity,
            fingerprintKey is not null ? fingerprintValue : null, fingerprintKey);

        var runJson = BuildRunJson("TestTool", [ruleJson], [resultJson]);
        return JsonDocument.Parse($$"""{"version":"2.1.0","runs":[{{runJson}}]}""");
    }

    private static string BuildRuleJson(
        string ruleId,
        string[]? nistArray = null,
        string[]? tags = null,
        string? securitySeverity = null)
    {
        var props = new List<string>();
        if (nistArray is not null)
        {
            var arr = string.Join(",", nistArray.Select(n => $"\"{n}\""));
            props.Add($"\"nist\":[{arr}]");
        }
        if (tags is not null)
        {
            var arr = string.Join(",", tags.Select(t => $"\"{t}\""));
            props.Add($"\"tags\":[{arr}]");
        }
        if (securitySeverity is not null)
            props.Add($"\"security-severity\":\"{securitySeverity}\"");

        var propsJson = props.Count > 0 ? $",\"properties\":{{{string.Join(",", props)}}}" : string.Empty;

        return $$"""{"id":"{{ruleId}}","shortDescription":{"text":"Rule {{ruleId}}"}{{propsJson}}}""";
    }

    private static string BuildResultJson(
        string ruleId,
        string level,
        string? securitySeverity = null,
        string? fingerprint = null,
        string? fingerprintKey = null)
    {
        var fpKey = fingerprintKey ?? "primaryLocationLineHash/v1";
        var fpJson = fingerprint is not null
            ? $$""","fingerprints":{"{{fpKey}}":"{{fingerprint}}"}"""
            : string.Empty;

        return $$"""{"ruleId":"{{ruleId}}","level":"{{level}}","message":{"text":"Test message"}{{fpJson}}}""";
    }

    private static string BuildRunJson(
        string toolName,
        string[] rules,
        string[] results)
    {
        var rulesArr = string.Join(",", rules);
        var resultsArr = string.Join(",", results);
        return @"{""tool"":{""driver"":{""name"":""" + toolName
            + @""",""rules"":[" + rulesArr + @"]}},"
            + @"""results"":[" + resultsArr + "]}";
    }

    private static JsonDocument BuildSarifDoc(string version, string[] runs)
    {
        var runsJson = string.Join(",", runs);
        return JsonDocument.Parse($$"""{"version":"{{version}}","runs":[{{runsJson}}]}""");
    }
}

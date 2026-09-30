using Xunit;
using FluentAssertions;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Models;

/// <summary>
/// Unit tests for Feature 017 — SCAP/STIG Viewer Import entities, enums, and DTOs.
/// Validates default values, enum members, and record immutability.
/// </summary>
public class ScanImportModelTests
{
    // ─── ScanImportType Enum ─────────────────────────────────────────────────

    [Fact]
    public void ScanImportType_Should_Have_Ckl_And_Xccdf()
    {
        Enum.GetValues<ScanImportType>().Should().HaveCount(5);
        Enum.IsDefined(ScanImportType.Ckl).Should().BeTrue();
        Enum.IsDefined(ScanImportType.Xccdf).Should().BeTrue();
        Enum.IsDefined(ScanImportType.PrismaCsv).Should().BeTrue();
        Enum.IsDefined(ScanImportType.PrismaApi).Should().BeTrue();
        Enum.IsDefined(ScanImportType.NessusXml).Should().BeTrue();
    }

    // ─── ScanImportStatus Enum ───────────────────────────────────────────────

    [Fact]
    public void ScanImportStatus_Should_Include_Retained_Lifecycle_And_Preserve_Stored_Values()
    {
        // Arrange
        var expected = new[]
        {
            ScanImportStatus.Completed,
            ScanImportStatus.CompletedWithWarnings,
            ScanImportStatus.Failed,
            ScanImportStatus.Queued,
            ScanImportStatus.Processing,
            ScanImportStatus.Cancelled
        };

        // Act
        var statuses = Enum.GetValues<ScanImportStatus>();

        // Assert
        statuses.Should().Equal(expected);
        expected.Select(status => (int)status).Should().Equal(0, 1, 2, 3, 4, 5);
    }

    // ─── ImportConflictResolution Enum ───────────────────────────────────────

    [Fact]
    public void ImportConflictResolution_Should_Have_Three_Strategies()
    {
        Enum.GetValues<ImportConflictResolution>().Should().HaveCount(3);
        Enum.IsDefined(ImportConflictResolution.Skip).Should().BeTrue();
        Enum.IsDefined(ImportConflictResolution.Overwrite).Should().BeTrue();
        Enum.IsDefined(ImportConflictResolution.Merge).Should().BeTrue();
    }

    // ─── ImportFindingAction Enum ────────────────────────────────────────────

    [Fact]
    public void ImportFindingAction_Should_Have_Seven_Values()
    {
        Enum.GetValues<ImportFindingAction>().Should().HaveCount(7);
        Enum.IsDefined(ImportFindingAction.Created).Should().BeTrue();
        Enum.IsDefined(ImportFindingAction.Updated).Should().BeTrue();
        Enum.IsDefined(ImportFindingAction.Skipped).Should().BeTrue();
        Enum.IsDefined(ImportFindingAction.Unmatched).Should().BeTrue();
        Enum.IsDefined(ImportFindingAction.NotApplicable).Should().BeTrue();
        Enum.IsDefined(ImportFindingAction.NotReviewed).Should().BeTrue();
        Enum.IsDefined(ImportFindingAction.Error).Should().BeTrue();
    }

    // ─── ScanImportRecord Entity ─────────────────────────────────────────────

    [Fact]
    public void ScanImportRecord_Should_Have_Generated_Id()
    {
        var record = new ScanImportRecord();
        record.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(record.Id, out _).Should().BeTrue();
    }

    [Fact]
    public void ScanImportRecord_Should_Have_Default_Values()
    {
        var record = new ScanImportRecord();

        record.RegisteredSystemId.Should().Be(string.Empty);
        record.AssessmentId.Should().Be(string.Empty);
        record.FileName.Should().Be(string.Empty);
        record.FileHash.Should().Be(string.Empty);
        record.FileSizeBytes.Should().Be(0);
        record.IsDryRun.Should().BeFalse();
        record.ImportedBy.Should().Be(string.Empty);
        record.Warnings.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ScanImportRecord_Should_Have_Nullable_Optional_Fields()
    {
        var record = new ScanImportRecord();

        record.BenchmarkId.Should().BeNull();
        record.BenchmarkVersion.Should().BeNull();
        record.BenchmarkTitle.Should().BeNull();
        record.TargetHostName.Should().BeNull();
        record.TargetIpAddress.Should().BeNull();
        record.ScanTimestamp.Should().BeNull();
        record.XccdfScore.Should().BeNull();
        record.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ScanImportRecord_Should_Default_Counts_To_Zero()
    {
        var record = new ScanImportRecord();

        record.TotalEntries.Should().Be(0);
        record.OpenCount.Should().Be(0);
        record.PassCount.Should().Be(0);
        record.NotApplicableCount.Should().Be(0);
        record.NotReviewedCount.Should().Be(0);
        record.ErrorCount.Should().Be(0);
        record.SkippedCount.Should().Be(0);
        record.UnmatchedCount.Should().Be(0);
        record.FindingsCreated.Should().Be(0);
        record.FindingsUpdated.Should().Be(0);
        record.EffectivenessRecordsCreated.Should().Be(0);
        record.EffectivenessRecordsUpdated.Should().Be(0);
        record.NistControlsAffected.Should().Be(0);
    }

    [Fact]
    public void ScanImportRecord_Two_Instances_Should_Have_Unique_Ids()
    {
        var record1 = new ScanImportRecord();
        var record2 = new ScanImportRecord();
        record1.Id.Should().NotBe(record2.Id);
    }

    [Fact]
    public void ScanImportRecord_ImportedAt_Should_Be_Close_To_UtcNow()
    {
        var before = DateTime.UtcNow;
        var record = new ScanImportRecord();
        var after = DateTime.UtcNow;

        record.ImportedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ─── ScanImportFinding Entity ────────────────────────────────────────────

    [Fact]
    public void ScanImportFinding_Should_Have_Generated_Id()
    {
        var finding = new ScanImportFinding();
        finding.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(finding.Id, out _).Should().BeTrue();
    }

    [Fact]
    public void ScanImportFinding_Should_Have_Default_Values()
    {
        var finding = new ScanImportFinding();

        finding.ScanImportRecordId.Should().Be(string.Empty);
        finding.VulnId.Should().Be(string.Empty);
        finding.RawStatus.Should().Be(string.Empty);
        finding.RawSeverity.Should().Be(string.Empty);
        finding.ResolvedNistControlIds.Should().NotBeNull().And.BeEmpty();
        finding.ResolvedCciRefs.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ScanImportFinding_Should_Have_Nullable_Optional_Fields()
    {
        var finding = new ScanImportFinding();

        finding.RuleId.Should().BeNull();
        finding.StigVersion.Should().BeNull();
        finding.MappedSeverity.Should().BeNull();
        finding.FindingDetails.Should().BeNull();
        finding.Comments.Should().BeNull();
        finding.SeverityOverride.Should().BeNull();
        finding.SeverityJustification.Should().BeNull();
        finding.ResolvedStigControlId.Should().BeNull();
        finding.ComplianceFindingId.Should().BeNull();
    }

    // ─── ComplianceFinding.ImportRecordId FK ─────────────────────────────────

    [Fact]
    public void ComplianceFinding_ImportRecordId_Should_Be_Nullable()
    {
        var finding = new ComplianceFinding();
        finding.ImportRecordId.Should().BeNull();
    }

    [Fact]
    public void ComplianceFinding_ImportRecordId_Should_Accept_Value()
    {
        var finding = new ComplianceFinding { ImportRecordId = "test-import-id" };
        finding.ImportRecordId.Should().Be("test-import-id");
    }

    // ─── ParsedCklEntry DTO ──────────────────────────────────────────────────

    [Fact]
    public void ParsedCklEntry_Should_Store_All_Fields()
    {
        var cciRefs = new List<string> { "CCI-000366", "CCI-001084" };
        var entry = new ParsedCklEntry(
            VulnId: "V-254239",
            RuleId: "SV-254239r849090_rule",
            StigVersion: "WN22-AU-000010",
            RuleTitle: "Test Rule",
            Severity: "high",
            Status: "Open",
            FindingDetails: "Finding details text",
            Comments: "Assessor comments",
            SeverityOverride: "medium",
            SeverityJustification: "Override justification",
            CciRefs: cciRefs,
            GroupTitle: "SRG-OS-000037-GPOS-00015");

        entry.VulnId.Should().Be("V-254239");
        entry.RuleId.Should().Be("SV-254239r849090_rule");
        entry.StigVersion.Should().Be("WN22-AU-000010");
        entry.RuleTitle.Should().Be("Test Rule");
        entry.Severity.Should().Be("high");
        entry.Status.Should().Be("Open");
        entry.FindingDetails.Should().Be("Finding details text");
        entry.Comments.Should().Be("Assessor comments");
        entry.SeverityOverride.Should().Be("medium");
        entry.SeverityJustification.Should().Be("Override justification");
        entry.CciRefs.Should().BeEquivalentTo(cciRefs);
        entry.GroupTitle.Should().Be("SRG-OS-000037-GPOS-00015");
    }

    [Fact]
    public void ParsedCklEntry_Should_Allow_Null_Optional_Fields()
    {
        var entry = new ParsedCklEntry(
            VulnId: "V-100000",
            RuleId: null,
            StigVersion: null,
            RuleTitle: null,
            Severity: "low",
            Status: "Not_Reviewed",
            FindingDetails: null,
            Comments: null,
            SeverityOverride: null,
            SeverityJustification: null,
            CciRefs: new List<string>(),
            GroupTitle: null);

        entry.RuleId.Should().BeNull();
        entry.StigVersion.Should().BeNull();
        entry.CciRefs.Should().BeEmpty();
    }

    // ─── ParsedCklFile DTO ───────────────────────────────────────────────────

    [Fact]
    public void ParsedCklFile_Should_Compose_Asset_StigInfo_Entries()
    {
        var asset = new CklAssetInfo("server01", "10.0.0.1", "server01.domain.com", "00:11:22:33:44:55", "Computing", "4000");
        var stigInfo = new CklStigInfo("Windows_Server_2022_STIG", "V2R1", "Release: 1 Benchmark Date: 01 Jan 2026", "Windows Server 2022 STIG");
        var entries = new List<ParsedCklEntry>();

        var file = new ParsedCklFile(asset, stigInfo, entries);

        file.Asset.HostName.Should().Be("server01");
        file.StigInfo.StigId.Should().Be("Windows_Server_2022_STIG");
        file.Entries.Should().BeEmpty();
    }

    // ─── CklAssetInfo DTO ────────────────────────────────────────────────────

    [Fact]
    public void CklAssetInfo_Should_Allow_All_Nulls()
    {
        var asset = new CklAssetInfo(null, null, null, null, null, null);
        asset.HostName.Should().BeNull();
        asset.HostIp.Should().BeNull();
        asset.HostFqdn.Should().BeNull();
        asset.HostMac.Should().BeNull();
        asset.AssetType.Should().BeNull();
        asset.TargetKey.Should().BeNull();
    }

    // ─── CklStigInfo DTO ────────────────────────────────────────────────────

    [Fact]
    public void CklStigInfo_Should_Allow_All_Nulls()
    {
        var info = new CklStigInfo(null, null, null, null);
        info.StigId.Should().BeNull();
        info.Version.Should().BeNull();
        info.ReleaseInfo.Should().BeNull();
        info.Title.Should().BeNull();
    }

    // ─── ParsedXccdfResult DTO ───────────────────────────────────────────────

    [Fact]
    public void ParsedXccdfResult_Should_Store_All_Fields()
    {
        var result = new ParsedXccdfResult(
            RuleIdRef: "xccdf_mil.disa.stig_rule_SV-254239r849090_rule",
            ExtractedRuleId: "SV-254239r849090_rule",
            Result: "fail",
            Severity: "high",
            Weight: 10.0m,
            Timestamp: new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
            Message: "Check failed: audit policy not configured",
            CheckRef: "oval:mil.disa.stig.windows_server_2022:def:254239");

        result.RuleIdRef.Should().Contain("xccdf_mil.disa.stig_rule_");
        result.ExtractedRuleId.Should().Be("SV-254239r849090_rule");
        result.Result.Should().Be("fail");
        result.Severity.Should().Be("high");
        result.Weight.Should().Be(10.0m);
        result.Timestamp.Should().NotBeNull();
        result.Message.Should().NotBeNullOrEmpty();
        result.CheckRef.Should().Contain("oval:");
    }

    // ─── ParsedXccdfFile DTO ─────────────────────────────────────────────────

    [Fact]
    public void ParsedXccdfFile_Should_Compose_Results_And_Facts()
    {
        var facts = new Dictionary<string, string>
        {
            ["urn:xccdf:fact:asset:identifier:fqdn"] = "server01.domain.com",
            ["urn:xccdf:fact:asset:identifier:os_name"] = "Windows Server 2022"
        };

        var file = new ParsedXccdfFile(
            BenchmarkHref: "Windows_Server_2022_STIG",
            Title: "XCCDF Result for Windows Server 2022",
            Target: "server01",
            TargetAddress: "10.0.0.1",
            StartTime: DateTime.UtcNow.AddMinutes(-5),
            EndTime: DateTime.UtcNow,
            Score: 85.5m,
            MaxScore: 100.0m,
            TargetFacts: facts,
            Results: new List<ParsedXccdfResult>());

        file.BenchmarkHref.Should().NotBeNullOrEmpty();
        file.Score.Should().Be(85.5m);
        file.MaxScore.Should().Be(100.0m);
        file.TargetFacts.Should().HaveCount(2);
        file.Results.Should().BeEmpty();
    }

    // ─── ImportResult DTO ────────────────────────────────────────────────────

    [Fact]
    public void ImportResult_Should_Store_All_Counts_And_Warnings()
    {
        var warnings = new List<string> { "Unmatched rule: V-999999" };
        var unmatched = new List<UnmatchedRuleInfo>
        {
            new("V-999999", "SV-999999r1_rule", "Unknown Rule", "high")
        };

        var result = new ImportResult(
            ImportRecordId: "import-001",
            Status: ScanImportStatus.CompletedWithWarnings,
            BenchmarkId: "Windows_Server_2022_STIG",
            BenchmarkTitle: "Windows Server 2022 STIG",
            TotalEntries: 100,
            OpenCount: 10,
            PassCount: 80,
            NotApplicableCount: 5,
            NotReviewedCount: 3,
            ErrorCount: 0,
            SkippedCount: 1,
            UnmatchedCount: 1,
            FindingsCreated: 13,
            FindingsUpdated: 0,
            EffectivenessRecordsCreated: 8,
            EffectivenessRecordsUpdated: 0,
            NistControlsAffected: 8,
            Warnings: warnings,
            UnmatchedRules: unmatched,
            ErrorMessage: null);

        result.ImportRecordId.Should().Be("import-001");
        result.Status.Should().Be(ScanImportStatus.CompletedWithWarnings);
        result.TotalEntries.Should().Be(100);
        result.OpenCount.Should().Be(10);
        result.PassCount.Should().Be(80);
        result.UnmatchedCount.Should().Be(1);
        result.Warnings.Should().HaveCount(1);
        result.UnmatchedRules.Should().HaveCount(1);
        result.UnmatchedRules[0].VulnId.Should().Be("V-999999");
        result.ErrorMessage.Should().BeNull();
    }

    // ─── UnmatchedRuleInfo DTO ───────────────────────────────────────────────

    [Fact]
    public void UnmatchedRuleInfo_Should_Store_All_Fields()
    {
        var info = new UnmatchedRuleInfo("V-254239", "SV-254239r849090_rule", "Audit Policy", "high");
        info.VulnId.Should().Be("V-254239");
        info.RuleId.Should().Be("SV-254239r849090_rule");
        info.RuleTitle.Should().Be("Audit Policy");
        info.Severity.Should().Be("high");
    }

    [Fact]
    public void UnmatchedRuleInfo_Should_Allow_Null_Optional_Fields()
    {
        var info = new UnmatchedRuleInfo("V-100000", null, null, "low");
        info.RuleId.Should().BeNull();
        info.RuleTitle.Should().BeNull();
    }
}

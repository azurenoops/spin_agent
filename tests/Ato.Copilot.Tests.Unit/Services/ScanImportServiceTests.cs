// ═══════════════════════════════════════════════════════════════════════════
// Feature 017 — SCAP/STIG Viewer Import: ScanImportService Tests
// Covers T018 (STIG resolution), T022-T024 (finding/effectiveness/evidence),
// T028-T029 (conflict resolution, dry-run).
// ═══════════════════════════════════════════════════════════════════════════

using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Services.ScanImport;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="ScanImportService"/>.
/// Uses EF Core InMemory provider for DbContext, mocked services for external dependencies.
/// </summary>
public class ScanImportServiceTests : IDisposable
{
    // ── Shared test infrastructure ───────────────────────────────────────

    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IStigKnowledgeService> _stigServiceMock;
    private readonly Mock<IBaselineService> _baselineServiceMock;
    private readonly Mock<IRmfLifecycleService> _rmfServiceMock;
    private readonly Mock<IAssessmentArtifactService> _artifactServiceMock;
    private readonly Mock<ICklParser> _cklParserMock;
    private readonly Mock<IXccdfParser> _xccdfParserMock;
    private readonly Mock<ICklGenerator> _cklGeneratorMock;
    private readonly ScanImportService _service;

    // Standard test system
    private const string TestSystemId = "sys-001";
    private const string TestAssessmentId = "assess-001";
    private const string TestImporter = "test-user";

    private readonly RegisteredSystem _testSystem = new()
    {
        Id = TestSystemId,
        Name = "Test System",
        CurrentRmfStep = RmfPhase.Assess,
        HostingEnvironment = "Azure Government",
        CreatedBy = "admin"
    };

    private readonly ControlBaseline _testBaseline = new()
    {
        Id = "bl-001",
        RegisteredSystemId = TestSystemId,
        BaselineLevel = "Moderate",
        ControlIds = new List<string> { "AC-2", "AC-7", "IA-5", "SC-7", "AU-3" },
        CreatedBy = "admin"
    };

    // Standard STIG controls for resolution tests
    private readonly StigControl _stigAc2 = new(
        StigId: "V-254239",
        VulnId: "V-254239",
        RuleId: "SV-254239r849090_rule",
        Title: "Windows Server 2022 must be configured to audit account management.",
        Description: "Audit account management",
        Severity: StigSeverity.High,
        Category: "CAT I",
        StigFamily: "Windows Server 2022",
        NistControls: new List<string> { "AC-2" },
        CciRefs: new List<string> { "CCI-000018", "CCI-000172" },
        CheckText: "Check audit policy",
        FixText: "Configure audit policy",
        AzureImplementation: new Dictionary<string, string>(),
        ServiceType: "Windows",
        StigVersion: "WN22-AU-000010",
        BenchmarkId: "Windows_Server_2022_STIG");

    private readonly StigControl _stigIa5 = new(
        StigId: "V-254240",
        VulnId: "V-254240",
        RuleId: "SV-254240r849093_rule",
        Title: "Windows Server 2022 must enforce password complexity.",
        Description: "Password complexity",
        Severity: StigSeverity.Medium,
        Category: "CAT II",
        StigFamily: "Windows Server 2022",
        NistControls: new List<string> { "IA-5" },
        CciRefs: new List<string> { "CCI-000192" },
        CheckText: "Check password policy",
        FixText: "Configure password policy",
        AzureImplementation: new Dictionary<string, string>(),
        ServiceType: "Windows",
        StigVersion: "WN22-SO-000060",
        BenchmarkId: "Windows_Server_2022_STIG");

    private readonly StigControl _stigSc7 = new(
        StigId: "V-254241",
        VulnId: "V-254241",
        RuleId: "SV-254241r849096_rule",
        Title: "Windows Server 2022 must restrict remote connections.",
        Description: "Remote connections",
        Severity: StigSeverity.Low,
        Category: "CAT III",
        StigFamily: "Windows Server 2022",
        NistControls: new List<string> { "SC-7" },
        CciRefs: new List<string> { "CCI-001097" },
        CheckText: "Check remote settings",
        FixText: "Configure remote settings",
        AzureImplementation: new Dictionary<string, string>(),
        ServiceType: "Windows",
        StigVersion: "WN22-CC-000010",
        BenchmarkId: "Windows_Server_2022_STIG");

    public ScanImportServiceTests()
    {
        // Set up in-memory SQLite
        var services = new ServiceCollection();
        var dbName = $"ScanImportTests_{Guid.NewGuid()}";
        services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _serviceProvider = services.BuildServiceProvider();

        // Initialize DB
        using var initScope = _serviceProvider.CreateScope();
        var initCtx = initScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        initCtx.Database.EnsureCreated();

        // Seed assessment
        initCtx.Assessments.Add(new ComplianceAssessment
        {
            Id = TestAssessmentId,
            RegisteredSystemId = TestSystemId,
            Framework = "NIST80053",
            Status = AssessmentStatus.InProgress,
            InitiatedBy = TestImporter
        });
        initCtx.SaveChanges();

        // Set up mocks
        _stigServiceMock = new Mock<IStigKnowledgeService>();
        _baselineServiceMock = new Mock<IBaselineService>();
        _rmfServiceMock = new Mock<IRmfLifecycleService>();
        _artifactServiceMock = new Mock<IAssessmentArtifactService>();
        _cklParserMock = new Mock<ICklParser>();
        _xccdfParserMock = new Mock<IXccdfParser>();
        _cklGeneratorMock = new Mock<ICklGenerator>();

        // Default setup: system exists, baseline exists
        _rmfServiceMock.Setup(s => s.GetSystemAsync(TestSystemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSystemResult.Found(_testSystem));
        _baselineServiceMock.Setup(s => s.GetBaselineAsync(TestSystemId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testBaseline);

        // Default STIG resolution
        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-254239", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_stigAc2);
        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-254240", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_stigIa5);
        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-254241", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_stigSc7);

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var logger = NullLogger<ScanImportService>.Instance;

        _service = new ScanImportService(
            scopeFactory,
            _stigServiceMock.Object,
            _baselineServiceMock.Object,
            _rmfServiceMock.Object,
            _artifactServiceMock.Object,
            _cklParserMock.Object,
            _xccdfParserMock.Object,
            _cklGeneratorMock.Object,
            Mock.Of<ISystemSubscriptionResolver>(),
            new PrismaCsvParser(NullLogger<PrismaCsvParser>.Instance),
            new PrismaApiJsonParser(NullLogger<PrismaApiJsonParser>.Instance),
            Mock.Of<INessusParser>(),
            Mock.Of<INessusControlMapper>(),
            logger);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    // ── Helper: Build standard ParsedCklFile ─────────────────────────────

    private static ParsedCklFile BuildParsedCkl(params ParsedCklEntry[] entries)
    {
        return new ParsedCklFile(
            Asset: new CklAssetInfo("web-server-01", "10.0.1.100", "web-server-01.example.mil", "00:0A:95:9D:68:16", "Computing", "4089"),
            StigInfo: new CklStigInfo("Windows_Server_2022_STIG", "3", "Release: 1 Benchmark Date: 23 Mar 2023", "Microsoft Windows Server 2022 STIG"),
            Entries: entries.ToList());
    }

    private static ParsedCklEntry BuildEntry(
        string vulnId, string status, string severity = "high",
        string? ruleId = null, string? stigVersion = null,
        string? findingDetails = null, string? comments = null,
        string? severityOverride = null, string? severityJustification = null,
        List<string>? cciRefs = null)
    {
        return new ParsedCklEntry(
            VulnId: vulnId,
            RuleId: ruleId ?? $"SV-{vulnId[2..]}r849090_rule",
            StigVersion: stigVersion ?? "WN22-AU-000010",
            RuleTitle: $"Test Rule {vulnId}",
            Severity: severity,
            Status: status,
            FindingDetails: findingDetails,
            Comments: comments,
            SeverityOverride: severityOverride,
            SeverityJustification: severityJustification,
            CciRefs: cciRefs ?? new List<string> { "CCI-000018" },
            GroupTitle: "SRG-OS-000003-GPOS-00004");
    }

    private byte[] DummyFileContent => "dummy-ckl-content"u8.ToArray();

    // ═══════════════════════════════════════════════════════════════════════
    // T018: STIG Resolution Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportCkl_VulnIdMatch_ReturnsCorrectStigControl()
    {
        // Arrange
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.Status.Should().NotBe(ScanImportStatus.Failed);
        result.FindingsCreated.Should().Be(1);
        result.UnmatchedCount.Should().Be(0);
    }

    [Fact]
    public async Task ImportCkl_VulnIdMiss_FallbackToRuleId()
    {
        // Arrange: VulnId "V-999999" not found, but RuleId matches
        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-999999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StigControl?)null);
        _stigServiceMock.Setup(s => s.GetStigControlByRuleIdAsync("SV-999999r123456_rule", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_stigAc2);

        var entry = BuildEntry("V-999999", "Open", ruleId: "SV-999999r123456_rule");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.UnmatchedCount.Should().Be(0);
        result.FindingsCreated.Should().Be(1);
    }

    [Fact]
    public async Task ImportCkl_BothMiss_TrackedAsUnmatched()
    {
        // Arrange: Neither VulnId nor RuleId match
        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-888888", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StigControl?)null);
        _stigServiceMock.Setup(s => s.GetStigControlByRuleIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StigControl?)null);

        var entry = BuildEntry("V-888888", "Open", ruleId: "SV-888888r000000_rule", stigVersion: "WN22-XX-000099");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.UnmatchedCount.Should().Be(1);
        result.FindingsCreated.Should().Be(0);
        result.UnmatchedRules.Should().HaveCount(1);
        result.UnmatchedRules[0].VulnId.Should().Be("V-888888");
    }

    [Fact]
    public async Task ImportCkl_CciResolution_ReturnsCorrectNistControls()
    {
        // Arrange: STIG maps to AC-2 (in baseline)
        var entry = BuildEntry("V-254239", "Open", cciRefs: new List<string> { "CCI-000018", "CCI-000172" });
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.NistControlsAffected.Should().BeGreaterThanOrEqualTo(1);
        result.EffectivenessRecordsCreated.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ImportCkl_NistControlNotInBaseline_WarningLogged()
    {
        // Arrange: STIG maps to "ZZ-99" which is NOT in baseline
        var outOfBaselineStig = new StigControl(
            StigId: "V-777777", VulnId: "V-777777", RuleId: "SV-777777r111_rule",
            Title: "Test", Description: "Test", Severity: StigSeverity.Medium,
            Category: "CAT II", StigFamily: "Test",
            NistControls: new List<string> { "ZZ-99" },
            CciRefs: new List<string> { "CCI-999999" },
            CheckText: "Check", FixText: "Fix",
            AzureImplementation: new Dictionary<string, string>(),
            ServiceType: "Test");

        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-777777", It.IsAny<CancellationToken>()))
            .ReturnsAsync(outOfBaselineStig);

        var entry = BuildEntry("V-777777", "Open", severity: "medium");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert: Finding created (audit trail) but no effectiveness (out of baseline)
        result.FindingsCreated.Should().Be(1);
        result.EffectivenessRecordsCreated.Should().Be(0);
        result.Warnings.Should().Contain(w => w.Contains("not in system baseline"));
    }

    [Fact]
    public async Task ImportCkl_MultipleCciRefs_AllNistControlsResolved()
    {
        // Arrange: STIG maps to multiple NIST controls
        var multiControlStig = new StigControl(
            StigId: "V-254239", VulnId: "V-254239", RuleId: "SV-254239r849090_rule",
            Title: "Test", Description: "Test", Severity: StigSeverity.High,
            Category: "CAT I", StigFamily: "Test",
            NistControls: new List<string> { "AC-2", "AU-3" },
            CciRefs: new List<string> { "CCI-000018", "CCI-000172" },
            CheckText: "", FixText: "",
            AzureImplementation: new Dictionary<string, string>(),
            ServiceType: "Test");

        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-254239", It.IsAny<CancellationToken>()))
            .ReturnsAsync(multiControlStig);

        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert: Both AC-2 and AU-3 should be affected
        result.NistControlsAffected.Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T022: Finding Creation Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportCkl_OpenEntry_CreatesOpenFinding()
    {
        // Arrange
        var entry = BuildEntry("V-254239", "Open", findingDetails: "Audit policy not configured.");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.FindingsCreated.Should().Be(1);
        result.OpenCount.Should().Be(1);

        // Verify the actual finding in DB
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings
            .Where(f => f.StigId == "V-254239")
            .FirstOrDefaultAsync();

        finding.Should().NotBeNull();
        finding!.Status.Should().Be(FindingStatus.Open);
        finding.StigFinding.Should().BeTrue();
        finding.Source.Should().Be("CKL Import");
        finding.Description.Should().Contain("Audit policy not configured.");
    }

    [Fact]
    public async Task ImportCkl_NotAFinding_CreatesRemediatedFinding()
    {
        // Arrange
        var entry = BuildEntry("V-254240", "NotAFinding", severity: "medium");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.FindingsCreated.Should().Be(1);
        result.PassCount.Should().Be(1);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.Where(f => f.StigId == "V-254240").FirstOrDefaultAsync();

        finding.Should().NotBeNull();
        finding!.Status.Should().Be(FindingStatus.Remediated);
    }

    [Fact]
    public async Task ImportCkl_NotApplicable_NoFindingCreated()
    {
        // Arrange
        var entry = BuildEntry("V-254241", "Not_Applicable", severity: "low");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.FindingsCreated.Should().Be(0);
        result.NotApplicableCount.Should().Be(1);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var findings = await ctx.Findings.Where(f => f.StigId == "V-254241").ToListAsync();
        findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportCkl_NotReviewed_CreatesOpenFindingWithNote()
    {
        // Arrange
        var entry = BuildEntry("V-254239", "Not_Reviewed");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.FindingsCreated.Should().Be(1);
        result.NotReviewedCount.Should().Be(1);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.Where(f => f.StigId == "V-254239").FirstOrDefaultAsync();

        finding.Should().NotBeNull();
        finding!.Status.Should().Be(FindingStatus.Open);
        finding.Description.Should().Contain("Not yet reviewed");
    }

    [Fact]
    public async Task ImportCkl_SeverityMapping_HighToCatI()
    {
        var entry = BuildEntry("V-254239", "Open", severity: "high");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.Where(f => f.StigId == "V-254239").FirstOrDefaultAsync();

        finding.Should().NotBeNull();
        finding!.Severity.Should().Be(FindingSeverity.High);
        finding.CatSeverity.Should().Be(CatSeverity.CatI);
    }

    [Theory]
    [InlineData("medium", FindingSeverity.Medium, CatSeverity.CatII)]
    [InlineData("low", FindingSeverity.Low, CatSeverity.CatIII)]
    public async Task ImportCkl_SeverityMapping_MediumAndLow(
        string rawSeverity, FindingSeverity expectedSeverity, CatSeverity expectedCat)
    {
        var entry = BuildEntry("V-254240", "Open", severity: rawSeverity);
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.Where(f => f.StigId == "V-254240").FirstOrDefaultAsync();

        finding.Should().NotBeNull();
        finding!.Severity.Should().Be(expectedSeverity);
        finding.CatSeverity.Should().Be(expectedCat);
    }

    [Fact]
    public async Task ImportCkl_SeverityOverride_AppliedOverDefault()
    {
        // Arrange: Default severity = medium, override = high
        var entry = BuildEntry("V-254240", "Open", severity: "medium",
            severityOverride: "high", severityJustification: "Escalated per ISSM review.");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.Where(f => f.StigId == "V-254240").FirstOrDefaultAsync();

        finding.Should().NotBeNull();
        finding!.Severity.Should().Be(FindingSeverity.High);
        finding.CatSeverity.Should().Be(CatSeverity.CatI);
    }

    [Fact]
    public async Task ImportCkl_OutOfBaselineNistControl_FindingCreated_NoEffectiveness()
    {
        // Arrange: STIG maps to "ZZ-99" which is NOT in baseline
        var outOfBaselineStig = new StigControl(
            StigId: "V-666666", VulnId: "V-666666", RuleId: "SV-666666r111_rule",
            Title: "Out-of-baseline rule", Description: "Test", Severity: StigSeverity.Medium,
            Category: "CAT II", StigFamily: "Test",
            NistControls: new List<string> { "ZZ-99" },
            CciRefs: new List<string> { "CCI-999999" },
            CheckText: "", FixText: "",
            AzureImplementation: new Dictionary<string, string>(), ServiceType: "Test");

        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-666666", It.IsAny<CancellationToken>()))
            .ReturnsAsync(outOfBaselineStig);

        var entry = BuildEntry("V-666666", "Open", severity: "medium");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert: Finding created but no effectiveness for out-of-baseline
        result.FindingsCreated.Should().Be(1);
        result.EffectivenessRecordsCreated.Should().Be(0);
        result.Warnings.Should().Contain(w => w.Contains("not in system baseline"));
    }

    [Fact]
    public async Task ImportCkl_SystemNotInAssessStep_WarningEmitted()
    {
        // Arrange: System in "Implement" step (before Assess)
        var implementSystem = new RegisteredSystem
        {
            Id = TestSystemId,
            Name = "Test System",
            CurrentRmfStep = RmfPhase.Implement,
            HostingEnvironment = "Azure Government",
            CreatedBy = "admin"
        };
        _rmfServiceMock.Setup(s => s.GetSystemAsync(TestSystemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSystemResult.Found(implementSystem));

        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert: Warning emitted but import proceeds
        result.Status.Should().NotBe(ScanImportStatus.Failed);
        result.Warnings.Should().Contain(w => w.Contains("Implement") && w.Contains("expected Assess"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T023: Effectiveness Upsert Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportCkl_AllRulesPass_EffectivenessSatisfied()
    {
        var entries = new[]
        {
            BuildEntry("V-254239", "NotAFinding"),
            BuildEntry("V-254240", "NotAFinding", severity: "medium")
        };
        var parsed = BuildParsedCkl(entries);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        result.EffectivenessRecordsCreated.Should().BeGreaterThanOrEqualTo(1);

        // Verify effectiveness in DB
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var effRecords = await ctx.ControlEffectivenessRecords
            .Where(e => e.AssessmentId == TestAssessmentId)
            .ToListAsync();

        effRecords.Should().NotBeEmpty();
        effRecords.Should().OnlyContain(e => e.Determination == EffectivenessDetermination.Satisfied);
        effRecords.Should().OnlyContain(e => e.AssessmentMethod == "Test");
    }

    [Fact]
    public async Task ImportCkl_OneRuleFails_EffectivenessOtherThanSatisfied()
    {
        // AC-2 maps to V-254239 (Open) → OtherThanSatisfied
        var entries = new[]
        {
            BuildEntry("V-254239", "Open"), // AC-2 → Open
            BuildEntry("V-254240", "NotAFinding", severity: "medium") // IA-5 → pass
        };
        var parsed = BuildParsedCkl(entries);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var ac2Eff = await ctx.ControlEffectivenessRecords
            .Where(e => e.ControlId == "AC-2" && e.AssessmentId == TestAssessmentId)
            .FirstOrDefaultAsync();
        var ia5Eff = await ctx.ControlEffectivenessRecords
            .Where(e => e.ControlId == "IA-5" && e.AssessmentId == TestAssessmentId)
            .FirstOrDefaultAsync();

        ac2Eff.Should().NotBeNull();
        ac2Eff!.Determination.Should().Be(EffectivenessDetermination.OtherThanSatisfied);

        ia5Eff.Should().NotBeNull();
        ia5Eff!.Determination.Should().Be(EffectivenessDetermination.Satisfied);
    }

    [Fact]
    public async Task ImportCkl_EffectivenessMethodIsTest()
    {
        var entry = BuildEntry("V-254239", "NotAFinding");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var eff = await ctx.ControlEffectivenessRecords
            .Where(e => e.AssessmentId == TestAssessmentId)
            .FirstOrDefaultAsync();

        eff.Should().NotBeNull();
        eff!.AssessmentMethod.Should().Be("Test");
    }

    [Fact]
    public async Task ImportCkl_ReimportAggregateReeval_FlipsToSatisfied()
    {
        // First import: V-254239 is Open → OtherThanSatisfied for AC-2
        var firstEntry = BuildEntry("V-254239", "Open");
        var firstParsed = BuildParsedCkl(firstEntry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(firstParsed);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "first.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Second import: V-254239 is now NotAFinding → re-evaluate should flip AC-2 to Satisfied
        var secondEntry = BuildEntry("V-254239", "NotAFinding");
        var secondParsed = BuildParsedCkl(secondEntry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(secondParsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "second.ckl",
            ImportConflictResolution.Overwrite, false, TestImporter);

        // Assert: Effectiveness for AC-2 should now be Satisfied
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var ac2Eff = await ctx.ControlEffectivenessRecords
            .Where(e => e.ControlId == "AC-2" && e.AssessmentId == TestAssessmentId)
            .FirstOrDefaultAsync();

        ac2Eff.Should().NotBeNull();
        ac2Eff!.Determination.Should().Be(EffectivenessDetermination.Satisfied);
    }

    [Fact]
    public async Task ImportCkl_OutOfBaselineControl_NoEffectivenessCreated()
    {
        var outOfBaselineStig = new StigControl(
            StigId: "V-555555", VulnId: "V-555555", RuleId: "SV-555555r111_rule",
            Title: "OOB Test", Description: "Test", Severity: StigSeverity.High,
            Category: "CAT I", StigFamily: "Test",
            NistControls: new List<string> { "XX-1" },
            CciRefs: new List<string>(), CheckText: "", FixText: "",
            AzureImplementation: new Dictionary<string, string>(), ServiceType: "Test");

        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-555555", It.IsAny<CancellationToken>()))
            .ReturnsAsync(outOfBaselineStig);

        var entry = BuildEntry("V-555555", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        result.EffectivenessRecordsCreated.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T024: Evidence Creation Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportCkl_EvidenceCreated_WithSha256Hash()
    {
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var fileBytes = DummyFileContent;
        var expectedHash = ScanImportService.ComputeSha256(fileBytes);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, fileBytes, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var evidences = await ctx.Evidence
            .Where(e => e.AssessmentId == TestAssessmentId && e.EvidenceType == "StigChecklist")
            .ToListAsync();

        evidences.Should().HaveCount(1);
        evidences[0].ContentHash.Should().Be(expectedHash);
        evidences[0].EvidenceCategory.Should().Be(EvidenceCategory.Configuration);
        evidences[0].CollectionMethod.Should().Be("Manual");
    }

    [Fact]
    public async Task ImportCkl_EvidenceType_IsStigChecklist()
    {
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var evidence = await ctx.Evidence
            .Where(e => e.EvidenceType == "StigChecklist")
            .FirstOrDefaultAsync();

        evidence.Should().NotBeNull();
        evidence!.EvidenceType.Should().Be("StigChecklist");
    }

    [Fact]
    public async Task ImportCkl_EvidenceContent_ContainsSummaryJson()
    {
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var evidence = await ctx.Evidence
            .Where(e => e.EvidenceType == "StigChecklist")
            .FirstOrDefaultAsync();

        evidence.Should().NotBeNull();
        evidence!.Content.Should().Contain("CKL");
        evidence.Content.Should().Contain("OpenCount");
    }

    [Fact]
    public async Task ImportCkl_EvidenceLinkedToEffectiveness()
    {
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var effRecords = await ctx.ControlEffectivenessRecords
            .Where(e => e.AssessmentId == TestAssessmentId)
            .ToListAsync();

        effRecords.Should().NotBeEmpty();
        effRecords.Should().OnlyContain(e => e.EvidenceIds.Count > 0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T028: Conflict Resolution Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportCkl_Skip_ExistingFindingUnchanged()
    {
        // First import
        var entry1 = BuildEntry("V-254239", "Open", findingDetails: "Original details");
        var parsed1 = BuildParsedCkl(entry1);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed1);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "first.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Second import with Skip resolution
        var entry2 = BuildEntry("V-254239", "NotAFinding", findingDetails: "Updated details");
        var parsed2 = BuildParsedCkl(entry2);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed2);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "second.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert: Finding not updated
        result.SkippedCount.Should().Be(1);
        result.FindingsUpdated.Should().Be(0);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.Where(f => f.StigId == "V-254239").FirstOrDefaultAsync();

        finding.Should().NotBeNull();
        finding!.Status.Should().Be(FindingStatus.Open); // Original status
        finding.Description.Should().Contain("Original details");
    }

    [Fact]
    public async Task ImportCkl_Overwrite_FindingUpdatedWithImportedData()
    {
        // First import
        var entry1 = BuildEntry("V-254239", "Open", findingDetails: "Original details");
        var parsed1 = BuildParsedCkl(entry1);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed1);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "first.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Second import with Overwrite
        var entry2 = BuildEntry("V-254239", "NotAFinding", findingDetails: "Now remediated");
        var parsed2 = BuildParsedCkl(entry2);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed2);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "second.ckl",
            ImportConflictResolution.Overwrite, false, TestImporter);

        result.FindingsUpdated.Should().Be(1);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.Where(f => f.StigId == "V-254239").FirstOrDefaultAsync();

        finding.Should().NotBeNull();
        finding!.Status.Should().Be(FindingStatus.Remediated);
        finding.Description.Should().Contain("Now remediated");
    }

    [Fact]
    public async Task ImportCkl_Merge_SeverityTakesHigherValue()
    {
        // First import: medium severity
        var entry1 = BuildEntry("V-254240", "Open", severity: "medium");
        var parsed1 = BuildParsedCkl(entry1);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed1);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "first.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Second import with Merge: high severity (should upgrade)
        var entry2 = BuildEntry("V-254240", "Open", severity: "high");
        var parsed2 = BuildParsedCkl(entry2);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed2);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "second.ckl",
            ImportConflictResolution.Merge, false, TestImporter);

        result.FindingsUpdated.Should().Be(1);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.Where(f => f.StigId == "V-254240").FirstOrDefaultAsync();

        finding.Should().NotBeNull();
        finding!.Severity.Should().Be(FindingSeverity.High);
        finding.CatSeverity.Should().Be(CatSeverity.CatI);
    }

    [Fact]
    public async Task ImportCkl_Merge_DetailsAppendedIfDifferent()
    {
        // First import
        var entry1 = BuildEntry("V-254239", "Open", findingDetails: "First observation.");
        var parsed1 = BuildParsedCkl(entry1);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed1);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "first.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Second import with Merge: different details
        var entry2 = BuildEntry("V-254239", "Open", findingDetails: "Second observation.");
        var parsed2 = BuildParsedCkl(entry2);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed2);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "second.ckl",
            ImportConflictResolution.Merge, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.Where(f => f.StigId == "V-254239").FirstOrDefaultAsync();

        finding.Should().NotBeNull();
        finding!.Description.Should().Contain("First observation");
        finding.Description.Should().Contain("Second observation");
    }

    [Fact]
    public async Task ImportCkl_NoConflict_NewFindingCreatedRegardlessly()
    {
        // Import two different VULNs — no conflict
        var entries = new[]
        {
            BuildEntry("V-254239", "Open"),
            BuildEntry("V-254240", "NotAFinding", severity: "medium")
        };
        var parsed = BuildParsedCkl(entries);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        result.FindingsCreated.Should().Be(2);
        result.SkippedCount.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T029: Dry-Run Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportCkl_DryRun_ReturnsAccurateCounts()
    {
        var entries = new[]
        {
            BuildEntry("V-254239", "Open"),
            BuildEntry("V-254240", "NotAFinding", severity: "medium"),
            BuildEntry("V-254241", "Not_Applicable", severity: "low")
        };
        var parsed = BuildParsedCkl(entries);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, true, TestImporter);

        result.Status.Should().NotBe(ScanImportStatus.Failed);
        result.TotalEntries.Should().Be(3);
        result.OpenCount.Should().Be(1);
        result.PassCount.Should().Be(1);
        result.NotApplicableCount.Should().Be(1);
        result.FindingsCreated.Should().Be(2); // Open + NotAFinding (Not_Applicable skipped)
    }

    [Fact]
    public async Task ImportCkl_DryRun_CreatesNoDbRecords()
    {
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, true, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // No findings created
        var findings = await ctx.Findings.Where(f => f.StigId == "V-254239").ToListAsync();
        findings.Should().BeEmpty();

        // No import records created
        var imports = await ctx.ScanImportRecords.ToListAsync();
        imports.Should().BeEmpty();

        // No evidence created
        var evidence = await ctx.Evidence
            .Where(e => e.EvidenceType == "StigChecklist")
            .ToListAsync();
        evidence.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportCkl_DryRun_ResultsMatchNonDryRun()
    {
        var entries = new[]
        {
            BuildEntry("V-254239", "Open"),
            BuildEntry("V-254240", "NotAFinding", severity: "medium")
        };
        var parsed = BuildParsedCkl(entries);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Dry run first
        var dryResult = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, true, TestImporter);

        // Actual run
        var actualResult = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert: Counts should match
        dryResult.TotalEntries.Should().Be(actualResult.TotalEntries);
        dryResult.OpenCount.Should().Be(actualResult.OpenCount);
        dryResult.PassCount.Should().Be(actualResult.PassCount);
        dryResult.FindingsCreated.Should().Be(actualResult.FindingsCreated);
        dryResult.UnmatchedCount.Should().Be(actualResult.UnmatchedCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Error Handling & Edge Cases
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportCkl_SystemNotFound_ReturnsFailedResult()
    {
        _rmfServiceMock.Setup(s => s.GetSystemAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSystemResult.NotFound("nonexistent"));

        var result = await _service.ImportCklAsync(
            "nonexistent", null, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Status.Should().Be(ScanImportStatus.Failed);
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ImportCkl_ParseError_ReturnsFailedResult()
    {
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>()))
            .Throws(new CklParseException("bad.ckl", "Truncated XML at position 256"));

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "bad.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Status.Should().Be(ScanImportStatus.Failed);
        result.ErrorMessage.Should().Contain("Truncated XML");
    }

    [Fact]
    public async Task ImportCkl_EmptyEntries_CompletesWithZeroCounts()
    {
        var parsed = BuildParsedCkl(); // No entries
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "empty.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Status.Should().NotBe(ScanImportStatus.Failed);
        result.TotalEntries.Should().Be(0);
        result.FindingsCreated.Should().Be(0);
    }

    [Fact]
    public async Task ImportCkl_DuplicateFile_WarningEmitted()
    {
        // First import
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var fileBytes = DummyFileContent;
        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, fileBytes, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Second import with same file content
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, fileBytes, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Warnings.Should().Contain(w => w.Contains("previously imported"));
    }

    [Fact]
    public async Task ImportCkl_AssessmentNotFound_ReturnsFailedResult()
    {
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var result = await _service.ImportCklAsync(
            TestSystemId, "nonexistent-assessment", DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Status.Should().Be(ScanImportStatus.Failed);
        result.ErrorMessage.Should().Contain("Assessment");
    }

    [Fact]
    public async Task ImportCkl_NoAssessmentId_AutoCreatesAssessment()
    {
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Pass null assessmentId — should auto-create
        var result = await _service.ImportCklAsync(
            TestSystemId, null, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Status.Should().NotBe(ScanImportStatus.Failed);
        result.FindingsCreated.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Utility Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeSha256_ReturnsConsistentHash()
    {
        var data = "Hello, World!"u8.ToArray();
        var hash1 = ScanImportService.ComputeSha256(data);
        var hash2 = ScanImportService.ComputeSha256(data);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64); // 32 bytes = 64 hex chars
        hash1.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void ComputeSha256_DifferentInput_DifferentHash()
    {
        var hash1 = ScanImportService.ComputeSha256("ABC"u8.ToArray());
        var hash2 = ScanImportService.ComputeSha256("XYZ"u8.ToArray());

        hash1.Should().NotBe(hash2);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Import Management Tests (T048-T049)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListImports_BySysemId_ReturnsCorrectResults()
    {
        // Seed some import records
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test1.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Act
        var (records, total) = await _service.ListImportsAsync(
            TestSystemId, 1, 20, null, null, false, null, null);

        // Assert
        total.Should().BeGreaterThanOrEqualTo(1);
        records.Should().NotBeEmpty();
        records.Should().OnlyContain(r => r.RegisteredSystemId == TestSystemId);
    }

    [Fact]
    public async Task GetImportSummary_ExistingImport_ReturnsDetails()
    {
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var importResult = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "test.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Act
        var summary = await _service.GetImportSummaryAsync(importResult.ImportRecordId);

        // Assert
        summary.Should().NotBeNull();
        summary!.Value.Record.FileName.Should().Be("test.ckl");
        summary.Value.Findings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetImportSummary_NonExistent_ReturnsNull()
    {
        var summary = await _service.GetImportSummaryAsync("nonexistent-id");
        summary.Should().BeNull();
    }

    [Fact]
    public async Task ListImports_ExcludesDryRuns_ByDefault()
    {
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // One dry run
        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "dry.ckl",
            ImportConflictResolution.Skip, true, TestImporter);

        // One real import
        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "real.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Act: exclude dry runs (default)
        var (records, _) = await _service.ListImportsAsync(
            TestSystemId, 1, 20, null, null, false, null, null);

        // Assert: Only real import should appear
        records.Should().OnlyContain(r => !r.IsDryRun);
    }

    [Fact]
    public async Task ListImports_ByBenchmark_FiltersCorrectly()
    {
        // Arrange: create two imports with different benchmark IDs
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "win2022.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Act: filter by benchmark
        var (records, _) = await _service.ListImportsAsync(
            TestSystemId, 1, 20, "Windows_Server_2022_STIG", null, false, null, null);

        // Assert
        records.Should().OnlyContain(r =>
            r.BenchmarkId != null &&
            r.BenchmarkId.Contains("Windows_Server_2022", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListImports_SystemWithNoImports_ReturnsEmptyList()
    {
        // Act: query a system that has no imports
        var (records, total) = await _service.ListImportsAsync(
            "no-imports-system", 1, 20, null, null, false, null, null);

        // Assert
        records.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    public async Task GetImportSummary_IncludesUnmatchedRules()
    {
        // Arrange: import with an unmatched rule
        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-999999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StigControl?)null);
        _stigServiceMock.Setup(s => s.GetStigControlByRuleIdAsync(It.Is<string>(r => r.StartsWith("SV-999999")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StigControl?)null);
        _stigServiceMock.Setup(s => s.GetStigControlAsync(It.Is<string>(r => r.StartsWith("SV-999999")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StigControl?)null);

        var entries = new[]
        {
            BuildEntry("V-254239", "Open"),
            BuildEntry("V-999999", "Open", ruleId: "SV-999999r000000_rule")
        };
        var parsed = BuildParsedCkl(entries);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var importResult = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "unmatched.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Act
        var summary = await _service.GetImportSummaryAsync(importResult.ImportRecordId);

        // Assert
        summary.Should().NotBeNull();
        summary!.Value.Record.UnmatchedCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetImportSummary_IncludesFindingCounts()
    {
        var entry = BuildEntry("V-254239", "Open");
        var parsed = BuildParsedCkl(entry);
        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        var importResult = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId, DummyFileContent, "counts.ckl",
            ImportConflictResolution.Skip, false, TestImporter);

        // Act
        var summary = await _service.GetImportSummaryAsync(importResult.ImportRecordId);

        // Assert
        summary.Should().NotBeNull();
        summary!.Value.Record.TotalEntries.Should().BeGreaterThanOrEqualTo(1);
        summary.Value.Record.FindingsCreated.Should().BeGreaterThanOrEqualTo(1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T042: XCCDF Import Tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Helper to build a parsed XCCDF file with the given rule results.</summary>
    private static ParsedXccdfFile BuildParsedXccdf(params ParsedXccdfResult[] results)
    {
        return new ParsedXccdfFile(
            BenchmarkHref: "xccdf_mil.disa.stig_benchmark_Windows_Server_2022_STIG",
            Title: "SCAP SCC Scan - Windows Server 2022",
            Target: "web-server-01",
            TargetAddress: "10.0.1.100",
            StartTime: DateTime.UtcNow.AddMinutes(-15),
            EndTime: DateTime.UtcNow,
            Score: 72.5m,
            MaxScore: 100m,
            TargetFacts: new Dictionary<string, string>
            {
                ["urn:scap:fact:asset:identifier:host_name"] = "web-server-01"
            },
            Results: results.ToList());
    }

    private static ParsedXccdfResult BuildXccdfResult(
        string ruleId, string result, string severity = "high",
        string? message = null)
    {
        return new ParsedXccdfResult(
            RuleIdRef: $"xccdf_mil.disa.stig_rule_{ruleId}",
            ExtractedRuleId: ruleId,
            Result: result,
            Severity: severity,
            Weight: 10.0m,
            Timestamp: DateTime.UtcNow,
            Message: message,
            CheckRef: null);
    }

    private byte[] DummyXccdfContent => "dummy-xccdf-content"u8.ToArray();

    private void SetupXccdfRuleIdResolution()
    {
        // XCCDF uses ResolveStigControlByRuleIdAsync which first tries RuleId, then derives VulnId
        _stigServiceMock.Setup(s => s.GetStigControlByRuleIdAsync("SV-254239r849090_rule", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_stigAc2);
        _stigServiceMock.Setup(s => s.GetStigControlByRuleIdAsync("SV-254240r849093_rule", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_stigIa5);
        _stigServiceMock.Setup(s => s.GetStigControlByRuleIdAsync("SV-254241r849096_rule", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_stigSc7);
    }

    [Fact]
    public async Task ImportXccdf_FailResult_CreatesOpenFinding_OtherThanSatisfied()
    {
        // Arrange
        SetupXccdfRuleIdResolution();
        var xccdfResult = BuildXccdfResult("SV-254239r849090_rule", "fail", "high");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.Status.Should().NotBe(ScanImportStatus.Failed);
        result.FindingsCreated.Should().Be(1);
        result.OpenCount.Should().Be(1);
        result.EffectivenessRecordsCreated.Should().BeGreaterThanOrEqualTo(1);

        // Verify finding in DB
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.FirstOrDefaultAsync(f => f.StigId == "V-254239");
        finding.Should().NotBeNull();
        finding!.Status.Should().Be(FindingStatus.Open);
        finding.Source.Should().Be("XCCDF Import");

        // Verify effectiveness
        var eff = await ctx.ControlEffectivenessRecords
            .FirstOrDefaultAsync(e => e.ControlId == "AC-2");
        eff.Should().NotBeNull();
        eff!.Determination.Should().Be(EffectivenessDetermination.OtherThanSatisfied);
    }

    [Fact]
    public async Task ImportXccdf_PassResult_CreatesSatisfiedEffectiveness()
    {
        // Arrange
        SetupXccdfRuleIdResolution();
        var xccdfResult = BuildXccdfResult("SV-254241r849096_rule", "pass", "low");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.FindingsCreated.Should().Be(1);
        result.PassCount.Should().Be(1);

        // Verify effectiveness
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var eff = await ctx.ControlEffectivenessRecords
            .FirstOrDefaultAsync(e => e.ControlId == "SC-7");
        eff.Should().NotBeNull();
        eff!.Determination.Should().Be(EffectivenessDetermination.Satisfied);
    }

    [Fact]
    public async Task ImportXccdf_NotApplicable_DoesNotCreateFinding()
    {
        // Arrange
        SetupXccdfRuleIdResolution();
        var xccdfResult = BuildXccdfResult("SV-254241r849096_rule", "notapplicable", "medium");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.NotApplicableCount.Should().Be(1);
        result.FindingsCreated.Should().Be(0);
    }

    [Fact]
    public async Task ImportXccdf_ErrorResult_FlaggedForReview()
    {
        // Arrange
        SetupXccdfRuleIdResolution();
        var xccdfResult = BuildXccdfResult("SV-254239r849090_rule", "error", "high",
            message: "OVAL evaluation error");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.ErrorCount.Should().Be(1);
        result.FindingsCreated.Should().Be(0);
        result.Warnings.Should().Contain(w => w.Contains("flagged for manual review"));
    }

    [Fact]
    public async Task ImportXccdf_UnknownResult_FlaggedForReview()
    {
        // Arrange
        SetupXccdfRuleIdResolution();
        var xccdfResult = BuildXccdfResult("SV-254240r849093_rule", "unknown", "medium");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.ErrorCount.Should().Be(1);
        result.Warnings.Should().Contain(w => w.Contains("unknown"));
    }

    [Fact]
    public async Task ImportXccdf_CapturesXccdfScore()
    {
        // Arrange
        SetupXccdfRuleIdResolution();
        var xccdfResult = BuildXccdfResult("SV-254239r849090_rule", "fail", "high");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert: verify import record captured the score
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var record = await ctx.ScanImportRecords.FirstOrDefaultAsync();
        record.Should().NotBeNull();
        record!.XccdfScore.Should().Be(72.5m);
        record.ImportType.Should().Be(ScanImportType.Xccdf);
    }

    [Fact]
    public async Task ImportXccdf_CollectionMethod_IsAutomated()
    {
        // Arrange
        SetupXccdfRuleIdResolution();
        var xccdfResult = BuildXccdfResult("SV-254239r849090_rule", "fail", "high");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert: evidence should use "Automated" collection method
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var evidence = await ctx.Evidence.FirstOrDefaultAsync(e => e.EvidenceType == "XccdfScanResult");
        evidence.Should().NotBeNull();
        evidence!.CollectionMethod.Should().Be("Automated");
    }

    [Fact]
    public async Task ImportXccdf_UnmatchedRule_TrackedCorrectly()
    {
        // Arrange: rule ID not found in STIG knowledge
        _stigServiceMock.Setup(s => s.GetStigControlByRuleIdAsync("SV-999999r000000_rule", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StigControl?)null);
        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-999999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StigControl?)null);
        _stigServiceMock.Setup(s => s.GetStigControlAsync("SV-999999r000000_rule", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StigControl?)null);

        var xccdfResult = BuildXccdfResult("SV-999999r000000_rule", "fail", "high");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.UnmatchedCount.Should().Be(1);
        result.FindingsCreated.Should().Be(0);
        result.UnmatchedRules.Should().HaveCount(1);
    }

    [Fact]
    public async Task ImportXccdf_MultipleResults_CountsCorrectly()
    {
        // Arrange
        SetupXccdfRuleIdResolution();
        var results = new[]
        {
            BuildXccdfResult("SV-254239r849090_rule", "fail", "high"),
            BuildXccdfResult("SV-254240r849093_rule", "fail", "medium"),
            BuildXccdfResult("SV-254241r849096_rule", "pass", "low")
        };
        var parsed = BuildParsedXccdf(results);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.TotalEntries.Should().Be(3);
        result.OpenCount.Should().Be(2);
        result.PassCount.Should().Be(1);
        result.FindingsCreated.Should().Be(3);
    }

    [Fact]
    public async Task ImportXccdf_DryRun_DoesNotPersist()
    {
        // Arrange
        SetupXccdfRuleIdResolution();
        var xccdfResult = BuildXccdfResult("SV-254239r849090_rule", "fail", "high");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, true, TestImporter);

        // Assert
        result.FindingsCreated.Should().Be(1);

        // Nothing persisted
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var findings = await ctx.Findings.Where(f => f.Source == "XCCDF Import").ToListAsync();
        findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportXccdf_InvalidSystem_ReturnsFailed()
    {
        // Arrange: system not found
        _rmfServiceMock.Setup(s => s.GetSystemAsync("bad-sys", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSystemResult.NotFound("bad-sys"));

        var xccdfResult = BuildXccdfResult("SV-254239r849090_rule", "fail", "high");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            "bad-sys", TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.Status.Should().Be(ScanImportStatus.Failed);
        result.ErrorMessage.Should().Contain("bad-sys");
    }

    [Fact]
    public async Task ImportXccdf_SeverityMapping_CatIForHigh()
    {
        // Arrange
        SetupXccdfRuleIdResolution();
        var xccdfResult = BuildXccdfResult("SV-254239r849090_rule", "fail", "high");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.FirstOrDefaultAsync(f => f.StigId == "V-254239");
        finding.Should().NotBeNull();
        finding!.CatSeverity.Should().Be(CatSeverity.CatI);
    }

    [Fact]
    public async Task ImportXccdf_BenchmarkId_ExtractedCorrectly()
    {
        // Arrange
        SetupXccdfRuleIdResolution();
        var xccdfResult = BuildXccdfResult("SV-254239r849090_rule", "pass", "medium");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.BenchmarkId.Should().Be("Windows_Server_2022_STIG");
    }

    [Fact]
    public async Task ImportXccdf_NotcheckedResult_FlaggedAsError()
    {
        // Arrange
        SetupXccdfRuleIdResolution();
        var xccdfResult = BuildXccdfResult("SV-254240r849093_rule", "notchecked", "medium");
        var parsed = BuildParsedXccdf(xccdfResult);
        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId, DummyXccdfContent, "scan.xccdf",
            ImportConflictResolution.Skip, false, TestImporter);

        // Assert
        result.ErrorCount.Should().Be(1);
        result.Warnings.Should().Contain(w => w.Contains("notchecked"));
    }
}

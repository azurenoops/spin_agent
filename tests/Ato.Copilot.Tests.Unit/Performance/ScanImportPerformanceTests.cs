// ═══════════════════════════════════════════════════════════════════════════
// Feature 017 — Phase 10 (T062): Performance Benchmark Tests
// Generates large CKL (500 VULNs) and XCCDF (500 rule-results) payloads
// programmatically and asserts import completes within wall-clock limits.
// ═══════════════════════════════════════════════════════════════════════════

using System.Diagnostics;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Services.ScanImport;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Ato.Copilot.Tests.Unit.Performance;

/// <summary>
/// Performance benchmark tests for SCAP/STIG import operations.
/// Validates that large payloads complete within acceptable wall-clock limits.
/// </summary>
public class ScanImportPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly ScanImportService _service;
    private readonly Mock<ICklParser> _cklParserMock;
    private readonly Mock<IXccdfParser> _xccdfParserMock;

    private const string TestSystemId = "sys-perf-001";
    private const string TestAssessmentId = "assess-perf-001";

    private readonly RegisteredSystem _testSystem = new()
    {
        Id = TestSystemId,
        Name = "Performance Test System",
        CurrentRmfStep = RmfPhase.Assess,
        HostingEnvironment = "Azure Government",
        CreatedBy = "admin"
    };

    private readonly ControlBaseline _testBaseline = new()
    {
        Id = "bl-perf-001",
        RegisteredSystemId = TestSystemId,
        BaselineLevel = "Moderate",
        ControlIds = Enumerable.Range(1, 50).Select(i => $"AC-{i}").ToList(),
        CreatedBy = "admin"
    };

    public ScanImportPerformanceTests(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        var dbName = $"PerfTests_{Guid.NewGuid()}";
        services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _serviceProvider = services.BuildServiceProvider();

        // Seed data
        using var initScope = _serviceProvider.CreateScope();
        var initCtx = initScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        initCtx.Database.EnsureCreated();
        initCtx.Assessments.Add(new ComplianceAssessment
        {
            Id = TestAssessmentId,
            RegisteredSystemId = TestSystemId,
            Framework = "NIST80053",
            Status = AssessmentStatus.InProgress,
            InitiatedBy = "perf-tester"
        });
        initCtx.SaveChanges();

        // Mocks
        var stigServiceMock = new Mock<IStigKnowledgeService>();
        var baselineServiceMock = new Mock<IBaselineService>();
        var rmfServiceMock = new Mock<IRmfLifecycleService>();
        var artifactServiceMock = new Mock<IAssessmentArtifactService>();
        _cklParserMock = new Mock<ICklParser>();
        _xccdfParserMock = new Mock<IXccdfParser>();
        var cklGeneratorMock = new Mock<ICklGenerator>();

        rmfServiceMock.Setup(s => s.GetSystemAsync(TestSystemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testSystem);
        baselineServiceMock.Setup(s => s.GetBaselineAsync(TestSystemId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testBaseline);

        // Setup STIG resolution: all 500 VulnIds will resolve to a STIG control
        stigServiceMock.Setup(s => s.GetStigControlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((id, _) => Task.FromResult<StigControl?>(
                new StigControl(
                    StigId: id, VulnId: id,
                    RuleId: $"SV-{id[2..]}r1_rule",
                    Title: $"Test Rule {id}",
                    Description: "Performance test STIG control",
                    Severity: StigSeverity.Medium,
                    Category: "CAT II",
                    StigFamily: "Performance Test",
                    NistControls: new List<string> { $"AC-{Math.Abs(id.GetHashCode()) % 50 + 1}" },
                    CciRefs: new List<string> { $"CCI-{Math.Abs(id.GetHashCode()) % 9999:D6}" },
                    CheckText: "Check",
                    FixText: "Fix",
                    AzureImplementation: new Dictionary<string, string>(),
                    ServiceType: "Test",
                    BenchmarkId: "Perf_Test_STIG")));

        stigServiceMock.Setup(s => s.GetStigControlByRuleIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((ruleId, _) =>
            {
                var vulnNum = ruleId.Replace("SV-", "").Split('r')[0];
                var vulnId = $"V-{vulnNum}";
                return Task.FromResult<StigControl?>(
                    new StigControl(
                        StigId: vulnId, VulnId: vulnId,
                        RuleId: ruleId,
                        Title: $"Test Rule {vulnId}",
                        Description: "Performance test STIG control",
                        Severity: StigSeverity.Medium,
                        Category: "CAT II",
                        StigFamily: "Performance Test",
                        NistControls: new List<string> { $"AC-{Math.Abs(vulnId.GetHashCode()) % 50 + 1}" },
                        CciRefs: new List<string> { $"CCI-{Math.Abs(vulnId.GetHashCode()) % 9999:D6}" },
                        CheckText: "Check",
                        FixText: "Fix",
                        AzureImplementation: new Dictionary<string, string>(),
                        ServiceType: "Test",
                        BenchmarkId: "Perf_Test_STIG"));
            });

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _service = new ScanImportService(
            scopeFactory,
            stigServiceMock.Object,
            baselineServiceMock.Object,
            rmfServiceMock.Object,
            artifactServiceMock.Object,
            _cklParserMock.Object,
            _xccdfParserMock.Object,
            cklGeneratorMock.Object,
            Mock.Of<ISystemSubscriptionResolver>(),
            new PrismaCsvParser(NullLogger<PrismaCsvParser>.Instance),
            new PrismaApiJsonParser(NullLogger<PrismaApiJsonParser>.Instance),
            Mock.Of<INessusParser>(),
            Mock.Of<INessusControlMapper>(),
            NullLogger<ScanImportService>.Instance);
    }

    public void Dispose() => _serviceProvider.Dispose();

    // ═════════════════════════════════════════════════════════════════════
        // CKL Performance: 500 VULNs < 10 seconds (existing)
        // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportCkl_500Vulns_CompletesWithin10Seconds()
    {
        // Arrange: Generate 500 CKL entries
        var entries = Enumerable.Range(100000, 500).Select(i =>
        {
            var vulnId = $"V-{i}";
            var statuses = new[] { "Open", "NotAFinding", "Not_Applicable", "Not_Reviewed" };
            var status = statuses[i % 4];
            var severity = (i % 3) switch { 0 => "high", 1 => "medium", _ => "low" };
            return new ParsedCklEntry(
                VulnId: vulnId,
                RuleId: $"SV-{i}r1_rule",
                StigVersion: $"WN22-TEST-{i:D6}",
                RuleTitle: $"Test Rule {vulnId}",
                Severity: severity,
                Status: status,
                FindingDetails: $"Details for {vulnId}",
                Comments: null,
                SeverityOverride: null,
                SeverityJustification: null,
                CciRefs: new List<string> { $"CCI-{i % 9999:D6}" },
                GroupTitle: "SRG-OS-000003-GPOS-00004");
        }).ToList();

        var parsed = new ParsedCklFile(
            Asset: new CklAssetInfo("perf-server", "10.0.1.1", "perf.example.mil", "AA:BB:CC:DD:EE:FF", "Computing", "9999"),
            StigInfo: new CklStigInfo("Perf_Test_STIG", "1", "Release: 1", "Performance Test STIG"),
            Entries: entries);

        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var sw = Stopwatch.StartNew();
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId,
            "perf-ckl-content"u8.ToArray(), "perf_500.ckl",
            ImportConflictResolution.Skip, false, "perf-user");
        sw.Stop();

        // Assert
        _output.WriteLine($"CKL 500 VULNs import completed in {sw.ElapsedMilliseconds}ms");
        result.Status.Should().NotBe(ScanImportStatus.Failed,
            $"Import failed with: {result.ErrorMessage}");
        result.TotalEntries.Should().Be(500);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
            $"Import took {sw.ElapsedMilliseconds}ms, expected < 10000ms");
    }

    // ═════════════════════════════════════════════════════════════════════
    // XCCDF Performance: 500 rule-results < 5 seconds
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportXccdf_500RuleResults_CompletesWithin5Seconds()
    {
        // Arrange: Generate 500 XCCDF results
        var results = Enumerable.Range(200000, 500).Select(i =>
        {
            var ruleId = $"xccdf_mil.disa.stig_rule_SV-{i}r1_rule";
            var outcomes = new[] { "pass", "fail", "notapplicable", "error" };
            var outcome = outcomes[i % 4];
            var severity = (i % 3) switch { 0 => "high", 1 => "medium", _ => "low" };
            return new ParsedXccdfResult(
                RuleIdRef: ruleId,
                ExtractedRuleId: $"SV-{i}r1_rule",
                Result: outcome,
                Severity: severity,
                Weight: 10.0m,
                Timestamp: DateTime.UtcNow,
                Message: null,
                CheckRef: null);
        }).ToList();

        var parsedXccdf = new ParsedXccdfFile(
            BenchmarkHref: "xccdf_mil.disa.stig_benchmark_Perf_Test_STIG",
            Title: "Performance Test STIG",
            Target: "perf-server",
            TargetAddress: "10.0.1.1",
            StartTime: DateTime.UtcNow.AddMinutes(-5),
            EndTime: DateTime.UtcNow,
            Score: 75.0m,
            MaxScore: 100.0m,
            TargetFacts: new Dictionary<string, string> { ["os"] = "Windows" },
            Results: results);

        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsedXccdf);

        // Act
        var sw = Stopwatch.StartNew();
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId,
            "perf-xccdf-content"u8.ToArray(), "perf_500.xccdf",
            ImportConflictResolution.Skip, false, "perf-user");
        sw.Stop();

        // Assert
        _output.WriteLine($"XCCDF 500 rule-results import completed in {sw.ElapsedMilliseconds}ms");
        result.Status.Should().NotBe(ScanImportStatus.Failed,
            $"Import failed with: {result.ErrorMessage}");
        result.TotalEntries.Should().Be(500);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            $"Import took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Task #176 (Epic #131) — 1K and 5K benchmarks (XmlReader streaming)
    // p99 target: CKL 5K < 10s, XCCDF 5K < 10s
    // ═════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1_000)]
    [InlineData(5_000)]
    public async Task ImportCkl_LargeFile_CompletesWithin10Seconds(int entryCount)
    {
        // Arrange
        var entries = Enumerable.Range(300_000, entryCount).Select(i =>
        {
            var statuses  = new[] { "Open", "NotAFinding", "Not_Applicable", "Not_Reviewed" };
            var severities = new[] { "high", "medium", "low" };
            return new ParsedCklEntry(
                VulnId:    $"V-{i}",
                RuleId:    $"SV-{i}r1_rule",
                StigVersion: $"WN22-TEST-{i:D6}",
                RuleTitle: $"Streaming Test Rule V-{i}",
                Severity:  severities[i % 3],
                Status:    statuses[i % 4],
                FindingDetails: $"XmlReader-streamed finding {i}",
                Comments:  null,
                SeverityOverride: null,
                SeverityJustification: null,
                CciRefs:   new List<string> { $"CCI-{i % 9999:D6}" },
                GroupTitle: "SRG-OS-000003-GPOS-00004");
        }).ToList();

        var parsed = new ParsedCklFile(
            Asset:    new CklAssetInfo("stream-server", "10.0.2.1", "stream.example.mil", "BB:CC:DD:EE:FF:AA", "Computing", "8888"),
            StigInfo: new CklStigInfo("Streaming_Test_STIG", "1", "Release: 1", "Streaming Performance STIG"),
            Entries:  entries);

        _cklParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsed);

        // Act
        var sw = Stopwatch.StartNew();
        var result = await _service.ImportCklAsync(
            TestSystemId, TestAssessmentId,
            "perf-ckl-content"u8.ToArray(), $"perf_{entryCount}.ckl",
            ImportConflictResolution.Skip, false, "perf-user");
        sw.Stop();

        // Assert
        _output.WriteLine($"CKL {entryCount} VULNs import completed in {sw.ElapsedMilliseconds}ms");
        result.Status.Should().NotBe(ScanImportStatus.Failed,
            $"Import failed with: {result.ErrorMessage}");
        result.TotalEntries.Should().Be(entryCount);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
            $"CKL {entryCount} import took {sw.ElapsedMilliseconds}ms, expected < 10s");
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(5_000)]
    public async Task ImportXccdf_LargeFile_CompletesWithin10Seconds(int entryCount)
    {
        // Arrange
        var outcomes   = new[] { "pass", "fail", "notapplicable", "error" };
        var severities = new[] { "high", "medium", "low" };
        var results = Enumerable.Range(400_000, entryCount).Select(i =>
            new ParsedXccdfResult(
                RuleIdRef:      $"xccdf_mil.disa.stig_rule_SV-{i}r1_rule",
                ExtractedRuleId: $"SV-{i}r1_rule",
                Result:         outcomes[i % 4],
                Severity:       severities[i % 3],
                Weight:         10.0m,
                Timestamp:      DateTime.UtcNow,
                Message:        null,
                CheckRef:       null)
        ).ToList();

        var parsedXccdf = new ParsedXccdfFile(
            BenchmarkHref: "xccdf_mil.disa.stig_benchmark_Streaming_Test_STIG",
            Title:         "Streaming Performance Test STIG",
            Target:        "stream-server",
            TargetAddress: "10.0.2.1",
            StartTime:     DateTime.UtcNow.AddMinutes(-10),
            EndTime:       DateTime.UtcNow,
            Score:         80.0m,
            MaxScore:      100.0m,
            TargetFacts:   new Dictionary<string, string> { ["os"] = "Windows Server 2022" },
            Results:       results);

        _xccdfParserMock.Setup(p => p.Parse(It.IsAny<byte[]>(), It.IsAny<string>())).Returns(parsedXccdf);

        // Act
        var sw = Stopwatch.StartNew();
        var result = await _service.ImportXccdfAsync(
            TestSystemId, TestAssessmentId,
            "perf-xccdf-content"u8.ToArray(), $"perf_{entryCount}.xccdf",
            ImportConflictResolution.Skip, false, "perf-user");
        sw.Stop();

        // Assert
        _output.WriteLine($"XCCDF {entryCount} rule-results import completed in {sw.ElapsedMilliseconds}ms");
        result.Status.Should().NotBe(ScanImportStatus.Failed,
            $"Import failed with: {result.ErrorMessage}");
        result.TotalEntries.Should().Be(entryCount);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
            $"XCCDF {entryCount} import took {sw.ElapsedMilliseconds}ms, expected < 10s");
    }
}

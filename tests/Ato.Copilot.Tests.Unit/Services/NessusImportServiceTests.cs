// ═══════════════════════════════════════════════════════════════════════════
// Feature 026 — ACAS/Nessus Scan Import: Service Tests
// Covers T009 (severity mapping), T010 (dedup key / SHA-256).
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
/// Unit tests for <see cref="ScanImportService"/> Nessus import — Feature 026.
/// Uses EF Core InMemory provider for DbContext, mocked services for external dependencies.
/// </summary>
public class NessusImportServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IRmfLifecycleService> _rmfServiceMock;
    private readonly Mock<IBaselineService> _baselineServiceMock;
    private readonly Mock<INessusParser> _nessusParserMock;
    private readonly Mock<INessusControlMapper> _nessusControlMapperMock;
    private readonly ScanImportService _service;

    private const string TestSystemId = "sys-nessus-001";
    private const string TestAssessmentId = "assess-nessus-001";
    private const string TestImporter = "test-user";

    private readonly RegisteredSystem _testSystem = new()
    {
        Id = TestSystemId,
        Name = "Nessus Test System",
        CurrentRmfStep = RmfPhase.Assess,
        HostingEnvironment = "Azure Government",
        CreatedBy = "admin"
    };

    private readonly ControlBaseline _testBaseline = new()
    {
        Id = "bl-nessus-001",
        RegisteredSystemId = TestSystemId,
        BaselineLevel = "Moderate",
        ControlIds = new List<string> { "RA-5", "SI-2", "AC-2" },
        CreatedBy = "admin"
    };

    public NessusImportServiceTests()
    {
        var services = new ServiceCollection();
        var dbName = $"NessusImportTests_{Guid.NewGuid()}";
        services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _serviceProvider = services.BuildServiceProvider();

        using var initScope = _serviceProvider.CreateScope();
        var initCtx = initScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        initCtx.Database.EnsureCreated();
        initCtx.Assessments.Add(new ComplianceAssessment
        {
            Id = TestAssessmentId,
            RegisteredSystemId = TestSystemId,
            Framework = "NIST80053",
            Status = AssessmentStatus.InProgress,
            InitiatedBy = TestImporter
        });
        initCtx.SaveChanges();

        _rmfServiceMock = new Mock<IRmfLifecycleService>();
        _baselineServiceMock = new Mock<IBaselineService>();
        _nessusParserMock = new Mock<INessusParser>();
        _nessusControlMapperMock = new Mock<INessusControlMapper>();

        // Default control mapper: return RA-5 heuristic mapping for any plugin
        _nessusControlMapperMock
            .Setup(m => m.MapAsync(It.IsAny<NessusPluginResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NessusControlMappingResult(
                NistControlIds: new List<string> { "RA-5" },
                CciRefs: new List<string>(),
                MappingSource: NessusControlMappingSource.PluginFamilyHeuristic));

        _rmfServiceMock.Setup(s => s.GetSystemAsync(TestSystemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSystemResult.Found(_testSystem));
        _baselineServiceMock.Setup(s => s.GetBaselineAsync(TestSystemId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testBaseline);

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _service = new ScanImportService(
            scopeFactory,
            Mock.Of<IStigKnowledgeService>(),
            _baselineServiceMock.Object,
            _rmfServiceMock.Object,
            Mock.Of<IAssessmentArtifactService>(),
            Mock.Of<ICklParser>(),
            Mock.Of<IXccdfParser>(),
            Mock.Of<ICklGenerator>(),
            Mock.Of<ISystemSubscriptionResolver>(),
            new PrismaCsvParser(NullLogger<PrismaCsvParser>.Instance),
            new PrismaApiJsonParser(NullLogger<PrismaApiJsonParser>.Instance),
            _nessusParserMock.Object,
            _nessusControlMapperMock.Object,
            NullLogger<ScanImportService>.Instance);
    }

    public void Dispose() => _serviceProvider.Dispose();

    // ── Helper: Build parsed Nessus output ────────────────────────────────

    private static ParsedNessusFile BuildParsedNessus(params NessusReportHost[] hosts)
    {
        var allPlugins = hosts.Sum(h => h.PluginResults.Count);
        return new ParsedNessusFile(
            ReportName: "Test Nessus Scan",
            Hosts: hosts.ToList(),
            TotalPluginResults: allPlugins,
            InformationalCount: 0);
    }

    private static NessusReportHost BuildHost(
        string hostname, string ip, bool credentialed = true,
        params NessusPluginResult[] plugins)
    {
        return new NessusReportHost(
            Name: hostname,
            HostIp: ip,
            Hostname: hostname,
            OperatingSystem: "Windows Server 2022",
            MacAddress: "00:0C:29:AA:BB:CC",
            CredentialedScan: credentialed,
            ScanStart: new DateTime(2025, 3, 12, 8, 0, 0, DateTimeKind.Utc),
            ScanEnd: new DateTime(2025, 3, 12, 8, 45, 0, DateTimeKind.Utc),
            PluginResults: plugins.ToList());
    }

    private static NessusPluginResult BuildPlugin(
        int pluginId, int severity, string pluginName = "Test Plugin",
        string pluginFamily = "General", int port = 0,
        string riskFactor = "Medium", string? protocol = "tcp",
        List<string>? cves = null, List<string>? xrefs = null)
    {
        return new NessusPluginResult(
            PluginId: pluginId,
            PluginName: pluginName,
            PluginFamily: pluginFamily,
            Severity: severity,
            RiskFactor: riskFactor,
            Port: port,
            Protocol: protocol,
            ServiceName: null,
            Synopsis: "Test synopsis",
            Description: "Test description",
            Solution: "Test solution",
            PluginOutput: null,
            Cves: cves ?? new List<string>(),
            Xrefs: xrefs ?? new List<string>(),
            CvssV2BaseScore: null,
            CvssV3BaseScore: null,
            CvssV3Vector: null,
            VprScore: null,
            ExploitAvailable: false,
            StigSeverity: null);
    }

    private static byte[] DummyContent => "dummy-nessus-content"u8.ToArray();

    // ═══════════════════════════════════════════════════════════════════════
    // T009: Severity Mapping Tests (UT-004 through UT-007)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MapNessusSeverity_Critical4_ReturnsCatI()
    {
        // UT-004: Nessus severity 4 (Critical) → CAT I
        var (severity, cat) = ScanImportService.MapNessusSeverity(4);
        severity.Should().Be(FindingSeverity.Critical);
        cat.Should().Be(CatSeverity.CatI);
    }

    [Fact]
    public void MapNessusSeverity_High3_ReturnsCatI()
    {
        // UT-005: Nessus severity 3 (High) → CAT I
        var (severity, cat) = ScanImportService.MapNessusSeverity(3);
        severity.Should().Be(FindingSeverity.High);
        cat.Should().Be(CatSeverity.CatI);
    }

    [Fact]
    public void MapNessusSeverity_Medium2_ReturnsCatII()
    {
        // UT-006: Nessus severity 2 (Medium) → CAT II
        var (severity, cat) = ScanImportService.MapNessusSeverity(2);
        severity.Should().Be(FindingSeverity.Medium);
        cat.Should().Be(CatSeverity.CatII);
    }

    [Fact]
    public void MapNessusSeverity_Low1_ReturnsCatIII()
    {
        // UT-007: Nessus severity 1 (Low) → CAT III
        var (severity, cat) = ScanImportService.MapNessusSeverity(1);
        severity.Should().Be(FindingSeverity.Low);
        cat.Should().Be(CatSeverity.CatIII);
    }

    [Fact]
    public void MapNessusSeverity_Info0_ReturnsInformational()
    {
        var (severity, cat) = ScanImportService.MapNessusSeverity(0);
        severity.Should().Be(FindingSeverity.Informational);
        cat.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T010: Import Infrastructure Tests (UT-013, UT-014)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeSha256_DeterministicHash()
    {
        // UT-013: SHA-256 hash computation is deterministic
        var data = "test-content"u8.ToArray();
        var hash1 = ScanImportService.ComputeSha256(data);
        var hash2 = ScanImportService.ComputeSha256(data);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64); // 32 bytes → 64 hex chars
    }

    [Fact]
    public async Task ImportNessus_SamePluginSameHostDifferentPort_CreatesDistinctFindings()
    {
        // UT-014: same PluginID + Hostname but different Port → distinct findings
        var host = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(12345, 3, port: 443),
            BuildPlugin(12345, 3, port: 8443));

        var parsed = BuildParsedNessus(host);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed);

        var result = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "test.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Status.Should().NotBe(ScanImportStatus.Failed);
        result.FindingsCreated.Should().Be(2, "different ports create distinct findings");
    }

    [Fact]
    public async Task ImportNessus_DuplicatePluginHostPort_SkippedOnReimport()
    {
        // UT-014 addendum: same PluginID + Hostname + Port on re-import → skipped
        var host = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(12345, 3, port: 443));

        var parsed = BuildParsedNessus(host);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed);

        // First import
        var result1 = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "scan1.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result1.FindingsCreated.Should().Be(1);

        // Second import with same content
        var result2 = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "scan2.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result2.SkippedCount.Should().Be(1, "duplicate dedup key should be skipped");
        result2.FindingsCreated.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Additional service-level tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportNessus_ValidFile_CreatesScanImportRecord()
    {
        var host = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(97833, 4, "MS17-010", "Windows : Microsoft Bulletins", 445, "Critical"),
            BuildPlugin(10287, 1, "Traceroute", "General", 0, "Low"));

        var parsed = BuildParsedNessus(host);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed);

        var result = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "test.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Status.Should().NotBe(ScanImportStatus.Failed);
        result.CriticalCount.Should().Be(1);
        result.LowCount.Should().Be(1);
        result.HostCount.Should().Be(1);
        result.FindingsCreated.Should().Be(2);
        result.CredentialedScan.Should().BeTrue();
    }

    [Fact]
    public async Task ImportNessus_NonexistentSystem_ReturnsFailed()
    {
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>()))
            .Returns(BuildParsedNessus());

        _rmfServiceMock.Setup(s => s.GetSystemAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSystemResult.NotFound("nonexistent"));

        var result = await _service.ImportNessusAsync(
            "nonexistent", null, DummyContent, "test.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Status.Should().Be(ScanImportStatus.Failed);
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ImportNessus_ParseError_ReturnsFailed()
    {
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>()))
            .Throws(new NessusParseException("Invalid .nessus file"));

        var result = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "bad.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Status.Should().Be(ScanImportStatus.Failed);
        result.ErrorMessage.Should().Contain("parse error");
    }

    [Fact]
    public async Task ImportNessus_SeverityBreakdown_CountsCorrectly()
    {
        var host = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(1, 4, riskFactor: "Critical"),
            BuildPlugin(2, 4, riskFactor: "Critical"),
            BuildPlugin(3, 3, riskFactor: "High"),
            BuildPlugin(4, 2, riskFactor: "Medium"),
            BuildPlugin(5, 2, riskFactor: "Medium"),
            BuildPlugin(6, 2, riskFactor: "Medium"),
            BuildPlugin(7, 1, riskFactor: "Low"));

        var parsed = BuildParsedNessus(host);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed);

        var result = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "test.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result.CriticalCount.Should().Be(2);
        result.HighCount.Should().Be(1);
        result.MediumCount.Should().Be(3);
        result.LowCount.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T016: Control Mapping Tests (UT-009, UT-010, UT-011, UT-012, UT-021)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ControlMapping_StigIdXref_ResolvesViaCciChain()
    {
        // UT-009: STIG-ID xref → CCI → NIST chain (Definitive confidence)
        var stigControl = new StigControl(
            StigId: "V-12345", VulnId: "V-12345", RuleId: "SV-12345r1_rule",
            Title: "Test STIG", Description: "Test", Severity: StigSeverity.High,
            Category: "CAT I", StigFamily: "Windows",
            NistControls: new List<string> { "SI-2", "CM-6" },
            CciRefs: new List<string> { "CCI-000366", "CCI-001453" },
            CheckText: "Check", FixText: "Fix",
            AzureImplementation: new Dictionary<string, string>(),
            ServiceType: "Windows",
            StigVersion: "WN19-00-000010");

        var stigServiceMock = new Mock<IStigKnowledgeService>();
        stigServiceMock.Setup(s => s.GetStigControlAsync("WN19-00-000010", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stigControl);

        var familyMappings = new PluginFamilyMappings(NullLogger<PluginFamilyMappings>.Instance);
        var mapper = new NessusControlMapper(
            stigServiceMock.Object, familyMappings, NullLogger<NessusControlMapper>.Instance);

        var plugin = BuildPlugin(97833, 4,
            pluginFamily: "Windows : Microsoft Bulletins",
            xrefs: new List<string> { "STIG-ID:WN19-00-000010", "IAVA:2017-A-0065" });

        var result = await mapper.MapAsync(plugin);

        result.MappingSource.Should().Be(NessusControlMappingSource.StigXref);
        result.NistControlIds.Should().Contain("SI-2");
        result.NistControlIds.Should().Contain("CM-6");
        result.CciRefs.Should().Contain("CCI-000366");
    }

    [Fact]
    public async Task ControlMapping_PluginFamilyHeuristic_MapsKnownFamily()
    {
        // UT-010: Plugin family heuristic mapping (no STIG-ID xref)
        var stigServiceMock = new Mock<IStigKnowledgeService>();
        var familyMappings = new PluginFamilyMappings(NullLogger<PluginFamilyMappings>.Instance);
        var mapper = new NessusControlMapper(
            stigServiceMock.Object, familyMappings, NullLogger<NessusControlMapper>.Instance);

        var plugin = BuildPlugin(104743, 3,
            pluginFamily: "Web Servers",
            xrefs: new List<string>()); // no STIG-ID xref

        var result = await mapper.MapAsync(plugin);

        result.MappingSource.Should().Be(NessusControlMappingSource.PluginFamilyHeuristic);
        result.NistControlIds.Should().Contain("SI-2"); // Web Servers → SI-2
        result.CciRefs.Should().BeEmpty();
    }

    [Fact]
    public async Task ControlMapping_HeuristicFlag_MarkedAsHeuristic()
    {
        // UT-011: Heuristic-mapped findings flagged as "Heuristic" not "Definitive"
        var stigServiceMock = new Mock<IStigKnowledgeService>();
        var familyMappings = new PluginFamilyMappings(NullLogger<PluginFamilyMappings>.Instance);
        var mapper = new NessusControlMapper(
            stigServiceMock.Object, familyMappings, NullLogger<NessusControlMapper>.Instance);

        var plugin = BuildPlugin(57582, 2, pluginFamily: "Firewalls");

        var result = await mapper.MapAsync(plugin);

        result.MappingSource.Should().Be(NessusControlMappingSource.PluginFamilyHeuristic);
        result.MappingSource.Should().NotBe(NessusControlMappingSource.StigXref);
        result.NistControlIds.Should().Contain("SC-7"); // Firewalls → SC-7
    }

    [Fact]
    public async Task ControlMapping_UnresolvedStigId_FallsBackToHeuristic()
    {
        // UT-012: Plugin with STIG-ID xref that doesn't resolve → heuristic fallback + warning
        var stigServiceMock = new Mock<IStigKnowledgeService>();
        // GetStigControlAsync returns null (not found)
        stigServiceMock.Setup(s => s.GetStigControlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StigControl?)null);
        stigServiceMock.Setup(s => s.SearchStigsAsync(It.IsAny<string>(), null, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StigControl>());

        var familyMappings = new PluginFamilyMappings(NullLogger<PluginFamilyMappings>.Instance);
        var mapper = new NessusControlMapper(
            stigServiceMock.Object, familyMappings, NullLogger<NessusControlMapper>.Instance);

        var plugin = BuildPlugin(99999, 3,
            pluginFamily: "DNS",
            xrefs: new List<string> { "STIG-ID:NONEXISTENT-00-000001" });

        var result = await mapper.MapAsync(plugin);

        // Should fall back to plugin family heuristic
        result.MappingSource.Should().Be(NessusControlMappingSource.PluginFamilyHeuristic);
        result.NistControlIds.Should().Contain("SC-20"); // DNS → SC-20
    }

    [Fact]
    public void ControlMapping_MappingTableCompleteness_Has35Entries()
    {
        // UT-021: Plugin family mapping table completeness — all entries resolve to valid NIST controls
        var familyMappings = new PluginFamilyMappings(NullLogger<PluginFamilyMappings>.Instance);

        familyMappings.Count.Should().BeGreaterThanOrEqualTo(35);

        // Spot-check known families
        var bulletins = familyMappings.GetMapping("Windows : Microsoft Bulletins");
        bulletins.PrimaryControl.Should().Be("SI-2");

        var firewalls = familyMappings.GetMapping("Firewalls");
        firewalls.PrimaryControl.Should().Be("SC-7");

        var dns = familyMappings.GetMapping("DNS");
        dns.PrimaryControl.Should().Be("SC-20");

        var backdoors = familyMappings.GetMapping("Backdoors");
        backdoors.PrimaryControl.Should().Be("SI-3");

        // Unknown family defaults to RA-5
        var unknown = familyMappings.GetMapping("SomeUnknownFamily");
        unknown.PrimaryControl.Should().Be("RA-5");
    }

    [Fact]
    public void ExtractStigIds_ParsesStigIdXrefs()
    {
        var xrefs = new List<string>
        {
            "STIG-ID:WN19-00-000010",
            "IAVA:2017-A-0065",
            "STIG-ID:WN19-SO-000030",
            "CWE:79"
        };

        var stigIds = NessusControlMapper.ExtractStigIds(xrefs);

        stigIds.Should().HaveCount(2);
        stigIds.Should().Contain("WN19-00-000010");
        stigIds.Should().Contain("WN19-SO-000030");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T026: Conflict Resolution Tests (UT-015, UT-016, UT-017)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportNessus_SkipResolution_KeepsExistingFindingUnchanged()
    {
        // UT-015: Skip keeps existing finding unchanged
        var host = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(12345, 3, pluginName: "Original Plugin", port: 443));
        var parsed = BuildParsedNessus(host);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed);

        // First import creates the finding
        var result1 = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "scan1.nessus",
            ImportConflictResolution.Skip, false, TestImporter);
        result1.FindingsCreated.Should().Be(1);

        // Re-import with updated plugin name — Skip should keep existing
        var host2 = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(12345, 4, pluginName: "Updated Plugin", port: 443));
        var parsed2 = BuildParsedNessus(host2);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed2);

        var result2 = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "scan2.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result2.SkippedCount.Should().Be(1);
        result2.FindingsCreated.Should().Be(0);
        result2.FindingsUpdated.Should().Be(0);
    }

    [Fact]
    public async Task ImportNessus_OverwriteResolution_ReplacesExistingFindingFields()
    {
        // UT-016: Overwrite replaces existing finding fields
        var host = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(12345, 2, pluginName: "Original Plugin", port: 443, riskFactor: "Medium"));
        var parsed = BuildParsedNessus(host);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed);

        // First import
        await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "scan1.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        // Re-import with higher severity — Overwrite should replace
        var host2 = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(12345, 4, pluginName: "Updated Plugin", port: 443, riskFactor: "Critical"));
        var parsed2 = BuildParsedNessus(host2);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed2);

        var result2 = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "scan2.nessus",
            ImportConflictResolution.Overwrite, false, TestImporter);

        result2.FindingsUpdated.Should().Be(1);
        result2.FindingsCreated.Should().Be(0);
        result2.SkippedCount.Should().Be(0);
    }

    [Fact]
    public async Task ImportNessus_MergeResolution_AppendsNewDetails()
    {
        // UT-017: Merge appends new details
        var host = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(12345, 3, pluginName: "Test Plugin", port: 443));
        var parsed = BuildParsedNessus(host);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed);

        // First import
        await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "scan1.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        // Re-import with Merge — should append details
        var host2 = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(12345, 3, pluginName: "Test Plugin", port: 443));
        var parsed2 = BuildParsedNessus(host2);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed2);

        var result2 = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "scan2.nessus",
            ImportConflictResolution.Merge, false, TestImporter);

        result2.FindingsUpdated.Should().Be(1);
        result2.FindingsCreated.Should().Be(0);
        result2.SkippedCount.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T031: POA&M Weakness Tests (UT-022 through UT-026)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportNessus_CriticalFinding_CreatesPoamWeakness()
    {
        // UT-022: Critical severity finding creates POA&M entry
        var host = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(97833, 4, pluginName: "MS17-010 Critical Vuln", port: 445, riskFactor: "Critical"));
        var parsed = BuildParsedNessus(host);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed);

        var result = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "test.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result.PoamWeaknessesCreated.Should().BeGreaterThan(0, "Critical findings should create POA&M entries");
    }

    [Fact]
    public async Task ImportNessus_HighFinding_CreatesPoamWeakness()
    {
        // UT-023: High severity finding creates POA&M entry
        var host = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(97834, 3, pluginName: "High Severity Vuln", port: 443, riskFactor: "High"));
        var parsed = BuildParsedNessus(host);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed);

        var result = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "test.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result.PoamWeaknessesCreated.Should().BeGreaterThan(0, "High findings should create POA&M entries");
    }

    [Fact]
    public async Task ImportNessus_MediumFinding_CreatesPoamWeakness()
    {
        // UT-024: Medium severity finding creates POA&M entry
        var host = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(97835, 2, pluginName: "Medium Severity Vuln", port: 80, riskFactor: "Medium"));
        var parsed = BuildParsedNessus(host);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed);

        var result = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "test.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result.PoamWeaknessesCreated.Should().BeGreaterThan(0, "Medium findings should create POA&M entries");
    }

    [Fact]
    public async Task ImportNessus_LowFinding_NoPoamWeakness()
    {
        // UT-025: Low severity finding excluded from POA&M
        var host = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(10287, 1, pluginName: "Low Severity Vuln", port: 0, riskFactor: "Low"));
        var parsed = BuildParsedNessus(host);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed);

        var result = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "test.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result.PoamWeaknessesCreated.Should().Be(0, "Low findings should not create POA&M entries");
    }

    [Fact]
    public async Task ImportNessus_InformationalFinding_NoPoamWeakness()
    {
        // UT-026: Informational finding excluded from POA&M
        var host = BuildHost("server01", "10.0.1.50", true,
            BuildPlugin(10335, 0, pluginName: "Informational Plugin", port: 0, riskFactor: "None"));
        var parsed = BuildParsedNessus(host);
        _nessusParserMock.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(parsed);

        var result = await _service.ImportNessusAsync(
            TestSystemId, TestAssessmentId, DummyContent, "test.nessus",
            ImportConflictResolution.Skip, false, TestImporter);

        result.PoamWeaknessesCreated.Should().Be(0, "Informational findings should not create POA&M entries");
    }
}

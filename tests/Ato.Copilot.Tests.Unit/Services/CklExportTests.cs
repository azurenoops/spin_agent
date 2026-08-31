// ═══════════════════════════════════════════════════════════════════════════
// Feature 017 — SCAP/STIG Import: CKL Generator & Export Tests (T047)
// Covers CklGenerator XML output, STATUS mapping, CCI_REF inclusion,
// round-trip parsing, no-findings behavior, partial assessments,
// and ExportCklAsync service integration.
// ═══════════════════════════════════════════════════════════════════════════

using System.Xml.Linq;
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
/// Unit tests for <see cref="CklGenerator"/> and <see cref="ScanImportService.ExportCklAsync"/>.
/// </summary>
public class CklExportTests : IDisposable
{
    private readonly CklGenerator _generator;
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IStigKnowledgeService> _stigServiceMock;
    private readonly Mock<IBaselineService> _baselineServiceMock;
    private readonly Mock<IRmfLifecycleService> _rmfServiceMock;
    private readonly Mock<IAssessmentArtifactService> _artifactServiceMock;
    private readonly Mock<ICklParser> _cklParserMock;
    private readonly Mock<IXccdfParser> _xccdfParserMock;
    private readonly Mock<ICklGenerator> _cklGeneratorMock;
    private readonly ScanImportService _service;

    private const string TestSystemId = "sys-export-001";
    private const string TestAssessmentId = "assess-export-001";
    private const string TestBenchmarkId = "Windows_Server_2022_STIG";

    private readonly RegisteredSystem _testSystem = new()
    {
        Id = TestSystemId,
        Name = "Export Test System",
        CurrentRmfStep = RmfPhase.Assess,
        HostingEnvironment = "Azure Government",
        CreatedBy = "admin"
    };

    // Reusable STIG controls
    private readonly StigControl _stig1 = new(
        StigId: "V-254239", VulnId: "V-254239",
        RuleId: "SV-254239r849090_rule",
        Title: "Audit account management",
        Description: "Test desc",
        Severity: StigSeverity.High,
        Category: "CAT I",
        StigFamily: "Windows Server 2022",
        NistControls: new List<string> { "AC-2" },
        CciRefs: new List<string> { "CCI-000018", "CCI-000172" },
        CheckText: "Check", FixText: "Fix",
        AzureImplementation: new Dictionary<string, string>(),
        ServiceType: "Windows",
        StigVersion: "WN22-AU-000010",
        BenchmarkId: TestBenchmarkId);

    private readonly StigControl _stig2 = new(
        StigId: "V-254240", VulnId: "V-254240",
        RuleId: "SV-254240r849093_rule",
        Title: "Password complexity",
        Description: "Test desc",
        Severity: StigSeverity.Medium,
        Category: "CAT II",
        StigFamily: "Windows Server 2022",
        NistControls: new List<string> { "IA-5" },
        CciRefs: new List<string> { "CCI-000192" },
        CheckText: "Check", FixText: "Fix",
        AzureImplementation: new Dictionary<string, string>(),
        ServiceType: "Windows",
        StigVersion: "WN22-SO-000060",
        BenchmarkId: TestBenchmarkId);

    private readonly StigControl _stig3 = new(
        StigId: "V-254241", VulnId: "V-254241",
        RuleId: "SV-254241r849096_rule",
        Title: "Remote connections",
        Description: "Test desc",
        Severity: StigSeverity.Low,
        Category: "CAT III",
        StigFamily: "Windows Server 2022",
        NistControls: new List<string> { "SC-7" },
        CciRefs: new List<string> { "CCI-001097" },
        CheckText: "Check", FixText: "Fix",
        AzureImplementation: new Dictionary<string, string>(),
        ServiceType: "Windows",
        StigVersion: "WN22-CC-000010",
        BenchmarkId: TestBenchmarkId);

    public CklExportTests()
    {
        _generator = new CklGenerator(NullLogger<CklGenerator>.Instance);

        // Service setup for ExportCklAsync tests
        var services = new ServiceCollection();
        var dbName = $"CklExportTests_{Guid.NewGuid()}";
        services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(dbName));
        _serviceProvider = services.BuildServiceProvider();

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
            InitiatedBy = "test-user"
        });
        initCtx.SaveChanges();

        _stigServiceMock = new Mock<IStigKnowledgeService>();
        _baselineServiceMock = new Mock<IBaselineService>();
        _rmfServiceMock = new Mock<IRmfLifecycleService>();
        _artifactServiceMock = new Mock<IAssessmentArtifactService>();
        _cklParserMock = new Mock<ICklParser>();
        _xccdfParserMock = new Mock<IXccdfParser>();
        _cklGeneratorMock = new Mock<ICklGenerator>();

        _rmfServiceMock.Setup(s => s.GetSystemAsync(TestSystemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSystemResult.Found(_testSystem));

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

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
            NullLogger<ScanImportService>.Instance);
    }

    public void Dispose() => _serviceProvider.Dispose();

    // ═══════════════════════════════════════════════════════════════════════
    // CklGenerator Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Generate_ProducesWellFormedXml()
    {
        var controls = new List<StigControl> { _stig1 };
        var findings = new Dictionary<string, ComplianceFinding>();

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "Windows Server 2022 STIG");

        // Should parse without error
        var doc = XDocument.Parse(xml);
        doc.Root.Should().NotBeNull();
        doc.Root!.Name.LocalName.Should().Be("CHECKLIST");
    }

    [Fact]
    public void Generate_AssetSection_ContainsSystemMetadata()
    {
        var controls = new List<StigControl> { _stig1 };
        var findings = new Dictionary<string, ComplianceFinding>();

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "Windows Server 2022 STIG");

        var doc = XDocument.Parse(xml);
        var asset = doc.Root!.Element("ASSET")!;
        asset.Element("HOST_NAME")!.Value.Should().Be("Export Test System");
    }

    [Fact]
    public void Generate_StigInfo_ContainsBenchmarkData()
    {
        var controls = new List<StigControl> { _stig1 };
        var findings = new Dictionary<string, ComplianceFinding>();

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "Windows Server 2022 STIG");

        var doc = XDocument.Parse(xml);
        var stigInfo = doc.Root!.Descendants("STIG_INFO").First();
        var siDataValues = stigInfo.Elements("SI_DATA")
            .ToDictionary(
                e => e.Element("SID_NAME")!.Value,
                e => e.Element("SID_DATA")!.Value);

        siDataValues["stigid"].Should().Be(TestBenchmarkId);
        siDataValues["version"].Should().Be("3");
        siDataValues["title"].Should().Be("Windows Server 2022 STIG");
    }

    [Fact]
    public void Generate_VulnEntries_HaveCorrectCount()
    {
        var controls = new List<StigControl> { _stig1, _stig2, _stig3 };
        var findings = new Dictionary<string, ComplianceFinding>();

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "STIG");

        var doc = XDocument.Parse(xml);
        var vulns = doc.Root!.Descendants("VULN").ToList();
        vulns.Should().HaveCount(3);
    }

    [Fact]
    public void Generate_OpenFinding_MapsToOpenStatus()
    {
        var controls = new List<StigControl> { _stig1 };
        var findings = new Dictionary<string, ComplianceFinding>
        {
            ["V-254239"] = new()
            {
                StigId = "V-254239",
                Status = FindingStatus.Open,
                Description = "Audit not configured"
            }
        };

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "STIG");

        var doc = XDocument.Parse(xml);
        var status = doc.Root!.Descendants("VULN").First().Element("STATUS")!.Value;
        status.Should().Be("Open");
    }

    [Fact]
    public void Generate_RemediatedFinding_MapsToNotAFinding()
    {
        var controls = new List<StigControl> { _stig1 };
        var findings = new Dictionary<string, ComplianceFinding>
        {
            ["V-254239"] = new()
            {
                StigId = "V-254239",
                Status = FindingStatus.Remediated,
                Description = "Fixed"
            }
        };

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "STIG");

        var doc = XDocument.Parse(xml);
        var status = doc.Root!.Descendants("VULN").First().Element("STATUS")!.Value;
        status.Should().Be("NotAFinding");
    }

    [Fact]
    public void Generate_AcceptedFinding_MapsToNotApplicable()
    {
        var controls = new List<StigControl> { _stig1 };
        var findings = new Dictionary<string, ComplianceFinding>
        {
            ["V-254239"] = new()
            {
                StigId = "V-254239",
                Status = FindingStatus.Accepted,
                Description = "Risk accepted"
            }
        };

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "STIG");

        var doc = XDocument.Parse(xml);
        var status = doc.Root!.Descendants("VULN").First().Element("STATUS")!.Value;
        status.Should().Be("Not_Applicable");
    }

    [Fact]
    public void Generate_NoFinding_DefaultsToNotReviewed()
    {
        var controls = new List<StigControl> { _stig1, _stig2 };
        var findings = new Dictionary<string, ComplianceFinding>(); // No findings

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "STIG");

        var doc = XDocument.Parse(xml);
        var statuses = doc.Root!.Descendants("VULN")
            .Select(v => v.Element("STATUS")!.Value)
            .ToList();

        statuses.Should().OnlyContain(s => s == "Not_Reviewed");
    }

    [Fact]
    public void Generate_CciRefElements_Included()
    {
        var controls = new List<StigControl> { _stig1 }; // Has CCI-000018, CCI-000172
        var findings = new Dictionary<string, ComplianceFinding>();

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "STIG");

        var doc = XDocument.Parse(xml);
        var vuln = doc.Root!.Descendants("VULN").First();
        var cciRefs = vuln.Elements("STIG_DATA")
            .Where(sd => sd.Element("VULN_ATTRIBUTE")!.Value == "CCI_REF")
            .Select(sd => sd.Element("ATTRIBUTE_DATA")!.Value)
            .ToList();

        cciRefs.Should().Contain("CCI-000018");
        cciRefs.Should().Contain("CCI-000172");
    }

    [Fact]
    public void Generate_CorrectSeverityMapping()
    {
        var controls = new List<StigControl> { _stig1, _stig2, _stig3 };
        var findings = new Dictionary<string, ComplianceFinding>();

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "STIG");

        var doc = XDocument.Parse(xml);
        var vulns = doc.Root!.Descendants("VULN").ToList();
        var severities = vulns.Select(v =>
            v.Elements("STIG_DATA")
                .First(sd => sd.Element("VULN_ATTRIBUTE")!.Value == "Severity")
                .Element("ATTRIBUTE_DATA")!.Value)
            .ToList();

        severities[0].Should().Be("high");
        severities[1].Should().Be("medium");
        severities[2].Should().Be("low");
    }

    [Fact]
    public void Generate_RoundTrip_CklParserCanReparse()
    {
        // Generate CKL with known findings
        var controls = new List<StigControl> { _stig1, _stig2, _stig3 };
        var findings = new Dictionary<string, ComplianceFinding>
        {
            ["V-254239"] = new() { StigId = "V-254239", Status = FindingStatus.Open, Description = "Open finding" },
            ["V-254240"] = new() { StigId = "V-254240", Status = FindingStatus.Remediated, Description = "Fixed" }
        };

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "Windows Server 2022 STIG");

        // Parse it back using the real CklParser
        var parser = new CklParser(NullLogger<CklParser>.Instance);
        var content = System.Text.Encoding.UTF8.GetBytes(xml);
        var parsed = parser.Parse(content, "round-trip.ckl");

        // Verify round-trip integrity
        parsed.Entries.Should().HaveCount(3);
        parsed.Entries.Count(e => e.Status == "Open").Should().Be(1);
        parsed.Entries.Count(e => e.Status == "NotAFinding").Should().Be(1);
        parsed.Entries.Count(e => e.Status == "Not_Reviewed").Should().Be(1);
        parsed.StigInfo.StigId.Should().Be(TestBenchmarkId);
        parsed.Asset.HostName.Should().Be("Export Test System");
    }

    [Fact]
    public void Generate_PartialAssessment_UnassessedDefaultToNotReviewed()
    {
        // 3 controls, only 1 has a finding
        var controls = new List<StigControl> { _stig1, _stig2, _stig3 };
        var findings = new Dictionary<string, ComplianceFinding>
        {
            ["V-254239"] = new() { StigId = "V-254239", Status = FindingStatus.Open, Description = "Open" }
        };

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "STIG");

        var doc = XDocument.Parse(xml);
        var statuses = doc.Root!.Descendants("VULN")
            .Select(v => v.Element("STATUS")!.Value)
            .ToList();

        statuses.Count(s => s == "Open").Should().Be(1);
        statuses.Count(s => s == "Not_Reviewed").Should().Be(2);
    }

    [Fact]
    public void Generate_InProgressFinding_MapsToOpen()
    {
        var controls = new List<StigControl> { _stig1 };
        var findings = new Dictionary<string, ComplianceFinding>
        {
            ["V-254239"] = new()
            {
                StigId = "V-254239",
                Status = FindingStatus.InProgress,
                Description = "Being remediated"
            }
        };

        var xml = _generator.Generate(_testSystem, controls, findings,
            TestBenchmarkId, "3", "STIG");

        var doc = XDocument.Parse(xml);
        var status = doc.Root!.Descendants("VULN").First().Element("STATUS")!.Value;
        status.Should().Be("Open");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ExportCklAsync Service Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportCkl_InvalidSystem_ThrowsInvalidOperation()
    {
        _rmfServiceMock.Setup(s => s.GetSystemAsync("bad-sys", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSystemResult.NotFound("bad-sys"));

        var act = () => _service.ExportCklAsync("bad-sys", TestBenchmarkId, TestAssessmentId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*bad-sys*");
    }

    [Fact]
    public async Task ExportCkl_NoBenchmarkControls_ThrowsInvalidOperation()
    {
        _stigServiceMock.Setup(s => s.GetStigControlsByBenchmarkAsync("NonExistent_STIG", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StigControl>());

        var act = () => _service.ExportCklAsync(TestSystemId, "NonExistent_STIG", TestAssessmentId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NonExistent_STIG*");
    }

    [Fact]
    public async Task ExportCkl_ValidRequest_CallsCklGenerator()
    {
        _stigServiceMock.Setup(s => s.GetStigControlsByBenchmarkAsync(TestBenchmarkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StigControl> { _stig1, _stig2 });

        _cklGeneratorMock.Setup(g => g.Generate(
                It.IsAny<RegisteredSystem>(),
                It.IsAny<List<StigControl>>(),
                It.IsAny<Dictionary<string, ComplianceFinding>>(),
                TestBenchmarkId,
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .Returns("<CHECKLIST></CHECKLIST>");

        var result = await _service.ExportCklAsync(TestSystemId, TestBenchmarkId, TestAssessmentId);

        result.Should().NotBeNullOrEmpty();
        // Result should be valid base64
        var decoded = Convert.FromBase64String(result);
        var xml = System.Text.Encoding.UTF8.GetString(decoded);
        xml.Should().Contain("CHECKLIST");

        _cklGeneratorMock.Verify(g => g.Generate(
            It.Is<RegisteredSystem>(s => s.Id == TestSystemId),
            It.Is<List<StigControl>>(l => l.Count == 2),
            It.IsAny<Dictionary<string, ComplianceFinding>>(),
            TestBenchmarkId,
            It.IsAny<string?>(),
            It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ExportCkl_WithFindings_PassesFindingsToGenerator()
    {
        // Seed a finding
        using (var seedScope = _serviceProvider.CreateScope())
        {
            var seedCtx = seedScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            seedCtx.Findings.Add(new ComplianceFinding
            {
                AssessmentId = TestAssessmentId,
                StigFinding = true,
                StigId = "V-254239",
                Status = FindingStatus.Open,
                ControlId = "AC-2",
                ControlFamily = "AC",
                Title = "Test",
                Description = "Open finding",
                Severity = FindingSeverity.High,
                Source = "CKL Import"
            });
            await seedCtx.SaveChangesAsync();
        }

        _stigServiceMock.Setup(s => s.GetStigControlsByBenchmarkAsync(TestBenchmarkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StigControl> { _stig1, _stig2 });

        _cklGeneratorMock.Setup(g => g.Generate(
                It.IsAny<RegisteredSystem>(),
                It.IsAny<List<StigControl>>(),
                It.IsAny<Dictionary<string, ComplianceFinding>>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .Returns("<CHECKLIST></CHECKLIST>");

        await _service.ExportCklAsync(TestSystemId, TestBenchmarkId, TestAssessmentId);

        // Verify that the findings dictionary passed to Generator contains the seeded finding
        _cklGeneratorMock.Verify(g => g.Generate(
            It.IsAny<RegisteredSystem>(),
            It.IsAny<List<StigControl>>(),
            It.Is<Dictionary<string, ComplianceFinding>>(d => d.ContainsKey("V-254239")),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>()), Times.Once);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Feature 017 — Phase 10 (T060): SCAP/STIG Import Integration Tests
// End-to-end: register → categorize → select baseline → import CKL →
// verify findings → export CKL → re-import with Skip → no duplicates.
// ═══════════════════════════════════════════════════════════════════════════

using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Services.ScanImport;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Integration.Tools;

/// <summary>
/// Full integration test for Feature 017 — SCAP/STIG Viewer Import.
/// Uses real services (in-memory EF Core) with mocked IStigKnowledgeService.
/// Validates the complete import → export → re-import lifecycle.
/// </summary>
public class ScanImportIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly RegisterSystemTool _registerTool;
    private readonly CategorizeSystemTool _categorizeTool;
    private readonly SelectBaselineTool _selectBaselineTool;
    private readonly ImportCklTool _importCklTool;
    private readonly ExportCklTool _exportCklTool;
    private readonly ListImportsTool _listImportsTool;
    private readonly GetImportSummaryTool _getImportSummaryTool;
    private readonly Mock<IStigKnowledgeService> _stigServiceMock;

    // Known STIG controls that our mock will return
    private static readonly StigControl StigAC2 = new StigControl(
        StigId: "V-254239", VulnId: "V-254239", RuleId: "SV-254239r1_rule",
        Title: "Windows Server 2022 must have the number of allowed bad logon attempts configured to three or less.",
        Description: "Test STIG rule for AC-2", Severity: StigSeverity.High,
        Category: "CAT I", StigFamily: "Windows Server 2022",
        NistControls: new List<string> { "AC-7" },
        CciRefs: new List<string> { "CCI-000044" },
        CheckText: "Verify the number of allowed bad logon attempts.",
        FixText: "Configure the policy to limit the number of bad logon attempts.",
        AzureImplementation: new Dictionary<string, string>(),
        ServiceType: "OS", BenchmarkId: "Windows_Server_2022_STIG");

    private static readonly StigControl StigCM6 = new StigControl(
        StigId: "V-254240", VulnId: "V-254240", RuleId: "SV-254240r1_rule",
        Title: "Windows Server 2022 must be configured to audit logon successes.",
        Description: "Test STIG rule for CM-6", Severity: StigSeverity.Medium,
        Category: "CAT II", StigFamily: "Windows Server 2022",
        NistControls: new List<string> { "CM-6" },
        CciRefs: new List<string> { "CCI-000366" },
        CheckText: "Verify audit logon successes is enabled.",
        FixText: "Enable audit logon successes.",
        AzureImplementation: new Dictionary<string, string>(),
        ServiceType: "OS", BenchmarkId: "Windows_Server_2022_STIG");

    private static readonly StigControl StigSC28 = new StigControl(
        StigId: "V-254241", VulnId: "V-254241", RuleId: "SV-254241r1_rule",
        Title: "Windows Server 2022 must be configured to prevent the storage of passwords using reversible encryption.",
        Description: "Test STIG rule for SC-28", Severity: StigSeverity.High,
        Category: "CAT I", StigFamily: "Windows Server 2022",
        NistControls: new List<string> { "SC-28" },
        CciRefs: new List<string> { "CCI-002476" },
        CheckText: "Verify reversible encryption is disabled.",
        FixText: "Disable reversible encryption.",
        AzureImplementation: new Dictionary<string, string>(),
        ServiceType: "OS", BenchmarkId: "Windows_Server_2022_STIG");

    private static readonly List<StigControl> AllControls = new() { StigAC2, StigCM6, StigSC28 };

    public ScanImportIntegrationTests()
    {
        var dbName = $"ScanImportIntTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        // Real services
        var lifecycleSvc = new RmfLifecycleService(scopeFactory, Mock.Of<ILogger<RmfLifecycleService>>());
        var categorizationSvc = new CategorizationService(scopeFactory, Mock.Of<ILogger<CategorizationService>>(), Mock.Of<IPrivacyService>());
        var referenceDataSvc = new ReferenceDataService(Mock.Of<ILogger<ReferenceDataService>>());
        var baselineSvc = new BaselineService(scopeFactory, referenceDataSvc, Mock.Of<ILogger<BaselineService>>(), Mock.Of<IOrgInheritanceService>());
        var artifactSvc = new AssessmentArtifactService(scopeFactory, Mock.Of<ILogger<AssessmentArtifactService>>());

        // Mock STIG knowledge service
        _stigServiceMock = new Mock<IStigKnowledgeService>();
        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-254239", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StigAC2);
        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-254240", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StigCM6);
        _stigServiceMock.Setup(s => s.GetStigControlAsync("V-254241", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StigSC28);
        _stigServiceMock.Setup(s => s.GetStigControlAsync(It.Is<string>(id =>
                id != "V-254239" && id != "V-254240" && id != "V-254241"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StigControl?)null);
        _stigServiceMock.Setup(s => s.GetStigControlsByBenchmarkAsync("Windows_Server_2022_STIG", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllControls);
        _stigServiceMock.Setup(s => s.GetStigControlByRuleIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StigControl?)null);

        // Real parsers & generator
        var cklParser = new CklParser(Mock.Of<ILogger<CklParser>>());
        var xccdfParser = new XccdfParser(Mock.Of<ILogger<XccdfParser>>());
        var cklGenerator = new CklGenerator(Mock.Of<ILogger<CklGenerator>>());

        var importSvc = new ScanImportService(
            scopeFactory, _stigServiceMock.Object, baselineSvc, lifecycleSvc,
            artifactSvc, cklParser, xccdfParser, cklGenerator,
            Mock.Of<ISystemSubscriptionResolver>(),
            new PrismaCsvParser(Mock.Of<ILogger<PrismaCsvParser>>()),
            new PrismaApiJsonParser(Mock.Of<ILogger<PrismaApiJsonParser>>()),
            Mock.Of<INessusParser>(),
            Mock.Of<INessusControlMapper>(),
            Mock.Of<ILogger<ScanImportService>>());

        // Tools
        _registerTool = new RegisterSystemTool(lifecycleSvc, Mock.Of<ILogger<RegisterSystemTool>>());
        _categorizeTool = new CategorizeSystemTool(categorizationSvc, Mock.Of<ILogger<CategorizeSystemTool>>());
        _selectBaselineTool = new SelectBaselineTool(baselineSvc, Mock.Of<ILogger<SelectBaselineTool>>());
        _importCklTool = new ImportCklTool(importSvc, scopeFactory, Mock.Of<ILogger<ImportCklTool>>());
        _exportCklTool = new ExportCklTool(importSvc, Mock.Of<ILogger<ExportCklTool>>());
        _listImportsTool = new ListImportsTool(importSvc, Mock.Of<ILogger<ListImportsTool>>());
        _getImportSummaryTool = new GetImportSummaryTool(importSvc, Mock.Of<ILogger<GetImportSummaryTool>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    /// <summary>
    /// Full lifecycle: register → categorize → select baseline → import CKL →
    /// verify findings → export CKL → re-import with Skip → verify no duplicates.
    /// </summary>
    [Fact]
    public async Task FullScanImportLifecycle_CklImportExportReimport()
    {
        // ─── Step 1: Register system ──────────────────────────────────
        var systemId = await RegisterSystem("STIG Import Test System", "MajorApplication");
        systemId.Should().NotBeNullOrEmpty();

        // ─── Step 2: Categorize as Moderate ───────────────────────────
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");

        // ─── Step 3: Select baseline ──────────────────────────────────
        var selectResult = await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = true
        });
        var selectJson = JsonDocument.Parse(selectResult);
        selectJson.RootElement.GetProperty("status").GetString().Should().Be("success");

        // ─── Step 4: Import CKL ──────────────────────────────────────
        var cklXml = BuildTestCkl();
        var cklBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(cklXml));

        var importResult = await _importCklTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["file_content"] = cklBase64,
            ["file_name"] = "windows_server_2022.ckl",
            ["conflict_resolution"] = "Skip",
            ["dry_run"] = false
        });

        var importJson = JsonDocument.Parse(importResult);
        importJson.RootElement.GetProperty("status").GetString().Should().Be("success");

        var importData = importJson.RootElement.GetProperty("data");
        var importRecordId = importData.GetProperty("import_record_id").GetString();
        importRecordId.Should().NotBeNullOrEmpty();
        importData.GetProperty("total_entries").GetInt32().Should().Be(3);

        var summary = importData.GetProperty("summary");
        summary.GetProperty("open").GetInt32().Should().Be(1);        // V-254239 is Open
        summary.GetProperty("pass").GetInt32().Should().Be(1);        // V-254240 is NotAFinding
        summary.GetProperty("not_applicable").GetInt32().Should().Be(1); // V-254241 is Not_Applicable

        var changes = importData.GetProperty("changes");
        changes.GetProperty("findings_created").GetInt32().Should().BeGreaterOrEqualTo(1);
        changes.GetProperty("nist_controls_affected").GetInt32().Should().BeGreaterOrEqualTo(0);

        // ─── Step 5: List imports ────────────────────────────────────
        var listResult = await _listImportsTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });
        var listJson = JsonDocument.Parse(listResult);
        listJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        listJson.RootElement.GetProperty("data").GetProperty("total_count").GetInt32().Should().Be(1);

        // ─── Step 6: Get import summary ──────────────────────────────
        var summaryResult = await _getImportSummaryTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["import_id"] = importRecordId
        });
        var summaryJson = JsonDocument.Parse(summaryResult);
        summaryJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        summaryJson.RootElement.GetProperty("data").GetProperty("file_name").GetString()
            .Should().Be("windows_server_2022.ckl");

        // ─── Step 7: Export CKL ──────────────────────────────────────
        var exportResult = await _exportCklTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["benchmark_id"] = "Windows_Server_2022_STIG"
        });
        var exportJson = JsonDocument.Parse(exportResult);
        exportJson.RootElement.GetProperty("status").GetString().Should().Be("success");

        var exportedBase64 = exportJson.RootElement.GetProperty("data")
            .GetProperty("file_content").GetString();
        exportedBase64.Should().NotBeNullOrEmpty();

        // Verify exported CKL is valid XML with CHECKLIST root
        var exportedXml = Encoding.UTF8.GetString(Convert.FromBase64String(exportedBase64!));
        exportedXml.Should().Contain("<CHECKLIST>");
        exportedXml.Should().Contain("V-254239");

        // ─── Step 8: Re-import same CKL with Skip → no duplicates ───
        var reimportResult = await _importCklTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["file_content"] = cklBase64,
            ["file_name"] = "windows_server_2022_v2.ckl",
            ["conflict_resolution"] = "Skip",
            ["dry_run"] = false
        });

        var reimportJson = JsonDocument.Parse(reimportResult);
        reimportJson.RootElement.GetProperty("status").GetString().Should().Be("success");

        var reimportChanges = reimportJson.RootElement.GetProperty("data").GetProperty("changes");
        // With Skip strategy, existing findings should be skipped (0 created)
        reimportChanges.GetProperty("findings_created").GetInt32().Should().Be(0);

        // ─── Step 9: Verify total import count is now 2 ─────────────
        var finalListResult = await _listImportsTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });
        var finalListJson = JsonDocument.Parse(finalListResult);
        finalListJson.RootElement.GetProperty("data").GetProperty("total_count").GetInt32().Should().Be(2);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private async Task<string> RegisterSystem(string name, string type)
    {
        var result = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = name,
            ["system_type"] = type,
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "Government"
        });
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        return json.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task CategorizeSystem(string systemId, string conf, string integ, string avail)
    {
        var result = await _categorizeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["information_types"] = new List<InformationTypeInput>
            {
                new()
                {
                    Sp80060Id = "C.3.5.8", Name = "Information Security",
                    Category = "Management and Support",
                    ConfidentialityImpact = conf, IntegrityImpact = integ, AvailabilityImpact = avail
                }
            },
            ["justification"] = "Integration test categorization"
        });
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
    }

    /// <summary>
    /// Builds a minimal but valid CKL XML with 3 VULNs:
    /// V-254239 (Open, high), V-254240 (NotAFinding, medium), V-254241 (Not_Applicable, high).
    /// </summary>
    private static string BuildTestCkl()
    {
        return @"<?xml version=""1.0"" encoding=""UTF-8""?>
<CHECKLIST>
  <ASSET>
    <ROLE>None</ROLE>
    <ASSET_TYPE>Computing</ASSET_TYPE>
    <HOST_NAME>test-server-01</HOST_NAME>
    <HOST_IP>10.0.1.100</HOST_IP>
    <HOST_MAC>00:0A:95:9D:68:16</HOST_MAC>
    <HOST_FQDN>test-server-01.example.mil</HOST_FQDN>
    <TARGET_COMMENT></TARGET_COMMENT>
    <TECH_AREA></TECH_AREA>
    <TARGET_KEY>4089</TARGET_KEY>
    <WEB_OR_DATABASE>false</WEB_OR_DATABASE>
    <WEB_DB_SITE></WEB_DB_SITE>
    <WEB_DB_INSTANCE></WEB_DB_INSTANCE>
  </ASSET>
  <STIGS>
    <iSTIG>
      <STIG_INFO>
        <SI_DATA><SID_NAME>version</SID_NAME><SID_DATA>3</SID_DATA></SI_DATA>
        <SI_DATA><SID_NAME>stigid</SID_NAME><SID_DATA>Windows_Server_2022_STIG</SID_DATA></SI_DATA>
        <SI_DATA><SID_NAME>releaseinfo</SID_NAME><SID_DATA>Release: 1</SID_DATA></SI_DATA>
        <SI_DATA><SID_NAME>title</SID_NAME><SID_DATA>Microsoft Windows Server 2022 STIG</SID_DATA></SI_DATA>
      </STIG_INFO>
      <VULN>
        <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-254239</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Severity</VULN_ATTRIBUTE><ATTRIBUTE_DATA>high</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Group_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SRG-OS-000003</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SV-254239r1_rule</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Rule_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Bad logon attempts</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Rule_Ver</VULN_ATTRIBUTE><ATTRIBUTE_DATA>WN22-AC-000010</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>CCI_REF</VULN_ATTRIBUTE><ATTRIBUTE_DATA>CCI-000044</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Check_Content</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Verify logon attempts</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Fix_Text</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Configure logon attempts</ATTRIBUTE_DATA></STIG_DATA>
        <STATUS>Open</STATUS>
        <FINDING_DETAILS>Found 10 allowed attempts</FINDING_DETAILS>
        <COMMENTS>Needs remediation</COMMENTS>
        <SEVERITY_OVERRIDE></SEVERITY_OVERRIDE>
        <SEVERITY_JUSTIFICATION></SEVERITY_JUSTIFICATION>
      </VULN>
      <VULN>
        <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-254240</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Severity</VULN_ATTRIBUTE><ATTRIBUTE_DATA>medium</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Group_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SRG-OS-000037</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SV-254240r1_rule</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Rule_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Audit logon successes</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Rule_Ver</VULN_ATTRIBUTE><ATTRIBUTE_DATA>WN22-AU-000010</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>CCI_REF</VULN_ATTRIBUTE><ATTRIBUTE_DATA>CCI-000366</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Check_Content</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Verify audit settings</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Fix_Text</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Enable audit logon</ATTRIBUTE_DATA></STIG_DATA>
        <STATUS>NotAFinding</STATUS>
        <FINDING_DETAILS>Audit logon enabled</FINDING_DETAILS>
        <COMMENTS>Verified</COMMENTS>
        <SEVERITY_OVERRIDE></SEVERITY_OVERRIDE>
        <SEVERITY_JUSTIFICATION></SEVERITY_JUSTIFICATION>
      </VULN>
      <VULN>
        <STIG_DATA><VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE><ATTRIBUTE_DATA>V-254241</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Severity</VULN_ATTRIBUTE><ATTRIBUTE_DATA>high</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Group_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SRG-OS-000405</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE><ATTRIBUTE_DATA>SV-254241r1_rule</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Rule_Title</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Reversible encryption</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Rule_Ver</VULN_ATTRIBUTE><ATTRIBUTE_DATA>WN22-SO-000010</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>CCI_REF</VULN_ATTRIBUTE><ATTRIBUTE_DATA>CCI-002476</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Check_Content</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Verify encryption</ATTRIBUTE_DATA></STIG_DATA>
        <STIG_DATA><VULN_ATTRIBUTE>Fix_Text</VULN_ATTRIBUTE><ATTRIBUTE_DATA>Disable reversible encryption</ATTRIBUTE_DATA></STIG_DATA>
        <STATUS>Not_Applicable</STATUS>
        <FINDING_DETAILS>N/A for this config</FINDING_DETAILS>
        <COMMENTS>Not applicable</COMMENTS>
        <SEVERITY_OVERRIDE></SEVERITY_OVERRIDE>
        <SEVERITY_JUSTIFICATION></SEVERITY_JUSTIFICATION>
      </VULN>
    </iSTIG>
  </STIGS>
</CHECKLIST>";
    }
}

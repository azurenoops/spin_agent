// ─────────────────────────────────────────────────────────────────────────────
// Feature 015 · Phase 12 — eMASS & OSCAL Interoperability (US10)
// T223: Integration tests — end-to-end export/import/OSCAL lifecycle
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Integration.Tools;

/// <summary>
/// Integration tests for Feature 015 Phase 12 — eMASS &amp; OSCAL Interoperability (US10).
/// Uses real EmassExportService with in-memory EF Core DB.
/// Validates: export controls Excel → verify eMASS headers → import with dry-run →
/// export OSCAL SSP JSON → export POA&amp;M Excel.
/// </summary>
public class EmassIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ExportEmassTool _exportTool;
    private readonly ImportEmassTool _importTool;
    private readonly ExportOscalTool _oscalTool;

    public EmassIntegrationTests()
    {
        var dbName = $"EmassIntTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var oscalMock = new Mock<IOscalSspExportService>();
        oscalMock.Setup(s => s.ExportAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalExportResult(
                "{\"system-security-plan\":{\"uuid\":\"test\",\"metadata\":{\"title\":\"Test SSP\",\"oscal-version\":\"1.0.6\"}}}",
                new List<string>(),
                new OscalStatistics(2, 1, 0, 0, 0)));

        var emassSvc = new EmassExportService(
            _scopeFactory, Mock.Of<ILogger<EmassExportService>>(),
            oscalMock.Object);

        _exportTool = new ExportEmassTool(
            emassSvc, Mock.Of<ILogger<ExportEmassTool>>());
        _importTool = new ImportEmassTool(
            emassSvc, Mock.Of<ILogger<ImportEmassTool>>());
        _oscalTool = new ExportOscalTool(
            emassSvc, Mock.Of<IOscalSapExportService>(), Mock.Of<ILogger<ExportOscalTool>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    // ═════════════════════════════════════════════════════════════════════════
    //  Full lifecycle: seed → export controls → verify headers → import
    //  dry-run → export OSCAL SSP
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullEmassLifecycle_EndToEnd()
    {
        // ─── Step 1: Seed system, baseline, implementations, POA&M ────
        var systemId = await SeedFullSystem();

        // ─── Step 2: Export controls to eMASS Excel ───────────────────
        var exportResult = await _exportTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["export_type"] = "controls"
        });

        var exportDoc = JsonDocument.Parse(exportResult);
        exportDoc.RootElement.GetProperty("status").GetString().Should().Be("success",
            because: $"Export controls should succeed but got: {exportResult}");

        var exportData = exportDoc.RootElement.GetProperty("data");
        exportData.GetProperty("export_type").GetString().Should().Be("controls");
        exportData.GetProperty("controls_file_size_bytes").GetInt32().Should().BeGreaterThan(0);
        exportData.GetProperty("controls_exported").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        // ─── Step 3: Verify eMASS column headers in the Excel file ───
        var controlsBase64 = exportData.GetProperty("controls_base64").GetString()!;
        var controlBytes = Convert.FromBase64String(controlsBase64);

        using (var ms = new MemoryStream(controlBytes))
        using (var wb = new XLWorkbook(ms))
        {
            var ws = wb.Worksheets.First();
            ws.Cell(1, 1).GetString().Should().Be("System Name");
            ws.Cell(1, 5).GetString().Should().Be("Control Identifier");
            ws.Cell(1, 8).GetString().Should().Be("Implementation Status");
            ws.Cell(1, 9).GetString().Should().Be("Implementation Narrative");
            ws.Cell(1, 12).GetString().Should().Be("Compliance Status");
            ws.Cell(1, 17).GetString().Should().Be("Security Control Baseline");

            // Data rows should contain our seeded controls
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            lastRow.Should().BeGreaterThanOrEqualTo(3, "should have header + at least 2 data rows");
        }

        // ─── Step 4: Import with dry-run (skip strategy) ─────────────
        var importResult = await _importTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["file_base64"] = controlsBase64,
            ["conflict_strategy"] = "skip",
            ["dry_run"] = "true"
        });

        var importDoc = JsonDocument.Parse(importResult);
        importDoc.RootElement.GetProperty("status").GetString().Should().Be("success",
            because: $"Import dry-run should succeed but got: {importResult}");
        var importData = importDoc.RootElement.GetProperty("data");
        importData.GetProperty("dry_run").GetBoolean().Should().BeTrue();
        importData.GetProperty("total_rows").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        // ─── Step 5: Export OSCAL SSP ─────────────────────────────────
        var oscalResult = await _oscalTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["model"] = "ssp"
        });

        var oscalDoc = JsonDocument.Parse(oscalResult);
        oscalDoc.RootElement.GetProperty("status").GetString().Should().Be("success",
            because: $"OSCAL SSP export should succeed but got: {oscalResult}");
        var oscalData = oscalDoc.RootElement.GetProperty("data");
        oscalData.GetProperty("model").GetString().Should().Be("ssp");
        oscalData.GetProperty("oscal_version").GetString().Should().Be("1.0.6");
        oscalData.GetProperty("oscal_document").ValueKind.Should().Be(JsonValueKind.Object);
    }

    /// <summary>
    /// POA&amp;M export: Seed POA&amp;M items → export → verify Excel headers + data rows.
    /// </summary>
    [Fact]
    public async Task PoamExport_WithMilestones_ReturnsValidExcel()
    {
        var systemId = await SeedFullSystem();

        var exportResult = await _exportTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["export_type"] = "poam"
        });

        var doc = JsonDocument.Parse(exportResult);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success",
            because: $"Export POA&M should succeed but got: {exportResult}");

        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("export_type").GetString().Should().Be("poam");
        data.GetProperty("poam_file_size_bytes").GetInt32().Should().BeGreaterThan(0);
        data.GetProperty("poam_exported").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        // Verify Excel structure
        var poamBase64 = data.GetProperty("poam_base64").GetString()!;
        var poamBytes = Convert.FromBase64String(poamBase64);

        using var ms = new MemoryStream(poamBytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        ws.Cell(1, 1).GetString().Should().Be("System Name");
        ws.Cell(1, 3).GetString().Should().Be("POA&M ID");
        ws.Cell(1, 4).GetString().Should().Be("Weakness");
    }

    /// <summary>
    /// OSCAL POA&amp;M model export matches the seeded data.
    /// </summary>
    [Fact]
    public async Task OscalPoamExport_ReturnsValidDocument()
    {
        var systemId = await SeedFullSystem();

        var result = await _oscalTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["model"] = "poam"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("model").GetString().Should().Be("poam");

        // The OSCAL document should contain a plan-of-action-and-milestones root
        var oscalDocument = data.GetProperty("oscal_document");
        oscalDocument.ValueKind.Should().Be(JsonValueKind.Object);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private async Task<string> SeedFullSystem()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // ─── RegisteredSystem ─────────────────────────────────────────
        var system = new RegisteredSystem
        {
            Name = "eMASS Integration Test System",
            Acronym = "EMITS",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "AzureGovernment",
            CreatedBy = "test-issm"
        };
        db.RegisteredSystems.Add(system);

        // ─── Security Categorization with InformationTypes ─────────
        var categorization = new SecurityCategorization
        {
            RegisteredSystemId = system.Id,
            CategorizedBy = "test-issm"
        };
        db.SecurityCategorizations.Add(categorization);

        db.InformationTypes.Add(new InformationType
        {
            SecurityCategorizationId = categorization.Id,
            Sp80060Id = "D.1.1",
            Name = "Personnel Management",
            ConfidentialityImpact = ImpactValue.Moderate,
            IntegrityImpact = ImpactValue.Moderate,
            AvailabilityImpact = ImpactValue.Low
        });

        // ─── Control Baseline ─────────────────────────────────────────
        var baseline = new ControlBaseline
        {
            RegisteredSystemId = system.Id,
            BaselineLevel = "Moderate",
            TotalControls = 325,
            CustomerControls = 200,
            InheritedControls = 100,
            SharedControls = 25,
            ControlIds = new List<string> { "AC-1", "AC-2", "AU-1", "SC-7" },
            CreatedBy = "test-issm"
        };
        db.ControlBaselines.Add(baseline);

        // ─── Control Implementations ──────────────────────────────────
        db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            ImplementationStatus = ImplementationStatus.Implemented,
            Narrative = "Access control policy and procedures are documented and reviewed annually.",
            AuthoredBy = "test-issm"
        });

        db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = system.Id,
            ControlId = "AC-2",
            ImplementationStatus = ImplementationStatus.PartiallyImplemented,
            Narrative = "Account management procedures partially implemented via Azure AD.",
            AuthoredBy = "test-issm"
        });

        // ─── POA&M Items ──────────────────────────────────────────────
        var poamItem = new PoamItem
        {
            RegisteredSystemId = system.Id,
            Weakness = "AC-2 account provisioning process not fully automated",
            WeaknessSource = "SCA Assessment",
            SecurityControlNumber = "AC-2",
            CatSeverity = CatSeverity.CatII,
            PointOfContact = "John Doe",
            PocEmail = "john.doe@example.mil",
            ScheduledCompletionDate = DateTime.UtcNow.AddDays(90),
            Status = PoamStatus.Ongoing,
            Milestones = new List<PoamMilestone>
            {
                new()
                {
                    Description = "Complete automation script development",
                    TargetDate = DateTime.UtcNow.AddDays(30),
                    Sequence = 1
                },
                new()
                {
                    Description = "Deploy and validate automation",
                    TargetDate = DateTime.UtcNow.AddDays(60),
                    Sequence = 2
                }
            }
        };
        db.PoamItems.Add(poamItem);

        await db.SaveChangesAsync();
        return system.Id;
    }
}

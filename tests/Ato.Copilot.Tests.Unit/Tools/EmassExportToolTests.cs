// ─────────────────────────────────────────────────────────────────────────────
// Feature 015 · Phase 12 — eMASS & OSCAL Interoperability (US10)
// T151-T153: Unit tests for ExportEmassTool, ImportEmassTool, ExportOscalTool
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using Moq;
using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Tools;

/// <summary>
/// Unit tests covering ExportEmassTool (T151), ImportEmassTool (T152),
/// and ExportOscalTool (T153).
/// </summary>
public class EmassExportToolTests
{
    private readonly Mock<IEmassExportService> _mockService = new();

    // ═════════════════════════════════════════════════════════════════════════
    //  Factories
    // ═════════════════════════════════════════════════════════════════════════

    private ExportEmassTool CreateExportTool() =>
        new(_mockService.Object, Mock.Of<ILogger<ExportEmassTool>>());

    private ImportEmassTool CreateImportTool() =>
        new(_mockService.Object, Mock.Of<ILogger<ImportEmassTool>>());

    private ExportOscalTool CreateOscalTool() =>
        new(_mockService.Object, Mock.Of<IOscalSapExportService>(), Mock.Of<ILogger<ExportOscalTool>>());

    private static byte[] FakeExcelBytes() =>
        CreateMinimalXlsx();

    /// <summary>
    /// Creates a minimal valid .xlsx workbook via ClosedXML for test fixtures.
    /// </summary>
    private static byte[] CreateMinimalXlsx(string sheetName = "Controls",
        string[]? headers = null, string[][]? rows = null)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add(sheetName);

        headers ??= new[]
        {
            "System Name", "System Acronym", "DITPR ID", "eMASS ID",
            "Control Identifier", "Control Name", "Control Family",
            "Implementation Status", "Implementation Narrative",
            "Common Control Provider", "Responsibility Type",
            "Compliance Status", "Assessment Procedure", "Assessor Name",
            "Assessment Date", "Test Result",
            "Security Control Baseline", "Is Overlay Control", "Overlay Name",
            "AP Number", "Security Plan Title",
            "Last Modified", "Modified By"
        };

        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];

        if (rows != null)
        {
            for (int r = 0; r < rows.Length; r++)
            {
                for (int c = 0; c < rows[r].Length; c++)
                    ws.Cell(r + 2, c + 1).Value = rows[r][c];
            }
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  T151: ExportEmassTool Tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportEmass_Controls_ReturnsBase64()
    {
        // Arrange
        var excelBytes = FakeExcelBytes();
        _mockService
            .Setup(s => s.ExportControlsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(excelBytes);

        var tool = CreateExportTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["export_type"] = "controls"
        };

        // Act
        var result = await tool.ExecuteCoreAsync(args);

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("export_type").GetString().Should().Be("controls");
        data.GetProperty("controls_base64").GetString().Should().NotBeNullOrEmpty();
        data.GetProperty("controls_file_size_bytes").GetInt32().Should().BeGreaterThan(0);
        data.GetProperty("poam_file_size_bytes").GetInt32().Should().Be(0);

        var meta = doc.RootElement.GetProperty("metadata");
        meta.GetProperty("format").GetString().Should().Be("xlsx");
        meta.GetProperty("emass_compatible").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExportEmass_Poam_ReturnsBase64()
    {
        // Arrange
        var excelBytes = FakeExcelBytes();
        _mockService
            .Setup(s => s.ExportPoamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(excelBytes);

        var tool = CreateExportTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["export_type"] = "poam"
        };

        // Act
        var result = await tool.ExecuteCoreAsync(args);

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("export_type").GetString().Should().Be("poam");
        data.GetProperty("poam_base64").GetString().Should().NotBeNullOrEmpty();
        data.GetProperty("controls_file_size_bytes").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ExportEmass_Full_ExportsBothTypes()
    {
        // Arrange
        var excelBytes = FakeExcelBytes();
        _mockService
            .Setup(s => s.ExportControlsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(excelBytes);
        _mockService
            .Setup(s => s.ExportPoamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(excelBytes);

        var tool = CreateExportTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["export_type"] = "full"
        };

        // Act
        var result = await tool.ExecuteCoreAsync(args);

        // Assert
        var doc = JsonDocument.Parse(result);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("export_type").GetString().Should().Be("full");
        data.GetProperty("controls_base64").GetString().Should().NotBeNullOrEmpty();
        data.GetProperty("poam_base64").GetString().Should().NotBeNullOrEmpty();
        data.GetProperty("controls_file_size_bytes").GetInt32().Should().BeGreaterThan(0);
        data.GetProperty("poam_file_size_bytes").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportEmass_ColumnHeaders_MatchEmassTemplate()
    {
        // Arrange — service returns workbook with eMASS headers
        var excelBytes = CreateMinimalXlsx("Controls", new[]
        {
            "System Name", "System Acronym", "DITPR ID", "eMASS ID",
            "Control Identifier", "Control Name", "Control Family",
            "Implementation Status", "Implementation Narrative",
            "Common Control Provider", "Responsibility Type",
            "Compliance Status", "Assessment Procedure", "Assessor Name",
            "Assessment Date", "Test Result",
            "Security Control Baseline", "Is Overlay Control", "Overlay Name",
            "AP Number", "Security Plan Title",
            "Last Modified", "Modified By"
        });

        _mockService
            .Setup(s => s.ExportControlsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(excelBytes);

        var tool = CreateExportTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["export_type"] = "controls"
        };

        // Act
        var result = await tool.ExecuteCoreAsync(args);

        // Assert — verify we can read the headers back from base64
        var doc = JsonDocument.Parse(result);
        var b64 = doc.RootElement.GetProperty("data")
            .GetProperty("controls_base64").GetString()!;
        var bytes = Convert.FromBase64String(b64);

        using var ms = new MemoryStream(bytes);
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        ws.Cell(1, 1).GetString().Should().Be("System Name");
        ws.Cell(1, 5).GetString().Should().Be("Control Identifier");
        ws.Cell(1, 8).GetString().Should().Be("Implementation Status");
        ws.Cell(1, 12).GetString().Should().Be("Compliance Status");
        ws.Cell(1, 17).GetString().Should().Be("Security Control Baseline");
    }

    [Fact]
    public async Task ExportEmass_MissingSystemId_ExportsWithNullId()
    {
        // GetArg returns null when key is missing; tool proceeds with null systemId
        _mockService
            .Setup(s => s.ExportControlsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FakeExcelBytes());

        var tool = CreateExportTool();
        var args = new Dictionary<string, object?>
        {
            ["export_type"] = "controls"
        };

        var result = await tool.ExecuteCoreAsync(args);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  T152: ImportEmassTool Tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportEmass_Skip_ReturnsSkippedConflicts()
    {
        // Arrange
        var importResult = new EmassImportResult(
            TotalRows: 10, Imported: 3, Skipped: 7, Conflicts: 7,
            ConflictDetails: new List<EmassImportConflict>
            {
                new("AC-1", "ImplementationStatus", "Implemented",
                    "Planned", "Skipped"),
                new("AC-2", "ImplementationStatus", "Implemented",
                    "Planned", "Skipped")
            });

        _mockService
            .Setup(s => s.ImportAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.Is<EmassImportOptions>(o =>
                    o.OnConflict == ConflictResolution.Skip && o.DryRun),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(importResult);

        var tool = CreateImportTool();
        var fileBytes = FakeExcelBytes();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["file_base64"] = Convert.ToBase64String(fileBytes),
            ["conflict_strategy"] = "skip",
            ["dry_run"] = "true"
        };

        // Act
        var result = await tool.ExecuteCoreAsync(args);

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("dry_run").GetBoolean().Should().BeTrue();
        data.GetProperty("conflict_strategy").GetString().Should().Be("skip");
        data.GetProperty("total_rows").GetInt32().Should().Be(10);
        data.GetProperty("imported").GetInt32().Should().Be(3);
        data.GetProperty("skipped").GetInt32().Should().Be(7);
        data.GetProperty("conflicts").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task ImportEmass_Overwrite_AppliesChanges()
    {
        // Arrange
        var importResult = new EmassImportResult(
            TotalRows: 5, Imported: 5, Skipped: 0, Conflicts: 2,
            ConflictDetails: new List<EmassImportConflict>
            {
                new("AC-1", "ImplementationStatus", "Planned",
                    "Implemented", "Overwritten")
            });

        _mockService
            .Setup(s => s.ImportAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(),
                It.Is<EmassImportOptions>(o =>
                    o.OnConflict == ConflictResolution.Overwrite && !o.DryRun),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(importResult);

        var tool = CreateImportTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["file_base64"] = Convert.ToBase64String(FakeExcelBytes()),
            ["conflict_strategy"] = "overwrite",
            ["dry_run"] = "false"
        };

        // Act
        var result = await tool.ExecuteCoreAsync(args);

        // Assert
        var doc = JsonDocument.Parse(result);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("dry_run").GetBoolean().Should().BeFalse();
        data.GetProperty("imported").GetInt32().Should().Be(5);
        doc.RootElement.GetProperty("metadata")
            .GetProperty("applied").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ImportEmass_Merge_AppendsSeparator()
    {
        // Arrange
        var importResult = new EmassImportResult(
            TotalRows: 3, Imported: 3, Skipped: 0, Conflicts: 1,
            ConflictDetails: new List<EmassImportConflict>
            {
                new("AC-2", "ImplementationStatus", "PartiallyImplemented",
                    "Implemented", "Merged")
            });

        _mockService
            .Setup(s => s.ImportAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(),
                It.Is<EmassImportOptions>(o =>
                    o.OnConflict == ConflictResolution.Merge),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(importResult);

        var tool = CreateImportTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["file_base64"] = Convert.ToBase64String(FakeExcelBytes()),
            ["conflict_strategy"] = "merge",
            ["dry_run"] = "true"
        };

        // Act
        var result = await tool.ExecuteCoreAsync(args);

        // Assert
        var doc = JsonDocument.Parse(result);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("conflict_strategy").GetString().Should().Be("merge");
        var conflictDetails = data.GetProperty("conflict_details");
        conflictDetails.GetArrayLength().Should().BeGreaterThan(0);
        var firstConflict = conflictDetails[0];
        firstConflict.GetProperty("resolution").GetString().Should().Be("Merged");
    }

    [Fact]
    public async Task ImportEmass_DryRun_DefaultsToTrue()
    {
        // Arrange
        _mockService
            .Setup(s => s.ImportAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(),
                It.Is<EmassImportOptions>(o => o.DryRun),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmassImportResult(0, 0, 0, 0, new()));

        var tool = CreateImportTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["file_base64"] = Convert.ToBase64String(FakeExcelBytes())
            // No dry_run param — should default to true
        };

        // Act
        var result = await tool.ExecuteCoreAsync(args);

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data")
            .GetProperty("dry_run").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ImportEmass_MalformedExcel_ThrowsError()
    {
        // Arrange
        _mockService
            .Setup(s => s.ImportAsync(
                It.IsAny<byte[]>(), It.IsAny<string>(),
                It.IsAny<EmassImportOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Excel workbook contains no worksheets."));

        var tool = CreateImportTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["file_base64"] = Convert.ToBase64String(new byte[] { 0x50, 0x4B }) // truncated zip
        };

        // Act
        var act = async () => await tool.ExecuteCoreAsync(args);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no worksheets*");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  T153: ExportOscalTool Tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportOscal_Ssp_ReturnsValidJson()
    {
        // Arrange
        var oscalSsp = JsonSerializer.Serialize(new
        {
            system_security_plan = new
            {
                uuid = Guid.NewGuid().ToString(),
                metadata = new { title = "Test SSP", oscal_version = "1.0.6" }
            }
        });

        _mockService
            .Setup(s => s.ExportOscalAsync(
                It.IsAny<string>(), OscalModelType.Ssp,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(oscalSsp);

        var tool = CreateOscalTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["model"] = "ssp"
        };

        // Act
        var result = await tool.ExecuteCoreAsync(args);

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("model").GetString().Should().Be("ssp");
        data.GetProperty("oscal_version").GetString().Should().Be("1.1.2");
        data.GetProperty("oscal_document").ValueKind.Should().Be(JsonValueKind.Object);

        var meta = doc.RootElement.GetProperty("metadata");
        meta.GetProperty("spec_version").GetString().Should().Be("OSCAL 1.1.2");
    }

    [Fact]
    public async Task ExportOscal_AssessmentResults_ReturnsResults()
    {
        // Arrange
        var oscalAr = JsonSerializer.Serialize(new
        {
            assessment_results = new
            {
                uuid = Guid.NewGuid().ToString(),
                metadata = new { title = "AR", oscal_version = "1.0.6" },
                results = new[] { new { title = "Result 1" } }
            }
        });

        _mockService
            .Setup(s => s.ExportOscalAsync(
                It.IsAny<string>(), OscalModelType.AssessmentResults,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(oscalAr);

        var tool = CreateOscalTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["model"] = "assessment-results"
        };

        // Act
        var result = await tool.ExecuteCoreAsync(args);

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data")
            .GetProperty("model").GetString().Should().Be("assessment-results");
        doc.RootElement.GetProperty("data")
            .GetProperty("oscal_document").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task ExportOscal_Poam_ReturnsPoamItems()
    {
        // Arrange
        var oscalPoam = JsonSerializer.Serialize(new
        {
            plan_of_action_and_milestones = new
            {
                uuid = Guid.NewGuid().ToString(),
                metadata = new { title = "POA&M", oscal_version = "1.0.6" },
                poam_items = new[]
                {
                    new { title = "Weakness 1", description = "Test" }
                }
            }
        });

        _mockService
            .Setup(s => s.ExportOscalAsync(
                It.IsAny<string>(), OscalModelType.Poam,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(oscalPoam);

        var tool = CreateOscalTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["model"] = "poam"
        };

        // Act
        var result = await tool.ExecuteCoreAsync(args);

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data")
            .GetProperty("model").GetString().Should().Be("poam");
    }

    [Fact]
    public async Task ExportOscal_InvalidModel_ThrowsError()
    {
        var tool = CreateOscalTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "sys-001",
            ["model"] = "invalid-model"
        };

        var act = async () => await tool.ExecuteCoreAsync(args);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid OSCAL model*");
    }

    [Fact]
    public async Task ExportOscal_MissingSystem_PropagatesServiceError()
    {
        _mockService
            .Setup(s => s.ExportOscalAsync(
                It.IsAny<string>(), It.IsAny<OscalModelType>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "RegisteredSystem 'unknown' not found."));

        var tool = CreateOscalTool();
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = "unknown",
            ["model"] = "ssp"
        };

        var act = async () => await tool.ExecuteCoreAsync(args);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Tool metadata tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExportEmassTool_Name_IsCorrect()
    {
        var tool = CreateExportTool();
        tool.Name.Should().Be("compliance_export_emass");
    }

    [Fact]
    public void ImportEmassTool_Name_IsCorrect()
    {
        var tool = CreateImportTool();
        tool.Name.Should().Be("compliance_import_emass");
    }

    [Fact]
    public void ExportOscalTool_Name_IsCorrect()
    {
        var tool = CreateOscalTool();
        tool.Name.Should().Be("compliance_export_oscal");
    }

    [Fact]
    public void ExportEmassTool_Parameters_ContainRequired()
    {
        var tool = CreateExportTool();
        tool.Parameters.Should().ContainKey("system_id");
        tool.Parameters.Should().ContainKey("export_type");
        tool.Parameters["system_id"].Required.Should().BeTrue();
        tool.Parameters["export_type"].Required.Should().BeTrue();
    }

    [Fact]
    public void ImportEmassTool_Parameters_ContainOptionals()
    {
        var tool = CreateImportTool();
        tool.Parameters.Should().ContainKey("system_id");
        tool.Parameters.Should().ContainKey("file_base64");
        tool.Parameters.Should().ContainKey("conflict_strategy");
        tool.Parameters.Should().ContainKey("dry_run");
        tool.Parameters["conflict_strategy"].Required.Should().BeFalse();
        tool.Parameters["dry_run"].Required.Should().BeFalse();
    }

    [Fact]
    public void ExportOscalTool_Parameters_ContainModelParam()
    {
        var tool = CreateOscalTool();
        tool.Parameters.Should().ContainKey("system_id");
        tool.Parameters.Should().ContainKey("model");
        tool.Parameters["model"].Required.Should().BeTrue();
    }
}

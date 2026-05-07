using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="EmassImportParser"/> (T071 / FR-031).
/// Generates an in-memory eMASS-style XLSX with 5 systems including one
/// malformed row, then asserts per-system counts + malformed flagging.
/// </summary>
public class EmassImportParserTests
{
    private readonly EmassImportParser _sut = new(NullLogger<EmassImportParser>.Instance);

    [Fact]
    public async Task ParseAsync_FiveSystemsXlsx_ReturnsAllWithCounts()
    {
        await using var stream = BuildFiveSystemFixture();

        var result = await _sut.ParseAsync(stream, "fixture.xlsx");

        result.Systems.Should().HaveCount(5);
        result.Systems.Where(s => s.MalformedReason is null).Should().HaveCount(4);
        result.Systems.Where(s => s.MalformedReason is not null).Should().HaveCount(1);

        var sysA = result.Systems.Single(s => s.SystemIdentifier == "SYS-A");
        sysA.SystemName.Should().Be("Acme Portal");
        sysA.ControlCount.Should().Be(120);
        sysA.PoamCount.Should().Be(8);
    }

    [Fact]
    public async Task ParseAsync_MissingNameFlagsMalformedNotFails()
    {
        await using var stream = BuildFiveSystemFixture();

        var result = await _sut.ParseAsync(stream, "fixture.xlsx");

        var malformed = result.Systems.Single(s => s.MalformedReason is not null);
        malformed.MalformedReason.Should().Contain("system_name");
    }

    [Fact]
    public async Task ParseAsync_PackageZip_ExtractsAndParsesFirstXlsx()
    {
        await using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("export/systems.xlsx");
            await using var entryStream = entry.Open();
            await using var inner = BuildFiveSystemFixture();
            await inner.CopyToAsync(entryStream);
        }
        ms.Position = 0;

        var result = await _sut.ParseAsync(ms, "package.zip");

        result.Systems.Should().HaveCount(5);
        result.SourceFormat.Should().Be("PackageZip");
    }

    private static MemoryStream BuildFiveSystemFixture()
    {
        using var wb = new XLWorkbook();
        var sheet = wb.Worksheets.Add("Systems");
        sheet.Cell(1, 1).Value = "system_identifier";
        sheet.Cell(1, 2).Value = "system_name";
        sheet.Cell(1, 3).Value = "controls";
        sheet.Cell(1, 4).Value = "poams";

        // 4 well-formed rows
        WriteRow(sheet, 2, "SYS-A", "Acme Portal", 120, 8);
        WriteRow(sheet, 3, "SYS-B", "Acme Gateway", 95, 12);
        WriteRow(sheet, 4, "SYS-C", "Acme Vault", 45, 1);
        WriteRow(sheet, 5, "SYS-D", "Acme Identity", 67, 0);

        // 1 malformed row — missing system_name
        sheet.Cell(6, 1).Value = "SYS-E";
        sheet.Cell(6, 3).Value = 10;
        sheet.Cell(6, 4).Value = 0;

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static void WriteRow(IXLWorksheet sheet, int row, string id, string name, int controls, int poams)
    {
        sheet.Cell(row, 1).Value = id;
        sheet.Cell(row, 2).Value = name;
        sheet.Cell(row, 3).Value = controls;
        sheet.Cell(row, 4).Value = poams;
    }
}

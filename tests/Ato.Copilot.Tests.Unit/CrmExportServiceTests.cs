using Xunit;
using FluentAssertions;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Mcp.Services;
using System.Text;

namespace Ato.Copilot.Tests.Unit;

/// <summary>
/// Feature 043 – T038: Unit tests for CrmExportService.
/// CSV/Excel generation across Custom, FedRAMP, and eMASS layouts, plus import parsing.
/// </summary>
public class CrmExportServiceTests
{
    private readonly CrmExportService _service = new();

    private static CrmResult CreateSampleCrm() => new()
    {
        FamilyGroups =
        [
            new CrmFamilyGroup
            {
                Family = "AC",
                FamilyName = "Access Control",
                Controls =
                [
                    new CrmEntry { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure Gov", CustomerResponsibility = null },
                    new CrmEntry { ControlId = "AC-2", InheritanceType = "Shared", Provider = "Azure Gov", CustomerResponsibility = "Manage user accounts" },
                ]
            },
            new CrmFamilyGroup
            {
                Family = "AU",
                FamilyName = "Audit and Accountability",
                Controls =
                [
                    new CrmEntry { ControlId = "AU-1", InheritanceType = "Customer", Provider = null, CustomerResponsibility = "Org defines audit policy" },
                ]
            }
        ]
    };

    // ─── CSV Generation ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("custom")]
    [InlineData("fedramp")]
    [InlineData("emass")]
    public void GenerateCsv_AllLayouts_ProducesValidOutput(string layout)
    {
        var csv = Encoding.UTF8.GetString(_service.GenerateCsv(CreateSampleCrm(), layout));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.Should().Be(4); // 1 header + 3 data rows
        lines[0].Should().Contain("Control");
    }

    [Fact]
    public void GenerateCsv_Custom_ContainsExpectedHeaders()
    {
        var csv = Encoding.UTF8.GetString(_service.GenerateCsv(CreateSampleCrm(), "custom"));
        csv.Should().StartWith("Control ID,Family,Inheritance Type,Provider,Customer Responsibility,Designation Source");
    }

    [Fact]
    public void GenerateCsv_FedRamp_ContainsExpectedHeaders()
    {
        var csv = Encoding.UTF8.GetString(_service.GenerateCsv(CreateSampleCrm(), "fedramp"));
        csv.Should().StartWith("Control ID,Control Family,Responsible Role,CSP/CP Name,Customer Responsibility,Designation Source");
    }

    [Fact]
    public void GenerateCsv_Emass_MapsInheritanceToStatus()
    {
        var csv = Encoding.UTF8.GetString(_service.GenerateCsv(CreateSampleCrm(), "emass"));
        csv.Should().Contain("Implemented"); // inherited→Implemented
        csv.Should().Contain("Customer"); // eMASS "Responsible Entity"
    }

    [Fact]
    public void GenerateCsv_QuotedFields_EscapedCorrectly()
    {
        var crm = new CrmResult
        {
            FamilyGroups =
            [
                new CrmFamilyGroup
                {
                    Family = "AC", FamilyName = "Access Control",
                    Controls = [new CrmEntry { ControlId = "AC-1", InheritanceType = "Customer", CustomerResponsibility = "Handle \"quotes\", commas" }]
                }
            ]
        };

        var csv = Encoding.UTF8.GetString(_service.GenerateCsv(crm, "custom"));
        csv.Should().Contain("\"Handle \"\"quotes\"\", commas\"");
    }

    [Fact]
    public void GenerateCsv_EmptyBaseline_ProducesHeaderOnly()
    {
        var crm = new CrmResult { FamilyGroups = [] };
        var csv = Encoding.UTF8.GetString(_service.GenerateCsv(crm, "custom"));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(1);
    }

    // ─── Excel Generation ───────────────────────────────────────────────────

    [Theory]
    [InlineData("custom")]
    [InlineData("fedramp")]
    [InlineData("emass")]
    public void GenerateExcel_AllLayouts_ProducesValidXlsx(string layout)
    {
        var bytes = _service.GenerateExcel(CreateSampleCrm(), layout);
        bytes.Should().NotBeEmpty();
        // XLSX magic bytes: PK (ZIP header)
        bytes[0].Should().Be(0x50);
        bytes[1].Should().Be(0x4B);
    }

    // ─── CSV Parsing ────────────────────────────────────────────────────────

    [Fact]
    public void ParseCsv_ValidFile_ReturnsColumnsAndRows()
    {
        var csv = "Control ID,Type,Provider\nAC-1,Inherited,Azure Gov\nAC-2,Customer,\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = _service.ParseCsv(stream);

        result.Columns.Should().Equal("Control ID", "Type", "Provider");
        result.Rows.Should().HaveCount(2);
        result.Rows[0]["Control ID"].Should().Be("AC-1");
        result.Rows[0]["Type"].Should().Be("Inherited");
        result.SampleRows.Count.Should().BeLessOrEqualTo(5);
    }

    [Fact]
    public void ParseCsv_QuotedFields_ParsedCorrectly()
    {
        var csv = "Name,Value\n\"Last, First\",\"He said \"\"hello\"\"\"\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = _service.ParseCsv(stream);
        result.Rows[0]["Name"].Should().Be("Last, First");
        result.Rows[0]["Value"].Should().Be("He said \"hello\"");
    }

    // ─── Column Mapping Suggestion ──────────────────────────────────────────

    [Fact]
    public void SuggestColumnMapping_StandardHeaders_MapsCorrectly()
    {
        var cols = new List<string> { "Control ID", "Inheritance Type", "Provider", "Customer Responsibility" };
        var mapping = _service.SuggestColumnMapping(cols);

        mapping["controlId"].Should().Be("Control ID");
        mapping["inheritanceType"].Should().Be("Inheritance Type");
        mapping["provider"].Should().Be("Provider");
        mapping["customerResponsibility"].Should().Be("Customer Responsibility");
    }

    [Fact]
    public void SuggestColumnMapping_EmassHeaders_MapsCorrectly()
    {
        var cols = new List<string> { "Control Number", "Implementation Status", "Responsible Entity", "Customer Responsibility Description" };
        var mapping = _service.SuggestColumnMapping(cols);

        mapping["controlId"].Should().Be("Control Number");
        mapping["inheritanceType"].Should().Be("Implementation Status");
    }

    // ─── Feature 044: Designation Source in CRM ─────────────────────────────

    [Fact]
    public void GenerateCsv_Custom_IncludesDesignationSourceColumn()
    {
        var crm = CreateCrmWithDesignationSources();
        var csv = Encoding.UTF8.GetString(_service.GenerateCsv(crm, "custom"));
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Contain("Designation Source");
        lines[1].Should().Contain("Org Default");
        lines[2].Should().Contain("System Override");
        lines[3].Should().EndWith(","); // Undesignated has empty source
    }

    [Fact]
    public void GenerateCsv_FedRamp_IncludesDesignationSourceColumn()
    {
        var crm = CreateCrmWithDesignationSources();
        var csv = Encoding.UTF8.GetString(_service.GenerateCsv(crm, "fedramp"));
        csv.Should().Contain("Designation Source");
        csv.Should().Contain("Org Default");
    }

    [Fact]
    public void GenerateCsv_Emass_IncludesDesignationSourceColumn()
    {
        var crm = CreateCrmWithDesignationSources();
        var csv = Encoding.UTF8.GetString(_service.GenerateCsv(crm, "emass"));
        csv.Should().Contain("Designation Source");
        csv.Should().Contain("System Override");
    }

    [Fact]
    public void GenerateExcel_WithDesignationSource_ProducesValidXlsx()
    {
        var crm = CreateCrmWithDesignationSources();
        var bytes = _service.GenerateExcel(crm, "custom");
        bytes.Should().NotBeEmpty();
        bytes[0].Should().Be(0x50); // PK ZIP header
    }

    private static CrmResult CreateCrmWithDesignationSources() => new()
    {
        FamilyGroups =
        [
            new CrmFamilyGroup
            {
                Family = "AC",
                FamilyName = "Access Control",
                Controls =
                [
                    new CrmEntry { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure Gov", DesignationSource = "Org Default" },
                    new CrmEntry { ControlId = "AC-2", InheritanceType = "Shared", Provider = "Custom", CustomerResponsibility = "Manage accounts", DesignationSource = "System Override" },
                    new CrmEntry { ControlId = "AC-3", InheritanceType = "Undesignated", DesignationSource = null },
                ]
            }
        ]
    };
}

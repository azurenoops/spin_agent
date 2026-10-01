using System.Net;
using System.Net.Http.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

public sealed partial class CapabilityResponsibilityTests
{
    [Theory]
    [InlineData("Shared", "Shared")]
    [InlineData("Inherited", "Inherited")]
    [InlineData("Customer", "System-Specific")]
    public async Task PanelConfirmation_PreservesReviewEvidenceAndLocalDuties_InCrmSspAndEmassPreparation(
        string allocation, string exportedResponsibility)
    {
        // Arrange
        await AddBaselineAsync();
        (await _client.PostAsJsonAsync(Route, new { capabilityId = _capability })).EnsureSuccessStatusCode();
        var preview = (await _client.GetFromJsonAsync<CapabilityResponsibilityResponse>($"{Route}/responsibilities"))!;
        var source = preview.Items.Single(item => item.ControlId == "AU-6");
        var provider = allocation == "Customer" ? null : "Synthetic reviewed provider";
        const string duties = "Maintain local operational review evidence.";
        const string notes = "Provider duties: Operate the scoped archive.\n\nBasis for this allocation: Reviewed synthetic evidence revision 1.";
        var request = new ConfirmCapabilityResponsibilitiesRequest(preview.BaselineId!, source.SourceRevision,
            source.ReviewRevision, [new("AU-6", allocation, provider, duties)], true, true, notes);

        // Act
        var response = await _client.PutAsJsonAsync($"{Route}/{_capability}/responsibilities", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var saved = (await response.Content.ReadFromJsonAsync<CapabilityResponsibilityResponse>())!
            .Items.Single(item => item.ControlId == "AU-6");
        saved.Allocation!.InheritanceType.Should().Be(allocation);
        saved.Allocation.CustomerResponsibility.Should().Be(duties);
        saved.ReviewNotes.Should().Be(notes);
        saved.ProviderCoverageVerified.Should().BeTrue();
        saved.CustomerDutiesReviewed.Should().BeTrue();
        saved.ConfirmedBy.Should().NotBeNullOrWhiteSpace();
        saved.ConfirmedAt.Should().NotBeNull();
        saved.ReviewedSourceRevision.Should().Be(source.SourceRevision);
        saved.ReviewedSourceSnapshotJson.Should().NotBeNullOrWhiteSpace();
        await using var db = new AtoCopilotContext(_options);
        (await db.ControlInheritances.SingleAsync()).CustomerResponsibility.Should().Be(duties);
        var scopes = _app.Services.GetRequiredService<IServiceScopeFactory>();
        var crm = await new BaselineService(scopes, Mock.Of<IReferenceDataService>(),
            NullLogger<BaselineService>.Instance, Mock.Of<IOrgInheritanceService>()).GenerateCrmAsync(_system);
        crm.FamilyGroups.SelectMany(family => family.Controls).Single(control => control.ControlId == "AU-6")
            .CustomerResponsibility.Should().Be(duties);
        var ssp = await new SspService(scopes, NullLogger<SspService>.Instance).GenerateSspAsync(_system, sections: ["controls"]);
        ssp.Content.Should().Contain($"**Responsibility**: {allocation}");
        var exporter = new EmassExportService(scopes, NullLogger<EmassExportService>.Instance,
            Mock.Of<IOscalSspExportService>());
        using var workbook = new XLWorkbook(new MemoryStream(await exporter.ExportControlsAsync(_system)));
        var row = workbook.Worksheet("Controls").RowsUsed().Single(row => row.Cell(5).GetString() == "AU-6");
        row.Cell(10).GetString().Should().Be(provider ?? "");
        row.Cell(11).GetString().Should().Be(exportedResponsibility);
        row.Cell(12).GetString().Should().Be("Not Assessed");
    }
}

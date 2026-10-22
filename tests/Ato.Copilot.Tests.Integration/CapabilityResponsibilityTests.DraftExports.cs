using System.Net;
using System.Net.Http.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
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
    [InlineData("Customer")]
    [InlineData("Shared")]
    [InlineData("Inherited")]
    public async Task DraftConfirmation_ProjectsAcceptedAllocationToDocumentationAndEmassControls(string allocation)
    {
        // Arrange
        await AddBaselineAsync();
        await using (var db = new AtoCopilotContext(_options))
        {
            var implementation = new ControlImplementation
            {
                TenantId = _tenant,
                RegisteredSystemId = _system,
                ControlId = "AU-6",
                Narrative = "Human approved text",
                TechnicalNarrative = "Human approved text",
                ApprovalStatus = SspSectionStatus.Approved,
                ImplementationStatus = ImplementationStatus.Planned
            };
            var approved = new NarrativeVersion
            {
                TenantId = _tenant,
                ControlImplementationId = implementation.Id,
                Content = "Human approved text",
                Status = SspSectionStatus.Approved,
                AuthoredBy = "fixture-author"
            };
            db.AddRange(implementation, approved);
            await db.SaveChangesAsync();
            implementation.ApprovedVersionId = approved.Id;
            await db.SaveChangesAsync();
        }

        var prepare = await _client.PostAsJsonAsync($"{Route}/drafts/AU-6/prepare",
            new PrepareResponsibilityDraftRequest(null, 0, Generate: false));
        prepare.StatusCode.Should().Be(HttpStatusCode.OK, await prepare.Content.ReadAsStringAsync());
        var first = (await prepare.Content.ReadFromJsonAsync<ResponsibilityDraftContext>())!;
        var values = first.Draft!.Values.ToDictionary(entry => entry.Key, entry => entry.Value.Value);
        values["allocation"] = allocation;
        values["customer"] = "Human confirmed duties.";
        values["basis"] = "Human reviewed the current system responsibility.";
        if (allocation is "Shared" or "Inherited")
        {
            values["provider"] = "Human verified provider";
            values["providerDuties"] = "Human reviewed provider duties.";
        }
        if (allocation == "Inherited")
        {
            values["scope"] = "Reviewed synthetic scope";
            values["exclusions"] = "No exclusions in the reviewed synthetic source";
            values["source"] = "Synthetic source revision 1";
        }
        var save = await _client.PutAsJsonAsync($"{Route}/drafts/record/{first.Draft.Id}",
            new SaveResponsibilityDraftRequest(first.Draft.Revision, values));
        save.StatusCode.Should().Be(HttpStatusCode.OK, await save.Content.ReadAsStringAsync());
        var edited = (await save.Content.ReadFromJsonAsync<ResponsibilityDraftResponse>())!;

        // Act
        var response = await _client.PostAsJsonAsync($"{Route}/drafts/record/{edited.Id}/confirm",
            new ConfirmResponsibilityDraftRequest(edited.Revision, first.SourceHash, true, true,
                "Human reviewed current sources and responsibility duties."));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var verify = new AtoCopilotContext(_options);
        var implementationAfter = await verify.ControlImplementations.SingleAsync();
        implementationAfter.Narrative.Should().Be("Human approved text");
        implementationAfter.TechnicalNarrative.Should().Be("Human approved text");
        implementationAfter.ApprovalStatus.Should().Be(SspSectionStatus.Approved);
        implementationAfter.ImplementationStatus.Should().Be(ImplementationStatus.Planned);
        (await verify.ControlInheritances.SingleAsync()).CustomerResponsibility.Should().Be("Human confirmed duties.");
        (await verify.Set<ResponsibilityDraftHistory>().CountAsync()).Should().Be(3);

        var scopeFactory = _app.Services.GetRequiredService<IServiceScopeFactory>();
        var crm = await new BaselineService(scopeFactory, Mock.Of<IReferenceDataService>(),
            NullLogger<BaselineService>.Instance, Mock.Of<IOrgInheritanceService>()).GenerateCrmAsync(_system);
        crm.FamilyGroups.SelectMany(group => group.Controls).Single(control => control.ControlId == "AU-6")
            .CustomerResponsibility.Should().Be("Human confirmed duties.");

        var ssp = await new SspService(scopeFactory, NullLogger<SspService>.Instance)
            .GenerateSspAsync(_system, sections: ["controls"]);
        ssp.Content.Should().Contain($"**Responsibility**: {allocation}").And.Contain("Human approved text");

        var exporter = new EmassExportService(scopeFactory, NullLogger<EmassExportService>.Instance,
            Mock.Of<IOscalSspExportService>());
        using var workbook = new XLWorkbook(new MemoryStream(await exporter.ExportControlsAsync(_system)));
        var exportRow = workbook.Worksheet("Controls").RowsUsed()
            .Single(row => row.Cell(5).GetString() == "AU-6");
        exportRow.Cell(11).GetString().Should().Be(allocation == "Customer" ? "System-Specific" : allocation);
        exportRow.Cell(12).GetString().Should().Be("Not Assessed");
        var reread = (await _client.GetFromJsonAsync<ResponsibilityDraftContext>($"{Route}/drafts/AU-6"))!;
        reread.Draft!.IsStale.Should().BeFalse();
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.IO.Compression;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Tests.Unit.Server;

public sealed partial class ProfileDraftPersistenceTests
{
    [Fact]
    public async Task DataHandling_LegacyOmissionsRetainNewFieldsAndUnderReviewEditsStayBlocked()
    {
        // Arrange
        (await PutAsync("DataTypes", """{"content":"{}","childItems":[{"dataTypeName":"Recorded data","sensitivityClassification":"CUI","retentionRule":"Retained handling rule","privacyApplicability":"NoPii"}]}"""))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var initial = await _client.GetFromJsonAsync<JsonElement>(Root + "DataTypes");
        var id = initial.GetProperty("dataTypeEntries")[0].GetProperty("id").GetString();
        var legacy = $$"""{"content":"{}","childItems":[{"id":"{{id}}","dataTypeName":"Recorded data","sensitivityClassification":"CUI"}]}""";
        // Act
        (await PutAsync("DataTypes", legacy)).StatusCode.Should().Be(HttpStatusCode.OK);
        // Assert
        var retained = await _client.GetFromJsonAsync<JsonElement>(Root + "DataTypes");
        retained.GetProperty("dataTypeEntries")[0].GetProperty("retentionRule").GetString().Should().Be("Retained handling rule");
        // Act
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ISystemProfileService>().SubmitForReviewAsync("mission", [ProfileSectionType.DataTypes], "owner");
        var blocked = await PutAsync("DataTypes", legacy);
        // Assert
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    [Theory]
    [InlineData("confidentialityImpact", "ApprovedModerate")]
    [InlineData("privacyApplicability", "AutomaticPrivacyApproval")]
    [InlineData("categorizationReference", "javascript:alert(1)")]
    [InlineData("categorizationReference", "//example.invalid/source")]
    [InlineData("categorizationReference", "/\\example.invalid/source")]
    [InlineData("categorizationReference", "http://example.invalid/source")]
    public async Task DataHandling_RejectsUnsupportedImpactPrivacyAndUnsafeSource(string field, string value)
    {
        // Arrange
        var payload = $$"""{"content":"{}","childItems":[{"dataTypeName":"Recorded data","sensitivityClassification":"CUI","{{field}}":"{{value}}"}]}""";
        // Act
        var result = await PutAsync("DataTypes", payload);
        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task DataHandling_SchemaIsRerunnableAndNeverBackfillsGuessedImpacts()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE DataTypeEntries (Id TEXT PRIMARY KEY, DataTypeName TEXT);
            INSERT INTO DataTypeEntries VALUES ('legacy','Legacy data');
            """);
        // Act
        await DataTypeHandlingSchemaAdditions.ApplyAsync(db);
        await DataTypeHandlingSchemaAdditions.ApplyAsync(db);
        // Assert
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM DataTypeEntries WHERE CuiCategory IS NOT NULL OR ConfidentialityImpact IS NOT NULL OR PrivacyApplicability IS NOT NULL OR RetentionRule IS NOT NULL").SingleAsync()).Should().Be(0);
    }
    [Fact]
    public async Task DataHandling_RoundTripsReviewedNativeOutputAndKeepsLaterDraftOutsideApproval()
    {
        // Arrange
        await AddReviewerAsync();
        const string payload = """
            {"content":"{\"dataOverview\":\"Recorded information handling\",\"customSource\":\"preserve\"}","childItems":[{
             "dataTypeName":"Recorded system security data","description":"Recorded logs and control documentation","sensitivityClassification":"CUI",
             "cuiCategory":"Recorded systems information","confidentialityImpact":"Moderate","integrityImpact":"Moderate","availabilityImpact":"Low",
             "privacyApplicability":"NoPii","retentionRule":"Retain for recorded six-year period","disposalMethod":"Recorded cryptographic erasure",
             "categorizationRationale":"Recorded mission impact basis","categorizationReference":"https://example.invalid/categorization",
             "source":"Recorded collectors","destination":"Recorded archive","applicableRegulations":"Recorded regulations"}]}
            """;
        // Act
        var saved = await PutAsync("DataTypes", payload);
        // Assert
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var current = await _client.GetFromJsonAsync<JsonElement>(Root + "DataTypes");
        using var requested = JsonDocument.Parse(payload);
        var expected = requested.RootElement.GetProperty("childItems")[0];
        foreach (var field in expected.EnumerateObject())
            current.GetProperty("dataTypeEntries")[0].GetProperty(field.Name).GetString().Should().Be(field.Value.GetString());
        var scopes = _app.Services.GetRequiredService<IServiceScopeFactory>();
        using (var scope = scopes.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ISystemProfileService>();
            await service.SubmitForReviewAsync("mission", [ProfileSectionType.DataTypes], "owner");
            await service.ReviewSectionAsync("mission", ProfileSectionType.DataTypes, ReviewDecision.Approve, "reviewer");
        }
        // Act
        var oscal = await new OscalSspExportService(scopes, NullLogger<OscalSspExportService>.Instance).ExportAsync("mission");
        var template = new DocumentTemplateService(scopes, NullLogger<DocumentTemplateService>.Instance);
        var word = await template.RenderDocxAsync("mission", "ssp");
        var pdf = await template.RenderPdfAsync("mission", "ssp");
        // Assert
        oscal.OscalJson.Should().Contain("Recorded systems information").And.Contain("Retain for recorded six-year period").And.Contain("Recorded cryptographic erasure");
        oscal.OscalJson.Should().Contain("recorded declarations").And.Contain("privacy determination");
        using (var zip = new ZipArchive(new MemoryStream(word)))
        using (var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()))
            (await reader.ReadToEndAsync()).Should().Contain("Recorded mission impact basis").And.Contain("Recorded cryptographic erasure");
        using (var document = UglyToad.PdfPig.PdfDocument.Open(pdf))
            string.Join("\n", document.GetPages().Select(p => p.Text)).Should().Contain("Retain for recorded six-year period");
        // Act
        (await PutAsync("DataTypes", payload.Replace("Retain for recorded six-year period", "LATER DRAFT retention"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var retained = await new OscalSspExportService(scopes, NullLogger<OscalSspExportService>.Instance).ExportAsync("mission");
        // Assert
        retained.OscalJson.Should().Contain("Retain for recorded six-year period").And.NotContain("LATER DRAFT retention");
        var retainedWord = await template.RenderDocxAsync("mission", "ssp");
        using (var zip = new ZipArchive(new MemoryStream(retainedWord)))
        using (var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()))
            (await reader.ReadToEndAsync()).Should().Contain("Retain for recorded six-year period").And.NotContain("LATER DRAFT retention");
        using (var document = UglyToad.PdfPig.PdfDocument.Open(await template.RenderPdfAsync("mission", "ssp")))
            string.Join("\n", document.GetPages().Select(p => p.Text)).Should().Contain("Retain for recorded six-year period").And.NotContain("LATER DRAFT retention");
    }
}

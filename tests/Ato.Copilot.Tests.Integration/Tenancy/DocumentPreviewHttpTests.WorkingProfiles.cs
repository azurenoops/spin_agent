using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed partial class DocumentPreviewHttpTests
{
    [Fact]
    public async Task WorkingProfilePreview_GetAndRetainedPostReflectSavedContributionsWithoutExportApproval()
    {
        // Arrange
        var fixture = await SeedAsync();
        using var client = Client(fixture.Actor);
        var profilePath = $"/api/dashboard/systems/{fixture.System}/profile/UsersAndAccess";
        using var created = await client.PutAsJsonAsync(profilePath, new
        {
            content = """{"access":"Saved working access context"}""",
            childItems = new[] { new { categoryName = "Saved working operators", approximateCount = 7 } }
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var saved = await created.Content.ReadFromJsonAsync<JsonElement>();
        var rowId = saved.GetProperty("userCategories")[0].GetProperty("id").GetString()!;

        // Act
        var before = await client.GetFromJsonAsync<JsonElement>(PreviewPath(fixture.System));
        using var edited = await client.PutAsJsonAsync(profilePath, new
        {
            content = """{"access":"Updated working access context"}""",
            childItems = new[] { new { id = rowId, revision = 1, categoryName = "Updated working operators", approximateCount = 9 } }
        });
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await client.GetFromJsonAsync<JsonElement>(PreviewPath(fixture.System));
        using var retainedResponse = await client.PostAsync(PreviewPath(fixture.System), null);
        retainedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retained = await retainedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var previewId = retained.GetProperty("previewId").GetGuid();
        using var promotion = await client.PostAsJsonAsync($"/api/dashboard/systems/{fixture.System}/exports",
            new { format = "json", sourcePreviewId = previewId });

        // Assert
        try
        {
            var original = WorkingProfile(before, "UsersAndAccess");
            var updated = WorkingProfile(after, "UsersAndAccess");
            original.GetProperty("content").GetProperty("scalarContent").GetString().Should().Contain("Saved working");
            updated.GetProperty("content").GetProperty("scalarContent").GetString().Should().Contain("Updated working");
            var category = updated.GetProperty("content").GetProperty("userCategories")[0];
            category.GetProperty("id").GetString().Should().Be(rowId);
            category.GetProperty("categoryName").GetString().Should().Be("Updated working operators");
            category.GetProperty("approximateCount").GetInt32().Should().Be(9);
            category.GetProperty("governanceStatus").GetString().Should().Be("Draft");
            category.GetProperty("revision").GetInt32().Should().Be(2);
            updated.GetProperty("contentHash").GetString().Should().NotBe(original.GetProperty("contentHash").GetString());
            WorkingProfile(retained, "UsersAndAccess").GetRawText().Should().Be(updated.GetRawText());
            after.GetProperty("sourceManifest").GetProperty("profiles")[0].GetProperty("kind").GetString().Should().Be("WorkingProfile");
            retained.GetProperty("sourceManifest").GetProperty("previewOnly").GetBoolean().Should().BeTrue();
            retained.GetProperty("canGenerate").GetBoolean().Should().BeFalse();
            promotion.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var scope = factory.Services.CreateScope();
            var ordinary = await scope.ServiceProvider.GetRequiredService<IOscalSspExportService>().ExportAsync(fixture.System);
            ordinary.OscalJson.Should().NotContain("Updated working operators").And.NotContain("Updated working access context");
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var snapshot = await db.SspExports.SingleAsync(s => s.Id == previewId);
            var settings = factory.Services.GetRequiredService<IOptions<ExportSettings>>().Value;
            File.Delete(Path.Combine(settings.ExportsPath, snapshot.FilePath!));
        }
    }

    private static JsonElement WorkingProfile(JsonElement response, string sectionType)
    {
        using var document = JsonDocument.Parse(response.GetProperty("content").GetString()!);
        foreach (var property in document.RootElement.GetProperty("system-security-plan")
                     .GetProperty("system-characteristics").GetProperty("props").EnumerateArray())
        {
            if (property.GetProperty("name").GetString() != "working-profile") continue;
            using var content = JsonDocument.Parse(property.GetProperty("value").GetString()!);
            if (content.RootElement.GetProperty("sectionType").GetString() == sectionType)
                return content.RootElement.Clone();
        }
        throw new Xunit.Sdk.XunitException($"Working profile '{sectionType}' was not included in the generated SSP.");
    }
}

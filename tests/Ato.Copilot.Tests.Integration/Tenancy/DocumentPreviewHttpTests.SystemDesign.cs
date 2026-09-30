using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed partial class DocumentPreviewHttpTests
{
    [Fact]
    public async Task ApprovedSourceQuery_IsDistinctAndCannotClaimMissingDesignReady()
    {
        // Arrange
        var fixture = await SeedAsync();
        using var client = Client(fixture.Actor);

        // Act
        using var workingResponse = await client.GetAsync(PreviewPath(fixture.System));
        using var approvedResponse = await client.GetAsync(PreviewPath(fixture.System) + "?source=approved");
        using var invalidResponse = await client.GetAsync(PreviewPath(fixture.System) + "?source=unknown");
        workingResponse.StatusCode.Should().Be(HttpStatusCode.OK, await workingResponse.Content.ReadAsStringAsync());
        approvedResponse.StatusCode.Should().Be(HttpStatusCode.OK, await approvedResponse.Content.ReadAsStringAsync());
        var working = await workingResponse.Content.ReadFromJsonAsync<JsonElement>();
        var approved = await approvedResponse.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        workingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        approvedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        working.GetProperty("sourceState").GetString().Should().Be("CurrentWorkingData");
        approved.GetProperty("sourceState").GetString().Should().Be("ApprovedSources");
        approved.GetProperty("canGenerate").GetBoolean().Should().BeFalse();
        approved.GetProperty("sourceGaps").EnumerateArray().Select(g => g.GetProperty("code").GetString())
            .Should().Contain("DESIGN_APPROVAL_UNVERIFIED");
        approved.GetProperty("sourceManifest").TryGetProperty("previewOnly", out var previewOnly).Should().BeFalse();
        using var content = JsonDocument.Parse(working.GetProperty("content").GetString()!);
        var ssp = content.RootElement.GetProperty("system-security-plan");
        var graph = ssp.GetProperty("system-characteristics").GetProperty("props").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "working-system-design").GetProperty("value").GetString()!;
        using var graphJson = JsonDocument.Parse(graph);
        graphJson.RootElement.GetProperty("GovernanceStatus").GetString().Should().Be("NotStarted");
        graphJson.RootElement.GetProperty("ApprovedRevision").ValueKind.Should().Be(JsonValueKind.Null);
        graphJson.RootElement.GetProperty("Nodes").GetArrayLength().Should().BeGreaterThan(0);
        var diagrams = ssp.GetProperty("back-matter").GetProperty("resources").EnumerateArray()
            .Where(r => r.TryGetProperty("base64", out var b) && b.GetProperty("media-type").GetString() == "image/svg+xml").ToArray();
        diagrams.Should().HaveCount(4);
        foreach (var diagram in diagrams)
            Encoding.UTF8.GetString(Convert.FromBase64String(diagram.GetProperty("base64").GetProperty("value").GetString()!))
                .Should().Contain("DRAFT / UNAPPROVED").And.Contain("NotStarted");
        working.GetProperty("canGenerate").GetBoolean().Should().BeFalse();
    }
}

using System.Net;
using System.Text.Json;
using Ato.Copilot.Mcp;
using Ato.Copilot.Tests.Integration.Tenancy;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class NistCatalogIntegrityEndpointTests :
    IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly HttpClient _client;

    public NistCatalogIntegrityEndpointTests(
        MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ValidBundledCatalog_ReportsIntegrityBaseline()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");

        // Act
        using var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, json);
        using var document = JsonDocument.Parse(json);
        var nistHealth = document.RootElement
            .GetProperty("agents")
            .EnumerateArray()
            .Single(agent => agent.GetProperty("name").GetString() == "nist-controls");
        nistHealth.GetProperty("status").GetString().Should().Be("Healthy");
        nistHealth.TryGetProperty("data", out var data).Should().BeTrue(json);
        data.GetProperty("integrityValid").GetBoolean().Should().BeTrue();
        data.GetProperty("schemaValid").GetBoolean().Should().BeTrue();
        data.GetProperty("oscalVersion").GetString().Should().Be("1.1.3");
        data.GetProperty("groupCount").GetInt32().Should().Be(20);
        data.GetProperty("baseControlCount").GetInt32().Should().Be(324);
        data.GetProperty("enhancementCount").GetInt32().Should().Be(872);
        data.GetProperty("totalControlCount").GetInt32().Should().Be(1196);
    }
}

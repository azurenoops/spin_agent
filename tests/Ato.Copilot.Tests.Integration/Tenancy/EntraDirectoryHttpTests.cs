using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class EntraDirectoryHttpTests : IClassFixture<WorkspaceMembershipFactory>
{
    private readonly WorkspaceMembershipFactory factory;
    public EntraDirectoryHttpTests(WorkspaceMembershipFactory factory) => this.factory = factory;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderDirectoryEndpointRequiresAdministrator(bool admin)
    {
        // Arrange
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", "cccccccc-0000-0000-0000-000000000003");
        client.DefaultRequestHeaders.Add("X-Test-Oid", "cccccccc-0000-0000-0000-000000000002");
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "csp");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        if (admin) client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");
        // Act
        var response = await client.GetAsync("/api/csp/directory/connections");
        // Assert
        Assert.Equal(admin ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        if (admin)
        {
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("data").ValueKind);
        }
    }
    [Fact]
    public async Task AnonymousDirectorySearchIsRejected()
    {
        // Arrange
        using var client = factory.CreateClient();
        // Act
        var response = await client.GetAsync("/api/csp/directory/users?connectionId=provider&query=test");
        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

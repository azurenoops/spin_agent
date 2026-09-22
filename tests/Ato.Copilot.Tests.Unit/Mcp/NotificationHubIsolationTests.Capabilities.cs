using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Mcp.Services.Tenancy;
using FluentAssertions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

public partial class NotificationHubIsolationTests
{
    [Theory]
    [InlineData("Cookies")]
    [InlineData("Simulated")]
    public async Task Capabilities_ServerSessionCanPollWithoutMsal_ButDoesNotGrantHubAuthentication(string authenticationType)
    {
        // Arrange
        await SeedAsync();
        var notification = Notification(_tenantA);
        await using (var db = await Db())
        {
            db.AlertNotifications.Add(notification);
            await db.SaveChangesAsync();
        }
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);
        client.DefaultRequestHeaders.Add("X-Test-AuthType", authenticationType);

        // Act
        var response = await client.GetAsync("/api/dashboard/notifications/capabilities");
        var list = await client.GetAsync("/api/dashboard/notifications");
        var negotiate = await client.PostAsync("/hubs/package/negotiate?negotiateVersion=1", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("recipientId").GetGuid().Should().Be(_actor);
        body.GetProperty("rest").GetProperty("available").GetBoolean().Should().BeTrue();
        body.GetProperty("realtime").GetProperty("available").GetBoolean().Should().BeFalse();
        body.GetProperty("realtime").GetProperty("cookieSessionSupported").GetBoolean().Should().BeFalse();
        body.GetProperty("realtime").GetProperty("reasonCode").GetString().Should().Be("REALTIME_BEARER_REQUIRED");
        body.GetProperty("fallback").GetProperty("transport").GetString().Should().Be("rest-polling");
        body.GetProperty("fallback").GetProperty("pollIntervalSeconds").GetInt32().Should().Be(30);
        (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("id").GetGuid()).Should().Equal(notification.Id);
        negotiate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("progress-test", true, null)]
    [InlineData("invalid", false, "REALTIME_BEARER_INVALID")]
    [InlineData("other-actor", false, "REALTIME_ACTOR_MISMATCH")]
    public async Task Capabilities_ValidatesBearerAndMatchesItToTheServerSession(string token, bool ready, string? reason)
    {
        // Arrange
        await SeedAsync();
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/dashboard/notifications/capabilities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("recipientId").GetGuid().Should().Be(_actor);
        body.GetProperty("realtime").GetProperty("available").GetBoolean().Should().Be(ready);
        body.GetProperty("realtime").GetProperty("reasonCode").GetString().Should().Be(reason);
        body.GetProperty("realtime").GetProperty("authentication").GetString().Should().Be("bearer");
        body.GetProperty("realtime").GetProperty("hubPaths").EnumerateArray().Select(p => p.GetString())
            .Should().BeEquivalentTo(["/hubs/notifications", "/hubs/package", "/hubs/import-progress"]);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(token);
    }

    [Theory]
    [InlineData("progress-test", false)]
    [InlineData("cac-token", true)]
    public async Task Capabilities_DoesNotWeakenCacRequirements(string token, bool ready)
    {
        // Arrange
        await SeedAsync();
        using var server = HttpServer(requireCac: true);
        using var client = HttpClient(server, _tenantA);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/dashboard/notifications/capabilities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("realtime").GetProperty("available").GetBoolean().Should().Be(ready);
    }

    [Fact]
    public async Task Capabilities_RechecksWorkspaceWithBearerClaims_NotCookieRoles()
    {
        // Arrange
        await SeedAsync();
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);
        client.DefaultRequestHeaders.Remove("X-Workspace-Kind");
        client.DefaultRequestHeaders.Remove("X-Workspace-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "csp");
        client.DefaultRequestHeaders.Add("X-Test-Csp", "true");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "progress-test");

        // Act
        var response = await client.GetAsync("/api/dashboard/notifications/capabilities");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("rest").GetProperty("available").GetBoolean().Should().BeFalse();
        body.GetProperty("rest").GetProperty("reasonCode").GetString().Should().Be("ORGANIZATION_WORKSPACE_REQUIRED");
        body.GetProperty("realtime").GetProperty("available").GetBoolean().Should().BeFalse();
        body.GetProperty("realtime").GetProperty("reasonCode").GetString().Should().Be("REALTIME_WORKSPACE_NOT_AUTHORIZED");
        body.GetProperty("fallback").GetProperty("transport").GetString().Should().Be("none");
    }

    [Fact]
    public async Task Capabilities_NeverAuthenticatesFromCookieOrQueryAlone_AndRejectsForgedRecipient()
    {
        // Arrange
        await SeedAsync();
        using var server = HttpServer();
        using var anonymous = server.CreateClient();
        anonymous.DefaultRequestHeaders.Add("Cookie", "untrusted=session");
        using var member = HttpClient(server, _tenantA);

        // Act
        var noSession = await anonymous.GetAsync("/api/dashboard/notifications/capabilities");
        var queryToken = await member.GetAsync("/api/dashboard/notifications/capabilities?access_token=progress-test");
        var forged = await member.GetAsync($"/api/dashboard/notifications/capabilities?userId={Guid.NewGuid()}");

        // Assert
        noSession.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        queryToken.StatusCode.Should().Be(HttpStatusCode.OK);
        (await queryToken.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("realtime").GetProperty("available").GetBoolean().Should().BeFalse();
        forged.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Capabilities_SupportRequiresBothMatchingBearerAndLiveSupportSession()
    {
        // Arrange
        await SeedAsync();
        var session = new ImpersonationCookiePayload(_actor.ToString(), Guid.Empty, _tenantA,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5), _directory);
        _support.Setup(s => s.ValidateWorkspaceTokenAsync("support-caps", It.IsAny<CancellationToken>())).ReturnsAsync(() => session);
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);
        client.DefaultRequestHeaders.Remove("X-Workspace-Mode");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "support");
        client.DefaultRequestHeaders.Add("X-Test-Csp", "true");
        client.DefaultRequestHeaders.Add("Cookie", "ato-impersonate=support-caps");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "csp-token");

        // Act
        var valid = await client.GetAsync("/api/dashboard/notifications/capabilities");
        session = session with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
        var expired = await client.GetAsync("/api/dashboard/notifications/capabilities");

        // Assert
        valid.StatusCode.Should().Be(HttpStatusCode.OK);
        (await valid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("realtime").GetProperty("available").GetBoolean().Should().BeTrue();
        expired.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Mcp.Authentication;
using Ato.Copilot.Mcp.Hubs;
using Ato.Copilot.Mcp.Hubs.Notifications;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

public class NotificationHubAuthenticationTests
{
    [Fact]
    public void Hub_UsesDedicatedValidatedAuthenticationScheme()
    {
        // Arrange
        var authorization = (AuthorizeAttribute)Attribute.GetCustomAttribute(typeof(NotificationHub), typeof(AuthorizeAttribute))!;

        // Act
        var scheme = authorization.AuthenticationSchemes;

        // Assert
        scheme.Should().Be("NotificationWorkspace");
    }

    [Theory]
    [InlineData("/hubs/notifications?access_token=valid", false, HttpStatusCode.OK)]
    [InlineData("/hubs/notifications", true, HttpStatusCode.OK)]
    [InlineData("/hubs/notifications?access_token=invalid", false, HttpStatusCode.Unauthorized)]
    [InlineData("/hubs/notifications?access_token=valid&access_token=invalid", false, HttpStatusCode.Unauthorized)]
    [InlineData("/hubs/notifications?access_token=other", true, HttpStatusCode.Unauthorized)]
    [InlineData("/hubs/notifications", false, HttpStatusCode.Unauthorized)]
    [InlineData("/hubs/package?access_token=valid", false, HttpStatusCode.OK)]
    [InlineData("/hubs/import-progress?access_token=valid", false, HttpStatusCode.OK)]
    [InlineData("/hubs/package?access_token=invalid", false, HttpStatusCode.Unauthorized)]
    [InlineData("/hubs/import-progress?access_token=valid&access_token=other", false, HttpStatusCode.Unauthorized)]
    [InlineData("/hubs/package", false, HttpStatusCode.Unauthorized)]
    [InlineData("/hubs/import-progress", false, HttpStatusCode.Unauthorized)]
    [InlineData("/hubs/not-allowed?access_token=valid", false, HttpStatusCode.Unauthorized)]
    [InlineData("/api/not-hub?access_token=valid", false, HttpStatusCode.Unauthorized)]
    public async Task Tokens_AreValidatedOnlyForTheNotificationHub(string path, bool header, HttpStatusCode expected)
    {
        // Arrange
        var validator = Validator();
        using var server = Server(validator);
        using var client = server.CreateClient();
        if (header) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid");

        // Act
        var response = await client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Negotiate_UsesBearerHeader_NotQueryCredentials()
    {
        // Arrange
        using var server = Server(Validator());
        using var client = server.CreateClient();

        // Act
        var query = await client.PostAsync("/hubs/notifications/negotiate?access_token=valid", null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid");
        var header = await client.PostAsync("/hubs/notifications/negotiate", null);

        // Assert
        query.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        header.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CacRequirement_IsNotBypassedByWebSocketQueryToken()
    {
        // Arrange
        using var server = Server(Validator(), requireCac: true);
        using var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/hubs/notifications?access_token=valid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingOrExpiredTokenLifetime_IsRejected(bool expired)
    {
        // Arrange
        var validator = Validator();
        var claims = new List<Claim> { new("tid", Guid.NewGuid().ToString()), new("oid", Guid.NewGuid().ToString()) };
        if (expired) claims.Add(new("exp", DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString()));
        validator.Setup(v => v.ValidateAsync("valid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));
        using var server = Server(validator);
        using var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/hubs/notifications?access_token=valid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnsupportedCredentialType_IsRejected()
    {
        // Arrange
        using var server = Server(Validator());
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "not-a-bearer-token");

        // Act
        var response = await client.GetAsync("/hubs/notifications");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthenticatedButUnauthorizedRole_ReturnsForbidden()
    {
        // Arrange
        using var server = Server(Validator(), requireRole: true);
        using var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/hubs/notifications?access_token=valid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(typeof(PackageHub))]
    [InlineData(typeof(ImportProgressHub))]
    public void ProgressHubs_UseValidatedAuthenticationScheme(Type hubType)
    {
        // Arrange
        var authorization = (AuthorizeAttribute)Attribute.GetCustomAttribute(hubType, typeof(AuthorizeAttribute))!;

        // Act
        var scheme = authorization.AuthenticationSchemes;

        // Assert
        scheme.Should().Be("NotificationWorkspace");
    }

    private static Mock<IEntraJwtTokenValidator> Validator()
    {
        var validator = new Mock<IEntraJwtTokenValidator>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecurityTokenException("Invalid test token"));
        validator.Setup(v => v.ValidateAsync("valid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("tid", Guid.NewGuid().ToString()), new Claim("oid", Guid.NewGuid().ToString()),
                 new Claim("exp", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString())], "test")));
        return validator;
    }

    private static TestServer Server(Mock<IEntraJwtTokenValidator> validator, bool requireCac = false, bool requireRole = false) =>
        new(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddSingleton(validator.Object);
            services.Configure<AzureAdOptions>(options => options.RequireCac = requireCac);
            services.AddAuthentication();
            services.AddAuthorization();
            services.AddWorkspaceNotifications();
        }).Configure(app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                var policy = new AuthorizeAttribute { AuthenticationSchemes = "NotificationWorkspace" };
                foreach (var path in new[] { "/hubs/notifications", "/hubs/package", "/hubs/import-progress", "/hubs/not-allowed" })
                {
                    var hub = endpoints.MapGet(path, () => "Authenticated").RequireAuthorization(policy);
                    if (requireRole) hub.RequireAuthorization(p => p.RequireRole("CSP.Admin"));
                    endpoints.MapPost($"{path}/negotiate", () => "Authenticated").RequireAuthorization(policy);
                }
                endpoints.MapGet("/api/not-hub", () => "Never authenticated by query").RequireAuthorization(policy);
            });
        }));
}

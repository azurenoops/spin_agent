using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Compliance;

public sealed class AuthorizationDecisionEndpointsTests : IAsyncLifetime
{
    private const string TestAuthScheme = "AuthorizationDecisionTest";
    private const string Route = "/api/dashboard/systems/system-1/authorization";

    private readonly Mock<IAuthorizationService> _authorizationService = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase($"AuthorizationDecisionEndpoints_{Guid.NewGuid():N}"));
        builder.Services.AddSingleton(_authorizationService.Object);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ICurrentUserService, CurrentUserService>();
        builder.Services
            .AddAuthentication(TestAuthScheme)
            .AddScheme<AuthenticationSchemeOptions, RoleHeaderAuthenticationHandler>(TestAuthScheme, _ => { });
        builder.Services.AddAuthorization(Policies.RegisterPolicies);
        builder.WebHost.UseTestServer();

        _authorizationService
            .Setup(service => service.IssueAuthorizationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<List<RiskAcceptanceInput>?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthorizationDecision
            {
                Id = "decision-1",
                RegisteredSystemId = "system-1",
                DecisionType = AuthorizationDecisionType.Ato,
                ExpirationDate = DateTime.UtcNow.AddYears(1),
                IssuedBy = "ao-123",
                IssuedByName = "Alex Official",
            });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapDashboardAuthorizationEndpoints();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task IssueAuthorization_ViewerIsForbidden()
    {
        // Arrange
        SetPrincipal(ComplianceRoles.Viewer, "viewer-123", "Vera Viewer");

        // Act
        var response = await _client.PostAsJsonAsync(Route, CreateRequest());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _authorizationService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IssueAuthorization_AuthorizingOfficialUsesAuthenticatedIdentity()
    {
        // Arrange
        SetPrincipal(ComplianceRoles.AuthorizingOfficial, "ao-123", "Alex Official");

        // Act
        var response = await _client.PostAsJsonAsync(Route, CreateRequest());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _authorizationService.Verify(service => service.IssueAuthorizationAsync(
            "system-1",
            "ATO",
            It.IsAny<DateTime?>(),
            "Medium",
            null,
            "Residual risk accepted",
            null,
            "ao-123",
            "Alex Official",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetPrincipal(string role, string subject, string displayName)
    {
        _client.DefaultRequestHeaders.Remove("X-Test-Role");
        _client.DefaultRequestHeaders.Remove("X-Test-Subject");
        _client.DefaultRequestHeaders.Remove("X-Test-Name");
        _client.DefaultRequestHeaders.Add("X-Test-Role", role);
        _client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        _client.DefaultRequestHeaders.Add("X-Test-Name", displayName);
    }

    private static object CreateRequest() => new
    {
        decisionType = "ATO",
        expirationDate = DateTime.UtcNow.AddYears(1),
        residualRiskLevel = "Medium",
        residualRiskJustification = "Residual risk accepted",
        issuedBy = "forged-user",
        issuedByName = "Forged Official",
    };

    private sealed class RoleHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var subject = Request.Headers["X-Test-Subject"].FirstOrDefault() ?? "test-user";
            var name = Request.Headers["X-Test-Name"].FirstOrDefault() ?? subject;
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, subject),
                    new Claim(ClaimTypes.Name, name),
                    new Claim(ClaimTypes.Role, role),
                ],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Compliance;

public sealed class EmassWorkflowEndpointsTests : IAsyncLifetime
{
    private const string TestAuthScheme = "EmassWorkflowTest";
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });
        var statusService = new Mock<IEmassWorkflowStatusService>();
        statusService.Setup(service => service.GetStatusAsync(
                "system-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmassWorkflowStatus(
                "system-1",
                EmassWorkflowOverallStatus.HasConflicts,
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow,
                2,
                Array.Empty<EmassExportCategorySummary>(),
                new EmassReadinessSummary(true, 0, 1)));

        builder.Services.AddSingleton(statusService.Object);
        builder.Services.AddSingleton(Mock.Of<IEmassExportReadinessService>());
        builder.Services.AddSingleton(Mock.Of<IEmassRoundTripSyncService>());
        builder.Services
            .AddAuthentication(TestAuthScheme)
            .AddScheme<AuthenticationSchemeOptions, RoleHeaderAuthenticationHandler>(TestAuthScheme, _ => { });
        builder.Services.AddAuthorization(Policies.RegisterPolicies);
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapEmassWorkflowEndpoints();
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
    public async Task Status_AuditorReceivesContractEnvelope()
    {
        // Arrange
        SetRole(ComplianceRoles.Auditor);

        // Act
        var response = await _client.GetAsync("/api/systems/system-1/emass/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("systemId").GetString().Should().Be("system-1");
        body.GetProperty("data").GetProperty("unresolvedConflictCount").GetInt32().Should().Be(2);
        body.GetProperty("errors").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ResolveConflict_AuthorizingOfficialIsForbidden()
    {
        // Arrange
        SetRole(ComplianceRoles.AuthorizingOfficial);

        // Act
        var response = await _client.PutAsJsonAsync(
            "/api/systems/system-1/emass/conflicts/conflict-1",
            new ResolveConflictRequest(ConflictStatus.KeepSpin));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private void SetRole(string role)
    {
        _client.DefaultRequestHeaders.Remove("X-Test-Role");
        _client.DefaultRequestHeaders.Add("X-Test-Role", role);
        _client.DefaultRequestHeaders.Remove("X-Test-Subject");
        _client.DefaultRequestHeaders.Add("X-Test-Subject", "test-user");
    }

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
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, subject),
                    new Claim(ClaimTypes.Role, role),
                ],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
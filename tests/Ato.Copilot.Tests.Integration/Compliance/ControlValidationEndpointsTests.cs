using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
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

public sealed class ControlValidationEndpointsTests : IAsyncLifetime
{
    private const string TestAuthScheme = "ControlValidationTest";
    private const string SystemId = "validation-system";
    private const string ControlId = "AC-2";
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        var tenantContext = new TenantContext(TenantId);
        var tenantAccessor = new Mock<ITenantContextAccessor>();
        tenantAccessor.SetupGet(accessor => accessor.Current).Returns(tenantContext);
        var databaseName = $"ControlValidationEndpoints_{Guid.NewGuid():N}";

        builder.Services.AddSingleton<ITenantContextAccessor>(tenantAccessor.Object);
        builder.Services.AddDbContextFactory<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        builder.Services.AddSingleton<IControlValidationLinkService, ControlValidationLinkService>();
        builder.Services.AddSingleton<ITenantContext>(tenantContext);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ICurrentUserService, CurrentUserService>();
        builder.Services
            .AddAuthentication(TestAuthScheme)
            .AddScheme<AuthenticationSchemeOptions, RoleHeaderAuthenticationHandler>(TestAuthScheme, _ => { });
        builder.Services.AddAuthorization(Policies.RegisterPolicies);

        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControlValidationEndpoints();

        await using var scope = _app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        context.RegisteredSystems.Add(new RegisteredSystem
        {
            TenantId = TenantId,
            Id = SystemId,
            Name = "Validation System",
            SystemType = SystemType.MajorApplication,
        });
        context.ControlImplementations.Add(new ControlImplementation
        {
            TenantId = TenantId,
            Id = "control-implementation-ac-2",
            RegisteredSystemId = SystemId,
            ControlId = ControlId,
            ImplementationStatus = ImplementationStatus.Implemented,
            AuthoredBy = "integration-test",
        });
        await context.SaveChangesAsync();

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-Role", ComplianceRoles.Auditor);
        _client.DefaultRequestHeaders.Add("X-Test-Subject", "sca-user-1");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task ManualLink_PostGetDelete_CompletesHttpLifecycle()
    {
        // Arrange
        var route = $"/api/systems/{SystemId}/controls/{ControlId}/validation";
        var request = new
        {
            linkType = "ExternalUrl",
            linkTarget = "https://example.test/evidence/ac-2",
            description = "Assessor validation report",
        };

        // Act
        var createResponse = await _client.PostAsJsonAsync(route, request);

        // Assert
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("linkType").GetString().Should().Be("ExternalUrl");
        created.GetProperty("isAutomated").GetBoolean().Should().BeFalse();
        created.GetProperty("addedBy").GetString().Should().Be("sca-user-1");
        var linkId = created.GetProperty("id").GetString();
        linkId.Should().NotBeNullOrWhiteSpace();

        // Act
        var duplicateResponse = await _client.PostAsJsonAsync(route, request);
        var listResponse = await _client.GetAsync(route);

        // Assert
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        listed.GetProperty("systemId").GetString().Should().Be(SystemId);
        listed.GetProperty("controlId").GetString().Should().Be(ControlId);
        listed.GetProperty("total").GetInt32().Should().Be(1);
        listed.GetProperty("links").GetArrayLength().Should().Be(1);

        // Act
        var deleteResponse = await _client.DeleteAsync($"{route}/{linkId}");
        var emptyResponse = await _client.GetFromJsonAsync<JsonElement>(route);

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        emptyResponse.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ValidationRoutes_ViewerCanReadButCannotMutate()
    {
        // Arrange
        var route = $"/api/dashboard/systems/{SystemId}/controls/{ControlId}/validation";
        _client.DefaultRequestHeaders.Remove("X-Test-Role");
        _client.DefaultRequestHeaders.Add("X-Test-Role", ComplianceRoles.Viewer);

        // Act
        var getResponse = await _client.GetAsync(route);
        var postResponse = await _client.PostAsJsonAsync(route, new
        {
            linkType = "ExternalUrl",
            linkTarget = "https://example.test/reader-cannot-add",
        });
        var deleteResponse = await _client.DeleteAsync($"{route}/missing-link");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
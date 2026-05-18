using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints.Tenancy;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// HTTP-level smoke tests for <see cref="OrgControlOverrideEndpoints"/>
/// (Feature 048 follow-up — user ask #2). Verifies the envelope shape,
/// that GETs are reachable without the admin policy, that PUT round-trips,
/// and that DELETE removes the row.
/// </summary>
public class OrgControlOverrideEndpointsTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";
    private static readonly Guid AdminTenantId = Guid.NewGuid();
    private static readonly Guid AdminUserId = Guid.NewGuid();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        var dbName = $"OrgControlOverride_{Guid.NewGuid():N}";
        builder.Services.AddDbContextFactory<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));

        // Stand up a minimal ITenantContext that mirrors what the real
        // TenantResolutionMiddleware would populate — the endpoint group
        // never reads it directly, but the service implementation does.
        builder.Services.AddScoped<ITenantContext>(_ =>
        {
            var ctx = new Mock<ITenantContext>();
            ctx.SetupGet(t => t.TenantId).Returns(AdminTenantId);
            ctx.SetupGet(t => t.EffectiveTenantId).Returns(AdminTenantId);
            ctx.SetupGet(t => t.IsCspAdmin).Returns(false);
            return ctx.Object;
        });
        builder.Services.AddSingleton<ILogger<OrgControlOverrideService>>(
            NullLogger<OrgControlOverrideService>.Instance);
        builder.Services.AddScoped<IOrgControlOverrideService, OrgControlOverrideService>();

        builder.Services.AddAuthentication(AuthScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(AuthScheme, _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(
                OnboardingAdministratorRequirement.PolicyName,
                p => p.RequireAssertion(_ => true));
        });

        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapOrgControlOverrideEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-User", $"{AdminTenantId}|{AdminUserId}|alice@org");
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _client.Dispose();
    }

    [Fact]
    public async Task List_NoRows_ReturnsEmptyEnvelope()
    {
        // Act
        var resp = await _client.GetAsync("/api/orgs/control-overrides");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Put_Then_Get_RoundTripsRow()
    {
        // Arrange
        var payload = new
        {
            implementationStatus = "PartiallyImplemented",
            inheritanceApplicability = "Hybrid",
            justification = "Local SIEM ingests CSP events but org adds privileged-access review.",
        };

        // Act — PUT
        var put = await _client.PutAsJsonAsync("/api/orgs/control-overrides/AC-2", payload);
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var putBody = await put.Content.ReadFromJsonAsync<JsonElement>(Json);
        putBody.GetProperty("ok").GetBoolean().Should().BeTrue();
        putBody.GetProperty("data").GetProperty("controlId").GetString().Should().Be("AC-2");

        // Act — GET single
        var get = await _client.GetAsync("/api/orgs/control-overrides/AC-2");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var getBody = await get.Content.ReadFromJsonAsync<JsonElement>(Json);
        getBody.GetProperty("data").GetProperty("implementationStatus").GetString()
            .Should().Be("PartiallyImplemented");
        getBody.GetProperty("data").GetProperty("inheritanceApplicability").GetString()
            .Should().Be("Hybrid");
        getBody.GetProperty("data").GetProperty("justification").GetString()
            .Should().Contain("SIEM");

        // Act — list
        var list = await _client.GetAsync("/api/orgs/control-overrides");
        var listBody = await list.Content.ReadFromJsonAsync<JsonElement>(Json);
        listBody.GetProperty("data").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Get_MissingControl_Returns404()
    {
        // Act
        var resp = await _client.GetAsync("/api/orgs/control-overrides/ZZ-99");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("ok").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Put_BothFieldsNull_DeletesAndReturnsNullData()
    {
        // Arrange — seed a row, then PUT with both fields null.
        var seed = new
        {
            implementationStatus = "Implemented",
            inheritanceApplicability = (string?)null,
            justification = "stable",
        };
        (await _client.PutAsJsonAsync("/api/orgs/control-overrides/SC-7", seed))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Act — clear both override fields.
        var clear = new
        {
            implementationStatus = (string?)null,
            inheritanceApplicability = (string?)null,
            justification = (string?)null,
        };
        var put = await _client.PutAsJsonAsync("/api/orgs/control-overrides/SC-7", clear);

        // Assert
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await put.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        body.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);

        var get = await _client.GetAsync("/api/orgs/control-overrides/SC-7");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_OverrideWithoutJustification_Returns400()
    {
        // Arrange
        var payload = new
        {
            implementationStatus = "Planned",
            inheritanceApplicability = (string?)null,
            justification = "",
        };

        // Act
        var resp = await _client.PutAsJsonAsync("/api/orgs/control-overrides/AU-2", payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("ok").GetBoolean().Should().BeFalse();
        body.GetProperty("errorCode").GetString().Should().Be("ValidationFailed");
    }

    [Fact]
    public async Task Put_BadEnumValue_Returns400()
    {
        // Arrange
        var payload = new
        {
            implementationStatus = "BogusEnumValue",
            inheritanceApplicability = (string?)null,
            justification = "j",
        };

        // Act
        var resp = await _client.PutAsJsonAsync("/api/orgs/control-overrides/AC-2", payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("errorCode").GetString().Should().Be("ValidationFailed");
        body.GetProperty("message").GetString().Should().Contain("ImplementationStatus");
    }

    [Fact]
    public async Task Delete_ExistingRow_Returns200()
    {
        // Arrange
        var seed = new
        {
            implementationStatus = "Implemented",
            inheritanceApplicability = (string?)null,
            justification = "stable",
        };
        (await _client.PutAsJsonAsync("/api/orgs/control-overrides/AC-2", seed))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var del = await _client.DeleteAsync("/api/orgs/control-overrides/AC-2");

        // Assert
        del.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await del.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        body.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Delete_MissingRow_Returns404()
    {
        // Act
        var del = await _client.DeleteAsync("/api/orgs/control-overrides/ZZ-99");

        // Assert
        del.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Test handler — emits <c>tid</c>, <c>oid</c>, and <c>upn</c> claims
    /// from a pipe-separated <c>X-Test-User: tenantId|userId|upn</c> header.
    /// </summary>
    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var header))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var parts = header.ToString().Split('|', 3);
            var claims = new List<Claim>
            {
                new("tid", parts.Length > 0 ? parts[0] : string.Empty),
                new("oid", parts.Length > 1 ? parts[1] : string.Empty),
                new("upn", parts.Length > 2 ? parts[2] : "test-user"),
                new(ClaimTypes.Name, "test-user"),
            };
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(claims, AuthScheme)),
                    AuthScheme)));
        }
    }
}

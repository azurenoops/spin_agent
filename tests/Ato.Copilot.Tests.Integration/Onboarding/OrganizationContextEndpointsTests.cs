using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
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
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// Integration tests for <see cref="OrganizationContextEndpoints"/> (T049 / FR-010..FR-014).
/// Uses TestServer with a stub authentication handler that emits the tenant and oid
/// claims required by the wizard authorization filter.
/// </summary>
public class OrganizationContextEndpointsTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";
    private static readonly Guid AdminTenantId = Guid.NewGuid();
    private static readonly Guid AdminUserId = Guid.NewGuid();
    private static readonly Guid OutsiderTenantId = Guid.NewGuid();
    private static readonly Guid OutsiderUserId = Guid.NewGuid();
    private static readonly Guid ForbiddenUserId = Guid.NewGuid();

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Mock<IWizardAuditService> _auditMock = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        var dbName = $"OrgContextEndpoints_{Guid.NewGuid():N}";
        builder.Services.AddDbContextFactory<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddScoped<IWizardAuditService>(_ => _auditMock.Object);
        builder.Services.AddScoped<IOrganizationContextService, OrganizationContextService>();

        builder.Services.AddSingleton<ILogger<OrganizationContextService>>(NullLogger<OrganizationContextService>.Instance);

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
        _app.MapOrganizationContextEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _client.Dispose();
    }

    private void AuthenticateAsAdmin()
    {
        _client.DefaultRequestHeaders.Remove("X-Test-User");
        _client.DefaultRequestHeaders.Add("X-Test-User", $"{AdminTenantId}|{AdminUserId}");
    }

    private void AuthenticateAsForbidden()
    {
        // Send a user with no tenant claim to exercise the 403 branch.
        _client.DefaultRequestHeaders.Remove("X-Test-User");
        _client.DefaultRequestHeaders.Add("X-Test-User", $"|{ForbiddenUserId}");
    }

    [Fact]
    public async Task Get_NoRow_ReturnsOkWithNullData()
    {
        AuthenticateAsAdmin();
        var response = await _client.GetAsync("/api/onboarding/organization-context");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        body.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Put_HappyPath_PersistsAndReturnsValue()
    {
        AuthenticateAsAdmin();
        var payload = new
        {
            organizationName = "Test Agency",
            branch = "CivilAgency",
            subOrganization = "Bureau of Compliance",
        };
        var response = await _client.PutAsJsonAsync(
            "/api/onboarding/organization-context", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("organizationName").GetString().Should().Be("Test Agency");
        body.GetProperty("data").GetProperty("branch").GetString().Should().Be("CivilAgency");

        // Confirm row in DB.
        using var scope = _app.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.OrganizationContexts.FirstOrDefaultAsync(
            c => c.TenantId == AdminTenantId);
        row.Should().NotBeNull();
        row!.OrganizationName.Should().Be("Test Agency");

        _auditMock.Verify(a => a.RecordAsync(
            AdminTenantId,
            AdminUserId,
            WizardAuditAction.OrganizationContextSaved,
            nameof(OrganizationContext),
            It.IsAny<Guid?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Put_IndustryPartnerOtherWithoutQualifier_Returns400()
    {
        AuthenticateAsAdmin();
        var payload = new
        {
            organizationName = "ACME",
            branch = "IndustryPartnerOther",
        };
        var response = await _client.PutAsJsonAsync(
            "/api/onboarding/organization-context", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("ok").GetBoolean().Should().BeFalse();
        body.GetProperty("message").GetString().Should().Contain("qualifier");
    }

    [Fact]
    public async Task Put_NoTenantClaim_Returns403()
    {
        AuthenticateAsForbidden();
        var payload = new { organizationName = "Anything", branch = "CivilAgency" };
        var response = await _client.PutAsJsonAsync(
            "/api/onboarding/organization-context", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("ok").GetBoolean().Should().BeFalse();
        body.GetProperty("errorCode").GetString().Should().Be("WIZARD_AUTH_FORBIDDEN");
    }

    /// <summary>
    /// Test authentication handler — emits a <see cref="ClaimsPrincipal"/> from the
    /// pipe-separated <c>X-Test-User</c> header (<c>tenantId|userId</c>).
    /// </summary>
    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var header))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var parts = header.ToString().Split('|', 2);
            var tenantId = parts.Length > 0 ? parts[0] : string.Empty;
            var userId = parts.Length > 1 ? parts[1] : string.Empty;

            var claims = new List<Claim>();
            if (!string.IsNullOrEmpty(tenantId))
            {
                claims.Add(new Claim("tid", tenantId));
            }
            if (!string.IsNullOrEmpty(userId))
            {
                claims.Add(new Claim("oid", userId));
            }
            claims.Add(new Claim(ClaimTypes.Name, "test-user"));
            var identity = new ClaimsIdentity(claims, AuthScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, AuthScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.AzureSubscriptions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// Integration tests for Azure subscription endpoints (T096 / FR-070..FR-077).
/// </summary>
public class AzureSubscriptionEndpointsTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";
    private static readonly Guid AdminTenantId = Guid.NewGuid();
    private static readonly Guid AdminUserId = Guid.NewGuid();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Mock<IDelegatedArmTokenProvider> _tokens = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        var dbName = $"AzSubEndpoints_{Guid.NewGuid():N}";
        builder.Services.AddDbContextFactory<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddSingleton(_tokens.Object);
        builder.Services.AddSingleton(new Mock<IWizardAuditService>().Object);
        builder.Services.Configure<OnboardingOptions>(_ => { });
        builder.Services.AddScoped<IAzureSubscriptionEnumerationService, AzureSubscriptionEnumerationService>();
        builder.Services.AddScoped<IAzureSubscriptionRegistrationService, AzureSubscriptionRegistrationService>();
        builder.Services.AddSingleton<ILogger<AzureSubscriptionEnumerationService>>(NullLogger<AzureSubscriptionEnumerationService>.Instance);
        builder.Services.AddSingleton<ILogger<AzureSubscriptionRegistrationService>>(NullLogger<AzureSubscriptionRegistrationService>.Instance);

        builder.Services.AddAuthentication(AuthScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(AuthScheme, _ => { });
        builder.Services.AddAuthorization(o =>
            o.AddPolicy(OnboardingAdministratorRequirement.PolicyName, p => p.RequireAssertion(_ => true)));

        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapAzureSubscriptionEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-User", $"{AdminTenantId}|{AdminUserId}");
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _client.Dispose();
    }

    [Fact]
    public async Task Get_NoConsent_Returns403WithInsufficientClaimsHeader()
    {
        _tokens.Setup(t => t.GetCredentialAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TokenCredential?)null);

        var response = await _client.GetAsync("/api/onboarding/azure/subscriptions");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.WwwAuthenticate.Should().NotBeEmpty();
        response.Headers.WwwAuthenticate.ToString().Should().Contain("insufficient_claims");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("errorCode").GetString().Should().Be("WIZARD_ARM_CONSENT_REQUIRED");
    }

    [Fact]
    public async Task Registrations_Get_ReturnsEmpty()
    {
        var response = await _client.GetAsync("/api/onboarding/azure/subscriptions/registrations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Registrations_PutWithoutSubscriptions_ReturnsErrorEnvelope()
    {
        _tokens.Setup(t => t.GetCredentialAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TokenCredential?)null);

        var response = await _client.PutAsJsonAsync(
            "/api/onboarding/azure/subscriptions/registrations",
            new { subscriptionIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("ok").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ScopeResolver_ReturnsOnlySelectedSubscriptions()
    {
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var selectedSubId = Guid.NewGuid();
            var unavailableSubId = Guid.NewGuid();
            db.AzureSubscriptionRegistrations.AddRange(
                new Ato.Copilot.Core.Models.Onboarding.AzureSubscriptionRegistration
                {
                    Id = Guid.NewGuid(), TenantId = AdminTenantId,
                    SubscriptionId = selectedSubId, DisplayName = "Sel",
                    Status = Ato.Copilot.Core.Models.Onboarding.SubscriptionStatus.Selected,
                },
                new Ato.Copilot.Core.Models.Onboarding.AzureSubscriptionRegistration
                {
                    Id = Guid.NewGuid(), TenantId = AdminTenantId,
                    SubscriptionId = unavailableSubId, DisplayName = "Stale",
                    Status = Ato.Copilot.Core.Models.Onboarding.SubscriptionStatus.Unavailable,
                });
            await db.SaveChangesAsync();
            var resolver = new AzureSubscriptionScopeResolver(factory);
            var ids = await resolver.GetSelectedSubscriptionIdsAsync(AdminTenantId);
            ids.Should().ContainSingle().Which.Should().Be(selectedSubId);
        }
    }

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
                return Task.FromResult(AuthenticateResult.NoResult());
            var parts = header.ToString().Split('|', 2);
            var claims = new List<Claim>();
            if (!string.IsNullOrEmpty(parts[0])) claims.Add(new Claim("tid", parts[0]));
            if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1])) claims.Add(new Claim("oid", parts[1]));
            claims.Add(new Claim(ClaimTypes.Name, "test-user"));
            var identity = new ClaimsIdentity(claims, AuthScheme);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), AuthScheme)));
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T156 [US7]: Validates the CSP-onboarding **gate** (FR-090) — in MultiTenant
/// mode with no active <c>CspProfile</c>, only <c>/api/csp/onboarding/*</c>,
/// <c>/api/auth/*</c>, and <c>/health</c> are reachable for CSP.Admin;
/// everything else returns <c>503 CSP_ONBOARDING_INCOMPLETE</c>.
/// </summary>
/// <remarks>
/// RED until T164 (TenantResolutionMiddleware adds the CSP-onboarding gate).
/// Acceptance scenarios 1 and 2 from spec.md US7.
/// </remarks>
[Collection("Tenancy")]
public class CspOnboardingGateTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspOnboardingGateTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        // Wipe any CspProfile row left over from prior tests in the shared
        // `[Collection("Tenancy")]` (e.g. CspOnboardingContractTests's
        // submit-success path). Without this reset the gate's cache-aside
        // would see an Active singleton and let traffic through, masking
        // the gate behavior under test.
        factory.ResetCspProfileAsync().GetAwaiter().GetResult();

        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.Status = TenantStatus.Active;
    }

    [Theory]
    [InlineData("/api/csp/onboarding/state")]
    [InlineData("/health")]
    public async Task AllowedPath_ReachableEvenWithoutCspProfile(string path)
    {
        // Arrange — fresh fixture: no CspProfile row exists.

        // Act
        var resp = await _client.GetAsync(path);

        // Assert
        ((int)resp.StatusCode).Should().BeLessThan(500,
            "the gate must not 503 the onboarding wizard or health-check");
        resp.StatusCode.Should().NotBe(HttpStatusCode.ServiceUnavailable,
            $"{path} is in the gate's allow-list per FR-090");
    }

    [Theory]
    [InlineData("/api/tenants")]
    [InlineData("/api/dashboard/systems")]
    [InlineData("/api/onboarding/tenant/state")]
    public async Task BlockedPath_AsCspAdmin_Returns503_CspOnboardingIncomplete(string path)
    {
        // Arrange — fresh fixture, IsCspAdmin = true, no CspProfile.

        // Act
        var resp = await _client.GetAsync(path);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("CSP_ONBOARDING_INCOMPLETE");
    }

    [Fact]
    public async Task BlockedPath_AsNonCspAdmin_Returns503_CspOnboardingIncomplete()
    {
        // Arrange
        _factory.GetActiveContext().IsCspAdmin = false;

        // Act
        var resp = await _client.GetAsync("/api/dashboard/systems");

        // Assert — non-CSP-Admin users also get 503 in MultiTenant mode
        // until the CSP profile is Active (FR-090).
        resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("CSP_ONBOARDING_INCOMPLETE");
    }

    [Fact]
    public async Task AfterCspOnboardingComplete_GatesLift_TenantsEndpointReachable()
    {
        // Arrange — walk through the wizard
        await _client.PostAsJsonAsync("/api/csp/onboarding/identity", new
        {
            legalEntityName = "Gate-Test Hosting LLC",
            displayName = "Gate Test CSP",
        });
        await _client.PostAsJsonAsync("/api/csp/onboarding/support", new
        {
            primarySupportEmail = "support@example.us",
        });
        await _client.PostAsJsonAsync("/api/csp/onboarding/classification", new
        {
            defaultClassificationFloor = "Unclassified",
        });
        await _client.PostAsync("/api/csp/onboarding/submit", content: null);

        // Act
        var resp = await _client.GetAsync("/api/tenants");

        // Assert — CSP-Admin can now read /api/tenants because the gate has lifted
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "the gate must lift once CspProfile.OnboardingState = Active");
    }
}

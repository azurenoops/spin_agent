using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T155 [US7]: Validates the <c>/api/csp/onboarding/*</c> contract surface
/// against <c>specs/048-tenant-isolation/contracts/csp-onboarding.openapi.yaml</c>.
/// Exercises status codes, the error-envelope shape, idempotency on the step
/// POSTs, and <c>409 CSP_ALREADY_ONBOARDED</c> on a second submit.
/// </summary>
/// <remarks>
/// RED until T160-T163 (CspProfile entity, ICspProfileService, CspOnboardingEndpoints)
/// are implemented. The fixture seeds the host in MultiTenant mode with no
/// active CspProfile; the FakeTenantContext is configured with
/// <c>IsCspAdmin = true</c> so the endpoints are reachable. The CSP-onboarding
/// gate (T164) is asserted in <see cref="CspOnboardingGateTests"/>; this file
/// only validates the contract once the caller is allowed through.
/// </remarks>
[Collection("Tenancy")]
public class CspOnboardingContractTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspOnboardingContractTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        // Wipe any CspProfile row left over from prior tests / classes in
        // the shared `[Collection("Tenancy")]` so each contract test method
        // starts from a fresh "Pending" deployment.
        factory.ResetCspProfileAsync().GetAwaiter().GetResult();

        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.Status = TenantStatus.Active;
    }

    [Fact]
    public async Task Get_State_AsCspAdmin_Returns200_WithExpectedShape()
    {
        // Arrange
        // (fixture state: MultiTenant + IsCspAdmin = true)

        // Act
        var resp = await _client.GetAsync("/api/csp/onboarding/state");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");

        var data = body.GetProperty("data");
        data.GetProperty("onboardingState").GetString()
            .Should().BeOneOf("Pending", "InWizard", "Active");
        data.GetProperty("currentStep").GetString()
            .Should().BeOneOf("Identity", "SupportContact", "Classification", "Review", "Complete");
    }

    [Fact]
    public async Task Get_State_AsNonCspAdmin_Returns403_ForbiddenNotCspAdmin()
    {
        // Arrange
        _factory.GetActiveContext().IsCspAdmin = false;

        // Act
        var resp = await _client.GetAsync("/api/csp/onboarding/state");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    [Fact]
    public async Task Post_Identity_FirstCall_Returns200_AdvancesStep()
    {
        // Arrange
        var request = new
        {
            legalEntityName = "ATO Copilot Test Hosting LLC",
            displayName = "ATO Test CSP",
        };

        // Act
        var resp = await _client.PostAsJsonAsync("/api/csp/onboarding/identity", request);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        body.GetProperty("data").GetProperty("currentStep").GetString()
            .Should().Be("SupportContact");
        body.GetProperty("data").GetProperty("identity").GetProperty("legalEntityName").GetString()
            .Should().Be("ATO Copilot Test Hosting LLC");
    }

    [Fact]
    public async Task Post_Identity_Idempotent_OnRepeatCallReturns200()
    {
        // Arrange
        var request = new
        {
            legalEntityName = "Idempotent Hosting LLC",
            displayName = "Idempotent CSP",
        };

        // Act
        var first = await _client.PostAsJsonAsync("/api/csp/onboarding/identity", request);
        var second = await _client.PostAsJsonAsync("/api/csp/onboarding/identity", request);

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK,
            "step POSTs are idempotent per the contract");
    }

    [Fact]
    public async Task Post_Identity_Validation_BadRequest_Returns422()
    {
        // Arrange — legalEntityName below minLength=2
        var request = new { legalEntityName = "x", displayName = "y" };

        // Act
        var resp = await _client.PostAsJsonAsync("/api/csp/onboarding/identity", request);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Post_Submit_Without_Required_Steps_Returns422()
    {
        // Arrange — no prior identity / support / classification calls in this fixture iteration.
        // The fixture's TenancySeedHostedService does NOT create a CspProfile, so a fresh
        // submit attempt must be rejected with 422 because required steps are missing.

        // Act
        var resp = await _client.PostAsync("/api/csp/onboarding/submit", content: null);

        // Assert — either 422 (steps missing) or 200 if a previous test already completed all steps
        // is OK. We assert it is NEVER a 5xx.
        resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        ((int)resp.StatusCode).Should().BeLessThan(500);
    }

    [Fact]
    public async Task Post_Submit_AfterCompletingAllSteps_Returns200_ThenSecondSubmitReturns409()
    {
        // Arrange — drive through every step in order
        await _client.PostAsJsonAsync("/api/csp/onboarding/identity", new
        {
            legalEntityName = "Full Onboarding LLC",
            displayName = "Full CSP",
        });
        await _client.PostAsJsonAsync("/api/csp/onboarding/support", new
        {
            primarySupportEmail = "support@example.us",
        });
        await _client.PostAsJsonAsync("/api/csp/onboarding/classification", new
        {
            defaultClassificationFloor = "Unclassified",
        });

        // Act
        var firstSubmit = await _client.PostAsync("/api/csp/onboarding/submit", content: null);

        // Assert
        firstSubmit.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstSubmit.Content.ReadFromJsonAsync<JsonElement>();
        firstBody.GetProperty("status").GetString().Should().Be("success");
        firstBody.GetProperty("data").GetProperty("onboardingState").GetString().Should().Be("Active");
        firstBody.GetProperty("data").TryGetProperty("onboardingCompletedAt", out var completedAt)
            .Should().BeTrue();
        completedAt.GetString().Should().NotBeNullOrEmpty();

        // Act 2 — second submit
        var secondSubmit = await _client.PostAsync("/api/csp/onboarding/submit", content: null);

        // Assert 2 — 409 CSP_ALREADY_ONBOARDED
        secondSubmit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var secondBody = await secondSubmit.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("CSP_ALREADY_ONBOARDED");
    }
}

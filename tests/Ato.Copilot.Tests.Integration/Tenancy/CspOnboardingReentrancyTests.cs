using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T158 [US7]: Validates wizard re-entrancy — closing the browser mid-wizard
/// must allow the next session to resume at the last incomplete step, and
/// re-submitting completed steps must NOT duplicate state. Acceptance scenario
/// 6 from spec.md US7.
/// </summary>
/// <remarks>
/// RED until T162 (CspProfileService step-machine semantics) and T163
/// (CspOnboardingEndpoints) are implemented.
/// </remarks>
[Collection("Tenancy")]
public class CspOnboardingReentrancyTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspOnboardingReentrancyTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        // Wipe any CspProfile row left over from prior tests in the shared
        // `[Collection("Tenancy")]` so each re-entrancy test method starts
        // from a fresh "Pending" deployment.
        factory.ResetCspProfileAsync().GetAwaiter().GetResult();

        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.Status = TenantStatus.Active;
    }

    [Fact]
    public async Task GetState_AfterPartialProgress_ReportsLastIncompleteStep()
    {
        // Arrange — complete only steps 1 and 2; do NOT post classification or submit.
        await _client.PostAsJsonAsync("/api/csp/onboarding/identity", new
        {
            legalEntityName = "Resume Test LLC",
            displayName = "Resume Test",
        });
        await _client.PostAsJsonAsync("/api/csp/onboarding/support", new
        {
            primarySupportEmail = "support@resume.us",
        });

        // Act — simulate browser-close by spinning a fresh client off the same fixture.
        using var freshClient = _factory.CreateClient();
        var resp = await freshClient.GetAsync("/api/csp/onboarding/state");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("currentStep").GetString()
            .Should().Be("Classification",
                "the wizard must resume at the next incomplete step (after SupportContact)");
        body.GetProperty("data").GetProperty("onboardingState").GetString()
            .Should().Be("InWizard");

        // Identity + support should still be persisted
        body.GetProperty("data").GetProperty("identity").GetProperty("legalEntityName").GetString()
            .Should().Be("Resume Test LLC");
        body.GetProperty("data").GetProperty("supportContact").GetProperty("primarySupportEmail").GetString()
            .Should().Be("support@resume.us");
    }

    [Fact]
    public async Task ResubmittingCompletedStep_DoesNotDuplicateState_NorAdvancePastFurtherSteps()
    {
        // Arrange — drive past identity step.
        await _client.PostAsJsonAsync("/api/csp/onboarding/identity", new
        {
            legalEntityName = "Idempotent CSP LLC",
            displayName = "Idempotent",
        });

        // Act — re-submit the SAME identity step
        var resp = await _client.PostAsJsonAsync("/api/csp/onboarding/identity", new
        {
            legalEntityName = "Idempotent CSP LLC",
            displayName = "Idempotent",
        });

        // Assert — second call returns 200 and currentStep is still SupportContact
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("currentStep").GetString()
            .Should().Be("SupportContact",
                "re-posting identity must not advance the cursor past the next incomplete step");

        // Verify exactly one CspProfile row exists.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var count = await db.Set<CspProfile>().IgnoreQueryFilters().CountAsync();
        count.Should().Be(1, "the wizard creates a singleton; repeated identity POST must NOT create duplicates");
    }
}

internal static class CspOnboardingReentrancyTestServiceExtensions
{
    public static T GetRequiredService<T>(this IServiceProvider services) where T : notnull
        => Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<T>(services);
}

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
/// T089 [US4]: Verifies the tenant-onboarding wizard is re-entrant — the
/// admin can submit a few steps, walk away, and come back later to a fresh
/// HTTP client / request scope and see exactly the steps they had completed.
/// </summary>
[Collection("Tenancy")]
public class WizardReentrancyTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;

    public WizardReentrancyTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ResumeAfterDisconnect_ReportsCorrectCurrentStepAndCompletedList()
    {
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        await ResetTenantAsync(tenantId);

        var ctx = _factory.GetActiveContext();
        ctx.TenantId = tenantId;
        ctx.IsCspAdmin = false;
        ctx.Status = TenantStatus.Active;

        // First "session" — submit Steps 1 and 2 then drop the client.
        using (var first = _factory.CreateClient())
        {
            (await first.PostAsJsonAsync("/api/onboarding/tenant/legal-entity", new
            {
                legalEntityName = "Resume Co",
                doDComponent = "Navy",
                timeZone = "UTC",
            })).StatusCode.Should().Be(HttpStatusCode.OK);

            (await first.PostAsJsonAsync("/api/onboarding/tenant/hq-address", new
            {
                hqAddressLine1 = "10 Nautical Way",
                hqCity = "San Diego",
                hqStateOrProvince = "CA",
                hqPostalCode = "92101",
                hqCountry = "USA",
            })).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Second "session" — fresh client, GET /state.
        using var second = _factory.CreateClient();
        var resp = await second.GetAsync("/api/onboarding/tenant/state");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var data = body.GetProperty("data");

        data.GetProperty("currentStep").GetString().Should().Be("Tenant.Classification");
        data.GetProperty("onboardingState").GetString().Should().Be("InWizard");

        var completed = data.GetProperty("completedSteps").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        completed.Should().BeEquivalentTo(new[]
        {
            "Tenant.LegalEntity",
            "Tenant.HqAddress",
        });
    }

    private async Task ResetTenantAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantId);
        tenant.OnboardingState = OnboardingState.Pending;
        tenant.LegalEntityName = null;
        tenant.HqAddressLine1 = null;
        tenant.HqCity = null;
        tenant.HqStateOrProvince = null;
        tenant.HqPostalCode = null;
        tenant.HqCountry = null;

        var existingAudits = await db.AuditLogs
            .Where(a => a.TenantId == tenantId && a.Action.StartsWith("TenantOnboarding."))
            .ToListAsync();
        db.AuditLogs.RemoveRange(existingAudits);

        var existingOrgs = await db.Organizations.Where(o => o.TenantId == tenantId).ToListAsync();
        db.Organizations.RemoveRange(existingOrgs);
        await db.SaveChangesAsync();
    }
}

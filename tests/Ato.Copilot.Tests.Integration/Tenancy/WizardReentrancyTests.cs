using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T089 [US4]: Verifies the tenant-onboarding wizard is re-entrant — the
/// admin can submit a few steps, walk away, and come back later to a fresh
/// HTTP client / request scope and see exactly the steps they had completed.
/// </summary>
[Collection("Tenancy")]
public class WizardReentrancyTests : IClassFixture<WorkspaceMembershipFactory>
{
    private readonly WorkspaceMembershipFactory _factory;

    public WizardReentrancyTests(WorkspaceMembershipFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ResumeAfterDisconnect_ReportsCorrectCurrentStepAndCompletedList()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var directoryId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        await SeedAdministratorAsync(tenantId, directoryId, objectId);

        // Act
        // First "session" — submit Steps 1 and 2 then drop the client.
        using (var first = CreateAdministratorSession(tenantId, directoryId, objectId))
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
        using var second = CreateAdministratorSession(tenantId, directoryId, objectId);
        var resp = await second.GetAsync("/api/onboarding/tenant/state");

        // Assert
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

    private HttpClient CreateAdministratorSession(Guid tenantId, Guid directoryId, Guid objectId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", directoryId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", objectId.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", tenantId.ToString());
        return client;
    }

    private async Task SeedAdministratorAsync(Guid tenantId, Guid directoryId, Guid objectId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = $"Reentry fixture {tenantId:N}" });
        var person = new Person
        {
            TenantId = tenantId, DisplayName = "Reentry Administrator", Email = $"{tenantId:N}@example.invalid"
        };
        db.Persons.Add(person);
        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            TenantId = tenantId, PersonId = person.Id, DirectoryTenantId = directoryId,
            ObjectId = objectId, GrantedBy = "test"
        });
        db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
        {
            TenantId = tenantId, PersonId = person.Id, Role = OrganizationRole.Administrator,
            CreatedBy = objectId, UpdatedBy = objectId
        });
        await db.SaveChangesAsync();
    }
}

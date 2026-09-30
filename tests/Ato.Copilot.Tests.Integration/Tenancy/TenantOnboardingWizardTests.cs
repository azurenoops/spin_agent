using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T088 [US4]: Drives the Tenant-and-Organization onboarding wizard
/// (Feature 048 / FR-054) end-to-end against the HTTP surface defined in
/// <c>specs/048-tenant-isolation/contracts/tenant-onboarding.openapi.yaml</c>.
/// </summary>
/// <remarks>
/// Uses the real workspace resolution pipeline with an explicit local Person,
/// active membership and Administrator assignment, not fixture context bypass.
/// </remarks>
[Collection("Tenancy")]
public class TenantOnboardingWizardTests : IClassFixture<WorkspaceMembershipFactory>
{
    private readonly WorkspaceMembershipFactory _factory;
    private readonly HttpClient _client;

    public TenantOnboardingWizardTests(WorkspaceMembershipFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Wizard_HappyPath_ProducesActiveTenantAndFirstOrganization()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var directoryId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        await using (var seed = _factory.Services.CreateAsyncScope())
        {
            var database = seed.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            database.Tenants.Add(new Tenant { Id = tenantId, DisplayName = $"Activation fixture {tenantId:N}" });
            var person = new Person { TenantId = tenantId, DisplayName = "Activation Administrator", Email = $"{tenantId:N}@example.invalid" };
            database.Persons.Add(person);
            database.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = tenantId, PersonId = person.Id, DirectoryTenantId = directoryId, ObjectId = objectId, GrantedBy = "test"
            });
            database.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                TenantId = tenantId, PersonId = person.Id, Role = OrganizationRole.Administrator, CreatedBy = objectId, UpdatedBy = objectId
            });
            await database.SaveChangesAsync();
        }
        await ResetTenantAsync(tenantId);
        _client.DefaultRequestHeaders.Add("X-Test-Tid", directoryId.ToString());
        _client.DefaultRequestHeaders.Add("X-Test-Oid", objectId.ToString());
        _client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        _client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", tenantId.ToString());

        // Act
        // Step 1 — legal entity.
        var legal = await _client.PostAsJsonAsync("/api/onboarding/tenant/legal-entity",
            new { legalEntityName = "Acme Defense LLC", doDComponent = "Army", timeZone = "America/New_York" });
        legal.StatusCode.Should().Be(HttpStatusCode.OK);
        var legalState = await ReadProgressAsync(legal);
        legalState.GetProperty("currentStep").GetString().Should().Be("Tenant.HqAddress");
        legalState.GetProperty("onboardingState").GetString().Should().Be("InWizard");

        // Step 2 — HQ address.
        var hq = await _client.PostAsJsonAsync("/api/onboarding/tenant/hq-address", new
        {
            hqAddressLine1 = "100 Main St",
            hqAddressLine2 = (string?)null,
            hqCity = "Arlington",
            hqStateOrProvince = "VA",
            hqPostalCode = "22202",
            hqCountry = "USA",
        });
        hq.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadProgressAsync(hq)).GetProperty("currentStep").GetString()
            .Should().Be("Tenant.Classification");

        // Step 3 — classification level.
        var clf = await _client.PostAsJsonAsync("/api/onboarding/tenant/classification",
            new { defaultClassificationLevel = "Cui" });
        clf.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadProgressAsync(clf)).GetProperty("currentStep").GetString()
            .Should().Be("Tenant.Ao");

        // Step 4 — Authorizing Official.
        var ao = await _client.PostAsJsonAsync("/api/onboarding/tenant/ao",
            new { authorizingOfficialName = "Jane AO", authorizingOfficialEmail = "jane.ao@example.mil" });
        ao.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadProgressAsync(ao)).GetProperty("currentStep").GetString()
            .Should().Be("Tenant.PrimaryPoc");

        // Step 5 — primary POC.
        var poc = await _client.PostAsJsonAsync("/api/onboarding/tenant/primary-poc", new
        {
            primaryPocName = "John POC",
            primaryPocEmail = "john.poc@example.mil",
            primaryPocPhone = "+1-555-555-0100",
        });
        poc.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadProgressAsync(poc)).GetProperty("currentStep").GetString()
            .Should().Be("Org.Profile");

        // Step 6 — first organization profile.
        var org = await _client.PostAsJsonAsync("/api/onboarding/tenant/org-profile",
            new { name = "Acme Cyber Division", description = "First org for tenant" });
        org.StatusCode.Should().Be(HttpStatusCode.OK);
        var orgState = await ReadProgressAsync(org);
        orgState.GetProperty("firstOrganizationId").GetString().Should().NotBeNullOrEmpty();

        // Final submission.
        var submit = await _client.PostAsJsonAsync("/api/onboarding/tenant/submit", new { });
        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalState = await ReadProgressAsync(submit);
        finalState.GetProperty("currentStep").GetString().Should().Be("Submitted");
        finalState.GetProperty("onboardingState").GetString().Should().Be("Active");

        // Assert
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantId);
        tenant.LegalEntityName.Should().Be("Acme Defense LLC");
        tenant.HqCity.Should().Be("Arlington");
        tenant.DefaultClassificationLevel.Should().Be(ClassificationLevel.CUI);
        tenant.AuthorizingOfficialEmail.Should().Be("jane.ao@example.mil");
        tenant.PrimaryPocName.Should().Be("John POC");
        tenant.OnboardingState.Should().Be(OnboardingState.Active);

        var orgs = await db.Organizations.Where(o => o.TenantId == tenantId).ToListAsync();
        orgs.Should().HaveCount(1);
        orgs[0].Name.Should().Be("Acme Cyber Division");

        // FR-056: every step submission emits an audit row.
        var actions = await db.AuditLogs
            .Where(a => a.TenantId == tenantId && a.Action.StartsWith("TenantOnboarding."))
            .Select(a => a.Action)
            .Distinct()
            .ToListAsync();
        actions.Should().Contain(new[]
        {
            "TenantOnboarding.Tenant.LegalEntity",
            "TenantOnboarding.Tenant.HqAddress",
            "TenantOnboarding.Tenant.Classification",
            "TenantOnboarding.Tenant.Ao",
            "TenantOnboarding.Tenant.PrimaryPoc",
            "TenantOnboarding.Org.Profile",
            "TenantOnboarding.Submitted",
        });
    }

    private async Task ResetTenantAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var tenant = await db.Tenants.FirstAsync(t => t.Id == tenantId);
        tenant.OnboardingState = OnboardingState.Pending;
        tenant.LegalEntityName = null;
        tenant.DoDComponent = null;
        tenant.HqAddressLine1 = null;
        tenant.HqAddressLine2 = null;
        tenant.HqCity = null;
        tenant.HqStateOrProvince = null;
        tenant.HqPostalCode = null;
        tenant.HqCountry = null;
        tenant.AuthorizingOfficialName = null;
        tenant.AuthorizingOfficialEmail = null;
        tenant.PrimaryPocName = null;
        tenant.PrimaryPocEmail = null;
        tenant.PrimaryPocPhone = null;
        tenant.DefaultClassificationLevel = ClassificationLevel.Unclassified;

        var existingOrgs = await db.Organizations.Where(o => o.TenantId == tenantId).ToListAsync();
        db.Organizations.RemoveRange(existingOrgs);

        var existingAudits = await db.AuditLogs
            .Where(a => a.TenantId == tenantId && a.Action.StartsWith("TenantOnboarding."))
            .ToListAsync();
        db.AuditLogs.RemoveRange(existingAudits);

        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> ReadProgressAsync(HttpResponseMessage resp)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        return body.GetProperty("data");
    }
}

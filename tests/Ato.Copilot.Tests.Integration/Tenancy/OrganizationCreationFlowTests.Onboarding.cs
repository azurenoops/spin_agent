using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed partial class OrganizationCreationFlowTests
{
    [Fact]
    public async Task Phase3_ExistingDraftIntentChange_AdvancesOperationRevisionAndRejectsStaleEnrollment()
    {
        // Arrange
        using var client = Client();
        var created = await DataAsync(await CreateAsync(client, Guid.NewGuid().ToString("N"), Body()));
        var tenantId = created.GetProperty("tenantId").GetGuid();
        var operationId = created.GetProperty("operationId").GetGuid();
        var before = await DataAsync(await client.GetAsync($"/api/csp/organizations/{tenantId}/provisioning/current"));
        var revision = before.GetProperty("revision").GetInt64();
        var identityB = Identity();
        var draftId = Guid.NewGuid();
        await DataAsync(await client.PutAsJsonAsync($"/api/csp/organization-onboarding/drafts/{draftId}", new
        {
            schemaVersion = 1, expectedRevision = 0, currentStep = "review",
            values = new { organizationChoice = "existing", existingTenantId = tenantId, administratorChoice = "other", administrator = identityB }
        }));

        // Act
        await DataAsync(await client.PostAsJsonAsync($"/api/csp/organization-onboarding/drafts/{draftId}/confirm",
            new { expectedRevision = 1, confirmed = true }));
        var after = await DataAsync(await client.GetAsync($"/api/csp/organizations/{tenantId}/provisioning/current"));
        await DataAsync(await client.PostAsJsonAsync($"/api/csp/organization-onboarding/drafts/{draftId}/confirm",
            new { expectedRevision = 1, confirmed = true }));
        var replayed = await DataAsync(await client.GetAsync($"/api/csp/organizations/{tenantId}/provisioning/current"));
        var staleIdentityA = Identity();
        staleIdentityA["expectedRevision"] = revision;
        var stale = await client.PatchAsJsonAsync($"/api/csp/organizations/{tenantId}/provisioning/{operationId}", staleIdentityA);

        // Assert
        after.GetProperty("revision").GetInt64().Should().Be(revision + 1);
        replayed.GetProperty("revision").GetInt64().Should().Be(revision + 1);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code")
            .GetString().Should().Be("STALE_REVISION");
        after.GetProperty("initialAdministrator").GetProperty("objectId").GetGuid().Should().Be((Guid)identityB["objectId"]!);
        await VerifyAsync(async db =>
        {
            (await db.Persons.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId)).Should().Be(0);
            (await db.OrganizationMemberships.CountAsync(x => x.TenantId == tenantId)).Should().Be(0);
            (await db.OrganizationRoleAssignments.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId)).Should().Be(0);
            var audit = await db.AuditLogs.IgnoreQueryFilters().SingleAsync(x =>
                x.TenantId == tenantId && x.Action == "OrganizationOnboarding.IntentUpdated");
            audit.UserId.Should().Be($"{DirectoryId:D}/{ActorId:D}");
            using var details = JsonDocument.Parse(audit.Details);
            details.RootElement.GetProperty("operationId").GetGuid().Should().Be(operationId);
            details.RootElement.GetProperty("revision").GetInt64().Should().Be(revision + 1);
        });
    }

    [Fact]
    public async Task Phase3_PartialDraft_SaveReadAndConflictDoNotCreateOrganizationOrAccess()
    {
        // Arrange
        using var client = Client();
        var id = Guid.NewGuid();
        var name = $"Draft only {id:N}";
        var body = DraftBody(name);

        // Act
        var saved = await client.PutAsJsonAsync($"/api/csp/organization-onboarding/drafts/{id}", body);
        var read = await client.GetAsync($"/api/csp/organization-onboarding/drafts/{id}");
        var stale = await client.PutAsJsonAsync($"/api/csp/organization-onboarding/drafts/{id}", DraftBody("Stale replacement"));

        // Assert
        saved.StatusCode.Should().Be(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        var value = await DataAsync(read);
        value.GetProperty("values").GetProperty("displayName").GetString().Should().Be(name);
        value.GetProperty("revision").GetInt64().Should().Be(1);
        value.GetProperty("tenantId").ValueKind.Should().Be(JsonValueKind.Null);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await VerifyAsync(async db =>
            (await db.Tenants.AnyAsync(x => x.DisplayName == name)).Should().BeFalse());
    }

    [Fact]
    public async Task Phase3_DraftConfirmation_ReplaysSameCreationAndDefersEnrollment()
    {
        // Arrange
        using var client = Client();
        var id = Guid.NewGuid();
        var name = $"Confirmed draft {id:N}";
        await client.PutAsJsonAsync($"/api/csp/organization-onboarding/drafts/{id}", DraftBody(name));
        var confirmation = new { expectedRevision = 1, confirmed = true };

        // Act
        var first = await client.PostAsJsonAsync($"/api/csp/organization-onboarding/drafts/{id}/confirm", confirmation);
        var replay = await client.PostAsJsonAsync($"/api/csp/organization-onboarding/drafts/{id}/confirm", confirmation);

        // Assert
        var result = await DataAsync(first);
        var tenantId = result.GetProperty("tenantId").GetGuid();
        (await DataAsync(replay)).GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        await VerifyAsync(async db =>
        {
            (await db.Tenants.CountAsync(x => x.DisplayName == name)).Should().Be(1);
            (await db.OrganizationMemberships.CountAsync(x => x.TenantId == tenantId)).Should().Be(0);
            (await db.OrganizationRoleAssignments.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId)).Should().Be(0);
        });
        await AssertNoSystemWritesAsync(tenantId);
    }

    [Fact]
    public async Task Phase3_SetupSummary_SeparatesDifferentLiveAdministratorFromUnboundRequest()
    {
        // Arrange
        using var client = Client();
        var created = await DataAsync(await CreateAsync(client, Guid.NewGuid().ToString("N"), Body(Identity())));
        var tenantId = created.GetProperty("tenantId").GetGuid();
        var operationId = created.GetProperty("operationId").GetGuid();
        await SeedOnboardingAdministratorAsync(tenantId, Guid.NewGuid());

        // Act
        var response = await client.GetAsync($"/api/csp/organizations/{tenantId}/setup-summary?operationId={operationId}&administratorPageSize=1");
        var tooLarge = await client.GetAsync($"/api/csp/organizations/{tenantId}/setup-summary?administratorPageSize=101");

        // Assert
        var summary = await DataAsync(response);
        summary.GetProperty("liveAccess").GetProperty("state").GetString().Should().Be("Available");
        summary.GetProperty("liveAccess").GetProperty("administrators").GetProperty("total").GetInt32().Should().Be(1);
        summary.GetProperty("requestedOperation").GetProperty("administratorState").GetString().Should().Be("Pending");
        summary.GetProperty("actorActions").GetProperty("canEnterOrganization").GetBoolean().Should().BeFalse();
        tooLarge.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Phase3_OrdinaryAdministrator_CanHydrateSubmitAndSaveIncompleteTenantDraft()
    {
        // Arrange
        using var provider = Client();
        var created = await DataAsync(await CreateAsync(provider, Guid.NewGuid().ToString("N"), Body()));
        var tenantId = created.GetProperty("tenantId").GetGuid();
        await SeedOnboardingAdministratorAsync(tenantId, ActorId);
        using var client = Client();
        client.DefaultRequestHeaders.Remove("X-Test-Roles");
        client.DefaultRequestHeaders.Remove("X-Workspace-Kind");
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", tenantId.ToString());

        // Act
        var initial = await DataAsync(await client.GetAsync("/api/onboarding/tenant/state"));
        var applied = await client.PostAsJsonAsync("/api/onboarding/tenant/legal-entity",
            new { legalEntityName = "Applied legal name", doDComponent = "NAVY", timeZone = "America/New_York" });

        // Assert
        initial.GetProperty("submittedValues").GetProperty("legalEntity").GetProperty("legalEntityName")
            .GetString().Should().Be("Legal entity");
        applied.StatusCode.Should().Be(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        var state = await DataAsync(applied);
        var revision = state.GetProperty("draftRevision").GetInt64();
        var saved = await client.PutAsJsonAsync("/api/onboarding/tenant/draft", new
        {
            schemaVersion = 1, expectedRevision = revision, currentStep = "Tenant.HqAddress",
            values = new { hqAddress = new { hqCity = "Unsaved city", hqCountry = "US" } }
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var hydrated = await DataAsync(await client.GetAsync("/api/onboarding/tenant/state"));
        hydrated.GetProperty("draft").GetProperty("values").GetProperty("hqAddress").GetProperty("hqCity")
            .GetString().Should().Be("Unsaved city");
        await VerifyAsync(async db =>
        {
            var tenant = await db.Tenants.SingleAsync(x => x.Id == tenantId);
            tenant.LegalEntityName.Should().Be("Applied legal name");
            tenant.HqCity.Should().BeNull();
            tenant.OnboardingState.Should().Be(OnboardingState.InWizard);
        });
    }

    [Fact]
    public async Task Phase3_OrdinaryMember_CannotWriteTenantDraftOrReadPrivateProviderDraft()
    {
        // Arrange
        using var provider = Client();
        var id = Guid.NewGuid();
        await provider.PutAsJsonAsync($"/api/csp/organization-onboarding/drafts/{id}", DraftBody("Private draft"));
        var created = await DataAsync(await CreateAsync(provider, Guid.NewGuid().ToString("N"), Body()));
        var tenantId = created.GetProperty("tenantId").GetGuid();
        await SeedOnboardingAdministratorAsync(tenantId, ActorId, administrator: false);
        using var member = Client();
        member.DefaultRequestHeaders.Remove("X-Test-Roles");
        member.DefaultRequestHeaders.Remove("X-Workspace-Kind");
        member.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        member.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", tenantId.ToString());

        // Act
        var save = await member.PutAsJsonAsync("/api/onboarding/tenant/draft",
            new { schemaVersion = 1, expectedRevision = 0, currentStep = "Tenant.LegalEntity", values = new { } });
        var privateRead = await member.GetAsync($"/api/csp/organization-onboarding/drafts/{id}");

        // Assert
        save.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        privateRead.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static object DraftBody(string name) => new
    {
        schemaVersion = 1, expectedRevision = 0, currentStep = "details",
        values = new
        {
            organizationChoice = "create", displayName = name, primaryPocName = "Contact only",
            administratorChoice = "deferred", deferralReason = "Complete access later"
        }
    };

    private Task SeedOnboardingAdministratorAsync(Guid tenantId, Guid objectId, bool administrator = true)
        => VerifyAsync(async db =>
        {
            var person = new Person { TenantId = tenantId, DisplayName = "Existing administrator", Email = $"{Guid.NewGuid():N}@example.invalid" };
            db.Persons.Add(person);
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = tenantId, PersonId = person.Id, DirectoryTenantId = DirectoryId,
                ObjectId = objectId, GrantedBy = "test"
            });
            if (administrator)
                db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
                {
                    TenantId = tenantId, PersonId = person.Id, Role = OrganizationRole.Administrator,
                    CreatedBy = ActorId, UpdatedBy = ActorId
                });
            await db.SaveChangesAsync();
        });
}

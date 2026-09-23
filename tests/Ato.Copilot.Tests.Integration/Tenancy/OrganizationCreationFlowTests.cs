using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed partial class OrganizationCreationFlowTests(WorkspaceMembershipFactory factory)
    : IClassFixture<WorkspaceMembershipFactory>
{
    private static readonly Guid DirectoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000101");
    private static readonly Guid ActorId = Guid.Parse("cccccccc-0000-0000-0000-000000000102");

    [Fact]
    public async Task DeferredCreation_RecoversWithoutWritesAndProjectsPendingSetup()
    {
        // Arrange
        using var client = Client();
        var key = Guid.NewGuid().ToString("N");
        var body = Body();

        // Act
        var created = await CreateAsync(client, key, body);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.Location.Should().NotBeNull();
        var data = await DataAsync(created);
        var tenantId = data.GetProperty("tenantId").GetGuid();
        var recovered = await client.GetAsync($"/api/csp/organization-creations/{key}");
        var current = await client.GetAsync($"/api/csp/organizations/{tenantId}/provisioning/current");
        var detail = await client.GetAsync($"/api/csp/organizations/{tenantId}");

        // Assert
        recovered.StatusCode.Should().Be(HttpStatusCode.OK);
        var recovery = await DataAsync(recovered);
        recovery.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        recovery.GetProperty("operationId").GetGuid().Should().Be(data.GetProperty("operationId").GetGuid());
        recovery.GetProperty("existing").GetBoolean().Should().BeTrue();
        recovery.GetProperty("status").GetString().Should().Be("Active");
        var state = await DataAsync(current);
        state.GetProperty("initialAdministrator").ValueKind.Should().Be(JsonValueKind.Null);
        state.GetProperty("personState").GetString().Should().Be("NotRequested");
        state.GetProperty("canEditAdministrator").GetBoolean().Should().BeTrue();
        state.GetProperty("overallState").GetString().Should().Be("InProgress");
        var organization = await DataAsync(detail);
        organization.GetProperty("setupState").GetString().Should().Be("Pending");
        organization.GetProperty("memberCount").GetInt32().Should().Be(0);
        organization.GetProperty("legalEntityName").GetString().Should().Be("Legal entity");
        organization.GetProperty("primaryPocName").GetString().Should().Be("Contact only");
        organization.GetProperty("primaryPocEmail").GetString().Should().Be("contact@example.invalid");
        await VerifyAsync(async db =>
        {
            (await db.Persons.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId)).Should().Be(0);
            (await db.OrganizationMemberships.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId)).Should().Be(0);
            var operation = await db.OrganizationProvisioningOperations.SingleAsync(x => x.TenantId == tenantId);
            operation.Revision.Should().Be(0, "recovery and detail reads must not advance provisioning");
            operation.InitialAdministratorJson.Should().BeNull();
        });
        var missing = await client.GetAsync($"/api/csp/organization-creations/{Guid.NewGuid():N}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code")
            .GetString().Should().Be("ORGANIZATION_CREATION_NOT_FOUND");
        await AssertNoSystemWritesAsync(tenantId);
    }

    [Fact]
    public async Task NewPerson_ConfirmedIntentSurvivesRefreshAndResumeIsIdempotent()
    {
        // Arrange
        using var client = Client();
        var identity = Identity();
        var body = Body(identity);
        var key = Guid.NewGuid().ToString("N");
        var created = await DataAsync(await CreateAsync(client, key, body));
        var tenantId = created.GetProperty("tenantId").GetGuid();
        var operationId = created.GetProperty("operationId").GetGuid();
        var route = $"/api/csp/organizations/{tenantId}/provisioning/{operationId}";

        // Act
        var before = await DataAsync(await client.GetAsync($"/api/csp/organizations/{tenantId}/provisioning/current"));
        var persistedBefore = before.GetProperty("initialAdministrator");
        persistedBefore.GetProperty("newPerson").GetProperty("email")
            .GetString().Should().Be("administrator@example.invalid");
        persistedBefore.GetProperty("personId").ValueKind.Should().Be(JsonValueKind.Null);
        var first = await client.PatchAsJsonAsync(route, persistedBefore);
        var refreshed = await DataAsync(await client.GetAsync($"/api/csp/organizations/{tenantId}/provisioning/current"));
        var persistedAfter = refreshed.GetProperty("initialAdministrator");
        persistedAfter.GetRawText().Should().Be(persistedBefore.GetRawText());
        persistedAfter.GetProperty("personId").ValueKind.Should().Be(JsonValueKind.Null);
        refreshed.TryGetProperty("boundPersonId", out _).Should().BeFalse();
        var retry = await client.PatchAsJsonAsync(route, persistedAfter);
        var replay = await CreateAsync(client, key, body);

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = await DataAsync(retry);
        state.GetProperty("personState").GetString().Should().Be("Completed");
        state.GetProperty("membershipState").GetString().Should().Be("Completed");
        state.GetProperty("administratorState").GetString().Should().Be("Completed");
        state.GetProperty("overallState").GetString().Should().Be("Completed");
        state.GetProperty("canEditAdministrator").GetBoolean().Should().BeFalse();
        await VerifyAsync(async db =>
        {
            var person = await db.Persons.IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenantId);
            person.DisplayName.Should().Be("Initial administrator");
            person.IsLinkedToDirectory.Should().BeFalse();
            person.EntraObjectId.Should().BeNull();
            (await db.OrganizationMemberships.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId && x.RevokedAt == null)).Should().Be(1);
            (await db.OrganizationRoleAssignments.IgnoreQueryFilters().CountAsync(x =>
                x.TenantId == tenantId && x.Role == OrganizationRole.Administrator && x.RemovedAt == null)).Should().Be(1);
            (await db.WizardAuditEntries.IgnoreQueryFilters().CountAsync(x =>
                x.TenantId == tenantId && x.ResourceId == person.Id && x.Action == WizardAuditAction.PersonCreated)).Should().Be(1);
        });
        var detail = await DataAsync(await client.GetAsync($"/api/csp/organizations/{tenantId}"));
        detail.GetProperty("setupState").GetString().Should().Be("Completed");
        detail.GetProperty("memberCount").GetInt32().Should().Be(1);
        var changed = Identity();
        (await client.PatchAsJsonAsync(route, changed)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CreateAsync(client, key, Body(changed, (string)body["displayName"]!))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertNoSystemWritesAsync(tenantId);
    }

    [Fact]
    public async Task InvalidUnboundPerson_CanBeCorrectedToExistingLocalPerson()
    {
        // Arrange
        using var client = Client();
        var created = await DataAsync(await CreateAsync(client, Guid.NewGuid().ToString("N"), Body()));
        var tenantId = created.GetProperty("tenantId").GetGuid();
        var operationId = created.GetProperty("operationId").GetGuid();
        var route = $"/api/csp/organizations/{tenantId}/provisioning/{operationId}";
        var directory = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var personId = Guid.NewGuid();

        // Act
        var invalid = await client.PatchAsJsonAsync(route,
            new { directoryTenantId = directory, objectId, personId = Guid.NewGuid() });
        var before = await DataAsync(await client.GetAsync($"/api/csp/organizations/{tenantId}/provisioning/current"));
        await VerifyAsync(async db =>
        {
            db.Persons.Add(new Person { Id = personId, TenantId = tenantId, DisplayName = "Existing", Email = "existing@example.invalid" });
            await db.SaveChangesAsync();
        });
        var corrected = await client.PatchAsJsonAsync(route, new { directoryTenantId = directory, objectId, personId });

        // Assert
        invalid.StatusCode.Should().Be(HttpStatusCode.NotFound);
        before.GetProperty("canEditAdministrator").GetBoolean().Should().BeTrue();
        corrected.StatusCode.Should().Be(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        var state = await DataAsync(corrected);
        state.GetProperty("personState").GetString().Should().Be("NotRequested");
        state.GetProperty("overallState").GetString().Should().Be("Completed");
        state.GetProperty("initialAdministrator").GetProperty("personId").GetGuid().Should().Be(personId);
    }

    [Theory]
    [InlineData("empty-directory")]
    [InlineData("both-persons")]
    [InlineData("missing-person")]
    [InlineData("bad-email")]
    public async Task InvalidInitialAdministrator_IsRejectedBeforeOrganizationCreation(string invalid)
    {
        // Arrange
        using var client = Client();
        var identity = Identity();
        if (invalid == "empty-directory") identity["directoryTenantId"] = Guid.Empty;
        if (invalid == "both-persons") identity["personId"] = Guid.NewGuid();
        if (invalid == "missing-person") identity.Remove("newPerson");
        if (invalid == "bad-email") identity["newPerson"] = new { displayName = "Admin", email = "invalid" };
        var key = Guid.NewGuid().ToString("N");
        var body = Body(identity);

        // Act
        var response = await CreateAsync(client, key, body);

        // Assert
        ((int)response.StatusCode).Should().BeOneOf(400, 422);
        await VerifyAsync(async db =>
            (await db.OrganizationProvisioningOperations.AnyAsync(x => x.IdempotencyKey == key)).Should().BeFalse());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentCreatesAndResumes_DoNotDuplicateOrganizationPersonOrGrants(bool differentCreationKeys)
    {
        // Arrange
        using var client = Client();
        var key = Guid.NewGuid().ToString("N");
        var identity = Identity();
        var body = Body(identity);

        // Act
        var creates = await Task.WhenAll(CreateAsync(client, key, body),
            CreateAsync(client, differentCreationKeys ? Guid.NewGuid().ToString("N") : key, body));
        var winner = creates.First(x => x.IsSuccessStatusCode);
        var created = await DataAsync(winner);
        var tenantId = created.GetProperty("tenantId").GetGuid();
        var operationId = created.GetProperty("operationId").GetGuid();
        var route = $"/api/csp/organizations/{tenantId}/provisioning/{operationId}";
        var resumes = await Task.WhenAll(client.PatchAsJsonAsync(route, identity), client.PatchAsJsonAsync(route, identity));

        // Assert
        creates.Count(x => x.StatusCode == HttpStatusCode.Created).Should().Be(1);
        if (!differentCreationKeys) creates.Should().OnlyContain(x => x.IsSuccessStatusCode);
        resumes.Should().OnlyContain(x => x.IsSuccessStatusCode || x.StatusCode == HttpStatusCode.Conflict);
        (await client.PatchAsJsonAsync(route, identity)).StatusCode.Should().Be(HttpStatusCode.OK);
        await VerifyAsync(async db =>
        {
            (await db.Persons.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId)).Should().Be(1);
            (await db.OrganizationMemberships.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId)).Should().Be(1);
            (await db.OrganizationRoleAssignments.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId)).Should().Be(1);
        });
    }

    private HttpClient Client(HttpClient? existing = null)
    {
        var client = existing ?? factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", DirectoryId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", ActorId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "csp");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        return client;
    }

    private static Dictionary<string, object?> Identity() => new()
    {
        ["directoryTenantId"] = Guid.NewGuid(), ["objectId"] = Guid.NewGuid(),
        ["newPerson"] = new { displayName = "Initial administrator", email = "administrator@example.invalid" }
    };

    private static Dictionary<string, object?> Body(object? administrator = null, string? name = null) => new()
    {
        ["displayName"] = name ?? $"Mission {Guid.NewGuid():N}", ["legalEntityName"] = "Legal entity",
        ["primaryPocName"] = "Contact only", ["primaryPocEmail"] = "contact@example.invalid",
        ["initialAdministrator"] = administrator
    };

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/csp/dashboard/tenants")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private async Task VerifyAsync(Func<AtoCopilotContext, Task> verify)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await verify(scope.ServiceProvider.GetRequiredService<AtoCopilotContext>());
    }

    private Task AssertNoSystemWritesAsync(Guid tenantId) => VerifyAsync(async db =>
    {
        (await db.RegisteredSystems.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId)).Should().BeFalse();
        (await db.SystemRoleAssignments.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId)).Should().BeFalse();
        (await db.CapabilitySubscriptions.IgnoreQueryFilters().AnyAsync(x => x.RoutingTenantId == tenantId)).Should().BeFalse();
        (await db.SystemCapabilityLinks.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId)).Should().BeFalse();
    });
}

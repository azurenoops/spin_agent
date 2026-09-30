using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class SystemSetupContractTests(WorkspaceMembershipFactory factory)
    : IClassFixture<WorkspaceMembershipFactory>
{
    private static readonly Guid Directory = Guid.Parse("acab0000-0000-0000-0000-000000000001");
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;
    private string Root => $"/api/workspaces/organizations/{Tenant}/systems";

    private async Task<(HttpClient Client, string SystemId, Guid PersonId)> SeedAsync(OrganizationRole role)
    {
        var client = factory.CreateClient();
        var actor = Guid.NewGuid();
        var systemId = $"sys-{Guid.NewGuid():N}";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var person = new Person { TenantId = Tenant, DisplayName = "Setup reviewer", Email = $"{actor:N}@example.invalid" };
        db.Persons.Add(person);
        db.RegisteredSystems.Add(new RegisteredSystem { Id = systemId, TenantId = Tenant,
            Name = "Existing opaque system", HostingEnvironment = "Test", CreatedBy = "fixture" });
        await db.SaveChangesAsync();
        db.OrganizationMemberships.Add(new() { TenantId = Tenant, DirectoryTenantId = Directory,
            ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture" });
        db.OrganizationRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id, Role = role });
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Add("X-Test-Tid", Directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        return (client, systemId, person.Id);
    }

    [Fact]
    public async Task CreateDraft_ReplaysSameIdentity_WithoutRoleOrAuthorizationGrants()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = f.Client;
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var request = new { name = "A saved preparation draft", objective = "initialAto", lastScreen = "s-details" };
        // Act
        var first = await client.PostAsJsonAsync($"{Root}/setup-drafts", request);
        var repeat = await client.PostAsJsonAsync($"{Root}/setup-drafts", request);
        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        repeat.StatusCode.Should().Be(HttpStatusCode.OK, await repeat.Content.ReadAsStringAsync());
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("systemId").GetString();
        (await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("systemId").GetString().Should().Be(id);
        var summaries = await client.GetFromJsonAsync<JsonElement>($"{Root}/setup-drafts");
        var summary = summaries.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("systemId").GetString() == id);
        summary.GetProperty("displayName").GetString().Should().Be(request.name);
        summary.GetProperty("revision").GetInt64().Should().BeGreaterThan(0);
        summary.GetProperty("savedAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));
        summary.GetProperty("setupState").GetString().Should().Be("draft");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.AuthorizationDecisions.AnyAsync(x => x.RegisteredSystemId == id)).Should().BeFalse();
        (await db.SystemRoleAssignments.AnyAsync(x => x.RegisteredSystemId == id)).Should().BeFalse();
        (await db.RmfRoleAssignments.AnyAsync(x => x.RegisteredSystemId == id)).Should().BeFalse();
    }

    [Fact]
    public async Task SetupRead_PreservesOpaqueSystemId_AndDoesNotAssertCollection()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = f.Client;
        // Act
        var response = await client.GetAsync($"{Root}/{f.SystemId}/setup");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("systemId").GetString().Should().Be(f.SystemId);
        data.GetProperty("setupState").GetString().Should().Be("legacy");
        data.GetProperty("monitoring").GetProperty("collection").GetString().Should().Be("unknown");
    }

    [Fact]
    public async Task MonitoringProjection_ReadsSavedAttachmentWithoutInventingSuccessfulCollection()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = f.Client;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var system = await db.RegisteredSystems.SingleAsync(x => x.Id == f.SystemId);
        system.AzureProfile = new AzureEnvironmentProfile
        {
            CloudEnvironment = AzureCloudEnvironment.Government,
            SubscriptionIds = ["11111111-2222-3333-4444-555555555555"]
        };
        await db.SaveChangesAsync();
        // Act
        var response = await client.GetAsync($"{Root}/{f.SystemId}/setup/monitoring");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        state.GetProperty("configuration").GetString().Should().Be("present");
        state.GetProperty("access").GetString().Should().Be("notChecked");
        state.GetProperty("collection").GetString().Should().Be("unknown");
        state.GetProperty("evaluation").GetString().Should().Be("unknown");
    }

    [Fact]
    public async Task SourceReceipts_AreSystemScoped_AndUnsupportedUploadNeverCommits()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = f.Client;
        using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent([1, 2, 3]), "file", "unsupported.exe");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        // Act
        var receipts = await client.GetAsync($"{Root}/{f.SystemId}/source-imports");
        var rejected = await client.PostAsync($"{Root}/{f.SystemId}/source-imports/unknown", upload);
        // Assert
        receipts.StatusCode.Should().Be(HttpStatusCode.OK, await receipts.Content.ReadAsStringAsync());
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest, await rejected.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.RegisteredSystems.SingleAsync(x => x.Id == f.SystemId)).Name.Should().Be("Existing opaque system");
    }

    [Fact]
    public async Task OrdinaryMember_CannotCreateSystemDraft()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.SystemOwner);
        using var client = f.Client;
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        // Act
        var response = await client.PostAsJsonAsync($"{Root}/setup-drafts", new { name = "Not authorized", lastScreen = "s-details" });
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(OrganizationRole.Issm, true)]
    [InlineData(OrganizationRole.SystemOwner, false)]
    public async Task SetupAccess_UsesTheCreateGate_NotBrowserRoleInference(OrganizationRole role, bool expected)
    {
        // Arrange
        var f = await SeedAsync(role);
        using var client = f.Client;
        // Act
        var response = await client.GetAsync($"{Root}/setup-access");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data")
            .GetProperty("canCreateSystem").GetBoolean().Should().Be(expected);
    }

    [Fact]
    public async Task DualCspAndOrganizationIssm_UsesOrdinaryMembershipWithoutCrossTenantOversight()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = f.Client;
        client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        // Act
        var allowed = await client.GetAsync($"{Root}/setup-access");
        var created = await client.PostAsJsonAsync($"{Root}/setup-drafts",
            new { name = "Dual-role ordinary draft", lastScreen = "s-details" });
        var crossTenant = await client.GetAsync($"/api/workspaces/organizations/{WorkspaceMembershipFactory.TenantBId}/systems/{f.SystemId}/setup");
        // Assert
        allowed.StatusCode.Should().Be(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        (await allowed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("canCreateSystem").GetBoolean().Should().BeTrue();
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        crossTenant.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("tenantId").GetGuid().Should().Be(Tenant);
    }

    [Fact]
    public async Task CreateDraft_KeyConflictAndRevokedReplayNeverCreateAnotherSystem()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = f.Client;
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var original = new { name = $"Replay {Guid.NewGuid():N}", lastScreen = "s-details" };
        var first = await client.PostAsJsonAsync($"{Root}/setup-drafts", original);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        // Act
        var conflict = await client.PostAsJsonAsync($"{Root}/setup-drafts", new { name = "Changed request", lastScreen = "s-details" });
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var membership = await db.OrganizationMemberships.SingleAsync(x => x.PersonId == f.PersonId);
        membership.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var replay = await client.PostAsJsonAsync($"{Root}/setup-drafts", original);
        // Assert
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        replay.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        (await db.RegisteredSystems.CountAsync(x => x.Name == original.name)).Should().Be(1);
    }

    [Fact]
    public async Task SaveAndConfirm_AreRevisionBound_AndDoNotAdvanceOrDiscardSystem()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = f.Client;
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var created = await client.PostAsJsonAsync($"{Root}/setup-drafts", new { name = "Retained draft", lastScreen = "s-details" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var data = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var id = data.GetProperty("systemId").GetString();
        var revision = data.GetProperty("revision").GetInt64();
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", $"\"setup-{revision}\"");
        var body = new { name = "Retained draft", objective = "initialAto", lastScreen = "s-review",
            contact = new { personId = f.PersonId, responsibility = "preparationContact" },
            expectedIdentityRevision = data.GetProperty("identityRevision").GetString() };
        // Act
        var saved = await client.PutAsJsonAsync($"{Root}/{id}/setup", body);
        var replay = await client.PutAsJsonAsync($"{Root}/{id}/setup", body);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var stale = await client.PutAsJsonAsync($"{Root}/{id}/setup", body);
        // Assert
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var savedData = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        client.DefaultRequestHeaders.Remove("If-Match");
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", $"\"setup-{savedData.GetProperty("revision").GetInt64()}\"");
        var confirmed = await client.PostAsJsonAsync($"{Root}/{id}/setup/confirm", new
        { reviewRevision = savedData.GetProperty("revision").GetInt64(),
            identityRevision = savedData.GetProperty("identityRevision").GetString(), confirmed = true });
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("setupState").GetString().Should().Be("confirmed");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var system = await db.RegisteredSystems.SingleAsync(x => x.Id == id);
        system.IsActive.Should().BeTrue();
        system.CurrentRmfStep.Should().Be(RmfPhase.Prepare);
        (await db.AuthorizationDecisions.AnyAsync(x => x.RegisteredSystemId == id)).Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_RejectsIdentityEditedThroughCanonicalSystemUpdateAfterReview()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = f.Client;
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var creation = await client.PostAsJsonAsync($"{Root}/setup-drafts", new
        {
            name = "Reviewed setup identity", missionPurpose = "Reviewed purpose", objective = "initialAto",
            contact = new { personId = f.PersonId, responsibility = "preparationContact" }, lastScreen = "s-review"
        });
        creation.StatusCode.Should().Be(HttpStatusCode.Created);
        var reviewed = (await creation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var id = reviewed.GetProperty("systemId").GetString();
        var update = await client.PutAsJsonAsync($"/api/dashboard/systems/{id}", new
        { name = "Concurrent identity", description = "Concurrent purpose" });
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", $"\"setup-{reviewed.GetProperty("revision").GetInt64()}\"");
        // Act
        var confirm = await client.PostAsJsonAsync($"{Root}/{id}/setup/confirm", new
        {
            reviewRevision = reviewed.GetProperty("revision").GetInt64(), confirmed = true,
            identityRevision = reviewed.GetProperty("identityRevision").GetString()
        });
        // Assert
        confirm.StatusCode.Should().Be(HttpStatusCode.Conflict, await confirm.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var system = await scope.ServiceProvider.GetRequiredService<AtoCopilotContext>().RegisteredSystems.SingleAsync(x => x.Id == id);
        system.SetupCompletedAt.Should().BeNull();
        system.Name.Should().Be("Concurrent identity");
        system.Description.Should().Be("Concurrent purpose");
    }

    [Fact]
    public async Task ExplicitTechnicalUpdates_ClearOnlyValidatedMarkers_AndRenderRecordedValues()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = f.Client;
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var creation = await client.PostAsJsonAsync($"{Root}/setup-drafts", new { name = "Technical draft", lastScreen = "s-details" });
        creation.StatusCode.Should().Be(HttpStatusCode.Created);
        var initial = (await creation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var id = initial.GetProperty("systemId").GetString()!;
        async Task<string[]> Markers()
        {
            var result = await client.GetFromJsonAsync<JsonElement>($"{Root}/{id}/setup");
            return result.GetProperty("data").GetProperty("unconfirmedFields").EnumerateArray()
                .Select(x => x.GetString()!).ToArray();
        }
        // Act / Assert
        var unrelated = await client.PutAsJsonAsync($"/api/dashboard/systems/{id}", new { description = "Updated mission purpose" });
        unrelated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Markers()).Should().BeEquivalentTo("systemType", "missionCriticality", "hostingEnvironment");
        var type = await client.PutAsJsonAsync($"/api/dashboard/systems/{id}", new { systemType = "Enclave" });
        type.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Markers()).Should().BeEquivalentTo("missionCriticality", "hostingEnvironment");
        var invalid = await client.PutAsJsonAsync($"/api/dashboard/systems/{id}", new { missionCriticality = "Invented" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Markers()).Should().BeEquivalentTo("missionCriticality", "hostingEnvironment");
        var rest = await client.PutAsJsonAsync($"/api/dashboard/systems/{id}",
            new { missionCriticality = "MissionCritical", hostingEnvironment = "OnPremises" });
        rest.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Markers()).Should().BeEmpty();
        await using var scope = factory.Services.CreateAsyncScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<Ato.Copilot.Core.Interfaces.Tenancy.ITenantContextAccessor>()
            .Push(new Ato.Copilot.Core.Services.Tenancy.TenantContext(Tenant));
        var bytes = await scope.ServiceProvider.GetRequiredService<Ato.Copilot.Core.Interfaces.Compliance.IDocumentTemplateService>()
            .RenderDocxAsync(id, "ssp", null);
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        var xml = await reader.ReadToEndAsync();
        xml.Should().Contain("Enclave").And.Contain("MissionCritical").And.Contain("OnPremises")
            .And.NotContain("Not recorded (setup draft)");
    }

    [Fact]
    public async Task ScopedSourceUpload_RejectsUnsupportedFormatWithoutFakeReceipt()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = f.Client;
        using var upload = new MultipartFormDataContent();
        upload.Add(new ByteArrayContent([1, 2, 3]), "file", "source.exe");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        // Act
        var response = await client.PostAsync($"{Root}/{f.SystemId}/source-imports/ssp-pdf", upload);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code")
            .GetString().Should().Be("UNSUPPORTED_SOURCE_FORMAT");
    }
}

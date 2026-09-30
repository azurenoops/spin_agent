using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using WorkspaceMembershipFactory = Ato.Copilot.Tests.Integration.Tenancy.WorkspaceMembershipFactory;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Compliance;

public sealed class EmassExchangeEndpointsTests(WorkspaceMembershipFactory factory) : IClassFixture<WorkspaceMembershipFactory>
{
    private static readonly Guid Directory = Guid.Parse("acab0000-0000-0000-0000-000000000001");
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private HttpClient Client(Guid actor, bool elevatedClaim = false)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", Directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        if (elevatedClaim) client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");
        return client;
    }

    private async Task<(Guid Actor, Guid Person, string System, AuthorizationPackage Package)> Seed(OrganizationRole? role)
    {
        using var hostClient = factory.CreateClient();
        var actor = Guid.NewGuid();
        var person = new Person { TenantId = Tenant, DisplayName = "Exchange recorder", Email = $"{actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = Tenant, Name = "Exchange fixture", CreatedBy = "fixture", IsActive = true };
        var package = new AuthorizationPackage { TenantId = Tenant, RegisteredSystemId = system.Id,
            Status = PackageStatus.Completed, ContentHash = Hash,
            GeneratedAt = DateTimeOffset.UtcNow.AddDays(-2), CompletedAt = DateTimeOffset.UtcNow.AddDays(-1), GeneratedBy = "fixture" };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Persons.Add(person);
        db.RegisteredSystems.Add(system);
        db.AuthorizationPackages.Add(package);
        db.OrganizationMemberships.Add(new() { TenantId = Tenant, DirectoryTenantId = Directory, ObjectId = actor,
            PersonId = person.Id, GrantedBy = "fixture" });
        if (role.HasValue) db.SystemRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id,
            RegisteredSystemId = system.Id, Role = role.Value });
        await db.SaveChangesAsync();
        return (actor, person.Id, system.Id, package);
    }

    private static RecordEmassExchangeRequest Request(AuthorizationPackage package) =>
        new(package.Id, Hash, package.GeneratedAt, "ReceiptRecorded", "Manual receiving workflow", "REF-123",
            DateTimeOffset.UtcNow.AddHours(-1), "Synthetic receipt observation.", Guid.NewGuid().ToString(), 0);

    [Theory]
    [InlineData(OrganizationRole.Issm, HttpStatusCode.OK)]
    [InlineData(OrganizationRole.Assessor, HttpStatusCode.Forbidden)]
    [InlineData(OrganizationRole.Isso, HttpStatusCode.OK)]
    [InlineData(OrganizationRole.MissionOwner, HttpStatusCode.Forbidden)]
    [InlineData(OrganizationRole.AuthorizingOfficial, HttpStatusCode.Forbidden)]
    public async Task Production_mapper_uses_persisted_system_workflow_roles(OrganizationRole role, HttpStatusCode expected)
    {
        // Arrange
        var fixture = await Seed(role);
        using var client = Client(fixture.Actor, elevatedClaim: true);
        // Act
        var response = await client.PostAsJsonAsync($"/api/dashboard/systems/{fixture.System}/emass/exchanges", Request(fixture.Package));
        // Assert
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.Set<EmassExchangeRecord>().CountAsync(x => x.RegisteredSystemId == fixture.System))
            .Should().Be(expected == HttpStatusCode.OK ? 1 : 0);
        (await db.AuthorizationDecisions.AnyAsync(x => x.RegisteredSystemId == fixture.System)).Should().BeFalse();
    }

    [Fact]
    public async Task Production_mapper_retains_actor_identity_replays_and_rejects_stale_history()
    {
        // Arrange
        var fixture = await Seed(OrganizationRole.Issm);
        using var client = Client(fixture.Actor);
        var request = Request(fixture.Package);
        var route = $"/api/systems/{fixture.System}/emass/exchanges";
        var spoof = JsonSerializer.SerializeToNode(request, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        spoof["recordedBy"] = "client-forged-actor";
        // Act
        var first = await client.PostAsJsonAsync(route, spoof);
        var replay = await client.PostAsJsonAsync(route, request);
        var stale = await client.PostAsJsonAsync(route, request with { IdempotencyKey = Guid.NewGuid().ToString() });
        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var firstBody = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var replayBody = (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        firstBody.GetProperty("id").GetString().Should().Be(replayBody.GetProperty("id").GetString());
        firstBody.GetProperty("recordedBy").GetString().Should().Be(fixture.Actor.ToString());
        firstBody.GetProperty("packageHash").GetString().Should().Be(Hash);
        var history = await client.GetFromJsonAsync<JsonElement>(route);
        history.GetProperty("data").GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Unassigned_and_foreign_tenant_export_and_record_are_hidden()
    {
        // Arrange
        var writer = await Seed(OrganizationRole.Issm);
        var other = await Seed(OrganizationRole.Issm);
        using var client = Client(writer.Actor, elevatedClaim: true);
        // Act
        var denied = await client.GetAsync($"/api/systems/{other.System}/emass/exchanges");
        var wrongPackage = await client.PostAsJsonAsync($"/api/systems/{writer.System}/emass/exchanges", Request(other.Package));
        // Assert
        denied.StatusCode.Should().Be(HttpStatusCode.NotFound, await denied.Content.ReadAsStringAsync());
        wrongPackage.StatusCode.Should().Be(HttpStatusCode.BadRequest, await wrongPackage.Content.ReadAsStringAsync());
        // Arrange
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.AuthorizationPackages.SingleAsync(x => x.Id == writer.Package.Id)).TenantId = WorkspaceMembershipFactory.TenantBId;
            await db.SaveChangesAsync();
        }
        // Act
        var foreign = await client.PostAsJsonAsync($"/api/systems/{writer.System}/emass/exchanges", Request(writer.Package));
        var exports = await client.GetFromJsonAsync<JsonElement>($"/api/systems/{writer.System}/emass/exchange-exports");
        // Assert
        foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        exports.GetProperty("data").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Revoked_assignment_prevents_replay_and_retains_prior_observation()
    {
        // Arrange
        var fixture = await Seed(OrganizationRole.Isso);
        using var client = Client(fixture.Actor);
        var route = $"/api/systems/{fixture.System}/emass/exchanges";
        var request = Request(fixture.Package);
        var saved = await client.PostAsJsonAsync(route, request);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.SystemRoleAssignments.SingleAsync(x => x.RegisteredSystemId == fixture.System)).RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        // Act
        var replay = await client.PostAsJsonAsync(route, request);
        var read = await client.GetAsync(route);
        // Assert
        replay.StatusCode.Should().Be(HttpStatusCode.NotFound);
        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await verifyDb.Set<EmassExchangeRecord>().CountAsync(x => x.RegisteredSystemId == fixture.System)).Should().Be(1);
    }
}

using System.Net;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Services;
using WorkspaceMembershipFactory = Ato.Copilot.Tests.Integration.Tenancy.WorkspaceMembershipFactory;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Notifications;

public sealed class WorkspaceProgressEndpointTests(WorkspaceMembershipFactory factory) : IClassFixture<WorkspaceMembershipFactory>
{
    private readonly Guid _directory = Guid.NewGuid();

    private HttpClient Client(Guid actor, Guid tenant)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Tid", _directory.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", tenant.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        return client;
    }

    private async Task<(Guid Actor, string System)> SeedAsync(Guid tenant, OrganizationRole role)
    {
        var actor = Guid.NewGuid();
        var person = new Person { TenantId = tenant, DisplayName = "Progress viewer", Email = $"{actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = tenant, Name = "Progress test", IsActive = true };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Persons.Add(person);
        db.RegisteredSystems.Add(system);
        db.OrganizationMemberships.Add(new OrganizationMembership { TenantId = tenant, PersonId = person.Id,
            DirectoryTenantId = _directory, ObjectId = actor, GrantedBy = "test" });
        db.SystemRoleAssignments.Add(new SystemRoleAssignment { TenantId = tenant, PersonId = person.Id,
            RegisteredSystemId = system.Id, Role = role });
        await db.SaveChangesAsync();
        return (actor, system.Id);
    }

    [Fact]
    public async Task RealRoute_PollingCannotBypassProgressTenantIsolation()
    {
        // Arrange
        var viewer = await SeedAsync(WorkspaceMembershipFactory.TenantAId, OrganizationRole.MissionOwner);
        var foreign = await SeedAsync(WorkspaceMembershipFactory.TenantBId, OrganizationRole.MissionOwner);
        var id = Guid.NewGuid().ToString();
        factory.Services.GetRequiredService<ScanImportStatusTracker>().Register(id, foreign.System);
        using var client = Client(viewer.Actor, WorkspaceMembershipFactory.TenantAId);

        // Act
        var response = await client.GetAsync($"/api/dashboard/systems/{foreign.System}/scans/import/{id}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(OrganizationRole.MissionOwner, HttpStatusCode.Forbidden)]
    [InlineData(OrganizationRole.Isso, HttpStatusCode.OK)]
    public async Task RealRoute_OnlyAssessmentOperatorsCanCancel(OrganizationRole role, HttpStatusCode expected)
    {
        // Arrange
        var actor = await SeedAsync(WorkspaceMembershipFactory.TenantAId, role);
        var id = Guid.NewGuid().ToString();
        var state = factory.Services.GetRequiredService<ScanImportStatusTracker>().Register(id, actor.System);
        using var client = Client(actor.Actor, WorkspaceMembershipFactory.TenantAId);

        // Act
        var response = await client.DeleteAsync($"/api/dashboard/systems/{actor.System}/scans/import/{id}");

        // Assert
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        state.CancelRequested.Should().Be(expected == HttpStatusCode.OK);
    }
}

using System.Net;
using System.Net.Http.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>Verifies the legacy REST designation writer through the real production HTTP pipeline.</summary>
public sealed class CapabilityResponsibilityAuthorizationTests(WorkspaceMembershipFactory factory)
    : IClassFixture<WorkspaceMembershipFactory>
{
    [Theory]
    [InlineData(OrganizationRole.Isso, HttpStatusCode.OK)]
    [InlineData(OrganizationRole.Issm, HttpStatusCode.OK)]
    [InlineData(OrganizationRole.MissionOwner, HttpStatusCode.Forbidden)]
    [InlineData(OrganizationRole.AuthorizingOfficial, HttpStatusCode.Forbidden)]
    [InlineData(OrganizationRole.Assessor, HttpStatusCode.Forbidden)]
    [InlineData(OrganizationRole.Administrator, HttpStatusCode.Forbidden)]
    public async Task RestInheritance_RequiresAssignedIssoOrIssm(OrganizationRole role, HttpStatusCode expected)
    {
        // Arrange
        using var client = factory.CreateClient();
        var tenant = WorkspaceMembershipFactory.TenantAId;
        var directory = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var person = new Person { TenantId = tenant, DisplayName = "Synthetic reviewer", Email = $"{actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = tenant, Name = $"Responsibility {actor:N}", CreatedBy = "fixture" };
        var baseline = new ControlBaseline { TenantId = tenant, RegisteredSystemId = system.Id, BaselineLevel = "Moderate",
            ControlIds = ["AU-6"], TotalControls = 1, CreatedBy = "fixture" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.AddRange(person, system, baseline);
            db.OrganizationMemberships.Add(new() { TenantId = tenant, PersonId = person.Id,
                DirectoryTenantId = directory, ObjectId = actor, GrantedBy = "fixture" });
            if (role == OrganizationRole.Administrator)
                db.OrganizationRoleAssignments.Add(new() { TenantId = tenant, PersonId = person.Id, Role = role });
            else
                db.SystemRoleAssignments.Add(new() { TenantId = tenant, PersonId = person.Id, RegisteredSystemId = system.Id, Role = role });
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Test-Tid", directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", tenant.ToString());

        // Act
        var response = await client.PutAsJsonAsync($"/api/dashboard/systems/{system.Id}/inheritance", new
        {
            designations = new[] { new { controlId = "AU-6", inheritanceType = "Shared",
                provider = "Synthetic provider", customerResponsibility = "Review synthetic alerts" } }
        });

        // Assert
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await verify.ControlInheritances.CountAsync(i => i.ControlBaselineId == baseline.Id))
            .Should().Be(expected == HttpStatusCode.OK ? 1 : 0);
    }
}

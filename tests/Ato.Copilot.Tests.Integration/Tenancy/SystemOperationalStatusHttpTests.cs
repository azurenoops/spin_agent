using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class SystemOperationalStatusHttpTests(MultiTenantWebApplicationFactory<McpProgram> factory)
{
    [Fact]
    public async Task ExplicitStatus_UsesLifecycleServiceAndRetainsAuditWithoutAuthorization()
    {
        // Arrange
        var systemId = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();
        var url = $"/api/dashboard/systems/{systemId}/operational-status";
        // Act
        var before = await client.GetFromJsonAsync<JsonElement>(url);
        var saved = await client.PutAsJsonAsync(url, new { operationalStatus = "UnderDevelopment" });
        var after = await client.GetFromJsonAsync<JsonElement>(url);
        // Assert
        before.GetProperty("operationalStatus").ValueKind.Should().Be(JsonValueKind.Null);
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        after.GetProperty("operationalStatus").GetString().Should().Be("UnderDevelopment");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.AuditLogs.AnyAsync(x => x.Action == "System.OperationalStatusUpdated")).Should().BeTrue();
        (await db.AuthorizationDecisions.AnyAsync(x => x.RegisteredSystemId == systemId)).Should().BeFalse();
        (await db.RegisteredSystems.SingleAsync(x => x.Id == systemId)).CurrentRmfStep.Should().Be(RmfPhase.Prepare);
    }

    [Fact]
    public async Task UnknownStatusReaderAndForeignTenant_AreRejected()
    {
        // Arrange
        var systemId = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();
        var url = $"/api/dashboard/systems/{systemId}/operational-status";
        // Act
        var invalid = await client.PutAsJsonAsync(url, new { operationalStatus = "999" });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.SystemRoleAssignments.SingleAsync(x => x.RegisteredSystemId == systemId)).Role = OrganizationRole.Assessor;
            await db.SaveChangesAsync();
        }
        var read = await client.GetFromJsonAsync<JsonElement>(url);
        var denied = await client.PutAsJsonAsync(url, new { operationalStatus = "Operational" });
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var foreign = await client.GetAsync(url);
        // Assert
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        read.GetProperty("canManage").GetBoolean().Should().BeFalse();
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    private async Task<string> SeedAsync(OrganizationRole role)
    {
        var tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var person = Guid.NewGuid();
        factory.ResetLegacyTenantContext(tenant);
        factory.GetActiveContext().PersonId = person;
        factory.GetActiveContext().IsWorkspaceRequest = true;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Persons.Add(new() { Id = person, TenantId = tenant, DisplayName = "Synthetic manager", Email = $"{person}@example.invalid" });
        db.OrganizationMemberships.Add(new() { TenantId = tenant, PersonId = person, DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "test" });
        var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic operational status", CreatedBy = "test" };
        db.RegisteredSystems.Add(system);
        db.SystemRoleAssignments.Add(new() { TenantId = tenant, PersonId = person, RegisteredSystemId = system.Id, Role = role });
        await db.SaveChangesAsync();
        return system.Id;
    }
}

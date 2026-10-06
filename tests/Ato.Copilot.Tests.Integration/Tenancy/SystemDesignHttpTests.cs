using System.Net;
using System.Net.Http.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class SystemDesignHttpTests(MultiTenantWebApplicationFactory<McpProgram> factory)
{
    [Fact]
    public async Task ComponentServiceUse_DraftEndpointRetainsProviderSourceAndEnforcesAreaVersionAndTenant()
    {
        // Arrange
        var id = await SeedAsync(OrganizationRole.SystemOwner);
        var tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var profile = Guid.NewGuid();
        var component = Guid.NewGuid();
        var area = Guid.NewGuid().ToString();
        string revision;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.CspProfiles.Add(new() { Id = profile, DisplayName = "Synthetic Flankspeed" });
            db.CspInheritedComponents.Add(new() { Id = component, CspProfileId = profile, Name = "Azure Backup",
                Description = "Synthetic provider-owned backup source; recovery results not verified.",
                Status = Ato.Copilot.Core.Models.Tenancy.CspInheritedComponentStatus.Published });
            db.AuthorizationBoundaryDefinitions.Add(new() { Id = area, TenantId = tenant, RegisteredSystemId = id, Name = "Mission API" });
            await db.SaveChangesAsync();
            var options = await scope.ServiceProvider.GetRequiredService<Ato.Copilot.Core.Interfaces.Workspaces.IWorkspaceOperationsService>()
                .GetSystemComponentPlacementsAsync(tenant, id, "provider", component.ToString(),
                    new(true, false, false, false, false, false), default);
            revision = options.SourceRevision;
        }
        using var client = factory.CreateClient();
        var path = $"/api/dashboard/systems/{id}/design";
        var request = new SaveComponentScopeRequest(0, "provider", component.ToString(), revision, "Included", area, "Recorded use; no restore result implied.");
        // Act
        var invalidArea = await client.PutAsJsonAsync(path + "/component-scope", request with { BoundaryId = "foreign-area" });
        var saved = await client.PutAsJsonAsync(path + "/component-scope", request);
        var stale = await client.PutAsJsonAsync(path + "/component-scope", request);
        // Assert
        invalidArea.StatusCode.Should().Be(HttpStatusCode.BadRequest, await invalidArea.Content.ReadAsStringAsync());
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var graph = await client.GetFromJsonAsync<SystemDesignGraph>(path);
        graph!.ComponentScopes.Single().BoundaryName.Should().Be("Mission API");
        graph.GovernanceStatus.Should().Be("Draft");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.CspInheritedComponents.FindAsync(component))!.Description.Should().Be("Synthetic provider-owned backup source; recovery results not verified.");
            db.BoundaryComponentAssignments.Any(x => x.CspInheritedComponentId == component).Should().BeFalse();
        }
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var foreign = await client.PutAsJsonAsync(path + "/component-scope", request with { ExpectedRevision = 1 });
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ComponentScope_AssessorCannotCreateDraft()
    {
        // Arrange
        var id = await SeedAsync(OrganizationRole.Assessor);
        using var client = factory.CreateClient();
        // Act
        var result = await client.PutAsJsonAsync($"/api/dashboard/systems/{id}/design/component-scope",
            new SaveComponentScopeRequest(0, "local", "unreadable", "source", "Excluded", null, "No permission"));
        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden, await result.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RecordedBuild_IsPersistedVersionCheckedAndDoesNotGrantAuthoringToAssessor()
    {
        // Arrange
        var id = await SeedAsync(OrganizationRole.SystemOwner);
        using var client = factory.CreateClient();
        var path = $"/api/dashboard/systems/{id}/design";
        // Act
        var built = await client.PostAsJsonAsync(path + "/build", new DesignRevisionRequest(0, "Build recorded design"));
        var stale = await client.PostAsJsonAsync(path + "/build", new DesignRevisionRequest(0, "Stale build"));
        var graph = await client.GetFromJsonAsync<SystemDesignGraph>(path);
        // Assert
        built.StatusCode.Should().Be(HttpStatusCode.OK, await built.Content.ReadAsStringAsync());
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        graph!.GovernanceStatus.Should().Be("Draft");
        graph.Revision.Should().Be(1);
        (await client.GetFromJsonAsync<DesignHistoryEntry[]>(path + "/history"))!.Single().Action.Should().Be("BuildFromRecorded");

        var assessorId = await SeedAsync(OrganizationRole.Assessor);
        var denied = await client.PostAsJsonAsync($"/api/dashboard/systems/{assessorId}/design/build",
            new DesignRevisionRequest(0, "Denied build"));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var foreign = await client.PostAsJsonAsync(path + "/build", new DesignRevisionRequest(1, "Foreign build"));
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
        db.Persons.Add(new() { Id = person, TenantId = tenant, DisplayName = "Synthetic design actor", Email = $"{person}@example.invalid" });
        db.OrganizationMemberships.Add(new() { TenantId = tenant, PersonId = person,
            DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "test" });
        var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic design HTTP", CreatedBy = "test" };
        db.RegisteredSystems.Add(system);
        db.SystemRoleAssignments.Add(new() { TenantId = tenant, PersonId = person, RegisteredSystemId = system.Id, Role = role });
        await db.SaveChangesAsync();
        return system.Id;
    }

    [Fact]
    public async Task AssignedOwner_SavesReloads_AndReceivesVersionConflictEnvelope()
    {
        // Arrange
        var id = await SeedAsync(OrganizationRole.SystemOwner);
        using var client = factory.CreateClient();
        var path = $"/api/dashboard/systems/{id}/design";
        var initial = await client.GetFromJsonAsync<SystemDesignGraph>(path);
        var request = new SaveSystemDesignRequest(initial!.Revision, initial.Nodes, initial.Edges, initial.Groups, "Synthetic HTTP design save");
        // Act
        var saved = await client.PutAsJsonAsync(path, request);
        var stale = await client.PutAsJsonAsync(path, request);
        var reload = await client.GetFromJsonAsync<SystemDesignGraph>(path);
        var approved = await client.GetAsync(path + "/approved");
        // Assert
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).Should().Contain("DESIGN_VERSION_STALE");
        reload!.GovernanceStatus.Should().Be("Draft");
        reload.Revision.Should().Be(1);
        (await approved.Content.ReadAsStringAsync()).Should().Be("null");
    }

    [Fact]
    public async Task AssessorCannotWrite_AndForeignTenantCannotRead()
    {
        // Arrange
        var id = await SeedAsync(OrganizationRole.Assessor);
        using var client = factory.CreateClient();
        var path = $"/api/dashboard/systems/{id}/design";
        var initial = await client.GetFromJsonAsync<SystemDesignGraph>(path);
        // Act
        var denied = await client.PutAsJsonAsync(path,
            new SaveSystemDesignRequest(0, initial!.Nodes, [], [], "Assessor cannot author"));
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var foreign = await client.GetAsync(path);
        // Assert
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, await denied.Content.ReadAsStringAsync());
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

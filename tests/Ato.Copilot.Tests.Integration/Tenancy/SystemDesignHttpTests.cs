using System.Net;
using System.Net.Http.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class SystemDesignHttpTests(MultiTenantWebApplicationFactory<McpProgram> factory)
{
    [Theory]
    [InlineData("Logical")]
    [InlineData("AzureDeployment")]
    public async Task DetailedViewLayout_IsIndependentlyVersionedAndAuthorized(string view)
    {
        // Arrange
        var id = await SeedAsync(OrganizationRole.SystemOwner);
        using var client = factory.CreateClient();
        var path = $"/api/dashboard/systems/{id}/design";
        var graph = await client.GetFromJsonAsync<SystemDesignGraph>(path)
            ?? throw new InvalidOperationException("Design graph was not returned.");
        var layout = await client.GetFromJsonAsync<DesignLayout>(path + "/layout/" + view)
            ?? throw new InvalidOperationException("Design layout was not returned.");
        var request = new SaveDesignLayoutRequest(0, layout with
            { Positions = new() { [graph.Nodes[0].Id] = new(25, 70) } });
        // Act
        var save = await client.PutAsJsonAsync(path + "/layout", request);
        var reload = await client.GetFromJsonAsync<DesignLayout>(path + "/layout/" + view);
        var sibling = await client.GetFromJsonAsync<DesignLayout>(path + "/layout/Context");
        var stale = await client.PutAsJsonAsync(path + "/layout", request);
        // Assert
        save.StatusCode.Should().Be(HttpStatusCode.OK, await save.Content.ReadAsStringAsync());
        reload!.View.Should().Be(view);
        reload.Version.Should().Be(1);
        reload.Positions[graph.Nodes[0].Id].Should().Be(new DesignPosition(25, 70));
        sibling!.Version.Should().Be(0);
        (await client.GetFromJsonAsync<SystemDesignGraph>(path))!.Revision.Should().Be(0);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var assessorId = await SeedAsync(OrganizationRole.Assessor);
        var denied = await client.PutAsJsonAsync($"/api/dashboard/systems/{assessorId}/design/layout", request);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        (await client.GetAsync(path + "/layout/" + view)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ComponentServiceUse_DraftEndpointRetainsProviderSourceAndEnforcesAreaVersionAndTenant()
    {
        // Arrange
        var id = await SeedAsync(OrganizationRole.SystemOwner);
        var tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var component = Guid.NewGuid();
        var area = Guid.NewGuid().ToString();
        string revision;
        int deploymentProfileCount;
        CspProfile? hostingProfile;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var profile = await db.CspProfiles.SingleAsync();
            db.CspInheritedComponents.Add(new() { Id = component, CspProfileId = profile.Id, Name = "Azure Backup",
                Description = "Synthetic provider-owned backup source; recovery results not verified.",
                Status = Ato.Copilot.Core.Models.Tenancy.CspInheritedComponentStatus.Published });
            db.AuthorizationBoundaryDefinitions.Add(new() { Id = area, TenantId = tenant, RegisteredSystemId = id, Name = "Mission API" });
            await db.SaveChangesAsync();
            deploymentProfileCount = await db.CspProfiles.CountAsync();
            scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(CspProfileService.CacheKey);
            hostingProfile = await scope.ServiceProvider.GetRequiredService<ICspProfileService>().GetAsync();
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
        deploymentProfileCount.Should().Be(1, "provider components must reuse the singleton deployment profile");
        hostingProfile.Should().NotBeNull();
        hostingProfile!.OnboardingState.Should().Be(OnboardingState.Active,
            "provider-source arrangement must not block later shared-fixture requests after cache expiry");
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

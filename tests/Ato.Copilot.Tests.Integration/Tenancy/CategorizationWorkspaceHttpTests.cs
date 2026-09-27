using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class CategorizationWorkspaceHttpTests : IClassFixture<WorkspaceMembershipFactory>, IDisposable
{
    private readonly WebApplicationFactory<McpProgram> _factory;
    private static readonly Guid DirectoryId = Guid.Parse("079ca000-0000-0000-0000-000000000001");
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;
    private static readonly Guid OtherTenant = WorkspaceMembershipFactory.TenantBId;

    public CategorizationWorkspaceHttpTests(WorkspaceMembershipFactory factory)
    {
        Environment.GetEnvironmentVariable("ATO_TEST_SQLSERVER_CONNSTRING").Should().BeNullOrEmpty(
            "this focused HTTP regression uses local SQLite, not an external database or Docker");
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType != typeof(TenancySeedHostedService)).ToArray())
                services.Remove(descriptor);
            services.AddTransient<IStartupFilter, LegacyRoleClaimsFilter>();
        }));
    }

    private HttpClient Client(Guid actor)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", DirectoryId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        return client;
    }

    private async Task<(Guid Actor, string SystemId)> SeedAsync(OrganizationRole role, bool foreign = false)
    {
        using var initialized = _factory.CreateClient();
        var actor = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Database.IsSqlite().Should().BeTrue();
        var person = new Person { TenantId = Tenant, DisplayName = "Assigned reviewer", Email = $"{actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = foreign ? OtherTenant : Tenant, Name = $"HTTP test {actor:N}",
            CurrentRmfStep = RmfPhase.Categorize, HostingEnvironment = "Local test", CreatedBy = "fixture", IsActive = true };
        db.Persons.Add(person); db.RegisteredSystems.Add(system);
        await db.SaveChangesAsync();
        db.OrganizationMemberships.Add(new() { TenantId = Tenant, DirectoryTenantId = DirectoryId,
            ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture" });
        if (!foreign) db.SystemRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id,
            RegisteredSystemId = system.Id, Role = role, IsInherited = false });
        else db.OrganizationRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id, Role = role });
        await db.SaveChangesAsync();
        return (actor, system.Id);
    }

    private static object Categorization() => new
    {
        isNationalSecuritySystem = false, justification = "Reviewed synthetic mission impact",
        informationTypes = new[] { new { sp80060Id = "C.2.1", name = "Synthetic mission support",
            category = "Services", confidentialityImpact = "Low", integrityImpact = "Low",
            availabilityImpact = "Low", usesProvisional = true } },
    };

    [Fact]
    public async Task AssignedIssm_CanCategorizeAndSelectBaselineThroughProductionHttp()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(fixture.Actor);
        var root = $"/api/dashboard/systems/{fixture.SystemId}";

        // Act
        var categorized = await client.PostAsJsonAsync($"{root}/categorization", Categorization());

        // Assert
        categorized.StatusCode.Should().Be(HttpStatusCode.OK, await categorized.Content.ReadAsStringAsync());
        var category = await categorized.Content.ReadFromJsonAsync<JsonElement>();
        category.GetProperty("nistBaseline").GetString().Should().Be("Low");

        // Act
        var selected = await client.PostAsJsonAsync($"{root}/baseline", new { applyOverlay = false });

        // Assert
        selected.StatusCode.Should().Be(HttpStatusCode.OK, await selected.Content.ReadAsStringAsync());
        var baseline = await selected.Content.ReadFromJsonAsync<JsonElement>();
        baseline.GetProperty("baselineLevel").GetString().Should().Be("Low");
        baseline.GetProperty("totalControls").GetInt32().Should().BeGreaterThan(0);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var persisted = await db.ControlBaselines.SingleAsync(item => item.RegisteredSystemId == fixture.SystemId);
        persisted.CreatedBy.Should().Be(fixture.Actor.ToString());
        (await db.SecurityCategorizations.SingleAsync(item => item.RegisteredSystemId == fixture.SystemId))
            .CategorizedBy.Should().Be(fixture.Actor.ToString());
    }

    [Theory]
    [InlineData(OrganizationRole.MissionOwner, "categorization")]
    [InlineData(OrganizationRole.MissionOwner, "baseline")]
    [InlineData(OrganizationRole.Isso, "categorization")]
    [InlineData(OrganizationRole.Isso, "baseline")]
    [InlineData(OrganizationRole.Assessor, "categorization")]
    [InlineData(OrganizationRole.Assessor, "baseline")]
    public async Task ReadAuthorizedNonManagers_CannotWrite(OrganizationRole role, string operation)
    {
        // Arrange
        var fixture = await SeedAsync(role);
        using var client = Client(fixture.Actor);

        // Act
        var response = await client.PostAsJsonAsync($"/api/dashboard/systems/{fixture.SystemId}/{operation}",
            operation == "categorization" ? Categorization() : new { applyOverlay = false });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("WORKSPACE_OPERATION_NOT_AUTHORIZED");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SecurityCategorizations.AnyAsync(item => item.RegisteredSystemId == fixture.SystemId)).Should().BeFalse();
        (await db.ControlBaselines.AnyAsync(item => item.RegisteredSystemId == fixture.SystemId)).Should().BeFalse();
    }

    [Theory]
    [InlineData("categorization")]
    [InlineData("baseline")]
    public async Task AssignedOrganizationIssm_CannotWriteAnotherTenantSystem(string operation)
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.Issm, foreign: true);
        using var client = Client(fixture.Actor);

        // Act
        var response = await client.PostAsJsonAsync($"/api/dashboard/systems/{fixture.SystemId}/{operation}",
            operation == "categorization" ? Categorization() : new { applyOverlay = false });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SecurityCategorizations.AnyAsync(item => item.RegisteredSystemId == fixture.SystemId)).Should().BeFalse();
        (await db.ControlBaselines.AnyAsync(item => item.RegisteredSystemId == fixture.SystemId)).Should().BeFalse();
    }

    [Theory]
    [InlineData(ComplianceRoles.SecurityLead)]
    [InlineData(ComplianceRoles.Viewer)]
    public async Task HeaderlessMembershipRequest_CannotBypassPersistedAssignmentWithGlobalRole(string role)
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.MissionOwner);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.Tenants.SingleAsync(item => item.Id == Tenant)).EntraTenantId = DirectoryId;
            await db.SaveChangesAsync();
        }
        using var client = Client(fixture.Actor);
        client.DefaultRequestHeaders.Remove("X-Workspace-Kind");
        client.DefaultRequestHeaders.Remove("X-Workspace-Mode");
        client.DefaultRequestHeaders.Remove("X-Workspace-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Legacy-Compliance-Role", role);

        // Act
        var categorized = await client.PostAsJsonAsync($"/api/dashboard/systems/{fixture.SystemId}/categorization", Categorization());
        var baseline = await client.PostAsJsonAsync($"/api/dashboard/systems/{fixture.SystemId}/baseline", new { applyOverlay = false });

        // Assert
        categorized.StatusCode.Should().Be(HttpStatusCode.Forbidden, await categorized.Content.ReadAsStringAsync());
        baseline.StatusCode.Should().Be(HttpStatusCode.Forbidden, await baseline.Content.ReadAsStringAsync());
        (await categorized.Content.ReadAsStringAsync()).Should().Contain("WORKSPACE_OPERATION_NOT_AUTHORIZED",
            "the real membership resolver selects the actor's workspace even without explicit workspace headers");
    }

    private sealed class LegacyRoleClaimsFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (http, run) =>
            {
                if (http.Request.Headers.TryGetValue("X-Legacy-Compliance-Role", out var role)
                    && http.User.Identity is ClaimsIdentity identity)
                    identity.AddClaim(new Claim(ClaimTypes.Role, role.ToString()));
                await run();
            });
            next(app);
        };
    }

    public void Dispose() => _factory.Dispose();
}

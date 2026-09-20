using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public class BoundaryComponentTenantIsolationTests
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantA = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
    private readonly Guid _tenantB = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;

    public BoundaryComponentTenantIsolationTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SetTenant(_tenantA);
    }

    [Fact]
    public async Task GetCandidates_ReturnsEligibleTenantAndPublishedCspComponentsOnly()
    {
        // Arrange
        var seed = await SeedBoundaryDataAsync();
        SetTenant(_tenantA);

        // Act
        var response = await _client.GetAsync(
            $"/api/dashboard/systems/{seed.SystemAId}/boundary-definitions/{seed.BoundaryAId}/component-candidates");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().Contain(item => item.GetProperty("name").GetString() == seed.TenantAPlaceName);
        items.Should().Contain(item => item.GetProperty("name").GetString() == seed.PublishedCspName
            && item.GetProperty("source").GetString() == "CSP");
        items.Should().NotContain(item => item.GetProperty("name").GetString() == seed.TenantAPersonName);
        items.Should().NotContain(item => item.GetProperty("name").GetString() == seed.TenantBThingName);
        items.Should().NotContain(item => item.GetProperty("name").GetString() == seed.DraftCspName);
    }

    [Fact]
    public async Task AssignPerson_DirectRequest_ReturnsBadRequestAndCreatesNothing()
    {
        // Arrange
        var seed = await SeedBoundaryDataAsync();
        SetTenant(_tenantA);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/dashboard/systems/{seed.SystemAId}/boundary-definitions/{seed.BoundaryAId}/components",
            new { componentId = seed.TenantAPersonId, source = "Organization", isInScope = true });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var created = await db.BoundaryComponentAssignments
            .IgnoreQueryFilters()
            .AnyAsync(a => a.AuthorizationBoundaryDefinitionId == seed.BoundaryAId
                && a.SystemComponentId == seed.TenantAPersonId);
        created.Should().BeFalse();
    }

    [Fact]
    public async Task AssignComponent_ThroughDifferentSystemRoute_ReturnsNotFound()
    {
        // Arrange
        var seed = await SeedBoundaryDataAsync();
        SetTenant(_tenantA);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/dashboard/systems/{seed.OtherSystemAId}/boundary-definitions/{seed.BoundaryAId}/components",
            new { componentId = seed.TenantAPlaceId, source = "Organization", isInScope = true });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssignmentLifecycle_ThroughDifferentSystemRoute_ReturnsNotFoundAndPreservesAssignment()
    {
        // Arrange
        var seed = await SeedBoundaryDataAsync();
        var assignmentId = $"assignment-{Guid.NewGuid():N}";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.BoundaryComponentAssignments.Add(new BoundaryComponentAssignment
            {
                Id = assignmentId,
                TenantId = _tenantA,
                AuthorizationBoundaryDefinitionId = seed.BoundaryAId,
                SystemComponentId = seed.TenantAPlaceId,
                IsInScope = true,
                CreatedBy = "test",
            });
            await db.SaveChangesAsync();
        }
        SetTenant(_tenantA);
        var route = $"/api/dashboard/systems/{seed.OtherSystemAId}/boundary-definitions/{seed.BoundaryAId}/components";

        // Act
        var listResponse = await _client.GetAsync(route);
        var updateResponse = await _client.PutAsJsonAsync(
            $"{route}/{assignmentId}",
            new { isInScope = false, exclusionRationale = "Out of scope" });
        var deleteResponse = await _client.DeleteAsync($"{route}/{assignmentId}");

        // Assert
        listResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var assignment = await verificationDb.BoundaryComponentAssignments
            .IgnoreQueryFilters()
            .SingleAsync(a => a.Id == assignmentId);
        assignment.IsInScope.Should().BeTrue();
    }

    private void SetTenant(Guid tenantId)
    {
        var context = _factory.GetActiveContext();
        context.TenantId = tenantId;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.Status = TenantStatus.Active;
    }

    private async Task<BoundarySeed> SeedBoundaryDataAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var systemAId = $"sys-a-{suffix}";
        var otherSystemAId = $"sys-a-other-{suffix}";
        var systemBId = $"sys-b-{suffix}";
        var boundaryAId = $"bnd-a-{suffix}";
        var tenantAPlaceId = $"place-a-{suffix}";
        var tenantAPersonId = $"person-a-{suffix}";
        var tenantBThingId = $"thing-b-{suffix}";
        var tenantAPlaceName = $"Tenant A Place {suffix}";
        var tenantAPersonName = $"Tenant A Person {suffix}";
        var tenantBThingName = $"Tenant B Thing {suffix}";
        var publishedCspName = $"Published CSP {suffix}";
        var draftCspName = $"Draft CSP {suffix}";

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var cspProfileId = await db.CspProfiles.IgnoreQueryFilters().Select(p => p.Id).FirstAsync();

        db.RegisteredSystems.AddRange(
            CreateSystem(systemAId, _tenantA, $"Tenant A System {suffix}"),
            CreateSystem(otherSystemAId, _tenantA, $"Tenant A Other {suffix}"),
            CreateSystem(systemBId, _tenantB, $"Tenant B System {suffix}"));
        db.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            Id = boundaryAId,
            TenantId = _tenantA,
            RegisteredSystemId = systemAId,
            Name = $"Boundary {suffix}",
            BoundaryType = BoundaryDefinitionType.Logical,
            IsPrimary = true,
            CreatedBy = "test",
        });
        db.SystemComponents.AddRange(
            CreateComponent(tenantAPlaceId, tenantAPlaceName, ComponentType.Place, _tenantA),
            CreateComponent(tenantAPersonId, tenantAPersonName, ComponentType.Person, _tenantA),
            CreateComponent(tenantBThingId, tenantBThingName, ComponentType.Thing, _tenantB));
        db.CspInheritedComponents.AddRange(
            CreateCspComponent(cspProfileId, publishedCspName, CspInheritedComponentStatus.Published),
            CreateCspComponent(cspProfileId, draftCspName, CspInheritedComponentStatus.Draft));
        await db.SaveChangesAsync();

        return new BoundarySeed(
            systemAId, otherSystemAId, boundaryAId,
            tenantAPlaceId, tenantAPersonId,
            tenantAPlaceName, tenantAPersonName, tenantBThingName,
            publishedCspName, draftCspName);
    }

    private static RegisteredSystem CreateSystem(string id, Guid tenantId, string name) => new()
    {
        Id = id,
        TenantId = tenantId,
        Name = name,
        SystemType = SystemType.MajorApplication,
        MissionCriticality = MissionCriticality.MissionSupport,
        HostingEnvironment = "Azure Government",
        CreatedBy = "test",
    };

    private static SystemComponent CreateComponent(
        string id, string name, ComponentType type, Guid tenantId) => new()
    {
        Id = id,
        TenantId = tenantId,
        Name = name,
        ComponentType = type,
        Status = ComponentStatus.Active,
        CreatedBy = "test",
    };

    private static CspInheritedComponent CreateCspComponent(
        Guid profileId, string name, CspInheritedComponentStatus status) => new()
    {
        CspProfileId = profileId,
        Name = name,
        Description = name,
        ComponentType = CspComponentType.Service,
        SourceFormat = SourceFormat.Manual,
        Status = status,
        ImportedBy = "test",
    };

    private sealed record BoundarySeed(
        string SystemAId,
        string OtherSystemAId,
        string BoundaryAId,
        string TenantAPlaceId,
        string TenantAPersonId,
        string TenantAPlaceName,
        string TenantAPersonName,
        string TenantBThingName,
        string PublishedCspName,
        string DraftCspName);
}
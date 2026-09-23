using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class ProviderCatalogReadTests(MultiTenantWebApplicationFactory<McpProgram> factory)
{
    [Fact]
    public async Task ManuallyCreatedCapability_NoWorkingRow_BootstrapsWithExpectedRevisionOne()
    {
        // Arrange
        SetContext();
        var seed = await SeedAsync();
        using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync(
            $"/api/csp/inherited-components/{seed.ComponentId}/capabilities",
            new
            {
                name = "New capability awaiting working authoring",
                description = "Source description, not a working baseline",
                mappedNistControlIds = new[] { "AC-2" }, markMappedImmediately = false
            });
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdData = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var capabilityId = createdData.GetProperty("id").GetGuid();
        var route = $"/api/csp/catalog/capabilities/{capabilityId}/working-revision";

        // Act
        var missing = await client.GetAsync(route);
        var invalidBaseline = await client.PutAsJsonAsync(route, new
        {
            expectedRevision = 1, classification = "", serviceCategory = "",
            contributors = Array.Empty<string>(), controlDuties = new Dictionary<string, string>()
        });
        var wrongToken = await client.PutAsJsonAsync(route, new
        {
            expectedRevision = 2, classification = "CUI", serviceCategory = "Identity",
            contributors = Array.Empty<string>(), controlDuties = new Dictionary<string, string>()
        });
        var saved = await client.PutAsJsonAsync(route, new
        {
            expectedRevision = 1, classification = "CUI", serviceCategory = "Identity",
            contributors = Array.Empty<string>(), controlDuties = new Dictionary<string, string>()
        });
        var loaded = await client.GetAsync(route);

        // Assert
        createdData.GetProperty("status").GetString().Should().Be("NeedsReview");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("WORKING_REVISION_NOT_FOUND");
        error.GetProperty("message").GetString().Should().Be("Working revision was not found.");
        invalidBaseline.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        wrongToken.StatusCode.Should().Be(HttpStatusCode.Conflict);
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        loaded.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("capabilityId").GetGuid().Should().Be(capabilityId);
        data.GetProperty("revision").GetInt64().Should().Be(1);
        data.GetProperty("classification").GetString().Should().Be("CUI");
        data.GetProperty("serviceCategory").GetString().Should().Be("Identity");
        data.GetProperty("contributors").GetArrayLength().Should().Be(0);
        data.GetProperty("controlDuties").EnumerateObject().Should().BeEmpty();
        data.GetProperty("approvalState").GetString().Should().Be("NotApproved");
        data.GetProperty("approvedRevision").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("approvedPreviewId").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("snapshotHash").GetString().Should().HaveLength(64);
        (await loaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetRawText()
            .Should().Be(data.GetRawText());
    }

    [Theory]
    [InlineData("overview", false, false)]
    [InlineData("capabilities/00000000-0000-0000-0000-000000000001", false, false)]
    [InlineData("overview", true, true)]
    [InlineData("capabilities/00000000-0000-0000-0000-000000000001", true, true)]
    [InlineData("", true, true)]
    [InlineData("capabilities/00000000-0000-0000-0000-000000000001/subscribers", true, true)]
    public async Task Catalog_DeniesOrganizationAndSupportContexts(string route, bool csp, bool support)
    {
        // Arrange
        SetContext(csp, support);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/csp/catalog{(route.Length == 0 ? "" : $"/{route}")}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Overview_ReturnsPersistedProviderAndPagedSourceProvenanceOnly()
    {
        // Arrange
        SetContext();
        var seed = await SeedAsync();
        using var client = factory.CreateClient();

        // Act
        var first = await client.GetAsync("/api/csp/catalog/overview?pageSize=1");

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("providerName").GetString().Should().Be(seed.ProviderName);
        data.GetProperty("authorizationRecord").ValueKind.Should().Be(JsonValueKind.Null);
        var page = data.GetProperty("sourceArtifacts");
        page.GetProperty("pageSize").GetInt32().Should().Be(1);
        page.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        page.GetProperty("items").GetArrayLength().Should().Be(1);
        var all = await client.GetFromJsonAsync<JsonElement>("/api/csp/catalog/overview?pageSize=200");
        var artifacts = all.GetProperty("data").GetProperty("sourceArtifacts").GetProperty("items").EnumerateArray();
        var source = artifacts.Single(x => x.GetProperty("componentId").GetGuid() == seed.ComponentId);
        source.GetProperty("sourceFileName").GetString().Should().Be("provider-package.zip");
        source.GetProperty("sourceReference").GetString().Should().Be("sha256:provider-package");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_UsesDirectLookupAndResolvesOnlyProviderContributors(bool approved)
    {
        // Arrange
        SetContext();
        var seed = await SeedAsync(approved, beyondFirstPage: true);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/csp/catalog/capabilities/{seed.CapabilityId}");
        var firstPage = await client.GetFromJsonAsync<JsonElement>("/api/csp/catalog?grouping=capability&pageSize=200");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        firstPage.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("capabilityId").GetGuid()).Should().NotContain(seed.CapabilityId);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var item = data.GetProperty("capability");
        item.GetProperty("capabilityId").GetGuid().Should().Be(seed.CapabilityId);
        item.GetProperty("workingApprovalState").GetString().Should().Be(approved ? "Approved" : "NotApproved");
        item.GetProperty("workingRevision").GetInt64().Should().Be(3);
        item.GetProperty("releasedRevision").ValueKind.Should().Be(JsonValueKind.Null);
        var supports = data.GetProperty("supportingComponents").EnumerateArray().ToArray();
        supports.Select(x => x.GetProperty("id").GetString()).Should().BeEquivalentTo(
            seed.ComponentId.ToString(), seed.ContributorId.ToString());
        supports.Select(x => x.GetProperty("componentType").GetString()).Should().BeEquivalentTo("Platform", "Service");
        data.GetProperty("unresolvedContributorIds").EnumerateArray().Select(x => x.GetString())
            .Should().BeEquivalentTo(seed.LocalComponentId.ToString(), "unresolved-provider-id");
        data.GetProperty("sourceArtifacts").GetArrayLength().Should().Be(2);
        data.GetProperty("mappedControlIds").EnumerateArray().Select(x => x.GetString()).Should().Equal("AC-2");
        data.GetProperty("sourceEvidenceReferences").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("implementationNarrative").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("description").GetString().Should().Be("Description, not an implementation narrative");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.ProviderCapabilityReleases.AnyAsync(x => x.CapabilityId == seed.CapabilityId)).Should().BeFalse();
    }

    [Fact]
    public async Task Detail_NoWorkingRowAndNotFound_AreExplicit()
    {
        // Arrange
        SetContext();
        var seed = await SeedAsync();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/csp/catalog/capabilities/{seed.NoWorkingCapabilityId}");
        var missing = await client.GetAsync($"/api/csp/catalog/capabilities/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("capability").GetProperty("workingRevision").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("capability").GetProperty("workingApprovalState").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("supportingComponents").GetArrayLength().Should().Be(1);
        data.GetProperty("unresolvedContributorIds").GetArrayLength().Should().Be(0);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("component")]
    [InlineData("capability")]
    public async Task Catalog_CountsMatchAuthorizedSubscribersAcrossCaseAndTenantQualifiedSystems(string grouping)
    {
        // Arrange
        SetContext();
        var seed = await SeedAsync();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            $"/api/csp/catalog?grouping={grouping}&componentId={seed.ComponentId.ToString().ToUpperInvariant()}&pageSize=1");
        var subscribers = await client.GetFromJsonAsync<JsonElement>(
            $"/api/csp/catalog/capabilities/{seed.CapabilityId}/subscribers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        page.GetProperty("total").GetInt32().Should().Be(grouping == "component" ? 1 : 2);
        var item = page.GetProperty("items")[0];
        item.GetProperty("componentId").GetGuid().Should().Be(seed.ComponentId);
        item.GetProperty("distinctAdoptionCount").GetInt32().Should().Be(3);
        item.GetProperty("distinctOrganizationCount").GetInt32().Should().Be(2);
        var systems = subscribers.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(x => (x.GetProperty("organizationId").GetGuid(), x.GetProperty("systemId").GetString()))
            .Distinct().ToArray();
        systems.Should().HaveCount(3);
        systems.Select(x => x.Item1).Distinct().Should().HaveCount(2);
        if (grouping == "capability")
        {
            item.GetProperty("supportingComponents").GetArrayLength().Should().Be(2);
            var second = await client.GetFromJsonAsync<JsonElement>(
                $"/api/csp/catalog?grouping=capability&componentId={seed.ComponentId}&pageSize=1&page=2");
            second.GetProperty("data").GetProperty("items")[0].GetProperty("capabilityId")
                .GetGuid().Should().Be(seed.NoWorkingCapabilityId);
        }
    }

    [Fact]
    public async Task Catalog_InvalidComponentFilter_ReturnsBadRequest()
    {
        // Arrange
        SetContext();
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/csp/catalog?componentId=not-a-guid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private void SetContext(bool csp = true, bool support = false)
    {
        var context = factory.GetActiveContext();
        context.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        context.IsCspAdmin = csp;
        context.ImpersonatedTenantId = support ? MultiTenantWebApplicationFactory<McpProgram>.TenantBId : null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
    }

    private async Task<Seed> SeedAsync(bool approved = false, bool beyondFirstPage = false)
    {
        await factory.EnsureActiveCspProfileAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var profile = await db.Set<CspProfile>().SingleAsync();
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            profile.DisplayName);
        db.CspInheritedComponents.AddRange(
            new CspInheritedComponent
            {
                Id = seed.ComponentId, CspProfileId = profile.Id, Name = $"Platform {seed.ComponentId}",
                ComponentType = CspComponentType.Platform, Status = CspInheritedComponentStatus.Published,
                SourceFileName = "provider-package.zip", SourceArtifactReference = "sha256:provider-package"
            },
            new CspInheritedComponent
            {
                Id = seed.ContributorId, CspProfileId = profile.Id, Name = "Supporting service",
                ComponentType = CspComponentType.Service, Status = CspInheritedComponentStatus.Draft,
                SourceFileName = "service-ssp.pdf", SourceArtifactReference = "sha256:service-ssp"
            });
        db.CspInheritedCapabilities.AddRange(
            new CspInheritedCapability
            {
                Id = seed.CapabilityId, CspInheritedComponentId = seed.ComponentId, Name = $"Z target {seed.CapabilityId}",
                Description = "Description, not an implementation narrative",
                Status = CspInheritedCapabilityStatus.Mapped, MappedNistControlIds = ["AC-2"]
            },
            new CspInheritedCapability
            {
                Id = seed.NoWorkingCapabilityId, CspInheritedComponentId = seed.ComponentId, Name = "ZZ no working",
                Status = CspInheritedCapabilityStatus.NeedsReview
            });
        if (beyondFirstPage)
            for (var i = 0; i < 205; i++)
                db.CspInheritedCapabilities.Add(new CspInheritedCapability
                {
                    CspInheritedComponentId = seed.ContributorId, Name = $"A earlier {i:D3}",
                    Status = CspInheritedCapabilityStatus.Mapped
                });
        db.ProviderCapabilityWorkingRevisions.Add(new ProviderCapabilityWorkingRevision
        {
            CapabilityId = seed.CapabilityId, Revision = 3,
            ContributorsJson = JsonSerializer.Serialize(new[]
            {
                seed.ComponentId.ToString().ToUpperInvariant(), seed.ContributorId.ToString().ToUpperInvariant(),
                seed.ContributorId.ToString(), seed.LocalComponentId.ToString(), "unresolved-provider-id"
            }),
            ApprovedRevision = approved ? 3 : null, ApprovedPreviewId = approved ? Guid.NewGuid() : null
        });
        var tenantA = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var tenantB = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        db.SystemComponents.Add(new SystemComponent
        {
            Id = seed.LocalComponentId.ToString(), TenantId = tenantB, Name = "Tenant secret must not resolve",
            ComponentType = ComponentType.Thing, CreatedBy = "test"
        });
        var system1 = Guid.NewGuid().ToString();
        var system2 = Guid.NewGuid().ToString();
        db.RegisteredSystems.AddRange(
            new RegisteredSystem { Id = system1, TenantId = tenantA, Name = "A1", CreatedBy = "test" },
            new RegisteredSystem { Id = system2, TenantId = tenantA, Name = "A2", CreatedBy = "test" },
            new RegisteredSystem { Id = $"b-{system1}", TenantId = tenantB, Name = "B1", CreatedBy = "test" });
        await db.SaveChangesAsync();
        foreach (var (tenant, system, reference, active) in new[]
        {
            (tenantA, system1, seed.CapabilityId.ToString(), true),
            (tenantA, system1, seed.CapabilityId.ToString().ToUpperInvariant(), true),
            (tenantA, system2, seed.CapabilityId.ToString().ToUpperInvariant(), true),
            (tenantB, $"b-{system1}", seed.CapabilityId.ToString(), true),
            (tenantB, system1, seed.CapabilityId.ToString(), true),
            (Guid.NewGuid(), system1, seed.CapabilityId.ToString(), true),
            (tenantA, system2, seed.CapabilityId.ToString(), false)
        })
            db.CapabilitySubscriptions.Add(new CapabilitySubscription
            {
                Id = Guid.NewGuid().ToString(), RoutingTenantId = tenant, RegisteredSystemId = system,
                CspInheritedCapabilityId = reference, IsActive = active
            });
        await db.SaveChangesAsync();
        return seed;
    }

    private sealed record Seed(Guid ComponentId, Guid CapabilityId, Guid ContributorId,
        Guid NoWorkingCapabilityId, Guid LocalComponentId, string ProviderName);
}

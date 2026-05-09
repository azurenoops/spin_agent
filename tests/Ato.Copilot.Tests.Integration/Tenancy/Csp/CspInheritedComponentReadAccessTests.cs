using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy.Csp;

/// <summary>
/// T191 [US9]: confirms the CSP-inherited components and their capabilities are
/// readable across every tenant (FR-104, FR-105) but every write operation is
/// gated to <c>CSP.Admin</c> (FR-106). A Mission Owner authenticated against
/// Tenant A SHOULD see Published rows seeded against the singleton CSP profile,
/// SHOULD NOT see Draft / Archived rows, and SHOULD receive 403 on every
/// mutator endpoint.
/// </summary>
/// <remarks>
/// RED until T203 + T205 + T208 (services + endpoints) are implemented. Until
/// then every endpoint returns 404 and these tests fail at the status-code
/// assertion.
/// </remarks>
[Collection("Tenancy")]
public class CspInheritedComponentReadAccessTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private const string ListUrl = "/api/csp/inherited-components";

    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;
    private readonly Guid _publishedComponentId;
    private readonly Guid _draftComponentId;
    private readonly Guid _archivedComponentId;
    private readonly Guid _publishedCapabilityId;

    public CspInheritedComponentReadAccessTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        // Mission Owner persona in Tenant A: NOT CSP-Admin.
        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = false;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;

        _publishedComponentId = Guid.NewGuid();
        _draftComponentId = Guid.NewGuid();
        _archivedComponentId = Guid.NewGuid();
        _publishedCapabilityId = Guid.NewGuid();

        SeedComponentsAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_List_AsMissionOwner_Returns200_WithOnlyPublishedRows()
    {
        // Act
        var resp = await _client.GetAsync(ListUrl);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "Mission Owners across every tenant must be able to list published CSP-inherited components (FR-104)");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var items = body.GetProperty("data").GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);

        var ids = new HashSet<Guid>();
        foreach (var item in items.EnumerateArray())
        {
            ids.Add(item.GetProperty("id").GetGuid());
            item.GetProperty("status").GetString()
                .Should().Be("Published",
                    "non-CSP-Admins must never see Draft or Archived components");
        }
        ids.Should().Contain(_publishedComponentId);
        ids.Should().NotContain(_draftComponentId);
        ids.Should().NotContain(_archivedComponentId);
    }

    [Fact]
    public async Task Get_Capabilities_AsMissionOwner_OnPublishedComponent_Returns200()
    {
        // Act
        var resp = await _client.GetAsync($"{ListUrl}/{_publishedComponentId}/capabilities");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        body.GetProperty("data").GetArrayLength().Should().BeGreaterThan(0);
    }

    // ───────────── Mutator endpoints — Mission Owner gets 403 ─────────────

    [Fact]
    public async Task Patch_Component_AsMissionOwner_Returns403()
    {
        // Arrange
        var payload = JsonContent.Create(new { name = "Renamed By Mission Owner" });

        // Act
        var resp = await _client.PatchAsync($"{ListUrl}/{_publishedComponentId}", payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    [Fact]
    public async Task Delete_Component_AsMissionOwner_Returns403()
    {
        // Act
        var resp = await _client.DeleteAsync($"{ListUrl}/{_publishedComponentId}");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_Publish_AsMissionOwner_Returns403()
    {
        // Act
        var resp = await _client.PostAsync(
            $"{ListUrl}/{_draftComponentId}/publish",
            content: null);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_Remap_AsMissionOwner_Returns403()
    {
        // Arrange
        var payload = JsonContent.Create(new { replaceMapped = false });

        // Act
        var resp = await _client.PostAsync($"{ListUrl}/{_publishedComponentId}/remap", payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Patch_CapabilityReview_AsMissionOwner_Returns403()
    {
        // Arrange
        var payload = JsonContent.Create(new
        {
            mappedNistControlIds = new[] { "AC-2" },
            reviewerNote = "should be blocked",
        });

        // Act
        var resp = await _client.PatchAsync(
            $"{ListUrl}/{_publishedComponentId}/capabilities/{_publishedCapabilityId}/review",
            payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ───────────────────────────── Helpers ───────────────────────────────

    private async Task SeedComponentsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Find the singleton CSP profile id (seeded by the fixture's hosted
        // service in MultiTenant mode). Falls back to a local stub if the
        // fixture has not yet seeded one.
        var profileId = await db.Set<CspProfile>().IgnoreQueryFilters()
            .Select(p => p.Id)
            .FirstOrDefaultAsync();
        if (profileId == Guid.Empty)
        {
            profileId = Guid.NewGuid();
        }

        // Idempotently insert the three component statuses + a capability
        // for the Published component. Use raw SQL so we can run BEFORE the
        // entity model is finalised by T197 (the model IS in place after
        // T197, but using SQL keeps this seed independent of EF caching).
        await db.Database.ExecuteSqlRawAsync(
            $"""
            INSERT OR IGNORE INTO "CspInheritedComponents"
                ("Id","CspProfileId","Name","Description","ComponentType",
                 "SourceFormat","Status","ImportedAt","ImportedBy")
            VALUES
                ('{_publishedComponentId}','{profileId}','Published-Comp','desc',2,0,1,'{DateTime.UtcNow:O}','seed'),
                ('{_draftComponentId}','{profileId}','Draft-Comp','desc',2,0,0,'{DateTime.UtcNow:O}','seed'),
                ('{_archivedComponentId}','{profileId}','Archived-Comp','desc',2,0,2,'{DateTime.UtcNow:O}','seed');
            INSERT OR IGNORE INTO "CspInheritedCapabilities"
                ("Id","CspInheritedComponentId","Name","Description",
                 "MappedNistControlIds","Status","MappedBy","CreatedAt","CreatedBy")
            VALUES
                ('{_publishedCapabilityId}','{_publishedComponentId}','Cap-1','desc',
                 '["AC-2"]',0,0,'{DateTime.UtcNow:O}','seed');
            """);
    }
}

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
/// T033 [Feature 050 / US3] — integration coverage for
/// <c>GET /api/csp/inherited-components/{componentId}/capabilities/{capabilityId}/history</c>.
/// </summary>
/// <remarks>
/// <para>Behavior pinned in
/// <c>specs/050-csp-capability-lifecycle/contracts/http-api.md § 3</c>:</para>
/// <list type="bullet">
///   <item>200 with <c>{ items, page, pageSize, total }</c> ordered
///         <c>OccurredAt DESC, Id DESC</c>.</item>
///   <item>Empty history → 200 with <c>items: []</c> (NOT 404).</item>
///   <item><c>pageSize</c> clamped to <c>[1, 200]</c> server-side.</item>
///   <item><c>metadata</c> returned as JSON object (NOT a JSON-encoded string).</item>
///   <item>403 <c>FORBIDDEN_NOT_CSP_ADMIN</c> for non-admin caller.</item>
///   <item>404 when capability does not belong to caller's tenant or
///         supplied source component (existence-leak guard).</item>
/// </list>
/// </remarks>
[Collection("Tenancy")]
public class ListCapabilityHistoryEndpointTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private const string BaseUrl = "/api/csp/inherited-components";

    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public ListCapabilityHistoryEndpointTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_History_HappyPath_ReturnsPaginatedEnvelope_OrderedDesc_WithMetadataAsObject()
    {
        // Arrange
        SetCspAdmin(true);
        var componentId = await SeedComponentAsync("History Component");
        var capabilityId = await SeedCapabilityWithHistoryAsync(componentId);

        // Act
        var resp = await _client.GetAsync($"{BaseUrl}/{componentId}/capabilities/{capabilityId}/history");

        // Assert — envelope
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var data = body.GetProperty("data");
        data.GetProperty("page").GetInt32().Should().Be(1);
        data.GetProperty("pageSize").GetInt32().Should().Be(50);
        data.GetProperty("total").GetInt32().Should().Be(3);
        var items = data.GetProperty("items");
        items.GetArrayLength().Should().Be(3);

        // Ordering: Created (oldest) → Edited → Reviewed (newest).
        // Assert reverse-chrono order via the seed timestamps:
        // item[0] = newest = Reviewed; item[2] = oldest = Created.
        items[0].GetProperty("eventType").GetString().Should().Be("Reviewed");
        items[1].GetProperty("eventType").GetString().Should().Be("Edited");
        items[2].GetProperty("eventType").GetString().Should().Be("Created");

        // Metadata must be a JSON object (NOT a string). The Reviewed row
        // carries `reviewerNote`.
        var reviewed = items[0];
        reviewed.GetProperty("metadata").ValueKind.Should().Be(JsonValueKind.Object,
            "metadata MUST be parsed JSON, never a stringified JSON value.");
        reviewed.GetProperty("metadata").GetProperty("reviewerNote").GetString()
            .Should().Be("Approved.");

        // Cache-Control: no-store (clients re-fetch on each open).
        resp.Headers.CacheControl.Should().NotBeNull();
        resp.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Get_History_EmptyResult_Returns200_WithEmptyItems()
    {
        // Arrange
        SetCspAdmin(true);
        var componentId = await SeedComponentAsync("Empty History Component");
        var capabilityId = await SeedBareCapabilityAsync(componentId);

        // Act
        var resp = await _client.GetAsync($"{BaseUrl}/{componentId}/capabilities/{capabilityId}/history");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "empty history is NOT a 404 — capability exists, just no rows yet.");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        data.GetProperty("total").GetInt32().Should().Be(0);
        data.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Get_History_PageSizeAboveMax_ClampsTo200()
    {
        // Arrange
        SetCspAdmin(true);
        var componentId = await SeedComponentAsync("Clamp PageSize");
        var capabilityId = await SeedBareCapabilityAsync(componentId);

        // Act
        var resp = await _client.GetAsync(
            $"{BaseUrl}/{componentId}/capabilities/{capabilityId}/history?pageSize=999");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("pageSize").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task Get_History_PageSizeZero_ClampsTo1()
    {
        // Arrange
        SetCspAdmin(true);
        var componentId = await SeedComponentAsync("Clamp PageSizeZero");
        var capabilityId = await SeedBareCapabilityAsync(componentId);

        // Act
        var resp = await _client.GetAsync(
            $"{BaseUrl}/{componentId}/capabilities/{capabilityId}/history?pageSize=0");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("pageSize").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Get_History_AsNonCspAdmin_Returns403_ForbiddenNotCspAdmin()
    {
        // Arrange
        SetCspAdmin(true);
        var componentId = await SeedComponentAsync("History 403");
        var capabilityId = await SeedBareCapabilityAsync(componentId);
        SetCspAdmin(false);

        // Act
        var resp = await _client.GetAsync($"{BaseUrl}/{componentId}/capabilities/{capabilityId}/history");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    [Fact]
    public async Task Get_History_CapabilityNotUnderSourceComponent_Returns404()
    {
        // Arrange — capability lives under "other"; caller asks via "source".
        SetCspAdmin(true);
        var sourceId = await SeedComponentAsync("History Source");
        var otherId  = await SeedComponentAsync("History Other");
        var capabilityId = await SeedBareCapabilityAsync(otherId);

        // Act
        var resp = await _client.GetAsync(
            $"{BaseUrl}/{sourceId}/capabilities/{capabilityId}/history");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private void SetCspAdmin(bool isCspAdmin)
    {
        var ctx = _factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = isCspAdmin;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;
    }

    private async Task<Guid> SeedComponentAsync(string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var profileId = await db.Set<CspProfile>().IgnoreQueryFilters()
            .Select(p => p.Id).FirstOrDefaultAsync();
        if (profileId == Guid.Empty) profileId = Guid.NewGuid();
        var id = Guid.NewGuid();
        db.CspInheritedComponents.Add(new CspInheritedComponent
        {
            Id = id,
            CspProfileId = profileId,
            Name = name,
            Description = "seed",
            ComponentType = CspComponentType.Service,
            SourceFormat = SourceFormat.Manual,
            Status = CspInheritedComponentStatus.Published,
            ImportedAt = DateTimeOffset.UtcNow,
            ImportedBy = "seed",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedBareCapabilityAsync(Guid componentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var id = Guid.NewGuid();
        db.CspInheritedCapabilities.Add(new CspInheritedCapability
        {
            Id = id,
            CspInheritedComponentId = componentId,
            Name = "Bare cap",
            Description = "no history",
            MappedNistControlIds = new List<string> { "AC-2" },
            MappedBy = MappedBy.User,
            Status = CspInheritedCapabilityStatus.Mapped,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "seed",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedCapabilityWithHistoryAsync(Guid componentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var capabilityId = Guid.NewGuid();

        db.CspInheritedCapabilities.Add(new CspInheritedCapability
        {
            Id = capabilityId,
            CspInheritedComponentId = componentId,
            Name = "History cap",
            Description = "seeded with 3 history rows",
            MappedNistControlIds = new List<string> { "AC-2" },
            MappedBy = MappedBy.User,
            Status = CspInheritedCapabilityStatus.Mapped,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            CreatedBy = "seed",
        });

        // Three rows — Created (oldest), Edited (middle), Reviewed (newest).
        var nowUtc = DateTimeOffset.UtcNow;
        db.CapabilityHistoryEvents.AddRange(
            new CapabilityHistoryEvent
            {
                Id = Guid.NewGuid(),
                CapabilityId = capabilityId,
                TenantId = tenantId,
                EventType = CapabilityHistoryEventType.Created,
                ActorOid = "creator-oid",
                OccurredAt = nowUtc.AddMinutes(-30),
                Summary = "Capability manually created.",
                MetadataJson = null,
            },
            new CapabilityHistoryEvent
            {
                Id = Guid.NewGuid(),
                CapabilityId = capabilityId,
                TenantId = tenantId,
                EventType = CapabilityHistoryEventType.Edited,
                ActorOid = "editor-oid",
                OccurredAt = nowUtc.AddMinutes(-20),
                Summary = "Capability edited.",
                MetadataJson = "{\"fields\":[\"name\"]}",
            },
            new CapabilityHistoryEvent
            {
                Id = Guid.NewGuid(),
                CapabilityId = capabilityId,
                TenantId = tenantId,
                EventType = CapabilityHistoryEventType.Reviewed,
                ActorOid = "reviewer-oid",
                OccurredAt = nowUtc.AddMinutes(-10),
                Summary = "Reviewed and approved.",
                MetadataJson = "{\"reviewerNote\":\"Approved.\"}",
            });
        await db.SaveChangesAsync();
        return capabilityId;
    }
}

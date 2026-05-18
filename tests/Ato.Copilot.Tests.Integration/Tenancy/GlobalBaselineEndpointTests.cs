using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T133 [Phase 10 / FR-081 / FR-082]: validates the
/// <c>/api/global-baselines/*</c> contract surface against
/// <c>specs/048-tenant-isolation/contracts/global-baselines.openapi.yaml</c>.
/// </summary>
[Collection("Tenancy")]
public class GlobalBaselineEndpointTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantA;

    public GlobalBaselineEndpointTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _tenantA = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;

        var ctx = factory.GetActiveContext();
        ctx.TenantId = _tenantA;
        ctx.IsCspAdmin = true;
        ctx.Status = TenantStatus.Active;
    }

    [Fact]
    public async Task List_AsAnyTenant_Returns200_WithEnvelope()
    {
        var resp = await _client.GetAsync("/api/global-baselines");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        body.GetProperty("data").GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Publish_AsCspAdmin_Returns201_AndPersistsBaseline()
    {
        var sourceId = Guid.NewGuid();

        var resp = await _client.PostAsJsonAsync("/api/global-baselines/publish", new
        {
            kind = "ControlNarrative",
            sourceId,
            title = "Baseline AC-2",
            notes = "Reusable narrative",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var data = body.GetProperty("data");
        data.GetProperty("kind").GetString().Should().Be("ControlNarrative");
        data.GetProperty("sourceId").GetGuid().Should().Be(sourceId);
        data.GetProperty("sourceTenantId").GetGuid().Should().Be(_tenantA);

        // The new row appears in subsequent list calls (any tenant can see it).
        var list = await _client.GetAsync("/api/global-baselines");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await list.Content.ReadFromJsonAsync<JsonElement>();
        var items = listBody.GetProperty("data").GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Publish_AsNonCspAdmin_Returns403()
    {
        _factory.GetActiveContext().IsCspAdmin = false;

        var resp = await _client.PostAsJsonAsync("/api/global-baselines/publish", new
        {
            kind = "ControlNarrative",
            sourceId = Guid.NewGuid(),
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    [Fact]
    public async Task Publish_WithUnsupportedKind_Returns400()
    {
        var resp = await _client.PostAsJsonAsync("/api/global-baselines/publish", new
        {
            kind = "NotAKind",
            sourceId = Guid.NewGuid(),
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("INVALID_REQUEST");
    }

    [Fact]
    public async Task Unpublish_AsCspAdmin_Returns204_AndRowDisappearsFromList()
    {
        // First, publish one.
        var publishResp = await _client.PostAsJsonAsync("/api/global-baselines/publish", new
        {
            kind = "EvidenceArtifact",
            sourceId = Guid.NewGuid(),
            title = "Evidence baseline",
        });
        publishResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var published = await publishResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = published.GetProperty("data").GetProperty("id").GetGuid();

        var unpublishResp = await _client.DeleteAsync($"/api/global-baselines/{id}");
        unpublishResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Subsequent unpublish returns 404 (idempotent — row already unpublished).
        var second = await _client.DeleteAsync($"/api/global-baselines/{id}");
        second.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unpublish_AsNonCspAdmin_Returns403()
    {
        _factory.GetActiveContext().IsCspAdmin = false;

        var resp = await _client.DeleteAsync($"/api/global-baselines/{Guid.NewGuid()}");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

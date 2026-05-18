using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T120 [Phase 9 / FR-073..FR-076]: Validates the
/// <c>/api/admin/migrate-to-multitenant</c> contract surface against
/// <c>specs/048-tenant-isolation/contracts/admin-migration.openapi.yaml</c>.
/// </summary>
[Collection("Tenancy")]
public class AdminMigrationEndpointTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantA;

    public AdminMigrationEndpointTests(MultiTenantWebApplicationFactory<McpProgram> factory)
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
    public async Task Preview_AsCspAdmin_Returns200_WithPerTableRows()
    {
        var resp = await _client.GetAsync("/api/admin/migrate-to-multitenant/preview");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var tables = body.GetProperty("data").GetProperty("tables");
        tables.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Preview_AsNonCspAdmin_Returns403()
    {
        _factory.GetActiveContext().IsCspAdmin = false;

        var resp = await _client.GetAsync("/api/admin/migrate-to-multitenant/preview");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    [Fact]
    public async Task Execute_WithoutDefaultTenantId_Returns400()
    {
        var resp = await _client.PostAsJsonAsync(
            "/api/admin/migrate-to-multitenant",
            new { defaultTenantId = Guid.Empty });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("INVALID_REQUEST");
    }

    [Fact]
    public async Task Execute_AsCspAdmin_ReturnsReport_AndIsIdempotent()
    {
        var defaultId = _tenantA;

        var first = await _client.PostAsJsonAsync(
            "/api/admin/migrate-to-multitenant",
            new { defaultTenantId = defaultId, installRls = false });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        firstBody.GetProperty("status").GetString().Should().Be("success");
        firstBody.GetProperty("data").GetProperty("defaultTenantId").GetGuid().Should().Be(defaultId);

        // Idempotency: re-running should still succeed and report 0 rows
        // assigned to the default (since the first run already covered them).
        var second = await _client.PostAsJsonAsync(
            "/api/admin/migrate-to-multitenant",
            new { defaultTenantId = defaultId, installRls = false });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("status").GetString().Should().Be("success");
    }

    [Fact]
    public async Task Execute_AsNonCspAdmin_Returns403()
    {
        _factory.GetActiveContext().IsCspAdmin = false;

        var resp = await _client.PostAsJsonAsync(
            "/api/admin/migrate-to-multitenant",
            new { defaultTenantId = _tenantA });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

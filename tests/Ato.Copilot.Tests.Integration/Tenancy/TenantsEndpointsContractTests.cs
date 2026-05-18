using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T062 [US2]: Validates the <c>/api/tenants</c> contract surface against
/// <c>specs/048-tenant-isolation/contracts/tenants.openapi.yaml</c>.
/// Exercises status codes, the <c>Envelope</c> shape, and idempotency on
/// <c>POST /api/tenants</c>.
/// </summary>
/// <remarks>
/// RED until T070 (TenantsEndpoints.cs) is implemented. We deliberately
/// assert on the HTTP-level surface so the test couples to the contract,
/// not the internal service. <see cref="MultiTenantWebApplicationFactory{TStartup}"/>
/// already seeds Tenant A and Tenant B; the FakeTenantContext is configured
/// with <c>IsCspAdmin = true</c> for these tests so the listing endpoint is
/// reachable.
/// </remarks>
[Collection("Tenancy")]
public class TenantsEndpointsContractTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public TenantsEndpointsContractTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.Status = TenantStatus.Active;
    }

    [Fact]
    public async Task Get_Tenants_AsCspAdmin_Returns200_WithEnvelopedList()
    {
        var resp = await _client.GetAsync("/api/tenants");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        body.GetProperty("metadata").GetProperty("executionTimeMs").GetInt64().Should().BeGreaterOrEqualTo(0);
        body.GetProperty("data").GetProperty("items").GetArrayLength().Should().BeGreaterOrEqualTo(2,
            "the fixture seeds at least two tenants");
    }

    [Fact]
    public async Task Get_Tenants_AsNonCspAdmin_Returns403_ForbiddenNotCspAdmin()
    {
        _factory.GetActiveContext().IsCspAdmin = false;

        var resp = await _client.GetAsync("/api/tenants");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString().Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    [Fact]
    public async Task Get_Tenant_ById_OutsideScope_Returns404_NotFound()
    {
        // Non-CSP-Admin asking for a tenant other than their own → 404 (not 403),
        // per contract: "Not found OR tenant exists but is outside caller's scope
        // (we do not leak existence)".
        _factory.GetActiveContext().IsCspAdmin = false;

        var resp = await _client.GetAsync($"/api/tenants/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_Tenant_AsCspAdmin_FirstCall_Returns201_SubsequentCall_Returns200_Idempotent()
    {
        var entraTid = Guid.NewGuid();
        var request = new
        {
            entraTenantId = entraTid,
            displayName = $"Idempotent-{entraTid:N}".Substring(0, 32),
        };

        var first = await _client.PostAsJsonAsync("/api/tenants", request);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        firstBody.GetProperty("status").GetString().Should().Be("success");
        var firstId = firstBody.GetProperty("data").GetProperty("id").GetString();

        var second = await _client.PostAsJsonAsync("/api/tenants", request);
        second.StatusCode.Should().Be(HttpStatusCode.OK,
            "POST /api/tenants is idempotent on entraTenantId per contract");
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("data").GetProperty("id").GetString().Should().Be(firstId,
            "the same entraTenantId must resolve to the same row");
    }

    [Fact]
    public async Task Patch_TenantStatus_AsNonCspAdmin_Returns403()
    {
        _factory.GetActiveContext().IsCspAdmin = false;

        var resp = await _client.PatchAsJsonAsync(
            $"/api/tenants/{MultiTenantWebApplicationFactory<McpProgram>.TenantAId}/status",
            new { status = "Suspended", reason = "test" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_Impersonate_AsNonCspAdmin_Returns403()
    {
        _factory.GetActiveContext().IsCspAdmin = false;

        var resp = await _client.PostAsync(
            $"/api/tenants/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}/impersonate", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_Impersonation_WhenNoCookie_Returns204()
    {
        var resp = await _client.DeleteAsync("/api/tenants/impersonation");

        // Per contract: deleting an absent cookie still returns 204.
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}

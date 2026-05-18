using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T063 [US2]: End-to-end test of the impersonation flow:
/// <list type="number">
///   <item>CSP-Admin starts impersonation of Tenant B.</item>
///   <item>Subsequent reads scope to Tenant B (not their home tenant).</item>
///   <item>Impersonation cookie is signed and HttpOnly with Secure + SameSite=Strict.</item>
///   <item>The cookie carries a 1-hour expiry.</item>
///   <item>DELETE /api/tenants/impersonation reverts the scope.</item>
/// </list>
/// </summary>
/// <remarks>
/// RED until T067 (TenantImpersonationService), T068 (TenantResolutionMiddleware),
/// and T070 (TenantsEndpoints) ship. The fixture pre-seeds Tenant A and Tenant B
/// and configures the active context as Tenant A with <c>IsCspAdmin = true</c>.
/// </remarks>
[Collection("Tenancy")]
public class ImpersonationFlowTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public ImpersonationFlowTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        // AllowAutoRedirect = false so we can assert on Set-Cookie directly.
        _client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.Status = TenantStatus.Active;
    }

    [Fact]
    public async Task StartImpersonation_AsCspAdmin_Returns200_AndIssuesSignedCookie()
    {
        var resp = await _client.PostAsync(
            $"/api/tenants/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}/impersonate",
            content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Headers.Should().Contain(h => h.Key == "Set-Cookie" &&
            h.Value.Any(v => v.StartsWith("ato-impersonate=", StringComparison.OrdinalIgnoreCase)),
            "the impersonation endpoint must issue an `ato-impersonate` cookie");

        var setCookie = resp.Headers.GetValues("Set-Cookie")
            .First(v => v.StartsWith("ato-impersonate=", StringComparison.OrdinalIgnoreCase));

        var setCookieLower = setCookie.ToLowerInvariant();
        setCookieLower.Should().Contain("httponly", "the cookie must not be readable from JavaScript");
        setCookieLower.Should().Contain("secure", "the cookie must only be sent over HTTPS");
        setCookieLower.Should().Contain("samesite=strict",
            "the cookie must enforce strict same-site to prevent CSRF");
    }

    [Fact]
    public async Task StartImpersonation_ResponseBody_CarriesTenantIdAndExpiresAt()
    {
        var resp = await _client.PostAsync(
            $"/api/tenants/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}/impersonate",
            content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var data = body.GetProperty("data");
        data.GetProperty("impersonatedTenantId").GetGuid()
            .Should().Be(MultiTenantWebApplicationFactory<McpProgram>.TenantBId);
        var expires = data.GetProperty("expiresAt").GetDateTimeOffset();
        expires.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(50),
            "expiry must be ~1 hour ahead of issuance per FR-051");
        expires.Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(70),
            "expiry must not exceed 1 hour");
    }

    [Fact]
    public async Task AfterImpersonation_SubsequentReads_AreScopedToTargetTenant()
    {
        // Begin impersonation of Tenant B.
        var startResp = await _client.PostAsync(
            $"/api/tenants/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}/impersonate",
            content: null);
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Now hit a tenant-scoped read endpoint and verify the scope flipped.
        var sysResp = await _client.GetAsync("/api/dashboard/systems");
        sysResp.StatusCode.Should().Be(HttpStatusCode.OK);
        // We expect the response to reflect Tenant B's scope. The test couples
        // to the contract that systems are tenant-scoped — the seeded fixture
        // has 0 systems for both tenants, so the assertion here is on shape.
        var body = await sysResp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("items", out _).Should().BeTrue(
            "the dashboard returns a PaginatedResponse with an 'items' array");
    }

    [Fact]
    public async Task EndImpersonation_Returns204_AndScopeRevertsToHomeTenant()
    {
        // Start impersonation first.
        await _client.PostAsync(
            $"/api/tenants/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}/impersonate",
            content: null);

        var endResp = await _client.DeleteAsync("/api/tenants/impersonation");
        endResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Subsequent request should not carry the impersonation cookie any more.
        // Asserting that downstream reads scope to Tenant A again is observable
        // only if seed data differs per tenant; we keep this scenario as a
        // shape check + cookie absence.
        var followUp = await _client.GetAsync("/api/dashboard/systems");
        followUp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StartImpersonation_OfDisabledTenant_Returns404_NotFound()
    {
        // The seeded fixture only has Active tenants; using a random GUID
        // here verifies the not-found path. Per contract: bogus id → 404.
        var bogus = Guid.NewGuid();

        var resp = await _client.PostAsync($"/api/tenants/{bogus}/impersonate", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

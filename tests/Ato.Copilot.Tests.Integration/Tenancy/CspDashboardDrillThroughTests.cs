using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T176 [US8]: simulates a CSP-Admin clicking a tenant row in the dashboard
/// (acceptance scenario 5). The click translates to
/// <c>POST /api/tenants/{id}/impersonate</c>; the response MUST issue a
/// signed <c>ato-impersonate</c> cookie carrying the target tenant id, and
/// the subsequent <c>GET /api/dashboard/systems</c> MUST be reachable
/// (proving the impersonation cookie did not break the read path).
/// </summary>
/// <remarks>
/// <para>
/// This test asserts the <em>contract</em> of the drill-through (the same
/// shape the dashboard JS will rely on): impersonate POST returns 200 +
/// envelope + signed cookie, then a tenant-scoped read endpoint still
/// returns 200 with a paginated <c>items</c> array.
/// </para>
/// <para>
/// The <em>EF query filter</em> that scopes the read to the impersonated
/// tenant is verified by <see cref="TenantQueryFilterTests"/> at the unit
/// level (which pushes <c>ITenantContextAccessor</c> directly so the filter
/// engages). The HTTP integration fixture pins
/// <c>Tenant:Resolution:BypassForTests=true</c> so the production middleware
/// does not consume the impersonation cookie — same pattern as
/// <see cref="ImpersonationFlowTests"/>.
/// </para>
/// </remarks>
[Collection("Tenancy")]
public class CspDashboardDrillThroughTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspDashboardDrillThroughTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;

        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task DashboardRowClick_ImpersonatesTenant_AndSubsequentDashboardReadIsReachable()
    {
        // Arrange — Tenant A has 1 system, Tenant B has 2 systems (see seed).
        var tenantBId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;

        // Act — the row click POSTs the impersonate endpoint.
        var startResp = await _client.PostAsync(
            $"/api/tenants/{tenantBId}/impersonate", content: null);

        // Assert — POST returns the canonical envelope with the target tenant id.
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var startBody = await startResp.Content.ReadFromJsonAsync<JsonElement>();
        startBody.GetProperty("status").GetString().Should().Be("success");
        startBody.GetProperty("data").GetProperty("impersonatedTenantId").GetGuid()
            .Should().Be(tenantBId,
                "the impersonate response must echo the click target so the dashboard JS can update its banner.");

        // Assert — the impersonation cookie was issued (CSRF-safe attributes
        // checked by ImpersonationFlowTests; here we only verify presence).
        startResp.Headers.Should().Contain(
            h => h.Key == "Set-Cookie"
                 && h.Value.Any(v => v.StartsWith("ato-impersonate=", System.StringComparison.OrdinalIgnoreCase)),
            "FR-051: dashboard drill-through MUST receive a signed `ato-impersonate` cookie.");

        // Act — drill into the per-tenant dashboard (the same path the
        // dashboard JS uses to render Tenant B's portfolio after the click).
        var sysResp = await _client.GetAsync("/api/dashboard/systems");

        // Assert — read endpoint stays reachable and returns the paginated
        // shape the dashboard JS expects. (Row-level scope is enforced by
        // the EF query filter exercised in TenantQueryFilterTests.)
        sysResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var sysBody = await sysResp.Content.ReadFromJsonAsync<JsonElement>();
        sysBody.TryGetProperty("items", out _).Should().BeTrue(
            "drill-through GET /api/dashboard/systems must return a PaginatedResponse with an 'items' array.");
    }

    private async Task SeedAsync()
    {
        var factory = _factory.Services.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await factory.CreateDbContextAsync();

        // Idempotent reset of just the system rows we own — keep tenants and
        // CspProfile intact for the shared fixture.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"RegisteredSystems\";");

        var aId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var bId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;

        db.RegisteredSystems.AddRange(
            new RegisteredSystem
            {
                TenantId = aId,
                Name = "A-System-1",
                SystemType = SystemType.MajorApplication,
                MissionCriticality = MissionCriticality.MissionEssential,
                HostingEnvironment = "Azure Government",
            },
            new RegisteredSystem
            {
                TenantId = bId,
                Name = "B-System-1",
                SystemType = SystemType.MajorApplication,
                MissionCriticality = MissionCriticality.MissionEssential,
                HostingEnvironment = "Azure Government",
            },
            new RegisteredSystem
            {
                TenantId = bId,
                Name = "B-System-2",
                SystemType = SystemType.Enclave,
                MissionCriticality = MissionCriticality.MissionSupport,
                HostingEnvironment = "Azure Government",
            });

        await db.SaveChangesAsync();
    }
}

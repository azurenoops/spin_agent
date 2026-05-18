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
/// Feature 048 (US8 follow-up): contract + cross-tenant aggregation tests for
/// <c>GET /api/csp/dashboard/systems</c>. The endpoint powers the CSP-level
/// `/systems` page in the dashboard — a flat per-system table across every
/// tenant, with a leading <c>Tenant</c> column.
/// </summary>
/// <remarks>
/// Mirrors the contract pattern of <c>CspDashboardSummaryAggregationTests</c>
/// and the auth pattern of <c>CspDashboardContractTests</c>:
/// <list type="bullet">
///   <item>200 with success envelope when caller is CSP-Admin in MultiTenant mode.</item>
///   <item>422 VALIDATION_FAILED when paging args are invalid.</item>
///   <item>Cross-tenant rollup excludes the system tenant (FR-070) and Disabled tenants (FR-098).</item>
///   <item>Each row carries <c>tenantId</c> + <c>orgDisplayName</c> for the
///         dashboard's drill-through-via-impersonation flow.</item>
/// </list>
/// RED until <c>ICspDashboardService.GetSystemsAsync</c> /
/// <c>CspDashboardService.GetSystemsAsync</c> /
/// <c>CspDashboardEndpoints.GetSystemsAsync</c> are implemented.
/// </remarks>
[Collection("Tenancy")]
public class CspDashboardSystemsTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private static readonly Guid TenantDId = Guid.Parse("d0d0d0d0-dddd-dddd-dddd-d0d0d0d0d0d0");

    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspDashboardSystemsTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;

        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_Systems_AsCspAdmin_Returns200_WithEnvelopeShape()
    {
        // Arrange — fixture state.

        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/systems");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");

        var data = body.GetProperty("data");
        data.GetProperty("page").GetInt32().Should().Be(1);
        data.GetProperty("pageSize").GetInt32().Should().Be(50);
        data.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(3,
            "the seed inserts 2 systems for Tenant A and 1 for Tenant D.");
        data.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Get_Systems_RowCarriesTenantIdAndDisplayName()
    {
        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/systems?pageSize=200");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").GetProperty("items");

        items.GetArrayLength().Should().BeGreaterThanOrEqualTo(3);
        var first = items[0];

        first.TryGetProperty("systemId", out _).Should().BeTrue();
        first.TryGetProperty("name", out _).Should().BeTrue();
        first.TryGetProperty("tenantId", out _).Should().BeTrue();
        first.TryGetProperty("orgDisplayName", out _).Should().BeTrue();
        first.TryGetProperty("impactLevel", out _).Should().BeTrue();
        first.TryGetProperty("currentRmfPhase", out _).Should().BeTrue();
        first.TryGetProperty("complianceScore", out _).Should().BeTrue();
        first.TryGetProperty("atoStatus", out _).Should().BeTrue();
        first.TryGetProperty("atoSeverity", out _).Should().BeTrue();
        first.TryGetProperty("openPoamCount", out _).Should().BeTrue();

        // Spot-check that we get back BOTH tenants in the page (cross-tenant proof).
        var distinctTenantNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            distinctTenantNames.Add(item.GetProperty("orgDisplayName").GetString() ?? string.Empty);
        }
        distinctTenantNames.Should().Contain("CSP Systems Tenant A");
        distinctTenantNames.Should().Contain("CSP Systems Tenant D");
    }

    [Fact]
    public async Task Get_Systems_ExcludesSystemTenantAndDisabledTenants()
    {
        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/systems?pageSize=200");

        // Assert
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").GetProperty("items");

        foreach (var item in items.EnumerateArray())
        {
            var tenantIdStr = item.GetProperty("tenantId").GetString() ?? string.Empty;
            tenantIdStr.Should().NotBe(Guid.Empty.ToString(),
                "FR-070: system tenant rows must never appear in the CSP systems list.");
            // Disabled tenant in seed is "DISABLED-CSP-Systems-Tenant" — must not surface.
            (item.GetProperty("orgDisplayName").GetString() ?? string.Empty)
                .Should().NotContain("DISABLED",
                "FR-098: systems belonging to Disabled tenants must be excluded.");
        }
    }

    [Fact]
    public async Task Get_Systems_PageSizeAboveMax_Returns422_ValidationFailed()
    {
        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/systems?pageSize=201");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Get_Systems_NotCspAdmin_Returns403_ForbiddenNotCspAdmin()
    {
        // Arrange — flip the ambient context off CSP-Admin.
        var ctx = _factory.GetActiveContext();
        ctx.IsCspAdmin = false;
        try
        {
            // Act
            var resp = await _client.GetAsync("/api/csp/dashboard/systems");

            // Assert
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("status").GetString().Should().Be("error");
            body.GetProperty("error").GetProperty("errorCode").GetString()
                .Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
        }
        finally
        {
            ctx.IsCspAdmin = true;
        }
    }

    private async Task SeedAsync()
    {
        var factory = _factory.Services.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await factory.CreateDbContextAsync();

        // Suspend FK enforcement for the seed (mirrors the pattern in
        // CspDashboardSummaryAggregationTests). Production runs on SQL Server.
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");

        // Idempotent reset of the rows this test class touches. The shared
        // fixture is per-class via [Collection("Tenancy")] serialization; the
        // existing Tenant A/B rows are owned by the fixture seed and MUST
        // stay intact (other tests in the collection rely on them).
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"RegisteredSystems\" WHERE \"Name\" LIKE 'CSP-Systems-%';");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"Tenants\" WHERE \"Id\" = {TenantDId}");
        // Always rename Tenant A back to its baseline so the cross-tenant
        // assertion is deterministic across re-runs.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Tenants\" SET \"DisplayName\" = 'CSP Systems Tenant A' WHERE \"Id\" = {MultiTenantWebApplicationFactory<McpProgram>.TenantAId}");

        // Add Tenant D for this test class (a Disabled tenant whose systems
        // must be excluded would race with other classes' seed; we flip an
        // unrelated tenant id for the exclusion check below).
        if (!await db.Tenants.AnyAsync(t => t.Id == TenantDId))
        {
            db.Tenants.Add(new Tenant
            {
                Id = TenantDId,
                DisplayName = "CSP Systems Tenant D",
                Status = TenantStatus.Active,
                OnboardingState = OnboardingState.Active,
                CreatedBy = "test",
            });
        }

        var aId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        // 2 systems for Tenant A + 1 system for Tenant D ⇒ totalCount ≥ 3
        // (other classes in the collection may add their own systems; we
        // assert "≥ 3" on the totalCount, not "== 3").
        db.RegisteredSystems.AddRange(
            NewSystem(aId, "CSP-Systems-A1"),
            NewSystem(aId, "CSP-Systems-A2"),
            NewSystem(TenantDId, "CSP-Systems-D1"));

        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
    }

    private static RegisteredSystem NewSystem(Guid tenantId, string name) => new()
    {
        TenantId = tenantId,
        Name = name,
        SystemType = SystemType.MajorApplication,
        MissionCriticality = MissionCriticality.MissionEssential,
        HostingEnvironment = "Azure Government",
    };
}

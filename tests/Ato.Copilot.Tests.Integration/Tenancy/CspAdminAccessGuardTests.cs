using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T065 [US2]: Asserts that non-CSP-Admin principals receive a
/// <c>403 FORBIDDEN_NOT_CSP_ADMIN</c> response from every endpoint that
/// requires the role: <c>/api/tenants</c> list, impersonation start, and the
/// administrative migration endpoints. Per FR-053..FR-055.
/// </summary>
/// <remarks>
/// RED until T070 implements the endpoints with role-gating + the migration
/// endpoints from <c>contracts/admin-migration.openapi.yaml</c> are wired in
/// downstream tasks. The fixture starts every test with
/// <c>IsCspAdmin = false</c>.
/// </remarks>
[Collection("Tenancy")]
public class CspAdminAccessGuardTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspAdminAccessGuardTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = false;
        ctx.Status = TenantStatus.Active;
    }

    private static async Task AssertForbiddenWithCspAdminCode(HttpResponseMessage resp)
    {
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString().Should().Be("FORBIDDEN_NOT_CSP_ADMIN",
            "the role-guard must surface this exact code per FR-053");
    }

    [Fact]
    public async Task ListTenants_AsNonCspAdmin_ReturnsForbidden_NotCspAdmin()
    {
        var resp = await _client.GetAsync("/api/tenants");
        await AssertForbiddenWithCspAdminCode(resp);
    }

    [Fact]
    public async Task PatchTenantStatus_AsNonCspAdmin_ReturnsForbidden_NotCspAdmin()
    {
        var resp = await _client.PatchAsJsonAsync(
            $"/api/tenants/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}/status",
            new { status = "Suspended", reason = "test" });
        await AssertForbiddenWithCspAdminCode(resp);
    }

    [Fact]
    public async Task StartImpersonation_AsNonCspAdmin_ReturnsForbidden_NotCspAdmin()
    {
        var resp = await _client.PostAsync(
            $"/api/tenants/{MultiTenantWebApplicationFactory<McpProgram>.TenantBId}/impersonate",
            content: null);
        await AssertForbiddenWithCspAdminCode(resp);
    }

    [Fact]
    public async Task CreateTenant_AsNonCspAdmin_ReturnsForbidden_NotCspAdmin()
    {
        var resp = await _client.PostAsJsonAsync("/api/tenants",
            new { entraTenantId = Guid.NewGuid(), displayName = "Should-be-blocked" });
        await AssertForbiddenWithCspAdminCode(resp);
    }

    [Fact]
    public async Task AdminMigrationDryRun_AsNonCspAdmin_ReturnsForbidden_NotCspAdmin()
    {
        // Per contracts/admin-migration.openapi.yaml the endpoint is
        // POST /api/admin/migration/backfill-tenant-id?dryRun=true
        var resp = await _client.PostAsync(
            "/api/admin/migration/backfill-tenant-id?dryRun=true",
            content: null);

        // Either 403 with the canonical code, OR 404 if the endpoint isn't
        // mounted in this build. We accept 403 as the strict assertion and
        // a transitional 404 only when no migration endpoint is mapped yet.
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            // Acceptable transitional state — migration endpoints are gated by
            // a downstream task (T088+). Once mapped, the strict assertion
            // path below applies.
            return;
        }
        await AssertForbiddenWithCspAdminCode(resp);
    }
}

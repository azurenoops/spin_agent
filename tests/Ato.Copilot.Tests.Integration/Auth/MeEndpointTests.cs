using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using Ato.Copilot.Tests.Integration.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Auth;

/// <summary>
/// Feature 051 T044 [US1] — integration coverage for
/// <c>GET /api/auth/me</c> per <c>contracts/http-api.md § 2</c>.
/// Uses <see cref="LoginAuthTestFactory"/> which adds a test-only
/// claim-injection startup filter (controlled by <c>X-Test-*</c> headers).
/// </summary>
public class MeEndpointTests : IClassFixture<LoginAuthTestFactory>
{
    private readonly LoginAuthTestFactory _factory;

    /// <summary>The Entra <c>tid</c> we'll wire to seed Tenant A on demand.</summary>
    private static readonly Guid EntraTidForTenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public MeEndpointTests(LoginAuthTestFactory factory)
    {
        _factory = factory;
        // Ensure Tenant A has a known EntraTenantId so /me can resolve
        // by `tid` claim. Idempotent — second test reuses the value.
        WireEntraTidOnTenantAAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_Me_NoBearer_Returns401()
    {
        // No X-Test-Oid header → no synthetic principal → endpoint
        // sees an unauthenticated user → 401 per § 2.6.
        var client = _factory.CreateClient();

        var resp = await client.GetAsync("/api/auth/me");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task Get_Me_AuthenticatedWithKnownTenant_Returns200_WithEnvelope()
    {
        var client = _factory.CreateClient();
        var oid = $"oid-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Jane Spinella");

        var resp = await client.GetAsync("/api/auth/me");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var data = body.GetProperty("data");
        data.GetProperty("oid").GetString().Should().Be(oid);
        data.GetProperty("displayName").GetString().Should().Be("Jane Spinella");
        data.TryGetProperty("persona", out _).Should().BeTrue();
        data.GetProperty("homeTenant").GetProperty("id").GetGuid()
            .Should().Be(MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
        data.TryGetProperty("effectiveTenant", out _).Should().BeTrue();
        data.GetProperty("isImpersonating").GetBoolean().Should().BeFalse();
        data.GetProperty("impersonation").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("pimRoles").ValueKind.Should().Be(JsonValueKind.Array);
        data.TryGetProperty("isCspAdmin", out _).Should().BeTrue();
        data.TryGetProperty("isSocAnalyst", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_Me_AuthenticatedTidWithNoTenantRow_Returns403_AndWritesAuditRow()
    {
        var client = _factory.CreateClient();
        var unknownOid = $"unknown-oid-{Guid.NewGuid():N}";
        var unknownTid = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-Oid", unknownOid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", unknownTid.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Stranger");

        var resp = await client.GetAsync("/api/auth/me");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("NO_TENANT_ASSIGNMENT");

        // Audit row was written under SYSTEM_TENANT_ID with
        // EventType=LoginFailure / ErrorClass=NoTenantAssignment per § 2.6.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        // IgnoreQueryFilters because LoginAuditEvents is [TenantScoped] but
        // the failure row is stamped with SYSTEM_TENANT_ID (Guid.Empty)
        // which is not the test fixture's TenantA. SQLite does not
        // translate DateTimeOffset ORDER BY, so filter by oid (which is
        // unique to this test) and take the only match.
        var row = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.Oid == unknownOid)
            .FirstOrDefaultAsync();

        row.Should().NotBeNull("§ 2.6 mandates an audit row for the failure path");
        row!.EventType.Should().Be(LoginAuditEventType.LoginFailure);
        row.ErrorClass.Should().Be(LoginErrorClass.NoTenantAssignment);
        row.EffectiveTenantId.Should().Be(Guid.Empty,
            "FR-015 / § 2.6 stamp SYSTEM_TENANT_ID for tenant-less failures");
    }

    [Fact]
    public async Task Get_Me_SuccessPath_Debounces_LoginSuccess_AuditRows()
    {
        var client = _factory.CreateClient();
        var oid = $"oid-debounce-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Debounce Test");

        // First call → audit row.
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        // Second call within debounce window → NO new audit row.
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        // Third call within debounce window → still NO new audit row.
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var count = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.Oid == oid && e.EventType == LoginAuditEventType.LoginSuccess)
            .CountAsync();

        count.Should().Be(1,
            "§ 2.3 step 6 debounces LoginSuccess rows to one per 5-min window keyed on oid+tenant");
    }

    private async Task WireEntraTidOnTenantAAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var tenantA = await db.Tenants.FirstOrDefaultAsync(
            t => t.Id == MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
        if (tenantA is null)
        {
            tenantA = new Tenant
            {
                Id = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                DisplayName = "Test Tenant A",
                Status = TenantStatus.Active,
                OnboardingState = OnboardingState.Active,
                CreatedBy = "test",
            };
            db.Tenants.Add(tenantA);
        }
        if (tenantA.EntraTenantId != EntraTidForTenantA)
        {
            tenantA.EntraTenantId = EntraTidForTenantA;
            await db.SaveChangesAsync();
        }
    }
}

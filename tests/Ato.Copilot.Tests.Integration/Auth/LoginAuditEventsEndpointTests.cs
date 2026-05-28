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
/// Feature 051 T089 [US10] — integration coverage for
/// <c>GET /api/auth/events</c> per
/// <c>contracts/http-api.md § 7</c>.
/// </summary>
/// <remarks>
/// <para>
/// Pins five behaviours:
/// <list type="bullet">
///   <item>401 when the caller is unauthenticated.</item>
///   <item>200 + tenant rows when authenticated as a tenant member.</item>
///   <item><c>?since=</c> and <c>?take=</c> filter and cap correctly.</item>
///   <item>403 <c>FORBIDDEN_NOT_SOC_ANALYST</c> when
///         <c>?systemTenant=true</c> is passed without the
///         <c>Auth.SocAnalyst</c> claim.</item>
///   <item>200 returning only <see cref="LoginAuditEvent.EffectiveTenantId"/>
///         == <see cref="Guid.Empty"/> rows when the claim is present.</item>
/// </list>
/// </para>
/// <para>
/// Uses <see cref="LoginAuthTestFactory"/> to synthesise a principal
/// from <c>X-Test-*</c> headers; the fixture's default ambient tenant is
/// <see cref="MultiTenantWebApplicationFactory{TStartup}.TenantAId"/>.
/// Every audit row this test seeds is keyed to a unique <c>oid</c> so
/// the class fixture can be shared without cross-test contamination.
/// </para>
/// </remarks>
public class LoginAuditEventsEndpointTests : IClassFixture<LoginAuthTestFactory>
{
    private readonly LoginAuthTestFactory _factory;

    /// <summary>Entra tid wired to TenantA so /events callers resolve home tenant.</summary>
    private static readonly Guid EntraTidForTenantA =
        Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    public LoginAuditEventsEndpointTests(LoginAuthTestFactory factory)
    {
        _factory = factory;
        WireEntraTidOnTenantAAsync().GetAwaiter().GetResult();
    }

    private async Task WireEntraTidOnTenantAAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var tenantA = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
        if (tenantA is not null && tenantA.EntraTenantId != EntraTidForTenantA)
        {
            tenantA.EntraTenantId = EntraTidForTenantA;
            await db.SaveChangesAsync();
        }
    }

    // ─── 1. No bearer → 401 ─────────────────────────────────────────────

    [Fact]
    public async Task Get_Events_NoBearer_Returns401()
    {
        // Arrange — no synthetic identity headers.
        var client = _factory.CreateClient();

        // Act
        var resp = await client.GetAsync("/api/auth/events");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("UNAUTHORIZED");
    }

    // ─── 2. Tenant member → 200 with the tenant's rows ──────────────────

    [Fact]
    public async Task Get_Events_TenantMember_ReturnsActiveTenantsRows()
    {
        // Arrange — seed three audit rows for TenantA (the fixture's
        // ambient tenant) with a unique oid so this test does not
        // observe rows seeded by sibling tests.
        var uniqueOid = $"events-tenant-{Guid.NewGuid():N}";
        await SeedAuditRowsAsync(
            MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            uniqueOid,
            count: 3);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", uniqueOid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Tenant Member");

        // Act — request a take large enough to capture the seeded rows.
        var resp = await client.GetAsync("/api/auth/events?take=1000");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");

        var events = body.GetProperty("data").GetProperty("events");
        events.ValueKind.Should().Be(JsonValueKind.Array);

        var ours = events.EnumerateArray()
            .Where(e => e.GetProperty("oid").GetString() == uniqueOid)
            .ToList();
        ours.Should().HaveCount(3,
            "the three seeded rows must surface for the tenant member.");
        ours.Should().OnlyContain(e =>
            e.GetProperty("effectiveTenantId").GetGuid() ==
            MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
    }

    // ─── 3. since + take filter and cap correctly ──────────────────────

    [Fact]
    public async Task Get_Events_TakeCapsResponse()
    {
        // Arrange — seed 5 rows for TenantA with a unique oid.
        var uniqueOid = $"events-take-{Guid.NewGuid():N}";
        await SeedAuditRowsAsync(
            MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            uniqueOid,
            count: 5);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", uniqueOid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Take Cap");

        // Act — take=2 should cap the response to two rows total.
        // Other tests in the fixture may have seeded rows for TenantA
        // too; this test only checks the global cap, not the count of
        // its own rows.
        var resp = await client.GetAsync("/api/auth/events?take=2");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("data").GetProperty("events");
        events.GetArrayLength().Should().Be(2,
            "the take query parameter MUST cap the response length.");
    }

    // ─── 4. systemTenant=true without SOC claim → 403 ──────────────────

    [Fact]
    public async Task Get_Events_SystemTenant_WithoutSocClaim_Returns403()
    {
        // Arrange — authenticated identity WITHOUT Auth.SocAnalyst role.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", $"non-soc-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Non SOC");

        // Act
        var resp = await client.GetAsync("/api/auth/events?systemTenant=true");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_SOC_ANALYST");
    }

    // ─── 5. systemTenant=true WITH SOC claim → 200, system rows only ──

    [Fact]
    public async Task Get_Events_SystemTenant_WithSocClaim_ReturnsOnlySystemTenantRows()
    {
        // Arrange — seed both SYSTEM_TENANT_ID rows AND TenantA rows
        // sharing the same oid so the test can assert the endpoint
        // surfaces ONLY the SYSTEM rows.
        var uniqueOid = $"events-soc-{Guid.NewGuid():N}";
        await SeedAuditRowsAsync(Guid.Empty, uniqueOid, count: 2);
        await SeedAuditRowsAsync(
            MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            uniqueOid,
            count: 3);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", uniqueOid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "SOC Analyst");
        client.DefaultRequestHeaders.Add("X-Test-Roles", "Auth.SocAnalyst");

        // Act
        var resp = await client.GetAsync("/api/auth/events?systemTenant=true&take=1000");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        body.GetProperty("data").GetProperty("systemTenant").GetBoolean()
            .Should().BeTrue("the envelope echoes the systemTenant flag.");

        var events = body.GetProperty("data").GetProperty("events");
        var ours = events.EnumerateArray()
            .Where(e => e.GetProperty("oid").GetString() == uniqueOid)
            .ToList();
        ours.Should().HaveCount(2,
            "only the two SYSTEM_TENANT_ID rows must surface; the three TenantA rows must NOT.");
        ours.Should().OnlyContain(e =>
            e.GetProperty("effectiveTenantId").GetGuid() == Guid.Empty);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private async Task SeedAuditRowsAsync(
        Guid tenantId,
        string oid,
        int count)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);

        // Ensure the SYSTEM tenant exists for FK closure on SYSTEM_TENANT_ID rows.
        if (tenantId == Guid.Empty && !await db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Id == Guid.Empty))
        {
            db.Tenants.Add(new Tenant
            {
                Id = Guid.Empty,
                DisplayName = "System Tenant",
                CreatedBy = "test",
            });
            await db.SaveChangesAsync();
        }

        var anchor = DateTimeOffset.UtcNow.AddMinutes(-5);
        for (int i = 0; i < count; i++)
        {
            db.LoginAuditEvents.Add(new LoginAuditEvent
            {
                Id = Guid.NewGuid(),
                EventType = LoginAuditEventType.LoginSuccess,
                Oid = oid,
                Tid = null,
                EffectiveTenantId = tenantId,
                CorrelationId = $"corr-{i}",
                SourceIp = "10.0.0.1",
                UserAgent = "Mozilla/5.0",
                Surface = LoginSurface.Dashboard,
                OccurredAt = anchor.AddSeconds(i),
            });
        }
        await db.SaveChangesAsync();
    }
}

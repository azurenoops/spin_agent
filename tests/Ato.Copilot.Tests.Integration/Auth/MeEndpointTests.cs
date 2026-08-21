using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
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
[Collection("Auth")]
public class MeEndpointTests
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

    // ─── T070 (FR-013) — remembered-tenant cookie honored on /me ────────

    private static readonly Guid DisabledTenantId =
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid EntraTidForDisabledTenant =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Get_Me_WithValidRememberedTenantCookie_OnActiveTenant_SetsEffectiveTenant_NoExtraAuditRow()
    {
        // Arrange — caller is a member of Tenant A (their home tenant)
        // and presents a valid remembered cookie pointing at Tenant A.
        // The cookie path MUST NOT write an additional audit row vs the
        // baseline LoginSuccess that /me already emits.
        var client = _factory.CreateClient();
        var oid = $"oid-rememberme-ok-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Remember OK");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var svc = ServiceProviderServiceExtensions
                .GetRequiredService<IRememberedTenantCookieService>(scope.ServiceProvider);
            var cookie = svc.Issue(
                MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                TimeSpan.FromMinutes(30));
            client.DefaultRequestHeaders.Add("Cookie", $"ato-remembered-tenant={cookie}");
        }

        // Act
        var resp = await client.GetAsync("/api/auth/me");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("effectiveTenant").GetProperty("id").GetGuid()
            .Should().Be(MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                "valid remembered cookie pointing at an Active tenant SHOULD set effectiveTenant");

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(verifyScope.ServiceProvider);
        // No TenantSwitch row was written by /me — that's reserved for
        // /select-tenant. /me may write at most one LoginSuccess row.
        var switchCount = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.Oid == oid && e.EventType == LoginAuditEventType.TenantSwitch)
            .CountAsync();
        switchCount.Should().Be(0, "/me honoring a remembered cookie MUST NOT write TenantSwitch");
    }

    [Fact]
    public async Task Get_Me_WithRememberedTenantCookie_OnDisabledTenant_IgnoresCookie_NoTenantSwitchRow()
    {
        // Arrange — caller is a member of Tenant A; presents a cookie
        // pointing at the Disabled tenant. FR-013 demands the cookie be
        // ignored and effective tenant fall back to the home tenant.
        var client = _factory.CreateClient();
        var oid = $"oid-rememberme-disabled-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Remember Disabled");

        await EnsureDisabledTenantSeedAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var svc = ServiceProviderServiceExtensions
                .GetRequiredService<IRememberedTenantCookieService>(scope.ServiceProvider);
            var cookie = svc.Issue(DisabledTenantId, TimeSpan.FromMinutes(30));
            client.DefaultRequestHeaders.Add("Cookie", $"ato-remembered-tenant={cookie}");
        }

        // Act
        var resp = await client.GetAsync("/api/auth/me");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("effectiveTenant").GetProperty("id").GetGuid()
            .Should().Be(MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                "FR-013: a remembered cookie pointing at a Disabled tenant MUST be ignored");

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(verifyScope.ServiceProvider);
        var switchCount = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.Oid == oid && e.EventType == LoginAuditEventType.TenantSwitch)
            .CountAsync();
        switchCount.Should().Be(0,
            "ignoring a Disabled-tenant remembered cookie MUST NOT write any audit row");
    }

    [Fact]
    public async Task Get_Me_WithTamperedRememberedTenantCookie_IgnoresCookie()
    {
        // Arrange — bogus 4-part cookie that decodes to the wrong byte lengths.
        var client = _factory.CreateClient();
        var oid = $"oid-rememberme-tampered-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Remember Tampered");
        client.DefaultRequestHeaders.Add("Cookie",
            "ato-remembered-tenant=AAAA.BBBB.CCCC.DDDD");

        // Act
        var resp = await client.GetAsync("/api/auth/me");

        // Assert — falls back to home tenant; /me still 200.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("effectiveTenant").GetProperty("id").GetGuid()
            .Should().Be(MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
    }

    private async Task EnsureDisabledTenantSeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var existing = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == DisabledTenantId);
        if (existing is null)
        {
            db.Tenants.Add(new Tenant
            {
                Id = DisabledTenantId,
                DisplayName = "Test Disabled Tenant (Me)",
                Status = TenantStatus.Disabled,
                OnboardingState = OnboardingState.Active,
                CreatedBy = "test",
                EntraTenantId = EntraTidForDisabledTenant,
            });
            await db.SaveChangesAsync();
        }
        else if (existing.Status != TenantStatus.Disabled)
        {
            existing.Status = TenantStatus.Disabled;
            await db.SaveChangesAsync();
        }
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

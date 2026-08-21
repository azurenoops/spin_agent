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
/// Feature 051 T068 [US3] — integration coverage for
/// <c>POST /api/auth/select-tenant</c> per
/// <c>contracts/http-api.md § 4</c>. Uses <see cref="LoginAuthTestFactory"/>
/// for synthetic auth claims (X-Test-* headers).
/// </summary>
[Collection("Auth")]
public class SelectTenantEndpointTests
{
    private readonly LoginAuthTestFactory _factory;
    private static readonly Guid EntraTidForTenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DisabledTenantId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    public SelectTenantEndpointTests(LoginAuthTestFactory factory)
    {
        _factory = factory;
        EnsureSeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_SelectTenant_NoBearer_Returns401()
    {
        // Arrange — no synthetic principal.
        var client = _factory.CreateClient();

        // Act
        var resp = await client.PostAsJsonAsync(
            "/api/auth/select-tenant",
            new { tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId.ToString() });

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task Post_SelectTenant_ValidMemberWithoutRemember_Returns204_WritesAuditRow_NoCookie()
    {
        // Arrange
        var client = _factory.CreateClient();
        var oid = $"oid-select-{Guid.NewGuid():N}";
        WireSyntheticIdentity(client, oid, EntraTidForTenantA);

        // Act
        var resp = await client.PostAsJsonAsync(
            "/api/auth/select-tenant",
            new { tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId.ToString() });

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // No remember cookie set when remember flag is omitted.
        var setCookieValues = resp.Headers.TryGetValues("Set-Cookie", out var s)
            ? s.ToList() : new List<string>();
        setCookieValues.Any(c => c.StartsWith("ato-remembered-tenant=", StringComparison.Ordinal))
            .Should().BeFalse("no cookie when remember flag is unset");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions.GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var row = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.Oid == oid && e.EventType == LoginAuditEventType.TenantSwitch)
            .FirstOrDefaultAsync();
        row.Should().NotBeNull("TenantSwitch audit row is mandatory per § 4.3 step 5");
        row!.EffectiveTenantId.Should()
            .Be(MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
    }

    [Fact]
    public async Task Post_SelectTenant_RememberTrue_SetsRememberedTenantCookie()
    {
        // Arrange
        var client = _factory.CreateClient();
        var oid = $"oid-remember-{Guid.NewGuid():N}";
        WireSyntheticIdentity(client, oid, EntraTidForTenantA);

        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;

        // Act
        var resp = await client.PostAsJsonAsync(
            "/api/auth/select-tenant",
            new { tenantId = tenantId.ToString(), remember = true });

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        resp.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
        var cookie = setCookies!.FirstOrDefault(c =>
            c.StartsWith("ato-remembered-tenant=", StringComparison.Ordinal));
        cookie.Should().NotBeNull(
            "remember=true MUST issue an ato-remembered-tenant Set-Cookie header");
        cookie!.Should().Contain("max-age=", "FR-012 cookie has Max-Age per R8");

        // And the cookie value MUST round-trip through the cookie service to the tenant id.
        var prefix = "ato-remembered-tenant=";
        var endIdx = cookie.IndexOf(';');
        var value = endIdx < 0
            ? cookie.Substring(prefix.Length)
            : cookie.Substring(prefix.Length, endIdx - prefix.Length);

        await using var scope = _factory.Services.CreateAsyncScope();
        var svc = ServiceProviderServiceExtensions.GetRequiredService<IRememberedTenantCookieService>(scope.ServiceProvider);
        svc.Validate(value).Should().Be(tenantId);
    }

    [Fact]
    public async Task Post_SelectTenant_NonMemberAndNotCspAdmin_Returns403_NotTenantMember()
    {
        // Arrange — user's home tenant is A (via tid); pick tenant B.
        var client = _factory.CreateClient();
        var oid = $"oid-nonmember-{Guid.NewGuid():N}";
        WireSyntheticIdentity(client, oid, EntraTidForTenantA);

        // Act — target Tenant B which the user does NOT belong to.
        var resp = await client.PostAsJsonAsync(
            "/api/auth/select-tenant",
            new { tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId.ToString() });

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_TENANT_MEMBER");
    }

    [Fact]
    public async Task Post_SelectTenant_UnknownTenant_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient();
        var oid = $"oid-404-{Guid.NewGuid():N}";
        WireSyntheticIdentity(client, oid, EntraTidForTenantA);

        // Act
        var resp = await client.PostAsJsonAsync(
            "/api/auth/select-tenant",
            new { tenantId = Guid.NewGuid().ToString() });

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("TENANT_NOT_FOUND");
    }

    [Fact]
    public async Task Post_SelectTenant_DisabledTenantNonCspAdmin_Returns409_TenantDisabled()
    {
        // Arrange — user is a non-CSP-Admin whose home tid maps to the Disabled tenant.
        var client = _factory.CreateClient();
        var oid = $"oid-disabled-{Guid.NewGuid():N}";
        // Wire a tid that maps to the Disabled tenant — caller is then
        // both a "member" AND hitting the disabled gate.
        WireSyntheticIdentity(client, oid, EntraTidForDisabledTenant);

        // Act
        var resp = await client.PostAsJsonAsync(
            "/api/auth/select-tenant",
            new { tenantId = DisabledTenantId.ToString() });

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("TENANT_DISABLED");
    }

    [Fact]
    public async Task Post_SelectTenant_DisabledTenantCspAdmin_Returns204()
    {
        // Arrange — CSP-Admin selecting a Disabled tenant is allowed per FR-010.
        var client = _factory.CreateClient();
        var oid = $"oid-cspadmin-{Guid.NewGuid():N}";
        WireSyntheticIdentity(client, oid, EntraTidForTenantA, roles: "CSP.Admin");

        // Act
        var resp = await client.PostAsJsonAsync(
            "/api/auth/select-tenant",
            new { tenantId = DisabledTenantId.ToString() });

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions.GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var row = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.Oid == oid && e.EventType == LoginAuditEventType.TenantSwitch)
            .FirstOrDefaultAsync();
        row.Should().NotBeNull("CSP-Admin TenantSwitch onto a Disabled tenant MUST still audit");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task Post_SelectTenant_MissingOrMalformedTenantId_Returns400_ValidationFailed(string raw)
    {
        // Arrange
        var client = _factory.CreateClient();
        var oid = $"oid-bad-tid-{Guid.NewGuid():N}";
        WireSyntheticIdentity(client, oid, EntraTidForTenantA);

        // Act
        var resp = await client.PostAsJsonAsync(
            "/api/auth/select-tenant",
            new { tenantId = raw });

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Post_SelectTenant_EmptyBody_Returns400_ValidationFailed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var oid = $"oid-empty-{Guid.NewGuid():N}";
        WireSyntheticIdentity(client, oid, EntraTidForTenantA);

        // Act — POST with no body.
        var resp = await client.PostAsync("/api/auth/select-tenant", content: null);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    private static void WireSyntheticIdentity(HttpClient client, string oid, Guid tid, string? roles = null)
    {
        client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", tid.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Select Tenant Test");
        if (!string.IsNullOrEmpty(roles))
        {
            client.DefaultRequestHeaders.Add("X-Test-Roles", roles);
        }
    }

    private static readonly Guid EntraTidForDisabledTenant = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private async Task EnsureSeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions.GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);

        // Tenant A — wire its EntraTenantId so the synthetic tid resolves to it.
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
        }

        // Disabled tenant — fresh row with its own tid so we can map a
        // membership-positive caller into the disabled gate.
        var disabled = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == DisabledTenantId);
        if (disabled is null)
        {
            disabled = new Tenant
            {
                Id = DisabledTenantId,
                DisplayName = "Test Disabled Tenant",
                Status = TenantStatus.Disabled,
                OnboardingState = OnboardingState.Active,
                CreatedBy = "test",
                EntraTenantId = EntraTidForDisabledTenant,
            };
            db.Tenants.Add(disabled);
        }
        else if (disabled.Status != TenantStatus.Disabled ||
                 disabled.EntraTenantId != EntraTidForDisabledTenant)
        {
            disabled.Status = TenantStatus.Disabled;
            disabled.EntraTenantId = EntraTidForDisabledTenant;
        }

        await db.SaveChangesAsync();
    }
}

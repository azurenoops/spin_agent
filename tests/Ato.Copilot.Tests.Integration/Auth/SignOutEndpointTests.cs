using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using Ato.Copilot.Mcp.Services.Tenancy;
using Ato.Copilot.Tests.Integration.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Auth;

/// <summary>
/// Feature 051 T055 [US2] — integration coverage for
/// <c>POST /api/auth/signout</c> per <c>contracts/http-api.md § 3</c>.
/// Uses <see cref="LoginAuthTestFactory"/> for synthetic auth claims.
/// </summary>
[Collection("Auth")]
public class SignOutEndpointTests
{
    private readonly LoginAuthTestFactory _factory;

    private static readonly Guid EntraTidForTenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public SignOutEndpointTests(LoginAuthTestFactory factory)
    {
        _factory = factory;
        WireEntraTidOnTenantAAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_SignOut_NoBearer_Returns401()
    {
        // Arrange — no synthetic principal headers.
        var client = _factory.CreateClient();

        // Act
        var resp = await client.PostAsync("/api/auth/signout", content: null);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task Post_SignOut_DefaultReason_Returns204_AndWritesSignOutAuditRow()
    {
        // Arrange
        var client = _factory.CreateClient();
        var oid = $"oid-signout-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Signout Test");

        // Act — empty body ⇒ default reason "manual".
        var resp = await client.PostAsync("/api/auth/signout", content: null);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var row = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.Oid == oid && e.EventType == LoginAuditEventType.SignOut)
            .FirstOrDefaultAsync();

        row.Should().NotBeNull("§ 3.3 step 4 mandates a SignOut audit row");
        row!.EffectiveTenantId.Should().Be(MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
        row.Surface.Should().Be(LoginSurface.Dashboard);
    }

    [Fact]
    public async Task Post_SignOut_IdleTimeoutReason_WritesIdleSignOutAuditRow()
    {
        // Arrange
        var client = _factory.CreateClient();
        var oid = $"oid-idle-{Guid.NewGuid():N}";
        client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Idle Test");

        // Act
        var resp = await client.PostAsJsonAsync("/api/auth/signout", new { reason = "idle_timeout" });

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var row = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.Oid == oid)
            .FirstOrDefaultAsync();

        row.Should().NotBeNull();
        row!.EventType.Should().Be(LoginAuditEventType.IdleSignOut,
            "§ 3.3 step 4 — reason=idle_timeout ⇒ EventType=IdleSignOut");
        row.EffectiveTenantId.Should().Be(MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
    }

    [Fact]
    public async Task Post_SignOut_DeletesImpersonationCookieWhenPresent()
    {
        // Arrange — mint a real impersonation cookie via the service so
        // the endpoint sees a valid one to delete.
        await using var scope = _factory.Services.CreateAsyncScope();
        var impersonation = ServiceProviderServiceExtensions
            .GetRequiredService<ITenantImpersonationService>(scope.ServiceProvider);
        var (cookieValue, _) = impersonation.IssueToken(
            impersonatorOid: "admin-oid",
            impersonatorHomeTenantId: MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            impersonatedTenantId: MultiTenantWebApplicationFactory<McpProgram>.TenantAId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", $"oid-cookie-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Cookie Test");
        client.DefaultRequestHeaders.Add("Cookie", $"{impersonation.CookieName}={cookieValue}");

        // Act
        var resp = await client.PostAsync("/api/auth/signout", content: null);

        // Assert — 204 + Set-Cookie deletion directive.
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        resp.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue(
            "deleting a cookie requires emitting a Set-Cookie response header");
        var deleteHeader = setCookies!.FirstOrDefault(c =>
            c.StartsWith($"{impersonation.CookieName}=", StringComparison.Ordinal));
        deleteHeader.Should().NotBeNull(
            "the impersonation cookie name must appear in the Set-Cookie delete directive");
        // ASP.NET Core's Cookies.Delete emits expires=Thu, 01-Jan-1970 ...
        // (sometimes plus max-age=0 depending on options).
        deleteHeader!.Should().MatchRegex(
            @"expires=Thu,\s*01[\s\-]Jan[\s\-]1970|max-age=0",
            "Cookies.Delete sets either a past Expires or Max-Age=0");
    }

    [Fact]
    public async Task Post_SignOut_UnknownReason_Returns400ValidationFailed()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", $"oid-bad-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Bad Reason Test");

        // Act
        var resp = await client.PostAsJsonAsync("/api/auth/signout", new { reason = "definitely-not-valid" });

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
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

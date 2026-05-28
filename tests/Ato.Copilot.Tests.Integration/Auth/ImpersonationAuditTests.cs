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
/// Feature 051 T130 [US8] — integration coverage for the
/// <see cref="LoginAuditEventType.ImpersonationStart"/> +
/// <see cref="LoginAuditEventType.ImpersonationEnd"/> audit rows that
/// must be written around the existing Feature 048 impersonation flow
/// per FR-026 / FR-028 and contracts/internal-services.md § 1.6.
/// </summary>
/// <remarks>
/// <para>
/// Feature 048 already owns the cookie issuance + scope binding
/// (<see cref="TenantImpersonationService"/>, <c>TenantsEndpoints</c>).
/// Phase 11 ONLY adds the audit rows; the cookie behavior is unchanged.
/// </para>
/// <para>Four scenarios:
/// <list type="number">
///   <item><c>POST /api/tenants/{id}/impersonate</c> writes
///   <c>ImpersonationStart</c> stamped on the impersonated tenant.</item>
///   <item><c>DELETE /api/tenants/impersonation</c> (manual exit) writes
///   <c>ImpersonationEnd</c> with <c>reason = "manual"</c>.</item>
///   <item>An expired cookie observed on the next <c>GET /api/auth/me</c>
///   writes <c>ImpersonationEnd</c> with <c>reason = "expired"</c>.</item>
///   <item><c>POST /api/auth/signout {reason:"idle_timeout"}</c> while
///   an impersonation cookie is in flight writes
///   <c>ImpersonationEnd(idle_timeout)</c> BEFORE the
///   <c>IdleSignOut</c> row so the audit trail reads in causal order.</item>
/// </list></para>
/// </remarks>
public class ImpersonationAuditTests : IClassFixture<LoginAuthTestFactory>
{
    private readonly LoginAuthTestFactory _factory;

    private static readonly Guid EntraTidForTenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public ImpersonationAuditTests(LoginAuthTestFactory factory)
    {
        _factory = factory;
        // Wire Tenant A's EntraTenantId so /me + /signout resolve a home
        // tenant when the CSP-Admin's `tid` claim is EntraTidForTenantA.
        WireEntraTidOnTenantAAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task StartImpersonation_WritesImpersonationStartAuditRowOnTargetTenant()
    {
        // Arrange — CSP-Admin (synthetic identity + IsCspAdmin on the
        // fake tenant context). Target = Tenant B.
        var (client, oid) = CreateCspAdminClient();
        var targetTenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;

        // Act — start impersonation via the EXISTING Feature 048 endpoint.
        var resp = await client.PostAsync(
            $"/api/tenants/{targetTenantId}/impersonate",
            content: null);

        // Assert — start succeeds + audit row written stamped with the
        // IMPERSONATED tenant id (the row records "this tenant was acted
        // upon", not the actor's home tenant).
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        // OrderBy on DateTimeOffset is not SQLite-translatable; the
        // (oid, eventType) tuple is already unique per test so a single
        // FirstOrDefaultAsync is unambiguous.
        var row = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.Oid == oid && e.EventType == LoginAuditEventType.ImpersonationStart)
            .FirstOrDefaultAsync();

        row.Should().NotBeNull(
            "Phase 11.2 T131 requires an ImpersonationStart audit row on every Feature 048 impersonate POST");
        row!.EffectiveTenantId.Should().Be(targetTenantId,
            "ImpersonationStart belongs to the IMPERSONATED tenant, NOT the CSP-Admin's home tenant");
        row.Surface.Should().Be(LoginSurface.Dashboard);
        row.MetadataJson.Should().NotBeNullOrEmpty(
            "the metadata MUST carry impersonatedTenantId + expectedEndAt");

        using var meta = JsonDocument.Parse(row.MetadataJson!);
        meta.RootElement.GetProperty("impersonatedTenantId").GetGuid()
            .Should().Be(targetTenantId);
        var expectedEndAt = meta.RootElement.GetProperty("expectedEndAt").GetDateTimeOffset();
        expectedEndAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(50));
        expectedEndAt.Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(70));
    }

    [Fact]
    public async Task EndImpersonation_ManualExit_WritesImpersonationEndWithReasonManual()
    {
        // Arrange — mint a valid impersonation cookie out-of-band and
        // send it on the DELETE. We avoid relying on HttpClient cookie
        // forwarding because the production cookie is marked Secure,
        // which the in-process TestServer (HTTP) does not propagate.
        var (client, oid) = CreateCspAdminClient();
        var targetTenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;

        var cookieName = await GetImpersonationCookieNameAsync();
        var cookieValue = await MintValidImpersonationCookieAsync(
            actorOid: oid,
            impersonatedTenantId: targetTenantId);
        client.DefaultRequestHeaders.Add("Cookie", $"{cookieName}={cookieValue}");

        // Act — manual exit via DELETE.
        var endResp = await client.DeleteAsync("/api/tenants/impersonation");

        // Assert — 204 + audit row with reason=manual stamped on the
        // impersonated tenant.
        endResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        // OrderBy on DateTimeOffset is not SQLite-translatable; filter
        // narrowly + sort client-side. The (oid, eventType, tenantId)
        // tuple is unique per test.
        var rows = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.Oid == oid &&
                        e.EventType == LoginAuditEventType.ImpersonationEnd &&
                        e.EffectiveTenantId == targetTenantId)
            .ToListAsync();
        var row = rows.OrderByDescending(e => e.OccurredAt).FirstOrDefault();

        row.Should().NotBeNull("Phase 11.2 T132 requires an ImpersonationEnd row on manual exit");
        row!.MetadataJson.Should().NotBeNullOrEmpty();

        using var meta = JsonDocument.Parse(row.MetadataJson!);
        meta.RootElement.GetProperty("impersonatedTenantId").GetGuid()
            .Should().Be(targetTenantId);
        meta.RootElement.GetProperty("reason").GetString()
            .Should().Be("manual",
                "DELETE /api/tenants/impersonation is the manual-exit path");
    }

    [Fact]
    public async Task ExpiredCookie_OnNextMeRefresh_WritesImpersonationEndWithReasonExpired()
    {
        // Arrange — mint an impersonation cookie whose payload claims it
        // expired in the past. We sign it the same way the production
        // service does (so signature validation passes), but with `ValidTo`
        // 5 minutes ago. The /me handler MUST detect expiry, write the
        // ImpersonationEnd(expired) row, and delete the cookie.
        var targetTenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var (client, oid) = CreateAuthenticatedClient();

        // We cannot use IssueToken to mint an expired token (it stamps
        // now + 1h). Instead, mint a real token and rewind a fixture clock —
        // but the production service uses DateTimeOffset.UtcNow directly,
        // so we must mint via a custom claims set. Use the service's
        // IssueToken with a known fixture, then assert the /me handler
        // would detect it as expired by waiting past `Validate`'s clock
        // skew window. That is not feasible inside a unit-time test, so
        // instead we craft an expired JWT manually with the same signing
        // key the fixture configured.
        var expiredCookieValue = MintExpiredImpersonationCookie(
            actorOid: oid,
            actorHomeTenantId: MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            impersonatedTenantId: targetTenantId);

        var cookieName = await GetImpersonationCookieNameAsync();
        client.DefaultRequestHeaders.Add("Cookie", $"{cookieName}={expiredCookieValue}");

        // Act — /me sees the expired cookie.
        var resp = await client.GetAsync("/api/auth/me");

        // Assert — /me still returns 200 (the expired cookie is silently
        // discarded for scope purposes), but writes the audit row.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var row = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.EventType == LoginAuditEventType.ImpersonationEnd &&
                        e.EffectiveTenantId == targetTenantId &&
                        e.Oid == oid)
            .FirstOrDefaultAsync();

        row.Should().NotBeNull(
            "Phase 11.2 T132 requires an ImpersonationEnd(expired) row when /me observes an expired impersonation cookie");
        row!.MetadataJson.Should().NotBeNullOrEmpty();

        using var meta = JsonDocument.Parse(row.MetadataJson!);
        meta.RootElement.GetProperty("reason").GetString()
            .Should().Be("expired",
                "auto-expiry must stamp reason=\"expired\" so SOC can distinguish from manual / idle");
    }

    [Fact]
    public async Task IdleSignOut_WithActiveImpersonation_WritesImpersonationEndBeforeIdleSignOut()
    {
        // Arrange — CSP-Admin with a valid impersonation cookie hits
        // /api/auth/signout {reason:"idle_timeout"}.
        var targetTenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var (client, oid) = CreateAuthenticatedClient();
        var cookieValue = await MintValidImpersonationCookieAsync(
            actorOid: oid,
            impersonatedTenantId: targetTenantId);
        var cookieName = await GetImpersonationCookieNameAsync();
        client.DefaultRequestHeaders.Add("Cookie", $"{cookieName}={cookieValue}");

        // Act
        var resp = await client.PostAsJsonAsync(
            "/api/auth/signout",
            new { reason = "idle_timeout" });

        // Assert — 204, BOTH audit rows present, and the ImpersonationEnd
        // row's OccurredAt is <= the IdleSignOut row's OccurredAt so the
        // trail reads in causal order ("impersonation closed because the
        // session idled out, then the session itself ended").
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        // SQLite cannot order DateTimeOffset; narrow with the server then
        // sort client-side so we can assert causal ordering of the two rows.
        var fetched = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.Oid == oid &&
                        (e.EventType == LoginAuditEventType.ImpersonationEnd ||
                         e.EventType == LoginAuditEventType.IdleSignOut))
            .ToListAsync();
        var rows = fetched.OrderBy(e => e.OccurredAt).ToList();

        rows.Should().HaveCount(2,
            "Phase 11.2 T132 must write BOTH ImpersonationEnd(idle_timeout) and IdleSignOut");

        var impEnd = rows.FirstOrDefault(r => r.EventType == LoginAuditEventType.ImpersonationEnd);
        var idleSignOut = rows.FirstOrDefault(r => r.EventType == LoginAuditEventType.IdleSignOut);

        impEnd.Should().NotBeNull("ImpersonationEnd(idle_timeout) row missing");
        idleSignOut.Should().NotBeNull("IdleSignOut row missing");

        rows[0].EventType.Should().Be(LoginAuditEventType.ImpersonationEnd,
            "the impersonation close MUST be ordered BEFORE the session sign-out so the audit reads causally");
        rows[1].EventType.Should().Be(LoginAuditEventType.IdleSignOut);

        impEnd!.EffectiveTenantId.Should().Be(targetTenantId,
            "ImpersonationEnd belongs to the impersonated tenant");

        using var meta = JsonDocument.Parse(impEnd.MetadataJson!);
        meta.RootElement.GetProperty("reason").GetString()
            .Should().Be("idle_timeout");
    }

    // ─── helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a fresh client with a synthesised CSP-Admin identity AND
    /// flips the FakeTenantContext to <c>IsCspAdmin = true</c> so the
    /// Feature 048 impersonation endpoints accept the call.
    /// </summary>
    private (HttpClient client, string oid) CreateCspAdminClient()
    {
        var ctx = _factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.Status = TenantStatus.Active;

        var oid = $"oid-impersonate-{Guid.NewGuid():N}";
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "CSP Admin Test");
        // `CSP.Admin` drives the legacy claims-based gate on /api/tenants;
        // `Compliance.Administrator` lets the synthesised identity pass the
        // ComplianceAuthorizationMiddleware role check. The fixture's
        // FakeTenantContext below is what the Feature 048 endpoint actually
        // consults for IsCspAdmin — the claim is belt-and-braces.
        client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin,Compliance.Administrator");
        return (client, oid);
    }

    /// <summary>
    /// Returns a fresh client with a synthesised identity that has
    /// CSP-Admin privileges (the only persona that can possess a valid
    /// impersonation cookie). Used by tests that craft their own
    /// impersonation cookie out-of-band (expired / idle scenarios).
    /// /api/auth/* is exempted from ComplianceAuthorizationMiddleware so
    /// no compliance role is required.
    /// </summary>
    private (HttpClient client, string oid) CreateAuthenticatedClient()
    {
        // CSP-Admin = true on the fake tenant context so the
        // TenantStampingSaveChangesInterceptor permits writing an audit
        // row stamped on the IMPERSONATED tenant (a tenant other than
        // the actor's home tenant). In production this is set by
        // TenantResolutionMiddleware from the CSP.Admin role claim.
        var ctx = _factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.Status = TenantStatus.Active;

        var oid = $"oid-cookie-{Guid.NewGuid():N}";
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            HandleCookies = false, // we manage the cookie ourselves
        });
        client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", EntraTidForTenantA.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Auth Cookie Test");
        // /api/auth/* is exempted from ComplianceAuthorizationMiddleware
        // (see middleware comment) so no compliance role is required here.
        return (client, oid);
    }

    private async Task<string> GetImpersonationCookieNameAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var impersonation = ServiceProviderServiceExtensions
            .GetRequiredService<ITenantImpersonationService>(scope.ServiceProvider);
        return impersonation.CookieName;
    }

    private async Task<string> MintValidImpersonationCookieAsync(
        string actorOid,
        Guid impersonatedTenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var impersonation = ServiceProviderServiceExtensions
            .GetRequiredService<ITenantImpersonationService>(scope.ServiceProvider);
        var (value, _) = impersonation.IssueToken(
            actorOid,
            MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            impersonatedTenantId);
        return value;
    }

    /// <summary>
    /// Mints an impersonation cookie whose payload is signed correctly
    /// but whose <c>nbf</c> + <c>exp</c> are entirely in the past, so
    /// <see cref="ITenantImpersonationService.Validate"/> returns null
    /// for the expiry reason (not signature reason). This is the
    /// failure mode the auto-expiry path MUST detect + audit.
    /// </summary>
    private static string MintExpiredImpersonationCookie(
        string actorOid,
        Guid actorHomeTenantId,
        Guid impersonatedTenantId)
    {
        // Mirror the production signing key the fixture configures via
        // ATO_Auth__Impersonation__SigningKey in MultiTenantWebApplicationFactory.
        const string signingKey = "ato-copilot-tests-impersonation-signing-key-stable-32B!";
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(signingKey);
        if (keyBytes.Length < 32)
        {
            var padded = new byte[32];
            Buffer.BlockCopy(keyBytes, 0, padded, 0, keyBytes.Length);
            keyBytes = padded;
        }
        var symmetricKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes);
        var signing = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            symmetricKey,
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        // 5 minutes ago — comfortably outside the service's 30s clock-skew
        // tolerance so Validate definitely fails with SecurityTokenExpiredException.
        var notBefore = DateTimeOffset.UtcNow.AddHours(-2).UtcDateTime;
        var expires = DateTimeOffset.UtcNow.AddMinutes(-5).UtcDateTime;

        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, actorOid),
            new("actor_tid", actorHomeTenantId.ToString("D")),
            new("eff_tid", impersonatedTenantId.ToString("D")),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString("N")),
        };
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "ato-copilot/impersonation",
            audience: "ato-copilot/dashboard",
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: signing);
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler { MapInboundClaims = false }
            .WriteToken(token);
    }

    private async Task WireEntraTidOnTenantAAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var tenantA = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
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

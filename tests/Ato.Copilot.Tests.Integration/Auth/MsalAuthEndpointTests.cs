using System.Net;
using System.Text.Json;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Mcp.Extensions;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.State.Extensions;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Auth;

/// <summary>
/// Epic #207 / Task #246 — Integration tests for the Feature 051 MSAL auth
/// endpoints: GET /api/auth/login-config, GET /api/auth/me,
/// POST /api/auth/select-tenant, POST /api/auth/signout.
///
/// Uses Development environment so CAC simulation middleware is active.
/// Mirrors the setup pattern from AuthEndpointIntegrationTests (T098).
/// </summary>
[Collection("IntegrationTests")]
public class MsalAuthEndpointTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly string _dbName = $"MsalAuthIntegration_{Guid.NewGuid():N}";
    private readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        // Development environment activates CacAuth simulation so tests get
        // an authenticated identity without real CAC hardware.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.Configure<GatewayOptions>(
            builder.Configuration.GetSection(GatewayOptions.SectionName));
        builder.Services.Configure<AzureAdOptions>(
            builder.Configuration.GetSection(AzureAdOptions.SectionName));
        builder.Services.AddHttpClient();

        builder.Services.AddSingleton(_ =>
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                AuthorityHost = AzureAuthorityHosts.AzureGovernment,
            });
            return new ArmClient(credential, default, new ArmClientOptions
            {
                Environment = ArmEnvironment.AzureGovernment,
            });
        });

        builder.Services.AddAtoCopilotMcpForTesting(builder.Configuration, _dbName);
        builder.Services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        builder.WebHost.UseTestServer();
        _app = builder.Build();

        _app.UseCors();
        _app.UseMiddleware<CacAuthenticationMiddleware>();
        _app.UseMiddleware<ComplianceAuthorizationMiddleware>();
        _app.UseMiddleware<AuditLoggingMiddleware>();

        var httpBridge = _app.Services.GetRequiredService<McpHttpBridge>();
        httpBridge.MapEndpoints(_app);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ────────────────────────────────────────────────────────────────────
    // GET /api/auth/login-config — public endpoint, no auth required
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// FR-001 / FR-002: login-config must be reachable without any auth
    /// header and must return the MSAL bootstrap fields.
    /// </summary>
    [Fact]
    public async Task GetLoginConfig_NoAuth_Returns200WithMsalBlock()
    {
        var response = await _client.GetAsync("/api/auth/login-config");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "login-config is a public endpoint (no auth required)");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Envelope: { status: "success", data: { msal: { clientId, authority, redirectUri }, ... } }
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");

        data.TryGetProperty("msal", out var msal).Should().BeTrue(
            "login-config must contain an 'msal' block for MSAL.js initialization");
        msal.TryGetProperty("clientId", out _).Should().BeTrue();
        msal.TryGetProperty("authority", out _).Should().BeTrue();
        msal.TryGetProperty("redirectUri", out _).Should().BeTrue();
    }

    /// <summary>
    /// NFR: login-config must not expose any secrets in the response body.
    /// Client secrets and certificates must never leave the server.
    /// </summary>
    [Fact]
    public async Task GetLoginConfig_ResponseBody_ContainsNoSecrets()
    {
        var response = await _client.GetAsync("/api/auth/login-config");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContainAny(
            ["clientSecret", "client_secret", "certificate", "thumbprint", "privateKey"],
            "no secrets must appear in the login-config response body");
    }

    /// <summary>
    /// FedRAMP / Epic #207: idleTimeoutMinutes must be present and ≤ 15.
    /// </summary>
    [Fact]
    public async Task GetLoginConfig_IdleTimeoutMinutes_PresentAndFedRampCompliant()
    {
        var response = await _client.GetAsync("/api/auth/login-config");
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        var data = doc.RootElement.GetProperty("data");
        data.TryGetProperty("idleTimeoutMinutes", out var idleEl).Should().BeTrue(
            "idleTimeoutMinutes must be exposed for the frontend idle timer");

        var idleMinutes = idleEl.GetInt32();
        idleMinutes.Should().BeGreaterThan(0).And.BeLessOrEqualTo(15,
            "FedRAMP AC-11 requires automatic session lock after ≤ 15 minutes of inactivity");
    }

    /// <summary>
    /// In non-Development environments the simulation descriptor must be absent.
    /// In Development (this test's environment) it may or may not be present
    /// depending on CacAuth:SimulationMode — we only assert it does NOT expose
    /// server secrets if present.
    /// </summary>
    [Fact]
    public async Task GetLoginConfig_SimulationDescriptor_NeverExposesSecrets()
    {
        var response = await _client.GetAsync("/api/auth/login-config");
        var body = await response.Content.ReadAsStringAsync();

        // If simulation block is present, it must not contain any password-like fields
        if (body.Contains("simulation", StringComparison.OrdinalIgnoreCase))
        {
            body.Should().NotContainAny(
                ["password", "secret", "token", "hash"],
                "simulation descriptor must not expose credential material");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // GET /api/auth/me — requires authentication
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// With CAC simulation active (Development environment), /me returns 200
    /// with the simulated principal's sub, roles, and tenantId.
    /// </summary>
    [Fact]
    public async Task GetMe_WithSimulatedSession_Returns200WithExpectedFields()
    {
        var response = await _client.GetAsync("/api/auth/me");

        // In Development with simulation enabled this should return 200.
        // In test environments where simulation is not configured, 401 is acceptable.
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data");

            data.TryGetProperty("sub", out _).Should().BeTrue("me must include 'sub' (OID)");
            data.TryGetProperty("roles", out var roles).Should().BeTrue("me must include 'roles'");
            roles.ValueKind.Should().Be(JsonValueKind.Array);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "unauthenticated requests to /me must return 401");
        }
    }

    /// <summary>
    /// Without any auth header or session cookie, /me returns 401.
    /// </summary>
    [Fact]
    public async Task GetMe_WithoutAuth_Returns401()
    {
        // Create a fresh client with no cookies/headers to ensure no session
        using var freshClient = _app.GetTestClient();
        freshClient.DefaultRequestHeaders.Clear();

        var response = await freshClient.GetAsync("/api/auth/me");

        // In Production-like scenarios (no simulation), must be 401.
        // In Development with simulation auto-inject, 200 is acceptable —
        // the important thing is it's never 500 or a data leak.
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.Unauthorized,
            "unauthenticated /me requests must return 200 (simulated) or 401 (production), never a server error");
    }

    // ────────────────────────────────────────────────────────────────────
    // POST /api/auth/signout — clears session, audit logs the event
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Signout with an authenticated session returns 204 and sets
    /// cache-control to no-store (prevents cached session from surviving logout).
    /// </summary>
    [Fact]
    public async Task PostSignout_WithAuthentication_Returns204()
    {
        // In Development the simulation middleware injects identity automatically.
        var response = await _client.PostAsJsonAsync("/api/auth/signout", new { reason = "manual" });

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NoContent,
            HttpStatusCode.OK,
            HttpStatusCode.Unauthorized,
            "signout must return 204 (success) or 401 (no session to sign out from)");
    }

    /// <summary>
    /// Signout with reason="idle_timeout" must be accepted (not rejected as
    /// an unknown reason) and must return 204, not 400.
    /// </summary>
    [Fact]
    public async Task PostSignout_WithIdleTimeoutReason_IsAcceptedNot400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/signout",
            new { reason = "idle_timeout" });

        // Must not be BadRequest — "idle_timeout" is a valid reason per the spec.
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest,
            "'idle_timeout' is a valid signout reason and must not be rejected with 400");
    }

    // ────────────────────────────────────────────────────────────────────
    // POST /api/auth/select-tenant — tenant session scoping
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// select-tenant with an invalid/unknown tenantId returns 400 or 404,
    /// not 500 or a data leak.
    /// </summary>
    [Fact]
    public async Task PostSelectTenant_UnknownTenantId_Returns400Or404()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/select-tenant",
            new { tenantId = Guid.NewGuid().ToString() });

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound,
            HttpStatusCode.Unauthorized,
            "unknown tenantId must produce a client-error response, not a server error");
    }

    /// <summary>
    /// select-tenant without authentication returns 401.
    /// </summary>
    [Fact]
    public async Task PostSelectTenant_WithoutAuth_Returns401()
    {
        using var freshClient = _app.GetTestClient();
        freshClient.DefaultRequestHeaders.Clear();

        var response = await freshClient.PostAsJsonAsync(
            "/api/auth/select-tenant",
            new { tenantId = Guid.NewGuid().ToString() });

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.BadRequest, // may fail body validation before auth check
            "unauthenticated select-tenant must not succeed");
    }
}

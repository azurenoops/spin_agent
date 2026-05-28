using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Mcp;
using Ato.Copilot.Tests.Integration.Tenancy;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Auth;

/// <summary>
/// Feature 051 T041 [US1] — integration coverage for
/// <c>GET /api/auth/login-config</c>. The endpoint is public
/// (no Bearer required) and emits the standard envelope shape per
/// <c>contracts/http-api.md § 1</c>.
/// </summary>
/// <remarks>
/// The host runs under <c>ASPNETCORE_ENVIRONMENT=Testing</c>, so the
/// simulation descriptor MUST be null (it's only emitted when env =
/// <c>Development</c> AND <c>CacAuth:SimulationMode</c> = true per FR-023).
/// </remarks>
[Collection("Tenancy")]
public class LoginConfigEndpointTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly HttpClient _client;

    public LoginConfigEndpointTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_LoginConfig_Returns200_WithStandardEnvelope()
    {
        var resp = await _client.GetAsync("/api/auth/login-config");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        body.TryGetProperty("data", out _).Should().BeTrue();
        body.TryGetProperty("metadata", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_LoginConfig_Data_HasAllRequiredFields()
    {
        var resp = await _client.GetAsync("/api/auth/login-config");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");

        data.TryGetProperty("branding", out var branding).Should().BeTrue();
        branding.TryGetProperty("deploymentName", out _).Should().BeTrue();

        data.TryGetProperty("defaultMethod", out _).Should().BeTrue();
        data.TryGetProperty("enabledMethods", out var methods).Should().BeTrue();
        methods.ValueKind.Should().Be(JsonValueKind.Array);
        methods.GetArrayLength().Should().BeGreaterThan(0,
            "FR-001/FR-002 require at least one configured method (CAC or Entra)");
        foreach (var m in methods.EnumerateArray())
        {
            m.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
            m.GetProperty("displayName").GetString().Should().NotBeNullOrEmpty();
        }

        data.TryGetProperty("cloud", out _).Should().BeTrue();
        data.TryGetProperty("idleTimeoutMinutes", out _).Should().BeTrue();
        data.TryGetProperty("rememberTenantCookieDays", out _).Should().BeTrue();
        data.TryGetProperty("msal", out _).Should().BeTrue();
        data.TryGetProperty("simulation", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Get_LoginConfig_OutsideDevelopment_SimulationIsNull()
    {
        // Factory pins ASPNETCORE_ENVIRONMENT=Testing — not Development,
        // so per FR-023 the simulation descriptor must be omitted.
        var resp = await _client.GetAsync("/api/auth/login-config");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("simulation").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_LoginConfig_SetsCacheControlNoStore()
    {
        var resp = await _client.GetAsync("/api/auth/login-config");

        resp.Headers.CacheControl.Should().NotBeNull(
            "branding can change on deploy and the simulation descriptor MUST NOT be cached across environments per § 1.7");
        resp.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Get_LoginConfig_RequiresNoAuthentication()
    {
        // No Authorization header — endpoint must be reachable per § 1.1
        // (the SPA hits it BEFORE any authentication occurs).
        var resp = await _client.GetAsync("/api/auth/login-config");

        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

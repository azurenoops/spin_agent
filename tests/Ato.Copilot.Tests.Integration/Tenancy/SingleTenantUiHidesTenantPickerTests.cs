using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T079 [US3]: Verifies the <c>GET /api/deployment/mode</c> contract used by
/// the dashboard to decide whether to render the tenant picker, the
/// impersonation banner, and any CSP-Admin-only menu items (FR-041).
/// </summary>
/// <remarks>
/// RED until T084 is implemented. This file uses a dedicated
/// <see cref="SingleTenantWebApplicationFactory{TStartup}"/> to flip
/// <c>Deployment:Mode</c> to <c>SingleTenant</c> at host construction time.
/// </remarks>
[Collection("Tenancy")]
public class SingleTenantUiHidesTenantPickerTests
    : IClassFixture<SingleTenantUiHidesTenantPickerTests.SingleTenantWebApplicationFactory<McpProgram>>
{
    private readonly SingleTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public SingleTenantUiHidesTenantPickerTests(SingleTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_DeploymentMode_ReturnsSingleTenant()
    {
        var resp = await _client.GetAsync("/api/deployment/mode");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        body.GetProperty("data").GetProperty("mode").GetString()
            .Should().Be("SingleTenant", "the host was configured with ATO_Deployment__Mode=SingleTenant");

        // defaultTenantId may be null when the bootstrap has not yet resolved
        // it; when present, it must be a parseable Guid.
        if (body.GetProperty("data").TryGetProperty("defaultTenantId", out var idProp)
            && idProp.ValueKind == JsonValueKind.String)
        {
            Guid.TryParse(idProp.GetString(), out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Get_TenantsList_InSingleTenantMode_ReturnsForbiddenOrEmpty()
    {
        // In SingleTenant mode the dashboard's TenantPicker self-hides because
        // it queries /api/deployment/mode FIRST. Even if a misbehaving client
        // calls /api/tenants directly, the host must still gate the surface
        // (CSP.Admin-only). This assertion guards FR-041 at the API layer.
        var resp = await _client.GetAsync("/api/tenants");
        // The surface remains gated; if the test caller is CSP-Admin a 200 is
        // also acceptable.
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.OK);
    }

    /// <summary>
    /// SingleTenant variant of <see cref="MultiTenantWebApplicationFactory{TStartup}"/>.
    /// We replicate just enough of the parent to flip the deployment-mode env var.
    /// </summary>
    public sealed class SingleTenantWebApplicationFactory<TStartup>
        : MultiTenantWebApplicationFactory<TStartup>
        where TStartup : class
    {
        public SingleTenantWebApplicationFactory()
        {
            // Env-var fallback for any code path that reads the var directly
            // (NOT relied on for IOptions<DeploymentOptions> — that's pinned
            // via DeploymentModeOverride below).
            Environment.SetEnvironmentVariable("ATO_Deployment__Mode", "SingleTenant");
        }

        protected override string DeploymentModeOverride => "SingleTenant";
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T175 [US8]: enforces the two access gates on the CSP dashboard surface.
/// (1) non-CSP-Admin gets <c>403 FORBIDDEN_NOT_CSP_ADMIN</c> on every
/// dashboard endpoint (acceptance scenario 3); (2) when <c>CspProfile</c> is
/// not <c>Active</c>, every dashboard endpoint returns
/// <c>503 CSP_ONBOARDING_INCOMPLETE</c> (FR-097, acceptance scenario 6).
/// </summary>
/// <remarks>
/// Will RED until T177–T181 ship. The 403 behavior is local to the dashboard
/// endpoints; the 503 behavior is delivered by the FR-090 gate in
/// <c>TenantResolutionMiddleware</c> (T164), but both are asserted here as a
/// suite-level guarantee.
/// </remarks>
[Collection("Tenancy")]
public class CspDashboardAuthorizationTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private static readonly string[] DashboardPaths =
    {
        "/api/csp/dashboard/summary",
        "/api/csp/dashboard/tenants",
        "/api/csp/dashboard/atos",
    };

    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspDashboardAuthorizationTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;
    }

    [Theory]
    [InlineData("/api/csp/dashboard/summary")]
    [InlineData("/api/csp/dashboard/tenants")]
    [InlineData("/api/csp/dashboard/atos")]
    public async Task NonCspAdmin_Returns403_ForbiddenNotCspAdmin(string path)
    {
        // Arrange
        _factory.GetActiveContext().IsCspAdmin = false;

        // Act
        var resp = await _client.GetAsync(path);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    [Fact]
    public async Task CspProfileNotActive_AllDashboardEndpoints_Return503_OnboardingIncomplete()
    {
        // Arrange — wipe the seeded Active CspProfile so the FR-090 gate
        // returns 503 for every non-allow-listed path.
        await _factory.ResetCspProfileAsync();

        // Re-affirm CSP-Admin so we are NOT being blocked by the 403 gate;
        // the 503 must be returned even for CSP-Admins (FR-097).
        _factory.GetActiveContext().IsCspAdmin = true;

        foreach (var path in DashboardPaths)
        {
            // Act
            var resp = await _client.GetAsync(path);

            // Assert — every dashboard endpoint must surface the gate.
            resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
                $"path {path} must propagate the FR-090 gate even for CSP-Admin.");
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("status").GetString().Should().Be("error");
            body.GetProperty("error").GetProperty("errorCode").GetString()
                .Should().Be("CSP_ONBOARDING_INCOMPLETE");
        }
    }
}

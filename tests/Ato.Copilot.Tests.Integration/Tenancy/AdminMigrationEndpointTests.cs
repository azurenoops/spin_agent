using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using Ato.Copilot.Mcp.Endpoints;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T120 [Phase 9 / FR-073..FR-076] / UF-019 [Wave 8]: Validates the
/// <c>/api/admin/migrate-to-multitenant</c> contract surface.
/// </summary>
/// <remarks>
/// The UF-019 tests (marked with the region comment) validate the new
/// <c>X-Admin-Confirm-Migration</c> guard added in Wave 8. Because the test
/// factory does not configure <c>Auth:Branding:DeploymentName</c>, the
/// deployment name defaults to <c>"ATO Copilot"</c> (per the endpoint's
/// null/empty fallback), so tests that need to pass the guard send
/// <c>X-Admin-Confirm-Migration: ATO Copilot</c>.
/// </remarks>
[Collection("Tenancy")]
public class AdminMigrationEndpointTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantA;

    /// <summary>
    /// Deployment name used by the confirmation guard when no
    /// <c>Auth:Branding:DeploymentName</c> is configured in the test factory.
    /// </summary>
    private const string DefaultDeploymentName = "ATO Copilot";

    public AdminMigrationEndpointTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _tenantA = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;

        var ctx = factory.GetActiveContext();
        ctx.TenantId = _tenantA;
        ctx.IsCspAdmin = true;
        ctx.Status = TenantStatus.Active;
    }

    // ─── Preview endpoint ─────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_AsCspAdmin_Returns200_WithPerTableRows()
    {
        var resp = await _client.GetAsync("/api/admin/migrate-to-multitenant/preview");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var tables = body.GetProperty("data").GetProperty("tables");
        tables.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Preview_AsNonCspAdmin_Returns403()
    {
        _factory.GetActiveContext().IsCspAdmin = false;

        var resp = await _client.GetAsync("/api/admin/migrate-to-multitenant/preview");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    // ─── Execute — existing contract tests (updated to send confirmation header) ─

    [Fact]
    public async Task Execute_WithoutDefaultTenantId_Returns400()
    {
        // The confirmation guard runs AFTER the body validation for the Guid check,
        // but the guard runs AFTER IsCspAdmin. We send the header so the guard
        // is not the test's bottleneck; the 400 here is for the missing Guid.
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/admin/migrate-to-multitenant");
        req.Content = JsonContent.Create(new { defaultTenantId = Guid.Empty });
        req.Headers.Add(AdminMigrationEndpoints.ConfirmationHeaderName, DefaultDeploymentName);

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("INVALID_REQUEST");
    }

    [Fact]
    public async Task Execute_AsCspAdmin_WithConfirmationHeader_ReturnsReport_AndIsIdempotent()
    {
        var defaultId = _tenantA;

        using var req1 = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/admin/migrate-to-multitenant");
        req1.Content = JsonContent.Create(new { defaultTenantId = defaultId, installRls = false });
        req1.Headers.Add(AdminMigrationEndpoints.ConfirmationHeaderName, DefaultDeploymentName);

        var first = await _client.SendAsync(req1);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        firstBody.GetProperty("status").GetString().Should().Be("success");
        firstBody.GetProperty("data").GetProperty("defaultTenantId").GetGuid().Should().Be(defaultId);

        // Idempotency: re-running should still succeed.
        using var req2 = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/admin/migrate-to-multitenant");
        req2.Content = JsonContent.Create(new { defaultTenantId = defaultId, installRls = false });
        req2.Headers.Add(AdminMigrationEndpoints.ConfirmationHeaderName, DefaultDeploymentName);

        var second = await _client.SendAsync(req2);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("status").GetString().Should().Be("success");
    }

    [Fact]
    public async Task Execute_AsNonCspAdmin_Returns403()
    {
        _factory.GetActiveContext().IsCspAdmin = false;

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/admin/migrate-to-multitenant");
        req.Content = JsonContent.Create(new { defaultTenantId = _tenantA });
        req.Headers.Add(AdminMigrationEndpoints.ConfirmationHeaderName, DefaultDeploymentName);

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── UF-019 confirmation guard ────────────────────────────────────────────

    [Fact]
    public async Task Execute_WithoutConfirmationHeader_Returns400_ConfirmationRequired()
    {
        // Direct API call with no confirmation header — the guard must reject it.
        var resp = await _client.PostAsJsonAsync(
            "/api/admin/migrate-to-multitenant",
            new { defaultTenantId = _tenantA, installRls = false });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("MIGRATION_CONFIRMATION_REQUIRED");
        // Suggestion is present and mentions the header name.
        body.GetProperty("error").GetProperty("suggestion").GetString()
            .Should().Contain(AdminMigrationEndpoints.ConfirmationHeaderName);
    }

    [Fact]
    public async Task Execute_WithEmptyConfirmationHeader_Returns400_ConfirmationRequired()
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/admin/migrate-to-multitenant");
        req.Content = JsonContent.Create(new { defaultTenantId = _tenantA, installRls = false });
        req.Headers.Add(AdminMigrationEndpoints.ConfirmationHeaderName, " ");

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("MIGRATION_CONFIRMATION_REQUIRED");
    }

    [Fact]
    public async Task Execute_WithWrongConfirmationValue_Returns400_ConfirmationMismatch()
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/admin/migrate-to-multitenant");
        req.Content = JsonContent.Create(new { defaultTenantId = _tenantA, installRls = false });
        req.Headers.Add(AdminMigrationEndpoints.ConfirmationHeaderName, "wrong-deployment-name");

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("MIGRATION_CONFIRMATION_MISMATCH");
        // Suggestion must tell the user what the correct name is.
        body.GetProperty("error").GetProperty("suggestion").GetString()
            .Should().Contain(DefaultDeploymentName);
    }

    [Fact]
    public async Task Execute_WithCaseInsensitiveConfirmationValue_Returns200()
    {
        // The guard is case-insensitive, so "ato copilot" must be accepted.
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/admin/migrate-to-multitenant");
        req.Content = JsonContent.Create(new { defaultTenantId = _tenantA, installRls = false });
        req.Headers.Add(AdminMigrationEndpoints.ConfirmationHeaderName, "ato copilot");

        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

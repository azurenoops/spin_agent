using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T172 [US8]: validates the <c>/api/csp/dashboard/*</c> contract surface
/// against <c>specs/048-tenant-isolation/contracts/csp-dashboard.openapi.yaml</c>.
/// Exercises status codes, the success envelope, pagination bounds, sort/filter
/// validation, and 422 VALIDATION_FAILED responses.
/// </summary>
/// <remarks>
/// RED until T177–T181 (ICspDashboardService / CspDashboardService /
/// CspDashboardEndpoints) are implemented. The fixture seeds the host in
/// MultiTenant mode with an Active CspProfile (per-class fresh SQLite file),
/// and the FakeTenantContext is configured with <c>IsCspAdmin = true</c> so
/// the endpoints are reachable.
/// </remarks>
[Collection("Tenancy")]
public class CspDashboardContractTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspDashboardContractTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;
    }

    // ────────────────────────────── Summary ──────────────────────────────

    [Fact]
    public async Task Get_Summary_AsCspAdmin_Returns200_WithExpectedShape()
    {
        // Arrange — fixture state.

        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/summary");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");

        var data = body.GetProperty("data");
        var counts = data.GetProperty("tenantCounts");
        counts.GetProperty("active").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        counts.GetProperty("suspended").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        counts.GetProperty("disabled").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        counts.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(0);

        data.GetProperty("disabledTenantCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("organizationCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("systemCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);

        var ato = data.GetProperty("atoStatusCounts");
        ato.GetProperty("authorized").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        ato.GetProperty("inProcess").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        ato.GetProperty("denied").GetInt32().Should().BeGreaterThanOrEqualTo(0);

        var sev = data.GetProperty("openFindingsBySeverity");
        sev.GetProperty("critical").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        sev.GetProperty("high").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        sev.GetProperty("moderate").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        sev.GetProperty("low").GetInt32().Should().BeGreaterThanOrEqualTo(0);

        data.GetProperty("openPoamCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("openDeviationCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.TryGetProperty("generatedAt", out var generated).Should().BeTrue();
        generated.GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    // ────────────────────────────── Tenants ──────────────────────────────

    [Fact]
    public async Task Get_Tenants_DefaultPaging_Returns200_WithEnvelopeShape()
    {
        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/tenants");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");

        var data = body.GetProperty("data");
        data.GetProperty("page").GetInt32().Should().Be(1);
        data.GetProperty("pageSize").GetInt32().Should().Be(50);
        data.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        var items = data.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThanOrEqualTo(2,
            "the fixture pre-seeds Tenant A and Tenant B.");

        var first = items[0];
        first.TryGetProperty("tenantId", out _).Should().BeTrue();
        first.TryGetProperty("displayName", out _).Should().BeTrue();
        first.GetProperty("status").GetString()
            .Should().BeOneOf("Active", "Suspended", "Disabled");
        first.GetProperty("onboardingState").GetString()
            .Should().BeOneOf("Pending", "InWizard", "Active");
    }

    [Fact]
    public async Task Get_Tenants_PageSizeAboveMax_Returns422_ValidationFailed()
    {
        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/tenants?pageSize=201");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Get_Tenants_InvalidSortField_Returns422_ValidationFailed()
    {
        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/tenants?sort=NotAField");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Get_Tenants_InvalidStatusFilter_Returns422_ValidationFailed()
    {
        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/tenants?status=Frozen");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    // ────────────────────────────── ATOs ──────────────────────────────

    [Fact]
    public async Task Get_Atos_DefaultPaging_Returns200_WithEnvelopeShape()
    {
        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/atos");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");

        var data = body.GetProperty("data");
        data.GetProperty("page").GetInt32().Should().Be(1);
        data.GetProperty("pageSize").GetInt32().Should().Be(50);
        data.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Get_Atos_InvalidDecisionStatus_Returns422_ValidationFailed()
    {
        // Act — "Granted" is not a contract enum member.
        var resp = await _client.GetAsync("/api/csp/dashboard/atos?decisionStatus=Granted");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Get_Atos_InvalidDecisionType_Returns422_ValidationFailed()
    {
        // Act — "P-ATO" is not a contract enum member.
        var resp = await _client.GetAsync("/api/csp/dashboard/atos?decisionType=P-ATO");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Get_Atos_InvalidSinceFormat_Returns422_ValidationFailed()
    {
        // Act
        var resp = await _client.GetAsync("/api/csp/dashboard/atos?since=not-a-date");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }
}

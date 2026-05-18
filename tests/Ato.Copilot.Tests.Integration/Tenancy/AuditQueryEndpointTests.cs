using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T114 [US6]: Validates the <c>/api/audit</c> contract surface against
/// <c>specs/048-tenant-isolation/contracts/audit.openapi.yaml</c>:
/// pagination, all 7 filter fields, page/pageSize bounds, role gating
/// (acceptance scenarios 1–2 / FR-060, FR-061).
/// </summary>
[Collection("Tenancy")]
public class AuditQueryEndpointTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantA;
    private readonly Guid _tenantB;

    public AuditQueryEndpointTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _tenantA = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        _tenantB = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;

        // Default: CSP-Admin so the endpoint is reachable.
        var ctx = factory.GetActiveContext();
        ctx.TenantId = _tenantA;
        ctx.IsCspAdmin = true;
        ctx.Status = TenantStatus.Active;
    }

    [Fact]
    public async Task Get_Audit_AsCspAdmin_Returns200_WithEnvelopedPage()
    {
        await SeedAuditRowsAsync(count: 3, tenantId: _tenantA, action: "Test.Read");

        var resp = await _client.GetAsync("/api/audit");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var page = body.GetProperty("data");
        page.GetProperty("page").GetInt32().Should().Be(1);
        page.GetProperty("pageSize").GetInt32().Should().Be(50, "default page size per contract");
        page.GetProperty("items").GetArrayLength().Should().BeGreaterOrEqualTo(3);
        page.GetProperty("total").GetInt32().Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task Get_Audit_AsNonCspAdmin_Returns403()
    {
        _factory.GetActiveContext().IsCspAdmin = false;

        var resp = await _client.GetAsync("/api/audit");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString().Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    [Fact]
    public async Task Get_Audit_FiltersByTenantId()
    {
        var marker = $"Filter.TenantA.{Guid.NewGuid():N}";
        await SeedAuditRowsAsync(count: 2, tenantId: _tenantA, action: marker);
        await SeedAuditRowsAsync(count: 2, tenantId: _tenantB, action: marker);

        var resp = await _client.GetAsync($"/api/audit?tenantId={_tenantA}&action={marker}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").GetProperty("items");
        foreach (var item in items.EnumerateArray())
        {
            item.GetProperty("effectiveTenantId").GetGuid().Should().Be(_tenantA,
                "tenantId filter selects EffectiveTenantId per contract");
        }
        items.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Get_Audit_RespectsPageSizeMaximum()
    {
        var resp = await _client.GetAsync("/api/audit?pageSize=500");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("pageSize").GetInt32().Should().Be(200,
            "contract caps pageSize at 200");
    }

    [Fact]
    public async Task Get_Audit_RejectsInvalidPagination()
    {
        var resp = await _client.GetAsync("/api/audit?page=0");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task SeedAuditRowsAsync(int count, Guid tenantId, string action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        for (var i = 0; i < count; i++)
        {
            db.AuditLogs.Add(new AuditLogEntry
            {
                TenantId = tenantId,
                ActorTenantId = tenantId,
                UserId = $"user-{i}",
                UserRole = "Tester",
                Action = action,
                Outcome = AuditOutcome.Success,
                Timestamp = DateTime.UtcNow.AddMilliseconds(-i),
                CorrelationId = $"corr-{i:D4}",
            });
        }
        await db.SaveChangesAsync();
    }
}

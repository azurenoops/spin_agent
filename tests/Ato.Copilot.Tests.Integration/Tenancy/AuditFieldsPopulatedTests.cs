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
/// T115 [US6]: After an in-tenant write and an impersonation read, the
/// resulting <c>AuditLogEntry</c> rows surface through <c>/api/audit</c>
/// with all required attribution fields populated:
/// <c>ActorTenantId</c> / <c>EffectiveTenantId</c> / <c>ImpersonatedTenantId</c>
/// / <c>ActorOid</c> (mapped from <c>UserId</c>) / <c>Action</c> /
/// <c>Outcome</c> / <c>CorrelationId</c> (FR-052, FR-061).
/// </summary>
[Collection("Tenancy")]
public class AuditFieldsPopulatedTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantA;
    private readonly Guid _tenantB;

    public AuditFieldsPopulatedTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _tenantA = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        _tenantB = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;

        var ctx = factory.GetActiveContext();
        ctx.TenantId = _tenantA;
        ctx.IsCspAdmin = true;
        ctx.Status = TenantStatus.Active;
    }

    [Fact]
    public async Task TenantLocalRow_ExposesAllAttributionFields()
    {
        // Seed: an in-tenant write — actor in tenant A, no impersonation.
        var marker = $"Local.{Guid.NewGuid():N}";
        var correlation = $"corr-local-{Guid.NewGuid():N}";
        await SeedAsync(new AuditLogEntry
        {
            TenantId = _tenantA,
            ActorTenantId = _tenantA,
            ImpersonatedTenantId = null,
            UserId = "actor-oid-local",
            UserRole = "Administrator",
            Action = marker,
            Outcome = AuditOutcome.Success,
            CorrelationId = correlation,
        });

        var page = await GetAuditPageAsync($"/api/audit?action={marker}");
        var item = page.GetProperty("items")[0];

        item.GetProperty("actorOid").GetString().Should().Be("actor-oid-local");
        item.GetProperty("actorTenantId").GetGuid().Should().Be(_tenantA);
        item.GetProperty("effectiveTenantId").GetGuid().Should().Be(_tenantA);
        item.TryGetProperty("impersonatedTenantId", out var imp).Should().BeTrue();
        (imp.ValueKind == JsonValueKind.Null).Should().BeTrue("no impersonation on a local write");
        item.GetProperty("action").GetString().Should().Be(marker);
        item.GetProperty("outcome").GetString().Should().Be("Success");
        item.GetProperty("correlationId").GetString().Should().Be(correlation);
    }

    [Fact]
    public async Task ImpersonationRow_PopulatesActorAndImpersonatedFields()
    {
        // Seed: a CSP-Admin in tenant A impersonating tenant B reads / writes.
        var marker = $"Impersonate.{Guid.NewGuid():N}";
        var correlation = $"corr-imp-{Guid.NewGuid():N}";
        await SeedAsync(new AuditLogEntry
        {
            TenantId = _tenantB,                  // effective = impersonated
            ActorTenantId = _tenantA,
            ImpersonatedTenantId = _tenantB,
            UserId = "actor-oid-csp-admin",
            UserRole = "CspAdmin",
            Action = marker,
            Outcome = AuditOutcome.Success,
            CorrelationId = correlation,
        });

        var page = await GetAuditPageAsync($"/api/audit?action={marker}");
        var item = page.GetProperty("items")[0];

        item.GetProperty("actorTenantId").GetGuid().Should().Be(_tenantA);
        item.GetProperty("effectiveTenantId").GetGuid().Should().Be(_tenantB);
        item.GetProperty("impersonatedTenantId").GetGuid().Should().Be(_tenantB);
        item.GetProperty("correlationId").GetString().Should().Be(correlation);
    }

    private async Task SeedAsync(AuditLogEntry row)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.AuditLogs.Add(row);
        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> GetAuditPageAsync(string url)
    {
        var resp = await _client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data");
    }
}

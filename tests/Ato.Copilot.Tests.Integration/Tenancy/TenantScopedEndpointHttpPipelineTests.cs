using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// Issue #100 follow-up (item 1): integration tests that drive [TenantScoped]
/// endpoints through the full HTTP pipeline and assert responses ARE scoped
/// to the correct tenant. Tests do NOT use <c>accessor.Push()</c> shortcuts —
/// they rely entirely on the factory's <see cref="FakeTenantContext"/> being
/// picked up through the normal service-resolution path (which is wired by
/// <see cref="MultiTenantWebApplicationFactory{TProgram}"/> replacing the
/// scoped <c>ITenantContext</c> service).
///
/// The gap this closes: <c>TenantQueryFilterTests</c> calls
/// <c>accessor.Push()</c> directly, bypassing middleware. These tests prove
/// that responses are scoped even when the accessor is populated via the
/// standard service-replacement path that production middleware uses.
/// </summary>
[Collection("Tenancy")]
public class TenantScopedEndpointHttpPipelineTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantA;
    private readonly Guid _tenantB;

    public TenantScopedEndpointHttpPipelineTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _tenantA = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        _tenantB = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;

        // Default context: active, non-impersonated Tenant-A user.
        var ctx = factory.GetActiveContext();
        ctx.TenantId = _tenantA;
        ctx.IsCspAdmin = false;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;
    }

    // ─── Tests ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Tenant-A user: GET /api/dashboard/systems returns ONLY Tenant-A systems.
    /// Proves the EF tenant-filter is applied in the HTTP pipeline.
    /// </summary>
    [Fact]
    public async Task GetSystems_AsTenantA_ReturnsOnlyTenantASystems()
    {
        // Arrange
        var systemA = await SeedSystemAsync(_tenantA, "TenantA-System-Pipeline-1");
        await SeedSystemAsync(_tenantB, "TenantB-System-Pipeline-1");

        SetTenant(_tenantA, isCspAdmin: false);

        // Act
        var resp = await _client.GetAsync("/api/dashboard/systems");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").GetProperty("items");
        var ids = Enumerable.Range(0, items.GetArrayLength())
                            .Select(i => items[i].GetProperty("systemId").GetString())
                            .ToList();

        ids.Should().Contain(systemA.ToString(),
            because: "Tenant-A systems must be visible to Tenant-A user");
        ids.Should().NotContain(
            id => IsSystemOwnedByOtherTenant(id!, _tenantA),
            because: "Tenant-B systems must NOT be visible to Tenant-A user");
    }

    /// <summary>
    /// Tenant-B user: GET /api/dashboard/systems returns ONLY Tenant-B systems.
    /// Disjoint from the Tenant-A result set.
    /// </summary>
    [Fact]
    public async Task GetSystems_AsTenantB_ReturnsOnlyTenantBSystems()
    {
        // Arrange
        await SeedSystemAsync(_tenantA, "TenantA-System-Pipeline-2");
        var systemB = await SeedSystemAsync(_tenantB, "TenantB-System-Pipeline-2");

        SetTenant(_tenantB, isCspAdmin: false);

        // Act
        var resp = await _client.GetAsync("/api/dashboard/systems");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").GetProperty("items");
        var ids = Enumerable.Range(0, items.GetArrayLength())
                            .Select(i => items[i].GetProperty("systemId").GetString())
                            .ToList();

        ids.Should().Contain(systemB.ToString(),
            because: "Tenant-B systems must be visible to Tenant-B user");
        ids.Should().NotContain(
            id => IsSystemOwnedByOtherTenant(id!, _tenantB),
            because: "Tenant-A systems must NOT be visible to Tenant-B user");
    }

    /// <summary>
    /// FR-026: CSP-Admin without impersonation sees ALL tenants' systems.
    /// Proves that the tenant filter is correctly disabled for this role.
    /// </summary>
    [Fact]
    public async Task GetSystems_AsCspAdminWithoutImpersonation_SeesAllTenants()
    {
        // Arrange — ensure at least one system per tenant is in the DB.
        await SeedSystemAsync(_tenantA, "TenantA-System-CspAdmin-1");
        await SeedSystemAsync(_tenantB, "TenantB-System-CspAdmin-1");

        // CSP-Admin: TenantId resolved to their home tenant, NO impersonation.
        SetTenant(_tenantA, isCspAdmin: true, impersonatedTenantId: null);

        // Act
        var resp = await _client.GetAsync("/api/dashboard/systems");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").GetProperty("items");

        // Must see systems from BOTH tenants.
        var ids = Enumerable.Range(0, items.GetArrayLength())
                            .Select(i => items[i].GetProperty("systemId").GetString())
                            .ToHashSet();

        // Collect all seeded systems from both tenants to assert cross-tenant visibility.
        var systemsA = await GetSystemIdsForTenantAsync(_tenantA);
        var systemsB = await GetSystemIdsForTenantAsync(_tenantB);
        var allSeeded = systemsA.Concat(systemsB).ToList();

        allSeeded.Should().NotBeEmpty(because: "we seeded at least 2 systems");
        // Every seeded system must appear in the CSP-Admin's view.
        foreach (var id in allSeeded)
        {
            ids.Should().Contain(id.ToString(),
                because: $"CSP-Admin must see system {id} from any tenant");
        }
    }

    /// <summary>
    /// CSP-Admin WITH impersonation: sees only the impersonated tenant's systems.
    /// </summary>
    [Fact]
    public async Task GetSystems_AsCspAdminImpersonatingTenantB_SeesOnlyTenantBSystems()
    {
        // Arrange
        var systemA = await SeedSystemAsync(_tenantA, "TenantA-System-Impersonate-1");
        var systemB = await SeedSystemAsync(_tenantB, "TenantB-System-Impersonate-1");

        SetTenant(_tenantA, isCspAdmin: true, impersonatedTenantId: _tenantB);

        // Act
        var resp = await _client.GetAsync("/api/dashboard/systems");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").GetProperty("items");
        var ids = Enumerable.Range(0, items.GetArrayLength())
                            .Select(i => items[i].GetProperty("systemId").GetString())
                            .ToList();

        ids.Should().Contain(systemB.ToString(),
            because: "impersonated Tenant-B system must be visible");
        ids.Should().NotContain(systemA.ToString(),
            because: "Tenant-A system must NOT be visible when impersonating Tenant-B");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void SetTenant(Guid tenantId, bool isCspAdmin, Guid? impersonatedTenantId = null)
    {
        var ctx = _factory.GetActiveContext();
        ctx.TenantId = tenantId;
        ctx.IsCspAdmin = isCspAdmin;
        ctx.ImpersonatedTenantId = impersonatedTenantId;
        ctx.Status = TenantStatus.Active;
    }

    private async Task<Guid> SeedSystemAsync(Guid tenantId, string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var id = Guid.NewGuid();
        db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = id.ToString(),
            TenantId = tenantId,
            SystemName = name,
            Acronym = name[..Math.Min(8, name.Length)].ToUpperInvariant(),
            SystemType = "Major Application",
            MissionCriticality = MissionCriticality.Moderate,
            HostingEnvironment = "Azure Commercial",
            CurrentRmfPhase = RmfPhase.Categorize,
            CreatedBy = "test",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<List<Guid>> GetSystemIdsForTenantAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        return db.RegisteredSystems
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .Select(s => Guid.Parse(s.Id))
            .ToList();
    }

    /// <summary>
    /// Returns true when the given system id is owned by any tenant OTHER THAN <paramref name="ownTenantId"/>.
    /// Used to assert absence of cross-tenant leakage in the result set.
    /// </summary>
    private bool IsSystemOwnedByOtherTenant(string systemId, Guid ownTenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        return db.RegisteredSystems
            .IgnoreQueryFilters()
            .Any(s => s.Id == systemId && s.TenantId != ownTenantId);
    }
}

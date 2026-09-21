using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task Issue962_BusinessContext_SqliteRoundTripPreservesConcurrency()
    {
        // Arrange
        var systemId = (await SeedSystemAsync(_tenantA, "Business-context-roundtrip")).ToString();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.ControlImplementations.Add(new ControlImplementation
        {
            TenantId = _tenantA, RegisteredSystemId = systemId, ControlId = "AC-2",
            PolicyNarrative = "Policy text", TechnicalNarrative = "Technical text"
        });
        db.RmfRoleAssignments.Add(new RmfRoleAssignment
        {
            TenantId = _tenantA, RegisteredSystemId = systemId,
            UserId = "multi-tenant-test-user", UserDisplayName = "Synthetic owner",
            RmfRole = RmfRole.MissionOwner, IsActive = true, AssignedBy = "test"
        });
        await db.SaveChangesAsync();
        var endpoint = $"/api/dashboard/systems/{systemId}/business-context/AC-2";

        // Act
        var created = await _client.PutAsJsonAsync(endpoint, new { content = new string('x', 8000) });

        // Assert
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await created.Content.ReadFromJsonAsync<JsonElement>();
        var draftId = response.GetProperty("id").GetString();
        var staleDraft = await db.BusinessContextDrafts.SingleAsync(item => item.Id == draftId);
        var originalToken = staleDraft.RowVersion.ToArray();
        var updated = await _client.PutAsJsonAsync(endpoint, new { content = "Updated context" });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = await db.BusinessContextDrafts.AsNoTracking().SingleAsync(item => item.Id == draftId);
        stored.Content.Should().Be("Updated context");
        stored.RowVersion.Should().NotEqual(originalToken);
        staleDraft.Content = "Stale replacement";
        Func<Task> staleSave = () => db.SaveChangesAsync();
        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Theory]
    [InlineData("GET", "AC-2")]
    [InlineData("GET", "flagged-controls")]
    [InlineData("PUT", "AC-2")]
    [InlineData("POST", "flags")]
    public async Task Issue962_BusinessContext_DoesNotExposeOrModifyOtherTenant(string method, string suffix)
    {
        // Arrange
        var systemId = (await SeedSystemAsync(_tenantB, "Business-context-isolation")).ToString();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var implementation = new ControlImplementation
        {
            TenantId = _tenantB, RegisteredSystemId = systemId, ControlId = "AC-2"
        };
        db.ControlImplementations.Add(implementation);
        var draft = new BusinessContextDraft
        {
            TenantId = _tenantB, ControlImplementationId = implementation.Id,
            Content = "Tenant B synthetic context", AuthoredBy = "tenant-b-owner"
        };
        db.BusinessContextDrafts.Add(draft);
        await db.SaveChangesAsync();
        SetTenant(_tenantA, isCspAdmin: false);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/dashboard/systems/{systemId}/business-context/{suffix}");
        if (method != "GET")
            request.Content = JsonContent.Create(new { content = "Unauthorized replacement", controlId = "AC-2", isFlagged = true });

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("SYSTEM_NOT_FOUND");
        (await db.BusinessContextDrafts.IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.Id == draft.Id))
            .Content.Should().Be("Tenant B synthetic context");
        (await db.BusinessContextControlFlags.IgnoreQueryFilters().CountAsync(item => item.RegisteredSystemId == systemId)).Should().Be(0);
    }

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
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = body.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object
            ? dataEl.GetProperty("items")
            : body.GetProperty("items");
        var ids = Enumerable.Range(0, items.GetArrayLength())
                            .Select(i => items[i].GetProperty("systemId").GetString())
                            .ToList();

        ids.Should().Contain(systemA.ToString(),
            because: "Tenant-A systems must be visible to Tenant-A user");

        // Verify no Tenant-B systems leaked into result
        var tenantBSystemIds = await GetSystemIdsForTenantAsync(_tenantB);
        foreach (var bId in tenantBSystemIds)
        {
            ids.Should().NotContain(bId.ToString(),
                because: $"Tenant-B system {bId} must NOT be visible to Tenant-A user");
        }
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
        var items = body.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object
            ? dataEl.GetProperty("items")
            : body.GetProperty("items");
        var ids = Enumerable.Range(0, items.GetArrayLength())
                            .Select(i => items[i].GetProperty("systemId").GetString())
                            .ToList();

        ids.Should().Contain(systemB.ToString(),
            because: "Tenant-B systems must be visible to Tenant-B user");

        var tenantASystemIds = await GetSystemIdsForTenantAsync(_tenantA);
        foreach (var aId in tenantASystemIds)
        {
            ids.Should().NotContain(aId.ToString(),
                because: $"Tenant-A system {aId} must NOT be visible to Tenant-B user");
        }
    }

    /// <summary>
    /// FR-026: CSP-Admin without impersonation sees ALL tenants' systems.
    /// Proves that the tenant filter is correctly disabled for this role.
    /// </summary>
    [Fact]
    public async Task GetSystems_AsCspAdminWithoutImpersonation_SeesAllTenants()
    {
        // Arrange — ensure at least one system per tenant is in the DB.
        var systemA = await SeedSystemAsync(_tenantA, "TenantA-System-CspAdmin-1");
        var systemB = await SeedSystemAsync(_tenantB, "TenantB-System-CspAdmin-1");

        // CSP-Admin: TenantId resolved to their home tenant, NO impersonation.
        SetTenant(_tenantA, isCspAdmin: true, impersonatedTenantId: null);

        // Act
        var resp = await _client.GetAsync("/api/dashboard/systems");

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object
            ? dataEl.GetProperty("items")
            : body.GetProperty("items");
        var ids = Enumerable.Range(0, items.GetArrayLength())
                            .Select(i => items[i].GetProperty("systemId").GetString())
                            .ToHashSet();

        // Must see systems from BOTH tenants.
        ids.Should().Contain(systemA.ToString(),
            because: "CSP-Admin must see Tenant-A systems");
        ids.Should().Contain(systemB.ToString(),
            because: "CSP-Admin must see Tenant-B systems (FR-026)");
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
        var items = body.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object
            ? dataEl.GetProperty("items")
            : body.GetProperty("items");
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
            Name = name,
            Acronym = name[..Math.Min(8, name.Length)].ToUpperInvariant(),
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionSupport,
            HostingEnvironment = "Azure Commercial",
            CurrentRmfStep = RmfPhase.Prepare,
            CreatedBy = "test",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<List<Guid>> GetSystemIdsForTenantAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        return await db.RegisteredSystems
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .Select(s => Guid.Parse(s.Id))
            .ToListAsync();
    }
}

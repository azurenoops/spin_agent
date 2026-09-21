using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
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

    [Theory]
    [InlineData(RmfRole.MissionOwner, "MissionAndPurpose")]
    [InlineData(RmfRole.SystemOwner, "UsersAndAccess")]
    [InlineData(RmfRole.Issm, "EnvironmentAndDeployment")]
    [InlineData(RmfRole.MissionOwner, "DataTypes")]
    [InlineData(RmfRole.SystemOwner, "PortsProtocolsAndServices")]
    [InlineData(RmfRole.Issm, "LeveragedAuthorizations")]
    public async Task Issue968_ProfileCapability_SaveReloadAuditAndRevocation(RmfRole role, string sectionType)
    {
        // Arrange
        var systemId = (await SeedSystemAsync(_tenantA, "Profile-capability")).ToString();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var assignment = new RmfRoleAssignment
        {
            TenantId = _tenantA, RegisteredSystemId = systemId, UserId = "multi-tenant-test-user",
            RmfRole = role, IsActive = true, AssignedBy = "test"
        };
        db.RmfRoleAssignments.Add(assignment);
        await db.SaveChangesAsync();
        var endpoint = $"/api/dashboard/systems/{systemId}/profile/{sectionType}";
        var content = "{\"missionStatement\":\"Synthetic mission\",\"businessPurpose\":\"Synthetic purpose\"}";
        var beforeSave = DateTime.UtcNow;

        // Act
        var initial = await _client.GetFromJsonAsync<JsonElement>(endpoint);
        var save = await _client.PutAsJsonAsync(endpoint, new { content });
        var saved = await save.Content.ReadFromJsonAsync<JsonElement>();
        var reloaded = await _client.GetFromJsonAsync<JsonElement>(endpoint);

        // Assert
        initial.GetProperty("canEditProfile").GetBoolean().Should().BeTrue();
        initial.GetProperty("governanceStatus").GetString().Should().Be("NotStarted");
        save.StatusCode.Should().Be(HttpStatusCode.OK);
        saved.GetProperty("canEditProfile").GetBoolean().Should().BeTrue();
        reloaded.GetProperty("canEditProfile").GetBoolean().Should().BeTrue();
        reloaded.GetProperty("governanceStatus").GetString().Should().Be("Draft");
        reloaded.GetProperty("draftContent").GetString().Should().Be(content);
        reloaded.GetProperty("lastEditedBy").GetString().Should().Be("multi-tenant-test-user");
        var sectionId = saved.GetProperty("id").GetString();
        var audit = await db.ProfileAuditEntries.AsNoTracking().SingleAsync(entry => entry.SystemProfileSectionId == sectionId);
        audit.Action.Should().Be("Drafted");
        audit.PerformedBy.Should().Be("multi-tenant-test-user");
        audit.PerformedAt.Should().BeOnOrAfter(beforeSave);
        audit.PreviousStatus.Should().BeNull();
        audit.NewStatus.Should().Be(SspSectionStatus.Draft);
        var staleSection = await db.SystemProfileSections.SingleAsync(item => item.Id == sectionId);
        var originalToken = staleSection.RowVersion.ToArray();
        await db.SaveChangesAsync();
        staleSection.RowVersion.Should().Equal(originalToken);
        var updated = await _client.PutAsJsonAsync(endpoint, new { content });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = await db.SystemProfileSections.AsNoTracking().SingleAsync(item => item.Id == sectionId);
        stored.RowVersion.Should().NotEqual(originalToken);
        staleSection.DraftContent = "stale content";
        Func<Task> staleSave = () => db.SaveChangesAsync();
        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();
        db.Entry(staleSection).State = EntityState.Detached;
        assignment.IsActive = false;
        await db.SaveChangesAsync();
        var revoked = await _client.PutAsJsonAsync(endpoint, new { content = "unauthorized replacement" });
        revoked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var afterRevocation = await _client.GetFromJsonAsync<JsonElement>(endpoint);
        afterRevocation.GetProperty("canEditProfile").GetBoolean().Should().BeFalse();
        afterRevocation.GetProperty("draftContent").GetString().Should().Be(content);
        (await db.ProfileAuditEntries.CountAsync(entry => entry.SystemProfileSectionId == sectionId)).Should().Be(1);
    }

    [Theory]
    [InlineData("unassigned")]
    [InlineData("inactive")]
    [InlineData("isso")]
    [InlineData("other-user")]
    [InlineData("other-system")]
    [InlineData("other-tenant")]
    public async Task Issue968_ProfileCapability_DeniesNonAuthorDespitePersona(string assignmentCase)
    {
        // Arrange
        var systemId = (await SeedSystemAsync(_tenantA, "Profile-denied")).ToString();
        var otherSystem = (await SeedSystemAsync(_tenantA, "Profile-other")).ToString();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        if (assignmentCase != "unassigned")
        {
            db.RmfRoleAssignments.Add(new RmfRoleAssignment
            {
                TenantId = assignmentCase == "other-tenant" ? _tenantB : _tenantA,
                RegisteredSystemId = assignmentCase == "other-system" ? otherSystem : systemId,
                UserId = assignmentCase == "other-user" ? "other-user" : "multi-tenant-test-user",
                RmfRole = assignmentCase == "isso" ? RmfRole.Isso : RmfRole.MissionOwner,
                IsActive = assignmentCase != "inactive", AssignedBy = "test"
            });
            await db.SaveChangesAsync();
        }
        var endpoint = $"/api/dashboard/systems/{systemId}/profile/MissionAndPurpose";
        using var readRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
        readRequest.Headers.Add("X-Simulated-Role", "MissionOwner");
        using var writeRequest = new HttpRequestMessage(HttpMethod.Put, endpoint)
        {
            Content = JsonContent.Create(new { content = "{}" })
        };
        writeRequest.Headers.Add("X-Simulated-Role", "MissionOwner");

        // Act
        var read = await _client.SendAsync(readRequest);
        var section = await read.Content.ReadFromJsonAsync<JsonElement>();
        var write = await _client.SendAsync(writeRequest);

        // Assert
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        section.GetProperty("canEditProfile").GetBoolean().Should().BeFalse();
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await db.SystemProfileSections.CountAsync(item => item.RegisteredSystemId == systemId)).Should().Be(0);
    }

    [Fact]
    public async Task Issue1001_ProposalHttpFlow_PreservesActiveContentAndRequiresSeparateReviewer()
    {
        // Arrange
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Synthetic proposed policy", [], ["Review record missing"]));
        await using var app = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IControlNarrativeService>();
            services.AddSingleton(generator.Object);
        }));
        using var client = app.CreateClient();
        var systemId = (await SeedSystemAsync(_tenantA, "Proposal-review")).ToString();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var implementation = new ControlImplementation
        {
            TenantId = _tenantA, RegisteredSystemId = systemId, ControlId = "AC-2", PolicyNarrative = "Approved policy",
            TechnicalNarrative = "Approved technical", ApprovalStatus = SspSectionStatus.Approved,
        };
        db.ControlImplementations.Add(implementation);
        db.RmfRoleAssignments.AddRange(
            new RmfRoleAssignment { TenantId = _tenantA, RegisteredSystemId = systemId, UserId = "multi-tenant-test-user", RmfRole = RmfRole.Issm },
            new RmfRoleAssignment { TenantId = _tenantA, RegisteredSystemId = systemId, UserId = "separate-author", RmfRole = RmfRole.SystemOwner });
        await db.SaveChangesAsync();
        var endpoint = $"/api/systems/{systemId}/narrative-library";
        // Act
        var generated = await client.PostAsJsonAsync(endpoint + "/proposals", new { controlId = "AC-2", narrativeType = "Policy", expectedVersion = 1 });
        // Assert
        generated.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await generated.Content.ReadFromJsonAsync<JsonElement>();
        var proposalId = proposal.GetProperty("id").GetGuid();
        (await db.ControlImplementations.AsNoTracking().SingleAsync(item => item.Id == implementation.Id)).PolicyNarrative.Should().Be("Approved policy");
        var review = new { expectedRevision = 1, decision = "Approve", note = "Reviewed synthetic proposal" };
        (await client.PostAsJsonAsync(endpoint + $"/proposals/{proposalId}/review", review)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(endpoint + "/access")).StatusCode.Should().Be(HttpStatusCode.OK);
        var row = await db.NarrativeProposals.SingleAsync(item => item.Id == proposalId);
        row.Status = "NeedsRevision";
        await db.SaveChangesAsync();
        var separate = await scope.ServiceProvider.GetRequiredService<NarrativeProposalService>()
            .GenerateAsync(systemId, "AC-2", "Policy", "separate-author", 1);
        var accepted = await client.PostAsJsonAsync(endpoint + $"/proposals/{separate.Id}/review", review);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var active = await db.ControlImplementations.AsNoTracking().SingleAsync(item => item.Id == implementation.Id);
        active.PolicyNarrative.Should().Be("Synthetic proposed policy");
        active.TechnicalNarrative.Should().Be("Approved technical");
        active.CurrentVersion.Should().Be(2);
        active.ImplementationStatus.Should().Be(ImplementationStatus.Planned);
        (await client.PostAsJsonAsync(endpoint + $"/proposals/{separate.Id}/review", review)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Issue1001_ReferenceImport_RequiresReviewAndPersistsWithoutEvidence()
    {
        // Arrange
        var systemId = (await SeedSystemAsync(_tenantA, "Reference-import")).ToString();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.ControlImplementations.Add(new ControlImplementation
        {
            TenantId = _tenantA, RegisteredSystemId = systemId, ControlId = "AC-2", PolicyNarrative = "Active policy"
        });
        var assignment = new RmfRoleAssignment
        {
            TenantId = _tenantA, RegisteredSystemId = systemId,
            UserId = "multi-tenant-test-user", RmfRole = RmfRole.SystemOwner, IsActive = true, AssignedBy = "test"
        };
        db.RmfRoleAssignments.Add(assignment);
        await db.SaveChangesAsync();
        var endpoint = $"/api/systems/{systemId}/narrative-library";
        var proposalList = await _client.GetAsync(endpoint + "/proposals");
        proposalList.StatusCode.Should().Be(HttpStatusCode.OK);
        (await proposalList.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength().Should().Be(0);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Access policy"), "title");
        form.Add(new StringContent("System"), "scope");
        form.Add(new StringContent(systemId), "scopeId");
        form.Add(new StringContent("AC-2\nPolicy Narrative:\nReview access quarterly."), "file", "policy.txt");

        // Act
        var uploaded = await _client.PostAsync(endpoint + "/imports", form);

        // Assert
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created);
        var draft = await uploaded.Content.ReadFromJsonAsync<JsonElement>();
        var id = draft.GetProperty("id").GetGuid();
        var review = new { expectedRevision = 1, reviewed = true, passages = new[]
        { new { controlId = "AC-2", narrativeType = "Policy", content = "Review access quarterly." } } };
        var published = await _client.PostAsJsonAsync(endpoint + $"/{id}/publish", review);
        published.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await _client.GetFromJsonAsync<JsonElement>(endpoint);
        list.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id)
            .GetProperty("isPublished").GetBoolean().Should().BeTrue();
        (await _client.PostAsJsonAsync(endpoint + $"/{id}/publish", review)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var stored = await db.NarrativeReferences.AsNoTracking().SingleAsync(item => item.Id == id);
        stored.TenantId.Should().Be(_tenantA);
        stored.PublishedBy.Should().Be("multi-tenant-test-user");
        (await db.ControlImplementations.SingleAsync(item => item.RegisteredSystemId == systemId)).PolicyNarrative.Should().Be("Active policy");
        assignment.IsActive = false;
        await db.SaveChangesAsync();
        (await _client.PostAsJsonAsync(endpoint + $"/{id}/publish", review)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        SetTenant(_tenantB, isCspAdmin: false);
        (await _client.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

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

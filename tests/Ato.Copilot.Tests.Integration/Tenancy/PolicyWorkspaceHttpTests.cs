using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class PolicyWorkspaceHttpTests : IClassFixture<WorkspaceMembershipFactory>
{
    private static readonly Guid Directory = Guid.NewGuid();
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;
    private readonly WebApplicationFactory<McpProgram> _factory;

    public PolicyWorkspaceHttpTests(WorkspaceMembershipFactory factory)
    {
        Environment.GetEnvironmentVariable("ATO_TEST_SQLSERVER_CONNSTRING").Should().BeNullOrEmpty();
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(s => s.ServiceType == typeof(IHostedService)
                && s.ImplementationType != typeof(TenancySeedHostedService)).ToArray())
                services.Remove(descriptor);
        }));
    }

    [Fact]
    public async Task Retention_RationaleRevisionAndUnlink_AreExactScopedAndAudited()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        var source = await client.GetFromJsonAsync<JsonElement>(f.Root + "/sources/" + f.Policy);
        const string rationale = "Required for this system's mission.";

        // Act
        var created = await client.PostAsJsonAsync(f.Root + "/references",
            new { policyId = f.Policy, expectedSourceRevision = source.GetProperty("revision").GetString(), rationale });

        // Assert
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var reference = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = reference.GetProperty("id").GetString();
        reference.GetProperty("rationale").GetString().Should().Be(rationale);
        reference.GetProperty("retention").GetString().Should().Be("Retained");
        var revision = reference.GetProperty("revision").GetInt32();
        (await client.PostAsJsonAsync(f.Root + "/references", new
        { policyId = f.Policy, expectedSourceRevision = source.GetProperty("revision").GetString(), rationale }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var policy = await db.SystemComponents.SingleAsync(p => p.Id == f.Policy);
            policy.Name = "Changed source";
            policy.Description = "Changed description";
            policy.ModifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var detail = await client.GetFromJsonAsync<JsonElement>(f.Root + "/references/" + id);
        detail.GetProperty("retainedSource").GetProperty("name").GetString().Should().Be("Original policy");
        detail.GetProperty("currentSource").GetProperty("name").GetString().Should().Be("Changed source");
        detail.GetProperty("reference").GetProperty("sourceChanged").GetBoolean().Should().BeTrue();
        detail.GetProperty("history").GetArrayLength().Should().Be(1);
        detail.GetRawText().Should().NotContain(f.OtherSystem);
        var patched = await client.PatchAsJsonAsync(f.Root + "/references/" + id,
            new { expectedRevision = revision, rationale = "Updated exact rationale." });
        patched.StatusCode.Should().Be(HttpStatusCode.OK, await patched.Content.ReadAsStringAsync());
        (await client.PatchAsJsonAsync(f.Root + "/references/" + id,
            new { expectedRevision = revision, rationale = "Stale edit" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.DeleteAsync(f.Root + $"/references/{id}?expectedRevision={revision}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        detail = await client.GetFromJsonAsync<JsonElement>(f.Root + "/references/" + id);
        detail.GetProperty("reference").GetProperty("rationale").GetString().Should().Be("Updated exact rationale.");
        detail.GetProperty("retainedSource").GetProperty("description").GetString().Should().Be("Original description");
        (await client.DeleteAsync(f.Root + $"/references/{id}?expectedRevision={revision + 1}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var verify = _factory.Services.CreateAsyncScope();
        var saved = verify.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await saved.SystemComponents.AnyAsync(p => p.Id == f.Policy)).Should().BeTrue();
        (await saved.ComponentSystemAssignments.AnyAsync(a => a.Id == f.OtherAssignment)).Should().BeTrue();
        var history = await saved.AuditLogs.Where(a => a.Action.StartsWith("PolicyReference.")).ToListAsync();
        history.Where(a => a.AffectedResources.Contains(id!)).Select(a => a.Action)
            .Should().BeEquivalentTo("PolicyReference.Created", "PolicyReference.Updated", "PolicyReference.Unlinked");
    }

    [Fact]
    public async Task Validation_StaleSourceAndExactSystemScope_AreEnforced()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        var source = await client.GetFromJsonAsync<JsonElement>(f.Root + "/sources/" + f.Policy);
        var revision = source.GetProperty("revision").GetString();

        // Act / Assert
        foreach (var rationale in new[] { "", "  ", new string('x', 501) })
            (await client.PostAsJsonAsync(f.Root + "/references", new { policyId = f.Policy, expectedSourceRevision = revision, rationale }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(f.Root + "/references",
            new { policyId = f.Policy, expectedSourceRevision = "stale", rationale = "Applies" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetAsync(f.Root + "/references/" + f.OtherAssignment)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PatchAsJsonAsync(f.Root + "/references/" + f.OtherAssignment,
            new { expectedRevision = 0, rationale = "Wrong system" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync(f.Root + $"/references/{f.OtherAssignment}?expectedRevision=0"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreach (var policyId in new[] { f.ForeignPolicy, f.OtherPrivatePolicy })
        {
            (await client.GetAsync(f.Root + "/sources/" + policyId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.PostAsJsonAsync(f.Root + "/references",
                new { policyId, expectedSourceRevision = revision, rationale = "Wrong scope" }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        var library = await client.GetFromJsonAsync<JsonElement>(f.Root + "/library");
        library.GetRawText().Should().NotContain(f.ForeignPolicy).And.NotContain(f.OtherPrivatePolicy).And.NotContain(f.OtherSystem);
        source.GetProperty("alreadyLinked").GetBoolean().Should().BeFalse("another system's assignment is private");
    }

    [Fact]
    public async Task PermissionProjection_MatchesDeniedSystemAndOrganizationWrites()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.MissionOwner);
        using var client = Client(f.Actor);

        // Act
        var list = await client.GetFromJsonAsync<JsonElement>(f.Root);
        var assign = await client.PostAsJsonAsync(f.Root + "/references",
            new { policyId = f.Policy, expectedSourceRevision = "anything", rationale = "Applies" });
        var create = await client.PostAsJsonAsync(f.Root + "/library", new { name = "Unauthorized source" });

        // Assert
        list.GetProperty("permissions").GetProperty("canAssign").GetBoolean().Should().BeFalse();
        list.GetProperty("permissions").GetProperty("canCreateLibrary").GetBoolean().Should().BeFalse();
        assign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PatchAsJsonAsync(f.Root + "/references/" + f.OtherAssignment,
            new { expectedRevision = 0, rationale = "Denied" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync(f.Root + $"/references/{f.OtherAssignment}?expectedRevision=0"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LibraryCreation_IsExplicitOrganizationOperation_NotAnAssignment()
    {
        // Arrange
        var f = await SeedAsync(admin: true);
        using var client = Client(f.Actor);
        var name = "Explicit policy " + Guid.NewGuid();

        // Act
        var response = await client.PostAsJsonAsync(f.Root + "/library",
            new { name, subType = "Law", description = "Explicit source description" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var source = await response.Content.ReadFromJsonAsync<JsonElement>();
        source.GetProperty("name").GetString().Should().Be(name);
        source.GetProperty("alreadyLinked").GetBoolean().Should().BeFalse();
        source.GetProperty("versionLabel").GetString().Should().Contain("Created");
        (await client.GetFromJsonAsync<JsonElement>(f.Root)).GetProperty("unfilteredTotal").GetInt32().Should().Be(0);
        foreach (var invalid in new[] { new { name = new string('n', 201), subType = "", description = "" },
            new { name = "Valid", subType = new string('s', 101), description = "" },
            new { name = "Valid", subType = "", description = new string('d', 2001) } })
            (await client.PostAsJsonAsync(f.Root + "/library", invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LegacyRationaleEdit_DoesNotInventRetainedVersion()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        string id;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var assignment = new ComponentSystemAssignment
            { TenantId = Tenant, RegisteredSystemId = f.System, SystemComponentId = f.Policy, CreatedBy = "legacy" };
            db.Add(assignment);
            await db.SaveChangesAsync();
            id = assignment.Id;
        }

        // Act
        var detail = await client.GetFromJsonAsync<JsonElement>(f.Root + "/references/" + id);
        var response = await client.PatchAsJsonAsync(f.Root + "/references/" + id,
            new { expectedRevision = detail.GetProperty("reference").GetProperty("revision").GetInt32(), rationale = "Legacy rationale" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        detail = await client.GetFromJsonAsync<JsonElement>(f.Root + "/references/" + id);
        detail.GetProperty("retainedSource").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("reference").GetProperty("retention").GetString().Should().Be("LegacyUnretained");
        detail.GetProperty("reference").GetProperty("rationale").GetString().Should().Be("Legacy rationale");
    }

    [Fact]
    public async Task MissingExpectedRevision_IsNotTreatedAsLegacyRevisionZero()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        var otherRoot = $"/api/dashboard/systems/{f.OtherSystem}/policy-workspace";

        // Act
        var patch = await client.PatchAsJsonAsync(otherRoot + "/references/" + f.OtherAssignment, new { rationale = "Missing revision" });
        var delete = await client.DeleteAsync(otherRoot + "/references/" + f.OtherAssignment);

        // Assert
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        delete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConcurrentCreatesAndEdits_CommitOnlyOneWinner()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        var source = await client.GetFromJsonAsync<JsonElement>(f.Root + "/sources/" + f.Policy);
        var body = new { policyId = f.Policy, expectedSourceRevision = source.GetProperty("revision").GetString(), rationale = "Concurrent reference" };

        // Act
        var responses = await Task.WhenAll(client.PostAsJsonAsync(f.Root + "/references", body),
            client.PostAsJsonAsync(f.Root + "/references", body));

        // Assert
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        var reference = await responses.Single(r => r.IsSuccessStatusCode).Content.ReadFromJsonAsync<JsonElement>();
        var url = f.Root + "/references/" + reference.GetProperty("id").GetString();
        var edit = new { expectedRevision = reference.GetProperty("revision").GetInt32(), rationale = "Concurrent edit" };
        var edits = await Task.WhenAll(client.PatchAsJsonAsync(url, edit), client.PatchAsJsonAsync(url, edit));
        edits.Select(r => r.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        var detail = await client.GetFromJsonAsync<JsonElement>(url);
        detail.GetProperty("history").GetArrayLength().Should().Be(2);
        detail.GetProperty("history").EnumerateArray().Select(x => x.GetProperty("actor").GetString())
            .Should().OnlyContain(x => x == f.Actor.ToString());
    }

    [Fact]
    public async Task ActualSourceEdit_InvalidatesSelection_AndNeverRewritesNarrativeOrSnapshot()
    {
        // Arrange
        var f = await SeedAsync(admin: true);
        using var client = Client(f.Actor);
        var capability = await SeedCapabilityAsync(f, linkSystem: false);
        var before = await client.GetFromJsonAsync<JsonElement>(f.Root + "/sources/" + f.Policy);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().Push(new TenantContext(Tenant));
            var service = scope.ServiceProvider.GetRequiredService<ComponentService>();
            await service.UpdateOrgComponentAsync(f.Policy, new CreateComponentRequest
            {
                Name = "Edited library policy", Description = "Source edit", ComponentType = "Policy",
                Status = "Active", LinkedCapabilityIds = [capability]
            });
        }

        // Act
        var stale = await client.PostAsJsonAsync(f.Root + "/references", new
        { policyId = f.Policy, expectedSourceRevision = before.GetProperty("revision").GetString(), rationale = "Stale selection" });
        var current = await client.GetFromJsonAsync<JsonElement>(f.Root + "/sources/" + f.Policy);
        var created = await client.PostAsJsonAsync(f.Root + "/references", new
        { policyId = f.Policy, expectedSourceRevision = current.GetProperty("revision").GetString(), rationale = "Reviewed selection" });

        // Assert
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        current.GetProperty("relatedControls").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo("AC-1", "AC-2");
        current.GetRawText().Should().NotContain("AC-FOREIGN");
        await using var verify = _factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemCapabilityLinks.AnyAsync(c => c.RegisteredSystemId == f.System)).Should().BeFalse();
        (await db.ControlImplementations.SingleAsync(c => c.RegisteredSystemId == f.System))
            .Narrative.Should().Be("Reviewed narrative must remain exact.");
        var assignment = await db.ComponentSystemAssignments.SingleAsync(a => a.RegisteredSystemId == f.System);
        assignment.PolicySourceCapturedAt.Should().NotBeNull();
        assignment.PolicySourceModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task IndirectAndSystemOwnedLegacyPolicies_AreVisibleButNotFakeUnlinkTargets()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        await SeedCapabilityAsync(f, linkSystem: true);
        string legacyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var legacy = new SystemComponent { TenantId = Tenant, RegisteredSystemId = f.System,
                Name = "Owned legacy policy", ComponentType = ComponentType.Policy, CreatedBy = "legacy" };
            db.Add(legacy);
            await db.SaveChangesAsync();
            legacyId = legacy.Id;
        }

        // Act
        var list = await client.GetFromJsonAsync<JsonElement>(f.Root);
        var detail = await client.GetFromJsonAsync<JsonElement>(f.Root + "/references/indirect:" + f.Policy);
        var legacyDetail = await client.GetFromJsonAsync<JsonElement>(f.Root + "/references/legacy:" + legacyId);

        // Assert
        list.GetProperty("unfilteredTotal").GetInt32().Should().Be(2);
        detail.GetProperty("reference").GetProperty("retention").GetString().Should().Be("Indirect");
        detail.GetProperty("reference").GetProperty("canRemove").GetBoolean().Should().BeFalse();
        detail.GetProperty("reference").GetProperty("actionReason").GetString().Should().Contain("capability");
        detail.GetProperty("retainedSource").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetRawText().Should().NotContain(f.OtherSystem).And.NotContain("AC-FOREIGN");
        legacyDetail.GetProperty("reference").GetProperty("retention").GetString().Should().Be("LegacyUnretained");
        legacyDetail.GetProperty("reference").GetProperty("canRemove").GetBoolean().Should().BeFalse();
        (await client.DeleteAsync(f.Root + $"/references/indirect:{f.Policy}?expectedRevision=0"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PatchAsJsonAsync(f.Root + "/references/indirect:" + f.Policy,
            new { expectedRevision = 0, rationale = "Not a direct link" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        detail.GetProperty("currentSource").GetProperty("alreadyLinked").GetBoolean().Should().BeFalse();
        legacyDetail.GetProperty("currentSource").GetProperty("alreadyLinked").GetBoolean().Should().BeFalse();
        foreach (var (policyId, pseudoId) in new[] { (f.Policy, "indirect:" + f.Policy), (legacyId, "legacy:" + legacyId) })
        {
            var source = await client.GetFromJsonAsync<JsonElement>(f.Root + "/sources/" + policyId);
            source.GetProperty("alreadyLinked").GetBoolean().Should().BeFalse();
            var library = await client.GetFromJsonAsync<JsonElement>(f.Root + "/library");
            library.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetString() == policyId)
                .GetProperty("alreadyLinked").GetBoolean().Should().BeFalse();
            var assigned = await client.PostAsJsonAsync(f.Root + "/references", new
            { policyId, expectedSourceRevision = source.GetProperty("revision").GetString(), rationale = "Explicitly retained rationale" });
            assigned.StatusCode.Should().Be(HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync());
            var reference = await assigned.Content.ReadFromJsonAsync<JsonElement>();
            reference.GetProperty("retention").GetString().Should().Be("Retained");
            reference.GetProperty("rationale").GetString().Should().Be("Explicitly retained rationale");
            var directId = reference.GetProperty("id").GetString();
            var directDetail = await client.GetFromJsonAsync<JsonElement>(f.Root + "/references/" + directId);
            directDetail.GetProperty("currentSource").GetProperty("alreadyLinked").GetBoolean().Should().BeTrue();
            directDetail.GetProperty("removalImpact").GetRawText().Should().Contain(
                policyId == f.Policy ? "capability links" : "system-owned legacy policy");
            var after = await client.GetFromJsonAsync<JsonElement>(f.Root + "/sources/" + policyId);
            after.GetProperty("revision").GetString().Should().Be(source.GetProperty("revision").GetString());
            after.GetProperty("alreadyLinked").GetBoolean().Should().BeTrue();
            list = await client.GetFromJsonAsync<JsonElement>(f.Root);
            list.GetProperty("unfilteredTotal").GetInt32().Should().Be(2);
            var rows = list.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("policyId").GetString() == policyId).ToList();
            rows.Should().ContainSingle().Which.GetProperty("id").GetString().Should().Be(directId);
            (await client.DeleteAsync(f.Root + $"/references/{directId}?expectedRevision={reference.GetProperty("revision").GetInt32()}"))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
            list = await client.GetFromJsonAsync<JsonElement>(f.Root);
            list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("policyId").GetString() == policyId)
                .GetProperty("id").GetString().Should().Be(pseudoId);
            (await client.GetFromJsonAsync<JsonElement>(f.Root + "/sources/" + policyId))
                .GetProperty("alreadyLinked").GetBoolean().Should().BeFalse();
        }
        await using var verify = _factory.Services.CreateAsyncScope();
        var saved = verify.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await saved.SystemCapabilityLinks.CountAsync(c => c.RegisteredSystemId == f.System)).Should().Be(1);
        (await saved.ComponentCapabilityLinks.CountAsync(c => c.SystemComponentId == f.Policy)).Should().Be(1);
    }

    [Fact]
    public async Task PagingFiltersAndMembershipRevocation_AreAppliedToEveryRead()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        var source = await client.GetFromJsonAsync<JsonElement>(f.Root + "/sources/" + f.Policy);
        var response = await client.PostAsJsonAsync(f.Root + "/references",
            new { policyId = f.Policy, expectedSourceRevision = source.GetProperty("revision").GetString(), rationale = "Policy reason" });
        var reference = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = reference.GetProperty("id").GetString();

        // Act
        var filtered = await client.GetFromJsonAsync<JsonElement>(f.Root + "?search=does-not-match&pageSize=1");
        var unchanged = await client.GetFromJsonAsync<JsonElement>(f.Root + "?sourceChanged=false&status=Active");

        // Assert
        filtered.GetProperty("totalCount").GetInt32().Should().Be(0);
        filtered.GetProperty("unfilteredTotal").GetInt32().Should().Be(1);
        unchanged.GetProperty("totalCount").GetInt32().Should().Be(1);
        foreach (var suffix in new[] { "?page=0", "?pageSize=201", "?sourceChanged=maybe" })
            (await client.GetAsync(f.Root + suffix)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.SystemRoleAssignments.SingleAsync(a => a.RegisteredSystemId == f.System)).RemovedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        foreach (var suffix in new[] { "", "/library", "/sources/" + f.Policy, "/references/" + id })
            (await client.GetAsync(f.Root + suffix)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<string> SeedCapabilityAsync(Fixture f, bool linkSystem)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var capability = new SecurityCapability { TenantId = Tenant, Name = "Mapped capability " + Guid.NewGuid(), Description = "Fixture" };
        db.Add(capability);
        db.ComponentCapabilityLinks.Add(new() { TenantId = Tenant, SystemComponentId = f.Policy, SecurityCapabilityId = capability.Id });
        db.CapabilityControlMappings.AddRange(
            new() { TenantId = Tenant, SecurityCapabilityId = capability.Id, ControlId = "AC-1" },
            new() { TenantId = Tenant, SecurityCapabilityId = capability.Id, ControlId = "AC-2", RegisteredSystemId = f.System },
            new() { TenantId = Tenant, SecurityCapabilityId = capability.Id, ControlId = "AC-FOREIGN", RegisteredSystemId = f.OtherSystem });
        db.ControlImplementations.Add(new() { TenantId = Tenant, RegisteredSystemId = f.System,
            ControlId = "AC-1", SecurityCapabilityId = capability.Id, Narrative = "Reviewed narrative must remain exact." });
        if (linkSystem) db.SystemCapabilityLinks.Add(new() { TenantId = Tenant, RegisteredSystemId = f.System,
            SecurityCapabilityId = capability.Id, LinkedBy = "fixture" });
        await db.SaveChangesAsync();
        return capability.Id;
    }

    [Fact]
    public async Task Mutations_UseExecutionStrategyCompatibleTransactions()
    {
        // Arrange
        var f = await SeedAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(db.Database.GetConnectionString())
            .ReplaceService<IExecutionStrategyFactory, PolicyRetryStrategyFactory>().Options;
        var service = new ComponentService(new RetryContextFactory(options), NullLogger<ComponentService>.Instance,
            scope.ServiceProvider.GetRequiredService<NarrativeTemplateService>(),
            scope.ServiceProvider.GetRequiredService<SystemCapabilityLinkService>());
        var source = await service.GetPolicySourceAsync(Tenant, f.System, f.Policy, default);

        // Act
        var reference = await service.CreatePolicyReferenceAsync(Tenant, f.System,
            new(f.Policy, source.Revision, "Retry-safe applicability"), "actor", default);
        var edited = await service.UpdatePolicyReferenceAsync(Tenant, f.System, reference.Id,
            new(reference.Revision, "Retry-safe edit"), "actor", default);
        await service.RemovePolicyReferenceAsync(Tenant, f.System, reference.Id, edited.Revision, "actor", default);

        // Assert
        (await db.ComponentSystemAssignments.AnyAsync(a => a.Id == reference.Id)).Should().BeFalse();
        (await db.AuditLogs.CountAsync(a => a.Action.StartsWith("PolicyReference.") && a.Details.Contains(reference.Id)))
            .Should().Be(3);
    }

    private sealed class RetryContextFactory(DbContextOptions<AtoCopilotContext> options) : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
    }

    public sealed class PolicyRetryStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new PolicyRetryStrategy(dependencies);
    }

    private sealed class PolicyRetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    private HttpClient Client(Guid actor)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", Directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        return client;
    }

    private async Task<Fixture> SeedAsync(OrganizationRole role = OrganizationRole.Issm, bool admin = false)
    {
        using var initialized = _factory.CreateClient();
        var actor = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var person = new Person { TenantId = Tenant, DisplayName = "Policy actor", Email = $"{actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = Tenant, Name = "Policy system " + actor, IsActive = true };
        var other = new RegisteredSystem { TenantId = Tenant, Name = "Other private system " + actor, IsActive = true };
        var policy = new SystemComponent { TenantId = Tenant, Name = "Original policy",
            Description = "Original description", ComponentType = ComponentType.Policy, CreatedBy = "fixture" };
        var foreign = new SystemComponent { TenantId = WorkspaceMembershipFactory.TenantBId, Name = "Foreign policy",
            ComponentType = ComponentType.Policy, CreatedBy = "fixture" };
        var privatePolicy = new SystemComponent { TenantId = Tenant, RegisteredSystemId = other.Id, Name = "Private policy",
            ComponentType = ComponentType.Policy, CreatedBy = "fixture" };
        var assignment = new ComponentSystemAssignment { TenantId = Tenant, RegisteredSystemId = other.Id,
            SystemComponentId = policy.Id, CreatedBy = "other system actor" };
        db.AddRange(person, system, other, policy, foreign, privatePolicy, assignment);
        db.OrganizationMemberships.Add(new() { TenantId = Tenant, DirectoryTenantId = Directory,
            ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture" });
        db.SystemRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id, RegisteredSystemId = system.Id, Role = role });
        db.SystemRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id, RegisteredSystemId = other.Id, Role = role });
        if (admin) db.OrganizationRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id, Role = OrganizationRole.Administrator });
        await db.SaveChangesAsync();
        return new(actor, system.Id, other.Id, policy.Id, foreign.Id, privatePolicy.Id, assignment.Id);
    }

    private sealed record Fixture(Guid Actor, string System, string OtherSystem, string Policy,
        string ForeignPolicy, string OtherPrivatePolicy, string OtherAssignment)
    {
        public string Root => $"/api/dashboard/systems/{System}/policy-workspace";
    }
}

using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for Entra ID discovery and import endpoints
/// (Feature 040 — User Story 9).
/// Tests the end-to-end flow: discover → import → re-discover (dedup),
/// setting-disabled rejection, and mixed user/group scenarios.
/// </summary>
public class EntraIdDiscoveryEndpointTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly ComponentService _componentService;

    public EntraIdDiscoveryEndpointTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"EntraIdDiscoveryIntegration_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(options);
        _componentService = new ComponentService(
            _db, NullLogger<ComponentService>.Instance, new NarrativeTemplateService());
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    // ─── Setting-Disabled Rejection (Feature Gate) ───────────────────────────

    [Fact]
    public async Task DiscoverEntra_DisabledSetting_ReturnsPartialFailure()
    {
        // When Graph client is null (feature disabled), discovery returns partial failure
        var service = new EntraIdDiscoveryService(
            NullLogger<EntraIdDiscoveryService>.Instance,
            graphClient: null);

        var result = await service.DiscoverUsersAndGroupsAsync(_db);

        result.PartialFailure.Should().BeTrue();
        result.FailureMessage.Should().Contain("not configured");
        result.Items.Should().BeEmpty();
    }

    // ─── Import End-to-End Flow ──────────────────────────────────────────────

    [Fact]
    public async Task ImportEntra_EndToEnd_ImportThenRediscover()
    {
        // Step 1: Import users and groups
        var people = new List<EntraImportPerson>
        {
            new() { EntraObjectId = "oid-user-1", DisplayName = "Alice Johnson", Email = "alice@contoso.com", Kind = "User" },
            new() { EntraObjectId = "oid-user-2", DisplayName = "Bob Smith", Email = "bob@contoso.com", Kind = "User" },
            new() { EntraObjectId = "oid-group-1", DisplayName = "Security Admins", Email = "secadmins@contoso.com", Kind = "Group" },
        };

        var importResult = await _componentService.ImportEntraIdPeopleAsync(people, "test-user");

        importResult.Imported.Should().Be(3);
        importResult.Skipped.Should().Be(0);
        importResult.Components.Should().HaveCount(3);

        // Step 2: Verify in database
        var dbComponents = await _db.SystemComponents
            .Where(c => c.ComponentType == ComponentType.Person)
            .ToListAsync();
        dbComponents.Should().HaveCount(3);

        var users = dbComponents.Where(c => c.SubType == "EntraId/User").ToList();
        users.Should().HaveCount(2);
        users.Should().AllSatisfy(c =>
        {
            c.RegisteredSystemId.Should().BeNull();
            c.AzureResourceId.Should().StartWith("oid-user-");
        });

        var groups = dbComponents.Where(c => c.SubType == "EntraId/Group").ToList();
        groups.Should().HaveCount(1);
        groups[0].AzureResourceId.Should().Be("oid-group-1");

        // Step 3: Re-import same people — all should be skipped
        var reimportResult = await _componentService.ImportEntraIdPeopleAsync(people, "test-user");
        reimportResult.Imported.Should().Be(0);
        reimportResult.Skipped.Should().Be(3);

        // Database should still have exactly 3 components
        var finalCount = await _db.SystemComponents.CountAsync();
        finalCount.Should().Be(3);
    }

    // ─── Mixed Partial Import ────────────────────────────────────────────────

    [Fact]
    public async Task ImportEntra_PartialDuplicate_ImportsOnlyNew()
    {
        // Pre-seed one Entra person
        _db.SystemComponents.Add(new SystemComponent
        {
            Name = "Pre-existing User",
            ComponentType = ComponentType.Person,
            SubType = "EntraId/User",
            AzureResourceId = "oid-existing",
            RegisteredSystemId = null,
            CreatedBy = "seed",
        });
        await _db.SaveChangesAsync();

        var people = new List<EntraImportPerson>
        {
            new() { EntraObjectId = "oid-existing", DisplayName = "Pre-existing User", Kind = "User" },
            new() { EntraObjectId = "oid-new-1", DisplayName = "New User", Kind = "User" },
            new() { EntraObjectId = "oid-new-group", DisplayName = "New Group", Kind = "Group" },
        };

        var result = await _componentService.ImportEntraIdPeopleAsync(people, "test-user");

        result.Imported.Should().Be(2);
        result.Skipped.Should().Be(1);
        result.SkippedDetails.Should().ContainSingle()
            .Which.ResourceId.Should().Be("oid-existing");

        var total = await _db.SystemComponents.CountAsync();
        total.Should().Be(3); // 1 pre-existing + 2 new
    }

    // ─── Entra ID Discovery Dedup (AlreadyImported flag) ─────────────────────

    [Fact]
    public async Task DiscoverEntra_NullClient_MarksNothingAsImported()
    {
        // Pre-seed a Person component in the DB
        _db.SystemComponents.Add(new SystemComponent
        {
            Name = "Existing Person",
            ComponentType = ComponentType.Person,
            AzureResourceId = "oid-person",
            RegisteredSystemId = null,
            CreatedBy = "seed",
        });
        await _db.SaveChangesAsync();

        // Discovery with null client short-circuits — does NOT reach dedup logic
        var service = new EntraIdDiscoveryService(
            NullLogger<EntraIdDiscoveryService>.Instance,
            graphClient: null);

        var result = await service.DiscoverUsersAndGroupsAsync(_db);

        result.Items.Should().BeEmpty();
        result.PartialFailure.Should().BeTrue();
    }

    // ─── Component Metadata Verification ─────────────────────────────────────

    [Fact]
    public async Task ImportEntra_SetsCorrectComponentFields()
    {
        var people = new List<EntraImportPerson>
        {
            new() { EntraObjectId = "oid-meta-user", DisplayName = "Test User", Email = "test@contoso.com", Kind = "User" },
            new() { EntraObjectId = "oid-meta-group", DisplayName = "Test Group", Email = "group@contoso.com", Kind = "Group" },
        };

        await _componentService.ImportEntraIdPeopleAsync(people, "import-user");

        var user = await _db.SystemComponents.SingleAsync(c => c.AzureResourceId == "oid-meta-user");
        user.Name.Should().Be("Test User");
        user.PersonName.Should().Be("Test User");
        user.Email.Should().Be("test@contoso.com");
        user.ComponentType.Should().Be(ComponentType.Person);
        user.SubType.Should().Be("EntraId/User");
        user.AzureResourceType.Should().Be("EntraId/User");
        user.Status.Should().Be(ComponentStatus.Active);
        user.CreatedBy.Should().Be("import-user");

        var group = await _db.SystemComponents.SingleAsync(c => c.AzureResourceId == "oid-meta-group");
        group.Name.Should().Be("Test Group");
        group.PersonName.Should().Be("Test Group");
        group.Email.Should().Be("group@contoso.com");
        group.ComponentType.Should().Be(ComponentType.Person);
        group.SubType.Should().Be("EntraId/Group");
        group.AzureResourceType.Should().Be("EntraId/Group");
    }

    // ─── Batch Dedup Within Single Import ────────────────────────────────────

    [Fact]
    public async Task ImportEntra_DuplicatesInSameBatch_DedupsCorrectly()
    {
        var people = new List<EntraImportPerson>
        {
            new() { EntraObjectId = "oid-dup", DisplayName = "First Copy", Kind = "User" },
            new() { EntraObjectId = "oid-dup", DisplayName = "Second Copy", Kind = "User" },
            new() { EntraObjectId = "oid-unique", DisplayName = "Unique User", Kind = "User" },
        };

        var result = await _componentService.ImportEntraIdPeopleAsync(people, "test-user");

        result.Imported.Should().Be(2);
        result.Skipped.Should().Be(1);

        var dbCount = await _db.SystemComponents.CountAsync();
        dbCount.Should().Be(2);
    }

    // ─── Large Batch Import ──────────────────────────────────────────────────

    [Fact]
    public async Task ImportEntra_LargeBatch_AllImported()
    {
        var people = Enumerable.Range(1, 50).Select(i => new EntraImportPerson
        {
            EntraObjectId = $"oid-bulk-{i}",
            DisplayName = $"User {i}",
            Email = $"user{i}@contoso.com",
            Kind = i % 5 == 0 ? "Group" : "User",
        }).ToList();

        var result = await _componentService.ImportEntraIdPeopleAsync(people, "bulk-import");

        result.Imported.Should().Be(50);
        result.Skipped.Should().Be(0);

        var dbCount = await _db.SystemComponents.CountAsync();
        dbCount.Should().Be(50);

        // Verify mix of types
        var groups = await _db.SystemComponents.Where(c => c.SubType == "EntraId/Group").CountAsync();
        groups.Should().Be(10); // every 5th is a group
    }
}

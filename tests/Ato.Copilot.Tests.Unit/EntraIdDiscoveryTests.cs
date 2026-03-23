using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit;

/// <summary>
/// Unit tests for Entra ID discovery service and import logic
/// (Feature 040 — User Story 9).
/// </summary>
public class EntraIdDiscoveryTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly ComponentService _componentService;

    public EntraIdDiscoveryTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AtoCopilotContext(options);
        _componentService = new ComponentService(_db, NullLogger<ComponentService>.Instance, new NarrativeTemplateService(), new SystemCapabilityLinkService(_db, NullLogger<SystemCapabilityLinkService>.Instance));
    }

    public void Dispose() => _db.Dispose();

    // ─── Discovery — Setting Gate (null GraphServiceClient) ──────────────────

    [Fact]
    public async Task DiscoverUsersAndGroups_NullGraphClient_ReturnsPartialFailure()
    {
        var service = new EntraIdDiscoveryService(
            NullLogger<EntraIdDiscoveryService>.Instance,
            graphClient: null);

        var result = await service.DiscoverUsersAndGroupsAsync(_db);

        result.Items.Should().BeEmpty();
        result.PartialFailure.Should().BeTrue();
        result.FailureMessage.Should().Contain("not configured");
    }

    [Fact]
    public async Task DiscoverUsersAndGroups_NullGraphClient_WithSearchFilter_StillReturnsPartialFailure()
    {
        var service = new EntraIdDiscoveryService(
            NullLogger<EntraIdDiscoveryService>.Instance,
            graphClient: null);

        var result = await service.DiscoverUsersAndGroupsAsync(_db, searchFilter: "john");

        result.Items.Should().BeEmpty();
        result.PartialFailure.Should().BeTrue();
    }

    // ─── Import — Entra ID People (Person components) ────────────────────────

    [Fact]
    public async Task ImportEntraIdPeople_CreatesPersonComponents()
    {
        var people = new List<EntraImportPerson>
        {
            new() { EntraObjectId = "oid-001", DisplayName = "Jane Smith", Email = "jane@example.com", Kind = "User" },
            new() { EntraObjectId = "oid-002", DisplayName = "Security Team", Email = "sec@example.com", Kind = "Group" },
        };

        var result = await _componentService.ImportEntraIdPeopleAsync(people, "test-user");

        result.Imported.Should().Be(2);
        result.Skipped.Should().Be(0);

        var dbComponents = await _db.SystemComponents.ToListAsync();
        dbComponents.Should().HaveCount(2);

        var user = dbComponents.Single(c => c.AzureResourceId == "oid-001");
        user.ComponentType.Should().Be(ComponentType.Person);
        user.SubType.Should().Be("EntraId/User");
        user.PersonName.Should().Be("Jane Smith");
        user.Email.Should().Be("jane@example.com");
        user.RegisteredSystemId.Should().BeNull();

        var group = dbComponents.Single(c => c.AzureResourceId == "oid-002");
        group.ComponentType.Should().Be(ComponentType.Person);
        group.SubType.Should().Be("EntraId/Group");
        group.PersonName.Should().Be("Security Team");
    }

    [Fact]
    public async Task ImportEntraIdPeople_SkipsDuplicates()
    {
        // Pre-seed an existing Entra person
        _db.SystemComponents.Add(new SystemComponent
        {
            Name = "Existing User",
            ComponentType = ComponentType.Person,
            AzureResourceId = "oid-existing",
            RegisteredSystemId = null,
            CreatedBy = "seed",
        });
        await _db.SaveChangesAsync();

        var people = new List<EntraImportPerson>
        {
            new() { EntraObjectId = "oid-existing", DisplayName = "Existing User", Kind = "User" },
            new() { EntraObjectId = "oid-new", DisplayName = "New User", Kind = "User" },
        };

        var result = await _componentService.ImportEntraIdPeopleAsync(people, "test-user");

        result.Imported.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.SkippedDetails.Should().ContainSingle()
            .Which.ResourceId.Should().Contain("oid-existing");
    }

    [Fact]
    public async Task ImportEntraIdPeople_DedupWithinBatch()
    {
        var people = new List<EntraImportPerson>
        {
            new() { EntraObjectId = "oid-dup", DisplayName = "User A", Kind = "User" },
            new() { EntraObjectId = "oid-dup", DisplayName = "User A Copy", Kind = "User" },
        };

        var result = await _componentService.ImportEntraIdPeopleAsync(people, "test-user");

        result.Imported.Should().Be(1);
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task ImportEntraIdPeople_EmptyList_ReturnsZeroCounts()
    {
        var result = await _componentService.ImportEntraIdPeopleAsync(new List<EntraImportPerson>(), "test-user");

        result.Imported.Should().Be(0);
        result.Skipped.Should().Be(0);
    }

    // ─── Discovery — AlreadyImported Dedup ────────────────────────────────────

    [Fact]
    public async Task DiscoverUsersAndGroups_NullClient_DoesNotCheckDedup()
    {
        // With no Graph client, the service short-circuits before hitting the DB
        _db.SystemComponents.Add(new SystemComponent
        {
            Name = "Existing Person",
            ComponentType = ComponentType.Person,
            AzureResourceId = "oid-pre",
            RegisteredSystemId = null,
            CreatedBy = "seed",
        });
        await _db.SaveChangesAsync();

        var service = new EntraIdDiscoveryService(
            NullLogger<EntraIdDiscoveryService>.Instance,
            graphClient: null);

        var result = await service.DiscoverUsersAndGroupsAsync(_db);

        // Should return empty because the client was null (short-circuit)
        result.Items.Should().BeEmpty();
        result.PartialFailure.Should().BeTrue();
    }

    // ─── Import — SubType mapping ─────────────────────────────────────────────

    [Fact]
    public async Task ImportEntraIdPeople_UserKind_SetsCorrectSubType()
    {
        var people = new List<EntraImportPerson>
        {
            new() { EntraObjectId = "oid-u", DisplayName = "User One", Kind = "User" },
        };

        await _componentService.ImportEntraIdPeopleAsync(people, "test-user");

        var comp = await _db.SystemComponents.SingleAsync();
        comp.SubType.Should().Be("EntraId/User");
        comp.AzureResourceType.Should().Be("EntraId/User");
    }

    [Fact]
    public async Task ImportEntraIdPeople_GroupKind_SetsCorrectSubType()
    {
        var people = new List<EntraImportPerson>
        {
            new() { EntraObjectId = "oid-g", DisplayName = "Group One", Kind = "Group" },
        };

        await _componentService.ImportEntraIdPeopleAsync(people, "test-user");

        var comp = await _db.SystemComponents.SingleAsync();
        comp.SubType.Should().Be("EntraId/Group");
        comp.AzureResourceType.Should().Be("EntraId/Group");
    }
}

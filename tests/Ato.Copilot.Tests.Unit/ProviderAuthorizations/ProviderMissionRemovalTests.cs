using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Environments;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed partial class ProviderMissionServiceTests
{
    private SystemEnvironmentService EnvironmentService(TenantContext tenant) =>
        new(new Factory(_options, _accessor), tenant,
            new SystemWorkspaceAccessService(new Factory(_options, _accessor), _accessor), Mock.Of<ISystemEnvironmentAzureSource>());

    [Fact]
    public async Task Removal_WithActiveCapabilityAdoption_IsBlockedUntilExplicitUnsubscribe()
    {
        // Arrange
        var (capabilityId, release) = await SeedPublishedAsync();
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var mission = Service(db, tenant);
        await mission.AssociateAsync(_system, new(_assignment.Id, 1), "manager", default);
        var adoption = await mission.AdoptAsync(_system, await AdoptionRequestAsync(mission, capabilityId, release.Id),
            "manager", default, "adopt");
        var environment = EnvironmentService(tenant);
        // Act
        var preview = await environment.PreviewProviderScopeRemovalAsync(_system, _assignment.Id,
            new(0, 1, 0, "End this provider relationship"), "manager");
        // Assert
        preview.CanCommit.Should().BeFalse();
        preview.Blockers.Should().ContainSingle();
        preview.Blockers[0].Should().Contain("Published capability").And.Contain(adoption.Subscription.Id);
        await FluentActions.Awaiting(() => environment.RemoveProviderScopeAsync(_system, _assignment.Id,
            new(0, preview.PreviewId, "End this provider relationship", true), "blocked-remove", "manager"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await db.CapabilitySubscriptions.AsNoTracking().SingleAsync()).IsActive.Should().BeTrue();
        (await db.Set<CapabilityAdoptionSnapshot>().CountAsync()).Should().Be(1);
        // Act
        var access = new SystemWorkspaceAccessService(new Factory(_options, _accessor), _accessor);
        var responsibility = new CapabilityResponsibilityService(db, tenant, access, NullLogger<CapabilityResponsibilityService>.Instance);
        await responsibility.UnsubscribeAsync(_system, capabilityId, "manager");
        var clear = await environment.PreviewProviderScopeRemovalAsync(_system, _assignment.Id,
            new(0, 1, 0, "End this provider relationship"), "manager");
        var removed = await environment.RemoveProviderScopeAsync(_system, _assignment.Id,
            new(0, clear.PreviewId, "End this provider relationship", true), "remove", "manager");
        // Assert
        clear.CanCommit.Should().BeTrue();
        clear.Blockers.Should().BeEmpty();
        removed.ProviderScopes.Single().State.Should().Be("Removed");
        (await mission.RelationshipsAsync(_system, 1, 25, default)).Items.Should().BeEmpty();
        (await db.Set<ProviderHostingAssignment>().IgnoreQueryFilters().CountAsync(x =>
            x.Id == _assignment.Id && x.ProviderId == _provider && x.TargetTenantId == _tenant && x.SystemId == _system))
            .Should().Be(1);
        (await db.Set<MissionProviderRelationshipReview>().CountAsync()).Should().Be(1);
        (await db.Set<CapabilityAdoptionSnapshot>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Removal_AdoptionCreatedAfterClearPreview_IsRecheckedAtCommit()
    {
        // Arrange
        var (capabilityId, release) = await SeedPublishedAsync();
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var mission = Service(db, tenant);
        await mission.AssociateAsync(_system, new(_assignment.Id, 1), "manager", default);
        var environment = EnvironmentService(tenant);
        var preview = await environment.PreviewProviderScopeRemovalAsync(_system, _assignment.Id,
            new(0, 1, 0, "End relationship"), "manager");
        preview.CanCommit.Should().BeTrue();
        await mission.AdoptAsync(_system, await AdoptionRequestAsync(mission, capabilityId, release.Id),
            "manager", default, "adopt-after-preview");
        // Act / Assert
        await FluentActions.Awaiting(() => environment.RemoveProviderScopeAsync(_system, _assignment.Id,
            new(0, preview.PreviewId, "End relationship", true), "stale-clear-preview", "manager"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await environment.ListAsync(_system)).ProviderScopes.Single().State.Should().Be("Active");
    }

    [Fact]
    public async Task HistoricalRemovedSelection_IsNotCurrentMissionOrDocumentSource_ButRetainsAdoptionHistory()
    {
        // Arrange
        var (capabilityId, release) = await SeedPublishedAsync();
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var mission = Service(db, tenant);
        await mission.AssociateAsync(_system, new(_assignment.Id, 1), "manager", default);
        var adopted = await mission.AdoptAsync(_system, await AdoptionRequestAsync(mission, capabilityId, release.Id),
            "manager", default, "adopt");
        var priorExport = await ExportAsync();
        priorExport.OscalJson.Should().Contain(adopted.AdoptionSnapshotId.ToString());
        db.Add(new SystemProviderScopeSelection { TenantId = _tenant, SystemId = _system, AssignmentId = _assignment.Id,
            State = "Removed", UpdatedBy = "historical-removal", HistoryJson = "[{\"action\":\"Removed\"}]" });
        await db.SaveChangesAsync();
        // Act
        var currentList = await mission.RelationshipsAsync(_system, 1, 25, default);
        var currentExport = await ExportAsync();
        // Assert
        currentList.Items.Should().BeEmpty();
        currentExport.ProviderProvenanceGaps.Should().Contain(x => x.Contains("removed", StringComparison.OrdinalIgnoreCase));
        using var document = System.Text.Json.JsonDocument.Parse(currentExport.OscalJson);
        var implementation = document.RootElement.GetProperty("system-security-plan").GetProperty("system-implementation");
        (implementation.TryGetProperty("leveraged-authorizations", out var currentSources) && currentSources.GetArrayLength() > 0)
            .Should().BeFalse();
        (await db.Set<CapabilityAdoptionSnapshot>().SingleAsync()).Id.Should().Be(adopted.AdoptionSnapshotId);
        (await db.Set<MissionProviderRelationshipReview>().CountAsync()).Should().Be(1);
        priorExport.OscalJson.Should().Contain(adopted.AdoptionSnapshotId.ToString());
    }
}

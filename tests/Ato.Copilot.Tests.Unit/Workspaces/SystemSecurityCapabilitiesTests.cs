using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Workspaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Workspaces;

public sealed class SystemSecurityCapabilitiesTests
{
    private static readonly SystemSecurityCapabilityAccess Manage = new(true, true, false, true, true, false);
    private static WorkspaceOperationsService Service(IDbContextFactory<AtoCopilotContext> factory)
    {
        var changes = new Mock<INarrativeChangeImpactService>(MockBehavior.Strict);
        changes.Setup(x => x.QueueAsync(It.IsAny<NarrativeChangeImpactRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NarrativeChangeImpactResult([]));
        return new(factory, narrativeChanges: changes.Object);
    }

    [Fact]
    public async Task SetupPlan_DisplayLabelsArePersistedImmutableAndLegacyCompatible()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync(sqlite: true);
        await using var lifetime = factory;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SystemComponents.Add(new() { Id = "named-server", TenantId = tenant,
                Name = "Audit collector", ComponentType = ComponentType.Thing });
            db.ComponentCapabilityLinks.Add(new() { TenantId = tenant, SystemComponentId = "named-server", SecurityCapabilityId = "local" });
            await db.SaveChangesAsync();
        }
        var sut = Service(factory);
        var item = (await sut.ListSystemSecurityCapabilitiesAsync(tenant, "one", new(), Manage, default)).Items.Single();
        var request = new PrepareSystemCapabilitySetupRequest("named-plan",
            [new("local", item.RecordId, item.SourceRevision, [new("local", "named-server", "boundary")], [])]);

        // Act
        var prepared = await sut.PrepareSystemCapabilitySetupAsync(tenant, "one", request, Manage, default);
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.SecurityCapabilities.SingleAsync(x => x.Id == "local")).Name = "Renamed capability";
            (await db.SystemComponents.SingleAsync(x => x.Id == "named-server")).Name = "Renamed component";
            (await db.AuthorizationBoundaryDefinitions.SingleAsync(x => x.Id == "boundary")).Name = "Renamed boundary";
            await db.SaveChangesAsync();
        }
        var recovered = await Service(factory).GetSystemCapabilityOperationAsync(tenant, "one", prepared.Operation.OperationId, Manage, default);
        var replay = await sut.PrepareSystemCapabilitySetupAsync(tenant, "one", request, Manage, default);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.CapabilitySetupOperations.SingleAsync(x => x.Id == prepared.Operation.OperationId);
            var legacy = System.Text.Json.Nodes.JsonNode.Parse(row.SystemPlanJson!)!;
            foreach (var write in legacy["Writes"]!.AsArray()) write!.AsObject().Remove("DisplayLabel");
            row.SystemPlanJson = legacy.ToJsonString();
            await db.SaveChangesAsync();
        }
        var legacyPlan = await sut.GetSystemCapabilityOperationAsync(tenant, "one", prepared.Operation.OperationId, Manage, default);

        // Assert
        prepared.Operation.PlannedWrites.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.DisplayLabel));
        prepared.Operation.PlannedWrites.Single(x => x.WriteKind == "component-placement").DisplayLabel.Should()
            .Be("Place \"Audit collector\" (Organization) on boundary \"Operations\"");
        prepared.Operation.PlannedWrites.Single(x => x.WriteKind == "system-link").DisplayLabel.Should()
            .Contain("Access").And.Contain("already present; no write");
        prepared.Operation.PlannedWrites.Single(x => x.WriteKind == "control-implementation").DisplayLabel.Should().Contain("AC-1");
        recovered!.PlannedWrites.Should().BeEquivalentTo(prepared.Operation.PlannedWrites, x => x.WithStrictOrdering());
        replay.Existing.Should().BeTrue();
        replay.Operation.PlannedWrites.Should().BeEquivalentTo(prepared.Operation.PlannedWrites, x => x.WithStrictOrdering());
        legacyPlan!.PlannedWrites.Should().OnlyContain(x => x.DisplayLabel == null);
        legacyPlan.PlannedWrites.Select(x => x.WriteId).Should().Equal(prepared.Operation.PlannedWrites.Select(x => x.WriteId));
    }

    [Fact]
    public async Task ComponentPlacement_AssignsDirectOrphan_RejectsStaleUnassign_AndRetainsSource()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync(sqlite: true);
        await using var lifetime = factory;
        await using (var seed = await factory.CreateDbContextAsync())
        {
            (await seed.SystemComponents.SingleAsync(x => x.Id == "orphan")).ComponentType = ComponentType.Thing;
            await seed.SaveChangesAsync();
        }
        var sut = Service(factory);
        var options = await sut.GetSystemComponentPlacementsAsync(tenant, "one", "local", "orphan", Manage, default);

        // Act
        var assigned = await sut.AssignSystemComponentPlacementAsync(tenant, "one", "local", "orphan",
            new("boundary", options.SourceRevision, options.RelationshipRevision), Manage, "actor", default);
        var stale = () => sut.AssignSystemComponentPlacementAsync(tenant, "one", "local", "orphan",
            new("boundary", options.SourceRevision, options.RelationshipRevision), Manage, "actor", default);
        var staleError = await stale.Should().ThrowAsync<SystemCapabilityConflictException>();
        var current = await sut.GetSystemComponentPlacementsAsync(tenant, "one", "local", "orphan", Manage, default);
        var placement = current.Placements.Single(x => x.Id == assigned.PlacementId);
        var badRemoval = () => sut.UnassignSystemComponentPlacementAsync(tenant, "one", "local", "orphan", placement.Id,
            new(current.SourceRevision, current.RelationshipRevision, "stale"), Manage, "actor", default);
        var removalError = await badRemoval.Should().ThrowAsync<SystemCapabilityConflictException>();
        var removed = await sut.UnassignSystemComponentPlacementAsync(tenant, "one", "local", "orphan", placement.Id,
            new(current.SourceRevision, current.RelationshipRevision, placement.Revision), Manage, "actor", default);

        // Assert
        options.CanAssignBoundary.Should().BeTrue();
        options.Placements.Should().Contain(x => !x.CanUnassign && x.UnassignBlockedReason != null);
        assigned.Action.Should().Be("Assigned");
        placement.CanUnassign.Should().BeTrue();
        staleError.Which.Code.Should().Be("STALE_RELATIONSHIP");
        removalError.Which.Code.Should().Be("STALE_PLACEMENT");
        removed.Action.Should().Be("Unassigned");
        await using var db = await factory.CreateDbContextAsync();
        (await db.SystemComponents.AnyAsync(x => x.Id == "orphan")).Should().BeTrue();
        (await db.BoundaryComponentAssignments.AnyAsync(x => x.Id == placement.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task ComponentPlacement_ProviderSourceReadonlyButBoundaryAssignable_PermissionsAndForeignSystemEnforced()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var providerId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var profileId = Guid.NewGuid();
            db.CspProfiles.Add(new() { Id = profileId, DisplayName = "Provider" });
            db.CspInheritedComponents.Add(new() { Id = providerId, CspProfileId = profileId, Name = "Source-owned", Status = CspInheritedComponentStatus.Published });
            db.AuthorizationBoundaryDefinitions.Add(new() { Id = "other-boundary", TenantId = tenant, RegisteredSystemId = "two", Name = "Other system" });
            await db.SaveChangesAsync();
        }
        var sut = Service(factory);
        var options = await sut.GetSystemComponentPlacementsAsync(tenant, "one", "provider", providerId.ToString(), Manage, default);
        var person = await sut.GetSystemComponentPlacementsAsync(tenant, "one", "local", "person", Manage, default);
        var request = new AssignSystemComponentPlacementRequest("boundary", options.SourceRevision, options.RelationshipRevision);

        // Act
        var denied = () => sut.AssignSystemComponentPlacementAsync(tenant, "one", "provider", providerId.ToString(),
            request, Manage with { CanManage = false }, "actor", default);
        var foreign = () => sut.AssignSystemComponentPlacementAsync(tenant, "one", "provider", providerId.ToString(),
            request with { BoundaryId = "other-boundary" }, Manage, "actor", default);
        await denied.Should().ThrowAsync<UnauthorizedAccessException>();
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
        var assigned = await sut.AssignSystemComponentPlacementAsync(tenant, "one", "provider", providerId.ToString(),
            request, Manage, "actor", default);

        // Assert
        options.CanAssignBoundary.Should().BeTrue();
        person.CanAssignBoundary.Should().BeFalse();
        person.AssignBlockedReason.Should().NotBeNullOrEmpty();
        assigned.BoundaryId.Should().Be("boundary");
        await using var after = await factory.CreateDbContextAsync();
        (await after.CspInheritedComponents.SingleAsync()).Name.Should().Be("Source-owned");
        (await after.BoundaryComponentAssignments.SingleAsync(x => x.CspInheritedComponentId == providerId)).TenantId.Should().Be(tenant);
        (await after.CapabilitySubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ComponentPlacement_ConcurrentRequests_SaveOnlyOneAssignment()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync(sqlite: true, separateConnections: true);
        await using var lifetime = factory;
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.SystemComponents.SingleAsync(x => x.Id == "orphan")).ComponentType = ComponentType.Thing;
            await db.SaveChangesAsync();
        }
        var sut = Service(factory);
        var options = await sut.GetSystemComponentPlacementsAsync(tenant, "one", "local", "orphan", Manage, default);
        var request = new AssignSystemComponentPlacementRequest("boundary", options.SourceRevision, options.RelationshipRevision);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var attempts = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await Record.ExceptionAsync(() => sut.AssignSystemComponentPlacementAsync(
                tenant, "one", "local", "orphan", request, Manage, "actor", default));
        })).ToArray();
        gate.SetResult();
        var outcomes = await Task.WhenAll(attempts);

        // Assert
        outcomes.Count(x => x is null).Should().Be(1);
        var conflict = outcomes.Single(x => x is not null).Should().BeOfType<SystemCapabilityConflictException>().Subject;
        conflict.Code.Should().BeOneOf("STALE_RELATIONSHIP", "STALE_PLACEMENT");
        await using var after = await factory.CreateDbContextAsync();
        (await after.BoundaryComponentAssignments.CountAsync(x => x.SystemComponentId == "orphan")).Should().Be(1);
        (await after.DashboardActivities.CountAsync(x => x.EventType == "BoundaryComponentAssigned")).Should().Be(1);
    }

    [Fact]
    public async Task ComponentPlacement_AuditFailureRollsBackAssignmentWithoutSuccessFallback()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync(sqlite: true);
        await using var lifetime = factory;
        await using var db = await factory.CreateDbContextAsync();
        (await db.SystemComponents.SingleAsync(x => x.Id == "orphan")).ComponentType = ComponentType.Thing;
        await db.SaveChangesAsync();
        var failing = new Factory(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(db.Database.GetDbConnection()).AddInterceptors(new PlacementAuditFailure()).Options);
        var sut = Service(failing);
        var options = await sut.GetSystemComponentPlacementsAsync(tenant, "one", "local", "orphan", Manage, default);

        // Act
        var attempt = () => sut.AssignSystemComponentPlacementAsync(tenant, "one", "local", "orphan",
            new("boundary", options.SourceRevision, options.RelationshipRevision), Manage, "actor", default);

        // Assert
        await attempt.Should().ThrowAsync<InvalidOperationException>().WithMessage("Synthetic audit failure.");
        (await db.BoundaryComponentAssignments.AnyAsync(x => x.SystemComponentId == "orphan")).Should().BeFalse();
        var refreshed = await sut.GetSystemComponentPlacementsAsync(tenant, "one", "local", "orphan", Manage, default);
        refreshed.RelationshipRevision.Should().Be(options.RelationshipRevision);
    }

    [Fact]
    public async Task ComponentPlacement_SetupRejectsPersonBoundaryButAllowsSystemWide()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var sut = Service(factory);
        var local = (await sut.ListSystemSecurityCapabilitiesAsync(tenant, "one",
            new(Scope: "available"), Manage, default)).Items.Single(x => x.RecordId == "local");
        var invalid = new PrepareSystemCapabilitySetupRequest("person-boundary",
            [new("local", "local", local.SourceRevision, [new("local", "person", "boundary")], [])]);

        // Act
        var rejected = () => sut.PrepareSystemCapabilitySetupAsync(tenant, "one", invalid, Manage, default);
        var valid = await sut.PrepareSystemCapabilitySetupAsync(tenant, "one",
            new("person-system", [new("local", "local", local.SourceRevision, [new("local", "person", null)], [])]), Manage, default);

        // Assert
        await rejected.Should().ThrowAsync<ArgumentException>().WithMessage("*Person*");
        valid.Operation.State.Should().Be("Prepared");
    }

    [Fact]
    public async Task ComponentPlacement_RejectsInvalidAndStaleRequests_AndPreservesOtherSystems()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync(sqlite: true);
        await using var lifetime = factory;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SystemComponents.Add(new() { Id = "shared", TenantId = tenant, Name = "Shared thing", ComponentType = ComponentType.Thing });
            db.AuthorizationBoundaryDefinitions.Add(new() { Id = "second", TenantId = tenant, RegisteredSystemId = "two", Name = "Second" });
            db.BoundaryComponentAssignments.Add(new() { Id = "retained", TenantId = tenant, AuthorizationBoundaryDefinitionId = "second", SystemComponentId = "shared" });
            await db.SaveChangesAsync();
        }
        var sut = Service(factory);
        var options = await sut.GetSystemComponentPlacementsAsync(tenant, "one", "local", "shared", Manage, default);
        var request = new AssignSystemComponentPlacementRequest("boundary", options.SourceRevision, options.RelationshipRevision);
        var stale = () => sut.AssignSystemComponentPlacementAsync(tenant, "one", "local", "shared",
            request with { SourceRevision = "old" }, Manage, "actor", default);
        var invalid = () => sut.AssignSystemComponentPlacementAsync(tenant, "one", "local", "shared",
            request with { SourceRevision = "" }, Manage, "actor", default);
        var foreign = () => sut.GetSystemComponentPlacementsAsync(Guid.NewGuid(), "one", "local", "shared", Manage, default);

        // Act
        var staleError = await stale.Should().ThrowAsync<SystemCapabilityConflictException>();
        await invalid.Should().ThrowAsync<ArgumentException>();
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
        var added = await sut.AssignSystemComponentPlacementAsync(tenant, "one", "local", "shared", request, Manage, "actor", default);
        var current = await sut.GetSystemComponentPlacementsAsync(tenant, "one", "local", "shared", Manage, default);
        var placement = current.Placements.Single();
        var duplicate = () => sut.AssignSystemComponentPlacementAsync(tenant, "one", "local", "shared",
            request with { RelationshipRevision = current.RelationshipRevision }, Manage, "actor", default);
        var duplicateError = await duplicate.Should().ThrowAsync<SystemCapabilityConflictException>();
        var wrong = () => sut.UnassignSystemComponentPlacementAsync(tenant, "one", "local", "shared", "retained",
            new(current.SourceRevision, current.RelationshipRevision, placement.Revision), Manage, "actor", default);
        await wrong.Should().ThrowAsync<KeyNotFoundException>();
        await sut.UnassignSystemComponentPlacementAsync(tenant, "one", "local", "shared", added.PlacementId,
            new(current.SourceRevision, current.RelationshipRevision, placement.Revision), Manage, "actor", default);

        // Assert
        staleError.Which.Code.Should().Be("STALE_SOURCE");
        duplicateError.Which.Code.Should().Be("DUPLICATE_PLACEMENT");
        await using var after = await factory.CreateDbContextAsync();
        (await after.BoundaryComponentAssignments.SingleAsync(x => x.SystemComponentId == "shared")).Id.Should().Be("retained");
        (await after.SystemComponents.SingleAsync(x => x.Id == "shared")).Name.Should().Be("Shared thing");
        (await after.SystemCapabilityLinks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ComponentPlacement_BlockedPersonAndLegacyRemoval_HaveExplicitReasons()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var sut = Service(factory);
        var person = await sut.GetSystemComponentPlacementsAsync(tenant, "one", "local", "person", Manage, default);
        var legacy = await sut.GetSystemComponentPlacementsAsync(tenant, "one", "local", "orphan", Manage, default);
        var oldPlacement = legacy.Placements.Single();

        // Act
        var assign = () => sut.AssignSystemComponentPlacementAsync(tenant, "one", "local", "person",
            new("boundary", person.SourceRevision, person.RelationshipRevision), Manage, "actor", default);
        var remove = () => sut.UnassignSystemComponentPlacementAsync(tenant, "one", "local", "orphan", oldPlacement.Id,
            new(legacy.SourceRevision, legacy.RelationshipRevision, oldPlacement.Revision), Manage, "actor", default);
        var assignError = await assign.Should().ThrowAsync<SystemCapabilityConflictException>();
        var removeError = await remove.Should().ThrowAsync<SystemCapabilityConflictException>();
        var denied = await sut.GetSystemComponentPlacementsAsync(tenant, "one", "local", "person", Manage with { CanManage = false }, default);

        // Assert
        assignError.Which.Code.Should().Be("PLACEMENT_BLOCKED");
        removeError.Which.Code.Should().Be("PLACEMENT_BLOCKED");
        denied.Placements.Should().OnlyContain(x => !x.CanUnassign && x.UnassignBlockedReason != null);
        oldPlacement.UnassignBlockedReason.Should().Contain("assignment workflow");
    }

    [Fact]
    public async Task ProviderRelease_UsesAllContributorsAndImmutableCoverage_NotUnpublishedEdits()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var providerId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var contributorId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var profileId = Guid.NewGuid();
            db.CspProfiles.Add(new() { Id = profileId, DisplayName = "Synthetic provider" });
            db.CspInheritedComponents.AddRange(
                new CspInheritedComponent { Id = parentId, CspProfileId = profileId, Name = "Platform", Status = CspInheritedComponentStatus.Published },
                new CspInheritedComponent { Id = contributorId, CspProfileId = profileId, Name = "Contributor", Status = CspInheritedComponentStatus.Published });
            db.CspInheritedCapabilities.Add(new()
            {
                Id = providerId, CspInheritedComponentId = parentId, Name = "Provider capability",
                Status = CspInheritedCapabilityStatus.Mapped, MappedNistControlIds = ["AC-2"]
            });
            db.ProviderCapabilityReleases.Add(new()
            {
                CapabilityId = providerId, Revision = 1, SnapshotHash = "published",
                SnapshotJson = JsonSerializer.Serialize(new
                {
                    ContributorsJson = JsonSerializer.Serialize(new[] { contributorId.ToString() }),
                    DutiesJson = "{\"AC-1\":\"Provider operates the platform\"}",
                    Capability = new { Name = "Published", Description = "Published content", Controls = new[] { "AC-1" } }
                })
            });
            db.BoundaryComponentAssignments.Add(new()
            {
                TenantId = tenant, CspInheritedComponentId = contributorId, AuthorizationBoundaryDefinitionId = "boundary"
            });
            await db.SaveChangesAsync();
        }
        var sut = Service(factory);

        // Act
        var detail = await sut.GetSystemSecurityCapabilityAsync(tenant, "one", "provider", "capability", providerId.ToString(), Manage, default);

        // Assert
        detail!.Item.Components.Select(x => x.RecordId).Should().BeEquivalentTo(parentId.ToString(), contributorId.ToString());
        detail.Item.ControlIds.Should().BeEquivalentTo("AC-1");
        detail.Item.Name.Should().Be("Published");
        detail.Item.Description.Should().Be("Published content");
        detail.Controls.Single().ProviderCoverage.Should().Be("Provider operates the platform");
        detail.Item.MutationAuthority.Should().Be("provider");
        detail.Item.Components.Single(x => x.RecordId == contributorId.ToString()).Placements
            .Should().Contain(x => x.BoundaryId == "boundary");
    }

    [Fact]
    public async Task SqlitePartialFailure_RetriesIncompleteWrites_AndConcurrentClaimsAreRejected()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync(sqlite: true);
        await using var lifetime = factory;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var changes = new Mock<INarrativeChangeImpactService>(MockBehavior.Strict);
        changes.Setup(x => x.QueueAsync(It.IsAny<NarrativeChangeImpactRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (NarrativeChangeImpactRequest _, CancellationToken _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.SetResult();
                    await release.Task;
                    throw new IOException("Synthetic transient delivery failure");
                }
                return new NarrativeChangeImpactResult([]);
            });
        var sut = new WorkspaceOperationsService(factory, narrativeChanges: changes.Object);
        var item = (await sut.ListSystemSecurityCapabilitiesAsync(tenant, "two", new(Scope: "available"), Manage, default)).Items.First(x => x.RecordId == "local");
        var prepared = await sut.PrepareSystemCapabilitySetupAsync(tenant, "two",
            new("retry", [new("local", item.RecordId, item.SourceRevision, [], [])]), Manage, default);

        // Act
        var first = sut.CompleteSystemCapabilitySetupAsync(tenant, "two", prepared.Operation.OperationId,
            new(prepared.Operation.Revision), Manage, "actor", default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var concurrent = () => sut.CompleteSystemCapabilitySetupAsync(tenant, "two", prepared.Operation.OperationId,
            new(prepared.Operation.Revision), Manage, "actor", default);
        var conflict = await concurrent.Should().ThrowAsync<SystemCapabilityConflictException>();
        release.SetResult();
        await ((Func<Task>)(() => first)).Should().ThrowAsync<SystemCapabilityWriteException>();
        var partial = await sut.GetSystemCapabilityOperationAsync(tenant, "two", prepared.Operation.OperationId, Manage, default);
        var completed = await sut.CompleteSystemCapabilitySetupAsync(tenant, "two", prepared.Operation.OperationId,
            new(partial!.Revision), Manage, "actor", default);

        // Assert
        conflict.Which.Code.Should().Be("SETUP_IN_PROGRESS");
        partial.State.Should().Be("Partial");
        partial.Outcomes.Should().Contain(x => x.WriteKind == "system-link" && x.State == "Completed");
        partial.Outcomes.Should().Contain(x => x.WriteKind == "narrative-change" && x.State == "Failed");
        completed.State.Should().Be("Completed");
        partial.PlannedWrites.Should().BeEquivalentTo(prepared.Operation.PlannedWrites, x => x.WithStrictOrdering());
        completed.PlannedWrites.Should().BeEquivalentTo(prepared.Operation.PlannedWrites, x => x.WithStrictOrdering());
        calls.Should().Be(2);
        await using var after = await factory.CreateDbContextAsync();
        (await after.SystemCapabilityLinks.CountAsync(x => x.RegisteredSystemId == "two")).Should().Be(1);
        (await after.ControlImplementations.CountAsync(x => x.RegisteredSystemId == "two")).Should().Be(1);
    }

    [Fact]
    public async Task ManagementPermission_AndImmutableIdempotencyIntentAreRequired()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var sut = Service(factory);
        var items = (await sut.ListSystemSecurityCapabilitiesAsync(tenant, "two", new(Scope: "available"), Manage, default)).Items;
        var request = new PrepareSystemCapabilitySetupRequest("intent", [new("local", items[0].RecordId, items[0].SourceRevision, [], [])]);
        await sut.PrepareSystemCapabilitySetupAsync(tenant, "two", request, Manage, default);

        // Act
        var unauthorized = () => sut.PrepareSystemCapabilitySetupAsync(tenant, "two", request,
            Manage with { CanManage = false, CanReviewResponsibilities = true }, default);
        var mismatch = () => sut.PrepareSystemCapabilitySetupAsync(tenant, "two",
            request with { Selections = [new("local", items[1].RecordId, items[1].SourceRevision, [], [])] }, Manage, default);

        // Assert
        await unauthorized.Should().ThrowAsync<UnauthorizedAccessException>();
        (await mismatch.Should().ThrowAsync<SystemCapabilityConflictException>()).Which.Code.Should().Be("SETUP_INTENT_CONFLICT");
    }

    [Fact]
    public async Task RemovalIdempotency_BindsTheReviewedRelationshipRevision()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var sut = Service(factory);
        var detail = await sut.GetSystemSecurityCapabilityAsync(tenant, "one", "local", "capability", "local", Manage, default);
        var request = new PrepareSystemCapabilityRemovalRequest("removal-intent", detail!.Item.SourceRevision, detail.RelationshipRevision);
        await sut.PrepareSystemCapabilityRemovalAsync(tenant, "one", "local", "local", request, Manage, default);

        // Act
        var changed = () => sut.PrepareSystemCapabilityRemovalAsync(tenant, "one", "local", "local",
            request with { RelationshipRevision = "different-reviewed-relationships" }, Manage, default);

        // Assert
        (await changed.Should().ThrowAsync<SystemCapabilityConflictException>()).Which.Code.Should().Be("SETUP_INTENT_CONFLICT");
    }

    [Fact]
    public async Task ExecutionRejectsChangedRelationships_AndForeignPlacements()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var sut = Service(factory);
        var item = (await sut.ListSystemSecurityCapabilitiesAsync(tenant, "two", new(Scope: "available"), Manage, default)).Items.First(x => x.RecordId == "local");
        var request = new PrepareSystemCapabilitySetupRequest("relation", [new("local", item.RecordId, item.SourceRevision, [], [])]);
        var prepared = await sut.PrepareSystemCapabilitySetupAsync(tenant, "two", request, Manage, default);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ComponentSystemAssignments.Add(new() { TenantId = tenant, RegisteredSystemId = "two", SystemComponentId = "person" });
            await db.SaveChangesAsync();
        }

        // Act
        var execute = () => sut.CompleteSystemCapabilitySetupAsync(tenant, "two", prepared.Operation.OperationId,
            new(prepared.Operation.Revision), Manage, "actor", default);
        var foreign = () => sut.PrepareSystemCapabilitySetupAsync(tenant, "two",
            new("foreign", [new("local", item.RecordId, item.SourceRevision, [new("local", "person", "boundary")], [])]), Manage, default);

        // Assert
        (await execute.Should().ThrowAsync<SystemCapabilityConflictException>()).Which.Code.Should().Be("STALE_RELATIONSHIP");
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Detail_ProtectsEvidenceOrigin_AndPreservesIndependentApprovedNarratives()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var valid = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var implementation = new ControlImplementation
            {
                Id = "impl", TenantId = tenant, RegisteredSystemId = "one", ControlId = "AC-1",
                PolicyNarrative = "Approved policy", TechnicalNarrative = "Approved technical"
            };
            var snapshot = NarrativeContentSnapshot.Capture(implementation);
            implementation.TechnicalNarrative = "Changed technical";
            implementation.ApprovedVersionId = "version";
            db.ControlImplementations.Add(implementation);
            db.NarrativeVersions.Add(new() { Id = "version", TenantId = tenant, ControlImplementationId = "impl", SnapshotJson = snapshot });
            db.EvidenceArtifacts.AddRange(
                new() { Id = "safe", TenantId = tenant, RegisteredSystemId = "one", ControlImplementationId = "impl",
                    FileName = "private/path/proof.pdf", StoragePath = "https://private.invalid/blob?sig=secret" },
                new() { Id = "foreign-system", TenantId = tenant, RegisteredSystemId = "two", SecurityCapabilityId = "local", FileName = "hidden.pdf" },
                new() { Id = "foreign-tenant", TenantId = Guid.NewGuid(), RegisteredSystemId = "one", SecurityCapabilityId = "local", FileName = "hidden.pdf" });
            db.NarrativeProposals.AddRange(
                new() { Id = valid, TenantId = tenant, RegisteredSystemId = "one", ControlId = "AC-1", NarrativeType = "Technical",
                    ChangeSourceKind = "OrganizationCapability", ChangeSourceId = "local", Status = "Draft" },
                new() { Id = Guid.NewGuid(), TenantId = tenant, RegisteredSystemId = "two", ControlId = "AC-1", NarrativeType = "Policy",
                    ChangeSourceKind = "OrganizationCapability", ChangeSourceId = "local" },
                new() { Id = Guid.NewGuid(), TenantId = tenant, RegisteredSystemId = "one", ControlId = "AC-1", NarrativeType = "Policy",
                    ChangeSourceKind = "OrganizationCapability", ChangeSourceId = "unused" });
            await db.SaveChangesAsync();
        }
        var sut = Service(factory);

        // Act
        var detail = await sut.GetSystemSecurityCapabilityAsync(tenant, "one", "local", "capability", "local", Manage, default);

        // Assert
        detail!.Evidence.Should().ContainSingle();
        detail.ResponsibilityReviewUrl.Should().Be("/systems/one/inheritance/subscriptions");
        detail.Evidence[0].Id.Should().Be("safe");
        detail.Evidence[0].FileName.Should().Be("proof.pdf");
        detail.Evidence[0].OpenUrl.Should().Be("/api/dashboard/systems/one/evidence/safe/download");
        JsonSerializer.Serialize(detail).Should().NotContain("sig=secret").And.NotContain("private/path");
        detail.Narratives.Single(x => x.NarrativeType == "Policy").Freshness.Should().Be("Current");
        var technical = detail.Narratives.Single(x => x.NarrativeType == "Technical");
        technical.ApprovedContent.Should().Be("Approved technical");
        technical.CurrentContent.Should().Be("Changed technical");
        technical.Freshness.Should().Be("ReviewRequired");
        technical.Proposals.Should().ContainSingle(x => x.Id == valid);
    }

    [Fact]
    public async Task ExecutionRejectsChangedBaseline_AndDoesNotInferAllocationFromMappingRole()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var sut = Service(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ControlBaselines.Add(new() { Id = "baseline", TenantId = tenant, RegisteredSystemId = "two", ControlIds = ["AC-1"] });
            await db.SaveChangesAsync();
        }
        var detail = await sut.GetSystemSecurityCapabilityAsync(tenant, "two", "local", "capability", "local", Manage, default);
        var prepared = await sut.PrepareSystemCapabilitySetupAsync(tenant, "two",
            new("baseline", [new("local", "local", detail!.Item.SourceRevision, [], [])]), Manage, default);
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.ControlBaselines.SingleAsync()).ControlIds = ["AC-1", "AC-2"];
            await db.SaveChangesAsync();
        }

        // Act
        var action = () => sut.CompleteSystemCapabilitySetupAsync(tenant, "two", prepared.Operation.OperationId,
            new(prepared.Operation.Revision), Manage, "actor", default);

        // Assert
        detail.Controls.Single().Allocation.Should().BeNull();
        detail.Controls.Single().ReviewState.Should().Be("Undesignated");
        (await action.Should().ThrowAsync<SystemCapabilityConflictException>()).Which.Code.Should().Be("STALE_BASELINE");
    }

    [Fact]
    public async Task ProviderSetupAndRemoval_PreserveOtherSystemsSourceAndLocalSupportPlacements()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var providerId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var profileId = Guid.NewGuid();
            db.CspProfiles.Add(new() { Id = profileId, DisplayName = "Provider" });
            db.CspInheritedComponents.Add(new()
            {
                Id = componentId, CspProfileId = profileId, Name = "Provider component", Status = CspInheritedComponentStatus.Published
            });
            db.CspInheritedCapabilities.Add(new()
            {
                Id = providerId, CspInheritedComponentId = componentId, Name = "Provider capability",
                Status = CspInheritedCapabilityStatus.Mapped
            });
            db.CapabilitySubscriptions.Add(new()
            {
                RegisteredSystemId = "one", RoutingTenantId = tenant,
                CspInheritedCapabilityId = providerId.ToString(), RoutingCapabilityId = providerId.ToString(), IsActive = true
            });
            db.AuthorizationBoundaryDefinitions.Add(new() { Id = "two-boundary", TenantId = tenant, RegisteredSystemId = "two", Name = "Two" });
            await db.SaveChangesAsync();
        }
        var responsibilities = new Mock<ICapabilityResponsibilityService>(MockBehavior.Strict);
        responsibilities.Setup(x => x.PreviewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string system, CancellationToken _) => new CapabilityResponsibilityResponse(system, null, false, [], []));
        responsibilities.Setup(x => x.ReconcileSetupAsync(It.IsAny<AtoCopilotContext>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AtoCopilotContext _, string system, string _, CancellationToken _) =>
                new CapabilityResponsibilityResponse(system, null, false, [], []));
        var changes = new Mock<INarrativeChangeImpactService>(MockBehavior.Strict);
        changes.Setup(x => x.QueueAsync(It.IsAny<NarrativeChangeImpactRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NarrativeChangeImpactResult([]));
        var sut = new WorkspaceOperationsService(factory, responsibilityService: responsibilities.Object, narrativeChanges: changes.Object);
        var available = await sut.ListSystemSecurityCapabilitiesAsync(tenant, "two", new(Scope: "available"), Manage, default);
        var provider = available.Items.Single(x => x.Source == "provider");
        var local = available.Items.Single(x => x.RecordId == "local");
        var prepared = await sut.PrepareSystemCapabilitySetupAsync(tenant, "two", new("provider-setup",
            [new("provider", provider.RecordId, provider.SourceRevision,
                [new("provider", componentId.ToString(), "two-boundary"), new("local", "person", null)],
                [new("local", local.SourceRevision)])]), Manage, default);

        // Act
        var completed = await sut.CompleteSystemCapabilitySetupAsync(tenant, "two", prepared.Operation.OperationId,
            new(prepared.Operation.Revision), Manage, "actor", default);
        var detail = await sut.GetSystemSecurityCapabilityAsync(tenant, "two", "provider", "capability", provider.RecordId, Manage, default);
        var removal = await sut.PrepareSystemCapabilityRemovalAsync(tenant, "two", "provider", provider.RecordId,
            new("provider-remove", detail!.Item.SourceRevision, detail.RelationshipRevision), Manage, default);
        await sut.CompleteSystemCapabilitySetupAsync(tenant, "two", removal.Operation.OperationId,
            new(removal.Operation.Revision), Manage, "actor", default);

        // Assert
        completed.State.Should().Be("Completed");
        prepared.Operation.PlannedWrites.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.DisplayLabel));
        prepared.Operation.PlannedWrites.Single(x => x.WriteKind == "support-link").DisplayLabel.Should()
            .Contain("Access").And.Contain("Provider capability");
        prepared.Operation.PlannedWrites.Single(x => x.WriteKind == "subscription").DisplayLabel.Should().Contain("Provider capability");
        prepared.Operation.PlannedWrites.Single(x => x.WriteKind == "responsibility-reconciliation").DisplayLabel.Should()
            .Be("Reconcile control responsibilities for this system");
        removal.Operation.PlannedWrites.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.DisplayLabel));
        removal.Operation.PlannedWrites.Single(x => x.WriteKind == "unsubscribe").DisplayLabel.Should().Contain("Provider capability");
        detail.Item.Components.Select(x => x.RecordId).Should().BeEquivalentTo(componentId.ToString(), "person");
        detail.Item.Capabilities.Should().ContainSingle(x => x.Source == "local" && x.RecordId == "local");
        await using var after = await factory.CreateDbContextAsync();
        (await after.CapabilitySubscriptions.SingleAsync(x => x.RegisteredSystemId == "one")).IsActive.Should().BeTrue();
        (await after.CapabilitySubscriptions.SingleAsync(x => x.RegisteredSystemId == "two")).IsActive.Should().BeFalse();
        (await after.BoundaryComponentAssignments.CountAsync(x => x.AuthorizationBoundaryDefinitionId == "two-boundary")).Should().Be(1);
        (await after.ComponentSystemAssignments.CountAsync(x => x.RegisteredSystemId == "two" && x.SystemComponentId == "person"
            && x.AuthorizationBoundaryDefinitionId == null)).Should().Be(1);
        (await after.SystemCapabilityLinks.SingleAsync(x => x.RegisteredSystemId == "two")).SupportingProviderCapabilityIdsJson.Should().Contain(providerId.ToString());
        (await after.CspInheritedCapabilities.SingleAsync()).Name.Should().Be("Provider capability");
        (await after.DashboardActivities.Where(x => x.RegisteredSystemId == "two")
            .Select(x => x.EventType).ToListAsync()).Should().BeEquivalentTo("CapabilitySubscribed", "CapabilityUnsubscribed");
        responsibilities.Verify(x => x.ReconcileSetupAsync(It.IsAny<AtoCopilotContext>(), "two", "actor",
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProviderDetail_UsesPerControlAllocations_AndRedactsCurrentAndReviewedSourceSecrets()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var capabilityId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspProfiles.Add(new() { Id = profileId, DisplayName = "Provider" });
            db.CspInheritedComponents.Add(new() { Id = componentId, CspProfileId = profileId, Name = "Source", Status = CspInheritedComponentStatus.Published });
            db.CspInheritedCapabilities.Add(new()
            {
                Id = capabilityId, CspInheritedComponentId = componentId, Name = "Control source",
                Status = CspInheritedCapabilityStatus.Mapped, MappedNistControlIds = ["AC-1", "AC-2"]
            });
            db.CapabilitySubscriptions.Add(new()
            {
                Id = "subscription", RoutingTenantId = tenant, RegisteredSystemId = "one",
                CspInheritedCapabilityId = capabilityId.ToString(), RoutingCapabilityId = capabilityId.ToString()
            });
            await db.SaveChangesAsync();
        }
        var snapshot = """{"References":["https://secret.invalid/token"],"Capability":{"Description":"Provider coverage","Component":{"SourceArtifactReference":"private/container/file"}}}""";
        var preview = new CapabilityResponsibilityResponse("one", null, true,
        [
            new("subscription", capabilityId, componentId, profileId, "AC-1", "available", "review1", "Applied",
                "confirmed", "reviewer", DateTimeOffset.UtcNow, new("AC-1", "Shared", "Provider", "Customer integration"),
                "Shared", "CapabilitySubscription", true, snapshot, snapshot),
            new("subscription", capabilityId, componentId, profileId, "AC-2", "available", "review2", "PreservedOverride",
                "confirmed", "reviewer", DateTimeOffset.UtcNow, new("AC-2", "Customer", null, "Customer operation"),
                "Customer", "Manual", true, snapshot, snapshot)
        ], []);
        var responsibility = new Mock<ICapabilityResponsibilityService>(MockBehavior.Strict);
        responsibility.Setup(x => x.PreviewAsync("one", It.IsAny<CancellationToken>())).ReturnsAsync(preview);
        var sut = new WorkspaceOperationsService(factory, responsibilityService: responsibility.Object);

        // Act
        var detail = await sut.GetSystemSecurityCapabilityAsync(tenant, "one", "provider", "capability", capabilityId.ToString(), Manage, default);

        // Assert
        detail!.Controls.Single(x => x.ControlId == "AC-1").Allocation.Should().Be("Shared");
        detail.Controls.Single(x => x.ControlId == "AC-2").Allocation.Should().Be("Customer");
        detail.Controls.Single(x => x.ControlId == "AC-2").ReviewState.Should().Be("PreservedOverride");
        detail.Controls.Single(x => x.ControlId == "AC-1").OrganizationDuty.Should().Be("Customer integration");
        JsonSerializer.Serialize(detail).Should().NotContain("secret.invalid").And.NotContain("private/container");
        detail.Controls.Should().OnlyContain(x => x.ConfirmedSourceRevision == "confirmed" && x.AvailableSourceRevision == "available");
    }

    [Fact]
    public async Task AppliedList_IncludesReusableContributorsAndDirectOrphans_NotOtherSystems()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var page = await sut.ListSystemSecurityCapabilitiesAsync(tenant, "one",
            new(Grouping: "component"), Manage, default);

        // Assert
        page.Items.Select(x => x.RecordId).Should().BeEquivalentTo("person", "orphan");
        page.Items.Single(x => x.RecordId == "person").Capabilities.Should().ContainSingle();
        page.Items.Single(x => x.RecordId == "orphan").Capabilities.Should().BeEmpty();
        page.Total.Should().Be(2);
    }

    [Fact]
    public async Task Filters_AreAppliedBeforeDistinctTotalAndPagination()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var page = await sut.ListSystemSecurityCapabilitiesAsync(tenant, "one",
            new(Grouping: "component", ComponentType: "Person", BoundaryId: "boundary", PageSize: 1),
            Manage, default);

        // Assert
        page.Total.Should().Be(1);
        page.Items.Single().RecordId.Should().Be("person");
        page.Items.Single().Placements.Should().Contain(x => x.BoundaryId == "boundary");
    }

    [Fact]
    public async Task Available_IsDifferentFromApplied_AndMarksAlreadyApplied()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var applied = await sut.ListSystemSecurityCapabilitiesAsync(tenant, "one", new(), Manage, default);
        var available = await sut.ListSystemSecurityCapabilitiesAsync(tenant, "one", new(Scope: "available"), Manage, default);

        // Assert
        applied.Items.Select(x => x.RecordId).Should().BeEquivalentTo("local");
        available.Items.Select(x => x.RecordId).Should().BeEquivalentTo("local", "unused");
        available.Items.Single(x => x.RecordId == "local").IsApplied.Should().BeTrue();
        available.Items.Single(x => x.RecordId == "unused").IsApplied.Should().BeFalse();
    }

    [Fact]
    public async Task SelectedSystem_AndTenantAreValidatedEvenWhenAccessWasSupplied()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var foreign = () => sut.ListSystemSecurityCapabilitiesAsync(Guid.NewGuid(), "one", new(), Manage, default);
        var hidden = () => sut.ListSystemSecurityCapabilitiesAsync(tenant, "one", new(), Manage with { CanRead = false }, default);

        // Assert
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
        await hidden.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultiSelection_PrepareIsReadOnly_CompletionIsDurableAndReplaySafe(bool legacyPlan)
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var sut = Service(factory);
        var available = await sut.ListSystemSecurityCapabilitiesAsync(tenant, "two", new(Scope: "available"), Manage, default);
        var request = new PrepareSystemCapabilitySetupRequest("multi",
            available.Items.Select(x => new SystemCapabilitySelection(x.Source, x.RecordId, x.SourceRevision, [], [])).ToArray());

        // Act
        var prepared = await sut.PrepareSystemCapabilitySetupAsync(tenant, "two", request, Manage, default);
        await using var before = await factory.CreateDbContextAsync();
        if (legacyPlan)
        {
            var row = await before.CapabilitySetupOperations.SingleAsync(x => x.Id == prepared.Operation.OperationId);
            var json = System.Text.Json.Nodes.JsonNode.Parse(row.SystemPlanJson!)!;
            foreach (var write in json["Writes"]!.AsArray()) write!.AsObject().Remove("DisplayLabel");
            row.SystemPlanJson = json.ToJsonString();
            await before.SaveChangesAsync();
        }
        var beforeCount = await before.SystemCapabilityLinks.CountAsync(x => x.RegisteredSystemId == "two");
        var complete = await sut.CompleteSystemCapabilitySetupAsync(tenant, "two", prepared.Operation.OperationId,
            new(prepared.Operation.Revision), Manage, "actor", default);
        var replay = await sut.CompleteSystemCapabilitySetupAsync(tenant, "two", complete.OperationId,
            new(complete.Revision), Manage, "actor", default);

        // Assert
        beforeCount.Should().Be(0);
        complete.State.Should().Be("Completed");
        complete.PlannedWrites.Should().BeEquivalentTo(prepared.Operation.PlannedWrites
            .Select(x => legacyPlan ? x with { DisplayLabel = null } : x), x => x.WithStrictOrdering());
        replay.Should().BeEquivalentTo(complete);
        await using var after = await factory.CreateDbContextAsync();
        (await after.SystemCapabilityLinks.CountAsync(x => x.RegisteredSystemId == "two")).Should().Be(2);
        complete.Outcomes.Should().HaveCount(complete.PlannedWrites.Count);
        complete.Outcomes.Should().OnlyContain(x => x.State == "Completed");
    }

    [Fact]
    public async Task PrepareRejectsStaleSourceAndExecutionRevalidatesIt()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        var sut = new WorkspaceOperationsService(factory);
        var available = await sut.ListSystemSecurityCapabilitiesAsync(tenant, "two", new(Scope: "available"), Manage, default);
        var item = available.Items.First();
        var request = new PrepareSystemCapabilitySetupRequest("stale", [new(item.Source, item.RecordId, item.SourceRevision, [], [])]);
        var prepared = await sut.PrepareSystemCapabilitySetupAsync(tenant, "two", request, Manage, default);
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.SecurityCapabilities.SingleAsync(x => x.Id == item.RecordId)).Description = "Changed";
            await db.SaveChangesAsync();
        }

        // Act
        var prepare = () => sut.PrepareSystemCapabilitySetupAsync(tenant, "two", request with { IdempotencyKey = "stale2" }, Manage, default);
        var execute = () => sut.CompleteSystemCapabilitySetupAsync(tenant, "two", prepared.Operation.OperationId,
            new(prepared.Operation.Revision), Manage, "actor", default);

        // Assert
        (await prepare.Should().ThrowAsync<SystemCapabilityConflictException>()).Which.Code.Should().Be("STALE_SOURCE");
        (await execute.Should().ThrowAsync<SystemCapabilityConflictException>()).Which.Code.Should().Be("STALE_SOURCE");
    }

    [Fact]
    public async Task RemovalRetainsSharedSourcePlacementsAndApprovedNarrative()
    {
        // Arrange
        var (factory, tenant) = await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SystemCapabilityLinks.Add(new() { TenantId = tenant, RegisteredSystemId = "two", SecurityCapabilityId = "local" });
            db.ControlImplementations.Add(new()
            {
                Id = "implementation", TenantId = tenant, RegisteredSystemId = "one", ControlId = "AC-1",
                SecurityCapabilityId = "local", PolicyNarrative = "Approved", ApprovalStatus = SspSectionStatus.Approved
            });
            await db.SaveChangesAsync();
        }
        var sut = Service(factory);
        var detail = await sut.GetSystemSecurityCapabilityAsync(tenant, "one", "local", "capability", "local", Manage, default);
        var prepared = await sut.PrepareSystemCapabilityRemovalAsync(tenant, "one", "local", "local",
            new("remove", detail!.Item.SourceRevision, detail.RelationshipRevision), Manage, default);

        // Act
        await sut.CompleteSystemCapabilitySetupAsync(tenant, "one", prepared.Operation.OperationId,
            new(prepared.Operation.Revision), Manage, "actor", default);

        // Assert
        await using var after = await factory.CreateDbContextAsync();
        (await after.SystemCapabilityLinks.CountAsync(x => x.RegisteredSystemId == "one")).Should().Be(0);
        (await after.SystemCapabilityLinks.CountAsync(x => x.RegisteredSystemId == "two")).Should().Be(1);
        (await after.SecurityCapabilities.CountAsync()).Should().Be(2);
        (await after.BoundaryComponentAssignments.CountAsync()).Should().Be(1);
        (await after.ControlImplementations.SingleAsync()).PolicyNarrative.Should().Be("Approved");
    }

    private static async Task<(Factory Factory, Guid Tenant)> SeedAsync(bool sqlite = false, bool separateConnections = false)
    {
        SqliteConnection? connection = null;
        var options = new DbContextOptionsBuilder<AtoCopilotContext>();
        if (sqlite)
        {
            connection = new SqliteConnection(separateConnections
                ? $"Data Source=placement-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=2;Pooling=False"
                : "Data Source=:memory:");
            await connection.OpenAsync();
            if (separateConnections) options.UseSqlite(connection.ConnectionString);
            else options.UseSqlite(connection);
        }
        else options.UseInMemoryDatabase(Guid.NewGuid().ToString());
        var factory = new Factory(options.Options, connection);
        var tenant = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new Tenant { Id = tenant, DisplayName = "Synthetic organization" });
        db.RegisteredSystems.AddRange(new RegisteredSystem { Id = "one", TenantId = tenant, Name = "One" },
            new RegisteredSystem { Id = "two", TenantId = tenant, Name = "Two" });
        db.AuthorizationBoundaryDefinitions.Add(new() { Id = "boundary", TenantId = tenant, RegisteredSystemId = "one", Name = "Operations" });
        db.SecurityCapabilities.AddRange(
            new SecurityCapability { Id = "local", TenantId = tenant, Name = "Access", Category = "AC" },
            new SecurityCapability { Id = "unused", TenantId = tenant, Name = "Unapplied", Category = "AU" });
        db.SystemCapabilityLinks.Add(new() { TenantId = tenant, RegisteredSystemId = "one", SecurityCapabilityId = "local" });
        db.SystemComponents.AddRange(new SystemComponent { Id = "person", TenantId = tenant, Name = "Operators", ComponentType = ComponentType.Person },
            new SystemComponent { Id = "orphan", TenantId = tenant, Name = "Direct orphan", RegisteredSystemId = "one" },
            new SystemComponent { Id = "other", TenantId = tenant, Name = "Other system", RegisteredSystemId = "two" });
        db.ComponentCapabilityLinks.Add(new() { TenantId = tenant, SystemComponentId = "person", SecurityCapabilityId = "local" });
        db.ComponentSystemAssignments.Add(new() { TenantId = tenant, SystemComponentId = "person", RegisteredSystemId = "one" });
        db.BoundaryComponentAssignments.Add(new() { TenantId = tenant, SystemComponentId = "person", AuthorizationBoundaryDefinitionId = "boundary" });
        db.CapabilityControlMappings.Add(new() { TenantId = tenant, SecurityCapabilityId = "local", ControlId = "AC-1" });
        await db.SaveChangesAsync();
        return (factory, tenant);
    }

    private sealed class Factory(DbContextOptions<AtoCopilotContext> options, SqliteConnection? connection = null)
        : IDbContextFactory<AtoCopilotContext>, IAsyncDisposable
    {
        public AtoCopilotContext CreateDbContext() => new(options);
        public Task<AtoCopilotContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
        public ValueTask DisposeAsync() => connection?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private sealed class PlacementAuditFailure : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<DashboardActivity>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Synthetic audit failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

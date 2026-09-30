using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Environments;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class SystemEnvironmentServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _registration = Guid.NewGuid();
    private readonly Guid _subscription = Guid.NewGuid();
    private readonly Guid _directory = Guid.NewGuid();
    private readonly Guid _person = Guid.NewGuid();
    private readonly string _system = Guid.NewGuid().ToString();
    private readonly TenantContextAccessor _accessor = new();
    private readonly Mock<ISystemWorkspaceAccessService> _access = new();
    private readonly Mock<ISystemEnvironmentAzureSource> _azure = new();
    private DbContextOptions<AtoCopilotContext> _options = null!;
    private string Resource => $"/subscriptions/{_subscription}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/one";
    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options;
        await using var db = new AtoCopilotContext(_options);
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new() { Id = _tenant, DisplayName = "Synthetic organization" });
        db.RegisteredSystems.Add(new() { Id = _system, TenantId = _tenant, Name = "Synthetic system" });
        db.Persons.Add(new() { Id = _person, TenantId = _tenant, DisplayName = "Synthetic owner", Email = "owner@example.invalid" });
        db.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person,
            Role = OrganizationRole.SystemOwner });
        db.AzureSubscriptionRegistrations.Add(new() { Id = _registration, TenantId = _tenant,
            SubscriptionId = _subscription, ParentTenantId = _directory, DisplayName = "Synthetic subscription" });
        await db.SaveChangesAsync();
        _access.Setup(x => x.GetAccessAsync(_tenant, It.IsAny<Guid?>(), _system, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemWorkspaceAccessResponse(_system, ["SystemOwner"],
                new(true, true, true, false, false, false, false, false, false)));
        _azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EnvironmentResource(Resource, "one", "microsoft.compute/virtualmachines", "rg", "eastus")]);
    }
    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();
    private SystemEnvironmentService Service(TenantContext tenant)
    {
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(_options, _accessor));
        return new(factory.Object, tenant, _access.Object, _azure.Object);
    }
    private TenantContext Tenant() => new(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
    private EnvironmentSourceSelection Selection => new("OrganizationOwned", _registration, null, null);

    [Fact]
    public async Task Apply_Replay_UsesOneRegistrationAndExactResources()
    {
        // Arrange
        var tenant = Tenant();
        using var scope = _accessor.Push(tenant);
        var service = Service(tenant);
        var discovery = await service.DiscoverAsync(_system, new(0, Selection), "actor");
        var input = new ApplySystemEnvironmentRequest(0, Selection, discovery.DiscoveryToken, [Resource], [], [], null);
        // Act
        var first = await service.ApplyAsync(_system, input, "stable-key", "actor");
        var replay = await service.ApplyAsync(_system, input, "stable-key", "actor");
        // Assert
        replay.Should().BeEquivalentTo(first);
        first.Attachments.Single().Scope.ResourceIds.Should().Equal(Resource);
        first.Attachments.Single().AssessmentAccess.State.Should().Be("NotChecked");
        first.Attachments.Single().Monitoring.Enabled.Should().BeFalse();
        await using var db = new AtoCopilotContext(_options);
        (await db.AzureSubscriptionRegistrations.CountAsync()).Should().Be(1);
        (await db.Set<SystemEnvironmentAttachmentRecord>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Apply_UndiscoveredOrBroadResource_DoesNotPersistAnything()
    {
        // Arrange
        var tenant = Tenant();
        using var scope = _accessor.Push(tenant);
        var service = Service(tenant);
        var discovery = await service.DiscoverAsync(_system, new(0, Selection), "actor");
        // Act / Assert
        await FluentActions.Awaiting(() => service.ApplyAsync(_system,
            new(0, Selection, discovery.DiscoveryToken, [$"/subscriptions/{_subscription}"], [], [], null),
            "bad", "actor")).Should().ThrowAsync<ArgumentException>();
        (await service.ListAsync(_system)).Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_RegistrationRevoked_BlocksFutureCollectionButRetainsScope()
    {
        // Arrange
        var tenant = Tenant();
        using var scope = _accessor.Push(tenant);
        var service = Service(tenant);
        var discovery = await service.DiscoverAsync(_system, new(0, Selection), "actor");
        await service.ApplyAsync(_system, new(0, Selection, discovery.DiscoveryToken, [Resource], [], [], null), "key", "actor");
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.AzureSubscriptionRegistrations.SingleAsync()).Status = SubscriptionStatus.Unavailable;
            await db.SaveChangesAsync();
        }
        // Act
        var result = await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment);
        // Assert
        result.Sources.Should().ContainSingle();
        result.Sources[0].Eligible.Should().BeFalse();
        result.Sources[0].ResourceIds.Should().Equal(Resource);
    }

    [Fact]
    public async Task ManagePermission_IsIndependentOfAssessmentRun()
    {
        // Arrange
        var tenant = Tenant();
        using var scope = _accessor.Push(tenant);
        // Act
        var choices = await Service(tenant).ChoicesAsync(_system);
        // Assert
        choices.Permissions.CanManageEnvironments.Should().BeTrue();
        choices.Permissions.CanRunAssessments.Should().BeFalse();
        choices.Permissions.CanRegisterSubscriptions.Should().BeFalse();
        choices.RegistrationHref.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_DiscoveryBoundToActor_RejectsOtherActor()
    {
        // Arrange
        var tenant = Tenant();
        using var scope = _accessor.Push(tenant);
        var service = Service(tenant);
        var discovery = await service.DiscoverAsync(_system, new(0, Selection), "actor");
        // Act / Assert
        await FluentActions.Awaiting(() => service.ApplyAsync(_system,
            new(0, Selection, discovery.DiscoveryToken, [Resource], [], [], null), "key", "other"))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Read_CrossTenantSystem_IsNotFound()
    {
        // Arrange
        var tenant = new TenantContext(Guid.NewGuid()) { PersonId = Guid.NewGuid(), IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        // Act / Assert
        await FluentActions.Awaiting(() => Service(tenant).ListAsync(_system)).Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Apply_ConflictingReplayOrStaleVersion_DoesNotChangeScope()
    {
        // Arrange
        var tenant = Tenant();
        using var scope = _accessor.Push(tenant);
        var service = Service(tenant);
        var discovery = await service.DiscoverAsync(_system, new(0, Selection), "actor");
        var request = new ApplySystemEnvironmentRequest(0, Selection, discovery.DiscoveryToken, [Resource], [], [], null);
        await service.ApplyAsync(_system, request, "key", "actor");
        // Act / Assert
        await FluentActions.Awaiting(() => service.ApplyAsync(_system, request with { ExpectedVersion = 1 }, "key", "actor"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        await FluentActions.Awaiting(() => service.ApplyAsync(_system, request, "another", "actor"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await service.ListAsync(_system)).Attachments.Single().Scope.ResourceIds.Should().Equal(Resource);
    }

    [Fact]
    public async Task Detach_RequiresImpactRationaleAndAcknowledgment_RetainsHistoryAndRevokes()
    {
        // Arrange
        var tenant = Tenant();
        using var scope = _accessor.Push(tenant);
        var service = Service(tenant);
        var discovery = await service.DiscoverAsync(_system, new(0, Selection), "actor");
        var attached = await service.ApplyAsync(_system, new(0, Selection, discovery.DiscoveryToken, [Resource], [], [], null), "key", "actor");
        var attachment = attached.Attachments.Single();
        var preview = await service.PreviewDetachAsync(_system, attachment.AttachmentId, new(1, 1, "Service retired"), "actor");
        // Act / Assert
        await FluentActions.Awaiting(() => service.DetachAsync(_system, attachment.AttachmentId,
            new(1, preview.PreviewId, "Service retired", false), "detach", "actor")).Should().ThrowAsync<ArgumentException>();
        var result = await service.DetachAsync(_system, attachment.AttachmentId, new(1, preview.PreviewId, "Service retired", true), "detach", "actor");
        result.Attachments.Single().AttachmentState.Should().Be("Detached");
        (await service.ResolveAsync(_system, EnvironmentScopePurpose.Monitoring)).Sources.Single().Eligible.Should().BeFalse();
        await using var db = new AtoCopilotContext(_options);
        (await db.Set<SystemEnvironmentAttachmentRecord>().SingleAsync()).HistoryJson.Should().Contain("Service retired");
    }

    [Fact]
    public async Task Discovery_FailureAndNonManager_DoNotProduceSuccessfulEmptyScope()
    {
        // Arrange
        var tenant = Tenant();
        using var scope = _accessor.Push(tenant);
        _azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EnvironmentSourceUnavailableException("Synthetic unavailable", new HttpRequestException()));
        // Act / Assert
        await FluentActions.Awaiting(() => Service(tenant).DiscoverAsync(_system, new(0, Selection), "actor"))
            .Should().ThrowAsync<EnvironmentSourceUnavailableException>();
        _access.Setup(x => x.GetAccessAsync(_tenant, _person, _system, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemWorkspaceAccessResponse(_system, ["Assessor"], new(true, false, false, false, false, false, true, false, false)));
        await FluentActions.Awaiting(() => Service(tenant).DiscoverAsync(_system, new(0, Selection), "actor"))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private sealed record ProviderFixture(Guid ProviderId, Guid OwnerId, Guid OfferingId, Guid RegistrationId,
        Guid SubscriptionId, Guid HostingId);
    private async Task<ProviderFixture> SeedProviderAsync()
    {
        var f = new ProviderFixture(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var db = new AtoCopilotContext(_options);
        db.CspProfiles.Add(new() { Id = f.ProviderId, DisplayName = "Synthetic provider", OnboardingState = OnboardingState.Active });
        db.Tenants.Add(new() { Id = f.OwnerId, DisplayName = "Provider owner", OnboardingState = OnboardingState.Active });
        (await db.Tenants.SingleAsync(x => x.Id == _tenant)).OnboardingState = OnboardingState.Active;
        db.AzureSubscriptionRegistrations.Add(new() { Id = f.RegistrationId, TenantId = f.OwnerId,
            SubscriptionId = f.SubscriptionId, ParentTenantId = _directory, DisplayName = "Provider subscription" });
        db.Add(new ProviderOffering { Id = f.OfferingId, OfferingId = f.OfferingId, ProviderId = f.ProviderId,
            Name = "Released technical offering", Lifecycle = "Active", EnvironmentsJson = "[\"AzureCloud\"]", CurrentHostingScopeRevisionId = f.HostingId });
        db.Add(new ProviderHostingScopeRevision { Id = f.HostingId, OfferingId = f.OfferingId, ProviderId = f.ProviderId,
            SnapshotJson = ProviderAuthorizationStore.Json(new CreateProviderHostingScopeRequest(1, null, "Released scope",
                [new ProviderAzureScope("AzureCloud", _directory, f.SubscriptionId, $"/subscriptions/{f.SubscriptionId}/resourceGroups/rg")], [], [])) });
        await db.SaveChangesAsync();
        return f;
    }
    private ProviderEnvironmentAllocationService ProviderService(TenantContext tenant)
    {
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(_options, _accessor));
        return new(new(factory.Object, tenant, NullLogger<ProviderAuthorizationStore>.Instance));
    }
    private RecordProviderAllocationRequest AllocationRequest(ProviderFixture f) =>
        new(1, f.RegistrationId, _tenant, f.HostingId, [$"/subscriptions/{f.SubscriptionId}/resourceGroups/rg"],
            DateTimeOffset.UtcNow.AddMinutes(-1), null, new("ProviderRecorded", null, null, "Verified", null, DateTimeOffset.UtcNow));

    [Fact]
    public async Task ProviderAllocation_UsesActualOwnedIdentity_WithdrawalRevokesConsumerAndRetainsHistory()
    {
        // Arrange
        var f = await SeedProviderAsync();
        var provider = new TenantContext(f.OwnerId) { IsCspAdmin = true };
        ProviderEnvironmentAllocation allocation;
        using (_accessor.Push(provider))
        {
            var service = ProviderService(provider);
            var request = AllocationRequest(f) with { ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) };
            allocation = await service.RecordAsync(f.OfferingId, request, "allocation-key", "provider");
            (await service.RecordAsync(f.OfferingId, request, "allocation-key", "provider")).Should().BeEquivalentTo(allocation);
            await FluentActions.Awaiting(() => service.RecordAsync(f.OfferingId, request with { RegistrationId = _registration },
                "other-owner", "provider")).Should().ThrowAsync<ArgumentException>();
        }
        var resource = $"/subscriptions/{f.SubscriptionId}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/mission";
        _azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EnvironmentResource(resource, "mission", "microsoft.compute/virtualmachines", "rg", null)]);
        var consumer = Tenant();
        using (_accessor.Push(consumer))
        {
            var selection = new EnvironmentSourceSelection("ProviderAllocation", f.RegistrationId, allocation.AllocationId, allocation.Version);
            var service = Service(consumer);
            var discovery = await service.DiscoverAsync(_system, new(0, selection), "actor");
            var response = await service.ApplyAsync(_system, new(0, selection, discovery.DiscoveryToken, [resource], [], [], null), "apply", "actor");
            response.Attachments.Single().HostingAssignmentId.Should().BeNull();
            response.Attachments.Single().AllocationState.Should().Be("Active");
            response.Attachments.Single().AllocationStartsAt.Should().Be(allocation.StartsAt);
            response.Attachments.Single().AllocationExpiresAt.Should().Be(allocation.ExpiresAt);
        }
        // Act
        using (_accessor.Push(provider))
        {
            var service = ProviderService(provider);
            var preview = await service.PreviewChangeAsync(f.OfferingId, allocation.AllocationId, new(1, "Withdraw", null, "Entitlement ended"), "provider");
            preview.Systems.Should().ContainSingle();
            await service.CommitChangeAsync(f.OfferingId, allocation.AllocationId, new(1, preview.PreviewId, "Entitlement ended", true), "withdraw", "provider");
        }
        // Assert
        using (_accessor.Push(consumer))
        {
            var scope = await Service(consumer).ResolveAsync(_system, EnvironmentScopePurpose.Assessment);
            scope.Sources.Single().Eligible.Should().BeFalse();
            scope.Sources.Single().IneligibleReason.Should().Contain("Withdrawn");
            scope.Sources.Single().ResourceIds.Should().Equal(resource);
            var retained = (await Service(consumer).ListAsync(_system)).Attachments.Single();
            retained.AllocationState.Should().Be("Withdrawn");
            retained.AllocationStartsAt.Should().Be(allocation.StartsAt);
            retained.AllocationExpiresAt.Should().Be(allocation.ExpiresAt);
        }
        await using var verify = new AtoCopilotContext(_options);
        (await verify.AzureSubscriptionRegistrations.CountAsync()).Should().Be(2);
        (await verify.Set<MissionProviderRelationshipReview>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProviderAllocation_RejectsDraftScopeAndUnverifiedExternalProvenance()
    {
        // Arrange
        var f = await SeedProviderAsync();
        var provider = new TenantContext(f.OwnerId) { IsCspAdmin = true };
        using var scope = _accessor.Push(provider);
        var service = ProviderService(provider);
        // Act / Assert
        await FluentActions.Awaiting(() => service.RecordAsync(f.OfferingId, AllocationRequest(f) with
        { Provenance = new("FAST", "source-id", "revision-1", "Verified", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow) },
            "bad-provenance", "provider")).Should().ThrowAsync<ArgumentException>();
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.Set<ProviderOffering>().SingleAsync()).Lifecycle = "Draft";
            await db.SaveChangesAsync();
        }
        await FluentActions.Awaiting(() => service.RecordAsync(f.OfferingId, AllocationRequest(f), "draft", "provider"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task MixedThreeSubscriptions_SharedAllocationAcrossSystems_KeepsDistinctExactSelections()
    {
        // Arrange
        var f = await SeedProviderAsync();
        var provider = new TenantContext(f.OwnerId) { IsCspAdmin = true };
        ProviderEnvironmentAllocation allocation;
        using (_accessor.Push(provider))
            allocation = await ProviderService(provider).RecordAsync(f.OfferingId, AllocationRequest(f), "allocation", "provider");
        var thirdId = Guid.NewGuid();
        var thirdSubscription = Guid.NewGuid();
        var otherSystem = Guid.NewGuid().ToString();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.AzureSubscriptionRegistrations.Add(new() { Id = thirdId, TenantId = _tenant, SubscriptionId = thirdSubscription,
                ParentTenantId = _directory, DisplayName = "Third subscription" });
            db.RegisteredSystems.Add(new() { Id = otherSystem, TenantId = _tenant, Name = "Other system" });
            db.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = otherSystem,
                PersonId = _person, Role = OrganizationRole.SystemOwner });
            await db.SaveChangesAsync();
        }
        _access.Setup(x => x.GetAccessAsync(_tenant, _person, otherSystem, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemWorkspaceAccessResponse(otherSystem, ["SystemOwner"], new(true, true, true, false, false, false, false, false, false)));
        _azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EnvironmentRegistration identity, IReadOnlyList<string> _, CancellationToken _) =>
                new[] { "one", "two", "future-unselected" }.Select(name => new EnvironmentResource(
                    $"/subscriptions/{identity.SubscriptionId}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/{name}",
                    name, "microsoft.compute/virtualmachines", "rg", null)).ToArray());
        var consumer = Tenant();
        using var context = _accessor.Push(consumer);
        var service = Service(consumer);
        var selections = new[] { Selection, new EnvironmentSourceSelection("ProviderAllocation", f.RegistrationId, allocation.AllocationId, 1),
            new EnvironmentSourceSelection("OrganizationOwned", thirdId, null, null) };
        // Act
        for (var i = 0; i < selections.Length; i++)
        {
            var discovery = await service.DiscoverAsync(_system, new(i, selections[i]), "actor");
            await service.ApplyAsync(_system, new(i, selections[i], discovery.DiscoveryToken,
                [discovery.Resources[0].ResourceId], [], [], null), $"apply-{i}", "actor");
        }
        var secondDiscovery = await service.DiscoverAsync(otherSystem, new(0, selections[1]), "actor");
        await service.ApplyAsync(otherSystem, new(0, selections[1], secondDiscovery.DiscoveryToken,
            [secondDiscovery.Resources[1].ResourceId], [], [], null), "apply-second", "actor");
        // Assert
        var first = await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment);
        var second = await service.ResolveAsync(otherSystem, EnvironmentScopePurpose.Monitoring);
        first.Sources.Should().HaveCount(3).And.OnlyContain(x => x.Eligible && x.ResourceIds.Count == 1);
        first.Sources.Single(x => x.AllocationId != null).ResourceIds.Should().NotEqual(second.Sources.Single().ResourceIds);
        first.Sources.SelectMany(x => x.ResourceIds).Should().NotContain(x => x.EndsWith("future-unselected"));
        await using var verify = new AtoCopilotContext(_options);
        (await verify.AzureSubscriptionRegistrations.CountAsync()).Should().Be(3);
        (await verify.Set<ProviderEnvironmentAllocationRecord>().CountAsync()).Should().Be(1);
        (await verify.Set<ProviderHostingAssignment>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ScopeChange_OnExistingBoundary_StagesReviewWithoutChangingBoundary()
    {
        // Arrange
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var discovery = await service.DiscoverAsync(_system, new(0, Selection), "actor");
        var initial = await service.ApplyAsync(_system, new(0, Selection, discovery.DiscoveryToken, [Resource], [], [], null), "apply", "actor");
        string boundaryId;
        await using (var db = new AtoCopilotContext(_options))
        {
            var boundary = new AuthorizationBoundaryDefinition { TenantId = _tenant, RegisteredSystemId = _system, Name = "Reviewed boundary" };
            var component = new SystemComponent { TenantId = _tenant, RegisteredSystemId = _system, Name = "Existing reviewed component" };
            db.AuthorizationBoundaryDefinitions.Add(boundary);
            db.SystemComponents.Add(component);
            db.BoundaryComponentAssignments.Add(new() { TenantId = _tenant,
                AuthorizationBoundaryDefinitionId = boundary.Id, SystemComponentId = component.Id, CreatedBy = "reviewer" });
            await db.SaveChangesAsync();
            boundaryId = boundary.Id;
        }
        var updatedDiscovery = await service.DiscoverAsync(_system, new(1, Selection), "actor");
        var attachment = initial.Attachments.Single();
        var preview = await service.PreviewScopeAsync(_system, attachment.AttachmentId,
            new(1, 1, updatedDiscovery.DiscoveryToken, [Resource], [], [], "Reconcile reviewed baseline"), "actor");
        // Act
        var result = await service.CommitScopeAsync(_system, attachment.AttachmentId,
            new(1, preview.PreviewId, "Reconcile reviewed baseline", true), "commit", "actor");
        // Assert
        preview.RequiresScopeReview.Should().BeTrue();
        result.Attachments.Single().Scope.ReviewState.Should().Be("PendingReview");
        await using var verify = new AtoCopilotContext(_options);
        (await verify.BoundaryComponentAssignments.SingleAsync()).AuthorizationBoundaryDefinitionId.Should().Be(boundaryId);
        (await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment)).Sources.Single().Eligible.Should().BeFalse();
    }

    [Fact]
    public async Task AdditiveSchema_IsRepeatableAndPreservesCanonicalRows()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE ProviderEnvironmentAllocationPreviews;
            DROP TABLE ProviderEnvironmentAllocationRecords;
            DROP TABLE SystemEnvironmentReplays;
            DROP TABLE SystemEnvironmentPendingOperations;
            DROP TABLE SystemEnvironmentAttachmentRecords;
            DROP TABLE SystemEnvironmentWorkspaces;
            """);
        // Act
        await Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions.SystemEnvironmentSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions.SystemEnvironmentSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        (await db.AzureSubscriptionRegistrations.SingleAsync()).Id.Should().Be(_registration);
        (await db.Set<SystemEnvironmentAttachmentRecord>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProviderApply_InvalidReuse_RollsBackAllLinks()
    {
        // Arrange
        var f = await SeedProviderAsync();
        var provider = new TenantContext(f.OwnerId) { IsCspAdmin = true };
        ProviderEnvironmentAllocation allocation;
        using (_accessor.Push(provider))
            allocation = await ProviderService(provider).RecordAsync(f.OfferingId, AllocationRequest(f), "allocation", "provider");
        var resource = $"/subscriptions/{f.SubscriptionId}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/mission";
        _azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EnvironmentResource(resource, "mission", "microsoft.compute/virtualmachines", "rg", null)]);
        var consumer = Tenant();
        using var context = _accessor.Push(consumer);
        var service = Service(consumer);
        var selection = new EnvironmentSourceSelection("ProviderAllocation", f.RegistrationId, allocation.AllocationId, 1);
        var discovery = await service.DiscoverAsync(_system, new(0, selection), "actor");
        // Act / Assert
        await FluentActions.Awaiting(() => service.ApplyAsync(_system,
            new(0, selection, discovery.DiscoveryToken, [resource], [], [], Guid.NewGuid()), "invalid", "actor"))
            .Should().ThrowAsync<ArgumentException>();
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<SystemEnvironmentAttachmentRecord>().CountAsync()).Should().Be(0);
        (await verify.Set<ProviderHostingAssignment>().CountAsync()).Should().Be(0);
        (await verify.Set<MissionProviderRelationshipReview>().CountAsync()).Should().Be(0);
        (await verify.Set<SystemEnvironmentReplay>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProviderAllocation_ExpiredOrWrongCloud_IsNeverAnEligibleChoice()
    {
        // Arrange
        var f = await SeedProviderAsync();
        var provider = new TenantContext(f.OwnerId) { IsCspAdmin = true };
        using (_accessor.Push(provider))
        {
            await ProviderService(provider).RecordAsync(f.OfferingId, AllocationRequest(f) with {
                StartsAt = DateTimeOffset.UtcNow.AddDays(-2), ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
            }, "expired", "provider");
            await using var db = new AtoCopilotContext(_options);
            (await db.AzureSubscriptionRegistrations.SingleAsync(x => x.Id == f.RegistrationId)).Environment = AzureEnvironment.AzureUSGovernment;
            await db.SaveChangesAsync();
        }
        var consumer = Tenant();
        using var context = _accessor.Push(consumer);
        // Act
        var choices = await Service(consumer).ChoicesAsync(_system);
        // Assert
        choices.Choices.Single(x => x.Source == "ProviderAllocation").Eligible.Should().BeFalse();
        choices.Choices.Single(x => x.Source == "ProviderAllocation").AllocationState.Should().Be("Expired");
    }

    [Fact]
    public async Task ProviderAllocation_OrdinaryConsumerCannotAllocate()
    {
        // Arrange
        var f = await SeedProviderAsync();
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        // Act / Assert
        await FluentActions.Awaiting(() => ProviderService(tenant).RecordAsync(f.OfferingId, AllocationRequest(f), "denied", "actor"))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task AccessCheck_ConcurrentRegistrationRevocation_CannotRestoreAvailableState()
    {
        // Arrange
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var discovery = await service.DiscoverAsync(_system, new(0, Selection), "actor");
        await service.ApplyAsync(_system, new(0, Selection, discovery.DiscoveryToken, [Resource], [], [], null), "apply", "actor");
        _azure.Setup(x => x.CheckAccessAsync(It.IsAny<ResolvedSystemEnvironmentScope>(), EnvironmentScopePurpose.Assessment, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await using var db = new AtoCopilotContext(_options);
                (await db.AzureSubscriptionRegistrations.SingleAsync()).Status = SubscriptionStatus.Unavailable;
                await db.SaveChangesAsync();
                return new EnvironmentCheckState("Available", DateTimeOffset.UtcNow, null);
            });
        // Act
        var result = await service.CheckAccessAsync(_system, new(1, "Assessment"), "actor");
        // Assert
        result.Attachments.Single().AssessmentAccess.State.Should().Be("Blocked");
        result.Attachments.Single().Monitoring.Health.Should().NotBe("Healthy");
    }

    [Fact]
    public async Task AccessCheck_RetainsSuccessfulDeniedAndSkippedServicesWithoutClaimingCollectionHealth()
    {
        // Arrange
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var discovery = await service.DiscoverAsync(_system, new(0, Selection), "actor");
        await service.ApplyAsync(_system, new(0, Selection, discovery.DiscoveryToken, [Resource], [], [], null), "apply", "actor");
        var attempted = DateTimeOffset.UtcNow;
        var checks = new EnvironmentSourceCheck[] {
            new("arm", "Access", "Available", true, attempted, attempted, null, null, "scope-1", null),
            new("policy", "Access", "Denied", true, attempted, null, "Synthetic denial", "AZURE_ACCESS_DENIED", "scope-1", null),
            new("defender", "Access", "NotChecked", true, null, null, "Skipped after denial", "SOURCE_NOT_CHECKED", "scope-1", null)
        };
        _azure.Setup(x => x.CheckAccessAsync(It.IsAny<ResolvedSystemEnvironmentScope>(), EnvironmentScopePurpose.Assessment, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnvironmentCheckState("Denied", attempted, "Required policy source denied.") { Sources = checks });
        // Act
        await service.CheckAccessAsync(_system, new(1, "Assessment"), "actor");
        var result = await service.ListAsync(_system);
        // Assert
        var attachment = result.Attachments.Single();
        attachment.AssessmentAccess.State.Should().Be("Denied");
        attachment.AssessmentAccess.Sources.Should().BeEquivalentTo(checks);
        attachment.AssessmentAccess.Sources.Single(x => x.SourceId == "defender").AttemptedAt.Should().BeNull();
        attachment.Monitoring.Sources.Should().OnlyContain(x => x.Kind == "Collection" && x.State == "Unsupported");
        attachment.Monitoring.Health.Should().Be("NotEvaluated");
    }

    [Fact]
    public async Task Workspace_ReadinessSeparatesEntitlementFromUnsupportedCollectionAndOldEnabledRule()
    {
        // Arrange
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var discovery = await service.DiscoverAsync(_system, new(0, Selection), "actor");
        await service.ApplyAsync(_system, new(0, Selection, discovery.DiscoveryToken, [Resource], [], [], null), "apply", "actor");
        await using (var db = new AtoCopilotContext(_options))
        {
            db.AlertRules.Add(new() { Id = Guid.NewGuid(), TenantId = _tenant, RegisteredSystemId = _system,
                Name = "Retained pre-scope collector configuration", IsEnabled = true, LastEvaluatedAt = DateTimeOffset.UtcNow,
                ReviewedScopeJson = ProviderAuthorizationStore.Json(new[] { new MonitoringScopeResource("assignment", "boundary", null, Resource, null) }) });
            await db.SaveChangesAsync();
        }
        // Act
        var entitlement = await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment);
        var attachment = (await service.ListAsync(_system)).Attachments.Single();
        // Assert
        entitlement.Sources.Single().Eligible.Should().BeTrue();
        attachment.Readiness.State.Should().Be("Blocked");
        attachment.Readiness.Sources.Should().OnlyContain(x => x.State == "Unsupported" && x.Kind == "Collection");
        attachment.Readiness.Reason.Should().Contain("not yet supported");
        attachment.Monitoring.Configured.Should().BeTrue();
        attachment.Monitoring.Enabled.Should().BeFalse();
        attachment.Monitoring.EvaluatedAt.Should().BeNull();
        attachment.Monitoring.Health.Should().Be("Unavailable");
        await using var verify = new AtoCopilotContext(_options);
        (await verify.AlertRules.SingleAsync()).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ProviderDisplays_UseRecordedNamesAndExactHostingRevision_NotIdentifiers()
    {
        // Arrange
        var f = await SeedProviderAsync();
        var provider = new TenantContext(f.OwnerId) { IsCspAdmin = true };
        ProviderEnvironmentAllocation allocation;
        using (_accessor.Push(provider))
            allocation = await ProviderService(provider).RecordAsync(f.OfferingId, AllocationRequest(f), "allocation", "provider");
        var resource = $"/subscriptions/{f.SubscriptionId}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/mission";
        _azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EnvironmentResource(resource, "mission", "microsoft.compute/virtualmachines", "rg", null)]);
        var consumer = Tenant();
        using var context = _accessor.Push(consumer);
        var service = Service(consumer);
        // Act
        var choice = (await service.ChoicesAsync(_system)).Choices.Single(x => x.AllocationId == allocation.AllocationId);
        var selection = new EnvironmentSourceSelection("ProviderAllocation", f.RegistrationId, allocation.AllocationId, allocation.Version);
        var discovery = await service.DiscoverAsync(_system, new(0, selection), "actor");
        var attached = await service.ApplyAsync(_system, new(0, selection, discovery.DiscoveryToken, [resource], [], [], null), "attach", "actor");
        // Assert
        allocation.ProviderName.Should().Be("Synthetic provider");
        allocation.HostingScopeName.Should().Be("Released scope");
        choice.ProviderName.Should().Be("Synthetic provider");
        choice.ConsumerName.Should().Be("Synthetic organization");
        choice.HostingScopeName.Should().Be("Released scope");
        var attachment = attached.Attachments.Single();
        attachment.ProviderName.Should().Be(choice.ProviderName);
        attachment.ConsumerName.Should().Be(choice.ConsumerName);
        attachment.HostingScopeName.Should().Be(choice.HostingScopeName);
        attachment.HostingScopeRevisionId.Should().Be(f.HostingId);
        attachment.OfferingName.Should().Be("Released technical offering");
        attachment.Registration.DisplayName.Should().Be("Provider subscription");
    }

    [Fact]
    public async Task PendingScope_ExplicitReviewCommitsImmutableReviewedRevisionWithoutChangingBoundary()
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            var boundary = new AuthorizationBoundaryDefinition { TenantId = _tenant, RegisteredSystemId = _system,
                Name = "Recorded boundary to preserve", Description = "Previously recorded description" };
            var component = new SystemComponent { TenantId = _tenant, RegisteredSystemId = _system, Name = "Recorded component" };
            db.AuthorizationBoundaryDefinitions.Add(boundary);
            db.SystemComponents.Add(component);
            db.BoundaryComponentAssignments.Add(new() { TenantId = _tenant, AuthorizationBoundaryDefinitionId = boundary.Id,
                SystemComponentId = component.Id, CreatedBy = "previous-reviewer" });
            await db.SaveChangesAsync();
        }
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var discovery = await service.DiscoverAsync(_system, new(0, Selection), "actor");
        var applied = await service.ApplyAsync(_system, new(0, Selection, discovery.DiscoveryToken, [Resource], [], [], null), "apply", "actor");
        var pending = applied.Attachments.Single();
        pending.Scope.ReviewState.Should().Be("PendingReview");
        (await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment)).Sources.Single().Eligible.Should().BeFalse();
        var rediscovered = await service.DiscoverAsync(_system, new(1, Selection), "reviewer");
        var review = new EnvironmentScopeChangeRequest(1, 1, rediscovered.DiscoveryToken, [Resource], [], [],
            "Reviewed the exact current allocation resources; boundary documentation is unchanged.") { ReviewPendingScope = true };
        // Act
        await FluentActions.Awaiting(() => service.PreviewScopeAsync(_system, pending.AttachmentId,
            review with { SharedDependencyResourceIds = [Resource] }, "reviewer"))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*exact pending*");
        var preview = await service.PreviewScopeAsync(_system, pending.AttachmentId, review, "reviewer");
        var commit = new CommitEnvironmentChangeRequest(1, preview.PreviewId, review.Rationale, true);
        await FluentActions.Awaiting(() => service.CommitScopeAsync(_system, pending.AttachmentId,
            commit with { AcknowledgeImpact = false }, "not-acknowledged", "reviewer"))
            .Should().ThrowAsync<ArgumentException>();
        var accepted = await service.CommitScopeAsync(_system, pending.AttachmentId, commit, "review-key", "reviewer");
        var replay = await service.CommitScopeAsync(_system, pending.AttachmentId, commit, "review-key", "reviewer");
        // Assert
        preview.RequiresScopeReview.Should().BeFalse();
        var scope = accepted.Attachments.Single().Scope;
        scope.ReviewState.Should().Be("Reviewed");
        scope.RevisionId.Should().NotBe(pending.Scope.RevisionId);
        scope.Version.Should().Be(2);
        scope.ReviewedBy.Should().Be("reviewer");
        scope.ReviewedAt.Should().NotBeNull();
        replay.Should().BeEquivalentTo(accepted);
        (await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment)).Sources.Single().Eligible.Should().BeTrue();
        accepted.Attachments.Single().Readiness.State.Should().Be("Blocked", "collector scope support is a different admission gate");
        await using var verify = new AtoCopilotContext(_options);
        (await verify.AuthorizationBoundaryDefinitions.SingleAsync()).Description.Should().Be("Previously recorded description");
        (await verify.SystemComponents.SingleAsync()).AzureResourceId.Should().BeNull();
        (await verify.BoundaryComponentAssignments.SingleAsync()).CreatedBy.Should().Be("previous-reviewer");
        (await verify.Set<SystemEnvironmentAttachmentRecord>().SingleAsync()).HistoryJson.Should().Contain("ScopeReviewed");
    }

    [Fact]
    public async Task ProviderScopeChange_DoesNotCreateOrRequireHostingAssociation()
    {
        // Arrange
        var f = await SeedProviderAsync();
        var provider = new TenantContext(f.OwnerId) { IsCspAdmin = true };
        ProviderEnvironmentAllocation allocation;
        using (_accessor.Push(provider))
            allocation = await ProviderService(provider).RecordAsync(f.OfferingId, AllocationRequest(f), "allocation", "provider");
        var resources = new[] { "one", "two" }.Select(name => new EnvironmentResource(
            $"/subscriptions/{f.SubscriptionId}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/{name}",
            name, "microsoft.compute/virtualmachines", "rg", null)).ToArray();
        _azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resources);
        var consumer = Tenant();
        using var context = _accessor.Push(consumer);
        var service = Service(consumer);
        var selection = new EnvironmentSourceSelection("ProviderAllocation", f.RegistrationId, allocation.AllocationId, 1);
        var discovery = await service.DiscoverAsync(_system, new(0, selection), "actor");
        var original = (await service.ApplyAsync(_system, new(0, selection, discovery.DiscoveryToken,
            [resources[0].ResourceId], [], [], null), "apply", "actor")).Attachments.Single();
        var next = await service.DiscoverAsync(_system, new(1, selection), "actor");
        var preview = await service.PreviewScopeAsync(_system, original.AttachmentId,
            new(1, 1, next.DiscoveryToken, [resources[1].ResourceId], [], [], "Change exact resources"), "actor");
        // Act
        var updated = (await service.CommitScopeAsync(_system, original.AttachmentId,
            new(1, preview.PreviewId, "Change exact resources", true), "scope-change", "actor")).Attachments.Single();
        // Assert
        updated.HostingAssignmentId.Should().BeNull();
        updated.Scope.ResourceIds.Should().Equal(resources[1].ResourceId);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<ProviderHostingAssignment>().CountAsync()).Should().Be(0);
        (await verify.Set<ProviderEnvironmentAllocationRecord>().CountAsync()).Should().Be(1);
    }
}

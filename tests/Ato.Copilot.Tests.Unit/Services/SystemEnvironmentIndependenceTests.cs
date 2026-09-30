using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class SystemEnvironmentServiceTests
{
    [Fact]
    public async Task IndependentProviderChoices_InspectExactPublishedDutiesBeforeAnySystemAssignment()
    {
        // Arrange
        var f = await PublishedServiceAsync();
        Guid releaseId;
        await using (var db = new AtoCopilotContext(_options))
        {
            var release = await db.ProviderCapabilityReleases.SingleAsync();
            releaseId = release.Id;
            release.SnapshotJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                DutiesJson = """{"AC-2":"Customer","AU-2":"Shared","SC-7":"Provider"}""",
                Capability = new { Name = "Exact released capability", Description = "Source-stated operational description." }
            });
            release.SnapshotHash = "retained-release-hash";
            (await db.CspInheritedCapabilities.SingleAsync()).Name = "Changed working name not the reviewed release";
            await db.SaveChangesAsync();
        }
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        // Act
        var choice = (await Service(tenant).ProviderScopeChoicesAsync(_system)).Choices.Single();
        // Assert
        choice.PublishedDuties.State.Should().Be("Available");
        var duties = choice.PublishedDuties.Capabilities.Single();
        duties.ReleaseId.Should().Be(releaseId);
        duties.ReleaseRevision.Should().Be(1);
        duties.CapabilityName.Should().Be("Exact released capability");
        duties.Description.Should().Be("Source-stated operational description.");
        duties.CustomerControlIds.Should().Equal("AC-2");
        duties.SharedControlIds.Should().Equal("AU-2");
        duties.ProviderControlIds.Should().Equal("SC-7");
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<ProviderHostingAssignment>().CountAsync()).Should().Be(0);
        (await verify.CapabilitySubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task IndependentProviderDuties_MissingContentAndUnadoptedResponsibilitiesAreNotFabricated()
    {
        // Arrange
        var f = await PublishedServiceAsync();
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        // Act
        var choice = (await service.ProviderScopeChoicesAsync(_system)).Choices.Single();
        var workspace = await service.AddProviderScopeAsync(_system, new(0, f.OfferingId, 1, f.HostingId), "scope", "actor");
        // Assert
        choice.PublishedDuties.State.Should().Be("Unavailable");
        choice.PublishedDuties.Capabilities.Should().BeEmpty();
        var review = workspace.ProviderScopes.Single().ResponsibilityReview;
        review.State.Should().Be("NotAdopted");
        review.CanReview.Should().BeTrue();
        review.CanConfirm.Should().BeFalse();
        review.Reason.Should().Contain("adoption");
    }

    [Fact]
    public async Task IndependentProviderDuties_DoNotBorrowFromAnotherHostingRevisionOrWorkingRecord()
    {
        // Arrange
        var f = await PublishedServiceAsync();
        await using (var db = new AtoCopilotContext(_options))
        {
            var release = await db.ProviderCapabilityReleases.SingleAsync();
            release.SnapshotJson = """{"DutiesJson":"{\"AC-2\":\"Customer\"}","Capability":{"Name":"Wrong released scope","Description":"Do not disclose as selected scope duties"}}""";
            var context = await db.Set<ProviderCatalogContextSnapshot>().SingleAsync();
            var material = ProviderAuthorizationStore.Read<ProviderPublicationContextMaterial>(context.SnapshotJson);
            context.SnapshotJson = ProviderAuthorizationStore.Json(material with { HostingScopeRevisionId = Guid.NewGuid() });
            context.SnapshotHash = ProviderAuthorizationStore.Hash(context.SnapshotJson);
            (await db.Set<ProviderAuthorizationImpactReview>().SingleAsync()).ContextSnapshotHash = context.SnapshotHash;
            db.Add(new ProviderHostingAssignment { TargetTenantId = _tenant, SystemId = _system,
                ProviderId = f.ProviderId, OfferingId = f.OfferingId, HostingScopeRevisionId = f.HostingId });
            await db.SaveChangesAsync();
        }
        var tenant = Tenant();
        using var pushed = _accessor.Push(tenant);
        // Act
        var choice = (await Service(tenant).ProviderScopeChoicesAsync(_system)).Choices.Single();
        // Assert
        choice.PublishedDuties.State.Should().Be("Unavailable");
        choice.PublishedDuties.Capabilities.Should().BeEmpty();
        choice.EligibilitySource.Should().Be("ExistingSystemAssignment");
    }

    [Theory]
    [InlineData(false, "PendingReview", "ReviewRequired")]
    [InlineData(true, "Ready", "Reviewed")]
    public async Task IndependentRetainedScope_ResponsibilityStateUsesCanonicalReviewPermission(bool canConfirm, string itemState, string expectedState)
    {
        // Arrange
        var f = await PublishedServiceAsync();
        var tenant = Tenant();
        using var pushed = _accessor.Push(tenant);
        var workspace = await Service(tenant).AddProviderScopeAsync(_system, new(0, f.OfferingId, 1, f.HostingId), "add", "actor");
        var assignmentId = workspace.ProviderScopes.Single().AssignmentId;
        string subscriptionId;
        Guid capabilityId;
        await using (var db = new AtoCopilotContext(_options))
        {
            var source = await db.Set<ProviderCatalogContextSnapshot>().SingleAsync();
            capabilityId = source.CapabilityId!.Value;
            var subscription = new CapabilitySubscription { RoutingTenantId = _tenant, RegisteredSystemId = _system,
                CspInheritedCapabilityId = capabilityId.ToString(), IsActive = true };
            db.CapabilitySubscriptions.Add(subscription);
            var adoption = new CapabilityAdoptionSnapshot { TenantId = _tenant, SystemId = _system, ProviderId = f.ProviderId,
                OfferingId = f.OfferingId, AssignmentId = assignmentId, AssignmentRevision = 1, CapabilityId = capabilityId,
                ReleaseId = source.ReleaseId!.Value, ContextSnapshotId = source.Id, SubscriptionId = subscription.Id };
            db.Add(adoption);
            subscription.CurrentAdoptionSnapshotId = adoption.Id;
            await db.SaveChangesAsync();
            subscriptionId = subscription.Id;
        }
        var canonical = new Mock<ICapabilityResponsibilityService>();
        canonical.Setup(x => x.PreviewAsync(_system, It.IsAny<CancellationToken>())).ReturnsAsync(
            new CapabilityResponsibilityResponse(_system, "baseline", "Reviewed baseline", canConfirm,
                [new(subscriptionId, capabilityId, null, f.ProviderId, "Provider", "AC-2", "source-version", "review-version",
                    itemState, null, null, null, null, null, null, true, null, null)], []));
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(_options, _accessor));
        var service = new Ato.Copilot.Core.Services.Environments.SystemEnvironmentService(factory.Object, tenant,
            _access.Object, _azure.Object, canonical.Object);
        // Act
        var state = (await service.ListAsync(_system)).ProviderScopes.Single().ResponsibilityReview;
        // Assert
        state.State.Should().Be(expectedState);
        state.CanConfirm.Should().Be(canConfirm);
        state.CanReview.Should().BeTrue();
        canonical.Verify(x => x.PreviewAsync(_system, It.IsAny<CancellationToken>()), Times.Once);
    }

    private async Task<ProviderFixture> PublishedServiceAsync()
    {
        var f = await SeedProviderAsync();
        await using var db = new AtoCopilotContext(_options);
        var hosting = await db.Set<ProviderHostingScopeRevision>().SingleAsync(x => x.Id == f.HostingId);
        hosting.SnapshotJson = ProviderAuthorizationStore.Json(new CreateProviderHostingScopeRequest(1, null,
            "Published service scope", [new ProviderServiceScope("ManualService", "service", "Documented service", null)], [], []));
        hosting.SnapshotHash = ProviderAuthorizationStore.Hash(hosting.SnapshotJson);
        var component = new CspInheritedComponent { CspProfileId = f.ProviderId, Name = "Published service",
            Status = CspInheritedComponentStatus.Published };
        db.CspInheritedComponents.Add(component);
        var capability = new CspInheritedCapability { CspInheritedComponentId = component.Id, Name = "Published capability",
            Status = CspInheritedCapabilityStatus.Mapped };
        db.CspInheritedCapabilities.Add(capability);
        var release = new ProviderCapabilityRelease { CapabilityId = capability.Id, Revision = 1 };
        db.ProviderCapabilityReleases.Add(release);
        var material = new ProviderPublicationContextMaterial(f.OfferingId, 1, Guid.NewGuid(), "boundary-hash",
            f.HostingId, hosting.SnapshotHash, [], [], []);
        var json = ProviderAuthorizationStore.Json(material);
        var review = new ProviderAuthorizationImpactReview { ProviderId = f.ProviderId, OfferingId = f.OfferingId,
            Disposition = "AcceptForPublication", ReviewedBy = "provider", ReviewedAt = DateTimeOffset.UtcNow,
            ContextSnapshotHash = ProviderAuthorizationStore.Hash(json) };
        db.Add(review);
        db.Add(new ProviderCatalogContextSnapshot { ProviderId = f.ProviderId, OfferingId = f.OfferingId,
            CapabilityId = capability.Id, ReleaseId = release.Id, ImpactReviewId = review.Id,
            SnapshotJson = json, SnapshotHash = ProviderAuthorizationStore.Hash(json) });
        await db.SaveChangesAsync();
        return f;
    }

    [Fact]
    public async Task IndependentProviderScope_NoSubscriptionOrAllocationNeeded_AndNoFalseLegacyWarning()
    {
        // Arrange
        var f = await PublishedServiceAsync();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.AzureSubscriptionRegistrations.RemoveRange(await db.AzureSubscriptionRegistrations.ToListAsync());
            await db.SaveChangesAsync();
        }
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        // Act
        var choices = await service.ProviderScopeChoicesAsync(_system);
        var request = new AddSystemProviderScopeRequest(0, f.OfferingId, 1, f.HostingId);
        var result = await service.AddProviderScopeAsync(_system, request, "scope-only", "actor");
        var replay = await service.AddProviderScopeAsync(_system, request, "scope-only", "actor");
        // Assert
        choices.Choices.Should().ContainSingle();
        choices.Choices[0].ProviderId.Should().Be(f.ProviderId);
        choices.Choices[0].HostingScopeRevision.Should().Be(1);
        result.Attachments.Should().BeEmpty();
        result.ProviderScopes.Should().ContainSingle();
        result.ProviderScopes[0].ProviderId.Should().Be(f.ProviderId);
        result.ProviderScopes[0].HostingScopeRevision.Should().Be(1);
        result.ProviderScopes[0].RelationshipState.Should().Be("Undetermined");
        result.LegacyReferences.Should().BeEmpty();
        result.HostingLinks.Should().BeEmpty();
        replay.Should().BeEquivalentTo(result);
        (await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment)).Version.Should().Be(0,
            "provider-only work is not Azure attachment history");
        await using var verify = new AtoCopilotContext(_options);
        (await verify.AzureSubscriptionRegistrations.CountAsync()).Should().Be(0);
        (await verify.Set<ProviderEnvironmentAllocationRecord>().CountAsync()).Should().Be(0);
        (await verify.Set<MissionProviderRelationshipReview>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task IndependentProviderChoices_DoNotExposePrivateOrForeignAssignedOffering()
    {
        // Arrange
        var f = await SeedProviderAsync();
        await using (var db = new AtoCopilotContext(_options))
        {
            var foreign = Guid.NewGuid();
            db.Tenants.Add(new() { Id = foreign, DisplayName = "Other organization" });
            db.RegisteredSystems.Add(new() { Id = "other-system", TenantId = foreign, Name = "Private system" });
            db.Add(new ProviderHostingAssignment { ProviderId = f.ProviderId, OfferingId = f.OfferingId,
                TargetTenantId = foreign, SystemId = "other-system", HostingScopeRevisionId = f.HostingId });
            await db.SaveChangesAsync();
        }
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        // Act / Assert
        (await service.ProviderScopeChoicesAsync(_system)).Choices.Should().BeEmpty();
        await FluentActions.Awaiting(() => service.AddProviderScopeAsync(_system,
            new(0, f.OfferingId, 1, f.HostingId), "private", "actor")).Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task IndependentProviderAllocation_ApplyCreatesNoHostingOrRelationship()
    {
        // Arrange
        var f = await SeedProviderAsync();
        var provider = new TenantContext(f.OwnerId) { IsCspAdmin = true };
        ProviderEnvironmentAllocation allocation;
        using (_accessor.Push(provider))
            allocation = await ProviderService(provider).RecordAsync(f.OfferingId, AllocationRequest(f), "allocation", "provider");
        var resource = $"/subscriptions/{f.SubscriptionId}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/mission";
        _azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EnvironmentResource(resource, "mission", "vm", "rg", null)]);
        var consumer = Tenant();
        using var context = _accessor.Push(consumer);
        var service = Service(consumer);
        var selection = new EnvironmentSourceSelection("ProviderAllocation", f.RegistrationId, allocation.AllocationId, 1);
        var discovery = await service.DiscoverAsync(_system, new(0, selection), "actor");
        // Act
        var result = await service.ApplyAsync(_system, new(0, selection, discovery.DiscoveryToken, [resource], [], [], null), "apply", "actor");
        // Assert
        result.Attachments.Single().HostingAssignmentId.Should().BeNull();
        result.ProviderScopes.Should().BeEmpty();
        result.HostingLinks.Should().BeEmpty();
        (await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment)).Sources.Single().Eligible.Should().BeTrue();
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<ProviderHostingAssignment>().CountAsync()).Should().Be(0);
        (await verify.Set<MissionProviderRelationshipReview>().CountAsync()).Should().Be(0);
    }

    private async Task<SystemEnvironmentsResponse> LinkAsync(Ato.Copilot.Core.Services.Environments.SystemEnvironmentService service,
        SystemEnvironmentsResponse workspace, Guid attachmentId, Guid assignmentId, string action = "Link")
    {
        var attachment = workspace.Attachments.Single(x => x.AttachmentId == attachmentId);
        var provider = workspace.ProviderScopes.Single(x => x.AssignmentId == assignmentId);
        var preview = await service.PreviewHostingLinkAsync(_system, new(workspace.Version, attachmentId,
            attachment.Version, assignmentId, provider.AssignmentVersion, action, "Explicit documentary link"), "actor");
        return await service.CommitHostingLinkAsync(_system,
            new(workspace.Version, preview.PreviewId, "Explicit documentary link", true), Guid.NewGuid().ToString(), "actor");
    }

    [Fact]
    public async Task IndependentLinks_ManyToMany_UnlinkRemoveAndDetachPreserveOppositeRecordsAndAdmission()
    {
        // Arrange
        var first = await PublishedServiceAsync();
        var second = await PublishedServiceAsync();
        var extra = Guid.NewGuid();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.AzureSubscriptionRegistrations.Add(new() { Id = extra, TenantId = _tenant, SubscriptionId = Guid.NewGuid(),
                ParentTenantId = _directory, DisplayName = "Second owned subscription" });
            await db.SaveChangesAsync();
        }
        _azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EnvironmentRegistration identity, IReadOnlyList<string> _, CancellationToken _) =>
                new[] { new EnvironmentResource($"/subscriptions/{identity.SubscriptionId}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/current", "current", "vm", "rg", null) });
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var workspace = await service.AddProviderScopeAsync(_system, new(0, first.OfferingId, 1, first.HostingId), "first", "actor");
        workspace = await service.AddProviderScopeAsync(_system, new(workspace.Version, second.OfferingId, 1, second.HostingId), "second", "actor");
        var items = new List<ApplySystemEnvironmentRequest>();
        foreach (var id in new[] { _registration, extra })
        {
            var selection = new EnvironmentSourceSelection("OrganizationOwned", id, null, null);
            var discovery = await service.DiscoverAsync(_system, new(workspace.Version, selection), "actor");
            items.Add(new(workspace.Version, selection, discovery.DiscoveryToken, [discovery.Resources[0].ResourceId], [], [], null));
        }
        workspace = await service.ApplyBatchAsync(_system, new(workspace.Version, items), "both", "actor");
        var subscriptionVersion = (await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment)).Version;
        var a = workspace.Attachments[0].AttachmentId;
        var b = workspace.Attachments[1].AttachmentId;
        var x = workspace.ProviderScopes[0].AssignmentId;
        var y = workspace.ProviderScopes[1].AssignmentId;
        // Act
        foreach (var pair in new[] { (a, x), (a, y), (b, x), (b, y) })
            workspace = await LinkAsync(service, workspace, pair.Item1, pair.Item2);
        // Assert
        workspace.HostingLinks.Should().HaveCount(4).And.OnlyContain(l => l.State == "Linked");
        (await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment)).Version.Should().Be(subscriptionVersion);
        workspace.Attachments.Should().OnlyContain(a => a.HostingAssignmentId == null);
        // Act
        workspace = await LinkAsync(service, workspace, a, x, "Unlink");
        var relationship = workspace.ProviderScopes.Single(p => p.AssignmentId == y);
        var preview = await service.PreviewProviderScopeRemovalAsync(_system, y,
            new(workspace.Version, relationship.AssignmentVersion, relationship.SelectionVersion, "Remove only provider relationship"), "actor");
        workspace = await service.RemoveProviderScopeAsync(_system, y,
            new(workspace.Version, preview.PreviewId, "Remove only provider relationship", true), "remove-y", "actor");
        // Assert
        workspace.Attachments.Should().HaveCount(2).And.OnlyContain(a => a.AttachmentState == "Attached");
        workspace.ProviderScopes.Single(p => p.AssignmentId == y).State.Should().Be("Removed");
        workspace.HostingLinks.Count(l => l.State == "Linked").Should().Be(1);
        var admission = await service.ResolveAsync(_system, EnvironmentScopePurpose.Monitoring);
        admission.Sources.Should().OnlyContain(s => s.Eligible);
        admission.Version.Should().Be(subscriptionVersion);
        // Act
        var attachment = workspace.Attachments.Single(t => t.AttachmentId == b);
        preview = await service.PreviewDetachAsync(_system, b, new(workspace.Version, attachment.Version, "Detach only subscription"), "actor");
        workspace = await service.DetachAsync(_system, b,
            new(workspace.Version, preview.PreviewId, "Detach only subscription", true), "detach-b", "actor");
        // Assert
        workspace.ProviderScopes.Single(p => p.AssignmentId == x).State.Should().Be("Active");
        workspace.HostingLinks.Should().OnlyContain(l => l.State == "Unlinked");
        workspace.LegacyReferences.Should().BeEmpty();
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<ProviderHostingAssignment>().CountAsync()).Should().Be(2);
        (await verify.Set<MissionProviderRelationshipReview>().CountAsync()).Should().Be(2);
        (await verify.Set<SystemEnvironmentAttachmentRecord>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task IndependentLink_RequiresActorVersionAcknowledgmentAndExactSystem()
    {
        // Arrange
        var f = await PublishedServiceAsync();
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var workspace = await service.AddProviderScopeAsync(_system, new(0, f.OfferingId, 1, f.HostingId), "scope", "actor");
        var discovery = await service.DiscoverAsync(_system, new(workspace.Version, Selection), "actor");
        workspace = await service.ApplyAsync(_system, new(workspace.Version, Selection, discovery.DiscoveryToken, [Resource], [], [], null), "apply", "actor");
        var scope = workspace.ProviderScopes.Single();
        var attachment = workspace.Attachments.Single();
        var request = new PreviewEnvironmentHostingLinkRequest(workspace.Version, attachment.AttachmentId,
            attachment.Version, scope.AssignmentId, scope.AssignmentVersion, "Link", "Document association");
        // Act / Assert
        await FluentActions.Awaiting(() => service.PreviewHostingLinkAsync(_system, request with { AssignmentId = Guid.NewGuid() }, "actor"))
            .Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Awaiting(() => service.PreviewHostingLinkAsync(_system, request with { ExpectedAttachmentVersion = 0 }, "actor"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        var preview = await service.PreviewHostingLinkAsync(_system, request, "actor");
        var commit = new CommitEnvironmentChangeRequest(workspace.Version, preview.PreviewId, request.Rationale, true);
        await FluentActions.Awaiting(() => service.CommitHostingLinkAsync(_system, commit, "other", "other-actor"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        await FluentActions.Awaiting(() => service.CommitHostingLinkAsync(_system, commit with { AcknowledgeImpact = false }, "no-ack", "actor"))
            .Should().ThrowAsync<ArgumentException>();
        var result = await service.CommitHostingLinkAsync(_system, commit, "link", "actor");
        (await service.CommitHostingLinkAsync(_system, commit, "link", "actor")).Should().BeEquivalentTo(result);
        await FluentActions.Awaiting(() => service.CommitHostingLinkAsync(_system, commit with { Rationale = "Changed intent" }, "link", "actor"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        _access.Setup(x => x.GetAccessAsync(_tenant, _person, _system, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemWorkspaceAccessResponse(_system, ["Assessor"], new(true, false, false, false, false, false, true, false, false)));
        await FluentActions.Awaiting(() => service.PreviewHostingLinkAsync(_system, request with { ExpectedVersion = result.Version }, "actor"))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task IndependentApplyBatch_FailedItemRollsBackAllAndStableReplayPreservesOneResult()
    {
        // Arrange
        var extra = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.AzureSubscriptionRegistrations.Add(new() { Id = extra, TenantId = _tenant, SubscriptionId = subscription,
                ParentTenantId = _directory, DisplayName = "Second" });
            await db.SaveChangesAsync();
        }
        _azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EnvironmentRegistration identity, IReadOnlyList<string> _, CancellationToken _) =>
                new[] { new EnvironmentResource($"/subscriptions/{identity.SubscriptionId}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/current", "current", "vm", "rg", null) });
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var items = new List<ApplySystemEnvironmentRequest>();
        foreach (var id in new[] { _registration, extra })
        {
            var selection = new EnvironmentSourceSelection("OrganizationOwned", id, null, null);
            var discovery = await service.DiscoverAsync(_system, new(0, selection), "actor");
            items.Add(new(0, selection, discovery.DiscoveryToken, [discovery.Resources[0].ResourceId], [], [], null));
        }
        // Act / Assert
        await FluentActions.Awaiting(() => service.ApplyBatchAsync(_system,
            new(0, [items[0], items[1] with { DiscoveryToken = Guid.NewGuid().ToString() }]), "fail", "actor"))
            .Should().ThrowAsync<ArgumentException>();
        (await service.ListAsync(_system)).Attachments.Should().BeEmpty();
        var result = await service.ApplyBatchAsync(_system, new(0, items), "batch", "actor");
        result.Version.Should().Be(1);
        result.Attachments.Should().HaveCount(2);
        (await service.ApplyBatchAsync(_system, new(0, items), "batch", "actor")).Should().BeEquivalentTo(result);
    }

    [Fact]
    public async Task IndependentLegacyLinks_VerifiedMappingSurvivesAdditiveMigrationAndUnlinkRetainsBoth()
    {
        // Arrange
        var f = await PublishedServiceAsync();
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var workspace = await service.AddProviderScopeAsync(_system, new(0, f.OfferingId, 1, f.HostingId), "scope", "actor");
        var discovery = await service.DiscoverAsync(_system, new(workspace.Version, Selection), "actor");
        workspace = await service.ApplyAsync(_system, new(workspace.Version, Selection, discovery.DiscoveryToken, [Resource], [], [], null), "apply", "actor");
        var assignmentId = workspace.ProviderScopes.Single().AssignmentId;
        var attachmentId = workspace.Attachments.Single().AttachmentId;
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.Set<SystemEnvironmentAttachmentRecord>().SingleAsync()).HostingAssignmentId = assignmentId;
            await db.SaveChangesAsync();
            await Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions.SystemEnvironmentSchemaAdditions.ApplyAsync(db,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        }
        // Act
        workspace = await service.ListAsync(_system);
        // Assert
        workspace.LegacyReferences.Should().BeEmpty();
        workspace.HostingLinks.Single().Source.Should().Be("RetainedLegacy");
        workspace.HostingLinks.Single().AssignmentId.Should().Be(assignmentId);
        // Act
        workspace = await LinkAsync(service, workspace, attachmentId, assignmentId, "Unlink");
        // Assert
        workspace.HostingLinks.Single().State.Should().Be("Unlinked");
        workspace.Attachments.Single().AttachmentState.Should().Be("Attached");
        workspace.ProviderScopes.Single().State.Should().Be("Active");
        workspace.LegacyReferences.Should().BeEmpty();
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<SystemEnvironmentHostingLinkRecord>().SingleAsync()).HistoryJson.Should().Contain("Unlink");
    }

    [Fact]
    public async Task IndependentLegacyInvalidLink_ProducesOneNamedActionableWarningNotCollectionDenial()
    {
        // Arrange
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var discovery = await service.DiscoverAsync(_system, new(0, Selection), "actor");
        await service.ApplyAsync(_system, new(0, Selection, discovery.DiscoveryToken, [Resource], [], [], null), "apply", "actor");
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.Set<SystemEnvironmentAttachmentRecord>().SingleAsync()).HostingAssignmentId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        // Act
        var workspace = await service.ListAsync(_system);
        // Assert
        workspace.LegacyReferences.Should().ContainSingle();
        workspace.LegacyReferences[0].DisplayName.Should().Be("Synthetic subscription");
        workspace.LegacyReferences[0].Reason.Should().Contain("Remove or replace");
        workspace.HostingLinks.Should().BeEmpty();
        var admission = await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment);
        admission.Sources.Single().Eligible.Should().BeTrue();
        admission.LegacyReferences.Should().BeEmpty("optional mapping warnings are not Azure admission prerequisites");
        var attachment = workspace.Attachments.Single();
        var preview = await service.PreviewHostingLinkAsync(_system, new(workspace.Version, attachment.AttachmentId,
            attachment.Version, attachment.HostingAssignmentId!.Value, 0, "Unlink", "Remove invalid retained mapping"), "actor");
        var repaired = await service.CommitHostingLinkAsync(_system,
            new(workspace.Version, preview.PreviewId, "Remove invalid retained mapping", true), "repair-invalid", "actor");
        repaired.LegacyReferences.Should().BeEmpty();
        repaired.Attachments.Single().AttachmentState.Should().Be("Attached");
        repaired.Attachments.Single().HostingAssignmentId.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentProviderAllocatedAttachment_LinkRemovalDoesNotRevokeActualAllocation(bool linkDuringApply)
    {
        // Arrange
        var allocationProvider = await SeedProviderAsync();
        var provider = new TenantContext(allocationProvider.OwnerId) { IsCspAdmin = true };
        ProviderEnvironmentAllocation allocation;
        using (_accessor.Push(provider))
            allocation = await ProviderService(provider).RecordAsync(allocationProvider.OfferingId,
                AllocationRequest(allocationProvider), "allocation", "provider");
        var serviceProvider = await PublishedServiceAsync();
        var resource = $"/subscriptions/{allocationProvider.SubscriptionId}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/current";
        _azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EnvironmentResource(resource, "current", "vm", "rg", null)]);
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var workspace = await service.AddProviderScopeAsync(_system,
            new(0, serviceProvider.OfferingId, 1, serviceProvider.HostingId), "service-provider", "actor");
        var selection = new EnvironmentSourceSelection("ProviderAllocation", allocationProvider.RegistrationId, allocation.AllocationId, 1);
        var discovery = await service.DiscoverAsync(_system, new(workspace.Version, selection), "actor");
        var relationship = workspace.ProviderScopes.Single();
        workspace = await service.ApplyAsync(_system, new(workspace.Version, selection, discovery.DiscoveryToken,
            [resource], [], [], linkDuringApply ? relationship.AssignmentId : null), "attach", "actor");
        if (!linkDuringApply)
            workspace = await LinkAsync(service, workspace, workspace.Attachments.Single().AttachmentId, relationship.AssignmentId);
        // Act
        var preview = await service.PreviewProviderScopeRemovalAsync(_system, relationship.AssignmentId,
            new(workspace.Version, relationship.AssignmentVersion, relationship.SelectionVersion, "End only the optional service relationship"), "actor");
        workspace = await service.RemoveProviderScopeAsync(_system, relationship.AssignmentId,
            new(workspace.Version, preview.PreviewId, "End only the optional service relationship", true), "remove-service", "actor");
        // Assert
        workspace.HostingLinks.Single().State.Should().Be("Unlinked");
        var admission = await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment);
        admission.Sources.Single().Eligible.Should().BeTrue();
        admission.Sources.Single().AllocationId.Should().Be(allocation.AllocationId);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<ProviderEnvironmentAllocationRecord>().SingleAsync()).State.Should().Be("Active");
        var mission = new ProviderMissionService(verify, tenant, _access.Object, new Mock<ICapabilityResponsibilityService>().Object);
        await FluentActions.Awaiting(() => mission.AssociateAsync(_system,
            new(relationship.AssignmentId, relationship.AssignmentVersion), "actor", default, "bypass-removal"))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task IndependentProviderRemoval_StaleReviewCannotRemoveChangedRelationship()
    {
        // Arrange
        var f = await PublishedServiceAsync();
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var workspace = await service.AddProviderScopeAsync(_system, new(0, f.OfferingId, 1, f.HostingId), "scope", "actor");
        var provider = workspace.ProviderScopes.Single();
        var preview = await service.PreviewProviderScopeRemovalAsync(_system, provider.AssignmentId,
            new(workspace.Version, provider.AssignmentVersion, provider.SelectionVersion, "End relationship"), "actor");
        await using (var db = new AtoCopilotContext(_options))
        {
            var review = await db.Set<MissionProviderRelationshipReview>().SingleAsync();
            review.Revision++;
            review.State = "SeparateAuthorizationBoundary";
            await db.SaveChangesAsync();
        }
        // Act / Assert
        await FluentActions.Awaiting(() => service.RemoveProviderScopeAsync(_system, provider.AssignmentId,
            new(workspace.Version, preview.PreviewId, "End relationship", true), "stale-remove", "actor"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await service.ListAsync(_system)).ProviderScopes.Single().State.Should().Be("Active");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task IndependentApplyBatch_RejectsOutOfBoundsBeforePersistence(int count)
    {
        // Arrange
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var item = new ApplySystemEnvironmentRequest(0, Selection, "not-discovered", [Resource], [], [], null);
        // Act / Assert
        await FluentActions.Awaiting(() => service.ApplyBatchAsync(_system, new(0, Enumerable.Repeat(item, count).ToArray()),
            "invalid-batch", "actor")).Should().ThrowAsync<ArgumentException>();
        (await service.ListAsync(_system)).Version.Should().Be(0);
    }

    [Fact]
    public async Task InitialOwnedAttachment_ExplicitExistingProviderScopeLinksAtomicallyWithoutCreatingRelationship()
    {
        // Arrange
        var f = await PublishedServiceAsync();
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var workspace = await service.AddProviderScopeAsync(_system, new(0, f.OfferingId, 1, f.HostingId), "scope", "actor");
        var providerScope = workspace.ProviderScopes.Single();
        var discovery = await service.DiscoverAsync(_system, new(workspace.Version, Selection), "actor");
        var input = new ApplySystemEnvironmentRequest(workspace.Version, Selection, discovery.DiscoveryToken,
            [Resource], [], [], providerScope.AssignmentId);
        // Act
        var attached = await service.ApplyBatchAsync(_system, new(workspace.Version, [input]), "explicit-apply", "actor");
        var replay = await service.ApplyBatchAsync(_system, new(workspace.Version, [input]), "explicit-apply", "actor");
        // Assert
        attached.Attachments.Single().Source.Should().Be("OrganizationOwned");
        attached.HostingLinks.Single().AssignmentId.Should().Be(providerScope.AssignmentId);
        attached.HostingLinks.Single().AttachmentId.Should().Be(attached.Attachments.Single().AttachmentId);
        attached.HostingLinks.Single().State.Should().Be("Linked");
        attached.Attachments.Single().HostingAssignmentId.Should().Be(providerScope.AssignmentId);
        replay.Should().BeEquivalentTo(attached);
        attached.ProviderScopes.Should().ContainSingle();
        attached.ProviderScopes.Single().RelationshipState.Should().Be("Undetermined");
        (await service.ResolveAsync(_system, EnvironmentScopePurpose.Assessment)).Sources.Single().Eligible.Should().BeTrue();
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<ProviderHostingAssignment>().CountAsync()).Should().Be(1);
        (await verify.Set<MissionProviderRelationshipReview>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InitialLinkBatch_LaterInvalidSelectionRollsBackAttachmentAndOptionalLinks()
    {
        // Arrange
        var f = await PublishedServiceAsync();
        var extraId = Guid.NewGuid();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.AzureSubscriptionRegistrations.Add(new() { Id = extraId, TenantId = _tenant, SubscriptionId = Guid.NewGuid(),
                ParentTenantId = _directory, DisplayName = "Second selected subscription" });
            await db.SaveChangesAsync();
        }
        _azure.Setup(x => x.DiscoverAsync(It.IsAny<EnvironmentRegistration>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EnvironmentRegistration identity, IReadOnlyList<string> _, CancellationToken _) =>
                new[] { new EnvironmentResource($"/subscriptions/{identity.SubscriptionId}/resourcegroups/rg/providers/microsoft.compute/virtualmachines/current", "current", "vm", "rg", null) });
        var tenant = Tenant();
        using var context = _accessor.Push(tenant);
        var service = Service(tenant);
        var workspace = await service.AddProviderScopeAsync(_system, new(0, f.OfferingId, 1, f.HostingId), "scope", "actor");
        var assignmentId = workspace.ProviderScopes.Single().AssignmentId;
        var inputs = new List<ApplySystemEnvironmentRequest>();
        foreach (var id in new[] { _registration, extraId })
        {
            var selection = new EnvironmentSourceSelection("OrganizationOwned", id, null, null);
            var discovery = await service.DiscoverAsync(_system, new(workspace.Version, selection), "actor");
            inputs.Add(new(workspace.Version, selection, discovery.DiscoveryToken, [discovery.Resources[0].ResourceId],
                [], [], id == _registration ? assignmentId : Guid.NewGuid()));
        }
        // Act / Assert
        await FluentActions.Awaiting(() => service.ApplyBatchAsync(_system, new(workspace.Version, inputs), "invalid-batch", "actor"))
            .Should().ThrowAsync<ArgumentException>();
        var retained = await service.ListAsync(_system);
        retained.Attachments.Should().BeEmpty();
        retained.HostingLinks.Should().BeEmpty();
        retained.Version.Should().Be(workspace.Version);
        retained.ProviderScopes.Should().ContainSingle();
        var corrected = new ApplySystemEnvironmentsRequest(workspace.Version, [inputs[0], inputs[1] with { ReuseHostingAssignmentId = null }]);
        var saved = await service.ApplyBatchAsync(_system, corrected, "corrected-batch", "actor");
        saved.Attachments.Should().HaveCount(2);
        saved.HostingLinks.Should().ContainSingle();
    }
}

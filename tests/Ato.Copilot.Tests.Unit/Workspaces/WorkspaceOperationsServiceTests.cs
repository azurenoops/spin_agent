using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Workspaces;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Text.Json;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Workspaces;

public sealed class WorkspaceOperationsServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrganizationDetail_LegacyProviderComponentLink_ResolvesPublishedComponent(bool uppercase)
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Organization" });
            db.CspInheritedComponents.Add(new CspInheritedComponent
            {
                Id = componentId, CspProfileId = Guid.NewGuid(), Name = "Backup service",
                Description = "Recovery platform", Status = CspInheritedComponentStatus.Published
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var id = uppercase ? componentId.ToString("D").ToUpperInvariant() : componentId.ToString("D");

        // Act
        var detail = await sut.GetOrganizationCapabilityAsync(tenantId, "provider", id, null, ["system"], default);

        // Assert
        detail.Should().NotBeNull();
        detail!.Capability.RecordType.Should().Be("component");
        detail.Capability.RecordId.Should().Be(componentId.ToString("D"));
        detail.Capability.Name.Should().Be("Backup service");
        detail.Responsibilities.Should().BeEmpty();
        detail.NarrativeReviews.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CspInheritedComponentStatus.Draft)]
    [InlineData(CspInheritedComponentStatus.Archived)]
    public async Task OrganizationDetail_ComponentFallback_DoesNotExposeUnpublishedProvider(
        CspInheritedComponentStatus status)
    {
        // Arrange
        var factory = NewFactory();
        var componentId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspInheritedComponents.Add(new CspInheritedComponent
            {
                Id = componentId, CspProfileId = Guid.NewGuid(), Name = "Private",
                Description = "Not published", Status = status
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var detail = await sut.GetOrganizationCapabilityAsync(
            Guid.NewGuid(), "provider", componentId.ToString(), null, ["system"], default);

        // Assert
        detail.Should().BeNull();
    }

    [Fact]
    public async Task OrganizationDetail_LocalComponent_RequiresMatchingTenantAndReadableSystem()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SystemComponents.Add(new SystemComponent
            {
                Id = "local-component", TenantId = tenantId, RegisteredSystemId = "allowed",
                Name = "Local platform", ComponentType = ComponentType.Thing, CreatedBy = "test"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var readable = await sut.GetOrganizationCapabilityAsync(
            tenantId, "local", "local-component", "allowed", ["allowed"], default);
        var foreign = await sut.GetOrganizationCapabilityAsync(
            Guid.NewGuid(), "local", "local-component", null, ["allowed"], default);
        var denied = await sut.GetOrganizationCapabilityAsync(
            tenantId, "local", "local-component", null, ["denied"], default);
        var wrongScope = await sut.GetOrganizationCapabilityAsync(
            tenantId, "local", "local-component", "denied", ["allowed", "denied"], default);

        // Assert
        readable.Should().NotBeNull();
        readable!.Capability.RecordType.Should().Be("component");
        readable.Capability.MutationAuthority.Should().Be("organization");
        foreign.Should().BeNull();
        denied.Should().BeNull();
        wrongScope.Should().BeNull();
    }

    [Fact]
    public async Task OrganizationDetail_ExplicitTypeDisambiguatesComponentAndCapabilityIds()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var id = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Organization" });
            db.CspInheritedComponents.Add(new CspInheritedComponent
            {
                Id = id, CspProfileId = Guid.NewGuid(), Name = "Component",
                Description = "Platform", Status = CspInheritedComponentStatus.Published
            });
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = id, CspInheritedComponentId = id, Name = "Capability",
                Description = "Service", Status = CspInheritedCapabilityStatus.Mapped
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var legacy = await sut.GetOrganizationCapabilityAsync(
            tenantId, "provider", id.ToString(), null, ["system"], default);
        var capability = await sut.GetOrganizationCapabilityAsync(
            tenantId, "provider", id.ToString(), null, ["system"], default, "capability");
        var component = await sut.GetOrganizationCapabilityAsync(
            tenantId, "provider", id.ToString(), null, ["system"], default, "component");

        // Assert
        legacy!.Capability.RecordType.Should().Be("capability");
        capability!.Capability.Name.Should().Be("Capability");
        component!.Capability.RecordType.Should().Be("component");
        component.Capability.Name.Should().Be("Component");
    }

    [Fact]
    public async Task OrganizationComponentChildren_LocalFilter_RequiresReadableTenantComponent()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.AddRange(new Tenant { Id = tenantId, DisplayName = "Organization" },
                new Tenant { Id = foreignTenant, DisplayName = "Foreign" });
            foreach (var (componentId, systemId, owner) in new[]
            {
                ("allowed-component", "allowed", tenantId), ("denied-component", "denied", tenantId),
                ("foreign-component", "allowed", foreignTenant)
            })
            {
                db.SystemComponents.Add(new SystemComponent
                {
                    Id = componentId, TenantId = owner, RegisteredSystemId = systemId,
                    Name = componentId, ComponentType = ComponentType.Thing, CreatedBy = "test"
                });
                db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
                {
                    TenantId = owner, SystemComponentId = componentId, SecurityCapabilityId = "shared"
                });
            }
            db.SecurityCapabilities.Add(new SecurityCapability
            {
                Id = "shared", TenantId = tenantId, Name = "Shared local capability",
                Description = "Local", Category = "AC", CreatedBy = "test"
            });
            db.SystemCapabilityLinks.Add(new SystemCapabilityLink
            {
                TenantId = tenantId, SecurityCapabilityId = "shared", RegisteredSystemId = "allowed"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var query = new WorkspaceCatalogQuery(Grouping: "capability", ComponentId: "allowed-component");

        // Act
        var allowed = await sut.ListOrganizationCapabilitiesAsync(tenantId, query, "local", "allowed", ["allowed"], default);
        var denied = await sut.ListOrganizationCapabilitiesAsync(
            tenantId, query with { ComponentId = "denied-component" }, "local", "allowed", ["allowed"], default);
        var foreign = await sut.ListOrganizationCapabilitiesAsync(
            tenantId, query with { ComponentId = "foreign-component" }, "local", "allowed", ["allowed"], default);

        // Assert
        allowed.Items.Should().ContainSingle(x => x.RecordId == "shared");
        denied.Total.Should().Be(0);
        foreign.Total.Should().Be(0);
    }

    [Fact]
    public async Task OrganizationDetail_ComponentSubscriptionCounts_ExcludeOtherTenantsAndUnreadableSystems()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspInheritedComponents.Add(new CspInheritedComponent
            {
                Id = componentId, CspProfileId = Guid.NewGuid(), Name = "Platform",
                Description = "Provider", Status = CspInheritedComponentStatus.Published
            });
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = componentId, Name = "Service",
                Description = "Provider", Status = CspInheritedCapabilityStatus.Mapped
            });
            foreach (var (systemId, owner, active) in new[]
            {
                ("allowed", tenantId, true), ("denied", tenantId, true),
                ("allowed", Guid.NewGuid(), true), ("inactive", tenantId, false)
            })
                db.CapabilitySubscriptions.Add(new CapabilitySubscription
                {
                    Id = Guid.NewGuid().ToString(), RoutingTenantId = owner,
                    CspInheritedCapabilityId = capabilityId.ToString("N").ToUpperInvariant(),
                    RegisteredSystemId = systemId, IsActive = active
                });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var detail = await sut.GetOrganizationCapabilityAsync(
            tenantId, "provider", componentId.ToString(), "allowed", ["allowed", "inactive"], default, "component");

        // Assert
        detail!.Capability.IsSubscribed.Should().BeTrue();
        detail.Capability.SystemCount.Should().Be(1);
    }

    [Fact]
    public async Task ListProviderCatalogAsync_PagesBeyondTwoHundredWithStableTotal()
    {
        // Arrange
        var factory = NewFactory();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Platform",
            Description = "Shared platform", Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.AddRange(Enumerable.Range(1, 250).Select(i =>
                new CspInheritedCapability
                {
                    Id = Guid.NewGuid(), CspInheritedComponentId = component.Id,
                    Name = $"Capability {i:D3}", Description = "description",
                    Status = CspInheritedCapabilityStatus.Mapped
                }));
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var result = await sut.ListProviderCatalogAsync(
            new WorkspaceCatalogQuery(2, 200, Grouping: "capability"), default);

        // Assert
        result.Total.Should().Be(250);
        result.Items.Should().HaveCount(50);
        result.Items.Select(x => x.Name).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task T010_ListProviderCatalogAsync_AppliesLifecycleAndReviewToRowsAndTotal()
    {
        // Arrange
        var factory = NewFactory();
        var published = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Published",
            Description = "Published component", Status = CspInheritedComponentStatus.Published
        };
        var draft = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Draft",
            Description = "Draft component", Status = CspInheritedComponentStatus.Draft
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspInheritedComponents.AddRange(published, draft);
            db.CspInheritedCapabilities.AddRange(
                new CspInheritedCapability
                {
                    Id = Guid.NewGuid(), CspInheritedComponentId = published.Id,
                    Name = "Ready", Description = "ready", Status = CspInheritedCapabilityStatus.Mapped
                },
                new CspInheritedCapability
                {
                    Id = Guid.NewGuid(), CspInheritedComponentId = published.Id,
                    Name = "Review", Description = "review", Status = CspInheritedCapabilityStatus.NeedsReview
                },
                new CspInheritedCapability
                {
                    Id = Guid.NewGuid(), CspInheritedComponentId = draft.Id,
                    Name = "Draft review", Description = "draft", Status = CspInheritedCapabilityStatus.NeedsReview
                });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var result = await sut.ListProviderCatalogAsync(
            new WorkspaceCatalogQuery(Grouping: "capability", Lifecycle: "Published", Review: "NeedsReview"),
            default);

        // Assert
        result.Total.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.Name == "Review");
    }

    [Fact]
    public async Task WorkingRevision_StaleWriteFails_AndPublishIsIdempotent()
    {
        // Arrange
        var factory = NewFactory();
        var capabilityId = Guid.NewGuid();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Component",
            Description = "description", Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id,
                Name = "Capability", Description = "description",
                Status = CspInheritedCapabilityStatus.Mapped
            });
            await db.SaveChangesAsync();
        }

        var sut = new WorkspaceOperationsService(factory);
        var saved = await sut.SaveWorkingRevisionAsync(capabilityId,
            new(1, "CUI", "Identity", ["alice", "bob"],
                new Dictionary<string, string> { ["AC-2"] = "Shared" }), "actor", default);
        var preview = await sut.GeneratePublicationPreviewAsync(capabilityId, saved.Revision, default);
        await sut.ApproveWorkingRevisionAsync(capabilityId,
            new(saved.Revision, preview.PreviewId, preview.PreviewHash), "reviewer", default);

        // Act
        var first = await sut.PublishAsync(capabilityId,
            new(saved.Revision, saved.Revision, preview.PreviewId, preview.PreviewHash, "publish-1"), "publisher", default);
        var second = await sut.PublishAsync(capabilityId,
            new(saved.Revision, saved.Revision, preview.PreviewId, preview.PreviewHash, "publish-1"), "publisher", default);
        Func<Task> mismatchedReplay = () => sut.PublishAsync(capabilityId,
            new(saved.Revision, saved.Revision, preview.PreviewId, new string('0', 64), "publish-1"), "publisher", default);
        Func<Task> stale = () => sut.SaveWorkingRevisionAsync(capabilityId,
            new(99, "CUI", "Identity", ["alice"],
                new Dictionary<string, string> { ["AC-2"] = "Provider" }), "actor", default);

        // Assert
        second.ReleaseId.Should().Be(first.ReleaseId);
        second.Existing.Should().BeTrue();
        await mismatchedReplay.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different publication*");
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.ProviderCapabilityReleases.CountAsync()).Should().Be(1);
        (await verify.ProviderCapabilityContributors.CountAsync()).Should().Be(2);
        (await verify.ProviderCapabilityDuties.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task WorkingRevision_DatabaseConcurrencyTokenRejectsSecondWriter()
    {
        // Arrange
        var factory = NewFactory();
        var row = new ProviderCapabilityWorkingRevision
        {
            CapabilityId = Guid.NewGuid(), Revision = 1, SnapshotHash = new string('A', 64),
            ContributorsJson = "[]", DutiesJson = "{}", UpdatedBy = "author"
        };
        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.ProviderCapabilityWorkingRevisions.Add(row);
            await seed.SaveChangesAsync();
        }
        await using var first = await factory.CreateDbContextAsync();
        await using var second = await factory.CreateDbContextAsync();
        var firstCopy = await first.ProviderCapabilityWorkingRevisions.SingleAsync();
        var secondCopy = await second.ProviderCapabilityWorkingRevisions.SingleAsync();
        firstCopy.Revision = 2;
        secondCopy.Revision = 2;
        firstCopy.UpdatedBy = "first";
        secondCopy.UpdatedBy = "second";
        await first.SaveChangesAsync();

        // Act
        Func<Task> staleSave = () => second.SaveChangesAsync();

        // Assert
        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task T013_PublishAsync_CreatesTenantSafeAddedChangedAndRemovedImpactStates()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Component",
            Description = "description", Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id,
                Name = "Capability", Description = "description",
                Status = CspInheritedCapabilityStatus.Mapped
            });
            db.CapabilitySubscriptions.Add(new CapabilitySubscription
            {
                Id = "subscription", RoutingTenantId = tenantId,
                RegisteredSystemId = "system", CspInheritedCapabilityId = capabilityId.ToString(),
                RoutingCapabilityId = capabilityId.ToString()
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var first = await sut.SaveWorkingRevisionAsync(capabilityId,
            new(1, "CUI", "Identity", [], new Dictionary<string, string>
            {
                ["AC-1"] = "Provider", ["AC-2"] = "Shared", ["AC-4"] = "Provider"
            }), "actor", default);
        var firstPreview = await sut.GeneratePublicationPreviewAsync(capabilityId, first.Revision, default);
        await sut.ApproveWorkingRevisionAsync(capabilityId,
            new(first.Revision, firstPreview.PreviewId, firstPreview.PreviewHash), "reviewer", default);
        await sut.PublishAsync(capabilityId,
            new(first.Revision, first.Revision, firstPreview.PreviewId, firstPreview.PreviewHash, "release-1"), "publisher", default);
        var second = await sut.SaveWorkingRevisionAsync(capabilityId,
            new(first.Revision, "CUI", "Identity", [], new Dictionary<string, string>
            {
                ["AC-2"] = "Customer", ["AC-3"] = "Provider", ["AC-4"] = "Provider"
            }), "actor", default);
        var secondPreview = await sut.GeneratePublicationPreviewAsync(capabilityId, second.Revision, default);
        await sut.ApproveWorkingRevisionAsync(capabilityId,
            new(second.Revision, secondPreview.PreviewId, secondPreview.PreviewHash), "reviewer", default);

        // Act
        var published = await sut.PublishAsync(capabilityId,
            new(second.Revision, second.Revision, secondPreview.PreviewId, secondPreview.PreviewHash, "release-2"), "publisher", default);

        // Assert
        await using var verify = await factory.CreateDbContextAsync();
        var impacts = await verify.ProviderReleaseImpacts.Where(x => x.ReleaseId == published.ReleaseId).ToListAsync();
        impacts.Should().HaveCount(3);
        impacts.Should().OnlyContain(x => x.TenantId == tenantId && x.DeliveryState == "Pending"
            && x.CustomerReviewState == "Pending" && x.NarrativeState == "Pending");
        impacts.Should().ContainSingle(x => x.ControlId == "AC-1" && x.ChangeKind == "Removed");
        impacts.Should().ContainSingle(x => x.ControlId == "AC-2" && x.ChangeKind == "Changed");
        impacts.Should().ContainSingle(x => x.ControlId == "AC-3" && x.ChangeKind == "Added");
        (await verify.CspInheritedCapabilities.SingleAsync(x => x.Id == capabilityId))
            .MappedNistControlIds.Should().BeEquivalentTo(["AC-2", "AC-3", "AC-4"]);
        (await verify.Set<CspResponsibilitySourceEvent>().CountAsync(x => x.CapabilityId == capabilityId))
            .Should().Be(2);
    }

    [Fact]
    public async Task CompleteSetupAsync_RetryDoesNotDuplicateSubscription()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Published",
            Description = "published", Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Org" });
            db.CspInheritedComponents.Add(component);
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "System",
                HostingEnvironment = "Azure", CreatedBy = "actor"
            });
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id,
                Name = "Capability", Description = "description",
                Status = CspInheritedCapabilityStatus.Mapped
            });
            await db.SaveChangesAsync();
        }

        var sut = new WorkspaceOperationsService(factory);
        var request = new Ato.Copilot.Core.Interfaces.Workspaces.CompleteCapabilitySetupRequest(
            "setup-1", "provider", capabilityId.ToString(), systemId, [], true);

        // Act
        var first = await sut.CompleteSetupAsync(tenantId, request, "actor", [systemId], default);
        var second = await sut.CompleteSetupAsync(tenantId, request, "actor", [systemId], default);

        // Assert
        second.OperationId.Should().Be(first.OperationId);
        second.SubscriptionState.Should().Be("Completed");
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.CapabilitySubscriptions.CountAsync()).Should().Be(1);
        (await verify.CapabilitySetupOperations.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CompleteSetupAsync_ConcurrentInsertReloadsPersistedWinner()
    {
        // Arrange
        var databasePath = Path.Combine(AppContext.BaseDirectory, "TestResults",
            $"workspace-setup-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var interceptor = new SetupReadBarrierInterceptor();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=30")
            .AddInterceptors(interceptor).Options;
        IDbContextFactory<AtoCopilotContext> factory = new TestFactory(options);
        var tenantId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Published",
            Description = "published", Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Org" });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "System",
                HostingEnvironment = "Azure", CreatedBy = "actor"
            });
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id,
                Name = "Capability", Description = "description",
                Status = CspInheritedCapabilityStatus.Mapped
            });
            await db.SaveChangesAsync();
        }
        var request = new Ato.Copilot.Core.Interfaces.Workspaces.CompleteCapabilitySetupRequest(
            "concurrent-setup", "provider", capabilityId.ToString(), systemId, [], true);
        var first = new WorkspaceOperationsService(factory);
        var second = new WorkspaceOperationsService(factory);

        // Act
        var results = await Task.WhenAll(
            first.CompleteSetupAsync(tenantId, request, "actor", [systemId], default),
            second.CompleteSetupAsync(tenantId, request, "actor", [systemId], default));

        // Assert
        results.Select(x => x.OperationId).Distinct().Should().ContainSingle();
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.CapabilitySetupOperations.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await verify.CapabilitySubscriptions.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        await verify.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }

    [Fact]
    public async Task T014_ListOrganizationsAsync_ReviewFilterUsesPersistedUnresolvedImpacts()
    {
        // Arrange
        var factory = NewFactory();
        var pendingTenant = new Tenant { Id = Guid.NewGuid(), DisplayName = "Pending" };
        var currentTenant = new Tenant { Id = Guid.NewGuid(), DisplayName = "Current" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.AddRange(pendingTenant, currentTenant);
            db.ProviderReleaseImpacts.Add(new Ato.Copilot.Core.Models.Workspaces.ProviderReleaseImpact
            {
                ReleaseId = Guid.NewGuid(), TenantId = pendingTenant.Id,
                RegisteredSystemId = Guid.NewGuid().ToString(), ControlId = "AC-2",
                CustomerReviewState = "Pending", NarrativeState = "Pending"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var result = await sut.ListOrganizationsAsync(
            new OrganizationCatalogQuery(Review: "Pending"), default);

        // Assert
        result.Total.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.Id == pendingTenant.Id && x.ReviewState == "Pending");
    }

    [Fact]
    public async Task T015_GetOrganizationAsync_DoesNotMixTargetSystemsOrActivity()
    {
        // Arrange
        var factory = NewFactory();
        var target = new Tenant { Id = Guid.NewGuid(), DisplayName = "Target" };
        var other = new Tenant { Id = Guid.NewGuid(), DisplayName = "Other" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.AddRange(target, other);
            db.RegisteredSystems.AddRange(
                new RegisteredSystem
                {
                    TenantId = target.Id, Id = "target-system", Name = "Target system",
                    HostingEnvironment = "Azure", CreatedBy = "actor"
                },
                new RegisteredSystem
                {
                    TenantId = other.Id, Id = "other-system", Name = "Other system",
                    HostingEnvironment = "Azure", CreatedBy = "actor"
                });
            db.CapabilitySubscriptions.Add(new CapabilitySubscription
            {
                Id = "target-subscription", RoutingTenantId = target.Id,
                RegisteredSystemId = "target-system",
                CspInheritedCapabilityId = Guid.NewGuid().ToString(),
                RoutingCapabilityId = "not-a-source-revision"
            });
            db.Set<CapabilityResponsibilityConfirmation>().Add(new CapabilityResponsibilityConfirmation
            {
                TenantId = target.Id, RegisteredSystemId = "target-system",
                SubscriptionId = "target-subscription", ReviewedBaselineId = "baseline",
                ControlId = "AC-2", SourceRevision = "release-7", SourceSnapshotJson = "{}",
                ConfirmedBy = "reviewer"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var result = await sut.GetOrganizationAsync(target.Id, default);

        // Assert
        result.Should().NotBeNull();
        result!.Systems.Should().ContainSingle(x => x.Id == "target-system");
        result.Systems.Should().NotContain(x => x.Id == "other-system");
        result.Subscriptions.Should().ContainSingle(x => x.SourceRevision == "release-7");
    }

    [Fact]
    public async Task T016_ProvisioningAsync_RetryPreservesIndependentEnrollmentStates()
    {
        // Arrange
        var factory = NewFactory();
        var tenant = new Tenant { Id = Guid.NewGuid(), DisplayName = "Organization" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var created = await sut.GetOrCreateProvisioningAsync(tenant.Id, "create-1", default);
        var personId = Guid.NewGuid();
        var request = new Ato.Copilot.Core.Interfaces.Workspaces.UpdateProvisioningRequest(
            Guid.NewGuid(), Guid.NewGuid(), personId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Persons.Add(new Ato.Copilot.Core.Models.Onboarding.Person
            {
                Id = personId, TenantId = tenant.Id, DisplayName = "Administrator", Email = "admin@example.invalid"
            });
            await db.SaveChangesAsync();
        }

        // Act
        var resumed = await sut.UpdateProvisioningAsync(tenant.Id, created.OperationId, request, default);

        // Assert
        resumed.OperationId.Should().Be(created.OperationId);
        resumed.AdministratorState.Should().Be("Pending");
        resumed.MembershipState.Should().Be("Pending");
        resumed.OverallState.Should().Be("InProgress");
    }

    [Fact]
    public async Task T020_CompleteSetupAsync_RejectsChangedIntentForSameIdempotencyKey()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Published",
            Description = "published", Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Org" });
            db.CspInheritedComponents.Add(component);
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "System",
                HostingEnvironment = "Azure", CreatedBy = "actor"
            });
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id,
                Name = "Capability", Description = "description",
                Status = CspInheritedCapabilityStatus.Mapped
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        await sut.CompleteSetupAsync(tenantId, new(
            "setup-intent", "provider", capabilityId.ToString(), systemId, [], true), "actor", [systemId], default);

        // Act
        Func<Task> changedIntent = () => sut.CompleteSetupAsync(tenantId, new(
            "setup-intent", "provider", capabilityId.ToString(), systemId, [], false), "actor", [systemId], default);

        // Assert
        await changedIntent.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different setup*");
    }

    [Fact]
    public async Task T018_ListOrganizationCapabilitiesAsync_IsolatesLocalRowsAndFiltersBySystem()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var localId = Guid.NewGuid().ToString();
        var otherId = Guid.NewGuid().ToString();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.AddRange(
                new Tenant { Id = tenantId, DisplayName = "Target" },
                new Tenant { Id = otherTenantId, DisplayName = "Other" });
            db.SecurityCapabilities.AddRange(
                new SecurityCapability
                {
                    TenantId = tenantId, Id = localId, Name = "Target local", Description = "target",
                    Provider = "local", Category = "AC", Owner = "owner", CreatedBy = "actor"
                },
                new SecurityCapability
                {
                    TenantId = otherTenantId, Id = otherId, Name = "Other local", Description = "other",
                    Provider = "local", Category = "AC", Owner = "owner", CreatedBy = "actor"
                });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = "system-a", Name = "System A",
                HostingEnvironment = "Azure", CreatedBy = "actor"
            });
            db.SystemCapabilityLinks.Add(new SystemCapabilityLink
            {
                TenantId = tenantId, RegisteredSystemId = "system-a",
                SecurityCapabilityId = localId, LinkedBy = "actor"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var result = await sut.ListOrganizationCapabilitiesAsync(
            tenantId, new WorkspaceCatalogQuery(Grouping: "capability"), "local", "system-a", ["system-a"], default);

        // Assert
        result.Total.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.Source == "local"
            && x.RecordId == localId && x.MutationAuthority == "organization");
        result.Items.Should().NotContain(x => x.RecordId == otherId);
    }

    [Fact]
    public async Task T020_CompleteSetupAsync_PersistsPartialFailureAndResumesWithoutDeletingSharedComponents()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid().ToString();
        var systemId = Guid.NewGuid().ToString();
        var existingComponentId = Guid.NewGuid().ToString();
        var missingComponentId = Guid.NewGuid().ToString();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Org" });
            db.SecurityCapabilities.Add(new SecurityCapability
            {
                TenantId = tenantId, Id = capabilityId, Name = "Local", Description = "local",
                Provider = "local", Category = "AC", Owner = "owner", CreatedBy = "actor"
            });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "System",
                HostingEnvironment = "Azure", CreatedBy = "actor"
            });
            db.SystemComponents.Add(new SystemComponent
            {
                TenantId = tenantId, Id = existingComponentId, RegisteredSystemId = systemId, Name = "Existing",
                ComponentType = Ato.Copilot.Core.Models.Compliance.ComponentType.Thing,
                CreatedBy = "actor"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var request = new Ato.Copilot.Core.Interfaces.Workspaces.CompleteCapabilitySetupRequest(
            "partial-setup", "local", capabilityId, systemId,
            [existingComponentId, missingComponentId], false);
        Func<Task> firstAttempt = () => sut.CompleteSetupAsync(tenantId, request, "actor", [systemId], default);
        await firstAttempt.Should().ThrowAsync<KeyNotFoundException>();
        await using (var repair = await factory.CreateDbContextAsync())
        {
            repair.SystemComponents.Add(new SystemComponent
            {
                TenantId = tenantId, Id = missingComponentId, RegisteredSystemId = systemId, Name = "Recovered",
                ComponentType = Ato.Copilot.Core.Models.Compliance.ComponentType.Thing,
                CreatedBy = "actor"
            });
            await repair.SaveChangesAsync();
        }

        // Act
        var resumed = await sut.CompleteSetupAsync(tenantId, request, "actor", [systemId], default);

        // Assert
        resumed.RecordState.Should().Be("Completed");
        resumed.ComponentLinksState.Should().Be("Completed");
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.ComponentCapabilityLinks.IgnoreQueryFilters()
            .CountAsync(x => x.TenantId == tenantId && x.SecurityCapabilityId == capabilityId)).Should().Be(2);
        (await verify.SystemComponents.IgnoreQueryFilters()
            .CountAsync(x => x.TenantId == tenantId)).Should().Be(2);
    }

    [Fact]
    public async Task CompleteSetupAsync_RejectsComponentOwnedByDifferentSystem()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid().ToString();
        var requestedSystem = Guid.NewGuid().ToString();
        var otherSystem = Guid.NewGuid().ToString();
        var componentId = Guid.NewGuid().ToString();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Org" });
            db.RegisteredSystems.AddRange(
                new RegisteredSystem { TenantId = tenantId, Id = requestedSystem, Name = "Requested", CreatedBy = "actor" },
                new RegisteredSystem { TenantId = tenantId, Id = otherSystem, Name = "Other", CreatedBy = "actor" });
            db.SecurityCapabilities.Add(new SecurityCapability
            {
                TenantId = tenantId, Id = capabilityId, Name = "Local", Description = "local",
                Provider = "local", Category = "AC", Owner = "owner", CreatedBy = "actor"
            });
            db.SystemComponents.Add(new SystemComponent
            {
                TenantId = tenantId, Id = componentId, RegisteredSystemId = otherSystem, Name = "Other component",
                ComponentType = ComponentType.Thing, CreatedBy = "actor"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        Func<Task> setup = () => sut.CompleteSetupAsync(tenantId,
            new("wrong-system-component", "local", capabilityId, requestedSystem, [componentId], false),
            "actor", [requestedSystem], default);

        // Assert
        await setup.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*target system*");
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        var operation = await verify.CapabilitySetupOperations.IgnoreQueryFilters().SingleAsync();
        JsonSerializer.Deserialize<Ato.Copilot.Core.Interfaces.Workspaces.SetupWriteOutcome[]>(
            operation.OutcomesJson).Should().ContainSingle(x =>
                x.WriteId == componentId && x.State == "Failed");
    }

    [Fact]
    public async Task PublishAsync_DutyOnlyChangeStagesReleaseBoundSourceEvent()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Component",
            Description = "provider", Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id, Name = "Capability",
                Description = "description", Status = CspInheritedCapabilityStatus.Mapped
            });
            db.CapabilitySubscriptions.Add(new CapabilitySubscription
            {
                Id = "subscription", RoutingTenantId = tenantId, RegisteredSystemId = "system",
                CspInheritedCapabilityId = capabilityId.ToString(), RoutingCapabilityId = capabilityId.ToString()
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var first = await sut.SaveWorkingRevisionAsync(capabilityId,
            new(1, "CUI", "Service", [], new Dictionary<string, string> { ["AC-2"] = "Provider" }),
            "author", default);
        var firstPreview = await sut.GeneratePublicationPreviewAsync(capabilityId, first.Revision, default);
        await sut.ApproveWorkingRevisionAsync(capabilityId,
            new(first.Revision, firstPreview.PreviewId, firstPreview.PreviewHash), "reviewer", default);
        await sut.PublishAsync(capabilityId,
            new(first.Revision, first.Revision, firstPreview.PreviewId, firstPreview.PreviewHash, "duty-release-1"), "publisher", default);
        var second = await sut.SaveWorkingRevisionAsync(capabilityId,
            new(first.Revision, "CUI", "Service", [], new Dictionary<string, string> { ["AC-2"] = "Shared" }),
            "author", default);
        var secondPreview = await sut.GeneratePublicationPreviewAsync(capabilityId, second.Revision, default);
        await sut.ApproveWorkingRevisionAsync(capabilityId,
            new(second.Revision, secondPreview.PreviewId, secondPreview.PreviewHash), "reviewer", default);

        // Act
        var release = await sut.PublishAsync(capabilityId,
            new(second.Revision, second.Revision, secondPreview.PreviewId, secondPreview.PreviewHash, "duty-release-2"), "publisher", default);

        // Assert
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.Set<CspResponsibilitySourceEvent>().CountAsync(x => x.CapabilityId == capabilityId))
            .Should().Be(2);
        var impact = await verify.ProviderReleaseImpacts.SingleAsync(x => x.ReleaseId == release.ReleaseId);
        impact.SourceRevision.Should().Be(second.SnapshotHash);
        var storedRelease = await verify.ProviderCapabilityReleases.SingleAsync(x => x.Id == release.ReleaseId);
        using var snapshot = JsonDocument.Parse(storedRelease.SnapshotJson);
        snapshot.RootElement.GetProperty("Capability").GetProperty("Component")
            .GetProperty("Name").GetString().Should().Be("Component");
        snapshot.RootElement.GetProperty("DutiesJson").GetString().Should().Contain("\"Shared\"");
    }

    [Fact]
    public async Task ProviderReleaseImpactProcessor_MakesSupersededTerminalAndNonReviewable()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var impact = new ProviderReleaseImpact
        {
            TenantId = tenantId, ReleaseId = Guid.NewGuid(), RegisteredSystemId = "system",
            SubscriptionId = "subscription", ControlId = "AC-2", SourceEventId = sourceId,
            SourceRevision = "revision"
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Org" });
            db.ProviderReleaseImpacts.Add(impact);
            db.Set<CspResponsibilitySourceEvent>().Add(new CspResponsibilitySourceEvent
            {
                Id = sourceId, CapabilityId = Guid.NewGuid(), ComponentId = Guid.NewGuid(),
                CspProfileId = Guid.NewGuid(), SourceRevision = "revision", Sequence = 1, Actor = "publisher"
            });
            db.Set<CapabilityResponsibilityDelivery>().Add(new CapabilityResponsibilityDelivery
            {
                Id = "delivery", TenantId = tenantId, RegisteredSystemId = "system",
                SubscriptionId = "subscription", SourceEventId = sourceId,
                Outcome = "Superseded", Attempts = 1, CompletedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
        await using var processorDb = await factory.CreateDbContextAsync();
        var processor = new ProviderReleaseImpactProcessor(processorDb);

        // Act
        var firstPass = await processor.RunOnceAsync();
        var secondPass = await processor.RunOnceAsync();
        var pending = await new WorkspaceOperationsService(factory).ListOrganizationsAsync(
            new OrganizationCatalogQuery(Review: "Pending"), default);
        var notRequired = await new WorkspaceOperationsService(factory).ListOrganizationsAsync(
            new OrganizationCatalogQuery(Review: "NotRequired"), default);

        // Assert
        firstPass.Should().Be(1);
        secondPass.Should().Be(0);
        processorDb.ChangeTracker.Clear();
        var updated = await processorDb.ProviderReleaseImpacts.SingleAsync();
        updated.DeliveryState.Should().Be("Superseded");
        updated.CustomerReviewState.Should().Be("NotRequired");
        updated.NarrativeState.Should().Be("NotRequired");
        pending.Items.Should().NotContain(x => x.Id == tenantId);
        notRequired.Items.Should().ContainSingle(x => x.Id == tenantId);
    }

    [Fact]
    public async Task UpdateProvisioningAsync_ConcurrentDifferentIdentityHasSingleBindingWinner()
    {
        // Arrange
        var interceptor = new ProvisioningSaveBarrierInterceptor();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"workspace-provision-{Guid.NewGuid():N}")
            .AddInterceptors(interceptor).Options;
        IDbContextFactory<AtoCopilotContext> factory = new TestFactory(options);
        var tenantId = Guid.NewGuid();
        Guid operationId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Org" });
            var operation = new OrganizationProvisioningOperation
            {
                TenantId = tenantId, IdempotencyKey = "provision-race"
            };
            db.OrganizationProvisioningOperations.Add(operation);
            await db.SaveChangesAsync();
            operationId = operation.Id;
        }
        var firstIdentity = new Ato.Copilot.Core.Interfaces.Workspaces.UpdateProvisioningRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var secondIdentity = new Ato.Copilot.Core.Interfaces.Workspaces.UpdateProvisioningRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Persons.AddRange(new[] { firstIdentity.PersonId!.Value, secondIdentity.PersonId!.Value }
                .Select(id => new Ato.Copilot.Core.Models.Onboarding.Person
                {
                    Id = id, TenantId = tenantId, DisplayName = "Administrator", Email = $"{id:N}@example.invalid"
                }));
            await db.SaveChangesAsync();
        }
        var first = new WorkspaceOperationsService(factory);
        var second = new WorkspaceOperationsService(factory);

        // Act
        var attempts = await Task.WhenAll(
            Record.ExceptionAsync(() => first.UpdateProvisioningAsync(tenantId, operationId, firstIdentity, default)),
            Record.ExceptionAsync(() => second.UpdateProvisioningAsync(tenantId, operationId, secondIdentity, default)));

        // Assert
        attempts.Count(x => x is null).Should().Be(1);
        attempts.Count(x => x is InvalidOperationException or DbUpdateConcurrencyException).Should().Be(1);
        await using var verify = await factory.CreateDbContextAsync();
        var winner = await verify.OrganizationProvisioningOperations.SingleAsync();
        new[] { firstIdentity.PersonId, secondIdentity.PersonId }.Should().Contain(winner.PersonId!.Value);
    }

    [Fact]
    public async Task OrganizationCapabilityDetail_ProjectsLocalNarrativeSource()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid().ToString();
        const string systemId = "local-system";
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Org" });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "System", CreatedBy = "actor"
            });
            db.SecurityCapabilities.Add(new SecurityCapability
            {
                TenantId = tenantId, Id = capabilityId, Name = "Local", Description = "local",
                Provider = "local", Category = "AC", Owner = "owner", CreatedBy = "actor"
            });
            db.SystemCapabilityLinks.Add(new SystemCapabilityLink
            {
                TenantId = tenantId, RegisteredSystemId = systemId,
                SecurityCapabilityId = capabilityId, LinkedBy = "actor"
            });
            db.NarrativeProposals.Add(new NarrativeProposal
            {
                TenantId = tenantId, RegisteredSystemId = systemId, ControlId = "AC-2", NarrativeType = "Policy",
                BaseVersion = 1, BeforeContent = "before", ProposedContent = "after", StateHash = "hash",
                ProvenanceJson = """{"source":"organization"}""", ConflictsJson = "[]", MissingEvidenceJson = "[]",
                Status = "Draft", Revision = 4, CreatedBy = "author", DeduplicationKey = Guid.NewGuid().ToString(),
                ChangeSourceKind = "OrganizationCapability", ChangeSourceId = capabilityId
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var detail = await sut.GetOrganizationCapabilityAsync(
            tenantId, "local", capabilityId, systemId, [systemId], default);

        // Assert
        detail.Should().NotBeNull();
        detail!.NarrativeReviews.Should().ContainSingle(x =>
            x.Revision == 4 && x.Provenance.GetProperty("source").GetString() == "organization");
    }

    [Fact]
    public async Task ListProviderSubscribersAsync_ReturnsBoundedSystemQualifiedSummaries()
    {
        // Arrange
        var factory = NewFactory();
        var capabilityId = Guid.NewGuid();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Component",
            Description = "provider", Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id, Name = "Capability",
                Description = "description", Status = CspInheritedCapabilityStatus.Mapped
            });
            for (var index = 0; index < 3; index++)
            {
                var tenantId = Guid.NewGuid();
                var systemId = $"system-{index}";
                db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = $"Organization {index}" });
                db.RegisteredSystems.Add(new RegisteredSystem
                {
                    TenantId = tenantId, Id = systemId, Name = $"System {index}", CreatedBy = "actor"
                });
                db.CapabilitySubscriptions.Add(new CapabilitySubscription
                {
                    Id = $"subscription-{index}", RoutingTenantId = tenantId, RegisteredSystemId = systemId,
                    CspInheritedCapabilityId = capabilityId.ToString(), RoutingCapabilityId = capabilityId.ToString()
                });
            }
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var result = await sut.ListProviderSubscribersAsync(capabilityId, 2, 2, default);

        // Assert
        result.Total.Should().Be(3);
        result.Items.Should().ContainSingle();
        result.Items[0].OrganizationName.Should().Be("Organization 2");
        result.Items[0].SystemId.Should().Be("system-2");
        result.Items[0].SubscriptionId.Should().Be("subscription-2");
    }

    [Fact]
    public async Task CompleteSetupAsync_PersistsOutcomeForEveryRequestedWrite()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        var capabilityId = Guid.NewGuid().ToString();
        var validComponent = Guid.NewGuid().ToString();
        var missingComponent = Guid.NewGuid().ToString();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Org" });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "System", CreatedBy = "actor"
            });
            db.SecurityCapabilities.Add(new SecurityCapability
            {
                TenantId = tenantId, Id = capabilityId, Name = "Local", Description = "local",
                Provider = "local", Category = "AC", Owner = "owner", CreatedBy = "actor"
            });
            db.SystemComponents.Add(new SystemComponent
            {
                TenantId = tenantId, Id = validComponent, RegisteredSystemId = systemId,
                Name = "Valid", ComponentType = ComponentType.Thing, CreatedBy = "actor"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var request = new Ato.Copilot.Core.Interfaces.Workspaces.CompleteCapabilitySetupRequest(
            "per-write", "local", capabilityId, systemId, [validComponent, missingComponent], false);

        // Act
        await Record.ExceptionAsync(() => sut.CompleteSetupAsync(
            tenantId, request, "actor", [systemId], default));

        // Assert
        await using var verify = await factory.CreateDbContextAsync();
        var operation = await verify.CapabilitySetupOperations.IgnoreQueryFilters().SingleAsync();
        var outcomes = JsonSerializer.Deserialize<Ato.Copilot.Core.Interfaces.Workspaces.SetupWriteOutcome[]>(
            operation.OutcomesJson)!;
        outcomes.Should().ContainSingle(x => x.WriteKind == "record" && x.State == "Completed");
        outcomes.Should().ContainSingle(x => x.WriteId == validComponent && x.State == "Pending");
        outcomes.Should().ContainSingle(x => x.WriteId == missingComponent && x.State == "Failed"
            && !string.IsNullOrWhiteSpace(x.Error));
    }

    [Fact]
    public async Task SaveWorkingRevisionAsync_AfterBackfillUsesNextReleaseRevision()
    {
        // Arrange
        var factory = NewFactory();
        var capabilityId = Guid.NewGuid();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Component",
            Description = "provider", Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id, Name = "Capability",
                Description = "description", Status = CspInheritedCapabilityStatus.Mapped
            });
            db.ProviderCapabilityReleases.Add(new ProviderCapabilityRelease
            {
                CapabilityId = capabilityId, Revision = 1, SnapshotHash = "backfill-hash",
                SnapshotJson = "{}", IdempotencyKey = $"schema-backfill:{capabilityId:N}",
                PublishedBy = "schema-backfill"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var result = await sut.SaveWorkingRevisionAsync(capabilityId,
            new(1, "CUI", "Service", [], new Dictionary<string, string> { ["AC-2"] = "Provider" }),
            "author", default);

        // Assert
        result.Revision.Should().Be(2);
    }

    [Fact]
    public async Task PublishAsync_RejectsRevisionReplayWhenSnapshotDiffers()
    {
        // Arrange
        var factory = NewFactory();
        var capabilityId = Guid.NewGuid();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Component",
            Description = "provider", Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id, Name = "Capability",
                Description = "description", Status = CspInheritedCapabilityStatus.Mapped
            });
            db.ProviderCapabilityWorkingRevisions.Add(new ProviderCapabilityWorkingRevision
            {
                CapabilityId = capabilityId, Revision = 1, Classification = "CUI",
                ServiceCategory = "Service", SnapshotHash = "authored", DutiesJson = "{}",
                UpdatedBy = "author"
            });
            db.ProviderCapabilityReleases.Add(new ProviderCapabilityRelease
            {
                CapabilityId = capabilityId, Revision = 1, SnapshotHash = "backfill",
                SnapshotJson = "{}", IdempotencyKey = $"schema-backfill:{capabilityId:N}",
                PublishedBy = "schema-backfill"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var preview = await sut.GeneratePublicationPreviewAsync(capabilityId, 1, default);
        await sut.ApproveWorkingRevisionAsync(capabilityId,
            new(1, preview.PreviewId, preview.PreviewHash), "reviewer", default);

        // Act
        Func<Task> publish = () => sut.PublishAsync(capabilityId,
            new(1, 1, preview.PreviewId, preview.PreviewHash, "authored-release"), "publisher", default);

        // Assert
        await publish.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different snapshot*");
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.ProviderCapabilityReleases.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CompleteSetupAsync_CanonicalizesEquivalentProviderGuidSpellings()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Org" });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "System", CreatedBy = "actor"
            });
            var component = new CspInheritedComponent
            {
                Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Component",
                Description = "provider", Status = CspInheritedComponentStatus.Published
            };
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id, Name = "Capability",
                Description = "description", Status = CspInheritedCapabilityStatus.Mapped
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var first = new Ato.Copilot.Core.Interfaces.Workspaces.CompleteCapabilitySetupRequest(
            "canonical-provider", "provider", capabilityId.ToString("N").ToUpperInvariant(),
            systemId, [], true);
        var retry = first with { RecordId = capabilityId.ToString("D") };

        // Act
        var firstResult = await sut.CompleteSetupAsync(
            tenantId, first, "actor", [systemId], default);
        var retryResult = await sut.CompleteSetupAsync(
            tenantId, retry, "actor", [systemId], default);
        var detail = await sut.GetOrganizationCapabilityAsync(
            tenantId, "provider", capabilityId.ToString("N").ToUpperInvariant(),
            systemId, [systemId], default);

        // Assert
        retryResult.OperationId.Should().Be(firstResult.OperationId);
        detail.Should().NotBeNull();
        detail!.Capability.RecordId.Should().Be(capabilityId.ToString("D"));
        detail.Capability.IsSubscribed.Should().BeTrue();
        await using var verify = await factory.CreateDbContextAsync();
        var operation = await verify.CapabilitySetupOperations.IgnoreQueryFilters().SingleAsync();
        operation.SourceRecordId.Should().Be(capabilityId.ToString("D"));
        var subscription = await verify.CapabilitySubscriptions.IgnoreQueryFilters().SingleAsync();
        subscription.CspInheritedCapabilityId.Should().Be(capabilityId.ToString("D"));
        subscription.RoutingCapabilityId.Should().Be(capabilityId.ToString("D"));
    }

    [Fact]
    public async Task OrganizationLibrary_UsesOnlyAuthorizedSystems_AndSupportsComponentGrouping()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Org" });
            db.RegisteredSystems.AddRange(
                new RegisteredSystem { TenantId = tenantId, Id = "allowed", Name = "Allowed", HostingEnvironment = "Azure", CreatedBy = "actor" },
                new RegisteredSystem { TenantId = tenantId, Id = "denied", Name = "Denied", HostingEnvironment = "Azure", CreatedBy = "actor" });
            db.SystemComponents.AddRange(
                new SystemComponent
                {
                    TenantId = tenantId, Id = "z-component", RegisteredSystemId = "allowed", Name = "Zulu",
                    ComponentType = ComponentType.Thing, CreatedBy = "actor"
                },
                new SystemComponent
                {
                    TenantId = tenantId, Id = "a-component", RegisteredSystemId = "denied", Name = "Alpha",
                    ComponentType = ComponentType.Thing, CreatedBy = "actor"
                });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var result = await sut.ListOrganizationCapabilitiesAsync(tenantId,
            new WorkspaceCatalogQuery(Grouping: "normalized-component", Sort: "name", Direction: "desc"),
            "local", "allowed", ["allowed"], default);

        // Assert
        result.Total.Should().Be(1);
        result.Items.Should().ContainSingle(x => x.RecordId == "z-component" && x.RecordType == "component");
        result.Items.Should().NotContain(x => x.RecordId == "a-component");
    }

    [Fact]
    public async Task OrganizationCapabilityDetail_ProjectsExistingNarrativeReviewStateAndProvenance()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Provider",
            Description = "provider", Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Org" });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = "system", Name = "System", HostingEnvironment = "Azure", CreatedBy = "actor"
            });
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id, Name = "Capability",
                Description = "description", Status = CspInheritedCapabilityStatus.Mapped
            });
            db.NarrativeProposals.Add(new NarrativeProposal
            {
                TenantId = tenantId, RegisteredSystemId = "system", ControlId = "AC-2", NarrativeType = "Policy",
                BaseVersion = 1, BeforeContent = "before", ProposedContent = "after", StateHash = "hash",
                ProvenanceJson = """{"source":"provider"}""", ConflictsJson = "[]", MissingEvidenceJson = "[]",
                Status = "Draft", Revision = 3, CreatedBy = "author", DeduplicationKey = Guid.NewGuid().ToString(),
                ChangeSourceKind = "CspCapability", ChangeSourceId = capabilityId.ToString()
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var detail = await sut.GetOrganizationCapabilityAsync(
            tenantId, "provider", capabilityId.ToString(), "system", ["system"], default);

        // Assert
        detail.Should().NotBeNull();
        detail!.NarrativeReviews.Should().ContainSingle(x =>
            x.Status == "Draft" && x.Revision == 3 && x.Provenance.GetProperty("source").GetString() == "provider");
    }

    [Fact]
    public async Task ProviderReleaseImpactProcessor_TransitionsDeliveryReviewAndNarrativeIndependently()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var impact = new ProviderReleaseImpact
        {
            ReleaseId = Guid.NewGuid(), TenantId = tenantId, RegisteredSystemId = "system",
            SubscriptionId = "subscription", ControlId = "AC-2", SourceEventId = sourceEventId,
            SourceRevision = "revision-2"
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProviderReleaseImpacts.Add(impact);
            db.Set<CspResponsibilitySourceEvent>().Add(new CspResponsibilitySourceEvent
            {
                Id = sourceEventId, CapabilityId = capabilityId, ComponentId = Guid.NewGuid(),
                CspProfileId = Guid.NewGuid(), SourceRevision = "revision-2", Sequence = 2, Actor = "publisher"
            });
            db.Set<CapabilityResponsibilityDelivery>().Add(new CapabilityResponsibilityDelivery
            {
                Id = "delivery", TenantId = tenantId, RegisteredSystemId = "system",
                SubscriptionId = "subscription", SourceEventId = sourceEventId, Outcome = "Completed",
                Attempts = 2, CompletedAt = DateTimeOffset.UtcNow
            });
            db.Set<CapabilityResponsibilityConfirmation>().Add(new CapabilityResponsibilityConfirmation
            {
                TenantId = tenantId, RegisteredSystemId = "system", SubscriptionId = "subscription",
                ReviewedBaselineId = "baseline", ControlId = "AC-2", SourceRevision = "revision-2",
                SourceSnapshotJson = "{}", ConfirmedBy = "reviewer"
            });
            db.NarrativeProposals.Add(new NarrativeProposal
            {
                TenantId = tenantId, RegisteredSystemId = "system", ControlId = "AC-2", NarrativeType = "Policy",
                BaseVersion = 1, BeforeContent = "before", ProposedContent = "after", StateHash = "hash",
                ProvenanceJson = "{}", ConflictsJson = "[]", MissingEvidenceJson = "[]", Status = "Approved",
                Revision = 2, CreatedBy = "author", DeduplicationKey = Guid.NewGuid().ToString(),
                ChangeSourceKind = "CspCapability", ChangeSourceId = capabilityId.ToString()
            });
            await db.SaveChangesAsync();
        }
        await using var processorDb = await factory.CreateDbContextAsync();
        var sut = new ProviderReleaseImpactProcessor(processorDb);

        // Act
        var processed = await sut.RunOnceAsync(default);

        // Assert
        processed.Should().Be(1);
        processorDb.ChangeTracker.Clear();
        var updated = await processorDb.ProviderReleaseImpacts.SingleAsync();
        updated.DeliveryState.Should().Be("Delivered");
        updated.DeliveryAttempts.Should().Be(2);
        updated.CustomerReviewState.Should().Be("Accepted");
        updated.NarrativeState.Should().Be("Accepted");
    }

    [Fact]
    public async Task T080_GetWorkingRevisionAsync_ReturnsEditableAndApprovalStateWithoutMutation()
    {
        // Arrange
        var factory = NewFactory();
        var capabilityId = Guid.NewGuid();
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ProviderCapabilityWorkingRevisions.Add(new ProviderCapabilityWorkingRevision
            {
                CapabilityId = capabilityId, Revision = 7, Classification = "CUI",
                ServiceCategory = "Identity", ContributorsJson = "[\"alice\",\"bob\"]",
                DutiesJson = "{\"AC-2\":\"Shared\"}", SnapshotHash = new string('a', 64),
                UpdatedAt = updatedAt, UpdatedBy = "author", ApprovedRevision = 7,
                ApprovedSnapshotHash = new string('b', 64), ApprovedAt = updatedAt,
                ApprovedBy = "reviewer", ApprovedPreviewId = Guid.NewGuid(),
                ApprovedPreviewHash = new string('b', 64)
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var result = await sut.GetWorkingRevisionAsync(capabilityId, default);

        // Assert
        result.Should().NotBeNull();
        result!.Classification.Should().Be("CUI");
        result.ServiceCategory.Should().Be("Identity");
        result.Contributors.Should().Equal("alice", "bob");
        result.ControlDuties.Should().Contain("AC-2", "Shared");
        result.Revision.Should().Be(7);
        result.SnapshotHash.Should().Be(new string('a', 64));
        result.ApprovalState.Should().Be("Approved");
        await using var verify = await factory.CreateDbContextAsync();
        var persisted = await verify.ProviderCapabilityWorkingRevisions.SingleAsync();
        persisted.UpdatedAt.Should().Be(updatedAt);
        persisted.Revision.Should().Be(7);
    }

    [Fact]
    public async Task T081_PublicationPreview_IsRevisionBoundAndRequiredForApprovalAndPublish()
    {
        // Arrange
        var factory = NewFactory();
        var capabilityId = Guid.NewGuid();
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Platform",
            Description = "description", SourceArtifactReference = "provider://platform",
            Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id, Name = "Identity",
                Description = "description", Status = CspInheritedCapabilityStatus.Mapped
            });
            db.CapabilitySubscriptions.AddRange(
                new CapabilitySubscription
                {
                    Id = "preview-sub-1", RoutingTenantId = organizationA,
                    RegisteredSystemId = "system-1", CspInheritedCapabilityId = capabilityId.ToString("D"),
                    RoutingCapabilityId = capabilityId.ToString("D")
                },
                new CapabilitySubscription
                {
                    Id = "preview-sub-2", RoutingTenantId = organizationA,
                    RegisteredSystemId = "system-2", CspInheritedCapabilityId = capabilityId.ToString("D"),
                    RoutingCapabilityId = capabilityId.ToString("D")
                },
                new CapabilitySubscription
                {
                    Id = "preview-sub-3", RoutingTenantId = organizationB,
                    RegisteredSystemId = "system-3", CspInheritedCapabilityId = capabilityId.ToString("D"),
                    RoutingCapabilityId = capabilityId.ToString("D")
                });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var working = await sut.SaveWorkingRevisionAsync(capabilityId,
            new(1, "CUI", "Identity", ["alice"],
                new Dictionary<string, string> { ["AC-2"] = "Shared" }), "author", default);

        // Act
        var preview = await sut.GeneratePublicationPreviewAsync(capabilityId, working.Revision, default);
        var expiredPreview = await sut.GeneratePublicationPreviewAsync(capabilityId, working.Revision, default);
        await using (var expire = await factory.CreateDbContextAsync())
        {
            var row = await expire.ProviderPublicationPreviews.SingleAsync(x => x.Id == expiredPreview.PreviewId);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await expire.SaveChangesAsync();
        }
        Func<Task> expiredApproval = () => sut.ApproveWorkingRevisionAsync(capabilityId,
            new(working.Revision, expiredPreview.PreviewId, expiredPreview.PreviewHash), "reviewer", default);
        var approved = await sut.ApproveWorkingRevisionAsync(capabilityId,
            new(working.Revision, preview.PreviewId, preview.PreviewHash), "reviewer", default);
        var published = await sut.PublishAsync(capabilityId,
            new(working.Revision, working.Revision, preview.PreviewId, preview.PreviewHash, "preview-publish"),
            "publisher", default);
        var next = await sut.SaveWorkingRevisionAsync(capabilityId,
            new(working.Revision, "CUI", "Identity", ["alice", "bob"],
                new Dictionary<string, string> { ["AC-2"] = "Provider" }), "author", default);
        Func<Task> staleApproval = () => sut.ApproveWorkingRevisionAsync(capabilityId,
            new(working.Revision, preview.PreviewId, preview.PreviewHash), "reviewer", default);

        // Assert
        preview.Revision.Should().Be(working.Revision);
        preview.ContributorChanges.Should().ContainSingle(x => x.Value == "alice" && x.ChangeKind == "Added");
        preview.DutyChanges.Should().ContainSingle(x => x.Key == "AC-2" && x.ChangeKind == "Added");
        preview.ReferenceChanges.Should().ContainSingle(x => x.Value == "provider://platform");
        preview.Delivery.DistinctOrganizations.Should().Be(2);
        preview.Delivery.DistinctSystems.Should().Be(3);
        preview.Delivery.ImpactWrites.Should().Be(3);
        preview.AffectedOrganizations.Should().BeEquivalentTo([organizationA, organizationB]);
        preview.AffectedSystems.Should().HaveCount(3);
        preview.Notifications.RecipientCount.Should().Be(3);
        preview.ExpiresAt.Should().BeAfter(preview.GeneratedAt);
        await expiredApproval.Should().ThrowAsync<DbUpdateConcurrencyException>();
        approved.ApprovedPreviewId.Should().Be(preview.PreviewId);
        published.Revision.Should().Be(working.Revision);
        next.Revision.Should().BeGreaterThan(working.Revision);
        await staleApproval.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.ProviderPublicationPreviews.ToListAsync()).Should()
            .OnlyContain(x => x.InvalidatedAt.HasValue);
    }

    [Fact]
    public async Task T086_PublishAsync_RejectsApprovedPreviewWhenReferencesAndTargetsDrift()
    {
        // Arrange
        var factory = NewFactory();
        var capabilityId = Guid.NewGuid();
        var component = new CspInheritedComponent
        {
            Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Platform",
            Description = "description", SourceArtifactReference = "provider://v1",
            Status = CspInheritedComponentStatus.Published
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = capabilityId, CspInheritedComponentId = component.Id, Name = "Identity",
                Description = "description", Status = CspInheritedCapabilityStatus.Mapped
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var working = await sut.SaveWorkingRevisionAsync(capabilityId,
            new(1, "CUI", "Identity", ["alice"],
                new Dictionary<string, string> { ["AC-2"] = "Shared" }), "author", default);
        var preview = await sut.GeneratePublicationPreviewAsync(capabilityId, working.Revision, default);
        await sut.ApproveWorkingRevisionAsync(capabilityId,
            new(working.Revision, preview.PreviewId, preview.PreviewHash), "reviewer", default);
        await using (var drift = await factory.CreateDbContextAsync())
        {
            (await drift.CspInheritedComponents.SingleAsync()).SourceArtifactReference = "provider://v2";
            drift.CapabilitySubscriptions.Add(new CapabilitySubscription
            {
                Id = "late-subscription", RoutingTenantId = Guid.NewGuid(),
                RegisteredSystemId = "late-system", CspInheritedCapabilityId = capabilityId.ToString("D"),
                RoutingCapabilityId = capabilityId.ToString("D")
            });
            await drift.SaveChangesAsync();
        }

        // Act
        Func<Task> publish = () => sut.PublishAsync(capabilityId,
            new(working.Revision, working.Revision, preview.PreviewId, preview.PreviewHash, "drifted"),
            "publisher", default);

        // Assert
        await publish.Should().ThrowAsync<DbUpdateConcurrencyException>()
            .WithMessage("*preview inputs*changed*");
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.ProviderCapabilityReleases.CountAsync()).Should().Be(0);
        (await verify.ProviderReleaseImpacts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task T087_CreateOrganizationAsync_ConcurrentEquivalentNamesHaveOneWinner()
    {
        // Arrange
        var databasePath = Path.Combine(AppContext.BaseDirectory, "TestResults",
            $"workspace-org-create-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var interceptor = new OrganizationNameReadBarrierInterceptor();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=30")
            .AddInterceptors(interceptor).Options;
        IDbContextFactory<AtoCopilotContext> factory = new TestFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();
        var first = new WorkspaceOperationsService(factory);
        var second = new WorkspaceOperationsService(factory);

        // Act
        var attempts = await Task.WhenAll(
            Record.ExceptionAsync(() => first.CreateOrganizationAsync(
                new("Mission Alpha", null, null, null), "create-alpha-1", "admin", default)),
            Record.ExceptionAsync(() => second.CreateOrganizationAsync(
                new(" mission alpha ", null, null, null), "create-alpha-2", "admin", default)));

        // Assert
        attempts.Count(x => x is null).Should().Be(1);
        attempts.Count(x => x is InvalidOperationException).Should().Be(1);
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.Tenants.CountAsync()).Should().Be(1);
        (await verify.OrganizationNameReservations.CountAsync()).Should().Be(1);
        await verify.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }

    [Fact]
    public async Task T082_CreateOrganizationAsync_ReplaysSameIntentAndRejectsChangedIntent()
    {
        // Arrange
        var factory = NewFactory();
        var sut = new WorkspaceOperationsService(factory);
        var request = new Ato.Copilot.Core.Interfaces.Workspaces.CreateWorkspaceOrganizationRequest(
            "Mission Alpha", "Alpha LLC", "Owner", "owner@example.mil");

        // Act
        var created = await sut.CreateOrganizationAsync(request, "create-alpha", "provider-admin", default);
        var replay = await sut.CreateOrganizationAsync(request, "create-alpha", "provider-admin", default);
        var status = await sut.GetProvisioningAsync(created.TenantId, "create-alpha", default);
        Func<Task> changedIntent = () => sut.CreateOrganizationAsync(
            request with { DisplayName = "Mission Beta" }, "create-alpha", "provider-admin", default);

        // Assert
        created.Existing.Should().BeFalse();
        replay.Existing.Should().BeTrue();
        replay.TenantId.Should().Be(created.TenantId);
        replay.OperationId.Should().Be(created.OperationId);
        status.Should().NotBeNull();
        status!.OperationId.Should().Be(created.OperationId);
        await changedIntent.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different organization creation intent*");
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.Tenants.CountAsync()).Should().Be(1);
        (await verify.OrganizationProvisioningOperations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task T083_CompleteSetupAsync_InlineLocalCreationIsDurableAndIdempotent()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString("D");
        var componentId = Guid.NewGuid().ToString("D");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Mission" });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "System", CreatedBy = "owner"
            });
            db.SystemComponents.Add(new SystemComponent
            {
                TenantId = tenantId, Id = componentId, RegisteredSystemId = systemId,
                Name = "Entra", ComponentType = ComponentType.Thing, CreatedBy = "owner"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var request = new Ato.Copilot.Core.Interfaces.Workspaces.CompleteCapabilitySetupRequest(
            "inline-local", "local", string.Empty, systemId, [componentId], false,
            new("MFA", "Microsoft", "IA", "Multi-factor authentication", "Implemented", "ISSO"));

        // Act
        var created = await sut.CompleteSetupAsync(tenantId, request, "owner", [systemId], default);
        var replay = await sut.CompleteSetupAsync(tenantId, request, "owner", [systemId], default);

        // Assert
        replay.OperationId.Should().Be(created.OperationId);
        created.Outcomes.Should().ContainSingle(x =>
            x.WriteKind == "record-create" && x.State == "Completed");
        created.Outcomes.Should().ContainSingle(x =>
            x.WriteKind == "system-link" && x.WriteId == systemId && x.State == "Completed");
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.SecurityCapabilities.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await verify.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await verify.SystemCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        var operation = await verify.CapabilitySetupOperations.IgnoreQueryFilters().SingleAsync();
        operation.LocalCapabilityJson.Should().Contain("\"MFA\"");
        operation.SourceRecordId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task T096_PrepareRefreshComplete_PersistsIntentWithoutEarlyWrites()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString("D");
        var componentId = Guid.NewGuid().ToString("D");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Prepared Mission" });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "Prepared System", CreatedBy = "owner"
            });
            db.SystemComponents.Add(new SystemComponent
            {
                TenantId = tenantId, Id = componentId, RegisteredSystemId = systemId,
                Name = "Prepared Component", ComponentType = ComponentType.Thing, CreatedBy = "owner"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var request = new PrepareCapabilitySetupRequest(
            "prepared-local", "local", string.Empty, systemId, [componentId], false,
            new("MFA", "Microsoft", "IA", "Prepared MFA", "Implemented", "ISSM"));

        // Act
        var prepared = await sut.PrepareSetupAsync(tenantId, request, [systemId], default);
        var refreshed = await sut.GetCapabilitySetupAsync(
            tenantId, prepared.Operation.OperationId, default);
        int capabilitiesBefore;
        int componentLinksBefore;
        int systemLinksBefore;
        await using (var verifyPrepared = await factory.CreateDbContextAsync())
        {
            capabilitiesBefore = await verifyPrepared.SecurityCapabilities.IgnoreQueryFilters().CountAsync();
            componentLinksBefore = await verifyPrepared.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync();
            systemLinksBefore = await verifyPrepared.SystemCapabilityLinks.IgnoreQueryFilters().CountAsync();
        }
        var completed = await sut.CompleteSetupAsync(
            tenantId,
            new(request.IdempotencyKey, request.Source, prepared.Operation.RecordId,
                request.SystemId, request.ComponentIds, request.Subscribe,
                request.InlineLocalCapability, prepared.Operation.OperationId),
            "owner", [systemId], default);

        // Assert
        prepared.Existing.Should().BeFalse();
        refreshed.Should().BeEquivalentTo(prepared.Operation);
        capabilitiesBefore.Should().Be(0);
        componentLinksBefore.Should().Be(0);
        systemLinksBefore.Should().Be(0);
        prepared.Operation.Outcomes.Should().OnlyContain(x => x.State == "Pending");
        completed.OperationId.Should().Be(prepared.Operation.OperationId);
        await using var verifyCompleted = await factory.CreateDbContextAsync();
        (await verifyCompleted.SecurityCapabilities.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await verifyCompleted.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await verifyCompleted.SystemCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task T097_PrepareSetupAsync_ReplaysIntentAndRevalidatesTenantAtCommit()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString("D");
        var capabilityId = Guid.NewGuid().ToString("D");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.AddRange(
                new Tenant { Id = tenantId, DisplayName = "Mission A" },
                new Tenant { Id = otherTenantId, DisplayName = "Mission B" });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "System A", CreatedBy = "owner"
            });
            db.SecurityCapabilities.Add(new SecurityCapability
            {
                TenantId = tenantId, Id = capabilityId, Name = "Existing",
                Provider = "Mission", Category = "SOFTWARE", Description = "Existing",
                Owner = "owner", CreatedBy = "owner"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var request = new PrepareCapabilitySetupRequest(
            "prepare-replay", "local", capabilityId, systemId, [], false);

        // Act
        var prepared = await sut.PrepareSetupAsync(tenantId, request, [systemId], default);
        var replay = await sut.PrepareSetupAsync(tenantId, request, [systemId], default);
        Func<Task> changed = () => sut.PrepareSetupAsync(
            tenantId, request with { Subscribe = true }, [systemId], default);
        await using (var mutate = await factory.CreateDbContextAsync())
        {
            var capability = await mutate.SecurityCapabilities.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == capabilityId);
            capability.TenantId = otherTenantId;
            await mutate.SaveChangesAsync();
        }
        Func<Task> complete = () => sut.CompleteSetupAsync(
            tenantId,
            new(request.IdempotencyKey, request.Source, request.RecordId, request.SystemId,
                request.ComponentIds, request.Subscribe, null, prepared.Operation.OperationId),
            "owner", [systemId], default);

        // Assert
        replay.Existing.Should().BeTrue();
        replay.Operation.OperationId.Should().Be(prepared.Operation.OperationId);
        await changed.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different setup*");
        await complete.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Capability was not found*");
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await verify.SystemCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task T098_PrepareSetupAsync_RemovesAbandonedPreparationsAfterSevenDays()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString("D");
        var abandonedId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Retention Mission" });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "Retention System", CreatedBy = "owner"
            });
            db.CapabilitySetupOperations.Add(new CapabilitySetupOperation
            {
                Id = abandonedId, TenantId = tenantId, IdempotencyKey = "abandoned",
                SourceKind = "local", SourceRecordId = Guid.NewGuid().ToString("D"),
                RegisteredSystemId = systemId,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-8),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-8)
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        await sut.PrepareSetupAsync(
            tenantId,
            new("current", "local", string.Empty, systemId, [], false,
                new("Current", "Mission", "IA", "Current", "Implemented", "owner")),
            [systemId], default);

        // Assert
        await using var verify = await factory.CreateDbContextAsync();
        (await verify.CapabilitySetupOperations.IgnoreQueryFilters()
            .AnyAsync(x => x.Id == abandonedId)).Should().BeFalse();
        (await verify.CapabilitySetupOperations.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task T100_CompletionClaimsBeforeValidationAndClaimedRetryDoesNotDuplicateWrites()
    {
        // Arrange
        var factory = NewFactory();
        var tenantId = Guid.NewGuid();
        var systemId = Guid.NewGuid().ToString("D");
        var cleanupSystemId = Guid.NewGuid().ToString("D");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, DisplayName = "Claim Mission" });
            db.RegisteredSystems.AddRange(
                new RegisteredSystem
                {
                    TenantId = tenantId, Id = systemId, Name = "Claim System", CreatedBy = "owner"
                },
                new RegisteredSystem
                {
                    TenantId = tenantId, Id = cleanupSystemId, Name = "Cleanup System", CreatedBy = "owner"
                });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);
        var request = new PrepareCapabilitySetupRequest(
            "claimed-setup", "local", string.Empty, systemId, [], false,
            new("Claimed MFA", "Mission", "IA", "Claim race", "Implemented", "owner"));
        var prepared = await sut.PrepareSetupAsync(tenantId, request, [systemId], default);
        await using (var ageAndInvalidate = await factory.CreateDbContextAsync())
        {
            var operation = await ageAndInvalidate.CapabilitySetupOperations
                .IgnoreQueryFilters().SingleAsync(x => x.Id == prepared.Operation.OperationId);
            operation.CreatedAt = DateTimeOffset.UtcNow.AddDays(-8);
            operation.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-8);
            var system = await ageAndInvalidate.RegisteredSystems.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == systemId);
            ageAndInvalidate.RegisteredSystems.Remove(system);
            await ageAndInvalidate.SaveChangesAsync();
        }
        var completion = new CompleteCapabilitySetupRequest(
            request.IdempotencyKey, request.Source, prepared.Operation.RecordId,
            request.SystemId, request.ComponentIds, request.Subscribe,
            request.InlineLocalCapability, prepared.Operation.OperationId);

        // Act
        Func<Task> invalid = () => sut.CompleteSetupAsync(
            tenantId, completion, "owner", [systemId], default);
        await invalid.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*System was not found*");
        await using (var reAge = await factory.CreateDbContextAsync())
        {
            var operation = await reAge.CapabilitySetupOperations.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == prepared.Operation.OperationId);
            operation.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-8);
            await reAge.SaveChangesAsync();
        }
        await sut.PrepareSetupAsync(
            tenantId,
            new("cleanup-trigger", "local", string.Empty, cleanupSystemId, [], false,
                new("Cleanup", "Mission", "IA", "Cleanup", "Implemented", "owner")),
            [cleanupSystemId], default);
        await using (var restore = await factory.CreateDbContextAsync())
        {
            restore.RegisteredSystems.Add(new RegisteredSystem
            {
                TenantId = tenantId, Id = systemId, Name = "Claim System", CreatedBy = "owner"
            });
            await restore.SaveChangesAsync();
        }
        var completed = await sut.CompleteSetupAsync(
            tenantId, completion, "owner", [systemId], default);
        var replay = await sut.CompleteSetupAsync(
            tenantId, completion, "owner", [systemId], default);

        // Assert
        replay.OperationId.Should().Be(completed.OperationId);
        await using var verify = await factory.CreateDbContextAsync();
        var persisted = await verify.CapabilitySetupOperations.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == prepared.Operation.OperationId);
        persisted.ClaimedAt.Should().NotBeNull();
        persisted.ExecutionClaimId.Should().BeNull();
        persisted.Revision.Should().BeGreaterThan(0);
        (await verify.SecurityCapabilities.IgnoreQueryFilters()
            .CountAsync(x => x.Id == prepared.Operation.RecordId)).Should().Be(1);
        (await verify.SystemCapabilityLinks.IgnoreQueryFilters()
            .CountAsync(x => x.RegisteredSystemId == systemId
                && x.SecurityCapabilityId == prepared.Operation.RecordId)).Should().Be(1);
    }

    private static IDbContextFactory<AtoCopilotContext> NewFactory()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"workspace-{Guid.NewGuid():N}").Options;
        return new TestFactory(options);
    }

    private sealed class TestFactory(DbContextOptions<AtoCopilotContext> options)
        : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
    }

    private sealed class SetupReadBarrierInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("CapabilitySetupOperations", StringComparison.Ordinal)
                && command.CommandText.Contains("IdempotencyKey", StringComparison.Ordinal)
                && Interlocked.Increment(ref _reads) <= 2)
            {
                if (_reads == 2) _release.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class OrganizationNameReadBarrierInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Tenants\"", StringComparison.Ordinal)
                && command.CommandText.Contains("DisplayName", StringComparison.Ordinal)
                && Interlocked.Increment(ref _reads) <= 2)
            {
                if (_reads == 2) _release.TrySetResult();
                await _release.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class ProvisioningSaveBarrierInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var bindsIdentity = eventData.Context?.ChangeTracker
                .Entries<OrganizationProvisioningOperation>()
                .Any(x => x.State == EntityState.Modified && x.Property(y => y.PersonId).IsModified) == true;
            if (bindsIdentity && Interlocked.Increment(ref _reads) <= 2)
            {
                if (_reads == 2) _release.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

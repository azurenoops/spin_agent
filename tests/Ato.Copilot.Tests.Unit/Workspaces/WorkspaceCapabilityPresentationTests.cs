using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Workspaces;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Workspaces;

public sealed class WorkspaceCapabilityPresentationTests
{
    [Fact]
    public async Task Provider_ProjectsPublishedParentAndMappedControlsWithoutInventingReview()
    {
        // Arrange
        var factory = NewFactory();
        var tenant = Guid.NewGuid();
        var capability = Provider(["AC-2", "ac-2", " IA-2 ", ""]);
        await using (var db = factory.CreateDbContext())
        {
            db.Tenants.Add(new Tenant { Id = tenant, DisplayName = "Organization" });
            db.CspProfiles.Add(new CspProfile
            {
                Id = capability.CspInheritedComponent.CspProfileId, DisplayName = "Hosting provider",
                LegalEntityName = "Hosting provider"
            });
            db.CspInheritedCapabilities.Add(capability);
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var list = await sut.ListOrganizationCapabilitiesAsync(
            tenant, new(Grouping: "capability"), "provider", null, [], default);
        var detail = await sut.GetOrganizationCapabilityAsync(
            tenant, "provider", capability.Id.ToString().ToUpperInvariant(), null, [], default, "capability");

        // Assert
        var item = list.Items.Should().ContainSingle().Subject;
        item.RecordType.Should().Be("capability");
        item.SupportingComponents.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new SupportingComponentSummary(capability.CspInheritedComponentId.ToString(), "Platform",
                capability.CspInheritedComponent.ComponentType.ToString(), "provider", "Published platform"));
        item.ControlCount.Should().Be(2);
        item.ReviewState.Should().Be("ReviewRequired");
        item.Responsibility.Should().Be("Undesignated");
        item.SourceName.Should().Be("Hosting provider");
        detail!.SupportingComponents.Should().BeEquivalentTo(item.SupportingComponents);
        detail.ProviderName.Should().Be("Hosting provider");
        detail.SourceReference.Should().Be("provider-ssp.pdf");
        detail.ControlCoverage.Should().BeEquivalentTo(new[]
        {
            new CapabilityControlCoverage("AC-2", "Undesignated"),
            new CapabilityControlCoverage("IA-2", "Undesignated")
        });
    }

    [Fact]
    public async Task SubscriptionAndApprovedNarrative_DoNotImplyResponsibilityOrCompletedReview()
    {
        // Arrange
        var factory = NewFactory();
        var tenant = Guid.NewGuid();
        var capability = Provider(["AC-2"]);
        capability.ReviewedAt = DateTimeOffset.UtcNow;
        capability.ReviewedBy = "provider-reviewer";
        await using (var db = factory.CreateDbContext())
        {
            db.Tenants.Add(new Tenant { Id = tenant, DisplayName = "Organization" });
            db.CspInheritedCapabilities.Add(capability);
            db.CapabilitySubscriptions.Add(Subscription(tenant, capability.Id, "readable"));
            db.NarrativeProposals.Add(new NarrativeProposal
            {
                TenantId = tenant, RegisteredSystemId = "readable", ControlId = "AC-2",
                ChangeSourceKind = "CspCapability", ChangeSourceId = capability.Id.ToString(),
                Status = "Approved", CreatedBy = "reviewer", DeduplicationKey = "narrative"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var detail = await sut.GetOrganizationCapabilityAsync(
            tenant, "provider", capability.Id.ToString(), "readable", ["readable"], default);

        // Assert
        detail!.Capability.IsSubscribed.Should().BeTrue();
        detail.Capability.ReviewState.Should().Be("ReviewRequired");
        detail.Capability.Responsibility.Should().Be("Undesignated");
        detail.Responsibilities.Should().BeEmpty();
        detail.NarrativeReviews.Should().ContainSingle(x => x.Status == "Approved");
        detail.ControlCoverage.Should().ContainSingle().Which.Should().Be(
            new CapabilityControlCoverage("AC-2", "Undesignated", null, "readable"));
    }

    [Theory]
    [InlineData(false, false, "Shared", "Reviewed")]
    [InlineData(true, false, "Undesignated", "ReviewRequired")]
    [InlineData(false, true, "Undesignated", "ReviewRequired")]
    public async Task ProviderCoverage_UsesOnlyCurrentSourceAndBaselineConfirmation(
        bool staleSource, bool staleBaseline, string designation, string reviewState)
    {
        // Arrange
        var factory = NewFactory();
        var tenant = Guid.NewGuid();
        var capability = Provider(["AC-2"]);
        var subscription = Subscription(tenant, capability.Id, "readable");
        await using (var db = factory.CreateDbContext())
        {
            db.Tenants.Add(new Tenant { Id = tenant, DisplayName = "Organization" });
            db.CspInheritedCapabilities.Add(capability);
            db.CapabilitySubscriptions.Add(subscription);
            db.ControlBaselines.Add(new ControlBaseline
            {
                Id = "current-baseline", TenantId = tenant, RegisteredSystemId = "readable",
                ControlIds = ["AC-2"], BaselineLevel = "Low", CreatedBy = "reviewer"
            });
            db.Set<CapabilityResponsibilityConfirmation>().Add(new()
            {
                TenantId = tenant, RegisteredSystemId = "readable", SubscriptionId = subscription.Id,
                ReviewedBaselineId = staleBaseline ? "old-baseline" : "current-baseline", ControlId = "AC-2",
                SourceRevision = staleSource ? "old-source"
                    : CspResponsibilitySourceTracker.Revision(CspResponsibilitySourceTracker.Snapshot(capability)),
                InheritanceType = InheritanceType.Shared, Provider = "Hosting provider",
                CustomerResponsibility = "Review privileged assignments", ConfirmedBy = "reviewer"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var list = await sut.ListOrganizationCapabilitiesAsync(
            tenant, new(Grouping: "capability"), "provider", "readable", ["readable"], default);
        var detail = await sut.GetOrganizationCapabilityAsync(
            tenant, "provider", capability.Id.ToString(), "readable", ["readable"], default);

        // Assert
        list.Items.Single().ReviewState.Should().Be(reviewState);
        list.Items.Single().Responsibility.Should().Be(designation);
        detail!.Capability.ReviewState.Should().Be(reviewState);
        detail.ControlCoverage.Should().ContainSingle().Which.Should().Be(new CapabilityControlCoverage(
            "AC-2", designation, designation == "Shared" ? "Review privileged assignments" : null, "readable"));
    }

    [Fact]
    public async Task ProviderCoverage_ExcludesForeignUnreadableInactiveAndMismatchedConfirmations()
    {
        // Arrange
        var factory = NewFactory();
        var tenant = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        var capability = Provider(["AC-2", "IA-2"]);
        await using (var db = factory.CreateDbContext())
        {
            db.CspInheritedCapabilities.Add(capability);
            foreach (var (owner, system, active) in new[]
            {
                (tenant, "readable", true), (tenant, "denied", true),
                (foreignTenant, "readable", true), (tenant, "inactive", false)
            })
            {
                var sub = Subscription(owner, capability.Id, system);
                sub.IsActive = active;
                db.CapabilitySubscriptions.Add(sub);
                db.Set<CapabilityResponsibilityConfirmation>().Add(new()
                {
                    TenantId = owner, RegisteredSystemId = system, SubscriptionId = sub.Id,
                    ReviewedBaselineId = "baseline", ControlId = "AC-2", InheritanceType = InheritanceType.Shared,
                    CustomerResponsibility = "Must not leak", SourceRevision = "old", ConfirmedBy = "reviewer"
                });
            }
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var detail = await sut.GetOrganizationCapabilityAsync(
            tenant, "provider", capability.Id.ToString(), "readable", ["readable"], default);
        var denied = await sut.GetOrganizationCapabilityAsync(
            tenant, "provider", capability.Id.ToString(), "denied", ["readable"], default);

        // Assert
        denied.Should().BeNull();
        detail!.Capability.SystemCount.Should().Be(1);
        detail.ControlCoverage.Should().HaveCount(2).And.OnlyContain(
            x => x.SystemId == "readable" && x.Designation == "Undesignated" && x.RemainingDuty == null);
    }

    [Fact]
    public async Task Local_ProjectsOnlyTenantAndReadableSystemComponentsAndMappings()
    {
        // Arrange
        var factory = NewFactory();
        var tenant = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Tenants.Add(new Tenant { Id = tenant, DisplayName = "Organization" });
            db.SecurityCapabilities.Add(new SecurityCapability
            {
                Id = "local", TenantId = tenant, Name = "Local MFA", Provider = "Local vendor",
                Category = "IA", Description = "Local capability", CreatedBy = "owner"
            });
            db.SystemCapabilityLinks.Add(new SystemCapabilityLink
            {
                TenantId = tenant, RegisteredSystemId = "readable", SecurityCapabilityId = "local"
            });
            foreach (var (id, owner, system, linkOwner) in new[]
            {
                ("visible", tenant, "readable", tenant),
                ("denied", tenant, "denied", tenant),
                ("foreign", foreign, "readable", tenant),
                ("foreign-link", tenant, "readable", foreign),
                ("org-component", tenant, (string?)null, tenant)
            })
            {
                db.SystemComponents.Add(new SystemComponent
                {
                    Id = id, TenantId = owner, RegisteredSystemId = system,
                    Name = id, Description = "Inventory item", ComponentType = ComponentType.Thing, CreatedBy = "owner"
                });
                db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
                {
                    TenantId = linkOwner, SystemComponentId = id, SecurityCapabilityId = "local"
                });
            }
            foreach (var (owner, control, system) in new[]
            {
                (tenant, "AC-2", (string?)null), (tenant, "IA-2", "readable"),
                (tenant, "AC-3", "denied"), (foreign, "AC-4", "readable")
            })
                db.CapabilityControlMappings.Add(new CapabilityControlMapping
                {
                    TenantId = owner, SecurityCapabilityId = "local", ControlId = control,
                    RegisteredSystemId = system, Role = CapabilityMappingRole.Shared, CreatedBy = "owner"
                });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var list = await sut.ListOrganizationCapabilitiesAsync(
            tenant, new(Grouping: "capability"), "local", "readable", ["readable"], default);
        var detail = await sut.GetOrganizationCapabilityAsync(tenant, "local", "local", "readable", ["readable"], default);

        // Assert
        var item = list.Items.Should().ContainSingle().Subject;
        item.SupportingComponents.Should().ContainSingle(x => x.Id == "visible");
        item.ControlCount.Should().Be(2);
        item.ReviewState.Should().Be("ReviewRequired");
        item.Responsibility.Should().Be("Undesignated", "a mapping role is not an inheritance designation");
        item.SourceName.Should().Be("Local vendor");
        detail!.ProviderName.Should().Be("Local vendor");
        detail.SourceReference.Should().BeNull();
        detail.SupportingComponents.Should().BeEquivalentTo(item.SupportingComponents);
        detail.ControlCoverage.Should().BeEquivalentTo(new[]
        {
            new CapabilityControlCoverage("AC-2", "Undesignated"),
            new CapabilityControlCoverage("IA-2", "Undesignated", null, "readable")
        });
    }

    [Fact]
    public async Task LocalWithoutComponentsOrMappings_ReturnsExplicitEmptyData()
    {
        // Arrange
        var factory = NewFactory();
        var tenant = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.SecurityCapabilities.Add(new SecurityCapability
            {
                Id = "empty", TenantId = tenant, Name = "Empty", CreatedBy = "owner"
            });
            db.SystemCapabilityLinks.Add(new SystemCapabilityLink
            {
                TenantId = tenant, RegisteredSystemId = "readable", SecurityCapabilityId = "empty"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var detail = await sut.GetOrganizationCapabilityAsync(tenant, "local", "empty", "readable", ["readable"], default);

        // Assert
        detail!.Capability.ControlCount.Should().Be(0);
        detail.SupportingComponents.Should().BeEmpty();
        detail.ControlCoverage.Should().BeEmpty();
        detail.Capability.SourceName.Should().BeNull();
        detail.ProviderName.Should().BeNull();
        detail.SourceReference.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sqlite_EnrichmentPreservesMixedPagingAndUppercaseSubscriptionMatching(bool confirmed)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new TestFactory(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        var tenant = Guid.NewGuid();
        var capability = Provider(confirmed ? ["AC-2"] : []);
        capability.Name = "A provider";
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Tenants.Add(new Tenant { Id = tenant, DisplayName = "Organization" });
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                Id = "readable", TenantId = tenant, Name = "System", CreatedBy = "owner"
            });
            db.CspProfiles.Add(new CspProfile
            {
                Id = capability.CspInheritedComponent.CspProfileId, DisplayName = "Provider", LegalEntityName = "Provider"
            });
            db.CspInheritedCapabilities.Add(capability);
            var subscription = Subscription(tenant, capability.Id, "readable");
            db.CapabilitySubscriptions.Add(subscription);
            if (confirmed)
            {
                db.ControlBaselines.Add(new ControlBaseline
                {
                    Id = "baseline", TenantId = tenant, RegisteredSystemId = "readable",
                    ControlIds = ["AC-2"], BaselineLevel = "Low", CreatedBy = "owner"
                });
                foreach (var revision in new[] { 1, 2 })
                    db.ProviderCapabilityReleases.Add(new ProviderCapabilityRelease
                    {
                        CapabilityId = capability.Id, Revision = revision, SnapshotHash = $"release-{revision}",
                        SnapshotJson = """{"Capability":{"Component":{}}}""",
                        IdempotencyKey = $"release-{revision}", PublishedBy = "provider"
                    });
                db.Set<CapabilityResponsibilityConfirmation>().Add(new()
                {
                    TenantId = tenant, RegisteredSystemId = "readable", SubscriptionId = subscription.Id,
                    ReviewedBaselineId = "baseline", ControlId = "AC-2", InheritanceType = InheritanceType.Inherited,
                    Provider = "Provider", SourceRevision = "release-2", ConfirmedBy = "reviewer"
                });
            }
            db.SecurityCapabilities.Add(new SecurityCapability
            {
                Id = "local", TenantId = tenant, Name = "B local", CreatedBy = "owner"
            });
            db.SystemCapabilityLinks.Add(new SystemCapabilityLink
            {
                TenantId = tenant, RegisteredSystemId = "readable", SecurityCapabilityId = "local"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var page = await sut.ListOrganizationCapabilitiesAsync(
            tenant, new(Page: 1, PageSize: 1, Grouping: "capability"), null, "readable", ["readable"], default);
        var second = await sut.ListOrganizationCapabilitiesAsync(
            tenant, new(Page: 2, PageSize: 1, Grouping: "capability"), null, "readable", ["readable"], default);
        var detail = await sut.GetOrganizationCapabilityAsync(
            tenant, "provider", capability.Id.ToString("N").ToUpperInvariant(), "readable", ["readable"], default);

        // Assert
        page.Total.Should().Be(2);
        page.Items.Should().ContainSingle(x => x.Source == "provider" && x.IsSubscribed);
        page.Items.Single().SupportingComponents.Should().ContainSingle();
        page.Items.Single().ControlCount.Should().Be(confirmed ? 1 : 0);
        second.Items.Should().ContainSingle(x => x.Source == "local");
        second.Items.Single().SupportingComponents.Should().BeEmpty();
        if (confirmed)
        {
            detail!.ControlCoverage.Should().ContainSingle().Which.Should().Be(
                new CapabilityControlCoverage("AC-2", "Inherited", null, "readable"));
            detail.Capability.ReviewState.Should().Be("Reviewed");
        }
        else detail!.ControlCoverage.Should().BeEmpty();
    }

    [Fact]
    public async Task ProviderList_EnrichmentUsesFixedQueryCountForCurrentPage()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var reads = new ReadCounter();
        var factory = new TestFactory(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(connection).AddInterceptors(reads).Options);
        var tenant = Guid.NewGuid();
        var provider = new CspProfile { DisplayName = "Provider", LegalEntityName = "Provider" };
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Tenants.Add(new Tenant { Id = tenant, DisplayName = "Organization" });
            db.CspProfiles.Add(provider);
            for (var i = 0; i < 25; i++)
            {
                var capability = Provider(["AC-2"]);
                capability.Name = $"Capability {i:D2}";
                capability.CspInheritedComponent.CspProfileId = provider.Id;
                db.CspInheritedCapabilities.Add(capability);
            }
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        reads.Count = 0;
        var first = await sut.ListOrganizationCapabilitiesAsync(
            tenant, new(PageSize: 1, Grouping: "capability"), "provider", null, [], default);
        var oneRowQueries = reads.Count;
        reads.Count = 0;
        var page = await sut.ListOrganizationCapabilitiesAsync(
            tenant, new(PageSize: 25, Grouping: "capability"), "provider", null, [], default);

        // Assert
        first.Items.Should().ContainSingle();
        first.Total.Should().Be(25);
        page.Items.Should().HaveCount(25).And.OnlyContain(x =>
            x.SupportingComponents != null && x.SupportingComponents.Count == 1 && x.ControlCount == 1);
        reads.Count.Should().Be(oneRowQueries, "enrichment batches the page instead of querying each capability");
    }

    [Theory]
    [InlineData("Draft", "readable", "ReviewRequired")]
    [InlineData("Approved", "readable", "Reviewed")]
    [InlineData("Draft", "denied", "Reviewed")]
    public async Task ProviderReview_KeepsNarrativeApprovalSeparateAndScoped(
        string narrativeStatus, string narrativeSystem, string reviewState)
    {
        // Arrange
        var factory = NewFactory();
        var tenant = Guid.NewGuid();
        var capability = Provider(["AC-2"]);
        var subscription = Subscription(tenant, capability.Id, "readable");
        await using (var db = factory.CreateDbContext())
        {
            db.CspInheritedCapabilities.Add(capability);
            db.CapabilitySubscriptions.Add(subscription);
            db.ControlBaselines.Add(new ControlBaseline
            {
                Id = "baseline", TenantId = tenant, RegisteredSystemId = "readable",
                ControlIds = ["AC-2"], BaselineLevel = "Low", CreatedBy = "reviewer"
            });
            db.Set<CapabilityResponsibilityConfirmation>().Add(new()
            {
                TenantId = tenant, RegisteredSystemId = "readable", SubscriptionId = subscription.Id,
                ReviewedBaselineId = "baseline", ControlId = "AC-2", InheritanceType = InheritanceType.Customer,
                CustomerResponsibility = "Operate the control",
                SourceRevision = CspResponsibilitySourceTracker.Revision(CspResponsibilitySourceTracker.Snapshot(capability)),
                ConfirmedBy = "reviewer"
            });
            db.NarrativeProposals.Add(new NarrativeProposal
            {
                TenantId = tenant, RegisteredSystemId = narrativeSystem, ControlId = "AC-2",
                ChangeSourceKind = "CspCapability", ChangeSourceId = capability.Id.ToString("N").ToUpperInvariant(),
                Status = narrativeStatus, CreatedBy = "author", DeduplicationKey = "proposal"
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(factory);

        // Act
        var detail = await sut.GetOrganizationCapabilityAsync(
            tenant, "provider", capability.Id.ToString(), "readable", ["readable"], default);

        // Assert
        detail!.Capability.ReviewState.Should().Be(reviewState);
        detail.Capability.Responsibility.Should().Be("Customer");
        detail.ControlCoverage.Should().ContainSingle().Which.RemainingDuty.Should().Be("Operate the control");
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }

    private static CspInheritedCapability Provider(List<string> controls)
    {
        var component = new CspInheritedComponent
        {
            CspProfileId = Guid.NewGuid(), Name = "Platform", Description = "Published platform",
            Status = CspInheritedComponentStatus.Published, SourceFileName = "provider-ssp.pdf",
            SourceArtifactReference = "https://storage.example/source?sig=private"
        };
        return new CspInheritedCapability
        {
            CspInheritedComponentId = component.Id, CspInheritedComponent = component,
            Name = "Provider capability", Description = "Mapped capability",
            Status = CspInheritedCapabilityStatus.Mapped, MappedNistControlIds = controls
        };
    }

    private static CapabilitySubscription Subscription(Guid tenant, Guid capability, string system) => new()
    {
        RoutingTenantId = tenant, CspInheritedCapabilityId = capability.ToString().ToUpperInvariant(),
        RegisteredSystemId = system
    };

    private static TestFactory NewFactory() => new(new DbContextOptionsBuilder<AtoCopilotContext>()
        .UseInMemoryDatabase($"capability-presentation-{Guid.NewGuid():N}").Options);

    private sealed class TestFactory(DbContextOptions<AtoCopilotContext> options) : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
    }
}

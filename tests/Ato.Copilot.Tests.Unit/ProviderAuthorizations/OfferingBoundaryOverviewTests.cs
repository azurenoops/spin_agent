using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class OfferingBoundaryOverviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkedCapabilities_DeduplicateByIdentity_KeepExactBoundariesAndStates(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        var source = await fixture.AddCandidateAsync("Shared name");
        var published = await fixture.AddCandidateAsync("Shared name", "Published");
        var canonical = await fixture.AddPublishedAsync("Shared name", published);
        await fixture.AddPublishedAsync("Independent canonical");
        await fixture.AddCandidateAsync("Rejected proposal", "Rejected");
        await fixture.AddCandidateAsync("Not a capability", type: "Component");
        await fixture.AddCandidateAsync("Not linked", linked: false);
        await fixture.AddCandidateAsync("Other offering", offeringId: fixture.OtherOffering);
        var approved = await fixture.AddCandidateAsync("Approved proposal", "Approved");

        // Act
        var result = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        result.OfferingRevision.Should().Be(7);
        result.Capabilities.Total.Should().Be(4);
        result.Capabilities.AwaitingReview.Should().Be(1);
        result.Capabilities.Published.Should().Be(2);
        result.Capabilities.Items.Count(x => x.Name == "Shared name").Should().Be(2);
        result.Capabilities.Items.Single(x => x.CandidateId == source.Id).Should().BeEquivalentTo(new
        {
            CapabilityId = (Guid?)null, PackageId = (Guid?)source.PackageId, ReviewState = "NeedsReview",
            PublicationState = "Unpublished", BoundaryRevisionId = (Guid?)fixture.SourceBoundary
        });
        result.Capabilities.Items.Single(x => x.CapabilityId == canonical.CapabilityId).Should().BeEquivalentTo(new
        {
            CandidateId = (Guid?)published.Id, PackageId = (Guid?)published.PackageId,
            ReviewState = "Published", PublicationState = "Published", ReleaseId = (Guid?)canonical.ReleaseId,
            BoundaryRevisionId = (Guid?)fixture.SourceBoundary
        });
        result.Capabilities.Items.Single(x => x.CandidateId == approved.Id).PublicationState.Should().Be("Unpublished");
        fixture.PackageService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublishedContexts_SelectLatestLinkedRelease_NotLatestOtherOffering_AndLabelArchived()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.AddPublishedAsync("Canonical");
        var latest = await fixture.AddPublishedAsync("Canonical", capabilityId: first.CapabilityId, revision: 2);
        await fixture.AddPublishedAsync("Canonical", capabilityId: first.CapabilityId, revision: 3, offeringId: fixture.OtherOffering);
        var archived = await fixture.AddPublishedAsync("Archived", status: CspInheritedCapabilityStatus.Archived);
        await using (var db = fixture.Db())
        {
            db.Add(new ProviderCatalogContextSnapshot
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering,
                CapabilityId = Guid.NewGuid(), ReleaseId = null
            });
            await db.SaveChangesAsync();
        }

        // Act
        var result = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        result.Capabilities.Total.Should().Be(2);
        result.Capabilities.Published.Should().Be(1);
        result.Capabilities.Items.Single(x => x.CapabilityId == first.CapabilityId).ReleaseId.Should().Be(latest.ReleaseId);
        result.Capabilities.Items.Single(x => x.CapabilityId == archived.CapabilityId).PublicationState.Should().Be("Archived");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missions_DistinguishAssignmentsAssociationsAndActiveAssignmentBoundAdoptions(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        var assignment = await fixture.AddAssignmentAsync();
        var sameSystem = await fixture.AddAssignmentAsync(systemId: assignment.SystemId);
        var unknownSystem = await fixture.AddAssignmentAsync(includeSystem: false);
        var unrelated = await fixture.AddAssignmentAsync(offeringId: fixture.OtherOffering);
        var capability = await fixture.AddPublishedAsync("Adopted");
        await fixture.AddRelationshipAsync(assignment, "SeparateMissionBoundary", reviewRequired: false);
        await fixture.AddAdoptionAsync(assignment, capability);
        await fixture.AddAdoptionAsync(assignment, capability); // historical pins cannot double count.
        var inactive = await fixture.AddPublishedAsync("Unsubscribed");
        await fixture.AddAdoptionAsync(assignment, inactive, active: false);
        await using (var db = fixture.Db())
        {
            db.CapabilitySubscriptions.Add(new()
            {
                RegisteredSystemId = sameSystem.SystemId, RoutingTenantId = fixture.Customer,
                CspInheritedCapabilityId = Guid.NewGuid().ToString(), IsActive = true
            });
            await db.SaveChangesAsync();
        }

        // Act
        var result = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        result.MissionSystems.Total.Should().Be(3);
        result.MissionSystems.Items.Single(x => x.AssignmentId == assignment.Id).Should().BeEquivalentTo(new
        {
            SystemName = "Synthetic mission", Associated = true, RelationshipState = "SeparateMissionBoundary",
            AdoptedCapabilityCount = 1, AssignedScopes = new[] { fixture.Scope }
        });
        result.MissionSystems.Items.Single(x => x.AssignmentId == sameSystem.Id).Should().BeEquivalentTo(new
        {
            Associated = false, RelationshipState = "Undetermined", AdoptedCapabilityCount = 0
        });
        result.MissionSystems.Items.Single(x => x.AssignmentId == unknownSystem.Id).SystemName.Should().BeNull();
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task StaleOrUnreviewedRelationship_DoesNotAssertCoverage(bool reviewRequired, long assignmentRevision)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var assignment = await fixture.AddAssignmentAsync();
        await fixture.AddRelationshipAsync(assignment, "CoveredWorkload", reviewRequired, assignmentRevision);

        // Act
        var result = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        result.MissionSystems.Items.Single().Associated.Should().BeTrue();
        result.MissionSystems.Items.Single().RelationshipState.Should().Be("ReviewRequired");
    }

    [Fact]
    public async Task ForeignMetadata_IsNotProjectedEvenWhenIdsMatchAnAssignment()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var assignment = await fixture.AddAssignmentAsync(includeSystem: false);
        await using (var db = fixture.Db())
        {
            db.RegisteredSystems.Add(new() { Id = assignment.SystemId, TenantId = Guid.NewGuid(), Name = "Private other tenant" });
            db.Add(new MissionProviderRelationshipReview
            {
                ProviderId = Guid.NewGuid(), OfferingId = fixture.Offering, TenantId = fixture.Customer,
                SystemId = assignment.SystemId, AssignmentId = assignment.Id, State = "CoveredWorkload",
                ReviewRequired = false, AssignmentRevision = 1
            });
            db.Add(new CapabilityAdoptionSnapshot
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering, TenantId = Guid.NewGuid(),
                SystemId = assignment.SystemId, AssignmentId = assignment.Id, CapabilityId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        // Act
        var result = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        result.MissionSystems.Items.Single().Should().BeEquivalentTo(new
        {
            SystemName = (string?)null, Associated = false, AdoptedCapabilityCount = 0, RelationshipState = "Undetermined"
        });
    }

    [Fact]
    public async Task Pagination_CountsAllRowsWithoutAcquisitionCap_AndReadDoesNotWrite()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        for (var i = 0; i < 105; i++) await fixture.AddCandidateAsync($"Capability {i:D3}");
        for (var i = 0; i < 12; i++) await fixture.AddAssignmentAsync();
        await using var before = fixture.Db();
        var original = await before.Set<ProviderOffering>().SingleAsync(x => x.Id == fixture.Offering);

        // Act
        var result = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 11, 2, 10, default);
        var empty = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 12, 3, 10, default);

        // Assert
        result.Capabilities.Should().BeEquivalentTo(new { Page = 11, PageSize = 10, Total = 105, AwaitingReview = 105, Published = 0 });
        result.Capabilities.Items.Should().HaveCount(5).And.OnlyContain(x => string.CompareOrdinal(x.Name, "Capability 100") >= 0);
        result.MissionSystems.Should().BeEquivalentTo(new { Page = 2, PageSize = 10, Total = 12 });
        result.MissionSystems.Items.Should().HaveCount(2);
        empty.Capabilities.Items.Should().BeEmpty();
        empty.Capabilities.Total.Should().Be(105);
        empty.MissionSystems.Items.Should().BeEmpty();
        await using var after = fixture.Db();
        (await after.Set<ProviderOffering>().SingleAsync(x => x.Id == fixture.Offering)).Should().BeEquivalentTo(original);
        (await after.Set<ProviderAuthorizationAudit>().CountAsync()).Should().Be(0);
        fixture.PackageService.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, 1, 10)]
    [InlineData(1, 0, 10)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 101)]
    [InlineData(int.MaxValue, 1, 100)]
    [InlineData(1, int.MaxValue, 100)]
    public async Task InvalidPagination_FailsExplicitly(int capabilityPage, int missionPage, int size)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        // Act
        var action = () => fixture.Service.BoundaryOverviewAsync(fixture.Offering, capabilityPage, missionPage, size, default);
        // Assert
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task UnauthorizedOrImpersonatedContext_IsDenied(bool admin, bool impersonated)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        fixture.Tenant.IsCspAdmin = admin;
        fixture.Tenant.ImpersonatedTenantId = impersonated ? fixture.Customer : null;
        // Act
        var action = () => fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);
        // Assert
        await action.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrForeignOffering_IsNotFound(bool exists)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        if (exists)
        {
            await using var db = fixture.Db();
            db.Add(new ProviderOffering { Id = id, OfferingId = id, ProviderId = Guid.NewGuid(), Name = "Foreign private" });
            await db.SaveChangesAsync();
        }
        // Act
        var action = () => fixture.Service.BoundaryOverviewAsync(id, 1, 1, 10, default);
        // Assert
        await action.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CorruptPublishedLink_IsFailureRatherThanSuccessShapedMissingData()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var candidate = await fixture.AddCandidateAsync("Published missing context", "Published");
        await using (var db = fixture.Db())
        {
            var row = await db.CspPackageCandidates.SingleAsync(x => x.Id == candidate.Id);
            row.PayloadJson = ProviderAuthorizationStore.Json(Read<PackageCandidateResponse>(row.PayloadJson) with { PublishedRecordId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        // Act
        var action = () => fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);
        // Assert
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedPackageVersion_IsAnExplicitOfferingLink_WhenReceiptPointerIsAbsent(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        var source = await fixture.AddCandidateAsync("Version-linked proposal", linked: false);
        await using (var db = fixture.Db())
        {
            db.Add(new ProviderPackageVersion
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering, PackageId = source.PackageId,
                BoundaryRevisionId = fixture.SourceBoundary, SeriesId = Guid.NewGuid(), Version = 1
            });
            await db.SaveChangesAsync();
        }

        // Act
        var result = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        result.Capabilities.Total.Should().Be(1);
        result.Capabilities.Items.Single().BoundaryRevisionId.Should().Be(fixture.SourceBoundary);
    }

    [Fact]
    public async Task ForeignProviderPackage_DoesNotBecomeVisibleThroughAnOfferingId()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var candidate = await fixture.AddCandidateAsync("Foreign private proposal");
        await using (var db = fixture.Db())
        {
            (await db.CspPackages.SingleAsync(x => x.Id == candidate.PackageId)).ProviderId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }

        // Act
        var result = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        result.Capabilities.Total.Should().Be(0);
        result.Capabilities.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("foreign-canonical")]
    [InlineData("mismatched-release")]
    [InlineData("foreign-boundary-context")]
    [InlineData("missing-boundary-context")]
    public async Task InvalidPublishedContexts_DoNotLeakForeignRecordsOrInventBoundaries(string kind)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var published = await fixture.AddPublishedAsync("Canonical");
        await using (var db = fixture.Db())
        {
            if (kind == "foreign-canonical")
            {
                var capability = await db.CspInheritedCapabilities.Include(x => x.CspInheritedComponent).SingleAsync();
                capability.CspInheritedComponent.CspProfileId = Guid.NewGuid();
            }
            else if (kind == "mismatched-release")
                (await db.ProviderCapabilityReleases.SingleAsync()).CapabilityId = Guid.NewGuid();
            else
            {
                var context = await db.Set<ProviderCatalogContextSnapshot>().SingleAsync();
                var material = Read<ProviderPublicationContextMaterial>(context.SnapshotJson);
                context.SnapshotJson = ProviderAuthorizationStore.Json(kind == "foreign-boundary-context"
                    ? material with { OfferingId = fixture.OtherOffering } : material with { BoundaryRevisionId = Guid.Empty });
            }
            await db.SaveChangesAsync();
        }

        // Act
        var action = () => fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly Mock<ITenantContextAccessor> _accessor = new();
        private readonly DbContextOptions<AtoCopilotContext> _options;
        private readonly SqliteConnection? _connection;
        public Guid Provider { get; } = Guid.NewGuid();
        public Guid Offering { get; } = Guid.NewGuid();
        public Guid OtherOffering { get; } = Guid.NewGuid();
        public Guid Customer { get; } = Guid.NewGuid();
        public Guid SourceBoundary { get; } = Guid.NewGuid();
        public Guid HostingScope { get; } = Guid.NewGuid();
        public Guid Impact { get; } = Guid.NewGuid();
        public TenantContext Tenant { get; } = new(Guid.Empty) { IsCspAdmin = true };
        public Mock<ICspPackageService> PackageService { get; } = new(MockBehavior.Strict);
        public ProviderAuthorizationService Service { get; }
        public ProviderAzureScope Scope { get; } = new("AzureCloud", Guid.NewGuid(), Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "/subscriptions/11111111-1111-1111-1111-111111111111");

        private Fixture(DbContextOptions<AtoCopilotContext> options, SqliteConnection? connection)
        {
            _options = options;
            _connection = connection;
            _accessor.SetupGet(x => x.Current).Returns(Tenant);
            var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
            factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() =>
            {
                return new ReadOnlyContext(_options, _accessor.Object);
            });
            Service = new(new(factory.Object, Tenant, NullLogger<ProviderAuthorizationStore>.Instance), PackageService.Object);
        }

        public AtoCopilotContext Db() => new(_options);

        public static async Task<Fixture> CreateAsync(bool sqlite = false)
        {
            SqliteConnection? connection = sqlite ? new("Data Source=:memory:") : null;
            if (connection is not null) await connection.OpenAsync();
            var builder = new DbContextOptionsBuilder<AtoCopilotContext>();
            if (connection is not null) builder.UseSqlite(connection);
            else builder.UseInMemoryDatabase(Guid.NewGuid().ToString());
            var fixture = new Fixture(builder.Options, connection);
            await using var db = fixture.Db();
            await db.Database.EnsureCreatedAsync();
            db.CspProfiles.Add(new() { Id = fixture.Provider });
            db.Tenants.Add(new() { Id = fixture.Customer, DisplayName = "Synthetic tenant" });
            db.AddRange(new ProviderOffering { Id = fixture.Offering, OfferingId = fixture.Offering,
                    ProviderId = fixture.Provider, Name = "Offering", Revision = 7, CurrentBoundaryRevisionId = Guid.NewGuid() },
                new ProviderOffering { Id = fixture.OtherOffering, OfferingId = fixture.OtherOffering, ProviderId = fixture.Provider, Name = "Other" });
            db.Add(new ProviderBoundaryRevision { Id = fixture.SourceBoundary, ProviderId = fixture.Provider, OfferingId = fixture.Offering });
            db.Add(new ProviderHostingScopeRevision { Id = fixture.HostingScope, ProviderId = fixture.Provider, OfferingId = fixture.Offering });
            db.Add(new ProviderAuthorizationImpactReview { Id = fixture.Impact, ProviderId = fixture.Provider, OfferingId = fixture.Offering });
            await db.SaveChangesAsync();
            return fixture;
        }

        public async Task<CspPackageCandidate> AddCandidateAsync(string name, string state = "NeedsReview",
            string type = "Capability", bool linked = true, Guid? offeringId = null)
        {
            await using var db = Db();
            var package = new CspPackage
            {
                ProviderId = Provider, OfferingId = linked ? offeringId ?? Offering : null,
                BoundaryRevisionId = linked ? SourceBoundary : null, IdempotencyKey = Guid.NewGuid().ToString()
            };
            var candidate = new CspPackageCandidate { PackageId = package.Id, Type = type, ReviewState = state };
            candidate.PayloadJson = ProviderAuthorizationStore.Json(new PackageCandidateResponse(candidate.Id, type, name, "", "", "", "",
                new Dictionary<string, string>(), [], [], [], null, null, state, 1, null, null));
            db.AddRange(package, candidate);
            await db.SaveChangesAsync();
            return candidate;
        }

        public async Task<(Guid CapabilityId, Guid ReleaseId, Guid ContextId)> AddPublishedAsync(string name,
            CspPackageCandidate? candidate = null, Guid? capabilityId = null, long revision = 1,
            Guid? offeringId = null, CspInheritedCapabilityStatus status = CspInheritedCapabilityStatus.Mapped)
        {
            await using var db = Db();
            var id = capabilityId ?? Guid.NewGuid();
            if (!await db.CspInheritedCapabilities.AnyAsync(x => x.Id == id))
            {
                var component = new CspInheritedComponent { CspProfileId = Provider, Name = "Synthetic", Status = CspInheritedComponentStatus.Published };
                db.Add(component);
                db.Add(new CspInheritedCapability { Id = id, CspInheritedComponentId = component.Id, Name = name, Status = status });
            }
            var release = new ProviderCapabilityRelease { CapabilityId = id, Revision = revision, IdempotencyKey = Guid.NewGuid().ToString() };
            var context = new ProviderCatalogContextSnapshot
            {
                ProviderId = Provider, OfferingId = offeringId ?? Offering, CapabilityId = id,
                ReleaseId = release.Id, ImpactReviewId = Impact,
                SnapshotJson = ProviderAuthorizationStore.Json(new ProviderPublicationContextMaterial(offeringId ?? Offering, 1, SourceBoundary, "", null, null, [], [], []))
            };
            db.AddRange(release, context);
            if (candidate is not null)
            {
                var row = await db.CspPackageCandidates.SingleAsync(x => x.Id == candidate.Id);
                row.PayloadJson = ProviderAuthorizationStore.Json(Read<PackageCandidateResponse>(row.PayloadJson) with { PublishedRecordId = id });
            }
            await db.SaveChangesAsync();
            return (id, release.Id, context.Id);
        }

        public async Task<ProviderHostingAssignment> AddAssignmentAsync(string? systemId = null, bool includeSystem = true, Guid? offeringId = null)
        {
            await using var db = Db();
            var id = systemId ?? Guid.NewGuid().ToString();
            if (includeSystem && !await db.RegisteredSystems.AnyAsync(x => x.Id == id))
                db.RegisteredSystems.Add(new() { Id = id, TenantId = Customer, Name = "Synthetic mission" });
            var offering = offeringId ?? Offering;
            var hostingId = HostingScope;
            if (offering != Offering)
            {
                var hosting = new ProviderHostingScopeRevision { ProviderId = Provider, OfferingId = offering };
                db.Add(hosting);
                hostingId = hosting.Id;
            }
            var assignment = new ProviderHostingAssignment
            {
                ProviderId = Provider, OfferingId = offering, TargetTenantId = Customer, SystemId = id,
                HostingScopeRevisionId = hostingId, AssignedScopesJson = ProviderAuthorizationStore.Json(new[] { Scope })
            };
            db.Add(assignment);
            await db.SaveChangesAsync();
            return assignment;
        }

        public async Task AddRelationshipAsync(ProviderHostingAssignment assignment, string state, bool reviewRequired, long revision = 1)
        {
            await using var db = Db();
            db.Add(new MissionProviderRelationshipReview
            {
                ProviderId = Provider, OfferingId = assignment.OfferingId, TenantId = Customer,
                SystemId = assignment.SystemId, AssignmentId = assignment.Id, AssignmentRevision = revision,
                State = state, ReviewRequired = reviewRequired
            });
            await db.SaveChangesAsync();
        }

        public async Task AddAdoptionAsync(ProviderHostingAssignment assignment,
            (Guid CapabilityId, Guid ReleaseId, Guid ContextId) capability, bool active = true)
        {
            await using var db = Db();
            var subscription = new CapabilitySubscription
            {
                RegisteredSystemId = assignment.SystemId, RoutingTenantId = Customer,
                CspInheritedCapabilityId = capability.CapabilityId.ToString(), IsActive = active
            };
            db.Add(subscription);
            db.Add(new CapabilityAdoptionSnapshot
            {
                ProviderId = Provider, OfferingId = assignment.OfferingId, TenantId = Customer,
                SystemId = assignment.SystemId, AssignmentId = assignment.Id, AssignmentRevision = 1,
                CapabilityId = capability.CapabilityId, ReleaseId = capability.ReleaseId, ContextSnapshotId = capability.ContextId,
                SubscriptionId = subscription.Id
            });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null) await _connection.DisposeAsync();
        }
    }

    private sealed class ReadOnlyContext(DbContextOptions<AtoCopilotContext> options, ITenantContextAccessor accessor)
        : AtoCopilotContext(options, accessor)
    {
        public override Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            throw new Xunit.Sdk.XunitException("The overview must not save changes.");
        public override int SaveChanges() => throw new Xunit.Sdk.XunitException("The overview must not save changes.");
    }
}

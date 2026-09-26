using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Moq;
using Ato.Copilot.Core.Interfaces.PackageImports;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ProviderMissionServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TenantContextAccessor _accessor = new();
    private DbContextOptions<AtoCopilotContext> _options = null!;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _provider = Guid.NewGuid();
    private readonly Guid _person = Guid.NewGuid();
    private readonly string _system = Guid.NewGuid().ToString();
    private ProviderOffering _offering = null!;
    private ProviderHostingAssignment _assignment = null!;
    private ProviderBoundaryRevision _boundary = null!;
    private ProviderAuthorizationRevision _decision = null!;
    private ProviderCitation _citation = null!;
    private ProviderAzureScope Scope => new("AzureCloud", _tenant, _tenant, $"/subscriptions/{_tenant}");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options;
        await using var db = new AtoCopilotContext(_options);
        await db.Database.EnsureCreatedAsync();
        db.CspProfiles.Add(new() { Id = _provider, OnboardingState = OnboardingState.Active });
        db.Tenants.Add(new() { Id = _tenant, DisplayName = "Customer", OnboardingState = OnboardingState.Active });
        db.RegisteredSystems.Add(new() { Id = _system, TenantId = _tenant, Name = "Mission" });
        db.Persons.Add(new() { Id = _person, TenantId = _tenant, DisplayName = "Reviewer", Email = "test@example.invalid" });
        db.OrganizationMemberships.Add(new() { PersonId = _person, TenantId = _tenant, DirectoryTenantId = Guid.NewGuid(),
            ObjectId = Guid.NewGuid(), GrantedBy = "test" });
        db.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system, PersonId = _person, Role = OrganizationRole.Issm });
        _offering = new() { ProviderId = _provider, Name = "Offering", EnvironmentsJson = ProviderAuthorizationStore.Json(new[] { "AzureCloud" }) };
        _offering.OfferingId = _offering.Id;
        _citation = new(Guid.NewGuid(), Guid.NewGuid(), "private/source.pdf", "page:1", "Restricted original quote");
        db.Add(new CspPackage { Id = _citation.PackageId, ProviderId = _provider, Name = "Private package", IdempotencyKey = "source" });
        db.Add(new CspPackageEntry { Id = _citation.ArtifactId, PackageId = _citation.PackageId, ArchivePath = _citation.ArchivePath,
            SegmentsJson = ProviderAuthorizationStore.Json(new[] { new CspPackageSourceSegment("segment", "entry",
                _citation.ArtifactId.ToString(), _citation.ArchivePath, _citation.Locator, _citation.Quote) }) });
        var boundaryBody = new CreateProviderBoundaryRequest(1, null, "Boundary", "Recorded scope", ["Service"],
            [], [Scope], [], ["Provider"], ["Customer"], [_citation]);
        _boundary = new() { ProviderId = _provider, OfferingId = _offering.Id, SnapshotJson = ProviderAuthorizationStore.Json(boundaryBody), SnapshotHash = Hash(ProviderAuthorizationStore.Json(boundaryBody)) };
        var hostingBody = new CreateProviderHostingScopeRequest(1, null, "Hosting", [Scope], [], []);
        var hosting = new ProviderHostingScopeRevision { ProviderId = _provider, OfferingId = _offering.Id, SnapshotJson = ProviderAuthorizationStore.Json(hostingBody), SnapshotHash = Hash(ProviderAuthorizationStore.Json(hostingBody)) };
        _offering.CurrentBoundaryRevisionId = _boundary.Id;
        _offering.CurrentHostingScopeRevisionId = hosting.Id;
        var record = new Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderAuthorizationRecord { ProviderId = _provider, OfferingId = _offering.Id };
        var decisionBody = new CreateProviderDecisionRequest(1, _boundary.Id, [], "ProviderDecision", "Decision reference",
            "External authority", "ATO", null, null, null, "NoExpiryStated", "Recorded scope", [], [_citation]);
        _decision = new() { ProviderId = _provider, OfferingId = _offering.Id, RecordId = record.Id, BoundaryRevisionId = _boundary.Id,
            SnapshotJson = ProviderAuthorizationStore.Json(decisionBody), SnapshotHash = Hash(ProviderAuthorizationStore.Json(decisionBody)), MetadataReviewState = "Recorded",
            RecordedBy = "provider", RecordedAt = DateTimeOffset.UtcNow };
        record.CurrentRevisionId = _decision.Id;
        _assignment = new() { ProviderId = _provider, OfferingId = _offering.Id, TargetTenantId = _tenant,
            SystemId = _system, HostingScopeRevisionId = hosting.Id, AssignedScopesJson = ProviderAuthorizationStore.Json(new[] { Scope }) };
        db.AddRange(_offering, _boundary, hosting, record, _decision, _assignment);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();
    private ProviderMissionService Service(AtoCopilotContext db, TenantContext tenant)
    {
        var access = new SystemWorkspaceAccessService(new Factory(_options, _accessor), _accessor);
        return new(db, tenant, access, new CapabilityResponsibilityService(db, tenant, access,
            NullLogger<CapabilityResponsibilityService>.Instance));
    }
    private sealed class Factory(DbContextOptions<AtoCopilotContext> options, TenantContextAccessor accessor) : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options, accessor);
    }

    [Fact]
    public async Task Association_RequiresFreshAllocationAndScopedIdempotencyIntent()
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);
        var request = new CreateMissionProviderRelationshipRequest(_assignment.Id, 1);
        // Act
        var first = await service.AssociateAsync(_system, request, "owner", default, "association-key");
        var replay = await service.AssociateAsync(_system, request, "owner", default, "association-key");
        // Assert
        replay.RelationshipId.Should().Be(first.RelationshipId);
        await FluentActions.Awaiting(() => service.AssociateAsync(_system, request with { ExpectedAssignmentRevision = 2 },
            "owner", default, "association-key")).Should().ThrowAsync<DbUpdateConcurrencyException>();
        await FluentActions.Awaiting(() => service.AssociateAsync(_system, request with { ExpectedAssignmentRevision = 2 },
            "owner", default, "new-key")).Should().ThrowAsync<DbUpdateConcurrencyException>();
        await FluentActions.Awaiting(() => service.AssociateAsync(_system, request with { AssignmentId = Guid.NewGuid() },
            "owner", default, "unknown-key")).Should().ThrowAsync<KeyNotFoundException>();
        (await db.Set<MissionProviderRelationshipReview>().CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task MissionLists_RejectInvalidPaging(int page, int size)
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);
        // Act
        var relationships = () => service.RelationshipsAsync(_system, page, size, default);
        var applicable = () => service.ApplicableAsync(_system, page, size, null, null, null, null, null, default);
        // Assert
        await relationships.Should().ThrowAsync<ArgumentException>();
        await applicable.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SeparateBoundaryReview_DoesNotNeedAoAndAssociationDoesNotResetIt()
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);
        var relationship = await service.AssociateAsync(_system, new(_assignment.Id, 1), "manager", default);
        var preview = await service.PreviewAsync(_system, relationship.RelationshipId!.Value,
            new(1, 1, "SeparateBoundaryConsumer", null, null, [], "Separate authorization retained"), "manager", default);
        // Act
        var reviewed = await service.ReviewAsync(_system, relationship.RelationshipId.Value,
            new(preview.Revision, preview.PreviewId, preview.PreviewHash, "Confirmed separate relationship"), "manager", default);
        var replay = await service.AssociateAsync(_system, new(_assignment.Id, 1), "manager", default);
        // Assert
        reviewed.State.Should().Be("SeparateBoundaryConsumer");
        replay.State.Should().Be(reviewed.State);
        replay.AuthorizationRevisionId.Should().BeNull();
        replay.ReviewRequired.Should().BeFalse();
        (await db.AuthorizationDecisions.CountAsync()).Should().Be(0);
        await FluentActions.Awaiting(() => service.ReviewAsync(_system, relationship.RelationshipId.Value,
            new(reviewed.Revision, preview.PreviewId, "changed", "Invalid"), "manager", default))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("system")]
    [InlineData("role")]
    public async Task FreshAccess_RevocationStopsReadsAndAssociation(string changed)
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);
        await service.RelationshipsAsync(_system, 1, 10, default);
        await using (var seed = new AtoCopilotContext(_options))
        {
            if (changed == "system") (await seed.RegisteredSystems.SingleAsync()).IsActive = false;
            else if (changed == "role") (await seed.SystemRoleAssignments.SingleAsync()).RemovedAt = DateTimeOffset.UtcNow;
            else (await seed.OrganizationMemberships.SingleAsync()).RevokedAt = DateTimeOffset.UtcNow;
            await seed.SaveChangesAsync();
        }
        // Act
        var read = () => service.RelationshipsAsync(_system, 1, 10, default);
        var associate = () => service.AssociateAsync(_system, new(_assignment.Id, 1), "owner", default);
        // Assert
        await read.Should().ThrowAsync<KeyNotFoundException>();
        await associate.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Decisions_RecordKindFilter_CountsOnlyMatchingCurrentRevisions()
    {
        // Arrange
        await using (var seed = new AtoCopilotContext(_options))
        {
            for (var i = 0; i < 3; i++)
            {
                var record = new Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderAuthorizationRecord
                    { ProviderId = _provider, OfferingId = _offering.Id };
                var body = Read<CreateProviderDecisionRequest>(_decision.SnapshotJson) with { RecordKind = "InheritedMicrosoftReference" };
                var revision = new ProviderAuthorizationRevision { ProviderId = _provider, OfferingId = _offering.Id,
                    RecordId = record.Id, BoundaryRevisionId = _boundary.Id, SnapshotJson = ProviderAuthorizationStore.Json(body),
                    SnapshotHash = Hash(ProviderAuthorizationStore.Json(body)) };
                record.CurrentRevisionId = revision.Id;
                seed.AddRange(record, revision);
            }
            await seed.SaveChangesAsync();
        }
        var tenant = new TenantContext(Guid.Empty) { IsCspAdmin = true };
        using var scope = _accessor.Push(tenant);
        var service = new ProviderAuthorizationService(new(new Factory(_options, _accessor), tenant,
            NullLogger<ProviderAuthorizationStore>.Instance), Mock.Of<ICspPackageService>());

        // Act
        var page = await service.DecisionsAsync(_offering.Id, 2, 1, null, default, "InheritedMicrosoftReference");
        var all = await service.DecisionsAsync(_offering.Id, 1, 10, null, default);

        // Assert
        page.Total.Should().Be(3);
        page.Items.Single().RecordKind.Should().Be("InheritedMicrosoftReference");
        all.Total.Should().Be(4);
        await FluentActions.Awaiting(() => service.DecisionsAsync(_offering.Id, 1, 10, null, default, "Unknown"))
            .Should().ThrowAsync<ArgumentException>();
    }

    private async Task<(Guid CapabilityId, ProviderCapabilityRelease Release)> SeedPublishedAsync()
    {
        var capabilityId = Guid.NewGuid();
        var release = new ProviderCapabilityRelease { CapabilityId = capabilityId, Revision = 1, SnapshotHash = "exact-release",
            SnapshotJson = System.Text.Json.JsonSerializer.Serialize(new { DutiesJson = "{\"AC-2\":\"Provider\",\"AC-3\":\"Shared\",\"AC-4\":\"Customer\"}" }),
            IdempotencyKey = "synthetic-release", PublishedBy = "provider" };
        await using (var seed = new AtoCopilotContext(_options))
        {
            var component = new CspInheritedComponent { CspProfileId = _provider, Name = "Published component", Status = CspInheritedComponentStatus.Published };
            seed.Add(component);
            seed.CspInheritedCapabilities.Add(new() { Id = capabilityId, CspInheritedComponentId = component.Id,
                Name = "Published capability", Status = CspInheritedCapabilityStatus.Mapped, MappedNistControlIds = ["AC-2", "AC-3", "AC-4"] });
            seed.Add(release);
            var hosting = await seed.Set<ProviderHostingScopeRevision>().SingleAsync();
            var context = new ProviderPublicationContextMaterial(_offering.Id, _offering.Revision, _boundary.Id, _boundary.SnapshotHash,
                hosting.Id, hosting.SnapshotHash, [new(_decision.RecordId, _decision.Id, _decision.SnapshotHash, "lifecycle")], [], []);
            var review = new ProviderAuthorizationImpactReview { ProviderId = _provider, OfferingId = _offering.Id,
                Disposition = "AcceptForPublication", ReviewedBy = "provider", ReviewedAt = DateTimeOffset.UtcNow,
                ContextJson = ProviderAuthorizationStore.Json(context), ContextSnapshotHash = Hash(ProviderAuthorizationStore.Json(context)) };
            seed.Add(review);
            seed.Add(new ProviderCatalogContextSnapshot { ProviderId = _provider, OfferingId = _offering.Id,
                CapabilityId = capabilityId, ReleaseId = release.Id, ImpactReviewId = review.Id,
                SnapshotJson = review.ContextJson, SnapshotHash = review.ContextSnapshotHash });
            await seed.SaveChangesAsync();
        }
        return (capabilityId, release);
    }

    [Theory]
    [InlineData("offering")]
    [InlineData("review")]
    [InlineData("withdrawn")]
    [InlineData("source-excluded")]
    [InlineData("release")]
    public async Task Applicable_StaleContextCannotCreateSubscription(string change)
    {
        // Arrange
        var (capabilityId, release) = await SeedPublishedAsync();
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);
        await service.AssociateAsync(_system, new(_assignment.Id, 1), "owner", default);
        var before = (await service.ApplicableAsync(_system, 1, 10, _assignment.Id, null, null, null, null, default)).Items.Single();
        await using (var seed = new AtoCopilotContext(_options))
        {
            switch (change)
            {
                case "offering": (await seed.Set<ProviderOffering>().SingleAsync()).Revision++; break;
                case "review": (await seed.Set<ProviderAuthorizationImpactReview>().SingleAsync()).InvalidatedAt = DateTimeOffset.UtcNow; break;
                case "source-excluded": (await seed.CspPackageEntries.SingleAsync()).Status = "Excluded"; break;
                case "withdrawn":
                    seed.Add(new ProviderAuthorizationLifecycleEvent { ProviderId = _provider, OfferingId = _offering.Id,
                        RecordId = _decision.RecordId, AuthorizationRevisionId = _decision.Id, Kind = "Withdrawn",
                        EffectiveOn = DateTime.UtcNow.ToString("yyyy-MM-dd") }); break;
                case "release": seed.Add(new ProviderCapabilityRelease { CapabilityId = capabilityId, Revision = 2,
                    SnapshotJson = release.SnapshotJson, SnapshotHash = "new-release", IdempotencyKey = "new-release", PublishedBy = "provider" }); break;
            }
            await seed.SaveChangesAsync();
        }
        // Act
        var after = (await service.ApplicableAsync(_system, 1, 10, _assignment.Id, null, null, null, null, default)).Items.Single();
        var act = () => service.AdoptAsync(_system, new(_assignment.Id, 1, capabilityId, release.Id,
            before.Applicability.SnapshotHash, before.ApplicabilityPreviewHash), "manager", default);
        // Assert
        after.CanProposeAdoption.Should().BeFalse();
        after.ApplicabilityState.Should().Be("ReviewRequired");
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await db.CapabilitySubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Applicable_UsesPublishedOfferingContextAndCanonicalAdoptionWithoutConfirmation()
    {
        // Arrange
        var (capabilityId, release) = await SeedPublishedAsync();
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);
        var unassociated = await service.ApplicableAsync(_system, 1, 10, _assignment.Id, null, null, null, null, default);
        unassociated.Items.Single().CanProposeAdoption.Should().BeFalse();
        await service.AssociateAsync(_system, new(_assignment.Id, 1), "owner", default);

        // Act
        var page = await service.ApplicableAsync(_system, 1, 10, _assignment.Id, null, "AzureCloud", null, null, default);
        var item = page.Items.Single();
        var adopted = await service.AdoptAsync(_system, new(_assignment.Id, 1, capabilityId, release.Id,
            item.Applicability.SnapshotHash, item.ApplicabilityPreviewHash), "manager", default, "adopt-key");
        var replay = await service.AdoptAsync(_system, new(_assignment.Id, 1, capabilityId, release.Id,
            item.Applicability.SnapshotHash, item.ApplicabilityPreviewHash), "manager", default, "adopt-key");

        // Assert
        page.Total.Should().Be(1);
        item.ProviderCoverage.Should().Equal("AC-2");
        item.SharedDuties.Should().Equal("AC-3");
        item.CustomerDuties.Should().Equal("AC-4");
        item.CanProposeAdoption.Should().BeTrue();
        adopted.ReleaseId.Should().Be(release.Id);
        replay.AdoptionSnapshotId.Should().Be(adopted.AdoptionSnapshotId);
        (await db.CapabilitySubscriptions.CountAsync()).Should().Be(1);
        (await db.Set<CapabilityResponsibilityConfirmation>().CountAsync()).Should().Be(0);
        (await db.Set<CapabilityAdoptionSnapshot>().SingleAsync()).ReleaseId.Should().Be(release.Id);
        await FluentActions.Awaiting(() => service.AdoptAsync(_system, new(_assignment.Id, 1, capabilityId,
            release.Id, "changed", item.ApplicabilityPreviewHash), "manager", default))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task CoveredRelationship_CannotBeDowngradedByNonAo()
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);
        var relationship = await service.AssociateAsync(_system, new(_assignment.Id, 1), "manager", default);
        await using (var seed = new AtoCopilotContext(_options))
        {
            var retained = await seed.Set<MissionProviderRelationshipReview>().SingleAsync();
            retained.State = "ExplicitlyCoveredByRecordedScope";
            await seed.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        // Act
        var act = () => service.PreviewAsync(_system, relationship.RelationshipId!.Value,
            new(1, 1, "SeparateBoundaryConsumer", null, null, [], "Change covered status"), "manager", default);
        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task CoveredReview_ExcludedRetainedEvidenceBlocksCoverage()
    {
        // Arrange
        await using (var seed = new AtoCopilotContext(_options))
        {
            seed.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
                PersonId = _person, Role = OrganizationRole.AuthorizingOfficial });
            (await seed.CspPackageEntries.SingleAsync()).Status = "Excluded";
            await seed.SaveChangesAsync();
        }
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);
        var relationship = await service.AssociateAsync(_system, new(_assignment.Id, 1), "manager", default);
        // Act
        var preview = await service.PreviewAsync(_system, relationship.RelationshipId!.Value,
            new(1, 1, "ExplicitlyCoveredByRecordedScope", _decision.Id, _boundary.Id, [_citation], "Review"), "ao", default);
        // Assert
        preview.CanReview.Should().BeFalse();
        preview.Blockers.Should().Contain(x => x.Code == "SOURCE_EVIDENCE_UNAVAILABLE");
    }

    [Fact]
    public async Task Allocations_ListBeforeAssociation_WithNamesWithoutPrivateEvidence()
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);

        // Act
        var result = await service.RelationshipsAsync(_system, 1, 10, default);

        // Assert
        result.Total.Should().Be(1);
        result.Items.Single().RelationshipId.Should().BeNull();
        ProviderAuthorizationStore.Json(result).Should().Contain("\"offeringName\":\"Offering\"")
            .And.Contain("\"canAssociate\":true").And.NotContain("private/source.pdf");
        (await db.Set<MissionProviderRelationshipReview>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task MissionOwner_CanAssociateWithoutAuthorityOrSubscriptions()
    {
        // Arrange
        await using (var seed = new AtoCopilotContext(_options))
        {
            (await seed.SystemRoleAssignments.SingleAsync()).Role = OrganizationRole.MissionOwner;
            await seed.SaveChangesAsync();
        }
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);

        // Act
        var first = await service.AssociateAsync(_system, new(_assignment.Id, 1), "owner", default);
        var replay = await service.AssociateAsync(_system, new(_assignment.Id, 1), "owner", default);

        // Assert
        replay.RelationshipId.Should().Be(first.RelationshipId);
        replay.State.Should().Be("Undetermined");
        (await db.Set<MissionProviderRelationshipReview>().CountAsync()).Should().Be(1);
        (await db.CapabilitySubscriptions.CountAsync()).Should().Be(0);
        (await db.AuthorizationDecisions.CountAsync()).Should().Be(0);
        await FluentActions.Awaiting(() => service.AdoptAsync(_system,
            new(_assignment.Id, 1, Guid.NewGuid(), Guid.NewGuid(), "hash", "hash"), "owner", default))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ProviderOrSupport_CannotActAsMissionOwner(bool admin, bool support)
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsCspAdmin = admin, IsWorkspaceRequest = true,
            ImpersonatedTenantId = support ? _tenant : null };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);
        // Act
        var act = () => service.AssociateAsync(_system, new(_assignment.Id, 1), "provider", default);
        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Associate_UsesExactCustomerProjectionAndDoesNotConfirmCoverage()
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);

        // Act
        var result = await service.AssociateAsync(_system, new(_assignment.Id, 1), "customer", default);

        // Assert
        result.State.Should().Be("Undetermined");
        result.ReviewRequired.Should().BeTrue();
        (await db.Set<ProviderHostingAssignment>().CountAsync()).Should().Be(0, "provider-private filters must remain intact");
        (await db.Set<MissionProviderRelationshipReview>().SingleAsync()).AssignmentId.Should().Be(_assignment.Id);
        (await db.AuthorizationDecisions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CoveredReview_RequiresPersistedAoEvenWhenManager()
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);
        var relationship = await service.AssociateAsync(_system, new(_assignment.Id, 1), "manager", default);

        // Act
        var act = () => service.PreviewAsync(_system, relationship.RelationshipId!.Value,
            new(relationship.Revision, 1, "ExplicitlyCoveredByRecordedScope", _decision.Id, _boundary.Id, [_citation], "Review"),
            "manager", default);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ForeignTenantAssignment_KnownIdDoesNotGrantAccess()
    {
        // Arrange
        await using (var seed = new AtoCopilotContext(_options))
        {
            var foreign = new Tenant { DisplayName = "Foreign" };
            seed.Add(foreign);
            var system = new RegisteredSystem { TenantId = foreign.Id, Name = "Foreign mission" };
            seed.Add(system);
            _assignment = new ProviderHostingAssignment { TargetTenantId = foreign.Id, SystemId = system.Id,
                ProviderId = _provider, OfferingId = _offering.Id, HostingScopeRevisionId = _assignment.HostingScopeRevisionId,
                AssignedScopesJson = _assignment.AssignedScopesJson };
            seed.Add(_assignment);
            await seed.SaveChangesAsync();
        }
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);

        // Act
        var act = () => Service(db, tenant).AssociateAsync(_system, new(_assignment.Id, 1), "actor", default);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task AoReview_RetainsHistoryAndExpiredAuthorityRequiresReviewWithoutDecisionMutation()
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);
        var relationship = await service.AssociateAsync(_system, new(_assignment.Id, 1), "manager", default);
        await using (var seed = new AtoCopilotContext(_options))
        {
            seed.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
                PersonId = _person, Role = OrganizationRole.AuthorizingOfficial });
            await seed.SaveChangesAsync();
        }
        var preview = await service.PreviewAsync(_system, relationship.RelationshipId!.Value,
            new(relationship.Revision, 1, "ExplicitlyCoveredByRecordedScope", _decision.Id, _boundary.Id, [_citation], "Evidenced review"),
            "ao", default);

        // Act
        var reviewed = await service.ReviewAsync(_system, relationship.RelationshipId.Value,
            new(preview.Revision, preview.PreviewId, preview.PreviewHash, "AO confirms external relationship only"), "ao", default);
        await using (var seed = new AtoCopilotContext(_options))
        {
            seed.Add(new ProviderAuthorizationLifecycleEvent { ProviderId = _provider, OfferingId = _offering.Id,
                RecordId = _decision.RecordId, AuthorizationRevisionId = _decision.Id, Kind = "Withdrawn",
                EffectiveOn = DateTime.UtcNow.ToString("yyyy-MM-dd"), Rationale = "External withdrawal", CitationsJson = ProviderAuthorizationStore.Json(new[] { _citation }) });
            await seed.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        var refreshed = await service.RelationshipsAsync(_system, 1, 20, default);

        // Assert
        reviewed.State.Should().Be("ExplicitlyCoveredByRecordedScope");
        reviewed.ReviewRequired.Should().BeFalse();
        refreshed.Items.Single().State.Should().Be("ExplicitlyCoveredByRecordedScope");
        refreshed.Items.Single().ReviewRequired.Should().BeTrue();
        var retained = await db.Set<MissionProviderRelationshipReview>().SingleAsync();
        retained.HistoryJson.Should().Contain("AO confirms external relationship only");
        (await db.AuthorizationDecisions.CountAsync()).Should().Be(0);
        ProviderAuthorizationStore.Json(refreshed).Should().NotContain("Restricted original quote").And.NotContain("private/source.pdf");
    }

    [Fact]
    public async Task AoPermissionRevokedAfterPreview_CannotConfirm()
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var scope = _accessor.Push(tenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var service = Service(db, tenant);
        var relationship = await service.AssociateAsync(_system, new(_assignment.Id, 1), "manager", default);
        await using (var seed = new AtoCopilotContext(_options))
        {
            seed.SystemRoleAssignments.Add(new() { TenantId = _tenant, RegisteredSystemId = _system,
                PersonId = _person, Role = OrganizationRole.AuthorizingOfficial });
            await seed.SaveChangesAsync();
        }
        var preview = await service.PreviewAsync(_system, relationship.RelationshipId!.Value,
            new(relationship.Revision, 1, "ExplicitlyCoveredByRecordedScope", _decision.Id, _boundary.Id, [_citation], "Evidence"),
            "ao", default);
        await using (var seed = new AtoCopilotContext(_options))
        {
            (await seed.SystemRoleAssignments.SingleAsync(x => x.Role == OrganizationRole.AuthorizingOfficial)).RemovedAt = DateTimeOffset.UtcNow;
            await seed.SaveChangesAsync();
        }

        // Act
        var act = () => service.ReviewAsync(_system, relationship.RelationshipId.Value,
            new(preview.Revision, preview.PreviewId, preview.PreviewHash, "Confirm"), "actor", default);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}

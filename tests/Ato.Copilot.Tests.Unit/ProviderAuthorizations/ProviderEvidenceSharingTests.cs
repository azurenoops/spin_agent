using System.Security.Cryptography;
using System.Text;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ProviderEvidenceSharingTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<ITenantContextAccessor> _accessor = new();
    private ITenantContext _context = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _systemId = Guid.NewGuid().ToString();
    private readonly byte[] _bytes = Encoding.UTF8.GetBytes("PRIVATE source attachment");
    private readonly Mock<IFileStorageProvider> _storage = new(MockBehavior.Strict);
    private readonly Mock<IProviderMissionService> _mission = new(MockBehavior.Strict);
    private ProviderOffering _offering = null!;
    private ProviderFindingEvidence _evidence = null!;
    private ProviderHostingAssignment _assignment = null!;
    private DbContextOptions<AtoCopilotContext> _options = null!;
    private TenantContext _providerContext = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options;
        _providerContext = new(Guid.Empty, isCspAdmin: true);
        _context = _providerContext;
        _accessor.SetupGet(x => x.Current).Returns(() => _context);
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
        var provider = new CspProfile { DisplayName = "Provider" };
        var tenant = new Tenant { Id = _tenantId, DisplayName = "Mission" };
        _offering = new() { ProviderId = provider.Id, Name = "Offering" };
        _offering.OfferingId = _offering.Id;
        var hosting = new ProviderHostingScopeRevision { ProviderId = provider.Id, OfferingId = _offering.Id };
        _offering.CurrentHostingScopeRevisionId = hosting.Id;
        _assignment = new() { ProviderId = provider.Id, OfferingId = _offering.Id, TargetTenantId = _tenantId,
            SystemId = _systemId, HostingScopeRevisionId = hosting.Id };
        var finding = new ProviderFinding { ProviderId = provider.Id, OfferingId = _offering.Id, Title = "Private finding" };
        _evidence = new() { ProviderId = provider.Id, OfferingId = _offering.Id, FindingId = finding.Id,
            FileName = "private.txt", StorageKey = "private/key", ByteLength = _bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(_bytes)), MediaType = "text/plain" };
        db.AddRange(provider, tenant, _offering, hosting, _assignment, finding, _evidence,
            new MissionProviderRelationshipReview { ProviderId = provider.Id, OfferingId = _offering.Id,
                TenantId = _tenantId, SystemId = _systemId, AssignmentId = _assignment.Id, AssignmentRevision = 1 });
        db.RegisteredSystems.Add(new() { Id = _systemId, TenantId = _tenantId, Name = "Mission" });
        await db.SaveChangesAsync();
        _storage.Setup(x => x.GetAsync("private/key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(_bytes));
        _mission.Setup(x => x.AuthorizeAsync(It.IsAny<string>(), false, false, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private AtoCopilotContext Db() => new(_options, _accessor.Object);
    private ProviderEvidenceSharingService Service()
    {
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Db);
        return new(new(factory.Object, _context, NullLogger<ProviderAuthorizationStore>.Instance),
            _storage.Object, _mission.Object, Mock.Of<ISystemWorkspaceAccessService>());
    }
    private ApproveProviderEvidenceShareRequest Request(Guid? previous = null, long version = 1) =>
        new(_assignment.Id, 1, 1, "Reviewed customer-facing summary", previous, version);
    private void Mission(Guid? tenant = null) =>
        _context = new TenantContext(tenant ?? _tenantId) { PersonId = Guid.NewGuid() };
    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task ApprovedSummary_PersistsExactProvenance_WithoutPrivateBytes()
    {
        // Arrange
        var service = Service();
        // Act
        var approved = await service.ApproveAsync(_offering.Id, _evidence.Id, Request(), "approve", "provider");
        var replay = await service.ApproveAsync(_offering.Id, _evidence.Id, Request(), "approve", "provider");
        Mission();
        var visible = await Service().ListMissionAsync(_systemId, 1, 25);
        var content = await Service().SummaryContentAsync(_systemId, approved.ShareId);
        // Assert
        replay.Should().BeEquivalentTo(approved);
        visible.Items.Should().ContainSingle();
        approved.SourceSha256.Should().Be(_evidence.Sha256);
        approved.ContentHash.Should().Be(Convert.ToHexString(SHA256.HashData(content)));
        Encoding.UTF8.GetString(content).Should().Contain("Reviewed customer-facing summary")
            .And.NotContain("PRIVATE").And.NotContain("private/key").And.NotContain("private.txt");
        await using var db = Db();
        (await db.Set<ProviderEvidenceShare>().IgnoreQueryFilters().SingleAsync()).ApprovedBy.Should().Be("provider");
    }

    [Fact]
    public async Task HistoricalRemovedScope_ExcludesCurrentEvidenceTargetsAndReads_WithoutDeletingRetainedShare()
    {
        // Arrange
        var approved = await Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "approve", "provider");
        await using (var db = Db())
        {
            db.Add(new SystemProviderScopeSelection { TenantId = _tenantId, SystemId = _systemId,
                AssignmentId = _assignment.Id, State = "Removed", UpdatedBy = "historical-removal" });
            await db.SaveChangesAsync();
        }
        // Act
        var targets = await Service().TargetsAsync(_offering.Id, 1, 25);
        Mission();
        var current = await Service().ListMissionAsync(_systemId, 1, 25);
        // Assert
        targets.Items.Should().BeEmpty();
        current.Items.Should().BeEmpty();
        await FluentActions.Awaiting(() => Service().SummaryContentAsync(_systemId, approved.ShareId))
            .Should().ThrowAsync<KeyNotFoundException>();
        _context = _providerContext;
        (await Service().ListProviderAsync(_offering.Id, _evidence.Id, 1, 25)).Items.Should().Contain(x => x.ShareId == approved.ShareId);
        await using var verify = Db();
        var retained = await verify.Set<ProviderEvidenceShare>().IgnoreQueryFilters().SingleAsync();
        retained.Id.Should().Be(approved.ShareId);
        retained.ContentHash.Should().Be(approved.ContentHash);
        retained.RevokedAt.Should().BeNull("history is retained rather than rewritten as an automatic revocation");
    }

    [Fact]
    public async Task CrossTenantWrongSystemRevoked_AndRevokedMembership_DenyReads()
    {
        // Arrange
        var approved = await Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "approve", "provider");
        // Act
        Mission(Guid.NewGuid());
        var otherTenant = await Service().ListMissionAsync(_systemId, 1, 25);
        var deniedTenant = () => Service().SummaryContentAsync(_systemId, approved.ShareId);
        // Assert
        otherTenant.Items.Should().BeEmpty();
        await deniedTenant.Should().ThrowAsync<KeyNotFoundException>();
        Mission();
        await FluentActions.Awaiting(() => Service().SummaryContentAsync("wrong-system", approved.ShareId))
            .Should().ThrowAsync<KeyNotFoundException>();
        _mission.Setup(x => x.AuthorizeAsync(_systemId, false, false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        await FluentActions.Awaiting(() => Service().SummaryContentAsync(_systemId, approved.ShareId))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        _mission.Setup(x => x.AuthorizeAsync(_systemId, false, false, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _context = _providerContext;
        await Service().RevokeAsync(_offering.Id, approved.ShareId, new(1, "Customer access withdrawn"), "revoke", "provider");
        Mission();
        (await Service().ListMissionAsync(_systemId, 1, 25)).Items.Should().BeEmpty();
        await FluentActions.Awaiting(() => Service().SummaryContentAsync(_systemId, approved.ShareId))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task PrivateByDefault_RequiresAssociation_AndVerifiesActualSourceHash()
    {
        // Arrange
        Mission();
        // Act
        var initial = await Service().ListMissionAsync(_systemId, 1, 25);
        _context = _providerContext;
        _storage.Setup(x => x.GetAsync("private/key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes("altered")));
        // Assert
        initial.Items.Should().BeEmpty();
        await FluentActions.Awaiting(() => Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "bad", "provider"))
            .Should().ThrowAsync<IOException>();
        await using var db = Db();
        (await db.Set<ProviderEvidenceShare>().CountAsync()).Should().Be(0);
        db.RemoveRange(await db.Set<MissionProviderRelationshipReview>().IgnoreQueryFilters().ToListAsync());
        await db.SaveChangesAsync();
        await FluentActions.Awaiting(() => Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "unassociated", "provider"))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Replacement_RetainsOldVersionButRevokesItsAccess_AndFencesChanges()
    {
        // Arrange
        var first = await Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "first", "provider");
        // Act
        var second = await Service().ApproveAsync(_offering.Id, _evidence.Id,
            Request(first.ShareId, 2) with { Summary = "Version two" }, "second", "provider");
        // Assert
        second.PreviousVersionId.Should().Be(first.ShareId);
        second.Version.Should().Be(2);
        await FluentActions.Awaiting(() => Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "first", "other-actor"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        await FluentActions.Awaiting(() => Service().RevokeAsync(_offering.Id, second.ShareId, new(99, "Stale"), "stale", "provider"))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        Mission();
        (await Service().ListMissionAsync(_systemId, 1, 25)).Items.Select(x => x.ShareId).Should().Equal(second.ShareId);
        await FluentActions.Awaiting(() => Service().SummaryContentAsync(_systemId, first.ShareId))
            .Should().ThrowAsync<KeyNotFoundException>();
        await using var db = Db();
        (await db.Set<ProviderEvidenceShare>().IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task AdditiveSchema_ReplaysAndPersistsApprovedVersionsWithOwnershipForeignKeys()
    {
        // Arrange
        await using var db = Db();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE ProviderEvidenceShares");
        // Act
        await ProviderEvidenceSharingSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var grant = await Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "schema", "provider");
        await ProviderEvidenceSharingSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        var retained = await db.Set<ProviderEvidenceShare>().SingleAsync();
        retained.ContentHash.Should().Be(grant.ContentHash);
        retained.Summary.Should().Be(grant.Summary);
        var invalid = new ProviderEvidenceShare { ProviderId = _offering.ProviderId, OfferingId = _offering.Id,
            AssignmentId = _assignment.Id, EvidenceId = _evidence.Id, TargetTenantId = Guid.NewGuid(),
            SystemId = _systemId, Version = 2 };
        db.Add(invalid);
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdditiveSchema_ColumnTypesAndNullabilityMatchProductionModel(bool sqlServer)
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AtoCopilotContext>();
        if (sqlServer) options.UseSqlServer("Server=unused;Database=metadata-only;Integrated Security=true");
        else options.UseSqlite("Data Source=:memory:");
        using var db = new AtoCopilotContext(options.Options);
        var entity = db.Model.FindEntityType(typeof(ProviderEvidenceShare))!;
        // Act
        var sql = string.Join("\n", ProviderEvidenceSharingSchemaAdditions.Scripts(sqlServer));
        // Assert
        foreach (var property in entity.GetProperties())
            sql.Should().Contain($"{property.Name} {property.GetColumnType()} {(property.IsNullable ? "NULL" : "NOT NULL")}");
        entity.GetQueryFilter().Should().NotBeNull();
        entity.FindProperty(nameof(ProviderEvidenceShare.Revision))!.IsConcurrencyToken.Should().BeTrue();
        sql.Should().Contain("ProviderId, OfferingId, TargetTenantId, SystemId, AssignmentId")
            .And.Contain("CREATE UNIQUE INDEX").And.NotContain("DELETE FROM").And.NotContain("DROP TABLE");
    }

    [Fact]
    public async Task Approval_DeniesMissionImpersonationWrongSourceAndWrongTarget()
    {
        // Arrange
        Mission();
        // Act / Assert
        await FluentActions.Awaiting(() => Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "mission", "mission"))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        _context = new TenantContext(Guid.Empty, isCspAdmin: true, impersonatedTenantId: _tenantId);
        await FluentActions.Awaiting(() => Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "support", "support"))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        _context = _providerContext;
        await FluentActions.Awaiting(() => Service().ApproveAsync(_offering.Id, Guid.NewGuid(), Request(), "source", "provider"))
            .Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Awaiting(() => Service().ApproveAsync(_offering.Id, _evidence.Id,
            Request() with { AssignmentId = Guid.NewGuid() }, "target", "provider"))
            .Should().ThrowAsync<KeyNotFoundException>();
        await using var db = Db();
        (await db.Set<ProviderEvidenceShare>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RevokedAssociationAndChangedAssignment_RemovePreviouslyGrantedAccess()
    {
        // Arrange
        var grant = await Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "access", "provider");
        await using (var db = Db())
        {
            var assignment = await db.Set<ProviderHostingAssignment>().SingleAsync();
            assignment.Revision++;
            await db.SaveChangesAsync();
        }
        // Act
        Mission();
        var result = await Service().ListMissionAsync(_systemId, 1, 25);
        // Assert
        result.Items.Should().BeEmpty();
        await FluentActions.Awaiting(() => Service().SummaryContentAsync(_systemId, grant.ShareId))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task HistoricalAdoptionWithoutCurrentActiveSubscription_DoesNotGrantAccess()
    {
        // Arrange
        string subscriptionId;
        await using (var db = Db())
        {
            db.RemoveRange(await db.Set<MissionProviderRelationshipReview>().IgnoreQueryFilters().ToListAsync());
            var impact = new ProviderAuthorizationImpactReview { ProviderId = _offering.ProviderId, OfferingId = _offering.Id };
            var context = new ProviderCatalogContextSnapshot { ProviderId = _offering.ProviderId, OfferingId = _offering.Id, ImpactReviewId = impact.Id };
            var subscription = new CapabilitySubscription { RegisteredSystemId = _systemId, RoutingTenantId = _tenantId };
            var adoption = new CapabilityAdoptionSnapshot { ProviderId = _offering.ProviderId, OfferingId = _offering.Id,
                TenantId = _tenantId, SystemId = _systemId, AssignmentId = _assignment.Id, AssignmentRevision = 1,
                SubscriptionId = subscription.Id, ContextSnapshotId = context.Id };
            subscription.CurrentAdoptionSnapshotId = adoption.Id;
            subscriptionId = subscription.Id;
            db.AddRange(impact, context, subscription, adoption);
            await db.SaveChangesAsync();
        }
        var grant = await Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "adopted", "provider");
        await using (var db = Db())
        {
            var subscription = await db.CapabilitySubscriptions.IgnoreQueryFilters().SingleAsync(x => x.Id == subscriptionId);
            subscription.IsActive = false;
            await db.SaveChangesAsync();
        }
        // Act
        Mission();
        var visible = await Service().ListMissionAsync(_systemId, 1, 25);
        // Assert
        visible.Items.Should().BeEmpty();
        await FluentActions.Awaiting(() => Service().SummaryContentAsync(_systemId, grant.ShareId))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task WorkerVerification_UsesCapturedIdentityWithoutAmbientImpersonation_AndChecksPinnedHash()
    {
        // Arrange
        var grant = await Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "worker", "provider");
        var (worker, person, accessor) = await WorkerAsync();
        _storage.Invocations.Clear();
        // Act
        await worker.VerifyForExportAsync(_tenantId, person, _systemId, grant.ShareId, grant.ContentHash);
        // Assert
        accessor.Current.Should().BeNull();
        _storage.Invocations.Should().BeEmpty("verification must not retrieve private source bytes");
        await FluentActions.Awaiting(() => worker.VerifyForExportAsync(_tenantId, person, _systemId, grant.ShareId, new string('0', 64)))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var db = Db();
        var row = await db.Set<ProviderEvidenceShare>().SingleAsync();
        row.ContentJson += "tampered";
        await db.SaveChangesAsync();
        await FluentActions.Awaiting(() => worker.VerifyForExportAsync(_tenantId, person, _systemId, grant.ShareId, grant.ContentHash))
            .Should().ThrowAsync<IOException>();
    }

    [Theory]
    [InlineData("revoked-membership")]
    [InlineData("removed-role")]
    [InlineData("disabled-tenant")]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-person")]
    [InlineData("wrong-system")]
    public async Task WorkerVerification_RechecksActualPersistedActorSystemAccess(string denial)
    {
        // Arrange
        var grant = await Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "worker-denial", "provider");
        var (worker, person, _) = await WorkerAsync();
        await using (var db = Db())
        {
            if (denial == "revoked-membership")
                (await db.OrganizationMemberships.IgnoreQueryFilters().SingleAsync()).RevokedAt = DateTimeOffset.UtcNow;
            if (denial == "removed-role")
                (await db.SystemRoleAssignments.IgnoreQueryFilters().SingleAsync()).RemovedAt = DateTimeOffset.UtcNow;
            if (denial == "disabled-tenant")
                (await db.Tenants.SingleAsync()).Status = TenantStatus.Disabled;
            await db.SaveChangesAsync();
        }
        // Act
        var action = () => worker.VerifyForExportAsync(denial == "wrong-tenant" ? Guid.NewGuid() : _tenantId,
            denial == "wrong-person" ? Guid.NewGuid() : person, denial == "wrong-system" ? "other" : _systemId,
            grant.ShareId, grant.ContentHash);
        // Assert
        await action.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("assignment-changed")]
    [InlineData("association-removed")]
    public async Task WorkerVerification_RechecksGrantAndEligibility_WithoutDeletingRetainedHistory(string denial)
    {
        // Arrange
        var grant = await Service().ApproveAsync(_offering.Id, _evidence.Id, Request(), "worker-grant", "provider");
        var (worker, person, _) = await WorkerAsync();
        await using (var db = Db())
        {
            if (denial == "revoked")
                (await db.Set<ProviderEvidenceShare>().SingleAsync()).RevokedAt = DateTimeOffset.UtcNow;
            if (denial == "assignment-changed")
                (await db.Set<ProviderHostingAssignment>().SingleAsync()).Revision++;
            if (denial == "association-removed")
                db.RemoveRange(await db.Set<MissionProviderRelationshipReview>().IgnoreQueryFilters().ToListAsync());
            await db.SaveChangesAsync();
        }
        // Act
        var action = () => worker.VerifyForExportAsync(_tenantId, person, _systemId, grant.ShareId, grant.ContentHash);
        // Assert
        await action.Should().ThrowAsync<KeyNotFoundException>();
        await using var retained = Db();
        (await retained.Set<ProviderEvidenceShare>().SingleAsync()).ContentHash.Should().Be(grant.ContentHash);
    }

    private async Task<(ProviderEvidenceSharingService Worker, Guid Person, TenantContextAccessor Accessor)> WorkerAsync()
    {
        var person = new Person { TenantId = _tenantId, DisplayName = "Export requester", Email = "export@example.invalid" };
        await using (var db = Db())
        {
            db.Persons.Add(person);
            db.OrganizationMemberships.Add(new() { TenantId = _tenantId, PersonId = person.Id,
                DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "fixture" });
            db.SystemRoleAssignments.Add(new() { TenantId = _tenantId, PersonId = person.Id,
                RegisteredSystemId = _systemId, Role = Ato.Copilot.Core.Models.Onboarding.OrganizationRole.MissionOwner });
            await db.SaveChangesAsync();
        }
        var accessor = new TenantContextAccessor();
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(_options, accessor));
        var worker = new ProviderEvidenceSharingService(
            new(factory.Object, new TenantContext(), NullLogger<ProviderAuthorizationStore>.Instance),
            _storage.Object, _mission.Object, new SystemWorkspaceAccessService(factory.Object, accessor));
        return (worker, person.Id, accessor);
    }
}

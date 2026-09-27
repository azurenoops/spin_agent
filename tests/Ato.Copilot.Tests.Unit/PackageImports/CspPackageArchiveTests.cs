using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.PackageImports.CspPackageServiceTests.Fixture;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed class CspPackageArchiveTests
{
    [Fact]
    public async Task Archive_PreservesHistoryBytesAndStates_ExcludesActiveList_AndReplaysOnce()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.AnalyzeAsync();
        var other = await fixture.ReceiveAsync("unrelated", "keep me");
        var candidates = await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, null, null, default);
        var entries = await fixture.Service.EntriesAsync(receipt.PackageId, 1, 25, default);
        var request = new ArchivePackageRequest(receipt.Revision, "Superseded synthetic receipt");
        var files = fixture.Files.ToDictionary(x => x.Key, x => x.Value.ToArray());
        await using var db = fixture.Factory.CreateDbContext();
        var approval = new CspPackageApproval { PackageId = receipt.PackageId, Revision = receipt.Revision };
        db.CspPackageApprovals.Add(approval);
        await db.SaveChangesAsync();
        var auditCount = await db.CspPackageAudits.CountAsync(x => x.PackageId == receipt.PackageId);

        // Act
        var archived = await fixture.Service.ArchiveAsync(receipt.PackageId, request, "test", default);
        var replay = await fixture.Service.ArchiveAsync(receipt.PackageId, request, "test", default);
        var history = await fixture.Service.HistoryAsync(receipt.PackageId, default);
        var list = await fixture.Service.ListAsync(1, 25, default);
        var source = await fixture.Service.ContentAsync(receipt.PackageId, entries.Items.First().ArtifactId, "test", default);
        await using var stream = source.Content;

        // Assert
        archived.ArchivedAt.Should().NotBeNull();
        archived.ArchivedBy.Should().Be("test");
        archived.ArchiveReason.Should().Be(request.Reason);
        archived.Revision.Should().Be(receipt.Revision + 1);
        archived.ProcessingState.Should().Be(receipt.ProcessingState);
        archived.PublicationState.Should().Be(receipt.PublicationState);
        replay.Should().BeEquivalentTo(archived);
        history.Package.Should().BeEquivalentTo(archived);
        history.Audits.Should().ContainSingle(x => x.Action == "Archived");
        history.Audits.Should().Contain(x => x.Action == "ProcessingClaimed");
        history.Audits.Should().HaveCount(auditCount + 1);
        history.Audits.Single(x => x.Action == "Archived").Detail.Should().Contain(request.Reason);
        (await db.CspPackageApprovals.AsNoTracking().SingleAsync()).Should().BeEquivalentTo(approval);
        list.Items.Should().ContainSingle().Which.PackageId.Should().Be(other.PackageId);
        list.Total.Should().Be(1);
        (await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, null, null, default))
            .Should().BeEquivalentTo(candidates);
        (await fixture.Service.EntriesAsync(receipt.PackageId, 1, 25, default)).Should().BeEquivalentTo(entries);
        fixture.Files.Should().BeEquivalentTo(files);
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).Should().Be("synthetic supporting quote");
        var retry = () => fixture.Service.RetryAsync(receipt.PackageId, "retry", "test", default);
        await retry.Should().ThrowAsync<DbUpdateConcurrencyException>();
        var different = () => fixture.Service.ArchiveAsync(receipt.PackageId,
            request with { Reason = "different reason" }, "test", default);
        await different.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Archive_PreservesArchivedReceiptIdentityDuringUploadReplay()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.AnalyzeAsync();
        await fixture.Service.ArchiveAsync(receipt.PackageId, new(receipt.Revision, "Superseded"), "test", default);
        // Act
        var replay = await fixture.ReceiveAsync("analysis", "synthetic supporting quote");
        // Assert
        replay.PackageId.Should().Be(receipt.PackageId);
        replay.ArchivedAt.Should().NotBeNull();
        (await fixture.Service.ListAsync(1, 25, default)).Total.Should().Be(0);
        await using var db = fixture.Factory.CreateDbContext();
        (await db.CspPackages.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.CspPackageAudits.CountAsync(x => x.Action == "Archived")).Should().Be(1);
    }

    [Theory]
    [InlineData("Published", "Ready", false)]
    [InlineData("PartiallyPublished", "Ready", false)]
    [InlineData("Publishing", "Ready", false)]
    [InlineData("Unpublished", "Processing", false)]
    [InlineData("Unpublished", "Received", false)]
    [InlineData("Unpublished", "NeedsAttention", true)]
    public async Task Archive_RejectsPublicationOrWorkerWork(string publication, string processing, bool lease)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.AnalyzeAsync();
        await using var db = fixture.Factory.CreateDbContext();
        var row = await db.CspPackages.SingleAsync();
        row.PublicationState = publication;
        row.ProcessingState = processing;
        row.LeaseId = lease ? Guid.NewGuid() : null;
        row.LeaseExpiresTicks = lease ? DateTimeOffset.UtcNow.AddMinutes(1).UtcTicks : 0;
        await db.SaveChangesAsync();
        // Act
        var archive = () => fixture.Service.ArchiveAsync(receipt.PackageId, new(receipt.Revision, "Superseded"), "test", default);
        // Assert
        await archive.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await db.CspPackageAudits.CountAsync(x => x.Action == "Archived")).Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Archive_RequiresOrdinaryProviderAdmin(bool admin, bool impersonated)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.AnalyzeAsync();
        fixture.Tenant.SetupGet(x => x.IsCspAdmin).Returns(admin);
        fixture.Tenant.SetupGet(x => x.ImpersonatedTenantId).Returns(impersonated ? Guid.NewGuid() : null);
        // Act
        var archive = () => fixture.Service.ArchiveAsync(receipt.PackageId, new(receipt.Revision, "Superseded"), "test", default);
        var history = () => fixture.Service.HistoryAsync(receipt.PackageId, default);
        // Assert
        await archive.Should().ThrowAsync<UnauthorizedAccessException>();
        await history.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Archive_RejectsStaleRevisionAndCanonicalContext()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.AnalyzeAsync();
        var stale = () => fixture.Service.ArchiveAsync(receipt.PackageId, new(receipt.Revision - 1, "Superseded"), "test", default);
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var db = fixture.Factory.CreateDbContext();
        var offering = new ProviderOffering { ProviderId = fixture.ProviderId };
        offering.OfferingId = offering.Id;
        var impact = new ProviderAuthorizationImpactReview { ProviderId = fixture.ProviderId, OfferingId = offering.Id };
        db.AddRange(offering, impact);
        db.Set<ProviderCatalogContextSnapshot>().Add(new()
        {
            ProviderId = fixture.ProviderId, OfferingId = offering.Id, ReleaseId = Guid.NewGuid(), ImpactReviewId = impact.Id,
            SnapshotJson = $$"""{"PackageVersions":[{"PackageId":"{{receipt.PackageId}}"}]}"""
        });
        await db.SaveChangesAsync();
        // Act
        var archive = () => fixture.Service.ArchiveAsync(receipt.PackageId, new(receipt.Revision, "Superseded"), "test", default);
        // Assert
        await archive.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await db.CspPackageAudits.CountAsync(x => x.Action == "Archived")).Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Archive_RejectsEmptyRationale(string reason)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.AnalyzeAsync();
        // Act
        var archive = () => fixture.Service.ArchiveAsync(receipt.PackageId, new(receipt.Revision, reason), "test", default);
        // Assert
        await archive.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Archive_RejectsAnotherProvidersReceipt()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Factory.CreateDbContext();
        var foreign = new CspPackage { ProviderId = Guid.NewGuid(), ProcessingState = "NeedsAttention" };
        db.CspPackages.Add(foreign);
        await db.SaveChangesAsync();
        // Act
        var archive = () => fixture.Service.ArchiveAsync(foreign.Id, new(1, "Superseded"), "test", default);
        var history = () => fixture.Service.HistoryAsync(foreign.Id, default);
        // Assert
        await archive.Should().ThrowAsync<KeyNotFoundException>();
        await history.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Archive_RejectsPublishedApprovalEvenWithLegacyUnpublishedFlag()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.AnalyzeAsync();
        await using var db = fixture.Factory.CreateDbContext();
        db.CspPackageApprovals.Add(new() { PackageId = receipt.PackageId, State = "Published", PublicationJson = "{}" });
        await db.SaveChangesAsync();
        // Act
        var archive = () => fixture.Service.ArchiveAsync(receipt.PackageId, new(receipt.Revision, "Superseded"), "test", default);
        // Assert
        await archive.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await db.CspPackages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ArchiveColumns_UpgradeLegacyLedgerTwiceWithoutHistoryLoss()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.AnalyzeAsync();
        await using var db = fixture.Factory.CreateDbContext();
        var audits = await db.CspPackageAudits.CountAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackages DROP COLUMN ArchivedAt");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackages DROP COLUMN ArchivedBy");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackages DROP COLUMN ArchiveReason");
        // Act
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        var retained = await fixture.Service.GetAsync(receipt.PackageId, default);
        retained.Should().BeEquivalentTo(receipt);
        (await db.CspPackageAudits.CountAsync()).Should().Be(audits);
        string.Join("\n", CspPackageSchemaAdditions.Scripts(true))
            .Should().Contain("ArchivedAt").And.Contain("ArchivedBy").And.Contain("ArchiveReason")
            .And.NotContain("DELETE ").And.NotContain("DROP ");
    }
}

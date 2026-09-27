using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.PackageImports.CspPackageServiceTests.Fixture;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed class CspPackageSupersedeReviewTests
{
    [Fact]
    public async Task SupersededReview_ReadsSavedPreviewAsHistoryWithoutRevalidatingRetiredCandidates()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var (source, replacement) = await SeedAsync(fixture);
        await using var db = fixture.Factory.CreateDbContext();
        var candidate = await db.CspPackageCandidates.FirstAsync(x => x.PackageId == source.PackageId);
        var approval = new CspPackageApproval
        {
            PackageId = source.PackageId, Revision = source.Revision, CreatedVersion = 1,
            SnapshotJson = "{\"Candidates\":[]}",
            SelectionJson = $$"""[{"CandidateId":"{{candidate.Id}}","Revision":{{candidate.Revision}}}]"""
        };
        db.CspPackageApprovals.Add(approval);
        await db.SaveChangesAsync();
        await fixture.Service.SupersedeReviewAsync(source.PackageId,
            new(source.Revision, replacement.PackageId, "Superseded"), "test", default);
        // Act
        var history = await fixture.Service.ReviewStateAsync(source.PackageId, default);
        // Assert
        history.Preview!.PreviewId.Should().Be(approval.Id);
        history.PreviewIsStale.Should().BeTrue();
        (await db.CspPackageApprovals.AsNoTracking().SingleAsync()).Should().BeEquivalentTo(approval);
    }

    [Fact]
    public async Task SupersessionColumns_UpgradeLegacySchemaAndPreserveDecisionOnRepeat()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var (source, replacement) = await SeedAsync(fixture);
        await using var db = fixture.Factory.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackages DROP COLUMN SupersededByPackageId");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackages DROP COLUMN SupersededAt");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackages DROP COLUMN SupersededBy");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackages DROP COLUMN SupersedeReason");
        // Act
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var retired = await fixture.Service.SupersedeReviewAsync(source.PackageId,
            new(source.Revision, replacement.PackageId, "Superseded"), "test", default);
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        (await fixture.Service.GetAsync(source.PackageId, default)).Should().BeEquivalentTo(retired);
        (await db.Set<ProviderCatalogContextSnapshot>().CountAsync()).Should().Be(1);
        string.Join("\n", CspPackageSchemaAdditions.Scripts(true))
            .Should().Contain("SupersededByPackageId").And.Contain("SupersededAt").And.Contain("SupersededBy").And.Contain("SupersedeReason");
    }

    [Fact]
    public async Task SupersedeReview_PreservesCanonicalSourceAndBytes_WithoutFakeReviewOrDuplicateAudit()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var (source, replacement) = await SeedAsync(fixture);
        var candidates = await fixture.Service.CandidatesAsync(source.PackageId, 1, 25, null, null, default);
        var entries = await fixture.Service.EntriesAsync(source.PackageId, 1, 25, default);
        var request = new SupersedePackageReviewRequest(source.Revision, replacement.PackageId, "Replaced review work");
        var archive = () => fixture.Service.ArchiveAsync(source.PackageId, new(source.Revision, "Cannot hide canonical source"), "test", default);
        await archive.Should().ThrowAsync<DbUpdateConcurrencyException>();
        // Act
        var retired = await fixture.Service.SupersedeReviewAsync(source.PackageId, request, "test", default);
        var replay = await fixture.Service.SupersedeReviewAsync(source.PackageId, request, "test", default);
        // Assert
        retired.SupersededByPackageId.Should().Be(replacement.PackageId);
        retired.SupersededAt.Should().NotBeNull();
        retired.ArchivedAt.Should().BeNull();
        retired.ProcessingState.Should().Be(source.ProcessingState);
        retired.PublicationState.Should().Be("Unpublished");
        replay.Should().BeEquivalentTo(retired);
        (await fixture.Service.CandidatesAsync(source.PackageId, 1, 25, null, null, default)).Should().BeEquivalentTo(candidates);
        (await fixture.Service.ListAsync(1, 25, default)).Items.Should().ContainSingle().Which.PackageId.Should().Be(replacement.PackageId);
        var history = await fixture.Service.HistoryAsync(source.PackageId, default);
        history.Audits.Should().ContainSingle(x => x.Action == "ReviewSuperseded");
        await using var db = fixture.Factory.CreateDbContext();
        (await db.CspPackages.CountAsync()).Should().Be(2);
        (await db.Set<ProviderPackageVersion>().CountAsync()).Should().Be(2);
        (await db.Set<ProviderCatalogContextSnapshot>().SingleAsync()).SnapshotJson.Should().Contain(source.PackageId.ToString());
        var content = await fixture.Service.ContentAsync(source.PackageId, entries.Items.First().ArtifactId, "test", default);
        using var reader = new StreamReader(content.Content);
        (await reader.ReadToEndAsync()).Should().Be("synthetic supporting quote");
        var retry = () => fixture.Service.RetryAsync(source.PackageId, "no-mutation", "test", default);
        await retry.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Theory]
    [InlineData("source-published")]
    [InlineData("source-processing")]
    [InlineData("source-lease")]
    [InlineData("replacement-unpublished")]
    [InlineData("replacement-wrong-offering")]
    [InlineData("replacement-old-version")]
    [InlineData("stale")]
    [InlineData("self")]
    [InlineData("not-admin")]
    [InlineData("support")]
    public async Task SupersedeReview_RejectsUnsafeOrUnauthorizedDecision(string scenario)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var (source, replacement) = await SeedAsync(fixture);
        await using var db = fixture.Factory.CreateDbContext();
        var old = await db.CspPackages.SingleAsync(x => x.Id == source.PackageId);
        var next = await db.CspPackages.SingleAsync(x => x.Id == replacement.PackageId);
        if (scenario == "source-published") old.PublicationState = "Published";
        if (scenario == "source-processing") old.ProcessingState = "Processing";
        if (scenario == "source-lease") old.LeaseExpiresTicks = DateTimeOffset.UtcNow.AddMinutes(5).UtcTicks;
        if (scenario == "replacement-unpublished") next.PublicationState = "Unpublished";
        if (scenario == "replacement-wrong-offering") next.OfferingId = Guid.NewGuid();
        if (scenario == "replacement-old-version")
        {
            var version = await db.Set<ProviderPackageVersion>().SingleAsync(x => x.Id == next.PackageVersionId);
            var newer = await db.Set<ProviderPackageVersion>().SingleAsync(x => x.Id == old.PackageVersionId);
            newer.SeriesId = version.SeriesId;
            newer.Version = 2;
        }
        await db.SaveChangesAsync();
        if (scenario == "not-admin") fixture.Tenant.SetupGet(x => x.IsCspAdmin).Returns(false);
        if (scenario == "support") fixture.Tenant.SetupGet(x => x.ImpersonatedTenantId).Returns(Guid.NewGuid());
        // Act
        var act = () => fixture.Service.SupersedeReviewAsync(source.PackageId,
            new(scenario == "stale" ? source.Revision - 1 : source.Revision,
                scenario == "self" ? source.PackageId : replacement.PackageId, "Superseded"), "test", default);
        // Assert
        if (scenario is "not-admin" or "support") await act.Should().ThrowAsync<UnauthorizedAccessException>();
        else await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await db.CspPackageAudits.CountAsync(x => x.Action == "ReviewSuperseded")).Should().Be(0);
    }

    internal static async Task<(PackageStatus Source, PackageStatus Replacement)> SeedAsync(Fixture fixture)
    {
        var source = await fixture.AnalyzeAsync();
        var replacement = await fixture.ReceiveAsync("replacement", "published replacement source");
        await using var db = fixture.Factory.CreateDbContext();
        var offering = new ProviderOffering { ProviderId = fixture.ProviderId };
        offering.OfferingId = offering.Id;
        var boundary = new ProviderBoundaryRevision { ProviderId = fixture.ProviderId, OfferingId = offering.Id };
        var impact = new ProviderAuthorizationImpactReview { ProviderId = fixture.ProviderId, OfferingId = offering.Id };
        db.AddRange(offering, boundary, impact);
        foreach (var id in new[] { source.PackageId, replacement.PackageId })
        {
            var row = await db.CspPackages.SingleAsync(x => x.Id == id);
            var version = new ProviderPackageVersion
            {
                ProviderId = fixture.ProviderId, OfferingId = offering.Id, PackageId = id,
                BoundaryRevisionId = boundary.Id, SeriesId = Guid.NewGuid(), Version = 1
            };
            row.OfferingId = offering.Id;
            row.BoundaryRevisionId = boundary.Id;
            row.PackageVersionId = version.Id;
            if (id == replacement.PackageId) { row.PublicationState = "Published"; row.ProcessingState = "ReadyForReview"; }
            db.Add(version);
        }
        db.Add(new ProviderCatalogContextSnapshot
        {
            ProviderId = fixture.ProviderId, OfferingId = offering.Id, ImpactReviewId = impact.Id, ReleaseId = Guid.NewGuid(),
            SnapshotJson = $$"""{"PackageVersions":[{"PackageId":"{{source.PackageId}}"}]}"""
        });
        await db.SaveChangesAsync();
        return (source, replacement);
    }
}

using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.ProviderAuthorizations.OfferingBoundaryOverviewTests.Fixture;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class OfferingArchiveCountsTests
{
    [Fact]
    public async Task SupersededReview_LeavesCanonicalContextsAndSourceCounts_ButRemovesDraftWork()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(true);
        var retired = await fixture.AddCandidateAsync("Retired source");
        var active = await fixture.AddCandidateAsync("Active source");
        var published = await fixture.AddPublishedAsync("Published source context");
        var sourceVersion = new ProviderPackageVersion
        {
            ProviderId = fixture.Provider, OfferingId = fixture.Offering, PackageId = retired.PackageId,
            BoundaryRevisionId = fixture.SourceBoundary, Version = 1
        };
        string contextJson;
        await using (var db = fixture.Db())
        {
            var row = await db.CspPackages.SingleAsync(x => x.Id == retired.PackageId);
            row.SupersededAt = DateTimeOffset.UtcNow;
            row.SupersededByPackageId = active.PackageId;
            db.Add(sourceVersion);
            db.CspPackageEntries.Add(new() { PackageId = retired.PackageId, StableKey = "retained-source" });
            db.CspPackageEntries.Add(new() { PackageId = active.PackageId, StableKey = "active-source" });
            var context = await db.Set<ProviderCatalogContextSnapshot>().SingleAsync(x => x.Id == published.ContextId);
            (await db.ProviderCapabilityReleases.SingleAsync(x => x.Id == published.ReleaseId)).SnapshotJson = "{}";
            (await db.Set<ProviderBoundaryRevision>().SingleAsync(x => x.Id == fixture.SourceBoundary)).SnapshotJson = "{\"componentSnapshotIds\":[]}";
            var material = ProviderAuthorizationStore.Read<Ato.Copilot.Core.Interfaces.ProviderAuthorizations.ProviderPublicationContextMaterial>(context.SnapshotJson);
            contextJson = ProviderAuthorizationStore.Json(material with
            {
                PackageVersions = [new(sourceVersion.Id, retired.PackageId, sourceVersion.ManifestHash)]
            });
            context.SnapshotJson = contextJson;
            await db.SaveChangesAsync();
        }
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => fixture.Db());
        var impact = new ProviderImpactService(new(factory.Object, fixture.Tenant, NullLogger<ProviderAuthorizationStore>.Instance));
        // Act
        var overview = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 25, default);
        var boundary = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 25, default);
        var exact = () => impact.OptionAsync(fixture.Offering, "Capability", retired.Id, default);
        // Assert
        overview.Packages.Total.Should().Be(1);
        overview.Packages.AwaitingReview.Should().Be(1);
        overview.Packages.SourceDocumentCount.Should().Be(2);
        boundary.Capabilities.Total.Should().Be(2);
        boundary.Capabilities.Published.Should().Be(1);
        boundary.Capabilities.AwaitingReview.Should().Be(1);
        await exact.Should().ThrowAsync<KeyNotFoundException>();
        (await impact.OptionsAsync(fixture.Offering, "Capability", 1, 25, default)).Items.Should().NotContain(x => x.Id == retired.Id);
        (await impact.OptionAsync(fixture.Offering, "Package", sourceVersion.Id, default)).Id.Should().Be(sourceVersion.Id);
        await using var retained = fixture.Db();
        (await retained.CspPackages.CountAsync()).Should().Be(2);
        (await retained.CspPackageCandidates.SingleAsync(x => x.Id == retired.Id)).ReviewState.Should().Be("NeedsReview");
        (await retained.Set<ProviderCatalogContextSnapshot>().SingleAsync()).SnapshotJson.Should().Be(contextJson);
        var newChange = () => ProviderImpactService.ChangeAsync(retained, fixture.Provider, "Capability", retired.Id, default);
        await newChange.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData("Capability")]
    [InlineData("Component")]
    [InlineData("Package")]
    public async Task Archive_ExcludesSourceImpactOptionListsAndExactLookups(string kind)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(true);
        var candidate = await fixture.AddCandidateAsync("Archived source", type: kind == "Package" ? "Capability" : kind);
        var id = candidate.Id;
        await using (var db = fixture.Db())
        {
            var package = await db.CspPackages.SingleAsync(x => x.Id == candidate.PackageId);
            package.ArchivedAt = DateTimeOffset.UtcNow;
            if (kind == "Package")
            {
                var version = new ProviderPackageVersion
                {
                    ProviderId = fixture.Provider, OfferingId = fixture.Offering, PackageId = package.Id,
                    BoundaryRevisionId = fixture.SourceBoundary, Version = 1
                };
                id = version.Id;
                db.Add(version);
            }
            await db.SaveChangesAsync();
        }
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => fixture.Db());
        var impact = new ProviderImpactService(new(factory.Object, fixture.Tenant, NullLogger<ProviderAuthorizationStore>.Instance));
        // Act
        var options = await impact.OptionsAsync(fixture.Offering, kind, 1, 25, default);
        var exact = () => impact.OptionAsync(fixture.Offering, kind, id, default);
        // Assert
        options.Total.Should().Be(0);
        await exact.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Archive_ExcludesLinkedAndDirectSourcesFromBothOverviews_PreservesPublished(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        var archived = await fixture.AddCandidateAsync("Superseded");
        var active = await fixture.AddCandidateAsync("Active");
        var published = await fixture.AddPublishedAsync("Canonical published capability");
        await using (var db = fixture.Db())
        {
            var row = await db.CspPackages.SingleAsync(x => x.Id == archived.PackageId);
            row.ArchivedAt = DateTimeOffset.UtcNow;
            row.ArchivedBy = "test";
            row.ArchiveReason = "Superseded";
            db.Add(new ProviderPackageVersion
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering, PackageId = row.Id,
                BoundaryRevisionId = fixture.SourceBoundary, Version = 1
            });
            db.CspPackageEntries.Add(new() { PackageId = row.Id, StableKey = "archived" });
            db.CspPackageEntries.Add(new() { PackageId = active.PackageId, StableKey = "active" });
            await db.SaveChangesAsync();
        }
        // Act
        var overview = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 25, default);
        var boundary = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 25, default);
        var versions = await fixture.Service.PackagesAsync(fixture.Offering, 1, 25, null, default);
        // Assert
        overview.Packages.Total.Should().Be(1);
        overview.Packages.AwaitingReview.Should().Be(1);
        overview.Packages.SourceDocumentCount.Should().Be(1);
        overview.Packages.Items.Should().ContainSingle();
        boundary.Capabilities.Total.Should().Be(2);
        boundary.Capabilities.AwaitingReview.Should().Be(1);
        boundary.Capabilities.Published.Should().Be(1);
        boundary.Capabilities.Items.Should().Contain(x => x.ReleaseId == published.ReleaseId);
        boundary.Capabilities.Items.Should().NotContain(x => x.PackageId == archived.PackageId);
        versions.Total.Should().Be(0);
        await using var retained = fixture.Db();
        (await retained.Set<ProviderPackageVersion>().CountAsync()).Should().Be(1);
        (await retained.CspPackageCandidates.CountAsync()).Should().Be(2);
        (await retained.CspPackages.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }
}

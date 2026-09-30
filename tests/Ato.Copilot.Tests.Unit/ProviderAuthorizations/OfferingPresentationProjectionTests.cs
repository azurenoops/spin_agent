using System.Text.Json;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using FluentAssertions;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.ProviderAuthorizations.OfferingBoundaryOverviewTests.Fixture;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class OfferingPresentationProjectionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overview_ProjectsOnlyCurrentOfferingPublishedRevisions_NotDocumentOrOfferingVersions(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        var first = await fixture.AddPublishedAsync("Audit", revision: 1);
        await fixture.AddPublishedAsync("Audit", capabilityId: first.CapabilityId, revision: 3);
        await using (var db = fixture.Db())
        {
            var otherImpact = new ProviderAuthorizationImpactReview { ProviderId = fixture.Provider, OfferingId = fixture.OtherOffering };
            var otherRelease = new ProviderCapabilityRelease { CapabilityId = first.CapabilityId, Revision = 99, IdempotencyKey = Guid.NewGuid().ToString() };
            db.AddRange(otherImpact, otherRelease, new ProviderCatalogContextSnapshot
            {
                ProviderId = fixture.Provider, OfferingId = fixture.OtherOffering,
                CapabilityId = first.CapabilityId, ReleaseId = otherRelease.Id, ImpactReviewId = otherImpact.Id
            });
            await db.SaveChangesAsync();
        }
        await fixture.AddPublishedAsync("Network", revision: 1);
        await fixture.AddPublishedAsync("Archived", revision: 8, status: CspInheritedCapabilityStatus.Archived);

        // Act
        var overview = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 1, default);
        var boundary = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);
        var overviewJson = JsonSerializer.SerializeToElement(overview, JsonOptions);
        var boundaryJson = JsonSerializer.SerializeToElement(boundary, JsonOptions);

        // Assert
        overviewJson.GetProperty("capabilities").GetProperty("publishedReleaseRevisions")
            .EnumerateArray().Select(x => x.GetInt64()).Should().Equal(1, 3);
        boundaryJson.GetProperty("capabilities").GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("capabilityId").GetGuid() == first.CapabilityId)
            .GetProperty("releaseRevision").GetInt64().Should().Be(3);
        overview.OfferingRevision.Should().Be(7);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overview_SourceAndFindingTotals_AreGlobalAcrossPagesAndExcludeForeignRecords(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        await using (var db = fixture.Db())
        {
            // SQLite ownership FKs reject a forged provider link; InMemory also exercises that defensive filter.
            var foreignProvider = sqlite ? fixture.Provider : Guid.NewGuid();
            var foreignOffering = sqlite ? fixture.OtherOffering : fixture.Offering;
            for (var i = 0; i < 4; i++)
            {
                var package = new CspPackage
                {
                    ProviderId = i == 3 ? foreignProvider : fixture.Provider,
                    OfferingId = i == 3 ? foreignOffering : i == 2 ? fixture.OtherOffering : fixture.Offering,
                    IdempotencyKey = Guid.NewGuid().ToString()
                };
                db.Add(package);
                db.AddRange(new CspPackageEntry { PackageId = package.Id, StableKey = "archive", FileName = "sources.zip", MediaType = "application/zip", Status = "Processed" },
                    new CspPackageEntry { PackageId = package.Id, StableKey = "processed", FileName = "source.json", MediaType = "application/json", Status = "Processed" },
                    new CspPackageEntry { PackageId = package.Id, StableKey = "excluded", FileName = "excluded.json", MediaType = "application/json", Status = "Excluded" });
            }
            db.AddRange(
                new ProviderFinding { ProviderId = fixture.Provider, OfferingId = fixture.Offering, WorkflowState = "Open" },
                new ProviderFinding { ProviderId = fixture.Provider, OfferingId = fixture.Offering, WorkflowState = "Closed" },
                new ProviderFinding { ProviderId = fixture.Provider, OfferingId = fixture.OtherOffering, WorkflowState = "Open" },
                new ProviderFinding { ProviderId = foreignProvider, OfferingId = foreignOffering, WorkflowState = "Open" });
            await db.SaveChangesAsync();
        }

        // Act
        var first = JsonSerializer.SerializeToElement(await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 1, default), JsonOptions);
        var second = JsonSerializer.SerializeToElement(await fixture.Service.OverviewAsync(fixture.Offering, 1, 2, 1, default), JsonOptions);

        // Assert
        foreach (var page in new[] { first, second })
        {
            page.GetProperty("packages").GetProperty("sourceDocumentCount").GetInt32().Should().Be(4);
            page.GetProperty("openFindingCount").GetInt32().Should().Be(1);
            page.GetProperty("capabilities").GetProperty("publishedReleaseRevisions").GetArrayLength().Should().Be(0);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Overview_ZipWithSixDocuments_CountsSix_WithoutTreatingAnalysisStateAsDocumentIdentity(bool sqlite, bool extraContainers)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        await using (var db = fixture.Db())
        {
            var package = new CspPackage
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering,
                IdempotencyKey = Guid.NewGuid().ToString()
            };
            db.Add(package);
            db.Add(new CspPackageEntry { PackageId = package.Id, StableKey = "zip", FileName = "REVIEW.ZIP",
                ArchivePath = "REVIEW.ZIP", IsOriginal = true, MediaType = "application/octet-stream", Status = "Processed" });
            var states = new[] { "Processed", "Pending", "Unreadable", "Unsupported", "Failed", "Excluded" };
            for (var index = 0; index < states.Length; index++)
                db.Add(new CspPackageEntry { PackageId = package.Id, StableKey = $"source-{index}",
                    FileName = $"source-{index}.json", ArchivePath = $"REVIEW.ZIP/sources/source-{index}.json",
                    MediaType = "application/json", Status = states[index] });
            if (extraContainers)
                db.AddRange(
                    new CspPackageEntry { PackageId = package.Id, StableKey = "directory", FileName = "",
                        ArchivePath = "REVIEW.ZIP/sources/", MediaType = "application/octet-stream", Status = "Excluded" },
                    new CspPackageEntry { PackageId = package.Id, StableKey = "nested-archive", FileName = "nested",
                        ArchivePath = "REVIEW.ZIP/nested", MediaType = "Application/ZIP; charset=binary", Status = "Unreadable" });
            await db.SaveChangesAsync();
        }

        // Act
        var result = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 1, default);

        // Assert
        result.Packages.SourceDocumentCount.Should().Be(6);
        result.Packages.Items.Single().Package.Coverage.Total.Should().Be(extraContainers ? 9 : 7,
            "analysis coverage retains the archive and directory entries separately from source-document totals");
    }
}

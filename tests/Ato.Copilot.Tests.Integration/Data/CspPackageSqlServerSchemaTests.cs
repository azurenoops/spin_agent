using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class CspPackageSqlServerSchemaTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableFact]
    public async Task CatalogSchema_PreservesReviewedRows_AndUsesDatabaseRowVersions()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await CspInheritedCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var component = new CspInheritedComponent
        {
            CspProfileId = Guid.NewGuid(), Name = "Synthetic existing published contributor",
            Status = CspInheritedComponentStatus.Published
        };
        var capability = new CspInheritedCapability
        {
            CspInheritedComponent = component, Name = "Synthetic reviewed capability",
            Status = CspInheritedCapabilityStatus.Mapped, MappedBy = MappedBy.User,
            MappedNistControlIds = ["AU-2"]
        };
        db.AddRange(component, capability);
        await db.SaveChangesAsync();
        var version = component.RowVersion;

        // Act
        await CspInheritedCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        db.ChangeTracker.Clear();

        // Assert
        var retained = await db.CspInheritedComponents.SingleAsync();
        retained.RowVersion.Should().HaveCount(8).And.Equal(version!);
        retained.Status.Should().Be(CspInheritedComponentStatus.Published);
        var retainedCapability = await db.CspInheritedCapabilities.SingleAsync();
        retainedCapability.Status.Should().Be(CspInheritedCapabilityStatus.Mapped);
        retainedCapability.MappedNistControlIds.Should().Equal("AU-2");
    }

    [SkippableFact]
    public async Task AdditiveSchema_WithRetryStrategy_PreservesPackageLedger()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var package = new CspPackage { ProviderId = Guid.NewGuid(), IdempotencyKey = "retained", Name = "Retained package" };
        db.CspPackages.Add(package);
        db.CspPackageApprovals.Add(new CspPackageApproval { PackageId = package.Id });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.CspPackages DROP COLUMN AnalysisCheckpointJson");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.CspPackageApprovals DROP COLUMN CreatedVersion");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.CspPackageApprovals DROP COLUMN PublishedVersion");
        // Act
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        db.ChangeTracker.Clear();
        (await db.CspPackages.SingleAsync()).Name.Should().Be("Retained package");
        (await db.CspPackages.SingleAsync()).AnalysisCheckpointJson.Should().BeNull();
        var approval = await db.CspPackageApprovals.SingleAsync();
        approval.CreatedVersion.Should().Be(0);
        approval.PublishedVersion.Should().BeNull();
        db.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeTrue();
    }
}

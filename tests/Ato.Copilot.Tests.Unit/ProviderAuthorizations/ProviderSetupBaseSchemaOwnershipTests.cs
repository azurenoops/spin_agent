using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ProviderSetupBaseSchemaOwnershipTests
{
    [Fact]
    public async Task BaseSchemaOwners_UpgradeTheirCurrentColumnsWithoutProviderSetupModuleOrDataReset()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await ApplyOwners(db);
        var profile = new CspProfile { DisplayName = "Retained provider", LegalEntityName = "Retained provider" };
        var offering = new ProviderOffering { ProviderId = profile.Id, Name = "Retained offering" };
        offering.OfferingId = offering.Id;
        var package = new CspPackage { ProviderId = profile.Id, Name = "Retained source", IdempotencyKey = "retained-key", ContentHash = new string('A', 64) };
        db.AddRange(profile, offering, package);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            DROP INDEX IX_CspPackages_UploadIntentId;
            ALTER TABLE CspPackages DROP COLUMN UploadIntentId;
            ALTER TABLE CspPackages DROP COLUMN HandlingPolicyVersion;
            ALTER TABLE CspPackages DROP COLUMN HandlingDeclarationJson;
            ALTER TABLE CspPackages DROP COLUMN RequiresOfferingAssociation;
            ALTER TABLE CspProfiles DROP COLUMN SetupRevision;
            ALTER TABLE CspProfiles DROP COLUMN DodComponent;
            ALTER TABLE CspProfiles DROP COLUMN TimeZoneId;
            ALTER TABLE ProviderOfferings DROP COLUMN ServiceModel;
            ALTER TABLE ProviderOfferings DROP COLUMN ManagementArrangement;
            ALTER TABLE ProviderOfferings DROP COLUMN ServiceOwner;
            ALTER TABLE ProviderOfferings DROP COLUMN SecurityContact;
            """);
        db.ChangeTracker.Clear();

        // Act
        await ApplyOwners(db);
        await ApplyOwners(db);

        // Assert
        var retained = await db.CspPackages.SingleAsync();
        retained.Id.Should().Be(package.Id);
        retained.ContentHash.Should().Be(new string('A', 64));
        retained.UploadIntentId.Should().BeNull();
        retained.HandlingPolicyVersion.Should().BeNull();
        retained.HandlingDeclarationJson.Should().BeNull();
        retained.RequiresOfferingAssociation.Should().BeFalse();
        var retainedProfile = await db.CspProfiles.SingleAsync();
        retainedProfile.Id.Should().Be(profile.Id);
        retainedProfile.SetupRevision.Should().Be(1);
        retainedProfile.DodComponent.Should().BeNull();
        retainedProfile.TimeZoneId.Should().BeNull();
        var retainedOffering = await db.Set<ProviderOffering>().SingleAsync();
        retainedOffering.Id.Should().Be(offering.Id);
        retainedOffering.ProviderId.Should().Be(profile.Id);
        retainedOffering.Name.Should().Be("Retained offering");
        retainedOffering.ServiceModel.Should().BeNull();
        retainedOffering.ManagementArrangement.Should().BeNull();
        retainedOffering.ServiceOwner.Should().BeNull();
        retainedOffering.SecurityContact.Should().BeNull();
        var offeringColumns = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM pragma_table_info('ProviderOfferings')").ToListAsync();
        offeringColumns.Should().Contain(["ServiceModel", "ManagementArrangement", "ServiceOwner", "SecurityContact"]);
        offeringColumns.Should().NotContain("ServiceDescriptionJson");
        (await db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='ProviderSetupDrafts'").SingleAsync())
            .Should().Be(0, "standalone schema owners must not depend on the new setup tables");
        var uniqueIndex = await db.Database.SqlQueryRaw<string>(
            "SELECT sql AS Value FROM sqlite_master WHERE type='index' AND name='IX_CspPackages_UploadIntentId'").SingleAsync();
        uniqueIndex.Should().Contain("UNIQUE").And.Contain("IS NOT NULL");
    }

    private static async Task ApplyOwners(AtoCopilotContext db)
    {
        await TenantsAndOrganizationsSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await ProviderAuthorizationSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
    }
}

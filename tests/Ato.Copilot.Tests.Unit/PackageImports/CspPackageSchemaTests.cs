using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed class CspPackageSchemaTests
{
    [Fact]
    public async Task CatalogSchema_PreservesRows_AndRestrictsContributorDeletion()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var db = new AtoCopilotContext(options);
        await CspInheritedCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var component = new CspInheritedComponent { Name = "Retained contributor", CspProfileId = Guid.NewGuid() };
        var capability = new CspInheritedCapability
        {
            CspInheritedComponent = component,
            Name = "Retained capability",
            MappedNistControlIds = ["AU-2"]
        };
        db.AddRange(component, capability);
        await db.SaveChangesAsync();

        // Act
        await CspInheritedCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        db.ChangeTracker.Clear();
        var deletion = () => db.CspInheritedComponents.Where(x => x.Id == component.Id).ExecuteDeleteAsync();

        // Assert
        (await db.CspInheritedCapabilities.SingleAsync()).MappedNistControlIds.Should().Equal("AU-2");
        (await db.CspInheritedComponents.SingleAsync()).Status.Should().Be(CspInheritedComponentStatus.Draft);
        await deletion.Should().ThrowAsync<SqliteException>();
        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM sqlite_master WHERE type='index' AND name LIKE 'IX_CspInherited%'").ToListAsync();
        indexes.Should().BeEquivalentTo(
            "IX_CspInheritedComponents_CspProfileId_Status", "IX_CspInheritedCapabilities_ComponentId_Status");
    }

    [Fact]
    public void CatalogSqlServerSchema_OnlyCreatesMissingStructures()
    {
        // Arrange
        var scripts = CspInheritedCatalogSchemaAdditions.Scripts(true);
        // Act
        var sql = string.Join("\n", scripts);
        // Assert
        sql.Should().Contain("IF OBJECT_ID").And.Contain("rowversion").And.Contain("ON DELETE NO ACTION");
        sql.Should().NotContain("DROP ").And.NotContain("DELETE FROM").And.NotContain("UPDATE ");
        scripts.Should().HaveCount(4);
    }

    [Fact]
    public async Task CheckpointColumn_UpgradesExistingLedgerWithoutReset()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var db = new AtoCopilotContext(options);
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var package = new CspPackage { ProviderId = Guid.NewGuid(), IdempotencyKey = "legacy", Name = "Retained original ledger" };
        db.CspPackages.Add(package);
        db.CspPackageApprovals.Add(new CspPackageApproval { PackageId = package.Id });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackages DROP COLUMN AnalysisCheckpointJson");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackageApprovals DROP COLUMN CreatedVersion");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackageApprovals DROP COLUMN PublishedVersion");
        // Act
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        db.ChangeTracker.Clear();
        var retained = await db.CspPackages.SingleAsync();
        retained.Name.Should().Be("Retained original ledger");
        retained.AnalysisCheckpointJson.Should().BeNull();
        var approval = await db.CspPackageApprovals.SingleAsync();
        approval.CreatedVersion.Should().Be(0);
        approval.PublishedVersion.Should().BeNull();
        retained.AnalysisCheckpointJson = "{\"Version\":1}";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await db.CspPackages.SingleAsync()).AnalysisCheckpointJson.Should().Be("{\"Version\":1}");
    }

    [Fact]
    public async Task RepeatedAdditiveSchema_PreservesLedger_AndEnforcesProviderKeyUniqueness()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var db = new AtoCopilotContext(options);
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var package = new CspPackage { ProviderId = Guid.NewGuid(), IdempotencyKey = "stable", Name = "Retained" };
        db.CspPackages.Add(package);
        await db.SaveChangesAsync();
        // Act
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        (await db.CspPackages.SingleAsync()).Name.Should().Be("Retained");
        db.CspPackages.Add(new CspPackage { ProviderId = package.ProviderId, IdempotencyKey = "stable" });
        var duplicate = () => db.SaveChangesAsync();
        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public void SqlServerSchema_OnlyUsesAdditiveStatements()
    {
        // Arrange
        var scripts = CspPackageSchemaAdditions.Scripts(true);
        // Act
        var sql = string.Join("\n", scripts);
        // Assert
        sql.Should().Contain("IF OBJECT_ID").And.Contain("CREATE UNIQUE INDEX");
        sql.Should().NotContain("DROP ").And.NotContain("DELETE ").And.NotContain("UPDATE ");
    }
}

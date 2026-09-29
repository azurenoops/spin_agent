using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class PackageReadinessSchemaTests
{
    [Fact]
    public async Task Sqlite_AdditiveSchemaIsIdempotent_PreservesLegacyPackageAndImmutableRun()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE RegisteredSystems (Id TEXT PRIMARY KEY);
            CREATE TABLE AuthorizationPackages (Id TEXT PRIMARY KEY, Purpose INTEGER NOT NULL DEFAULT 0);
            INSERT INTO RegisteredSystems VALUES ('system');
            INSERT INTO AuthorizationPackages VALUES ('historic',0);
            """);

        // Act
        await PackageReadinessSchemaAdditions.ApplyAsync(db);
        await PackageReadinessSchemaAdditions.ApplyAsync(db);
        var run = new PackageReadinessRun
        {
            TenantId = Guid.NewGuid(), RegisteredSystemId = "system", SelectionHash = new string('a', 64),
            StartedAt = DateTime.UtcNow, EvaluatedAt = DateTime.UtcNow, RuleVersion = "test", EvaluatedBy = "synthetic"
        };
        db.PackageReadinessRuns.Add(run);
        await db.SaveChangesAsync();
        run.Outcome = "Ready";
        var change = () => db.SaveChangesAsync();

        // Assert
        await change.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable*");
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM AuthorizationPackages WHERE Id='historic' AND Purpose=0 AND ReadinessRunId IS NULL").SingleAsync()).Should().Be(1);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_index_info('IX_PackageReadinessRuns_Scope')").SingleAsync()).Should().Be(5);
    }
}

public sealed class PackageReadinessSqlServerSchemaTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableFact]
    public async Task SqlServer_AdditiveSchema_RepeatedStartupPreservesExistingPackageAndForeignKey()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.RegisteredSystems (Id nvarchar(36) NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.AuthorizationPackages (Id nvarchar(36) NOT NULL PRIMARY KEY, Purpose int NOT NULL DEFAULT 0);
            INSERT INTO dbo.RegisteredSystems VALUES ('system');
            INSERT INTO dbo.AuthorizationPackages VALUES ('historic',0);
            """);

        // Act
        await PackageReadinessSchemaAdditions.ApplyAsync(db);
        await PackageReadinessSchemaAdditions.ApplyAsync(db);

        // Assert
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM dbo.AuthorizationPackages WHERE Id='historic' AND Purpose=0 AND ReadinessRunId IS NULL").SingleAsync()).Should().Be(1);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE name='FK_PackageReadinessRuns_RegisteredSystems'").SingleAsync()).Should().Be(1);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.indexes WHERE name='IX_PackageReadinessRuns_Scope'").SingleAsync()).Should().Be(1);
    }
}

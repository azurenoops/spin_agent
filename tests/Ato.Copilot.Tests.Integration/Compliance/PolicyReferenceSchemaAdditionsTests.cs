using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Compliance;

public sealed class PolicyReferenceSchemaAdditionsTests
{
    [Fact]
    public async Task AdditiveSchema_IsIdempotent_PreservesLegacyRows_AndEnforcesRetainedUniqueness()
    {
        // Arrange
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE ComponentSystemAssignments (Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL,
                SystemComponentId TEXT NOT NULL, RegisteredSystemId TEXT NOT NULL);
            INSERT INTO ComponentSystemAssignments VALUES ('legacy', 'tenant', 'source', 'system');
            """);

        // Act
        await PolicyReferenceSchemaAdditions.ApplyAsync(db);
        await PolicyReferenceSchemaAdditions.ApplyAsync(db);

        // Assert
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('ComponentSystemAssignments') WHERE name LIKE 'Policy%'").SingleAsync())
            .Should().Be(7);
        (await db.Database.SqlQueryRaw<int>("SELECT PolicyRevision AS Value FROM ComponentSystemAssignments WHERE Id = 'legacy'").SingleAsync()).Should().Be(0);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ComponentSystemAssignments WHERE Id = 'legacy' AND PolicySourceSnapshotJson IS NULL AND PolicySourceCapturedAt IS NULL").SingleAsync())
            .Should().Be(1);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO ComponentSystemAssignments (Id,TenantId,SystemComponentId,RegisteredSystemId,PolicyReferenceKey)
            VALUES ('retained', 'tenant', 'source2', 'system', 'system:source2');
            """);
        var duplicate = () => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO ComponentSystemAssignments (Id,TenantId,SystemComponentId,RegisteredSystemId,PolicyReferenceKey)
            VALUES ('duplicate', 'tenant', 'source2', 'system', 'system:source2');
            """);
        await duplicate.Should().ThrowAsync<SqliteException>();
        db.Model.FindEntityType(typeof(ComponentSystemAssignment))!.FindProperty(nameof(ComponentSystemAssignment.PolicyRevision))!
            .IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void SqlServerModel_ContainsNullableRetentionAndFilteredUniqueIndex()
    {
        // Arrange
        using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer("Server=localhost;Database=policy_schema_shape;Integrated Security=true;TrustServerCertificate=true").Options);

        // Act
        var script = db.Database.GenerateCreateScript();

        // Assert
        script.Should().Contain("[PolicyRationale] nvarchar(500) NULL")
            .And.Contain("[PolicySourceSnapshotJson] nvarchar(max) NULL")
            .And.Contain("[PolicyRevision] int NOT NULL")
            .And.Contain("CREATE UNIQUE INDEX [IX_ComponentSystemAssignment_PolicyReference]")
            .And.Contain("WHERE [PolicyReferenceKey] IS NOT NULL");
    }
}

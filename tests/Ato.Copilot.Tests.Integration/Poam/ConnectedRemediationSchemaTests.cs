using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Poam;

public class ConnectedRemediationSchemaTests
{
    [Fact]
    public async Task Sqlite_UpgradesLegacySchemaIdempotently_WithoutInferringOwnership()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE RemediationTasks (Id TEXT PRIMARY KEY, Title TEXT NOT NULL);
            CREATE TABLE PoamItems (Id TEXT PRIMARY KEY);
            INSERT INTO RemediationTasks VALUES ('task','Keep this task');
            INSERT INTO PoamItems VALUES ('poam');
            """);
        // Act
        await ConnectedRemediationSchemaAdditions.ApplyAsync(db);
        await ConnectedRemediationSchemaAdditions.ApplyAsync(db);
        // Assert
        (await db.Database.SqlQueryRaw<string>("SELECT Title AS Value FROM RemediationTasks").SingleAsync()).Should().Be("Keep this task");
        (await db.Database.SqlQueryRaw<string>("SELECT VerificationStatus AS Value FROM RemediationTasks").SingleAsync()).Should().Be("NotVerified");
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM RemediationTasks WHERE RegisteredSystemId IS NOT NULL").SingleAsync()).Should().Be(0);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO PoamTaskLinks VALUES ('tenant','system','poam','task','actor','2026-01-01');
            """);
        var duplicate = () => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO PoamTaskLinks VALUES ('tenant','system','poam','task','actor','2026-01-01');
            """);
        await duplicate.Should().ThrowAsync<SqliteException>();
    }
}

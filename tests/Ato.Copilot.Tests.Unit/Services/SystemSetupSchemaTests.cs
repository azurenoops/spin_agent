using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class SystemSetupSchemaTests
{
    [Fact]
    public async Task LegacySchema_RetrofitsTenantColumnBeforeCreatingScopedRequestIndex()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE RegisteredSystems (Id TEXT NOT NULL PRIMARY KEY)");
        // Act
        await SystemSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RegisteredSystems') WHERE name = 'TenantId'";
        Convert.ToInt64(await command.ExecuteScalarAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AdditiveSchema_ReplaysWithoutChangingOpaqueLegacyIds()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE RegisteredSystems (Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL);
            INSERT INTO RegisteredSystems (Id,TenantId) VALUES ('legacy-opaque-system','tenant-a');
            """);
        // Act
        await SystemSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await SystemSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, SetupRevision, SetupDraftJson FROM RegisteredSystems";
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("legacy-opaque-system");
        reader.GetInt64(1).Should().Be(0);
        reader.IsDBNull(2).Should().BeTrue();
        (await reader.ReadAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RequestKeyUniqueness_IsTenantAndActorScoped()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE RegisteredSystems (Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL)");
        await SystemSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO RegisteredSystems (Id,TenantId,SetupActorPersonId,SetupRequestKey) VALUES ('a','tenant-a','actor','same');
            INSERT INTO RegisteredSystems (Id,TenantId,SetupActorPersonId,SetupRequestKey) VALUES ('b','tenant-b','actor','same');
            """);
        // Act
        var duplicate = () => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO RegisteredSystems (Id,TenantId,SetupActorPersonId,SetupRequestKey) VALUES ('c','tenant-a','actor','same');
            """);
        // Assert
        await duplicate.Should().ThrowAsync<SqliteException>();
    }
}

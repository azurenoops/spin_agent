using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Data;

public class OrganizationMembershipSchemaTests
{
    [Fact]
    public async Task AdditiveSqliteSchema_IsIdempotent_AndCreatesNoImplicitGrants()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Tenants (Id TEXT PRIMARY KEY); CREATE TABLE Persons (Id TEXT PRIMARY KEY);");

        // Act
        await OrganizationMembershipSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await OrganizationMembershipSchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        (await db.OrganizationMemberships.CountAsync()).Should().Be(0);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name LIKE 'IX_OrganizationMemberships%'";
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(3);
    }

    [Fact]
    public void SqlServerSchema_UsesCompositeIdentityAndActivePersonUniqueness_WithoutBackfill()
    {
        // Arrange
        var script = OrganizationMembershipSchemaAdditions.SqlServerScript;

        // Act / Assert
        script.Should().Contain("TenantId, DirectoryTenantId, ObjectId")
            .And.Contain("ON dbo.OrganizationMemberships(PersonId) WHERE RevokedAt IS NULL")
            .And.Contain("REFERENCES dbo.Persons(Id)")
            .And.NotContain("INSERT INTO");
    }

    [Fact]
    public async Task UnsupportedProvider_FailsExplicitly()
    {
        // Arrange
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        // Act
        var operation = () => OrganizationMembershipSchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        await operation.Should().ThrowAsync<NotSupportedException>();
    }
}

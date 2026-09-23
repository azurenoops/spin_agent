using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

public class NotificationPreferencesWorkspaceSchemaTests
{
    [Fact]
    public async Task Sqlite_UpgradeReplacesGlobalIndex_PreservesValues_AndIsRepeatable()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""
            DROP INDEX IF EXISTS IX_NotificationPreferences_TenantId_UserId;
            CREATE UNIQUE INDEX IF NOT EXISTS IX_NotificationPreferences_UserId ON NotificationPreferences(UserId);
            """);
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        db.Tenants.AddRange(new Tenant { Id = tenant, DisplayName = "One" }, new Tenant { Id = otherTenant, DisplayName = "Two" });
        db.NotificationPreferences.Add(new NotificationPreferences
        {
            Id = Guid.NewGuid(), TenantId = tenant, UserId = "actor", AlertDaysBefore = 7, PoamOverdueAlerts = false
        });
        await db.SaveChangesAsync();

        // Act
        await NotificationPreferencesSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await NotificationPreferencesSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        db.ChangeTracker.Clear();
        db.NotificationPreferences.Add(new NotificationPreferences
        {
            Id = Guid.NewGuid(), TenantId = otherTenant, UserId = "actor", AlertDaysBefore = 21
        });
        await db.SaveChangesAsync();

        // Assert
        var original = await db.NotificationPreferences.SingleAsync(p => p.TenantId == tenant);
        original.AlertDaysBefore.Should().Be(7);
        original.PoamOverdueAlerts.Should().BeFalse();
        (await db.NotificationPreferences.CountAsync()).Should().Be(2);
        db.NotificationPreferences.Add(new NotificationPreferences { Id = Guid.NewGuid(), TenantId = tenant, UserId = "actor" });
        var duplicate = () => db.SaveChangesAsync();
        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Sqlite_InvalidExistingSchema_FailsInsteadOfRemovingGlobalConstraint()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE NotificationPreferences (Id TEXT PRIMARY KEY, UserId TEXT NOT NULL);
            CREATE UNIQUE INDEX IX_NotificationPreferences_UserId ON NotificationPreferences(UserId);
            """);

        // Act
        var upgrade = () => NotificationPreferencesSchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        await upgrade.Should().ThrowAsync<SqliteException>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM pragma_index_list('NotificationPreferences') WHERE name='IX_NotificationPreferences_UserId'";
        (await command.ExecuteScalarAsync()).Should().Be(1L);
    }

    [Fact]
    public async Task Sqlite_SameActorMaySavePreferencesInTwoOrganizations_ButNotDuplicateWithinOne()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var actor = Guid.NewGuid().ToString();
        db.Tenants.AddRange(new Tenant { Id = a, DisplayName = "One" }, new Tenant { Id = b, DisplayName = "Two" });
        db.NotificationPreferences.AddRange(
            new NotificationPreferences { Id = Guid.NewGuid(), TenantId = a, UserId = actor, AlertDaysBefore = 7 },
            new NotificationPreferences { Id = Guid.NewGuid(), TenantId = b, UserId = actor, AlertDaysBefore = 30 });

        // Act
        var save = () => db.SaveChangesAsync();

        // Assert
        await save.Should().NotThrowAsync("preferences belong to a selected organization, not a global oid");
        (await db.NotificationPreferences.CountAsync()).Should().Be(2);
        db.NotificationPreferences.Add(new NotificationPreferences { Id = Guid.NewGuid(), TenantId = a, UserId = actor });
        var duplicate = () => db.SaveChangesAsync();
        await duplicate.Should().ThrowAsync<DbUpdateException>("the tenant/actor preference identity must be unique");
    }
}

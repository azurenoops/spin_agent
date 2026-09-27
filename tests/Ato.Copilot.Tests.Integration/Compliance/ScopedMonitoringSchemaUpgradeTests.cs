using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Compliance;

public sealed class ScopedMonitoringSchemaUpgradeTests
{
    [Fact]
    public async Task Fresh_schema_supports_repeated_monitoring_upgrade()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        // Act
        await TenantIdColumnAdditions.ApplyAsync(db, NullLogger.Instance);
        await ScopedMonitoringSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await ScopedMonitoringSchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        var index = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM pragma_index_info('IX_MonitoringConfig_Sub_RG') ORDER BY seqno").ToListAsync();
        index.Should().Equal("TenantId", "SubscriptionId", "ResourceGroupName");
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name IN ('MonitoringRuleEvaluations', 'MonitoringImpactReviews', 'ScopedMonitoringObservations')").ToListAsync();
        tables.Should().HaveCount(3);
    }

    [Fact]
    public async Task Legacy_pretenant_schema_upgrades_in_startup_order_without_losing_records()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE AlertRules (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL);
            CREATE TABLE MonitoringConfigurations (
                Id TEXT NOT NULL PRIMARY KEY, SubscriptionId TEXT NOT NULL, ResourceGroupName TEXT NULL);
            CREATE UNIQUE INDEX IX_MonitoringConfig_Sub_RG
                ON MonitoringConfigurations (SubscriptionId, ResourceGroupName);
            INSERT INTO AlertRules (Id, Name) VALUES ('retained-rule', 'Retained legacy rule');
            INSERT INTO MonitoringConfigurations (Id, SubscriptionId, ResourceGroupName)
                VALUES ('retained-configuration', 'shared-subscription', 'resource-group');
            """);

        // Act
        // This is the production Program registration order: ownership retrofit before tenant-keyed indexes.
        await TenantIdColumnAdditions.ApplyAsync(db, NullLogger.Instance);
        await ScopedMonitoringSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await ScopedMonitoringSchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        (await db.Database.SqlQueryRaw<string>("SELECT Name AS Value FROM AlertRules WHERE Id = 'retained-rule'")
            .SingleAsync()).Should().Be("Retained legacy rule");
        (await db.Database.SqlQueryRaw<string>("SELECT SubscriptionId AS Value FROM MonitoringConfigurations WHERE Id = 'retained-configuration'")
            .SingleAsync()).Should().Be("shared-subscription");
        var index = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM pragma_index_info('IX_MonitoringConfig_Sub_RG') ORDER BY seqno").ToListAsync();
        index.Should().Equal("TenantId", "SubscriptionId", "ResourceGroupName");
        (await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM MonitoringConfigurations WHERE Id = 'retained-configuration' AND TenantId IS NULL")
            .SingleAsync()).Should().Be(1, "the upgrade must not invent tenant ownership for legacy records");
    }
}

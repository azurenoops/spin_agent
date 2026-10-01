using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class InheritanceAuditSqlServerTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableFact]
    public async Task FreshSchema_RoundTripsEveryDefinedChangeSource()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.EnsureCreatedAsync();
        foreach (var source in Enum.GetValues<InheritanceChangeSource>())
            db.InheritanceAuditEntries.Add(Entry(source));

        // Act
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var sources = await db.InheritanceAuditEntries.Select(entry => entry.ChangeSource).ToListAsync();

        // Assert
        sources.Should().BeEquivalentTo(Enum.GetValues<InheritanceChangeSource>());
    }

    [SkippableFact]
    public async Task LegacyUpgrade_IsRepeatableAndPreservesExistingAuditRows()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE dbo.InheritanceAuditEntries ALTER COLUMN ChangeSource NVARCHAR(20) NOT NULL;");
        var existing = Entry(InheritanceChangeSource.Manual);
        db.InheritanceAuditEntries.Add(existing);
        await db.SaveChangesAsync();
        var before = existing.Timestamp;

        // Act
        await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        db.InheritanceAuditEntries.Add(Entry(InheritanceChangeSource.SubscriptionReconcile));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Assert
        var retained = await db.InheritanceAuditEntries.SingleAsync(entry => entry.Id == existing.Id);
        retained.ChangeSource.Should().Be(InheritanceChangeSource.Manual);
        retained.Timestamp.Should().Be(before);
        (await db.InheritanceAuditEntries.CountAsync()).Should().Be(2);
        (await db.InheritanceAuditEntries.SingleAsync(entry => entry.ChangeSource == InheritanceChangeSource.SubscriptionReconcile))
            .NewProvider.Should().Be("Synthetic provider");
    }

    [SkippableTheory]
    [InlineData(true, 64)]
    [InlineData(false, 128)]
    public async Task Upgrade_PreservesNullabilityAndDoesNotShrinkWiderColumns(bool nullable, int expectedBytes)
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.EnsureCreatedAsync();
        var legacyShape = nullable
            ? "ALTER TABLE dbo.InheritanceAuditEntries ALTER COLUMN ChangeSource NVARCHAR(20) NULL;"
            : "ALTER TABLE dbo.InheritanceAuditEntries ALTER COLUMN ChangeSource NVARCHAR(64) NOT NULL;";
        await db.Database.ExecuteSqlRawAsync(legacyShape);

        // Act
        await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        await using var command = db.Database.GetDbConnection().CreateCommand();
        await db.Database.OpenConnectionAsync();
        command.CommandText = """
            SELECT max_length,is_nullable FROM sys.columns
            WHERE object_id=OBJECT_ID(N'dbo.InheritanceAuditEntries') AND name=N'ChangeSource';
            """;
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt16(0).Should().Be((short)expectedBytes);
        reader.GetBoolean(1).Should().Be(nullable);
    }

    private static InheritanceAuditEntry Entry(InheritanceChangeSource source) => new()
    {
        ControlInheritanceId = Guid.NewGuid().ToString(), ControlBaselineId = Guid.NewGuid().ToString(),
        ControlId = "AU-11", Actor = "synthetic-test-actor", NewInheritanceType = "Inherited",
        NewProvider = "Synthetic provider", ChangeSource = source,
    };
}

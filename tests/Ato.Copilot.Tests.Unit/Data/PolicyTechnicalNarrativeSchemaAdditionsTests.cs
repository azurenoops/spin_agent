using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Data;

public class PolicyTechnicalNarrativeSchemaAdditionsTests
{
    [Fact]
    public void SqlServerScript_UsesMaxForEightThousandCharacterNarratives()
    {
        var script = GetSqlServerScript();

        script
            .Should().Contain("PolicyNarrative NVARCHAR(MAX)")
            .And.Contain("TechnicalNarrative NVARCHAR(MAX)")
            .And.NotContain("NVARCHAR(8000)");
        script.Should().Contain("SnapshotJson NVARCHAR(MAX) NULL")
            .And.Contain("DerivationBasis NVARCHAR(32) NOT NULL")
            .And.Contain("DEFAULT 'Unknown'");
    }

    [Fact]
    public void SqlServerScript_DefersBackfillsUntilAddedColumnsExist()
    {
        var script = GetSqlServerScript();

        script
            .Should().Contain("EXEC(N'UPDATE ControlImplementations")
            .And.Contain("EXEC(N'UPDATE EvidenceArtifacts");
    }

    [Fact]
    public async Task ApplyAsync_OnLegacySqliteSchema_BackfillsOnceAndIsIdempotent()
    {
        // Arrange
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AtoCopilotContext(options);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "ControlImplementations" (
                "Id" TEXT NOT NULL PRIMARY KEY,
                "Narrative" TEXT NULL
            );
            INSERT INTO "ControlImplementations" ("Id", "Narrative")
            VALUES ('control-1', 'Legacy implementation narrative');

            CREATE TABLE "EvidenceArtifacts" (
                "Id" TEXT NOT NULL PRIMARY KEY
            );
            INSERT INTO "EvidenceArtifacts" ("Id") VALUES ('evidence-1');
            CREATE TABLE "OscalDecompositionFragments" (
                "Id" TEXT NOT NULL PRIMARY KEY, "ConfidenceScore" REAL NULL
            );
            INSERT INTO "OscalDecompositionFragments" VALUES ('fragment-1', 0.9);
            CREATE TABLE "NarrativeVersions" ("Id" TEXT NOT NULL PRIMARY KEY);
            INSERT INTO "NarrativeVersions" VALUES ('version-1');
            """);

        // Act
        await PolicyTechnicalNarrativeSchemaAdditions.ApplyAsync(context, NullLogger.Instance);
        await using (var provenanceCommand = connection.CreateCommand())
        {
            provenanceCommand.CommandText = "SELECT DerivationBasis FROM OscalDecompositionFragments";
            (await provenanceCommand.ExecuteScalarAsync()).Should().Be("Unknown");
            provenanceCommand.CommandText = "SELECT SnapshotJson FROM NarrativeVersions";
            (await provenanceCommand.ExecuteScalarAsync()).Should().Be(DBNull.Value);
        }
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "EvidenceArtifacts" ("Id") VALUES ('evidence-2');
            UPDATE "OscalDecompositionFragments" SET "DerivationBasis" = 'Fallback';
            UPDATE "NarrativeVersions" SET "SnapshotJson" = 'synthetic snapshot';
            """);
        await PolicyTechnicalNarrativeSchemaAdditions.ApplyAsync(context, NullLogger.Instance);

        // Assert
        await using (var provenanceCommand = connection.CreateCommand())
        {
            provenanceCommand.CommandText = "SELECT DerivationBasis FROM OscalDecompositionFragments";
            (await provenanceCommand.ExecuteScalarAsync()).Should().Be("Fallback");
            provenanceCommand.CommandText = "SELECT SnapshotJson FROM NarrativeVersions";
            (await provenanceCommand.ExecuteScalarAsync()).Should().Be("synthetic snapshot");
        }
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ci."TechnicalNarrative", ci."MigratedFromLegacy",
                   legacy."NarrativeType", added."NarrativeType"
            FROM "ControlImplementations" ci
            CROSS JOIN "EvidenceArtifacts" legacy
            CROSS JOIN "EvidenceArtifacts" added
            WHERE ci."Id" = 'control-1'
              AND legacy."Id" = 'evidence-1'
              AND added."Id" = 'evidence-2';
            """;
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("Legacy implementation narrative");
        reader.GetInt64(1).Should().Be(1);
        reader.GetString(2).Should().Be("Combined");
        reader.GetString(3).Should().Be("Unclassified");
    }

    private static string GetSqlServerScript() =>
        (string)typeof(PolicyTechnicalNarrativeSchemaAdditions)
            .GetField("SqlServerScript", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;
}
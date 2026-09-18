using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Adds and backfills the Feature 074 dual-narrative schema.</summary>
public static class PolicyTechnicalNarrativeSchemaAdditions
{
    /// <summary>Applies the additive schema changes for supported database providers.</summary>
    public static async Task ApplyAsync(
        AtoCopilotContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        var providerName = db.Database.ProviderName ?? string.Empty;

        try
        {
            if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                await db.Database.ExecuteSqlRawAsync(SqlServerScript, cancellationToken);
            }
            else if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                await ApplySqliteAsync(db, cancellationToken);
            }
            else
            {
                logger.LogWarning(
                    "PolicyTechnicalNarrativeSchemaAdditions: skipping unsupported provider {Provider}",
                    providerName);
                return;
            }

            logger.LogInformation(
                "Verified Feature 074 policy and technical narrative schema on {Provider}",
                providerName);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "PolicyTechnicalNarrativeSchemaAdditions: DDL FAILED for provider {Provider}. Startup aborted.",
                providerName);
            throw new InvalidOperationException(
                $"Database schema initialization failed in PolicyTechnicalNarrativeSchemaAdditions for provider '{providerName}'.",
                exception);
        }
    }

    private static async Task ApplySqliteAsync(
        AtoCopilotContext db,
        CancellationToken cancellationToken)
    {
        var controlColumns = await GetSqliteColumnsAsync(db, "ControlImplementations", cancellationToken);
        if (!controlColumns.Contains("PolicyNarrative"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"ControlImplementations\" ADD COLUMN \"PolicyNarrative\" TEXT NULL",
                cancellationToken);
        }
        if (!controlColumns.Contains("TechnicalNarrative"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"ControlImplementations\" ADD COLUMN \"TechnicalNarrative\" TEXT NULL",
                cancellationToken);
        }
        if (!controlColumns.Contains("MigratedFromLegacy"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"ControlImplementations\" ADD COLUMN \"MigratedFromLegacy\" INTEGER NOT NULL DEFAULT 0",
                cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "ControlImplementations"
            SET "TechnicalNarrative" = "Narrative", "MigratedFromLegacy" = 1
            WHERE "Narrative" IS NOT NULL AND "TechnicalNarrative" IS NULL;
            """, cancellationToken);

        var evidenceColumns = await GetSqliteColumnsAsync(db, "EvidenceArtifacts", cancellationToken);
        if (!evidenceColumns.Contains("NarrativeType"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"EvidenceArtifacts\" ADD COLUMN \"NarrativeType\" TEXT NOT NULL DEFAULT 'Unclassified'",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"EvidenceArtifacts\" SET \"NarrativeType\" = 'Combined'",
                cancellationToken);
        }
        if (!evidenceColumns.Contains("AutoTagRationale"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"EvidenceArtifacts\" ADD COLUMN \"AutoTagRationale\" TEXT NULL",
                cancellationToken);
        }
        if (!evidenceColumns.Contains("ManuallyTaggedBy"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"EvidenceArtifacts\" ADD COLUMN \"ManuallyTaggedBy\" TEXT NULL",
                cancellationToken);
        }
    }

    private static async Task<HashSet<string>> GetSqliteColumnsAsync(
        AtoCopilotContext db,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}')";
        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync(cancellationToken);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private const string SqlServerScript = """
        IF COL_LENGTH('ControlImplementations', 'PolicyNarrative') IS NULL
            ALTER TABLE ControlImplementations ADD PolicyNarrative NVARCHAR(MAX) NULL;

        IF COL_LENGTH('ControlImplementations', 'TechnicalNarrative') IS NULL
            ALTER TABLE ControlImplementations ADD TechnicalNarrative NVARCHAR(MAX) NULL;

        IF COL_LENGTH('ControlImplementations', 'MigratedFromLegacy') IS NULL
            ALTER TABLE ControlImplementations ADD MigratedFromLegacy BIT NOT NULL
                CONSTRAINT DF_ControlImplementations_MigratedFromLegacy DEFAULT 0;

        UPDATE ControlImplementations
        SET TechnicalNarrative = Narrative, MigratedFromLegacy = 1
        WHERE Narrative IS NOT NULL AND TechnicalNarrative IS NULL;

        IF COL_LENGTH('EvidenceArtifacts', 'NarrativeType') IS NULL
        BEGIN
            ALTER TABLE EvidenceArtifacts ADD NarrativeType NVARCHAR(20) NOT NULL
                CONSTRAINT DF_EvidenceArtifacts_NarrativeType DEFAULT 'Unclassified';
            UPDATE EvidenceArtifacts SET NarrativeType = 'Combined';
        END;

        IF COL_LENGTH('EvidenceArtifacts', 'AutoTagRationale') IS NULL
            ALTER TABLE EvidenceArtifacts ADD AutoTagRationale NVARCHAR(500) NULL;

        IF COL_LENGTH('EvidenceArtifacts', 'ManuallyTaggedBy') IS NULL
            ALTER TABLE EvidenceArtifacts ADD ManuallyTaggedBy NVARCHAR(200) NULL;
        """;
}
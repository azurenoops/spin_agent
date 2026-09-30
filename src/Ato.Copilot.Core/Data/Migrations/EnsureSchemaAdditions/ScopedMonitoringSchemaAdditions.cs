using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class ScopedMonitoringSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlServer() && !db.Database.IsSqlite())
            throw new NotSupportedException("Monitoring persistence requires SQLite or SQL Server.");
        var sql = db.Database.IsSqlServer();
        var text = sql ? "nvarchar(max)" : "TEXT";
        var date = sql ? "datetimeoffset" : "TEXT";
        var guid = sql ? "uniqueidentifier" : "TEXT";
        var number = sql ? "bigint" : "INTEGER";
        string Short(int length) => sql ? $"nvarchar({length})" : "TEXT";
        var columns = new Dictionary<string, Dictionary<string, string>>
        {
            ["AlertRules"] = new()
            {
                ["RegisteredSystemId"] = $"{Short(36)} NULL", ["BoundaryDefinitionId"] = $"{Short(36)} NULL",
                ["ReviewedScopeJson"] = $"{text} NULL", ["BaselineReference"] = $"{text} NULL",
                ["OwnerId"] = $"{Short(200)} NULL", ["LastModifiedBy"] = $"{Short(200)} NULL",
                ["Signal"] = $"{Short(32)} NOT NULL DEFAULT 'Alert'",
                ["Response"] = $"{Short(32)} NOT NULL DEFAULT 'CreateImpactReview'",
                ["CadenceMinutes"] = "int NOT NULL DEFAULT 60", ["Version"] = $"{number} NOT NULL DEFAULT 1",
                ["LastEvaluatedAt"] = $"{date} NULL", ["NextEvaluationUtcTicks"] = $"{number} NOT NULL DEFAULT 0"
            },
            ["MonitoringConfigurations"] = new()
            {
                ["LastAttemptAt"] = $"{date} NULL", ["LastFailureAt"] = $"{date} NULL", ["CollectionError"] = $"{text} NULL"
            }
        };
        foreach (var table in columns)
        {
            var existing = sql ? new List<string>() : await db.Database.SqlQueryRaw<string>(
                $"SELECT name AS Value FROM pragma_table_info('{table.Key}')").ToListAsync(ct);
            foreach (var column in table.Value)
            {
                if (!sql && existing.Contains(column.Key)) continue;
                var prefix = sql ? $"IF COL_LENGTH('{table.Key}', '{column.Key}') IS NULL " : "";
                await db.Database.ExecuteSqlRawAsync(
                    $"{prefix}ALTER TABLE [{table.Key}] ADD [{column.Key}] {column.Value};", ct);
            }
        }
        var tables = new Dictionary<string, string>
        {
            ["ScopedMonitoringObservations"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, TenantId {guid} NOT NULL, RegisteredSystemId {Short(36)} NOT NULL,
                SourceId {Short(64)} NOT NULL, Fingerprint {Short(64)} NOT NULL, SnapshotJson {text} NOT NULL,
                AttributedAt {date} NOT NULL,
                CONSTRAINT UQ_ScopedMonitoringObservation UNIQUE (TenantId, RegisteredSystemId, SourceId, Fingerprint)
                """,
            ["MonitoringRuleEvaluations"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, TenantId {guid} NOT NULL, RuleId {guid} NOT NULL,
                RuleVersion {number} NOT NULL, RegisteredSystemId {Short(36)} NOT NULL,
                InputFingerprint {Short(64)} NOT NULL, Outcome {Short(32)} NOT NULL,
                RuleSnapshotJson {text} NOT NULL, InputSnapshotJson {text} NOT NULL,
                EvaluatedAt {date} NOT NULL,
                CONSTRAINT UQ_MonitoringEvaluation UNIQUE (TenantId, RuleId, RuleVersion, InputFingerprint)
                """,
            ["MonitoringImpactReviews"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, TenantId {guid} NOT NULL, EvaluationId {guid} NOT NULL,
                RegisteredSystemId {Short(36)} NOT NULL, ControlId {Short(20)} NULL, OwnerId {Short(200)} NOT NULL,
                Disposition {Short(32)} NOT NULL, Rationale {text} NULL, ReviewedBy {text} NULL,
                ReviewedAt {date} NULL, Version {number} NOT NULL, AffectedRecordsJson {text} NOT NULL,
                NarrativeProposalIdsJson {text} NOT NULL, CreatedAt {date} NOT NULL,
                CONSTRAINT UQ_MonitoringImpact UNIQUE (TenantId, EvaluationId)
                """
        };
        foreach (var table in tables)
        {
            var prefix = sql ? $"IF OBJECT_ID('{table.Key}', 'U') IS NULL CREATE TABLE" : "CREATE TABLE IF NOT EXISTS";
            await db.Database.ExecuteSqlRawAsync($"{prefix} [{table.Key}] ({table.Value});", ct);
        }
        if (sql)
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MonitoringConfig_Sub_RG'
                    AND object_id = OBJECT_ID('MonitoringConfigurations'))
                AND NOT EXISTS (
                    SELECT 1 FROM sys.index_columns ic
                    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                    JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                    WHERE i.name = 'IX_MonitoringConfig_Sub_RG' AND c.name = 'TenantId')
                    DROP INDEX IX_MonitoringConfig_Sub_RG ON MonitoringConfigurations;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MonitoringConfig_Sub_RG'
                    AND object_id = OBJECT_ID('MonitoringConfigurations'))
                    CREATE UNIQUE INDEX IX_MonitoringConfig_Sub_RG ON MonitoringConfigurations (TenantId, SubscriptionId, ResourceGroupName);
                """, ct);
        }
        else
        {
            var indexColumns = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM pragma_index_info('IX_MonitoringConfig_Sub_RG')").ToListAsync(ct);
            if (!indexColumns.Contains("TenantId"))
            {
                await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_MonitoringConfig_Sub_RG;", ct);
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE UNIQUE INDEX IX_MonitoringConfig_Sub_RG ON MonitoringConfigurations (TenantId, SubscriptionId, ResourceGroupName);", ct);
            }
        }
        logger.LogInformation("Verified scoped monitoring schema.");
    }
}

using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class ProviderMonitoringSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlServer() && !db.Database.IsSqlite()) throw new NotSupportedException("Provider monitoring requires SQLite or SQL Server.");
        foreach (var script in Scripts(db.Database.IsSqlServer()))
            await db.Database.ExecuteSqlRawAsync(script, ct);
        logger.LogInformation("Verified provider-owned monitoring rule and evaluation schema.");
    }

    public static IReadOnlyList<string> Scripts(bool sql)
    {
        var scripts = new List<string>();
        var guid = sql ? "uniqueidentifier" : "TEXT";
        var text = sql ? "nvarchar(max)" : "TEXT";
        var number = sql ? "bigint" : "INTEGER";
        var date = sql ? "datetimeoffset" : "TEXT";
        var bit = sql ? "bit" : "INTEGER";
        var integer = sql ? "int" : "INTEGER";
        var prefix = sql ? "dbo." : "";
        string Short(int length) => sql ? $"nvarchar({length})" : "TEXT";
        var tables = new Dictionary<string, string>
        {
            ["ProviderMonitoringRules"] = $"""
                Name {Short(200)} NOT NULL, Signal {Short(40)} NOT NULL, SourceId {guid} NOT NULL,
                ConditionJson {Short(4000)} NOT NULL, OwnerId {Short(254)} NOT NULL, Response {Short(40)} NOT NULL,
                CadenceMinutes {integer} NOT NULL, IsEnabled {bit} NOT NULL, BaselineJson {text} NOT NULL,
                BaselineHash {Short(64)} NOT NULL, NextEvaluationUtcTicks {number} NOT NULL,
                LastEvaluatedAt {date} NULL, UpdatedBy {Short(254)} NOT NULL
                """,
            ["ProviderMonitoringEvaluations"] = $"""
                RuleId {guid} NOT NULL, RuleRevision {number} NOT NULL, ObservationHash {Short(64)} NOT NULL,
                Outcome {Short(32)} NOT NULL, CollectionHealth {Short(32)} NOT NULL,
                RuleSnapshotJson {text} NOT NULL, SourceSnapshotJson {text} NOT NULL, ImpactReviewId {guid} NULL,
                FOREIGN KEY (ProviderId, OfferingId, RuleId) REFERENCES {prefix}[ProviderMonitoringRules] (ProviderId, OfferingId, Id) ON DELETE NO ACTION
                """,
        };
        foreach (var table in tables)
        {
            var create = sql ? $"IF OBJECT_ID('{table.Key}', 'U') IS NULL CREATE TABLE" : "CREATE TABLE IF NOT EXISTS";
            scripts.Add($"""
                {create} {prefix}[{table.Key}] (
                    Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, OfferingId {guid} NOT NULL,
                    Revision {number} NOT NULL, CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL,
                    {table.Value},
                    CONSTRAINT AK_{table.Key}_Owner UNIQUE (ProviderId, OfferingId, Id),
                    FOREIGN KEY (ProviderId) REFERENCES {prefix}[CspProfiles] (Id) ON DELETE NO ACTION,
                    FOREIGN KEY (ProviderId, OfferingId) REFERENCES {prefix}[ProviderOfferings] (ProviderId, Id) ON DELETE NO ACTION
                );
                """);
        }
        foreach (var (table, columns, unique) in new[]
        {
            ("ProviderMonitoringRules", "ProviderId, OfferingId", false),
            ("ProviderMonitoringRules", "ProviderId, IsEnabled, NextEvaluationUtcTicks", false),
            ("ProviderMonitoringEvaluations", "ProviderId, OfferingId", false),
            ("ProviderMonitoringEvaluations", "ProviderId, OfferingId, RuleId, RuleRevision, ObservationHash", true)
        })
        {
            var name = $"IX_{table}_{columns.Replace(", ", "_")}";
            var guard = sql ? $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('dbo.{table}')) " : "";
            scripts.Add($"{guard}CREATE {(unique ? "UNIQUE " : "")}INDEX {(sql ? "" : "IF NOT EXISTS ")}[{name}] ON {prefix}[{table}] ({columns});");
        }
        return scripts;
    }
}

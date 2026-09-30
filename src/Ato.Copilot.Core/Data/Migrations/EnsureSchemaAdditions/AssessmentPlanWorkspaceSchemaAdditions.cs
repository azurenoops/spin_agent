using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class AssessmentPlanWorkspaceSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlServer() && !db.Database.IsSqlite())
            throw new NotSupportedException("Assessment plan schema requires SQLite or SQL Server.");
        var columns = new (string Table, string Name, string SqlServer, string Sqlite)[]
        {
            ("SecurityAssessmentPlans", "Revision", "bigint NOT NULL DEFAULT 1", "INTEGER NOT NULL DEFAULT 1"),
            ("SecurityAssessmentPlans", "UpdatedAt", "datetime2 NULL", "TEXT NULL"),
            ("SecurityAssessmentPlans", "UpdatedBy", "nvarchar(200) NULL", "TEXT NULL"),
            ("SecurityAssessmentPlans", "AssessmentLeadUserId", "nvarchar(200) NULL", "TEXT NULL"),
            ("SecurityAssessmentPlans", "GenerationRequestId", "nvarchar(200) NULL", "TEXT NULL"),
            ("SecurityAssessmentPlans", "PreviousPlanId", "nvarchar(36) NULL", "TEXT NULL"),
            ("SapControlEntries", "IsExcluded", "bit NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
            ("SapControlEntries", "ExclusionRationale", "nvarchar(4000) NULL", "TEXT NULL")
        };
        foreach (var (table, name, sqlServer, sqlite) in columns)
        {
            // Identifiers/types are fixed declarations, never request input.
            if (db.Database.IsSqlServer())
            {
                var ddl = $"IF COL_LENGTH('dbo.{table}', '{name}') IS NULL ALTER TABLE dbo.{table} ADD {name} {sqlServer};";
                await db.Database.ExecuteSqlRawAsync(ddl, ct);
            }
            else
            {
                var exists = await db.Database.SqlQuery<int>(
                    $"SELECT COUNT(*) AS Value FROM pragma_table_info({table}) WHERE name = {name}").SingleAsync(ct);
                if (exists == 0)
                {
                    var ddl = $"ALTER TABLE {table} ADD COLUMN {name} {sqlite};";
                    await db.Database.ExecuteSqlRawAsync(ddl, ct);
                }
            }
        }
        var indexes = new (string Name, string Columns, string Filter)[]
        {
            ("UX_Sap_WorkingDraft", "TenantId, RegisteredSystemId", "[Status] = 'Draft'"),
            ("UX_Sap_GenerationRequest", "TenantId, RegisteredSystemId, GenerationRequestId", "[GenerationRequestId] IS NOT NULL"),
            ("UX_Sap_PreviousPlan", "TenantId, RegisteredSystemId, PreviousPlanId", "[PreviousPlanId] IS NOT NULL")
        };
        foreach (var (name, columnsSql, filter) in indexes)
        {
            var sql = db.Database.IsSqlServer()
                ? $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('dbo.SecurityAssessmentPlans')) CREATE UNIQUE INDEX {name} ON dbo.SecurityAssessmentPlans ({columnsSql}) WHERE {filter};"
                : $"CREATE UNIQUE INDEX IF NOT EXISTS {name} ON SecurityAssessmentPlans ({columnsSql}) WHERE {filter};";
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        logger.LogInformation("Verified additive assessment plan revisions, exclusions and retry keys");
    }
}

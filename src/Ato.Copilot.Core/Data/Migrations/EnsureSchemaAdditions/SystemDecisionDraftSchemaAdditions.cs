using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class SystemDecisionDraftSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        var columns = new (string Table, string Column, string SqlType)[]
        {
            ("AuthorizationDecisions", "SourceEvidenceId", "nvarchar(36)"),
            ("AuthorizationDecisions", "SourceEvidenceHash", "nvarchar(128)"),
            ("AuthorizationDecisions", "ExternalIssuingAuthority", "nvarchar(500)"),
            ("AuthorizationDecisions", "BaselinePackageId", "nvarchar(36)"),
            ("AuthorizationDecisions", "BaselinePackageHash", "nvarchar(128)"),
            ("AuthorizationDecisions", "RecordedBy", "nvarchar(200)"),
            ("AuthorizationDecisions", "RecordedAt", "datetime2"),
            ("SecurityAssessmentPlans", "AssessmentLead", "nvarchar(200)"),
            ("SecurityAssessmentPlans", "AssessmentApproach", "nvarchar(4000)"),
        };
        foreach (var (table, column, sqlType) in columns)
        {
            // Identifiers and SQL types come only from the fixed schema declarations above.
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlRawAsync(
                    $"IF COL_LENGTH('dbo.{table}', '{column}') IS NULL ALTER TABLE dbo.{table} ADD {column} {sqlType} NULL;", ct);
            else if (db.Database.IsSqlite())
            {
                var exists = await db.Database.SqlQueryRaw<int>(
                    $"SELECT COUNT(*) AS Value FROM pragma_table_info('{table}') WHERE name = '{column}'").SingleAsync(ct);
                if (exists == 0) await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} TEXT NULL", ct);
            }
            else throw new NotSupportedException("System decision/draft metadata requires SQLite or SQL Server.");
        }
        logger.LogInformation("Verified additive system decision-source and SAP draft fields");
    }
}

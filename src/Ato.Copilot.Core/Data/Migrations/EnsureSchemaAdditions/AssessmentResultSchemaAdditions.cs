using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class AssessmentResultSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, CancellationToken ct = default)
    {
        foreach (var table in new[] { "Assessments", "ScanImportRecords", "SecurityAssessmentReports" })
        {
            // DDL identifiers are fixed schema declarations, never caller-provided values.
            foreach (var column in new[] { "WorkspaceOperationKey",
                table == "SecurityAssessmentReports" ? "SourceSnapshotJson" : "ResultProvenanceJson" })
            {
                var type = column == "WorkspaceOperationKey" ? "nvarchar(64)" : "nvarchar(max)";
                if (db.Database.IsSqlServer())
                {
                    var ddl = $"IF COL_LENGTH('dbo.{table}', '{column}') IS NULL ALTER TABLE dbo.[{table}] ADD [{column}] {type} NULL";
                    await db.Database.ExecuteSqlRawAsync(ddl, ct);
                }
                else if (db.Database.IsSqlite())
                {
                    var count = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM pragma_table_info({table}) WHERE name = {column}").SingleAsync(ct);
                    if (count == 0)
                    {
                        var ddl = $"ALTER TABLE [{table}] ADD COLUMN [{column}] TEXT NULL";
                        await db.Database.ExecuteSqlRawAsync(ddl, ct);
                    }
                }
                else throw new NotSupportedException("Assessment provenance requires SQLite or SQL Server.");
            }
            var indexDdl = db.Database.IsSqlite()
                ? $"CREATE UNIQUE INDEX IF NOT EXISTS IX_{table}_WorkspaceOperationKey ON [{table}](WorkspaceOperationKey) WHERE WorkspaceOperationKey IS NOT NULL"
                : $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_{table}_WorkspaceOperationKey' AND object_id = OBJECT_ID('dbo.{table}')) CREATE UNIQUE INDEX IX_{table}_WorkspaceOperationKey ON dbo.[{table}](WorkspaceOperationKey) WHERE WorkspaceOperationKey IS NOT NULL";
            await db.Database.ExecuteSqlRawAsync(indexDdl, ct);
        }
        if (db.Database.IsSqlite())
            await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_ScanImportRecords_TenantId_RegisteredSystemId_FileHash ON ScanImportRecords(TenantId,RegisteredSystemId,FileHash) WHERE WorkspaceOperationKey IS NOT NULL", ct);
        else
            await db.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ScanImportRecords_TenantId_RegisteredSystemId_FileHash' AND object_id = OBJECT_ID('dbo.ScanImportRecords')) CREATE UNIQUE INDEX IX_ScanImportRecords_TenantId_RegisteredSystemId_FileHash ON dbo.ScanImportRecords(TenantId,RegisteredSystemId,FileHash) WHERE WorkspaceOperationKey IS NOT NULL", ct);
    }
}

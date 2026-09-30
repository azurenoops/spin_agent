using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Additive retention metadata; legacy assignments deliberately remain unretained.</summary>
public static class PolicyReferenceSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, CancellationToken ct = default)
    {
        var columns = new (string Name, string SqlServer, string Sqlite)[]
        {
            ("PolicyRationale", "nvarchar(500) NULL", "TEXT NULL"),
            ("PolicySourceSnapshotJson", "nvarchar(max) NULL", "TEXT NULL"),
            ("PolicySourceRevision", "nvarchar(64) NULL", "TEXT NULL"),
            ("PolicySourceCapturedAt", "datetime2 NULL", "TEXT NULL"),
            ("PolicySourceModifiedAt", "datetime2 NULL", "TEXT NULL"),
            ("PolicyRevision", "int NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
            ("PolicyReferenceKey", "nvarchar(73) NULL", "TEXT NULL")
        };
        foreach (var column in columns)
        {
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlRawAsync(
                    $"IF COL_LENGTH('dbo.ComponentSystemAssignments', '{column.Name}') IS NULL ALTER TABLE dbo.ComponentSystemAssignments ADD [{column.Name}] {column.SqlServer}", ct);
            else if (db.Database.IsSqlite())
            {
                var count = await db.Database.SqlQueryRaw<int>(
                    $"SELECT COUNT(*) AS Value FROM pragma_table_info('ComponentSystemAssignments') WHERE name = '{column.Name}'").SingleAsync(ct);
                if (count == 0)
                    await db.Database.ExecuteSqlRawAsync(
                        $"ALTER TABLE ComponentSystemAssignments ADD COLUMN [{column.Name}] {column.Sqlite}", ct);
            }
            else throw new NotSupportedException("Policy references require SQLite or SQL Server.");
        }
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ComponentSystemAssignment_PolicyReference'
                    AND object_id = OBJECT_ID('dbo.ComponentSystemAssignments'))
                    CREATE UNIQUE INDEX IX_ComponentSystemAssignment_PolicyReference
                    ON dbo.ComponentSystemAssignments(TenantId, PolicyReferenceKey) WHERE PolicyReferenceKey IS NOT NULL;
                """, ct);
        else
            await db.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ComponentSystemAssignment_PolicyReference
                ON ComponentSystemAssignments(TenantId, PolicyReferenceKey) WHERE PolicyReferenceKey IS NOT NULL
                """, ct);
    }
}

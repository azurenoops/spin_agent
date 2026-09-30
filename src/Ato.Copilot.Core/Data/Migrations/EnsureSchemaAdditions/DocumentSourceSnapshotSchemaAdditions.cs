using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Adds retained review/generation content without inferring legacy approved child rows.</summary>
public static class DocumentSourceSnapshotSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, CancellationToken ct = default)
    {
        var columns = new (string Table, string Column, string SqlType)[]
        {
            ("SystemProfileSections", "ApprovedSnapshotId", "nvarchar(36)"),
            ("ProfileAuditEntries", "SnapshotJson", "nvarchar(max)"),
            ("ProfileAuditEntries", "SnapshotHash", "nvarchar(64)"),
            ("SspExports", "SourceManifestJson", "nvarchar(max)"),
            ("SspExports", "SourceGapsJson", "nvarchar(max)"),
            ("SspExports", "SourcePreviewId", "uniqueidentifier"),
            ("SspExports", "RequestScopeKey", "nvarchar(64)"),
            ("SspExports", "SourceTenantId", "uniqueidentifier"),
            ("SspExports", "RequestedPersonId", "uniqueidentifier"),
            ("AuthorizationPackages", "RetainedContextJson", "nvarchar(max)"),
            ("AuthorizationPackages", "RetainedContextHash", "nvarchar(64)"),
            ("AuthorizationPackages", "RequestScopeKey", "nvarchar(64)"),
            ("AuthorizationPackages", "RequestIntentHash", "nvarchar(64)")
        };
        foreach (var column in columns)
        {
            // Identifiers/types are fixed literals above, never request-derived.
            if (db.Database.IsSqlServer())
            {
                var sql = $"IF COL_LENGTH('dbo.{column.Table}', '{column.Column}') IS NULL ALTER TABLE dbo.[{column.Table}] ADD [{column.Column}] {column.SqlType} NULL";
                await db.Database.ExecuteSqlRawAsync(sql, ct);
            }
            else if (db.Database.IsSqlite())
            {
                var query = $"SELECT COUNT(*) AS Value FROM pragma_table_info('{column.Table}') WHERE name = '{column.Column}'";
                var count = await db.Database.SqlQueryRaw<int>(query).SingleAsync(ct);
                if (count == 0)
                {
                    var sql = $"ALTER TABLE [{column.Table}] ADD COLUMN [{column.Column}] TEXT NULL";
                    await db.Database.ExecuteSqlRawAsync(sql, ct);
                }
            }
            else throw new NotSupportedException("Document snapshots require SQLite or SQL Server.");
        }
        if (db.Database.IsSqlite())
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_SspExports_RequestScopeKey ON SspExports(RequestScopeKey) WHERE RequestScopeKey IS NOT NULL", ct);
        else
            await db.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SspExports_RequestScopeKey' AND object_id = OBJECT_ID('dbo.SspExports'))
                    CREATE UNIQUE INDEX IX_SspExports_RequestScopeKey ON dbo.SspExports(RequestScopeKey) WHERE RequestScopeKey IS NOT NULL;
                """, ct);
        if (db.Database.IsSqlite())
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_AuthorizationPackages_RequestScopeKey ON AuthorizationPackages(RequestScopeKey) WHERE RequestScopeKey IS NOT NULL", ct);
        else
            await db.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuthorizationPackages_RequestScopeKey' AND object_id = OBJECT_ID('dbo.AuthorizationPackages'))
                    CREATE UNIQUE INDEX IX_AuthorizationPackages_RequestScopeKey ON dbo.AuthorizationPackages(RequestScopeKey) WHERE RequestScopeKey IS NOT NULL;
                """, ct);
    }
}

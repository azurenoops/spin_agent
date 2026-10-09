using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Legacy category rows start as drafts; section approval never implies individual row approval.</summary>
public static class UserCategoryReviewSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, CancellationToken ct = default)
    {
        var columns = new (string Table, string Name, string SqlServer, string Sqlite)[]
        {
            ("UserCategories", "GovernanceStatus", "nvarchar(20) NOT NULL DEFAULT 'Draft'", "TEXT NOT NULL DEFAULT 'Draft'"),
            ("UserCategories", "Revision", "int NOT NULL DEFAULT 1", "INTEGER NOT NULL DEFAULT 1"),
            ("UserCategories", "PendingDeletion", "bit NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0"),
            ("UserCategories", "SubmittedBy", "nvarchar(200) NULL", "TEXT NULL"),
            ("UserCategories", "SubmittedAt", "datetime2 NULL", "TEXT NULL"),
            ("UserCategories", "ReviewedBy", "nvarchar(200) NULL", "TEXT NULL"),
            ("UserCategories", "ReviewedAt", "datetime2 NULL", "TEXT NULL"),
            ("UserCategories", "ReviewerComments", "nvarchar(2000) NULL", "TEXT NULL"),
            ("UserCategories", "ApprovedSnapshotId", "nvarchar(36) NULL", "TEXT NULL"),
            ("UserCategories", "IdentityType", "nvarchar(40) NULL", "TEXT NULL"),
            ("UserCategories", "PrivilegeLevel", "nvarchar(40) NULL", "TEXT NULL"),
            ("UserCategories", "Affiliation", "nvarchar(40) NULL", "TEXT NULL"),
            ("UserCategories", "AuthenticationMethod", "nvarchar(500) NULL", "TEXT NULL"),
            ("UserCategories", "ResponsibleOwner", "nvarchar(500) NULL", "TEXT NULL"),
            ("UserCategories", "UserLocations", "nvarchar(1000) NULL", "TEXT NULL"),
            ("UserCategories", "PermittedEnvironments", "nvarchar(1000) NULL", "TEXT NULL"),
            ("UserCategories", "AuthorizedDataTypes", "nvarchar(2000) NULL", "TEXT NULL"),
            ("ProfileAuditEntries", "UserCategoryId", "nvarchar(36) NULL", "TEXT NULL"),
            ("ProfileAuditEntries", "UserCategoryRevision", "int NULL", "INTEGER NULL")
        };
        foreach (var column in columns)
        {
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlRawAsync(
                    $"IF COL_LENGTH('dbo.{column.Table}', '{column.Name}') IS NULL ALTER TABLE dbo.[{column.Table}] ADD [{column.Name}] {column.SqlServer}", ct);
            else if (db.Database.IsSqlite())
            {
                var count = await db.Database.SqlQueryRaw<int>(
                    $"SELECT COUNT(*) AS Value FROM pragma_table_info('{column.Table}') WHERE name = '{column.Name}'").SingleAsync(ct);
                if (count == 0)
                    await db.Database.ExecuteSqlRawAsync(
                        $"ALTER TABLE [{column.Table}] ADD COLUMN [{column.Name}] {column.Sqlite}", ct);
            }
            else throw new NotSupportedException("User category reviews require SQLite or SQL Server.");
        }
    }
}

using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Additive evaluation history; the existing package-validation relationship remains unchanged.</summary>
public static class PackageReadinessSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, CancellationToken ct = default)
    {
        if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID('dbo.PackageReadinessRuns', 'U') IS NULL
                CREATE TABLE dbo.PackageReadinessRuns (
                    Id nvarchar(36) NOT NULL PRIMARY KEY, TenantId uniqueidentifier NOT NULL,
                    RegisteredSystemId nvarchar(36) NOT NULL, Purpose int NOT NULL,
                    SelectionHash nvarchar(64) NOT NULL, RetainedSelectionJson nvarchar(max) NULL,
                    Outcome nvarchar(32) NOT NULL, StartedAt datetime2 NOT NULL, EvaluatedAt datetime2 NOT NULL,
                    EvaluatedBy nvarchar(200) NOT NULL, EvaluatedPersonId uniqueidentifier NULL,
                    SourceHash nvarchar(64) NULL, SourceHashAfter nvarchar(64) NULL, RuleVersion nvarchar(80) NOT NULL,
                    ChecksJson nvarchar(max) NOT NULL, SourcesJson nvarchar(max) NOT NULL,
                    FailureJson nvarchar(max) NULL, ValidUntil datetime2 NULL,
                    CONSTRAINT FK_PackageReadinessRuns_RegisteredSystems FOREIGN KEY (RegisteredSystemId)
                        REFERENCES dbo.RegisteredSystems(Id));
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PackageReadinessRuns_Scope'
                    AND object_id = OBJECT_ID('dbo.PackageReadinessRuns'))
                    CREATE INDEX IX_PackageReadinessRuns_Scope ON dbo.PackageReadinessRuns
                    (TenantId, RegisteredSystemId, Purpose, SelectionHash, EvaluatedAt);
                IF COL_LENGTH('dbo.AuthorizationPackages', 'ReadinessRunId') IS NULL
                    ALTER TABLE dbo.AuthorizationPackages ADD ReadinessRunId nvarchar(36) NULL;
                IF COL_LENGTH('dbo.AuthorizationPackages', 'ReadinessSourceHash') IS NULL
                    ALTER TABLE dbo.AuthorizationPackages ADD ReadinessSourceHash nvarchar(64) NULL;
                """, ct);
        }
        else if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS PackageReadinessRuns (
                    Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, RegisteredSystemId TEXT NOT NULL,
                    Purpose INTEGER NOT NULL, SelectionHash TEXT NOT NULL, RetainedSelectionJson TEXT NULL,
                    Outcome TEXT NOT NULL, StartedAt TEXT NOT NULL, EvaluatedAt TEXT NOT NULL,
                    EvaluatedBy TEXT NOT NULL, EvaluatedPersonId TEXT NULL, SourceHash TEXT NULL,
                    SourceHashAfter TEXT NULL, RuleVersion TEXT NOT NULL, ChecksJson TEXT NOT NULL,
                    SourcesJson TEXT NOT NULL, FailureJson TEXT NULL, ValidUntil TEXT NULL,
                    FOREIGN KEY (RegisteredSystemId) REFERENCES RegisteredSystems(Id) ON DELETE RESTRICT);
                CREATE INDEX IF NOT EXISTS IX_PackageReadinessRuns_Scope ON PackageReadinessRuns
                    (TenantId, RegisteredSystemId, Purpose, SelectionHash, EvaluatedAt);
                """, ct);
            foreach (var column in new[] { "ReadinessRunId", "ReadinessSourceHash" })
            {
                var exists = await db.Database.SqlQueryRaw<int>(
                    $"SELECT COUNT(*) AS Value FROM pragma_table_info('AuthorizationPackages') WHERE name = '{column}'").SingleAsync(ct);
                if (exists == 0)
                    await db.Database.ExecuteSqlRawAsync($"ALTER TABLE AuthorizationPackages ADD COLUMN [{column}] TEXT NULL", ct);
            }
        }
        else throw new NotSupportedException("Package readiness requires SQL Server or SQLite.");
    }
}

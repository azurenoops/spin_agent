using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class ConnectedRemediationSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite() && !db.Database.IsSqlServer())
            throw new NotSupportedException("Connected remediation requires SQLite or SQL Server.");
        var sqlite = db.Database.IsSqlite();
        var columns = new (string Name, string SqlServer, string Sqlite)[]
        {
            ("RegisteredSystemId", "nvarchar(36) NULL", "TEXT NULL"),
            ("VerificationStatus", "nvarchar(20) NOT NULL DEFAULT 'NotVerified'", "TEXT NOT NULL DEFAULT 'NotVerified'"),
            ("VerificationNotes", "nvarchar(max) NULL", "TEXT NULL"),
            ("VerifiedBy", "nvarchar(200) NULL", "TEXT NULL"),
            ("VerifiedAt", "datetime2 NULL", "TEXT NULL"),
            ("EvidenceReferencesJson", "nvarchar(max) NOT NULL DEFAULT '[]'", "TEXT NOT NULL DEFAULT '[]'"),
            ("WorkspaceOperationKey", "nvarchar(64) NULL", "TEXT NULL"),
            ("WorkspaceIntentHash", "nvarchar(64) NULL", "TEXT NULL")
        };
        foreach (var column in columns)
        {
            if (sqlite)
            {
                var exists = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM pragma_table_info('RemediationTasks') WHERE name = {column.Name}").SingleAsync(ct);
                if (exists != 0) continue;
            }
            // Identifiers/types come exclusively from the fixed schema declarations above.
            var ddl = sqlite ? $"ALTER TABLE RemediationTasks ADD COLUMN [{column.Name}] {column.Sqlite}"
                : $"IF COL_LENGTH('dbo.RemediationTasks', '{column.Name}') IS NULL ALTER TABLE dbo.RemediationTasks ADD [{column.Name}] {column.SqlServer}";
            await db.Database.ExecuteSqlRawAsync(ddl, ct);
        }
        var tableDdl = sqlite ? """
            CREATE TABLE IF NOT EXISTS PoamTaskLinks (
                TenantId TEXT NOT NULL, RegisteredSystemId TEXT NOT NULL,
                PoamItemId TEXT NOT NULL, RemediationTaskId TEXT NOT NULL,
                LinkedBy TEXT NOT NULL, LinkedAt TEXT NOT NULL,
                PRIMARY KEY(PoamItemId,RemediationTaskId),
                FOREIGN KEY(PoamItemId) REFERENCES PoamItems(Id) ON DELETE CASCADE,
                FOREIGN KEY(RemediationTaskId) REFERENCES RemediationTasks(Id) ON DELETE RESTRICT);
            CREATE INDEX IF NOT EXISTS IX_PoamTaskLinks_TenantId_RegisteredSystemId ON PoamTaskLinks(TenantId,RegisteredSystemId);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_RemediationTasks_WorkspaceOperationKey ON RemediationTasks(WorkspaceOperationKey) WHERE WorkspaceOperationKey IS NOT NULL;
            """ : """
            IF OBJECT_ID('dbo.PoamTaskLinks', 'U') IS NULL
            CREATE TABLE dbo.PoamTaskLinks (
                TenantId uniqueidentifier NOT NULL, RegisteredSystemId nvarchar(36) NOT NULL,
                PoamItemId nvarchar(36) NOT NULL, RemediationTaskId nvarchar(36) NOT NULL,
                LinkedBy nvarchar(200) NOT NULL, LinkedAt datetime2 NOT NULL,
                CONSTRAINT PK_PoamTaskLinks PRIMARY KEY(PoamItemId,RemediationTaskId),
                CONSTRAINT FK_PoamTaskLinks_PoamItems FOREIGN KEY(PoamItemId) REFERENCES dbo.PoamItems(Id) ON DELETE CASCADE,
                CONSTRAINT FK_PoamTaskLinks_RemediationTasks FOREIGN KEY(RemediationTaskId) REFERENCES dbo.RemediationTasks(Id));
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_PoamTaskLinks_TenantId_RegisteredSystemId' AND object_id=OBJECT_ID('dbo.PoamTaskLinks'))
                CREATE INDEX IX_PoamTaskLinks_TenantId_RegisteredSystemId ON dbo.PoamTaskLinks(TenantId,RegisteredSystemId);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RemediationTasks_WorkspaceOperationKey' AND object_id=OBJECT_ID('dbo.RemediationTasks'))
                CREATE UNIQUE INDEX IX_RemediationTasks_WorkspaceOperationKey ON dbo.RemediationTasks(WorkspaceOperationKey) WHERE WorkspaceOperationKey IS NOT NULL;
            """;
        await db.Database.ExecuteSqlRawAsync(tableDdl, ct);
    }
}

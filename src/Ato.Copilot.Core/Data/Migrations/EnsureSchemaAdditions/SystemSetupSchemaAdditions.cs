using System.Data;
using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class SystemSetupSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsRelational()) return;
        await TenantIdColumnAdditions.ApplyAsync(db, logger, ct);
        if (db.Database.IsSqlite())
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            if (command.Connection!.State != ConnectionState.Open) await command.Connection.OpenAsync(ct);
            command.CommandText = "PRAGMA table_info('RegisteredSystems')";
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
            foreach (var (name, sql) in new[]
            {
                ("SetupDraftJson", "ALTER TABLE RegisteredSystems ADD COLUMN SetupDraftJson TEXT NULL"),
                ("SetupRevision", "ALTER TABLE RegisteredSystems ADD COLUMN SetupRevision INTEGER NOT NULL DEFAULT 0"),
                ("SetupCompletedAt", "ALTER TABLE RegisteredSystems ADD COLUMN SetupCompletedAt TEXT NULL"),
                ("SetupRequestKey", "ALTER TABLE RegisteredSystems ADD COLUMN SetupRequestKey TEXT NULL"),
                ("SetupRequestHash", "ALTER TABLE RegisteredSystems ADD COLUMN SetupRequestHash TEXT NULL"),
                ("SetupActorPersonId", "ALTER TABLE RegisteredSystems ADD COLUMN SetupActorPersonId TEXT NULL"),
                ("SetupLastCommandJson", "ALTER TABLE RegisteredSystems ADD COLUMN SetupLastCommandJson TEXT NULL")
            })
                if (!columns.Contains(name))
                    await db.Database.ExecuteSqlRawAsync(sql, ct);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS UX_RegisteredSystem_SetupRequest
                ON RegisteredSystems(TenantId,SetupActorPersonId,SetupRequestKey) WHERE SetupRequestKey IS NOT NULL;
                """, ct);
        }
        else if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'dbo.RegisteredSystems', N'SetupDraftJson') IS NULL ALTER TABLE dbo.RegisteredSystems ADD SetupDraftJson NVARCHAR(MAX) NULL;
                IF COL_LENGTH(N'dbo.RegisteredSystems', N'SetupRevision') IS NULL ALTER TABLE dbo.RegisteredSystems ADD SetupRevision BIGINT NOT NULL DEFAULT 0;
                IF COL_LENGTH(N'dbo.RegisteredSystems', N'SetupCompletedAt') IS NULL ALTER TABLE dbo.RegisteredSystems ADD SetupCompletedAt DATETIMEOFFSET NULL;
                IF COL_LENGTH(N'dbo.RegisteredSystems', N'SetupRequestKey') IS NULL ALTER TABLE dbo.RegisteredSystems ADD SetupRequestKey NVARCHAR(100) NULL;
                IF COL_LENGTH(N'dbo.RegisteredSystems', N'SetupRequestHash') IS NULL ALTER TABLE dbo.RegisteredSystems ADD SetupRequestHash CHAR(64) NULL;
                IF COL_LENGTH(N'dbo.RegisteredSystems', N'SetupActorPersonId') IS NULL ALTER TABLE dbo.RegisteredSystems ADD SetupActorPersonId UNIQUEIDENTIFIER NULL;
                IF COL_LENGTH(N'dbo.RegisteredSystems', N'SetupLastCommandJson') IS NULL ALTER TABLE dbo.RegisteredSystems ADD SetupLastCommandJson NVARCHAR(MAX) NULL;
                """, ct);
            // Compile the filtered index only after its new columns exist.
            await db.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_RegisteredSystem_SetupRequest' AND object_id=OBJECT_ID(N'dbo.RegisteredSystems'))
                    CREATE UNIQUE INDEX UX_RegisteredSystem_SetupRequest ON dbo.RegisteredSystems(TenantId,SetupActorPersonId,SetupRequestKey) WHERE SetupRequestKey IS NOT NULL;
                """, ct);
        }
        else throw new NotSupportedException($"System setup schema does not support {db.Database.ProviderName}.");
        await ApplySourceColumnsAsync(db, ct);
        logger.LogInformation("Verified additive registered-system setup schema");
    }

    private static async Task ApplySourceColumnsAsync(AtoCopilotContext db, CancellationToken ct)
    {
        foreach (var (table, index) in new[]
        {
            ("EmassImportSessions", "UX_EmassImportSession_SystemSourceRequest"),
            ("SspPdfImportSessions", "UX_SspPdfImportSession_SystemSourceRequest")
        })
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (db.Database.IsSqlite())
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                if (command.Connection!.State != ConnectionState.Open) await command.Connection.OpenAsync(ct);
                // Identifiers below are fixed schema constants, never request data.
                command.CommandText = $"PRAGMA table_info('{table}')";
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
                if (columns.Count == 0) continue;
            }
            foreach (var (name, sqlite, sqlServer) in new[]
            {
                ("TargetSystemId", "TEXT NULL", "NVARCHAR(36) NULL"),
                ("RequestKey", "TEXT NULL", "NVARCHAR(100) NULL"),
                ("RequestPayloadHash", "TEXT NULL", "CHAR(64) NULL"),
                ("RequestActorPersonId", "TEXT NULL", "UNIQUEIDENTIFIER NULL"),
                ("ReviewRevision", "INTEGER NOT NULL DEFAULT 0", "BIGINT NOT NULL DEFAULT 0"),
                ("ReviewProposalJson", "TEXT NULL", "NVARCHAR(MAX) NULL"),
                ("ReviewSnapshotJson", "TEXT NULL", "NVARCHAR(MAX) NULL"),
                ("ApplyReceiptJson", "TEXT NULL", "NVARCHAR(MAX) NULL")
            })
            {
                if (db.Database.IsSqlite() && columns.Contains(name)) continue;
                var sql = db.Database.IsSqlite() ? $"ALTER TABLE \"{table}\" ADD COLUMN \"{name}\" {sqlite}"
                    : $"IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.{table}', N'{name}') IS NULL ALTER TABLE dbo.[{table}] ADD [{name}] {sqlServer};";
                await db.Database.ExecuteSqlRawAsync(sql, ct);
            }
            var indexSql = db.Database.IsSqlite()
                ? $"CREATE UNIQUE INDEX IF NOT EXISTS [{index}] ON [{table}](TenantId,TargetSystemId,RequestActorPersonId,RequestKey) WHERE RequestKey IS NOT NULL;"
                : $"IF OBJECT_ID(N'dbo.{table}',N'U') IS NOT NULL AND NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'{index}' AND object_id=OBJECT_ID(N'dbo.{table}')) CREATE UNIQUE INDEX [{index}] ON dbo.[{table}](TenantId,TargetSystemId,RequestActorPersonId,RequestKey) WHERE RequestKey IS NOT NULL;";
            await db.Database.ExecuteSqlRawAsync(indexSql, ct);
        }
    }
}

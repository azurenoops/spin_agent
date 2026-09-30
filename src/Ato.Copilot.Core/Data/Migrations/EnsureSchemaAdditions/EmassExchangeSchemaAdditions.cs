using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class EmassExchangeSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlRawAsync(SqlServer, ct);
        else if (db.Database.IsSqlite())
            await db.Database.ExecuteSqlRawAsync(Sqlite, ct);
        else
            throw new NotSupportedException($"Exchange history schema does not support {db.Database.ProviderName}.");
        logger.LogInformation("Verified Feature 079 manual eMASS exchange history schema.");
    }

    private const string SqlServer = """
        IF OBJECT_ID(N'dbo.EmassExchangeRecords', N'U') IS NULL
        BEGIN
          CREATE TABLE dbo.EmassExchangeRecords (
            Id nvarchar(36) NOT NULL PRIMARY KEY,
            TenantId uniqueidentifier NOT NULL,
            RegisteredSystemId nvarchar(36) NOT NULL,
            Version bigint NOT NULL,
            PackageId nvarchar(36) NOT NULL,
            PackageHash nvarchar(128) NOT NULL,
            ExportGeneratedAt datetimeoffset NOT NULL,
            Outcome nvarchar(32) NOT NULL,
            ReceivingWorkflow nvarchar(200) NOT NULL,
            ExternalReference nvarchar(500) NOT NULL,
            OccurredAt datetimeoffset NOT NULL,
            RecordedAt datetimeoffset NOT NULL,
            RecordedBy nvarchar(200) NOT NULL,
            Notes nvarchar(4000) NOT NULL,
            SupersedesId nvarchar(36) NULL,
            IdempotencyKey nvarchar(100) NOT NULL,
            RequestHash nvarchar(64) NOT NULL
          );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EmassExchangeRecords_TenantId_RegisteredSystemId_Version'
          AND object_id = OBJECT_ID(N'dbo.EmassExchangeRecords'))
          CREATE UNIQUE INDEX IX_EmassExchangeRecords_TenantId_RegisteredSystemId_Version
          ON dbo.EmassExchangeRecords(TenantId, RegisteredSystemId, Version);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EmassExchangeRecords_TenantId_RegisteredSystemId_IdempotencyKey'
          AND object_id = OBJECT_ID(N'dbo.EmassExchangeRecords'))
          CREATE UNIQUE INDEX IX_EmassExchangeRecords_TenantId_RegisteredSystemId_IdempotencyKey
          ON dbo.EmassExchangeRecords(TenantId, RegisteredSystemId, IdempotencyKey);
        """;

    private const string Sqlite = """
        CREATE TABLE IF NOT EXISTS EmassExchangeRecords (
          Id TEXT NOT NULL PRIMARY KEY,
          TenantId TEXT NOT NULL,
          RegisteredSystemId TEXT NOT NULL,
          Version INTEGER NOT NULL,
          PackageId TEXT NOT NULL,
          PackageHash TEXT NOT NULL,
          ExportGeneratedAt TEXT NOT NULL,
          Outcome TEXT NOT NULL,
          ReceivingWorkflow TEXT NOT NULL,
          ExternalReference TEXT NOT NULL,
          OccurredAt TEXT NOT NULL,
          RecordedAt TEXT NOT NULL,
          RecordedBy TEXT NOT NULL,
          Notes TEXT NOT NULL,
          SupersedesId TEXT NULL,
          IdempotencyKey TEXT NOT NULL,
          RequestHash TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_EmassExchangeRecords_TenantId_RegisteredSystemId_Version
          ON EmassExchangeRecords(TenantId, RegisteredSystemId, Version);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_EmassExchangeRecords_TenantId_RegisteredSystemId_IdempotencyKey
          ON EmassExchangeRecords(TenantId, RegisteredSystemId, IdempotencyKey);
        """;
}

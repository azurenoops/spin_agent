using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Idempotent additive schema for Feature 071 eMASS sync conflicts.</summary>
public static class EmassConflictsSchemaAdditions
{
    public static async Task ApplyAsync(
        AtoCopilotContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        var providerName = db.Database.ProviderName ?? string.Empty;
        try
        {
            if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
                await db.Database.ExecuteSqlRawAsync(SqlServerScript, cancellationToken);
            else if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
                await db.Database.ExecuteSqlRawAsync(SqliteScript, cancellationToken);
            else
            {
                logger.LogWarning(
                    "EmassConflictsSchemaAdditions: skipping unsupported provider {Provider}",
                    providerName);
                return;
            }

            logger.LogInformation("Verified Feature 071 EmassConflicts schema on {Provider}", providerName);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "EmassConflictsSchemaAdditions: DDL failed for provider {Provider}",
                providerName);
            throw new InvalidOperationException(
                $"Database schema initialization failed in EmassConflictsSchemaAdditions for provider '{providerName}'.",
                exception);
        }
    }

    private const string SqlServerScript = """
        IF OBJECT_ID(N'dbo.EmassConflicts', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.EmassConflicts (
                Id NVARCHAR(36) NOT NULL CONSTRAINT PK_EmassConflicts PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL,
                RegisteredSystemId NVARCHAR(36) NOT NULL,
                SyncBatchId NVARCHAR(36) NOT NULL,
                EntityType NVARCHAR(50) NOT NULL,
                EntityId NVARCHAR(36) NULL,
                FieldName NVARCHAR(200) NOT NULL,
                SpinValue NVARCHAR(4000) NULL,
                EmassValue NVARCHAR(4000) NULL,
                ConflictStatus NVARCHAR(20) NOT NULL,
                DetectedAt DATETIMEOFFSET NOT NULL,
                ResolvedAt DATETIMEOFFSET NULL,
                ResolvedBy NVARCHAR(200) NULL,
                Notes NVARCHAR(1000) NULL,
                CONSTRAINT FK_EmassConflicts_Tenants_TenantId
                    FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT FK_EmassConflicts_RegisteredSystems_RegisteredSystemId
                    FOREIGN KEY (RegisteredSystemId) REFERENCES dbo.RegisteredSystems(Id)
                    ON DELETE CASCADE
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EmassConflict_SystemId_Status' AND object_id = OBJECT_ID(N'dbo.EmassConflicts'))
            CREATE INDEX IX_EmassConflict_SystemId_Status ON dbo.EmassConflicts(RegisteredSystemId, ConflictStatus);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EmassConflict_BatchId' AND object_id = OBJECT_ID(N'dbo.EmassConflicts'))
            CREATE INDEX IX_EmassConflict_BatchId ON dbo.EmassConflicts(SyncBatchId);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EmassConflicts_TenantId' AND object_id = OBJECT_ID(N'dbo.EmassConflicts'))
            CREATE INDEX IX_EmassConflicts_TenantId ON dbo.EmassConflicts(TenantId);
        """;

    private const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS "EmassConflicts" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_EmassConflicts" PRIMARY KEY,
            "TenantId" TEXT NOT NULL,
            "RegisteredSystemId" TEXT NOT NULL,
            "SyncBatchId" TEXT NOT NULL,
            "EntityType" TEXT NOT NULL,
            "EntityId" TEXT NULL,
            "FieldName" TEXT NOT NULL,
            "SpinValue" TEXT NULL,
            "EmassValue" TEXT NULL,
            "ConflictStatus" TEXT NOT NULL,
            "DetectedAt" TEXT NOT NULL,
            "ResolvedAt" TEXT NULL,
            "ResolvedBy" TEXT NULL,
            "Notes" TEXT NULL,
            CONSTRAINT "FK_EmassConflicts_Tenants_TenantId"
                FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id"),
            CONSTRAINT "FK_EmassConflicts_RegisteredSystems_RegisteredSystemId"
                FOREIGN KEY ("RegisteredSystemId") REFERENCES "RegisteredSystems" ("Id")
                ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS "IX_EmassConflict_SystemId_Status"
            ON "EmassConflicts" ("RegisteredSystemId", "ConflictStatus");
        CREATE INDEX IF NOT EXISTS "IX_EmassConflict_BatchId"
            ON "EmassConflicts" ("SyncBatchId");
        CREATE INDEX IF NOT EXISTS "IX_EmassConflicts_TenantId"
            ON "EmassConflicts" ("TenantId");
        """;
}
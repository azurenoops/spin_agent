using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class OrganizationCatalogSchemaAdditions
{
    /// <summary>Creates catalog tables before the model-wide tenant-column retrofit.</summary>
    public static Task EnsureTablesAsync(AtoCopilotContext db, CancellationToken ct = default)
    {
        var sql = db.Database.IsSqlite() ? SqliteScript : db.Database.IsSqlServer() ? SqlServerTablesScript
            : throw new NotSupportedException($"Organization catalog schema does not support {db.Database.ProviderName}.");
        return db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        await EnsureTablesAsync(db, ct);
        if (db.Database.IsSqlite())
        {
            var hasCapabilities = await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='SecurityCapabilities'")
                .SingleAsync(ct);
            if (hasCapabilities != 0)
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_SecurityCapability_Tenant_Name
                        ON SecurityCapabilities(TenantId, Name);
                    DROP INDEX IF EXISTS IX_SecurityCapability_Name;
                    """, ct);
        }
        else if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlRawAsync(SqlServerCapabilityNameIndexScript, ct);
        logger.LogInformation("Verified organization catalog schema on {Provider}", db.Database.ProviderName);
    }

    public const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS OrganizationCatalogEntries (
            Id TEXT NOT NULL PRIMARY KEY,
            TenantId TEXT NOT NULL REFERENCES Tenants(Id) ON DELETE CASCADE,
            Source TEXT NOT NULL,
            RecordType TEXT NOT NULL,
            RecordId TEXT NOT NULL,
            OrganizationContribution TEXT NOT NULL,
            OrganizationOwner TEXT NOT NULL,
            SupportingComponentsJson TEXT NOT NULL,
            Revision INTEGER NOT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            CreatedBy TEXT NOT NULL,
            UpdatedBy TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_OrganizationCatalogEntries_TenantId_Source_RecordType_RecordId
            ON OrganizationCatalogEntries(TenantId, Source, RecordType, RecordId);
        CREATE TABLE IF NOT EXISTS OrganizationCatalogAdditions (
            Id TEXT NOT NULL PRIMARY KEY,
            TenantId TEXT NOT NULL REFERENCES Tenants(Id) ON DELETE CASCADE,
            IdempotencyKey TEXT NOT NULL,
            IntentJson TEXT NOT NULL,
            ResultJson TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            CreatedBy TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_OrganizationCatalogAdditions_TenantId_IdempotencyKey
            ON OrganizationCatalogAdditions(TenantId, IdempotencyKey);
        """;

    public const string SqlServerScript = SqlServerTablesScript + "\n" + SqlServerCapabilityNameIndexScript;

    private const string SqlServerCapabilityNameIndexScript = """
        IF OBJECT_ID(N'dbo.SecurityCapabilities', N'U') IS NOT NULL
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = N'IX_SecurityCapability_Tenant_Name'
                AND object_id = OBJECT_ID(N'dbo.SecurityCapabilities'))
                CREATE UNIQUE INDEX IX_SecurityCapability_Tenant_Name ON dbo.SecurityCapabilities(TenantId, Name);
            IF EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = N'IX_SecurityCapability_Name'
                AND object_id = OBJECT_ID(N'dbo.SecurityCapabilities'))
                DROP INDEX IX_SecurityCapability_Name ON dbo.SecurityCapabilities;
        END;
        """;

    private const string SqlServerTablesScript = """
        IF OBJECT_ID(N'dbo.OrganizationCatalogEntries', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.OrganizationCatalogEntries (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Tenants(Id) ON DELETE CASCADE,
                Source NVARCHAR(16) NOT NULL,
                RecordType NVARCHAR(16) NOT NULL,
                RecordId NVARCHAR(36) NOT NULL,
                OrganizationContribution NVARCHAR(2000) NOT NULL,
                OrganizationOwner NVARCHAR(200) NOT NULL,
                SupportingComponentsJson NVARCHAR(MAX) NOT NULL,
                Revision BIGINT NOT NULL,
                CreatedAt DATETIMEOFFSET NOT NULL,
                UpdatedAt DATETIMEOFFSET NOT NULL,
                CreatedBy NVARCHAR(200) NOT NULL,
                UpdatedBy NVARCHAR(200) NOT NULL
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'IX_OrganizationCatalogEntries_TenantId_Source_RecordType_RecordId'
            AND object_id = OBJECT_ID(N'dbo.OrganizationCatalogEntries'))
            CREATE UNIQUE INDEX IX_OrganizationCatalogEntries_TenantId_Source_RecordType_RecordId
                ON dbo.OrganizationCatalogEntries(TenantId, Source, RecordType, RecordId);
        IF OBJECT_ID(N'dbo.OrganizationCatalogAdditions', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.OrganizationCatalogAdditions (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Tenants(Id) ON DELETE CASCADE,
                IdempotencyKey NVARCHAR(100) NOT NULL,
                IntentJson NVARCHAR(MAX) NOT NULL,
                ResultJson NVARCHAR(MAX) NOT NULL,
                CreatedAt DATETIMEOFFSET NOT NULL,
                CreatedBy NVARCHAR(200) NOT NULL
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes
            WHERE name = N'IX_OrganizationCatalogAdditions_TenantId_IdempotencyKey'
            AND object_id = OBJECT_ID(N'dbo.OrganizationCatalogAdditions'))
            CREATE UNIQUE INDEX IX_OrganizationCatalogAdditions_TenantId_IdempotencyKey
                ON dbo.OrganizationCatalogAdditions(TenantId, IdempotencyKey);
        """;
}

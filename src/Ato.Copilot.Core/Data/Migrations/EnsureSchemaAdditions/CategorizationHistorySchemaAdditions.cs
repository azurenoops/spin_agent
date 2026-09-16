using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>
/// Idempotent additive schema for immutable categorization history.
/// </summary>
public static class CategorizationHistorySchemaAdditions
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
                logger.LogWarning(
                    "CategorizationHistorySchemaAdditions: skipping unsupported provider {Provider}",
                    providerName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "CategorizationHistorySchemaAdditions: DDL failed for provider {Provider}",
                providerName);
            throw new InvalidOperationException(
                $"Database schema initialization failed in CategorizationHistorySchemaAdditions for provider '{providerName}'.",
                ex);
        }
    }

    private const string SqlServerScript = """
        IF OBJECT_ID(N'dbo.CategorizationHistoryEntries', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.CategorizationHistoryEntries (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_CategorizationHistoryEntries PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL,
                RegisteredSystemId NVARCHAR(36) NOT NULL,
                Version INT NOT NULL,
                ChangedBy NVARCHAR(200) NOT NULL,
                ChangedAt DATETIME2 NOT NULL,
                Justification NVARCHAR(4000) NULL,
                PreviousConfidentialityImpact NVARCHAR(10) NULL,
                PreviousIntegrityImpact NVARCHAR(10) NULL,
                PreviousAvailabilityImpact NVARCHAR(10) NULL,
                PreviousOverallImpact NVARCHAR(10) NULL,
                NewConfidentialityImpact NVARCHAR(10) NOT NULL,
                NewIntegrityImpact NVARCHAR(10) NOT NULL,
                NewAvailabilityImpact NVARCHAR(10) NOT NULL,
                NewOverallImpact NVARCHAR(10) NOT NULL,
                PreviousInformationTypesJson NVARCHAR(MAX) NOT NULL,
                NewInformationTypesJson NVARCHAR(MAX) NOT NULL,
                CONSTRAINT FK_CategorizationHistoryEntries_Tenants_TenantId
                    FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id) ON DELETE CASCADE
            );
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = 'UX_CategorizationHistory_Tenant_System_Version'
              AND object_id = OBJECT_ID(N'dbo.CategorizationHistoryEntries'))
        BEGIN
            CREATE UNIQUE INDEX UX_CategorizationHistory_Tenant_System_Version
                ON dbo.CategorizationHistoryEntries (TenantId, RegisteredSystemId, Version);
        END;
        """;

    private const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS "CategorizationHistoryEntries" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_CategorizationHistoryEntries" PRIMARY KEY,
            "TenantId" TEXT NOT NULL,
            "RegisteredSystemId" TEXT NOT NULL,
            "Version" INTEGER NOT NULL,
            "ChangedBy" TEXT NOT NULL,
            "ChangedAt" TEXT NOT NULL,
            "Justification" TEXT NULL,
            "PreviousConfidentialityImpact" TEXT NULL,
            "PreviousIntegrityImpact" TEXT NULL,
            "PreviousAvailabilityImpact" TEXT NULL,
            "PreviousOverallImpact" TEXT NULL,
            "NewConfidentialityImpact" TEXT NOT NULL,
            "NewIntegrityImpact" TEXT NOT NULL,
            "NewAvailabilityImpact" TEXT NOT NULL,
            "NewOverallImpact" TEXT NOT NULL,
            "PreviousInformationTypesJson" TEXT NOT NULL,
            "NewInformationTypesJson" TEXT NOT NULL,
            CONSTRAINT "FK_CategorizationHistoryEntries_Tenants_TenantId"
                FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX IF NOT EXISTS "UX_CategorizationHistory_Tenant_System_Version"
            ON "CategorizationHistoryEntries" ("TenantId", "RegisteredSystemId", "Version");
        """;
}
using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Idempotent additive schema for Feature 069 control validation links.</summary>
public static class ControlValidationLinksSchemaAdditions
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
                    "ControlValidationLinksSchemaAdditions: skipping unsupported provider {Provider}",
                    providerName);
                return;
            }

            logger.LogInformation("Verified Feature 069 ControlValidationLinks schema on {Provider}", providerName);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "ControlValidationLinksSchemaAdditions: DDL failed for provider {Provider}",
                providerName);
            throw new InvalidOperationException(
                $"Database schema initialization failed in ControlValidationLinksSchemaAdditions for provider '{providerName}'.",
                exception);
        }
    }

    private const string SqlServerScript = """
        IF OBJECT_ID(N'dbo.ControlValidationLinks', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ControlValidationLinks (
                Id NVARCHAR(36) NOT NULL CONSTRAINT PK_ControlValidationLinks PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL,
                ControlImplementationId NVARCHAR(36) NOT NULL,
                LinkType NVARCHAR(30) NOT NULL,
                LinkTarget NVARCHAR(2048) NOT NULL,
                Description NVARCHAR(2000) NULL,
                AddedBy NVARCHAR(200) NOT NULL,
                AddedAt DATETIME2 NOT NULL CONSTRAINT DF_ControlValidationLinks_AddedAt DEFAULT(SYSUTCDATETIME()),
                ValidatedAt DATETIME2 NULL,
                IsAutomated BIT NOT NULL CONSTRAINT DF_ControlValidationLinks_IsAutomated DEFAULT(0),
                CONSTRAINT FK_ControlValidationLinks_Tenants_TenantId
                    FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT FK_ControlValidationLinks_ControlImplementations_ControlImplementationId
                    FOREIGN KEY (ControlImplementationId) REFERENCES dbo.ControlImplementations(Id)
                    ON DELETE CASCADE
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ControlValidationLink_Implementation_Target' AND object_id = OBJECT_ID(N'dbo.ControlValidationLinks'))
            CREATE UNIQUE INDEX IX_ControlValidationLink_Implementation_Target ON dbo.ControlValidationLinks(ControlImplementationId, LinkTarget);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ControlValidationLink_ImplementationId' AND object_id = OBJECT_ID(N'dbo.ControlValidationLinks'))
            CREATE INDEX IX_ControlValidationLink_ImplementationId ON dbo.ControlValidationLinks(ControlImplementationId);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ControlValidationLinks_TenantId' AND object_id = OBJECT_ID(N'dbo.ControlValidationLinks'))
            CREATE INDEX IX_ControlValidationLinks_TenantId ON dbo.ControlValidationLinks(TenantId);
        """;

    private const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS "ControlValidationLinks" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_ControlValidationLinks" PRIMARY KEY,
            "TenantId" TEXT NOT NULL,
            "ControlImplementationId" TEXT NOT NULL,
            "LinkType" TEXT NOT NULL,
            "LinkTarget" TEXT NOT NULL,
            "Description" TEXT NULL,
            "AddedBy" TEXT NOT NULL,
            "AddedAt" TEXT NOT NULL DEFAULT (datetime('now')),
            "ValidatedAt" TEXT NULL,
            "IsAutomated" INTEGER NOT NULL DEFAULT 0,
            CONSTRAINT "FK_ControlValidationLinks_Tenants_TenantId"
                FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id"),
            CONSTRAINT "FK_ControlValidationLinks_ControlImplementations_ControlImplementationId"
                FOREIGN KEY ("ControlImplementationId") REFERENCES "ControlImplementations" ("Id")
                ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ControlValidationLink_Implementation_Target"
            ON "ControlValidationLinks" ("ControlImplementationId", "LinkTarget");
        CREATE INDEX IF NOT EXISTS "IX_ControlValidationLink_ImplementationId"
            ON "ControlValidationLinks" ("ControlImplementationId");
        CREATE INDEX IF NOT EXISTS "IX_ControlValidationLinks_TenantId"
            ON "ControlValidationLinks" ("TenantId");
        """;
}
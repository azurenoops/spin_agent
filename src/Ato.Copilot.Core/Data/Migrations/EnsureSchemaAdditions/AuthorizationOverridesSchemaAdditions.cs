using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Idempotent additive schema for authorization override history.</summary>
public static class AuthorizationOverridesSchemaAdditions
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
                    "AuthorizationOverridesSchemaAdditions: skipping unsupported provider {Provider}",
                    providerName);
                return;
            }

            logger.LogInformation("Verified AuthorizationOverrides schema on {Provider}", providerName);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "AuthorizationOverridesSchemaAdditions: DDL failed for provider {Provider}",
                providerName);
            throw new InvalidOperationException(
                $"Database schema initialization failed in AuthorizationOverridesSchemaAdditions for provider '{providerName}'.",
                exception);
        }
    }

    private const string SqlServerScript = """
        IF OBJECT_ID(N'dbo.AuthorizationOverrides', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.AuthorizationOverrides (
                Id NVARCHAR(36) NOT NULL CONSTRAINT PK_AuthorizationOverrides PRIMARY KEY,
                TenantId UNIQUEIDENTIFIER NOT NULL,
                AuthorizationDecisionId NVARCHAR(36) NOT NULL,
                OverrideStatus NVARCHAR(30) NOT NULL,
                AppliedBy NVARCHAR(200) NOT NULL,
                AppliedByName NVARCHAR(200) NOT NULL,
                AppliedAt DATETIME2 NOT NULL,
                Justification NVARCHAR(4000) NOT NULL,
                ExpirationDate DATETIME2 NOT NULL,
                CONSTRAINT FK_AuthorizationOverrides_Tenants_TenantId
                    FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT FK_AuthorizationOverrides_AuthorizationDecisions_AuthorizationDecisionId
                    FOREIGN KEY (AuthorizationDecisionId) REFERENCES dbo.AuthorizationDecisions(Id)
                    ON DELETE CASCADE
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuthorizationOverride_DecisionId_ExpirationDate' AND object_id = OBJECT_ID(N'dbo.AuthorizationOverrides'))
            CREATE INDEX IX_AuthorizationOverride_DecisionId_ExpirationDate ON dbo.AuthorizationOverrides(AuthorizationDecisionId, ExpirationDate);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuthorizationOverrides_TenantId' AND object_id = OBJECT_ID(N'dbo.AuthorizationOverrides'))
            CREATE INDEX IX_AuthorizationOverrides_TenantId ON dbo.AuthorizationOverrides(TenantId);
        """;

    private const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS "AuthorizationOverrides" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_AuthorizationOverrides" PRIMARY KEY,
            "TenantId" TEXT NOT NULL,
            "AuthorizationDecisionId" TEXT NOT NULL,
            "OverrideStatus" TEXT NOT NULL,
            "AppliedBy" TEXT NOT NULL,
            "AppliedByName" TEXT NOT NULL,
            "AppliedAt" TEXT NOT NULL,
            "Justification" TEXT NOT NULL,
            "ExpirationDate" TEXT NOT NULL,
            CONSTRAINT "FK_AuthorizationOverrides_Tenants_TenantId"
                FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id"),
            CONSTRAINT "FK_AuthorizationOverrides_AuthorizationDecisions_AuthorizationDecisionId"
                FOREIGN KEY ("AuthorizationDecisionId") REFERENCES "AuthorizationDecisions" ("Id")
                ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS "IX_AuthorizationOverride_DecisionId_ExpirationDate"
            ON "AuthorizationOverrides" ("AuthorizationDecisionId", "ExpirationDate");
        CREATE INDEX IF NOT EXISTS "IX_AuthorizationOverrides_TenantId"
            ON "AuthorizationOverrides" ("TenantId");
        """;
}
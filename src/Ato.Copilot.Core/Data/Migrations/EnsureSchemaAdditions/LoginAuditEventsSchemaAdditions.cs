using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>
/// Feature 051 (FR-032 / FR-033 / FR-034 / FR-036a): idempotent additive SQL
/// for the <c>LoginAuditEvents</c> table and its three indexes
/// (per-tenant read, daily archive scan, forensic Oid lookup).
/// Called from <c>Program.cs</c>'s <c>EnsureSchemaAdditionsAsync</c> after
/// the Feature 050 <c>CapabilityHistoryEventsSchemaAdditions</c> call. Safe
/// to run repeatedly.
/// </summary>
/// <remarks>
/// <para>
/// Why an EnsureSchemaAdditions module and not an EF Core migration: the
/// repository's EF model snapshot has drifted across Features 045–050.
/// A <c>dotnet ef migrations add</c> for this feature would emit dozens
/// of unrelated table mutations. Following the established Feature 050
/// pattern keeps the blast radius to exactly the new table.
/// </para>
/// <para>
/// FK configuration matches <c>OnModelCreating</c>:
/// <c>EffectiveTenantId</c> carries an <c>ON DELETE CASCADE</c> FK to
/// <c>Tenants</c> so tenant offboarding sweeps hot rows. The cold archive
/// (FR-036a) persists independently of cascade.
/// </para>
/// </remarks>
public static class LoginAuditEventsSchemaAdditions
{
    /// <summary>
    /// Creates the <c>LoginAuditEvents</c> table and its three indexes if
    /// they do not already exist. No-op when the table is already present.
    /// </summary>
    public static async Task ApplyAsync(
        AtoCopilotContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        var providerName = db.Database.ProviderName ?? string.Empty;
        var isSqlServer = providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
        var isSqlite = providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isSqlServer)
            {
                await db.Database.ExecuteSqlRawAsync(SqlServerScript, cancellationToken);
            }
            else if (isSqlite)
            {
                await db.Database.ExecuteSqlRawAsync(SqliteScript, cancellationToken);
            }
            else
            {
                logger.LogWarning(
                    "LoginAuditEventsSchemaAdditions: skipping (unsupported provider {Provider})",
                    providerName);
                return;
            }

            logger.LogInformation(
                "Verified Feature 051 LoginAuditEvents schema on {Provider}",
                providerName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "LoginAuditEventsSchemaAdditions encountered an error — "
                + "non-fatal if table already exists");
        }
    }

    private const string SqlServerScript = """
        IF OBJECT_ID(N'dbo.LoginAuditEvents', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.LoginAuditEvents (
                Id UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT PK_LoginAuditEvents PRIMARY KEY,
                EventType NVARCHAR(32) NOT NULL,
                Oid NVARCHAR(254) NULL,
                Tid NVARCHAR(254) NULL,
                EffectiveTenantId UNIQUEIDENTIFIER NOT NULL,
                CorrelationId NVARCHAR(64) NOT NULL,
                SourceIp NVARCHAR(45) NOT NULL,
                UserAgent NVARCHAR(512) NOT NULL,
                Surface NVARCHAR(16) NOT NULL,
                OccurredAt DATETIMEOFFSET NOT NULL
                    CONSTRAINT DF_LoginAuditEvents_OccurredAt DEFAULT(SYSUTCDATETIME()),
                ErrorClass NVARCHAR(32) NULL,
                MetadataJson NVARCHAR(2000) NULL,
                CONSTRAINT FK_LoginAuditEvents_Tenants_EffectiveTenantId
                    FOREIGN KEY (EffectiveTenantId) REFERENCES dbo.Tenants(Id)
                    ON DELETE CASCADE
            );
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = 'IX_LoginAuditEvents_Tenant_Occurred'
              AND object_id = OBJECT_ID(N'dbo.LoginAuditEvents'))
        BEGIN
            CREATE INDEX IX_LoginAuditEvents_Tenant_Occurred
                ON dbo.LoginAuditEvents (EffectiveTenantId ASC, OccurredAt DESC);
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = 'IX_LoginAuditEvents_Occurred'
              AND object_id = OBJECT_ID(N'dbo.LoginAuditEvents'))
        BEGIN
            CREATE INDEX IX_LoginAuditEvents_Occurred
                ON dbo.LoginAuditEvents (OccurredAt);
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = 'IX_LoginAuditEvents_Oid'
              AND object_id = OBJECT_ID(N'dbo.LoginAuditEvents'))
        BEGIN
            CREATE INDEX IX_LoginAuditEvents_Oid
                ON dbo.LoginAuditEvents (Oid ASC, OccurredAt DESC)
                WHERE Oid IS NOT NULL;
        END;
        """;

    private const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS "LoginAuditEvents" (
            "Id" TEXT NOT NULL
                CONSTRAINT "PK_LoginAuditEvents" PRIMARY KEY,
            "EventType" TEXT NOT NULL,
            "Oid" TEXT NULL,
            "Tid" TEXT NULL,
            "EffectiveTenantId" TEXT NOT NULL,
            "CorrelationId" TEXT NOT NULL,
            "SourceIp" TEXT NOT NULL,
            "UserAgent" TEXT NOT NULL,
            "Surface" TEXT NOT NULL,
            "OccurredAt" TEXT NOT NULL DEFAULT (datetime('now')),
            "ErrorClass" TEXT NULL,
            "MetadataJson" TEXT NULL,
            CONSTRAINT "FK_LoginAuditEvents_Tenants_EffectiveTenantId"
                FOREIGN KEY ("EffectiveTenantId") REFERENCES "Tenants" ("Id")
                ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS "IX_LoginAuditEvents_Tenant_Occurred"
            ON "LoginAuditEvents" ("EffectiveTenantId" ASC, "OccurredAt" DESC);

        CREATE INDEX IF NOT EXISTS "IX_LoginAuditEvents_Occurred"
            ON "LoginAuditEvents" ("OccurredAt");

        CREATE INDEX IF NOT EXISTS "IX_LoginAuditEvents_Oid"
            ON "LoginAuditEvents" ("Oid" ASC, "OccurredAt" DESC);
        """;
}

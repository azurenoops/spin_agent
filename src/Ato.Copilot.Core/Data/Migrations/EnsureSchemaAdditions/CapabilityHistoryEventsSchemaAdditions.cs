using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>
/// Feature 050 (FR-004 / FR-014 / FR-015): idempotent additive SQL for the
/// <c>CapabilityHistoryEvents</c> table and its primary read index.
/// Called from <c>Program.cs</c>'s <c>EnsureSchemaAdditionsAsync</c> after
/// <c>EnsureCreatedAsync</c> / <c>MigrateAsync</c>. Safe to run repeatedly.
/// </summary>
/// <remarks>
/// <para>
/// Why an EnsureSchemaAdditions module and not an EF Core migration: the
/// repository's EF model snapshot has drifted across Features 045–049
/// (those features ship via <c>EnsureCreatedAsync</c> + targeted additions
/// rather than full migrations). A <c>dotnet ef migrations add</c> for this
/// feature would emit dozens of unrelated table mutations as a side effect.
/// Following the established pattern keeps the blast radius to exactly the
/// new table.
/// </para>
/// <para>
/// The asymmetric foreign-key configuration in <c>OnModelCreating</c>
/// (NoAction → CspInheritedCapabilities, Cascade → Tenants) is mirrored
/// here: the <c>CapabilityId</c> column has no DB-level FK (intentional —
/// history outlives capability per FR-015), while the <c>TenantId</c>
/// column carries an <c>ON DELETE CASCADE</c> FK to <c>Tenants</c>.
/// </para>
/// </remarks>
public static class CapabilityHistoryEventsSchemaAdditions
{
    /// <summary>
    /// Creates the <c>CapabilityHistoryEvents</c> table and its primary
    /// composite index if they do not already exist. No-op when the table
    /// is already present.
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
                    "CapabilityHistoryEventsSchemaAdditions: skipping (unsupported provider {Provider})",
                    providerName);
                return;
            }

            logger.LogInformation(
                "Verified Feature 050 CapabilityHistoryEvents schema on {Provider}",
                providerName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "CapabilityHistoryEventsSchemaAdditions encountered an error — "
                + "non-fatal if table already exists");
        }
    }

    private const string SqlServerScript = """
        IF OBJECT_ID(N'dbo.CapabilityHistoryEvents', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.CapabilityHistoryEvents (
                Id UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT PK_CapabilityHistoryEvents PRIMARY KEY,
                CapabilityId UNIQUEIDENTIFIER NOT NULL,
                TenantId UNIQUEIDENTIFIER NOT NULL,
                EventType NVARCHAR(32) NOT NULL,
                ActorOid NVARCHAR(254) NOT NULL,
                OccurredAt DATETIMEOFFSET NOT NULL
                    CONSTRAINT DF_CapabilityHistoryEvents_OccurredAt DEFAULT(SYSUTCDATETIME()),
                Summary NVARCHAR(500) NOT NULL,
                MetadataJson NVARCHAR(2000) NULL,
                CONSTRAINT FK_CapabilityHistoryEvents_Tenants_TenantId
                    FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                    ON DELETE CASCADE
            );
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE name = 'IX_CapabilityHistoryEvents_Tenant_Capability_Occurred'
              AND object_id = OBJECT_ID(N'dbo.CapabilityHistoryEvents'))
        BEGIN
            CREATE INDEX IX_CapabilityHistoryEvents_Tenant_Capability_Occurred
                ON dbo.CapabilityHistoryEvents (TenantId ASC, CapabilityId ASC, OccurredAt DESC);
        END;
        """;

    private const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS "CapabilityHistoryEvents" (
            "Id" TEXT NOT NULL
                CONSTRAINT "PK_CapabilityHistoryEvents" PRIMARY KEY,
            "CapabilityId" TEXT NOT NULL,
            "TenantId" TEXT NOT NULL,
            "EventType" TEXT NOT NULL,
            "ActorOid" TEXT NOT NULL,
            "OccurredAt" TEXT NOT NULL DEFAULT (datetime('now')),
            "Summary" TEXT NOT NULL,
            "MetadataJson" TEXT NULL,
            CONSTRAINT "FK_CapabilityHistoryEvents_Tenants_TenantId"
                FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id")
                ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS "IX_CapabilityHistoryEvents_Tenant_Capability_Occurred"
            ON "CapabilityHistoryEvents" ("TenantId" ASC, "CapabilityId" ASC, "OccurredAt" DESC);
        """;
}

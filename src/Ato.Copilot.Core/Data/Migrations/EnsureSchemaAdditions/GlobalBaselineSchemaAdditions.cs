using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>
/// Feature 048 (T134, FR-081/FR-082): idempotent additive SQL migration for
/// the <c>GlobalBaselines</c> table. Called from <c>Program.cs</c>'s
/// <c>EnsureSchemaAdditionsAsync</c> after <c>EnsureCreatedAsync</c>. Safe to
/// run repeatedly. Detects the active EF Core provider and emits dialect-aware
/// SQL.
/// </summary>
public static class GlobalBaselineSchemaAdditions
{
    /// <summary>
    /// Creates the <c>GlobalBaselines</c> table and its indexes if they do not
    /// already exist. No-op when the table exists.
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
                    "GlobalBaselineSchemaAdditions: skipping (unsupported provider {Provider})",
                    providerName);
                return;
            }

            logger.LogInformation(
                "Verified Feature 048 GlobalBaselines schema on {Provider}",
                providerName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "GlobalBaselineSchemaAdditions encountered an error — non-fatal if table already exists");
        }
    }

    private const string SqlServerScript = """
        IF OBJECT_ID(N'dbo.GlobalBaselines', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.GlobalBaselines (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_GlobalBaselines PRIMARY KEY,
                Kind NVARCHAR(64) NOT NULL,
                SourceId UNIQUEIDENTIFIER NOT NULL,
                SourceTenantId UNIQUEIDENTIFIER NOT NULL,
                Title NVARCHAR(300) NULL,
                Notes NVARCHAR(4000) NULL,
                PublishedAt DATETIMEOFFSET NOT NULL CONSTRAINT DF_GlobalBaselines_PublishedAt DEFAULT(SYSUTCDATETIME()),
                PublishedBy NVARCHAR(200) NOT NULL,
                UnpublishedAt DATETIMEOFFSET NULL,
                UnpublishedBy NVARCHAR(200) NULL
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GlobalBaselines_Kind' AND object_id = OBJECT_ID(N'dbo.GlobalBaselines'))
        BEGIN
            CREATE INDEX IX_GlobalBaselines_Kind ON dbo.GlobalBaselines(Kind);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GlobalBaselines_SourceTenantId' AND object_id = OBJECT_ID(N'dbo.GlobalBaselines'))
        BEGIN
            CREATE INDEX IX_GlobalBaselines_SourceTenantId ON dbo.GlobalBaselines(SourceTenantId);
        END;
        """;

    private const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS "GlobalBaselines" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_GlobalBaselines" PRIMARY KEY,
            "Kind" TEXT NOT NULL,
            "SourceId" TEXT NOT NULL,
            "SourceTenantId" TEXT NOT NULL,
            "Title" TEXT NULL,
            "Notes" TEXT NULL,
            "PublishedAt" TEXT NOT NULL DEFAULT (datetime('now')),
            "PublishedBy" TEXT NOT NULL,
            "UnpublishedAt" TEXT NULL,
            "UnpublishedBy" TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS "IX_GlobalBaselines_Kind" ON "GlobalBaselines"("Kind");
        CREATE INDEX IF NOT EXISTS "IX_GlobalBaselines_SourceTenantId" ON "GlobalBaselines"("SourceTenantId");
        """;
}

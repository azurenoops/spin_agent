using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Idempotent support-session persistence. No stateless tokens are implicitly enrolled.</summary>
public static class TenantSupportSessionSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        var sql = db.Database.IsSqlite() ? SqliteScript : db.Database.IsSqlServer() ? SqlServerScript
            : throw new NotSupportedException($"Support-session schema does not support {db.Database.ProviderName}.");
        await db.Database.ExecuteSqlRawAsync(sql, ct);
        logger.LogInformation("Verified durable support-session schema on {Provider}", db.Database.ProviderName);
    }

    public const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS TenantSupportSessions (
            Id TEXT NOT NULL PRIMARY KEY,
            DirectoryTenantId TEXT NOT NULL,
            ObjectId TEXT NOT NULL,
            TargetTenantId TEXT NOT NULL REFERENCES Tenants(Id) ON DELETE CASCADE,
            IssuedAt TEXT NOT NULL,
            ExpiresAt TEXT NOT NULL,
            RevokedAt TEXT NULL,
            RevocationReason TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_TenantSupportSessions_DirectoryTenantId_ObjectId_ExpiresAt
            ON TenantSupportSessions(DirectoryTenantId, ObjectId, ExpiresAt);
        """;

    public const string SqlServerScript = """
        IF OBJECT_ID(N'dbo.TenantSupportSessions', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.TenantSupportSessions (
                Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                DirectoryTenantId UNIQUEIDENTIFIER NOT NULL,
                ObjectId UNIQUEIDENTIFIER NOT NULL,
                TargetTenantId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Tenants(Id) ON DELETE CASCADE,
                IssuedAt DATETIMEOFFSET NOT NULL,
                ExpiresAt DATETIMEOFFSET NOT NULL,
                RevokedAt DATETIMEOFFSET NULL,
                RevocationReason NVARCHAR(64) NULL
            );
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes
                       WHERE name = N'IX_TenantSupportSessions_DirectoryTenantId_ObjectId_ExpiresAt'
                         AND object_id = OBJECT_ID(N'dbo.TenantSupportSessions'))
            CREATE INDEX IX_TenantSupportSessions_DirectoryTenantId_ObjectId_ExpiresAt
                ON dbo.TenantSupportSessions(DirectoryTenantId, ObjectId, ExpiresAt);
        """;
}

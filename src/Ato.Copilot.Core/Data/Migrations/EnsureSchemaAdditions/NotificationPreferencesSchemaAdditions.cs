using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class NotificationPreferencesSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        var script = db.Database.IsSqlite() ? SqliteScript
            : db.Database.IsSqlServer() ? SqlServerScript
            : throw new NotSupportedException($"Notification preferences schema does not support {db.Database.ProviderName}.");
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync(script, ct);
            await transaction.CommitAsync(ct);
        });
        logger.LogInformation("Verified organization-scoped notification preferences index on {Provider}", db.Database.ProviderName);
    }

    public const string SqliteScript = """
        CREATE UNIQUE INDEX IF NOT EXISTS IX_NotificationPreferences_TenantId_UserId
            ON NotificationPreferences(TenantId, UserId);
        DROP INDEX IF EXISTS IX_NotificationPreferences_UserId;
        """;

    public const string SqlServerScript = """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes
                       WHERE name = N'IX_NotificationPreferences_TenantId_UserId'
                       AND object_id = OBJECT_ID(N'dbo.NotificationPreferences'))
            CREATE UNIQUE INDEX IX_NotificationPreferences_TenantId_UserId
                ON dbo.NotificationPreferences(TenantId, UserId);
        IF EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_NotificationPreferences_UserId'
                   AND object_id = OBJECT_ID(N'dbo.NotificationPreferences'))
            DROP INDEX IX_NotificationPreferences_UserId ON dbo.NotificationPreferences;
        """;
}

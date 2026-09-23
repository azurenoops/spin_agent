using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Chat.Data;

/// <summary>Adds nullable scope columns without assigning any legacy row to an owner.</summary>
public static class ChatWorkspaceSchema
{
    public static async Task EnsureAsync(ChatDbContext db, CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('Conversations', 'OwnerKey') IS NULL
                    ALTER TABLE Conversations ADD OwnerKey nvarchar(100) NULL;
                IF COL_LENGTH('Conversations', 'SystemId') IS NULL
                    ALTER TABLE Conversations ADD SystemId nvarchar(450) NULL;
                """, ct);
            return;
        }
        if (!db.Database.IsSqlite())
            throw new InvalidOperationException("Unsupported Chat database provider for workspace schema initialization.");
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA table_info('Conversations')";
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
            }
            if (!columns.Contains("OwnerKey"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Conversations ADD COLUMN OwnerKey TEXT NULL", ct);
            if (!columns.Contains("SystemId"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Conversations ADD COLUMN SystemId TEXT NULL", ct);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}

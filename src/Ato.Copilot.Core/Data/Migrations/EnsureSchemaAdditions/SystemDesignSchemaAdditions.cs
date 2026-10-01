using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Additive governed design stores. TenantScoped metadata enrolls all three in existing query filters and RLS.</summary>
public static class SystemDesignSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite() && !db.Database.IsSqlServer())
            throw new NotSupportedException("System design requires SQLite or SQL Server.");
        foreach (var script in Scripts(db.Database.IsSqlServer()))
            await db.Database.ExecuteSqlRawAsync(script, ct);
        logger.LogInformation("Verified additive System Design schema for {Provider}", db.Database.ProviderName);
    }
    public static IReadOnlyList<string> Scripts(bool sqlServer)
    {
        var guid = sqlServer ? "uniqueidentifier" : "TEXT";
        var text = sqlServer ? "nvarchar(max)" : "TEXT";
        var number = sqlServer ? "bigint" : "INTEGER";
        var date = sqlServer ? "datetimeoffset" : "TEXT";
        string Short(int size) => sqlServer ? $"nvarchar({size})" : "TEXT";
        var common = $"Id {guid} NOT NULL PRIMARY KEY, TenantId {guid} NOT NULL, SystemId {Short(36)} NOT NULL";
        var tables = new Dictionary<string, string>
        {
            ["SystemDesignWorkspaces"] = $"{common}, Revision {number} NOT NULL, ApprovedRevision {number} NULL, GraphJson {text} NOT NULL, SubmittedBy {Short(36)} NULL, UNIQUE(TenantId,SystemId)",
            ["SystemDesignRevisions"] = $"{common}, Revision {number} NOT NULL, Action {Short(32)} NOT NULL, Actor {Short(36)} NOT NULL, At {date} NOT NULL, Reason {Short(2000)} NOT NULL, GovernanceStatus {Short(32)} NOT NULL, SourceFingerprint {Short(64)} NOT NULL, SnapshotHash {Short(64)} NOT NULL, GraphJson {text} NOT NULL, UNIQUE(TenantId,SystemId,Revision)",
            ["SystemDesignLayouts"] = $"{common}, [View] {Short(16)} NOT NULL, Version {number} NOT NULL, LayoutJson {text} NOT NULL, UNIQUE(TenantId,SystemId,[View])"
        };
        return tables.Select(x => sqlServer
            ? $"IF OBJECT_ID(N'dbo.{x.Key}', N'U') IS NULL CREATE TABLE dbo.[{x.Key}] ({x.Value});"
            : $"CREATE TABLE IF NOT EXISTS [{x.Key}] ({x.Value});").ToArray();
    }
}

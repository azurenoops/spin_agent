using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Creates catalog tables omitted from the historical SQLite migration baseline.</summary>
public static class CspInheritedCatalogSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite() && !db.Database.IsSqlServer())
            throw new NotSupportedException("CSP catalog persistence requires SQLite or SQL Server.");

        foreach (var script in Scripts(db.Database.IsSqlServer()))
            await db.Database.ExecuteSqlRawAsync(script, ct);

        logger.LogInformation("Verified additive CSP inherited catalog schema for {Provider}", db.Database.ProviderName);
    }

    public static IReadOnlyList<string> Scripts(bool sqlServer)
    {
        var guid = sqlServer ? "uniqueidentifier" : "TEXT";
        var text = sqlServer ? "nvarchar(max)" : "TEXT";
        var date = sqlServer ? "datetimeoffset" : "TEXT";
        var integer = sqlServer ? "int" : "INTEGER";
        var number = sqlServer ? "float" : "REAL";
        var rowVersion = sqlServer ? "rowversion" : "BLOB";
        var deletion = sqlServer ? "NO ACTION" : "RESTRICT";
        var prefix = sqlServer ? "dbo." : "";
        string Short(int length) => sqlServer ? $"nvarchar({length})" : "TEXT";

        var tables = new Dictionary<string, string>
        {
            ["CspInheritedComponents"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, CspProfileId {guid} NOT NULL,
                Name {Short(256)} NOT NULL, Description {Short(2000)} NOT NULL,
                ComponentType {integer} NOT NULL, SourceFileName {Short(512)} NULL,
                SourceFormat {integer} NOT NULL, SourceArtifactReference {Short(2048)} NULL,
                Status {integer} NOT NULL, ImportedAt {date} NOT NULL,
                ImportedBy {Short(254)} NOT NULL, UpdatedAt {date} NULL,
                UpdatedBy {Short(254)} NULL, RowVersion {rowVersion} NULL
                """,
            ["CspInheritedCapabilities"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, CspInheritedComponentId {guid} NOT NULL,
                Name {Short(256)} NOT NULL, Description {Short(2000)} NOT NULL,
                MappedNistControlIds {text} NOT NULL, MappingConfidence {number} NULL,
                Status {integer} NOT NULL, MappingFailureReason {Short(500)} NULL,
                MappedBy {integer} NOT NULL, CreatedAt {date} NOT NULL,
                CreatedBy {Short(254)} NOT NULL, ReviewedAt {date} NULL,
                ReviewedBy {Short(254)} NULL, ReviewerNote {Short(2000)} NULL,
                RowVersion {rowVersion} NULL,
                CONSTRAINT FK_CspInheritedCapabilities_CspInheritedComponents_CspInheritedComponentId
                    FOREIGN KEY(CspInheritedComponentId) REFERENCES {prefix}CspInheritedComponents(Id)
                    ON DELETE {deletion}
                """
        };

        var scripts = tables.Select(table => sqlServer
            ? $"IF OBJECT_ID(N'dbo.{table.Key}', N'U') IS NULL CREATE TABLE dbo.{table.Key} ({table.Value});"
            : $"CREATE TABLE IF NOT EXISTS {table.Key} ({table.Value});").ToList();
        foreach (var (table, name, columns) in new[]
        {
            ("CspInheritedComponents", "IX_CspInheritedComponents_CspProfileId_Status", "CspProfileId,Status"),
            ("CspInheritedCapabilities", "IX_CspInheritedCapabilities_ComponentId_Status", "CspInheritedComponentId,Status")
        })
        {
            scripts.Add(sqlServer
                ? $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'{name}' AND object_id=OBJECT_ID(N'dbo.{table}')) CREATE INDEX {name} ON dbo.{table}({columns});"
                : $"CREATE INDEX IF NOT EXISTS {name} ON {table}({columns});");
        }
        return scripts;
    }
}

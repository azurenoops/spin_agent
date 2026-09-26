using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Additive private package ledger; never backfills publication or resets existing data.</summary>
public static class CspPackageSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite() && !db.Database.IsSqlServer())
            throw new NotSupportedException("Package persistence requires SQLite or SQL Server.");
        var sqlServer = db.Database.IsSqlServer();
        foreach (var script in Scripts(sqlServer))
            await db.Database.ExecuteSqlRawAsync(EscapeSqlFormat(script), ct);
        if (!sqlServer)
        {
            var columns = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM pragma_table_info('CspPackages')").ToListAsync(ct);
            if (!columns.Contains("AnalysisCheckpointJson", StringComparer.OrdinalIgnoreCase))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackages ADD COLUMN AnalysisCheckpointJson TEXT NULL", ct);
            if (!columns.Contains("OfferingId", StringComparer.OrdinalIgnoreCase))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackages ADD COLUMN OfferingId TEXT NULL", ct);
            if (!columns.Contains("PackageVersionId", StringComparer.OrdinalIgnoreCase))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackages ADD COLUMN PackageVersionId TEXT NULL", ct);
            if (!columns.Contains("BoundaryRevisionId", StringComparer.OrdinalIgnoreCase))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackages ADD COLUMN BoundaryRevisionId TEXT NULL", ct);
            var approvalColumns = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM pragma_table_info('CspPackageApprovals')").ToListAsync(ct);
            if (!approvalColumns.Contains("CreatedVersion", StringComparer.OrdinalIgnoreCase))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackageApprovals ADD COLUMN CreatedVersion INTEGER NOT NULL DEFAULT 0", ct);
            if (!approvalColumns.Contains("PublishedVersion", StringComparer.OrdinalIgnoreCase))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackageApprovals ADD COLUMN PublishedVersion INTEGER NULL", ct);
            foreach (var (table, additions) in ProfileColumns(false))
            {
                var columnQuery = $"SELECT name AS Value FROM pragma_table_info('{table}')";
                var existing = await db.Database.SqlQueryRaw<string>(columnQuery).ToListAsync(ct);
                foreach (var (column, definition) in additions)
                {
                    if (existing.Contains(column, StringComparer.OrdinalIgnoreCase)) continue;
                    var alter = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
                    await db.Database.ExecuteSqlRawAsync(EscapeSqlFormat(alter), ct);
                }
            }
        }
        logger.LogInformation("Verified additive private CSP package schema for {Provider}", db.Database.ProviderName);
    }

    // ExecuteSqlRaw formats even parameterless commands; JSON defaults contain literal braces.
    private static string EscapeSqlFormat(string sql) => sql.Replace("{", "{{").Replace("}", "}}");

    private static Dictionary<string, Dictionary<string, string>> ProfileColumns(bool sqlServer)
    {
        var integer = sqlServer ? "int" : "INTEGER";
        var text = sqlServer ? "nvarchar(max)" : "TEXT";
        return new()
        {
            ["CspPackages"] = new()
            {
                ["AnalysisProfileVersion"] = $"{integer} NOT NULL DEFAULT 1",
                ["TargetAnalysisProfileVersion"] = $"{integer} NULL",
                ["FamilyCoverageJson"] = $"{text} NOT NULL DEFAULT '{{}}'",
                ["AnalysisOperationsJson"] = $"{text} NOT NULL DEFAULT '[]'"
            },
            ["CspPackageEntries"] = new()
            {
                ["AnalysisProfileVersion"] = $"{integer} NOT NULL DEFAULT 1",
                ["FamilyCoverageJson"] = $"{text} NOT NULL DEFAULT '[]'"
            },
            ["CspPackageCandidates"] = new()
            {
                ["ClaimJson"] = $"{text} NULL",
                ["AnalysisProfileVersion"] = $"{integer} NOT NULL DEFAULT 1"
            }
        };
    }

    public static IReadOnlyList<string> Scripts(bool sqlServer)
    {
        var guid = sqlServer ? "uniqueidentifier" : "TEXT";
        var text = sqlServer ? "nvarchar(max)" : "TEXT";
        var date = sqlServer ? "datetimeoffset" : "TEXT";
        var integer = sqlServer ? "bigint" : "INTEGER";
        var boolean = sqlServer ? "bit" : "INTEGER";
        string Short(int length) => sqlServer ? $"nvarchar({length})" : "TEXT";
        var tables = new Dictionary<string, string>
        {
            ["CspPackages"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL,
                IdempotencyKey {Short(100)} NOT NULL, ContentHash {Short(64)} NOT NULL, Name {Short(256)} NOT NULL,
                ProcessingState {Short(32)} NOT NULL, PublicationState {Short(32)} NOT NULL,
                Revision {integer} NOT NULL, Version {integer} NOT NULL, LeaseId {guid} NULL,
                LeaseExpiresTicks {integer} NOT NULL, LastError {text} NULL, RetryKeysJson {text} NOT NULL,
                AnalysisCheckpointJson {text} NULL,
                OfferingId {guid} NULL, PackageVersionId {guid} NULL, BoundaryRevisionId {guid} NULL,
                CreatedAt {date} NOT NULL, UpdatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL
                """,
            ["CspPackageEntries"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, PackageId {guid} NOT NULL, StableKey {Short(64)} NOT NULL,
                IsOriginal {boolean} NOT NULL, OriginalEntryId {guid} NULL, FileName {Short(512)} NOT NULL,
                ArchivePath {text} NOT NULL, MediaType {Short(256)} NOT NULL, StorageKey {text} NULL,
                ByteLength {integer} NOT NULL, Sha256 {Short(64)} NOT NULL, Status {Short(32)} NOT NULL,
                Reason {text} NULL, ExclusionReason {text} NULL, Revision {integer} NOT NULL, SegmentsJson {text} NOT NULL,
                FOREIGN KEY(PackageId) REFERENCES CspPackages(Id)
                """,
            ["CspPackageCandidates"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, PackageId {guid} NOT NULL, StableKey {Short(64)} NOT NULL,
                Type {Short(32)} NOT NULL, ReviewState {Short(32)} NOT NULL, Revision {integer} NOT NULL,
                PayloadJson {text} NOT NULL, UnresolvedDependenciesJson {text} NOT NULL, ReviewedBy {text} NULL,
                ReviewedAt {date} NULL, FOREIGN KEY(PackageId) REFERENCES CspPackages(Id)
                """,
            ["CspPackageApprovals"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, PackageId {guid} NOT NULL, Revision {integer} NOT NULL,
                CreatedVersion {integer} NOT NULL, PublishedVersion {integer} NULL,
                PreviewHash {Short(64)} NOT NULL, SelectionJson {text} NOT NULL, SnapshotJson {text} NOT NULL, BlockersJson {text} NOT NULL,
                State {Short(32)} NOT NULL, ApprovedBy {text} NULL, ApprovedAt {date} NULL,
                ExpiresAt {date} NOT NULL, PublicationKey {Short(100)} NULL, PublicationJson {text} NULL,
                FOREIGN KEY(PackageId) REFERENCES CspPackages(Id)
                """,
            ["CspPackageAudits"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, PackageId {guid} NOT NULL, Action {Short(100)} NOT NULL,
                Actor {Short(254)} NOT NULL, Revision {integer} NOT NULL, Detail {text} NOT NULL,
                OccurredAt {date} NOT NULL, FOREIGN KEY(PackageId) REFERENCES CspPackages(Id)
                """
        };
        var profileColumns = ProfileColumns(sqlServer);
        foreach (var (table, columns) in profileColumns)
            tables[table] = string.Join(", ", columns.Select(column => $"{column.Key} {column.Value}")) + ", " + tables[table];
        var scripts = tables.Select(table => sqlServer
            ? $"IF OBJECT_ID(N'dbo.{table.Key}', N'U') IS NULL CREATE TABLE dbo.{table.Key} ({table.Value});"
            : $"CREATE TABLE IF NOT EXISTS {table.Key} ({table.Value});").ToList();
        if (sqlServer)
        {
            scripts.Add("IF COL_LENGTH(N'dbo.CspPackages', N'AnalysisCheckpointJson') IS NULL ALTER TABLE dbo.CspPackages ADD AnalysisCheckpointJson nvarchar(max) NULL;");
            scripts.Add("IF COL_LENGTH(N'dbo.CspPackages', N'OfferingId') IS NULL ALTER TABLE dbo.CspPackages ADD OfferingId uniqueidentifier NULL;");
            scripts.Add("IF COL_LENGTH(N'dbo.CspPackages', N'PackageVersionId') IS NULL ALTER TABLE dbo.CspPackages ADD PackageVersionId uniqueidentifier NULL;");
            scripts.Add("IF COL_LENGTH(N'dbo.CspPackages', N'BoundaryRevisionId') IS NULL ALTER TABLE dbo.CspPackages ADD BoundaryRevisionId uniqueidentifier NULL;");
            scripts.Add("IF COL_LENGTH(N'dbo.CspPackageApprovals', N'CreatedVersion') IS NULL ALTER TABLE dbo.CspPackageApprovals ADD CreatedVersion bigint NOT NULL DEFAULT 0;");
            scripts.Add("IF COL_LENGTH(N'dbo.CspPackageApprovals', N'PublishedVersion') IS NULL ALTER TABLE dbo.CspPackageApprovals ADD PublishedVersion bigint NULL;");
            foreach (var (table, columns) in profileColumns)
                foreach (var (column, definition) in columns)
                    scripts.Add($"IF COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL ALTER TABLE dbo.{table} ADD {column} {definition};");
        }
        var indexes = new[]
        {
            ("CspPackages", "ProviderId_IdempotencyKey", "ProviderId,IdempotencyKey", true),
            ("CspPackages", "ProcessingState_LeaseExpiresTicks", "ProcessingState,LeaseExpiresTicks", false),
            ("CspPackageEntries", "PackageId_StableKey", "PackageId,StableKey", true),
            ("CspPackageCandidates", "PackageId_StableKey", "PackageId,StableKey", true),
            ("CspPackageApprovals", "PackageId", "PackageId", false),
            ("CspPackageAudits", "PackageId", "PackageId", false)
        };
        foreach (var (table, suffix, columns, unique) in indexes)
        {
            var name = $"IX_{table}_{suffix}";
            var kind = unique ? "UNIQUE " : "";
            scripts.Add(sqlServer
                ? $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'{name}' AND object_id=OBJECT_ID(N'dbo.{table}')) CREATE {kind}INDEX {name} ON dbo.{table}({columns});"
                : $"CREATE {kind}INDEX IF NOT EXISTS {name} ON {table}({columns});");
        }
        return scripts;
    }
}

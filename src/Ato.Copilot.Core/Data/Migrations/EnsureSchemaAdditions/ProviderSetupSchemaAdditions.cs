using Ato.Copilot.Core.Data.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class ProviderSetupSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlServer() && !db.Database.IsSqlite())
            throw new NotSupportedException("Provider setup persistence requires SQLite or SQL Server.");
        var sql = db.Database.IsSqlServer();
        var guid = sql ? "uniqueidentifier" : "TEXT";
        var text = sql ? "nvarchar(max)" : "TEXT";
        var date = sql ? "datetimeoffset" : "TEXT";
        var number = sql ? "bigint" : "INTEGER";
        var boolean = sql ? "bit" : "INTEGER";
        string Short(int count) => sql ? $"nvarchar({count})" : "TEXT";
        var definitions = new Dictionary<string, string>
        {
            ["ProviderSetupDrafts"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL UNIQUE,
                SchemaVersion int NOT NULL, Revision {number} NOT NULL,
                DraftJson {text} NOT NULL, CommittedMetadataJson {text} NOT NULL, CompletionSnapshotJson {text} NULL,
                CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL, UpdatedAt {date} NOT NULL, UpdatedBy {Short(254)} NOT NULL,
                UNIQUE(ProviderId,Id), FOREIGN KEY(ProviderId) REFERENCES CspProfiles(Id)
                """,
            ["ProviderSetupCommands"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, DraftId {guid} NOT NULL,
                Operation {Short(32)} NOT NULL, IdempotencyKey {Short(100)} NOT NULL, IntentHash {Short(64)} NOT NULL,
                RequestJson {text} NOT NULL, OutcomeJson {text} NOT NULL, HistoricalCommitSnapshotJson {text} NOT NULL,
                CommittedDraftRevision {number} NOT NULL, CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL,
                UNIQUE(ProviderId,Operation,IdempotencyKey),
                FOREIGN KEY(ProviderId,DraftId) REFERENCES ProviderSetupDrafts(ProviderId,Id)
                """,
            ["CspPackageUploadIntents"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, DraftId {guid} NULL,
                EntryPoint {Short(32)} NOT NULL DEFAULT 'Onboarding', OfferingHintId {guid} NULL,
                IdempotencyKey {Short(100)} NOT NULL, IntentHash {Short(64)} NOT NULL, IntentJson {text} NOT NULL,
                Revision {number} NOT NULL, CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL,
                UNIQUE(ProviderId,IdempotencyKey),
                FOREIGN KEY(ProviderId) REFERENCES CspProfiles(Id),
                FOREIGN KEY(ProviderId,DraftId) REFERENCES ProviderSetupDrafts(ProviderId,Id)
                """,
            ["ServicePortfolios"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL,
                Name {Short(256)} NOT NULL, Description {text} NOT NULL, Lifecycle {Short(32)} NOT NULL,
                Revision {number} NOT NULL, CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL,
                UpdatedAt {date} NOT NULL, UpdatedBy {Short(254)} NOT NULL,
                UNIQUE(ProviderId,Id), FOREIGN KEY(ProviderId) REFERENCES CspProfiles(Id)
                """,
            ["ServicePortfolioOfferingRevisions"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL,
                PortfolioId {guid} NOT NULL, OfferingId {guid} NOT NULL, PredecessorId {guid} NULL,
                IsPrimary {boolean} NOT NULL, State {Short(32)} NOT NULL, Reason {Short(2000)} NULL,
                Revision {number} NOT NULL, CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL,
                UNIQUE(ProviderId,Id), UNIQUE(ProviderId,OfferingId,Revision),
                FOREIGN KEY(ProviderId,PortfolioId) REFERENCES ServicePortfolios(ProviderId,Id),
                FOREIGN KEY(ProviderId,OfferingId) REFERENCES ProviderOfferings(ProviderId,Id),
                FOREIGN KEY(ProviderId,PredecessorId) REFERENCES ServicePortfolioOfferingRevisions(ProviderId,Id)
                """,
            ["ProviderOfferingAuthorizationIntentRevisions"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, SetupId {guid} NOT NULL,
                OfferingId {guid} NULL, PredecessorId {guid} NULL, StartingPoint {Short(32)} NOT NULL,
                UnconfirmedFactsJson {text} NOT NULL, SourcesJson {text} NOT NULL, UnresolvedFieldsJson {text} NOT NULL,
                Revision {number} NOT NULL, CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL,
                UNIQUE(ProviderId,Id), UNIQUE(ProviderId,SetupId,Revision),
                FOREIGN KEY(ProviderId,SetupId) REFERENCES ProviderSetupDrafts(ProviderId,Id),
                FOREIGN KEY(ProviderId,OfferingId) REFERENCES ProviderOfferings(ProviderId,Id),
                FOREIGN KEY(ProviderId,PredecessorId) REFERENCES ProviderOfferingAuthorizationIntentRevisions(ProviderId,Id)
                """,
            ["ProviderSetupWorkItems"] = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, SetupId {guid} NOT NULL,
                PortfolioId {guid} NULL, OfferingId {guid} NULL, UploadIntentId {guid} NULL,
                Type {Short(64)} NOT NULL, ReasonCode {Short(64)} NOT NULL, State {Short(32)} NOT NULL,
                OwnerRole {Short(64)} NOT NULL, AccountablePrincipalId {Short(254)} NULL,
                ReviewerPrincipalId {Short(254)} NULL, DueAt {date} NULL,
                AcceptanceCriteria {Short(2000)} NOT NULL, Destination {Short(2048)} NOT NULL,
                SourceRevision {number} NOT NULL, IdempotencyKey {Short(300)} NOT NULL,
                CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL,
                ClosedAt {date} NULL, ClosedBy {Short(254)} NULL,
                UNIQUE(ProviderId,IdempotencyKey),
                FOREIGN KEY(ProviderId,SetupId) REFERENCES ProviderSetupDrafts(ProviderId,Id),
                FOREIGN KEY(ProviderId,PortfolioId) REFERENCES ServicePortfolios(ProviderId,Id),
                FOREIGN KEY(ProviderId,OfferingId) REFERENCES ProviderOfferings(ProviderId,Id)
                """
        };
        foreach (var (table, definition) in definitions)
        {
            var statement = sql ? $"IF OBJECT_ID(N'[{table}]', N'U') IS NULL CREATE TABLE [{table}] ({definition})"
                : $"CREATE TABLE IF NOT EXISTS [{table}] ({definition})";
            await db.Database.ExecuteSqlRawAsync(statement, ct);
        }
        foreach (var (table, column, definition) in new[]
        {
            ("CspProfiles", "SetupRevision", $"{number} NOT NULL DEFAULT 1"),
            ("CspProfiles", "DodComponent", $"{Short(128)} NULL"),
            ("CspProfiles", "TimeZoneId", $"{Short(128)} NULL"),
            ("CspPackages", "UploadIntentId", $"{guid} NULL"),
            ("CspPackages", "HandlingPolicyVersion", $"{Short(100)} NULL"),
            ("CspPackages", "HandlingDeclarationJson", $"{text} NULL"),
            ("CspPackages", "RequiresOfferingAssociation", $"{(sql ? "bit" : "INTEGER")} NOT NULL DEFAULT 0"),
            ("CspPackageUploadIntents", "EntryPoint", $"{Short(32)} NOT NULL DEFAULT 'Onboarding'"),
            ("CspPackageUploadIntents", "OfferingHintId", $"{guid} NULL")
        })
        {
            // DDL identifiers/types come exclusively from the closed declarations above.
            // Metadata lookup arguments are values and must be parameterized.
            if (sql)
            {
                var statement = $"IF COL_LENGTH(@tableName, @columnName) IS NULL ALTER TABLE [{table}] ADD [{column}] {definition}";
                await db.Database.ExecuteSqlRawAsync(statement,
                    new object[] { new SqlParameter("@tableName", table), new SqlParameter("@columnName", column) }, ct);
            }
            else
            {
                var columns = await db.Database.SqlQuery<string>(
                    $"SELECT name AS Value FROM pragma_table_info({table})").ToListAsync(ct);
                if (!columns.Contains(column, StringComparer.OrdinalIgnoreCase))
                {
                    var statement = $"ALTER TABLE [{table}] ADD [{column}] {definition}";
                    await db.Database.ExecuteSqlRawAsync(statement, ct);
                }
            }
        }
        if (sql)
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE CspPackageUploadIntents ALTER COLUMN DraftId uniqueidentifier NULL", ct);
        else
        {
            var requiredDraft = await db.Database.SqlQueryRaw<long>(
                """SELECT "notnull" AS Value FROM pragma_table_info('CspPackageUploadIntents') WHERE name = 'DraftId'""").SingleAsync(ct);
            if (requiredDraft == 1)
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE CspPackageUploadIntents_nullable (
                        Id TEXT NOT NULL PRIMARY KEY, ProviderId TEXT NOT NULL, DraftId TEXT NULL,
                        EntryPoint TEXT NOT NULL DEFAULT 'Onboarding', OfferingHintId TEXT NULL,
                        IdempotencyKey TEXT NOT NULL, IntentHash TEXT NOT NULL, IntentJson TEXT NOT NULL,
                        Revision INTEGER NOT NULL, CreatedAt TEXT NOT NULL, CreatedBy TEXT NOT NULL,
                        UNIQUE(ProviderId,IdempotencyKey),
                        FOREIGN KEY(ProviderId) REFERENCES CspProfiles(Id),
                        FOREIGN KEY(ProviderId,DraftId) REFERENCES ProviderSetupDrafts(ProviderId,Id));
                    INSERT INTO CspPackageUploadIntents_nullable
                        (Id,ProviderId,DraftId,EntryPoint,OfferingHintId,IdempotencyKey,IntentHash,IntentJson,Revision,CreatedAt,CreatedBy)
                        SELECT Id,ProviderId,DraftId,EntryPoint,OfferingHintId,IdempotencyKey,IntentHash,IntentJson,Revision,CreatedAt,CreatedBy
                        FROM CspPackageUploadIntents;
                    DROP TABLE CspPackageUploadIntents;
                    ALTER TABLE CspPackageUploadIntents_nullable RENAME TO CspPackageUploadIntents;
                    """, ct);
                await transaction.CommitAsync(ct);
            }
        }
        var intentListIndex = sql
            ? "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CspPackageUploadIntents_EntryPoint' AND object_id = OBJECT_ID(N'CspPackageUploadIntents')) CREATE INDEX IX_CspPackageUploadIntents_EntryPoint ON CspPackageUploadIntents(ProviderId,EntryPoint,OfferingHintId,Id)"
            : "CREATE INDEX IF NOT EXISTS IX_CspPackageUploadIntents_EntryPoint ON CspPackageUploadIntents(ProviderId,EntryPoint,OfferingHintId,Id)";
        await db.Database.ExecuteSqlRawAsync(intentListIndex, ct);
        var uniqueIntentIndex = sql
            ? "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CspPackages_UploadIntentId' AND object_id = OBJECT_ID(N'CspPackages')) CREATE UNIQUE INDEX IX_CspPackages_UploadIntentId ON CspPackages(UploadIntentId) WHERE UploadIntentId IS NOT NULL"
            : "CREATE UNIQUE INDEX IF NOT EXISTS IX_CspPackages_UploadIntentId ON CspPackages(UploadIntentId) WHERE UploadIntentId IS NOT NULL";
        await db.Database.ExecuteSqlRawAsync(uniqueIntentIndex, ct);
        var activePortfolioIndex = sql
            ? "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServicePortfolioOfferingRevisions_ActivePrimary' AND object_id = OBJECT_ID(N'ServicePortfolioOfferingRevisions')) CREATE UNIQUE INDEX IX_ServicePortfolioOfferingRevisions_ActivePrimary ON ServicePortfolioOfferingRevisions(ProviderId,OfferingId) WHERE IsPrimary = 1 AND State = 'Active'"
            : "CREATE UNIQUE INDEX IF NOT EXISTS IX_ServicePortfolioOfferingRevisions_ActivePrimary ON ServicePortfolioOfferingRevisions(ProviderId,OfferingId) WHERE IsPrimary = 1 AND State = 'Active'";
        await db.Database.ExecuteSqlRawAsync(activePortfolioIndex, ct);
        var workQueueIndex = sql
            ? "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProviderSetupWorkItems_Queue' AND object_id = OBJECT_ID(N'ProviderSetupWorkItems')) CREATE INDEX IX_ProviderSetupWorkItems_Queue ON ProviderSetupWorkItems(ProviderId,State,OwnerRole)"
            : "CREATE INDEX IF NOT EXISTS IX_ProviderSetupWorkItems_Queue ON ProviderSetupWorkItems(ProviderId,State,OwnerRole)";
        await db.Database.ExecuteSqlRawAsync(workQueueIndex, ct);
        logger.LogInformation("Verified additive private provider setup schema");
    }
}

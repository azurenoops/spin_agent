using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class ProviderEvidenceSharingSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite() && !db.Database.IsSqlServer())
            throw new NotSupportedException("Evidence sharing requires SQLite or SQL Server.");
        foreach (var script in Scripts(db.Database.IsSqlServer()))
            await db.Database.ExecuteSqlRawAsync(script, ct);
        logger.LogInformation("Verified additive provider evidence sharing schema");
    }

    public static IReadOnlyList<string> Scripts(bool sqlServer)
    {
        var guid = sqlServer ? "uniqueidentifier" : "TEXT";
        var date = sqlServer ? "datetimeoffset" : "TEXT";
        var number = sqlServer ? "bigint" : "INTEGER";
        var text = sqlServer ? "nvarchar(max)" : "TEXT";
        string Short(int n) => sqlServer ? $"nvarchar({n})" : "TEXT";
        var table = sqlServer ? "dbo.ProviderEvidenceShares" : "ProviderEvidenceShares";
        var create = $"""
            CREATE TABLE {table} (
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, OfferingId {guid} NOT NULL,
                Revision {number} NOT NULL, CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL,
                EvidenceId {guid} NOT NULL, EvidenceRevision {number} NOT NULL, AssignmentId {guid} NOT NULL, AssignmentRevision {number} NOT NULL,
                TargetTenantId {guid} NOT NULL, SystemId {Short(36)} NOT NULL, Version {number} NOT NULL,
                PreviousVersionId {guid} NULL, Summary {text} NOT NULL, SourceSha256 {Short(64)} NOT NULL,
                ContentJson {text} NOT NULL, ContentHash {Short(64)} NOT NULL,
                ApprovedBy {Short(254)} NOT NULL, ApprovedAt {date} NOT NULL, RevokedAt {date} NULL,
                RevokedBy {Short(254)} NULL, RevocationReason {Short(2000)} NULL,
                CONSTRAINT AK_ProviderEvidenceShares_Owner UNIQUE (ProviderId, OfferingId, Id),
                FOREIGN KEY (ProviderId) REFERENCES CspProfiles (Id) ON DELETE NO ACTION,
                FOREIGN KEY (ProviderId, OfferingId) REFERENCES ProviderOfferings (ProviderId, Id) ON DELETE NO ACTION,
                FOREIGN KEY (ProviderId, OfferingId, EvidenceId) REFERENCES ProviderFindingEvidences (ProviderId, OfferingId, Id) ON DELETE NO ACTION,
                FOREIGN KEY (ProviderId, OfferingId, TargetTenantId, SystemId, AssignmentId)
                    REFERENCES ProviderHostingAssignments (ProviderId, OfferingId, TargetTenantId, SystemId, Id) ON DELETE NO ACTION,
                FOREIGN KEY (ProviderId, OfferingId, PreviousVersionId) REFERENCES ProviderEvidenceShares (ProviderId, OfferingId, Id) ON DELETE NO ACTION
            );
            """;
        var scripts = new List<string>
        {
            sqlServer ? $"IF OBJECT_ID(N'{table}', N'U') IS NULL BEGIN {create} END;"
                : create.Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", StringComparison.Ordinal)
        };
        foreach (var (name, columns, unique) in new[]
        {
            ("IX_ProviderEvidenceShares_Version", "ProviderId, OfferingId, EvidenceId, AssignmentId, Version", true),
            ("IX_ProviderEvidenceShares_Target", "TargetTenantId, SystemId", false),
            ("IX_ProviderEvidenceShares_Owner", "ProviderId, OfferingId", false)
        })
        {
            var index = $"CREATE {(unique ? "UNIQUE " : "")}INDEX {name} ON {table} ({columns});";
            scripts.Add(sqlServer
                ? $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{name}' AND object_id = OBJECT_ID(N'{table}')) {index}"
                : index.Replace($"INDEX {name}", $"INDEX IF NOT EXISTS {name}", StringComparison.Ordinal));
        }
        return scripts;
    }
}

using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>Additive canonical environment references; never infer legacy registration or resource mappings.</summary>
public static class SystemEnvironmentSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite() && !db.Database.IsSqlServer())
            throw new NotSupportedException("Canonical environment persistence requires SQLite or SQL Server.");
        foreach (var script in Scripts(db.Database.IsSqlServer()))
            await db.Database.ExecuteSqlRawAsync(script, ct);
        logger.LogInformation("Verified additive canonical system environment schema for {Provider}", db.Database.ProviderName);
    }

    public static IReadOnlyList<string> Scripts(bool sqlServer)
    {
        var guid = sqlServer ? "uniqueidentifier" : "TEXT";
        var text = sqlServer ? "nvarchar(max)" : "TEXT";
        var date = sqlServer ? "datetimeoffset" : "TEXT";
        var number = sqlServer ? "bigint" : "INTEGER";
        string Short(int size) => sqlServer ? $"nvarchar({size})" : "TEXT";
        string Table(string name) => sqlServer ? $"dbo.[{name}]" : $"[{name}]";
        var common = $"Id {guid} NOT NULL PRIMARY KEY, TenantId {guid} NOT NULL, SystemId {Short(36)} NOT NULL";
        var provider = $"Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, OfferingId {guid} NOT NULL, "
            + $"Revision {number} NOT NULL, CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL";
        var tables = new Dictionary<string, string>
        {
            ["SystemProviderScopeSelections"] = $"""
                {common}, AssignmentId {guid} NOT NULL, Version {number} NOT NULL, State {Short(16)} NOT NULL,
                HistoryJson {text} NOT NULL, UpdatedAt {date} NOT NULL, UpdatedBy {Short(254)} NOT NULL,
                FOREIGN KEY (AssignmentId) REFERENCES {Table("ProviderHostingAssignments")} (Id) ON DELETE NO ACTION
                """,
            ["SystemEnvironmentWorkspaces"] = $"{common}, Version {number} NOT NULL, UNIQUE (TenantId,SystemId)",
            ["SystemEnvironmentAttachmentRecords"] = $"""
                {common}, RegistrationId {guid} NOT NULL, AllocationId {guid} NULL, AppliedAllocationVersion {number} NULL, HostingAssignmentId {guid} NULL,
                Source {Short(32)} NOT NULL, State {Short(32)} NOT NULL, Version {number} NOT NULL,
                ScopeJson {text} NOT NULL, RegistrationSnapshotJson {text} NOT NULL, ProvenanceJson {text} NOT NULL,
                HistoryJson {text} NOT NULL, AssessmentAccessJson {text} NOT NULL, MonitoringAccessJson {text} NOT NULL,
                UpdatedAt {date} NOT NULL, UpdatedBy {Short(254)} NOT NULL,
                UNIQUE (TenantId,SystemId,RegistrationId),
                FOREIGN KEY (RegistrationId) REFERENCES {Table("AzureSubscriptionRegistrations")} (Id) ON DELETE NO ACTION
                """,
            ["SystemEnvironmentPendingOperations"] = $"""
                {common}, Kind {Short(32)} NOT NULL, Actor {Short(254)} NOT NULL, Version {number} NOT NULL,
                MaterialJson {text} NOT NULL, ExpiresAt {date} NOT NULL
                """,
            ["SystemEnvironmentHostingLinkRecords"] = $"""
                {common}, AttachmentId {guid} NOT NULL, AssignmentId {guid} NOT NULL, Version {number} NOT NULL,
                State {Short(16)} NOT NULL, Source {Short(24)} NOT NULL, HistoryJson {text} NOT NULL,
                UpdatedAt {date} NOT NULL, UpdatedBy {Short(254)} NOT NULL,
                FOREIGN KEY (AttachmentId) REFERENCES {Table("SystemEnvironmentAttachmentRecords")} (Id) ON DELETE NO ACTION,
                FOREIGN KEY (AssignmentId) REFERENCES {Table("ProviderHostingAssignments")} (Id) ON DELETE NO ACTION
                """,
            ["SystemEnvironmentReplays"] = $"""
                {common}, [Key] {Short(100)} NOT NULL, Actor {Short(254)} NOT NULL, IntentHash {Short(64)} NOT NULL,
                ResponseJson {text} NOT NULL, UNIQUE (TenantId,SystemId,[Key])
                """,
            ["ProviderEnvironmentAllocationRecords"] = $"""
                {provider}, RegistrationId {guid} NOT NULL, RegistrationOwnerTenantId {guid} NOT NULL,
                ConsumerTenantId {guid} NOT NULL, HostingScopeRevisionId {guid} NOT NULL, PermittedResourceScopesJson {text} NOT NULL,
                RegistrationSnapshotJson {text} NOT NULL, ProvenanceJson {text} NOT NULL, State {Short(32)} NOT NULL,
                StartsAt {date} NOT NULL, ExpiresAt {date} NULL, ReplacementAllocationId {guid} NULL, HistoryJson {text} NOT NULL,
                UNIQUE (ProviderId,OfferingId,Id),
                FOREIGN KEY (ProviderId) REFERENCES {Table("CspProfiles")} (Id) ON DELETE NO ACTION,
                FOREIGN KEY (ProviderId,OfferingId) REFERENCES {Table("ProviderOfferings")} (ProviderId,Id) ON DELETE NO ACTION,
                FOREIGN KEY (RegistrationId) REFERENCES {Table("AzureSubscriptionRegistrations")} (Id) ON DELETE NO ACTION
                """,
            ["ProviderEnvironmentAllocationPreviews"] = $"""
                {provider}, AllocationId {guid} NOT NULL, AllocationVersion {number} NOT NULL,
                MaterialJson {text} NOT NULL, ImpactJson {text} NOT NULL, ExpiresAt {date} NOT NULL,
                UNIQUE (ProviderId,OfferingId,Id),
                FOREIGN KEY (ProviderId) REFERENCES {Table("CspProfiles")} (Id) ON DELETE NO ACTION,
                FOREIGN KEY (ProviderId,OfferingId) REFERENCES {Table("ProviderOfferings")} (ProviderId,Id) ON DELETE NO ACTION
                """
        };
        var scripts = tables.Select(x => sqlServer
            ? $"IF OBJECT_ID(N'dbo.{x.Key}', N'U') IS NULL CREATE TABLE {Table(x.Key)} ({x.Value});"
            : $"CREATE TABLE IF NOT EXISTS {Table(x.Key)} ({x.Value});").ToList();
        var indexes = new (string Table, string Columns, bool Unique)[]
        {
            ("SystemProviderScopeSelections", "TenantId,SystemId,AssignmentId", true),
            ("SystemProviderScopeSelections", "AssignmentId", false),
            ("SystemEnvironmentHostingLinkRecords", "TenantId,SystemId,AttachmentId,AssignmentId", true),
            ("SystemEnvironmentHostingLinkRecords", "AttachmentId", false),
            ("SystemEnvironmentHostingLinkRecords", "AssignmentId", false),
            ("SystemEnvironmentWorkspaces", "TenantId,SystemId", true),
            ("SystemEnvironmentAttachmentRecords", "TenantId,SystemId,RegistrationId", true),
            ("SystemEnvironmentAttachmentRecords", "RegistrationId", false),
            ("SystemEnvironmentPendingOperations", "TenantId,SystemId", false),
            ("SystemEnvironmentReplays", "TenantId,SystemId,Key", true),
            ("ProviderEnvironmentAllocationRecords", "ProviderId,OfferingId", false),
            ("ProviderEnvironmentAllocationRecords", "RegistrationId", false),
            ("ProviderEnvironmentAllocationRecords", "ConsumerTenantId,RegistrationId", false),
            ("ProviderEnvironmentAllocationPreviews", "ProviderId,OfferingId", false)
        };
        foreach (var index in indexes)
        {
            var name = $"IX_{index.Table}_{index.Columns.Replace(',', '_')}";
            var columns = string.Join(",", index.Columns.Split(',').Select(column => $"[{column}]"));
            var unique = index.Unique ? "UNIQUE " : "";
            scripts.Add(sqlServer
                ? $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{name}' AND object_id = OBJECT_ID(N'dbo.{index.Table}')) CREATE {unique}INDEX [{name}] ON {Table(index.Table)} ({columns});"
                : $"CREATE {unique}INDEX IF NOT EXISTS [{name}] ON {Table(index.Table)} ({columns});");
        }
        return scripts;
    }
}

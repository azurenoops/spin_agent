using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>
/// Additive offering and authorization ledger. Run after tenancy and the CSP package ledger;
/// existing receipts, catalogs, assignments, and recorded decisions are never backfilled.
/// </summary>
public static class ProviderAuthorizationSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite() && !db.Database.IsSqlServer())
            throw new NotSupportedException("Provider authorization persistence requires SQLite or SQL Server.");
        foreach (var script in Scripts(db.Database.IsSqlServer()))
            await db.Database.ExecuteSqlRawAsync(script, ct);
        logger.LogInformation("Verified additive provider authorization schema for {Provider}", db.Database.ProviderName);
    }

    public static IReadOnlyList<string> Scripts(bool sqlServer)
    {
        var guid = sqlServer ? "uniqueidentifier" : "TEXT";
        var text = sqlServer ? "nvarchar(max)" : "TEXT";
        var date = sqlServer ? "datetimeoffset" : "TEXT";
        var number = sqlServer ? "bigint" : "INTEGER";
        var integer = sqlServer ? "int" : "INTEGER";
        var boolean = sqlServer ? "bit" : "INTEGER";
        string Short(int length) => sqlServer ? $"nvarchar({length})" : "TEXT";
        string Table(string name) => sqlServer ? $"dbo.[{name}]" : $"[{name}]";
        string Reference(string columns, string table, string principal = "Id") =>
            $"FOREIGN KEY ({columns}) REFERENCES {Table(table)} ({principal}) ON DELETE NO ACTION";
        string ScopedReference(string column, string table) =>
            Reference($"ProviderId, OfferingId, {column}", table, "ProviderId, OfferingId, Id");

        var columns = new Dictionary<string, string>
        {
            ["ProviderOfferings"] = $"""
                Name {Short(256)} NOT NULL, Description {text} NOT NULL, EnvironmentsJson {text} NOT NULL,
                Lifecycle {Short(32)} NOT NULL, CurrentBoundaryRevisionId {guid} NULL, CurrentHostingScopeRevisionId {guid} NULL,
                CONSTRAINT AK_ProviderOfferings_ProviderId_Id UNIQUE (ProviderId, Id)
                """,
            ["ProviderBoundaryRevisions"] = $"""
                PredecessorId {guid} NULL, SnapshotJson {text} NOT NULL, SnapshotHash {Short(64)} NOT NULL,
                {ScopedReference("PredecessorId", "ProviderBoundaryRevisions")}
                """,
            ["ProviderHostingScopeRevisions"] = $"""
                PredecessorId {guid} NULL, SnapshotJson {text} NOT NULL, SnapshotHash {Short(64)} NOT NULL,
                {ScopedReference("PredecessorId", "ProviderHostingScopeRevisions")}
                """,
            ["ProviderAuthorizationRecords"] = $"CurrentRevisionId {guid} NOT NULL",
            ["ProviderAuthorizationRevisions"] = $"""
                RecordId {guid} NOT NULL, BoundaryRevisionId {guid} NOT NULL,
                SnapshotJson {text} NOT NULL, SnapshotHash {Short(64)} NOT NULL,
                MetadataReviewState {Short(32)} NOT NULL, RecordedBy {Short(254)} NULL, RecordedAt {date} NULL,
                ReviewRationale {text} NULL,
                {ScopedReference("RecordId", "ProviderAuthorizationRecords")},
                {ScopedReference("BoundaryRevisionId", "ProviderBoundaryRevisions")}
                """,
            ["ProviderAuthorizationLifecycleEvents"] = $"""
                RecordId {guid} NOT NULL, AuthorizationRevisionId {guid} NOT NULL,
                Kind {Short(32)} NOT NULL, EffectiveOn {Short(10)} NOT NULL, ReplacementRevisionId {guid} NULL,
                Rationale {text} NOT NULL, CitationsJson {text} NOT NULL,
                {ScopedReference("RecordId", "ProviderAuthorizationRecords")},
                {ScopedReference("AuthorizationRevisionId", "ProviderAuthorizationRevisions")},
                {ScopedReference("ReplacementRevisionId", "ProviderAuthorizationRevisions")}
                """,
            ["ProviderPackageVersions"] = $"""
                SeriesId {guid} NOT NULL, Version {number} NOT NULL, PackageId {guid} NOT NULL,
                BoundaryRevisionId {guid} NOT NULL, PreviousVersionId {guid} NULL, ManifestHash {Short(64)} NOT NULL,
                {Reference("PackageId", "CspPackages")},
                {ScopedReference("BoundaryRevisionId", "ProviderBoundaryRevisions")},
                {ScopedReference("PreviousVersionId", "ProviderPackageVersions")}
                """,
            ["ProviderAuthorizationImpactReviews"] = $"""
                PreviewId {guid} NOT NULL, InputJson {text} NOT NULL, ContextJson {text} NOT NULL,
                PreviewHash {Short(64)} NOT NULL, ContextSnapshotHash {Short(64)} NOT NULL,
                TargetsJson {text} NOT NULL, BlockersJson {text} NOT NULL, Disposition {Short(32)} NOT NULL,
                ExpiresAt {date} NOT NULL, InvalidatedAt {date} NULL, ReviewedBy {Short(254)} NULL,
                ReviewedAt {date} NULL, Rationale {text} NULL
                """,
            ["ProviderCatalogContextSnapshots"] = $"""
                ReleaseId {guid} NULL, PackageApprovalId {guid} NULL, CapabilityId {guid} NULL,
                ComponentId {guid} NULL, ImpactReviewId {guid} NOT NULL,
                SnapshotJson {text} NOT NULL, SnapshotHash {Short(64)} NOT NULL,
                {ScopedReference("ImpactReviewId", "ProviderAuthorizationImpactReviews")}
                """,
            ["ProviderHostingAssignments"] = $"""
                TargetTenantId {guid} NOT NULL, SystemId {Short(36)} NOT NULL, HostingScopeRevisionId {guid} NOT NULL,
                AssignedScopesJson {text} NOT NULL, ReferencesJson {text} NOT NULL,
                CONSTRAINT AK_ProviderHostingAssignments_ProviderId_OfferingId_TargetTenantId_SystemId_Id
                    UNIQUE (ProviderId, OfferingId, TargetTenantId, SystemId, Id),
                {Reference("TargetTenantId", "Tenants")},
                {ScopedReference("HostingScopeRevisionId", "ProviderHostingScopeRevisions")}
                """,
            ["MissionProviderRelationshipReviews"] = $"""
                TenantId {guid} NOT NULL, SystemId {Short(36)} NOT NULL, AssignmentId {guid} NOT NULL,
                AssignmentRevision {number} NOT NULL, State {Short(48)} NOT NULL, AuthorizationRevisionId {guid} NULL,
                BoundaryRevisionId {guid} NULL, PreviewId {guid} NULL, PreviewJson {text} NULL,
                PreviewHash {Short(64)} NULL, PreviewExpiresAt {date} NULL, ReviewRequired {boolean} NOT NULL,
                HistoryJson {text} NOT NULL, EvidenceJson {text} NOT NULL, ReviewedBy {Short(254)} NULL, ReviewedAt {date} NULL,
                {Reference("TenantId", "Tenants")},
                {Reference("ProviderId, OfferingId, TenantId, SystemId, AssignmentId", "ProviderHostingAssignments", "ProviderId, OfferingId, TargetTenantId, SystemId, Id")},
                {ScopedReference("AuthorizationRevisionId", "ProviderAuthorizationRevisions")},
                {ScopedReference("BoundaryRevisionId", "ProviderBoundaryRevisions")}
                """,
            ["CapabilityAdoptionSnapshots"] = $"""
                TenantId {guid} NOT NULL, SystemId {Short(36)} NOT NULL, SubscriptionId {Short(36)} NOT NULL,
                AssignmentId {guid} NOT NULL, AssignmentRevision {number} NOT NULL,
                CapabilityId {guid} NOT NULL, ReleaseId {guid} NOT NULL, ContextSnapshotId {guid} NOT NULL,
                SnapshotJson {text} NOT NULL, SnapshotHash {Short(64)} NOT NULL,
                {Reference("TenantId", "Tenants")},
                {Reference("ProviderId, OfferingId, TenantId, SystemId, AssignmentId", "ProviderHostingAssignments", "ProviderId, OfferingId, TargetTenantId, SystemId, Id")},
                {ScopedReference("ContextSnapshotId", "ProviderCatalogContextSnapshots")}
                """,
            ["ProviderFindings"] = $"""
                Title {Short(256)} NOT NULL, Observation {text} NOT NULL, SeverityAsStated {Short(2000)} NULL,
                WorkflowState {Short(32)} NOT NULL, SourceCandidateRefJson {text} NULL,
                ControlIdsJson {text} NOT NULL, CitationsJson {text} NOT NULL
                """,
            ["ProviderPoamItems"] = $"""
                Title {Short(256)} NOT NULL, FindingIdsJson {text} NOT NULL, CorrectiveAction {text} NOT NULL,
                OwnerAsStated {text} NULL, MilestonesJson {text} NOT NULL, WorkflowState {Short(32)} NOT NULL,
                SourceCandidateRefJson {text} NULL, CitationsJson {text} NOT NULL, HistoryJson {text} NOT NULL
                """,
            ["ProviderFindingEvidences"] = $"""
                FindingId {guid} NOT NULL, FileName {Short(512)} NOT NULL, MediaType {Short(256)} NOT NULL,
                StorageKey {text} NOT NULL, ByteLength {number} NOT NULL, Sha256 {Short(64)} NOT NULL,
                Description {text} NOT NULL, State {Short(32)} NOT NULL,
                {ScopedReference("FindingId", "ProviderFindings")}
                """,
            ["ProviderFindingReviews"] = $"""
                FindingId {guid} NOT NULL, EvidenceIdsJson {text} NOT NULL, Disposition {Short(32)} NOT NULL,
                Rationale {text} NOT NULL, {ScopedReference("FindingId", "ProviderFindings")}
                """,
            ["ProviderAuthorizationOperations"] = $"""
                OperationScope {Short(200)} NOT NULL, IdempotencyKey {Short(100)} NOT NULL,
                IntentHash {Short(64)} NOT NULL, ResponseJson {text} NOT NULL
                """,
            ["ProviderAuthorizationAudits"] = $"""
                Action {Short(100)} NOT NULL, TargetId {guid} NOT NULL, DetailJson {text} NOT NULL
                """,
            ["ProviderClaimReviews"] = $"""
                PackageId {guid} NOT NULL, CandidateId {guid} NOT NULL, CandidateRevision {number} NOT NULL,
                Action {Short(32)} NOT NULL, Rationale {text} NOT NULL, ResolutionsJson {text} NOT NULL,
                {Reference("PackageId", "CspPackages")}, {Reference("CandidateId", "CspPackageCandidates")}
                """,
            ["ProviderPackageEnrichments"] = $"""
                PackageId {guid} NOT NULL, SourceProfileVersion {integer} NOT NULL, TargetProfileVersion {integer} NOT NULL,
                State {Short(32)} NOT NULL, ErrorCode {text} NULL, Message {text} NULL,
                {Reference("PackageId", "CspPackages")}
                """
        };
        var scripts = new List<string>();
        foreach (var (table, extra) in columns)
        {
            var common = $"""
                Id {guid} NOT NULL PRIMARY KEY, ProviderId {guid} NOT NULL, OfferingId {guid} NOT NULL,
                Revision {number} NOT NULL, CreatedAt {date} NOT NULL, CreatedBy {Short(254)} NOT NULL
                """;
            var ownership = $"""
                CONSTRAINT AK_{table}_ProviderId_OfferingId_Id UNIQUE (ProviderId, OfferingId, Id),
                {Reference("ProviderId", "CspProfiles")}
                """;
            if (table != "ProviderOfferings")
                ownership += $", {Reference("ProviderId, OfferingId", "ProviderOfferings", "ProviderId, Id")}";
            scripts.Add(sqlServer
                ? $"IF OBJECT_ID(N'dbo.{table}', N'U') IS NULL CREATE TABLE {Table(table)} ({common}, {extra}, {ownership});"
                : $"CREATE TABLE IF NOT EXISTS {Table(table)} ({common}, {extra}, {ownership});");
        }

        foreach (var table in columns.Keys)
            Index(table, "ProviderId,OfferingId");
        Index("ProviderBoundaryRevisions", "ProviderId,OfferingId,Revision", true);
        Index("ProviderHostingScopeRevisions", "ProviderId,OfferingId,Revision", true);
        Index("ProviderAuthorizationRevisions", "ProviderId,OfferingId,RecordId,Revision", true);
        Index("ProviderPackageVersions", "ProviderId,OfferingId,SeriesId,Version", true);
        Index("ProviderPackageVersions", "PackageId", true);
        Index("ProviderAuthorizationOperations", "ProviderId,OperationScope,IdempotencyKey", true);
        Index("ProviderAuthorizationImpactReviews", "PreviewId", true);
        Index("ProviderClaimReviews", "ProviderId,OfferingId,CandidateId,CandidateRevision");
        Index("ProviderPackageEnrichments", "ProviderId,OfferingId,PackageId,TargetProfileVersion");
        Index("MissionProviderRelationshipReviews", "TenantId,SystemId");
        Index("CapabilityAdoptionSnapshots", "TenantId,SystemId");

        foreach (var (table, column) in new[]
        {
            ("ProviderBoundaryRevisions", "PredecessorId"),
            ("ProviderHostingScopeRevisions", "PredecessorId"),
            ("ProviderAuthorizationRevisions", "BoundaryRevisionId"),
            ("ProviderAuthorizationLifecycleEvents", "RecordId"),
            ("ProviderAuthorizationLifecycleEvents", "AuthorizationRevisionId"),
            ("ProviderAuthorizationLifecycleEvents", "ReplacementRevisionId"),
            ("ProviderPackageVersions", "BoundaryRevisionId"),
            ("ProviderPackageVersions", "PreviousVersionId"),
            ("ProviderCatalogContextSnapshots", "ImpactReviewId"),
            ("ProviderHostingAssignments", "HostingScopeRevisionId"),
            ("MissionProviderRelationshipReviews", "AuthorizationRevisionId"),
            ("MissionProviderRelationshipReviews", "BoundaryRevisionId"),
            ("CapabilityAdoptionSnapshots", "ContextSnapshotId"),
            ("ProviderFindingEvidences", "FindingId"),
            ("ProviderFindingReviews", "FindingId")
        })
            Index(table, $"ProviderId,OfferingId,{column}");
        Index("ProviderHostingAssignments", "TargetTenantId");
        Index("MissionProviderRelationshipReviews", "ProviderId,OfferingId,TenantId,SystemId,AssignmentId");
        Index("CapabilityAdoptionSnapshots", "ProviderId,OfferingId,TenantId,SystemId,AssignmentId");
        Index("ProviderClaimReviews", "PackageId");
        Index("ProviderClaimReviews", "CandidateId");
        Index("ProviderPackageEnrichments", "PackageId");
        return scripts;

        void Index(string table, string fields, bool unique = false)
        {
            var name = $"IX_{table}_{fields.Replace(',', '_')}";
            var kind = unique ? "UNIQUE " : "";
            scripts.Add(sqlServer
                ? $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'{name}' AND object_id=OBJECT_ID(N'dbo.{table}')) CREATE {kind}INDEX [{name}] ON {Table(table)} ({fields});"
                : $"CREATE {kind}INDEX IF NOT EXISTS [{name}] ON {Table(table)} ({fields});");
        }
    }
}

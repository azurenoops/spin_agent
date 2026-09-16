using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Dtos.Dashboard;

public record EmassWorkflowStatus(
    string SystemId,
    EmassWorkflowOverallStatus OverallStatus,
    DateTimeOffset? LastExportedAt,
    DateTimeOffset? LastSyncedAt,
    int UnresolvedConflictCount,
    IReadOnlyList<EmassExportCategorySummary> ExportSummary,
    EmassReadinessSummary ReadinessStatus);

public record EmassExportCategorySummary(
    string Category,
    int ExportedCount,
    int PendingCount,
    DateTimeOffset? LastExportedAt);

public record EmassReadinessSummary(
    bool IsReady,
    int BlockingGapCount,
    int AdvisoryGapCount);

public record EmassExportReadinessResult(
    string SystemId,
    bool IsReady,
    IReadOnlyList<ReadinessGap> Gaps,
    DateTimeOffset CheckedAt);

public record ReadinessGap(
    string FieldName,
    string Description,
    ReadinessGapSeverity Severity,
    string? FixUrl);

public record EmassConflictDto(
    string Id,
    string EntityType,
    string? EntityId,
    string FieldName,
    string? SpinValue,
    string? EmassValue,
    ConflictStatus ConflictStatus,
    DateTimeOffset DetectedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy);

public record ResolveConflictRequest(
    ConflictStatus Resolution,
    string? Notes = null);

public record EmassSyncResult(
    string BatchId,
    string SystemId,
    int ConflictsCreated,
    int IdenticalFields,
    int SkippedUnresolved,
    DateTimeOffset SyncedAt);

public enum EmassWorkflowOverallStatus
{
    NeverExported,
    UpToDate,
    PendingExport,
    HasConflicts,
}

public enum ReadinessGapSeverity
{
    Blocking,
    Advisory,
}
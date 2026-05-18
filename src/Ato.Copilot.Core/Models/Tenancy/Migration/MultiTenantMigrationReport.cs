namespace Ato.Copilot.Core.Models.Tenancy.Migration;

/// <summary>
/// Result of a single-tenant → multi-tenant migration run. Returned by
/// <c>POST /api/admin/migrate-to-multitenant</c> and the
/// <c>ato-cli tenants migrate-single-to-multi</c> CLI command.
/// See feature 048 spec FR-073..FR-076 and data-model.md §7.
/// </summary>
/// <param name="StartedAt">UTC time the migration began.</param>
/// <param name="CompletedAt">UTC time the migration finished (success or failure).</param>
/// <param name="DefaultTenantId">
/// The single tenant id used to backfill rows whose <c>TenantId</c> was null
/// (i.e., rows pre-dating the migration). All such rows are assigned to this
/// tenant unless overridden by a per-table <see cref="TenantOverride"/>.
/// </param>
/// <param name="Tables">Per-table report.</param>
/// <param name="RlsInstalled">
/// True when SQL Server Row-Level Security policies were successfully
/// installed at the end of the migration. False on SQLite or when the
/// migration was a dry run.
/// </param>
/// <param name="Error">
/// Non-null when the migration aborted; contains a human-readable description
/// of the failing operation. The migration is transactional, so a non-null
/// <c>Error</c> implies all changes were rolled back.
/// </param>
public sealed record MultiTenantMigrationReport(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    Guid DefaultTenantId,
    IReadOnlyList<MigrationTableReport> Tables,
    bool RlsInstalled,
    string? Error);

/// <summary>
/// Per-table migration outcome. See <see cref="MultiTenantMigrationReport"/>.
/// </summary>
/// <param name="TableName">Database table name (case-sensitive on SQL Server with case-sensitive collation).</param>
/// <param name="TotalRows">Total row count after migration.</param>
/// <param name="RowsAssignedByOverride">Rows whose <c>TenantId</c> was set via a <see cref="TenantOverride"/>.</param>
/// <param name="RowsAssignedToDefault">Rows whose <c>TenantId</c> was set to the default tenant id.</param>
/// <param name="RowsAlreadyAssigned">Rows that already had a non-empty <c>TenantId</c> (left untouched).</param>
public sealed record MigrationTableReport(
    string TableName,
    long TotalRows,
    long RowsAssignedByOverride,
    long RowsAssignedToDefault,
    long RowsAlreadyAssigned);

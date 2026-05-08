namespace Ato.Copilot.Core.Models.Tenancy.Migration;

/// <summary>
/// Per-row override directing the single → multi-tenant migration to assign a
/// specific row (matched by <see cref="TableName"/> + <see cref="PrimaryKey"/>)
/// to a specific tenant id, instead of the deployment-wide default tenant id.
/// Loaded from a JSON file passed to the <c>ato-cli tenants migrate-single-to-multi</c>
/// command via <c>--overrides</c>.
/// See feature 048 spec FR-074 and data-model.md §7.
/// </summary>
/// <example>
/// JSON shape (one entry per row):
/// <code>
/// [
///   { "TableName": "RegisteredSystems", "PrimaryKey": "00000000-…", "TenantId": "11111111-…" },
///   { "TableName": "ComplianceFindings", "PrimaryKey": "abc-123",   "TenantId": "22222222-…" }
/// ]
/// </code>
/// </example>
public sealed record TenantOverride(
    string TableName,
    string PrimaryKey,
    Guid TenantId);

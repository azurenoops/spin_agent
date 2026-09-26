using System.Data;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Workspaces;

public sealed partial class WorkspaceOperationsService
{
    public async Task<SystemComponentPlacementOptions> GetSystemComponentPlacementsAsync(
        Guid tenantId, string systemId, string source, string recordId,
        SystemSecurityCapabilityAccess access, CancellationToken ct)
    {
        ValidateSystemRecordKey(source, "component", recordId);
        recordId = CanonicalRecordId(source, recordId);
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireSystemCapabilityAccessAsync(db, tenantId, systemId, access, false, ct);
        var material = await LoadSystemCapabilityMaterialAsync(db, tenantId, systemId, ct, false);
        var item = RequirePlacementComponent(material, source, recordId);
        return await ProjectPlacementOptionsAsync(db, tenantId, item, material, access, ct);
    }

    public Task<SystemComponentPlacementResult> AssignSystemComponentPlacementAsync(
        Guid tenantId, string systemId, string source, string recordId,
        AssignSystemComponentPlacementRequest request, SystemSecurityCapabilityAccess access, string actor, CancellationToken ct) =>
        MutateSystemPlacementAsync(tenantId, systemId, source, recordId, request.SourceRevision,
            request.RelationshipRevision, request.BoundaryId, null, null, access, actor, ct);

    public Task<SystemComponentPlacementResult> UnassignSystemComponentPlacementAsync(
        Guid tenantId, string systemId, string source, string recordId, string placementId,
        UnassignSystemComponentPlacementRequest request, SystemSecurityCapabilityAccess access, string actor, CancellationToken ct) =>
        MutateSystemPlacementAsync(tenantId, systemId, source, recordId, request.SourceRevision,
            request.RelationshipRevision, null, placementId, request.PlacementRevision, access, actor, ct);

    private async Task<SystemComponentPlacementResult> MutateSystemPlacementAsync(
        Guid tenantId, string systemId, string source, string recordId, string sourceRevision,
        string relationshipRevision, string? boundaryId, string? placementId, string? placementRevision,
        SystemSecurityCapabilityAccess access, string actor, CancellationToken ct)
    {
        ValidateSystemRecordKey(source, "component", recordId);
        recordId = CanonicalRecordId(source, recordId);
        if (string.IsNullOrWhiteSpace(sourceRevision) || sourceRevision.Length > 64
            || string.IsNullOrWhiteSpace(relationshipRevision) || relationshipRevision.Length > 64
            || string.IsNullOrWhiteSpace(actor) || actor.Length > 200
            || placementId is null && (string.IsNullOrWhiteSpace(boundaryId) || boundaryId.Length > 64)
            || placementId is not null && (string.IsNullOrWhiteSpace(placementId) || placementId.Length > 100
                || string.IsNullOrWhiteSpace(placementRevision) || placementRevision.Length > 64))
            throw new ArgumentException("A target, current revisions, and actor are required.");
        await using var strategyDb = await factory.CreateDbContextAsync(ct);
        try
        {
            return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await using var transaction = db.Database.IsRelational()
                    ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
                await RequireSystemCapabilityAccessAsync(db, tenantId, systemId, access, true, ct);
                var material = await LoadSystemCapabilityMaterialAsync(db, tenantId, systemId, ct, false);
                var item = RequirePlacementComponent(material, source, recordId);
                if (item.SourceRevision != sourceRevision)
                    throw new SystemCapabilityConflictException("STALE_SOURCE", "The component source changed. Refresh placement options.");
                if (material.RelationshipRevision != relationshipRevision)
                    throw new SystemCapabilityConflictException("STALE_RELATIONSHIP", "System relationships changed. Refresh placement options.");
                var options = await ProjectPlacementOptionsAsync(db, tenantId, item, material, access, ct);
                var removing = placementId is not null;
                string targetId;
                string targetBoundary;
                if (removing)
                {
                    var placement = options.Placements.SingleOrDefault(x => x.Id == placementId)
                        ?? throw new KeyNotFoundException("Placement not found for this component in the selected system.");
                    if (placement.Revision != placementRevision)
                        throw new SystemCapabilityConflictException("STALE_PLACEMENT", "The reviewed placement changed.");
                    if (!placement.CanUnassign)
                        throw new SystemCapabilityConflictException("PLACEMENT_BLOCKED", placement.UnassignBlockedReason!);
                    targetId = placement.Id;
                    targetBoundary = placement.BoundaryId!;
                    if (!await ComponentService.RemoveBoundaryInContextAsync(db, targetId, ct))
                        throw new SystemCapabilityConflictException("STALE_PLACEMENT", "The placement was already removed.");
                }
                else
                {
                    if (!material.Boundaries.Any(x => x.Id == boundaryId))
                        throw new KeyNotFoundException("Boundary not found in the selected system.");
                    if (!options.CanAssignBoundary)
                        throw new SystemCapabilityConflictException("PLACEMENT_BLOCKED", options.AssignBlockedReason!);
                    if (item.Placements.Any(x => x.BoundaryId == boundaryId))
                        throw new SystemCapabilityConflictException("DUPLICATE_PLACEMENT", "This component already has a placement on that boundary.");
                    var result = await ComponentService.AssignBoundaryInContextAsync(db, systemId, boundaryId!, recordId,
                        source == "provider" ? "CSP" : "Organization", true, null, null, actor, ct);
                    if (result.Dto is null)
                        throw new SystemCapabilityConflictException("PLACEMENT_BLOCKED",
                            $"The component assignment service rejected placement: {result.Error}.");
                    targetId = result.Dto.AssignmentId;
                    targetBoundary = boundaryId!;
                }
                db.DashboardActivities.Add(new()
                {
                    RegisteredSystemId = systemId, Actor = actor,
                    EventType = removing ? "BoundaryComponentRemoved" : "BoundaryComponentAssigned",
                    Summary = removing ? "Removed the reviewed component boundary placement." : "Assigned the reviewed component to a boundary.",
                    RelatedEntityType = "BoundaryComponentAssignment", RelatedEntityId = targetId
                });
                await db.SaveChangesAsync(ct);
                var after = await LoadSystemCapabilityMaterialAsync(db, tenantId, systemId, ct, false);
                if (transaction is not null) await transaction.CommitAsync(ct);
                return new SystemComponentPlacementResult(source, recordId, targetId, targetBoundary,
                    removing ? "Unassigned" : "Assigned", after.RelationshipRevision);
            });
        }
        catch (Exception ex) when (IsPlacementConcurrency(ex))
        {
            throw new SystemCapabilityConflictException("STALE_PLACEMENT", "A concurrent placement write conflicted. Refresh before retrying.");
        }
    }

    private static bool IsPlacementConcurrency(Exception exception) =>
        exception is DbUpdateConcurrencyException
        || exception is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 5 or 6 }
        || exception is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 1555 or 2067 }
        || exception is Microsoft.Data.SqlClient.SqlException { Number: 1205 or 2601 or 2627 or 3960 }
        || exception is DbUpdateException { InnerException: { } inner } && IsPlacementConcurrency(inner);

    private static SystemSecurityCapabilityItem RequirePlacementComponent(
        SystemCapabilityMaterial material, string source, string recordId) =>
        material.Components.SingleOrDefault(x => x.Source == source && x.RecordId == recordId && (x.IsApplied || x.IsAvailable))
        ?? throw new KeyNotFoundException("Component not found in this system's accessible inventory.");

    private static async Task<SystemComponentPlacementOptions> ProjectPlacementOptionsAsync(AtoCopilotContext db,
        Guid tenantId, SystemSecurityCapabilityItem item, SystemCapabilityMaterial material,
        SystemSecurityCapabilityAccess access, CancellationToken ct)
    {
        var boundaryIds = material.Boundaries.Select(x => x.Id).ToArray();
        var providerId = item.Source == "provider" ? Guid.Parse(item.RecordId) : (Guid?)null;
        var removableIds = await db.BoundaryComponentAssignments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && boundaryIds.Contains(x.AuthorizationBoundaryDefinitionId)
                && (item.Source == "local" ? x.SystemComponentId == item.RecordId : x.CspInheritedComponentId == providerId))
            .Select(x => x.Id).ToListAsync(ct);
        var denied = access.CanManage ? null : "Current selected-system management permission is required.";
        var blocked = denied ?? (!item.IsAvailable ? "The source is unavailable for new placement."
            : item.Source == "local" && item.ComponentType == "Person" ? "Local Person components use system assignments, not boundary placement."
            : material.Boundaries.Count == 0 ? "Create an authorization boundary before placing this component." : null);
        return new(item.Source, item.RecordId, item.SourceRevision, material.RelationshipRevision, blocked is null,
            blocked, material.Boundaries, item.Placements.Select(p =>
            {
                var reason = denied ?? (removableIds.Contains(p.Id) ? null
                    : p.State == "Unassigned" ? "There is no assignment to remove."
                    : "System-wide and legacy assignments must be changed through their component assignment workflow.");
                return new SystemComponentPlacementOption(p.Id, p.BoundaryId, p.BoundaryName, p.State,
                    p.Revision, reason is null, reason);
            }).ToArray());
    }
}

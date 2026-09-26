using System.Data;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Workspaces;

public sealed partial class WorkspaceOperationsService
{
    private sealed record SystemSetupIntent(string Kind, IReadOnlyList<SystemCapabilitySelection> Selections,
        string? RemovalRelationshipRevision = null);
    private sealed record SystemSetupPlan(string BaselineRevision, string RelationshipRevision, string DependencyRevision,
        IReadOnlyList<SystemCapabilityPlannedWrite> Writes, IReadOnlyDictionary<string, string[]> Controls);

    public Task<PreparedSystemCapabilityOperation> PrepareSystemCapabilitySetupAsync(
        Guid tenantId, string systemId, PrepareSystemCapabilitySetupRequest request,
        SystemSecurityCapabilityAccess access, CancellationToken ct) =>
        PrepareSystemOperationAsync(tenantId, systemId, request.IdempotencyKey,
            new("Setup", NormalizeSelections(request.Selections)), null, access, ct);

    public Task<PreparedSystemCapabilityOperation> PrepareSystemCapabilityRemovalAsync(
        Guid tenantId, string systemId, string source, string recordId,
        PrepareSystemCapabilityRemovalRequest request, SystemSecurityCapabilityAccess access, CancellationToken ct) =>
        PrepareSystemOperationAsync(tenantId, systemId, request.IdempotencyKey,
            new("Removal", NormalizeSelections([new(source, recordId, request.SourceRevision, [], [])]), request.RelationshipRevision),
            request.RelationshipRevision, access, ct);

    private async Task<PreparedSystemCapabilityOperation> PrepareSystemOperationAsync(
        Guid tenantId, string systemId, string key, SystemSetupIntent intent, string? relationshipRevision,
        SystemSecurityCapabilityAccess access, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new ArgumentException("Idempotency key must contain 1–100 characters.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireSystemCapabilityAccessAsync(db, tenantId, systemId, access, true, ct);
        var json = JsonSerializer.Serialize(intent);
        var existing = await db.CapabilitySetupOperations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            EnsureSystemIntent(existing, systemId, json);
            return new(ProjectSystemOperation(existing), true);
        }
        var data = await LoadSystemCapabilityMaterialAsync(db, tenantId, systemId, ct, false);
        if (intent.Kind == "Removal" && relationshipRevision != data.RelationshipRevision)
            throw new SystemCapabilityConflictException("STALE_RELATIONSHIP", "System relationships changed. Review removal again.");
        ValidateSystemSelections(intent, data);
        var writes = new List<SystemCapabilityPlannedWrite>();
        var controls = new Dictionary<string, string[]>();
        var implementationIds = await db.ControlImplementations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId).Select(x => x.ControlId).ToArrayAsync(ct);
        string CapabilityLabel(string source, string id)
        {
            var item = data.Capabilities.Single(x => x.Source == source && x.RecordId == id);
            return $"\"{item.Name}\" ({item.SourceName})";
        }
        string PlacementLabel(string source, string id, string? boundary)
        {
            var item = data.Components.Single(x => x.Source == source && x.RecordId == id);
            var label = $"\"{item.Name}\" ({item.SourceName})";
            return boundary is null ? $"Assign {label} system-wide"
                : $"Place {label} on boundary \"{data.Boundaries.Single(x => x.Id == boundary).Name}\"";
        }
        void Plan(string kind, string source, string id, string? component, string? boundary, bool exists,
            IReadOnlyList<string>? controlIds = null, IReadOnlyList<string>? narrativeTypes = null)
        {
            var writeId = $"{kind}:{source}:{id}:{component}:{boundary}";
            if (writes.Any(x => x.WriteId == writeId)) return;
            var label = kind switch
            {
                "system-link" => $"Apply organization capability {CapabilityLabel(source, id)} to this system",
                "system-unlink" => $"Unlink organization capability {CapabilityLabel(source, id)} from this system",
                "subscription" => $"Subscribe this system to {CapabilityLabel(source, id)}",
                "unsubscribe" => $"Unsubscribe this system from {CapabilityLabel(source, id)}",
                "support-link" => $"Link organization capability {CapabilityLabel("local", component!)} as support for {CapabilityLabel(source, id)}",
                "component-placement" => PlacementLabel(source, id, boundary),
                "control-implementation" => $"Create control implementation for {id}",
                "responsibility-reconciliation" => "Reconcile control responsibilities for this system",
                "narrative-change" => $"Queue Policy and Technical narrative change review for {CapabilityLabel(source, id)}",
                _ => throw new InvalidOperationException($"Unsupported system setup write kind: {kind}")
            };
            writes.Add(new(kind, writeId, source, id, component, boundary, exists, controlIds, narrativeTypes,
                exists ? $"{label} (already present; no write)" : label));
        }
        foreach (var selection in intent.Selections)
        {
            var item = data.Capabilities.Single(x => x.Source == selection.Source && x.RecordId == selection.RecordId);
            controls[$"{selection.Source}:{selection.RecordId}"] = item.ControlIds.ToArray();
            Plan(intent.Kind == "Removal" ? selection.Source == "local" ? "system-unlink" : "unsubscribe"
                : selection.Source == "local" ? "system-link" : "subscription",
                selection.Source, selection.RecordId, null, null, intent.Kind == "Setup" && item.IsApplied);
            if (intent.Kind == "Removal") continue;
            foreach (var support in selection.SupportingCapabilities)
            {
                var local = data.Capabilities.Single(x => x.Source == "local" && x.RecordId == support.RecordId);
                Plan("support-link", selection.Source, selection.RecordId, support.RecordId, null,
                    item.Capabilities.Any(x => x.Source == "local" && x.RecordId == support.RecordId));
                controls[$"local:{local.RecordId}"] = local.ControlIds.ToArray();
            }
            foreach (var placement in selection.Placements)
            {
                var component = data.Components.Single(x => x.Source == placement.Source && x.RecordId == placement.ComponentId);
                Plan("component-placement", placement.Source, placement.ComponentId, placement.ComponentId, placement.BoundaryId,
                    component.Placements.Any(x => x.BoundaryId == placement.BoundaryId
                        && x.State == (placement.BoundaryId is null ? "SystemWide" : "InScope")));
            }
        }
        if (intent.Kind == "Setup")
            foreach (var control in controls.Values.SelectMany(x => x).Distinct().Order())
                Plan("control-implementation", "local", control, null, null, implementationIds.Contains(control));
        if (intent.Selections.Any(x => x.Source == "provider"))
        {
            var baselineId = data.Baseline?.Id;
            var priorControls = await db.Set<CapabilityResponsibilityProjection>().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId
                    && x.ControlBaselineId == baselineId).Select(x => x.ControlId).ToListAsync(ct);
            var reconciled = data.Capabilities.Where(x => x.Source == "provider" && (x.IsApplied
                    || intent.Selections.Any(s => s.Source == "provider" && s.RecordId == x.RecordId)))
                .SelectMany(x => x.ControlIds).Concat(priorControls)
                .Where(x => data.Baseline?.ControlIds.Contains(x, StringComparer.OrdinalIgnoreCase) == true)
                .Distinct().Order().ToArray();
            Plan("responsibility-reconciliation", "provider", systemId, null, null, false, reconciled);
        }
        foreach (var pair in controls.OrderBy(x => x.Key).ToArray())
        {
            var split = pair.Key.IndexOf(':');
            var source = pair.Key[..split];
            var id = pair.Key[(split + 1)..];
            var relevant = intent.Kind == "Removal" ? pair.Value.Intersect(implementationIds).ToArray() : pair.Value;
            controls[pair.Key] = relevant;
            if (relevant.Length != 0) Plan("narrative-change", source, id, null, null, false, relevant, ["Policy", "Technical"]);
        }
        var plan = new SystemSetupPlan(data.BaselineRevision, data.RelationshipRevision, SystemDependencyRevision(data), writes, controls);
        var operation = new CapabilitySetupOperation
        {
            TenantId = tenantId, RegisteredSystemId = systemId, IdempotencyKey = key,
            SourceKind = "system", SourceRecordId = systemId,
            SystemIntentJson = json, SystemPlanJson = JsonSerializer.Serialize(plan),
            OutcomesJson = JsonSerializer.Serialize(writes.Select(x =>
                new SetupWriteOutcome(x.WriteKind, x.WriteId, "Pending", null, DateTimeOffset.UtcNow)))
        };
        db.CapabilitySetupOperations.Add(operation);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.CapabilitySetupOperations.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.IdempotencyKey == key, ct);
            if (winner is null) throw;
            EnsureSystemIntent(winner, systemId, json);
            return new(ProjectSystemOperation(winner), true);
        }
        return new(ProjectSystemOperation(operation), false);
    }

    public async Task<SystemCapabilityOperation?> GetSystemCapabilityOperationAsync(
        Guid tenantId, string systemId, Guid operationId, SystemSecurityCapabilityAccess access, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireSystemCapabilityAccessAsync(db, tenantId, systemId, access, true, ct);
        var row = await db.CapabilitySetupOperations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId
                && x.Id == operationId && x.SystemIntentJson != null, ct);
        return row is null ? null : ProjectSystemOperation(row);
    }

    public async Task<SystemCapabilityOperation> CompleteSystemCapabilitySetupAsync(
        Guid tenantId, string systemId, Guid operationId, CompleteSystemCapabilitySetupRequest request,
        SystemSecurityCapabilityAccess access, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 200) throw new ArgumentException("A valid actor is required.");
        var claimId = Guid.NewGuid();
        await using (var claimDb = await factory.CreateDbContextAsync(ct))
        {
            await RequireSystemCapabilityAccessAsync(claimDb, tenantId, systemId, access, true, ct);
            var row = await RequireSystemOperationAsync(claimDb, tenantId, systemId, operationId, ct);
            var projected = ProjectSystemOperation(row);
            if (projected.State == "Completed") return projected;
            if (row.ExecutionClaimId.HasValue && row.ClaimedAt > DateTimeOffset.UtcNow.Subtract(SetupExecutionLease))
                throw new SystemCapabilityConflictException("SETUP_IN_PROGRESS", "Another request is executing this operation.");
            if (row.Revision != request.ExpectedRevision)
                throw new SystemCapabilityConflictException("STALE_OPERATION", "Reload the operation before retrying.");
            await ValidateSystemOperationStateAsync(claimDb, tenantId, systemId, row, ct);
            row.ExecutionClaimId = claimId;
            row.ClaimedAt = row.UpdatedAt = DateTimeOffset.UtcNow;
            row.LastError = null;
            row.Revision++;
            try { await claimDb.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            { throw new SystemCapabilityConflictException("SETUP_IN_PROGRESS", "Another request claimed this operation."); }
        }
        SystemCapabilityPlannedWrite? current = null;
        try
        {
            while (true)
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await RequireSystemCapabilityAccessAsync(db, tenantId, systemId, access, true, ct);
                var row = await RequireSystemOperationAsync(db, tenantId, systemId, operationId, ct);
                if (row.ExecutionClaimId != claimId)
                    throw new SystemCapabilityConflictException("SETUP_IN_PROGRESS", "Execution ownership changed. Reload the operation.");
                var plan = ReadSystemPlan(row);
                var outcomes = CapabilitySetupOutcomeReader.Read(row).ToList();
                current = plan.Writes.FirstOrDefault(write =>
                    !outcomes.Any(x => x.WriteId == write.WriteId && x.State == "Completed"));
                if (current is null)
                {
                    row.RecordState = row.ComponentLinksState = "Completed";
                    row.ExecutionClaimId = null;
                    row.LastError = null;
                    row.Revision++;
                    row.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                    return ProjectSystemOperation(row);
                }
                // Narrative delivery has its own durable receipt and scoped context. Do not hold
                // a reader transaction while that service writes; retries use the same impact ID.
                if (current.WriteKind == "narrative-change")
                {
                    await ValidateSystemOperationStateAsync(db, tenantId, systemId, row, ct);
                    await ApplySystemCapabilityWriteAsync(db, row, current, plan, actor, ct);
                }
                await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    // Each transient retry must re-read durable state, not reuse a rolled-back change tracker.
                    await using var writeDb = await factory.CreateDbContextAsync(ct);
                    await using var transaction = writeDb.Database.IsRelational()
                        ? await writeDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
                    var writeRow = await RequireSystemOperationAsync(writeDb, tenantId, systemId, operationId, ct);
                    if (writeRow.ExecutionClaimId != claimId)
                        throw new SystemCapabilityConflictException("SETUP_IN_PROGRESS", "Execution ownership changed.");
                    var writeOutcomes = CapabilitySetupOutcomeReader.Read(writeRow).ToList();
                    if (writeOutcomes.Any(x => x.WriteId == current.WriteId && x.State == "Completed")) return;
                    var writePlan = ReadSystemPlan(writeRow);
                    await ValidateSystemOperationStateAsync(writeDb, tenantId, systemId, writeRow, ct);
                    if (current.WriteKind != "narrative-change")
                        await ApplySystemCapabilityWriteAsync(writeDb, writeRow, current, writePlan, actor, ct);
                    await writeDb.SaveChangesAsync(ct);
                    var after = await LoadSystemCapabilityMaterialAsync(writeDb, tenantId, systemId, ct, false);
                    writeRow.SystemPlanJson = JsonSerializer.Serialize(writePlan with
                    {
                        BaselineRevision = after.BaselineRevision, RelationshipRevision = after.RelationshipRevision,
                        DependencyRevision = SystemDependencyRevision(after)
                    });
                    SetSetupOutcome(writeOutcomes, current.WriteKind, current.WriteId, "Completed");
                    writeRow.OutcomesJson = JsonSerializer.Serialize(writeOutcomes);
                    writeRow.ClaimedAt = writeRow.UpdatedAt = DateTimeOffset.UtcNow;
                    writeRow.LastError = null;
                    writeRow.Revision++;
                    await writeDb.SaveChangesAsync(ct);
                    if (transaction is not null) await transaction.CommitAsync(ct);
                });
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            using var failureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var failureDb = await factory.CreateDbContextAsync(failureTimeout.Token);
            var row = await RequireSystemOperationAsync(failureDb, tenantId, systemId, operationId, failureTimeout.Token);
            if (row.ExecutionClaimId == claimId)
            {
                var code = ex is SystemCapabilityConflictException conflict ? conflict.Code
                    : ex is OperationCanceledException ? "SETUP_CANCELLED" : "SETUP_WRITE_FAILED";
                var outcomes = CapabilitySetupOutcomeReader.Read(row).ToList();
                if (current is not null) SetSetupOutcome(outcomes, current.WriteKind, current.WriteId, "Failed", code);
                row.OutcomesJson = JsonSerializer.Serialize(outcomes);
                row.LastError = code;
                row.ExecutionClaimId = null;
                row.Revision++;
                row.UpdatedAt = DateTimeOffset.UtcNow;
                await failureDb.SaveChangesAsync(failureTimeout.Token);
            }
            if (ex is SystemCapabilityConflictException or UnauthorizedAccessException or KeyNotFoundException or OperationCanceledException)
                throw;
            throw new SystemCapabilityWriteException(operationId, ex);
        }
    }

    private async Task ApplySystemCapabilityWriteAsync(AtoCopilotContext db, CapabilitySetupOperation operation,
        SystemCapabilityPlannedWrite write, SystemSetupPlan plan, string actor, CancellationToken ct)
    {
        var tenantId = operation.TenantId;
        var systemId = operation.RegisteredSystemId!;
        var id = StableSystemWriteId(operation.Id, write.WriteId);
        switch (write.WriteKind)
        {
            case "system-link":
            case "support-link":
                var capabilityId = write.WriteKind == "support-link" ? write.ComponentId! : write.RecordId;
                var link = await db.SystemCapabilityLinks.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.TenantId == tenantId && x.RegisteredSystemId == systemId && x.SecurityCapabilityId == capabilityId, ct);
                if (link is null)
                {
                    link = new() { Id = id, TenantId = tenantId, RegisteredSystemId = systemId,
                        SecurityCapabilityId = capabilityId, LinkedBy = actor };
                    db.SystemCapabilityLinks.Add(link);
                }
                if (write.WriteKind == "support-link")
                    link.SupportingProviderCapabilityIdsJson = JsonSerializer.Serialize(
                        ReadSupportIds(link).Append(write.RecordId).Distinct().Order().ToArray());
                break;
            case "system-unlink":
                var remove = await db.SystemCapabilityLinks.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.TenantId == tenantId && x.RegisteredSystemId == systemId && x.SecurityCapabilityId == write.RecordId, ct);
                if (remove is null) throw new SystemCapabilityConflictException("STALE_RELATIONSHIP", "The system link changed.");
                db.SystemCapabilityLinks.Remove(remove);
                break;
            case "subscription":
            case "unsubscribe":
                var spellings = ProviderRecordSpellings(Guid.Parse(write.RecordId));
                var subscription = await db.CapabilitySubscriptions.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.RoutingTenantId == tenantId && x.RegisteredSystemId == systemId && spellings.Contains(x.CspInheritedCapabilityId), ct);
                if (subscription is null)
                {
                    if (write.WriteKind == "unsubscribe") throw new SystemCapabilityConflictException("STALE_RELATIONSHIP", "The subscription changed.");
                    subscription = new() { Id = id, RegisteredSystemId = systemId, RoutingTenantId = tenantId,
                        CspInheritedCapabilityId = write.RecordId, RoutingCapabilityId = write.RecordId,
                        SubscribedBy = actor, IsActive = false };
                    db.CapabilitySubscriptions.Add(subscription);
                }
                if (write.WriteKind == "subscription" && !subscription.IsActive)
                {
                    subscription.SubscribedAt = DateTime.UtcNow;
                    subscription.SubscribedBy = actor;
                    foreach (var confirmation in await db.Set<CapabilityResponsibilityConfirmation>().IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId && x.SubscriptionId == subscription.Id && x.IsCurrent).ToListAsync(ct))
                        confirmation.IsCurrent = false;
                }
                var active = write.WriteKind == "subscription";
                if (subscription.IsActive != active)
                    CapabilityResponsibilityService.AddSubscriptionActivity(db, systemId, subscription.Id, actor,
                        active ? "CapabilitySubscribed" : "CapabilityUnsubscribed",
                        $"{(active ? "Subscribed to" : "Unsubscribed from")} CSP capability: {write.RecordId}");
                subscription.IsActive = active;
                break;
            case "component-placement":
                if (write.BoundaryId is null)
                {
                    if (!await db.ComponentSystemAssignments.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId
                            && x.RegisteredSystemId == systemId && x.SystemComponentId == write.ComponentId
                            && x.AuthorizationBoundaryDefinitionId == null, ct))
                        db.ComponentSystemAssignments.Add(new() { Id = id, TenantId = tenantId,
                            RegisteredSystemId = systemId, SystemComponentId = write.ComponentId!, CreatedBy = actor });
                }
                else
                {
                    var providerId = write.Source == "provider" ? Guid.Parse(write.ComponentId!) : (Guid?)null;
                    var placement = await db.BoundaryComponentAssignments.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                        x.TenantId == tenantId && x.AuthorizationBoundaryDefinitionId == write.BoundaryId
                        && (write.Source == "local" ? x.SystemComponentId == write.ComponentId : x.CspInheritedComponentId == providerId), ct);
                    if (placement is null)
                    {
                        var assigned = await ComponentService.AssignBoundaryInContextAsync(db, systemId,
                            write.BoundaryId, write.ComponentId!, write.Source == "provider" ? "CSP" : "Organization",
                            true, null, null, actor, ct, id);
                        if (assigned.Dto is null)
                            throw new SystemCapabilityConflictException("PLACEMENT_BLOCKED",
                                $"The component assignment service rejected placement: {assigned.Error}.");
                    }
                    else if (!placement.IsInScope)
                        throw new SystemCapabilityConflictException("STALE_RELATIONSHIP",
                            "An excluded placement must be reviewed in the boundary workflow before adding it.");
                }
                break;
            case "control-implementation":
                if (!await db.ControlImplementations.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId
                        && x.RegisteredSystemId == systemId && x.ControlId == write.RecordId, ct))
                    db.ControlImplementations.Add(new() { Id = id, TenantId = tenantId, RegisteredSystemId = systemId,
                        ControlId = write.RecordId, AuthoredBy = actor });
                break;
            case "responsibility-reconciliation":
                await (responsibilityService ?? throw new InvalidOperationException("Responsibility service is unavailable."))
                    .ReconcileSetupAsync(db, systemId, actor, ct);
                break;
            case "narrative-change":
                await (narrativeChanges ?? throw new InvalidOperationException("Narrative change service is unavailable."))
                    .QueueAsync(new(tenantId, systemId, plan.Controls[$"{write.Source}:{write.RecordId}"], ["Policy", "Technical"],
                        write.Source == "local" ? "OrganizationCapability" : "CspCapability", write.RecordId, actor,
                        $"setup:{operation.Id:D}:{Hash(write.WriteId)}"), ct);
                break;
            default: throw new InvalidDataException("Stored setup write kind is not supported.");
        }
    }

    private static string StableSystemWriteId(Guid operationId, string writeId) =>
        new Guid(Convert.FromHexString(Hash($"{operationId:D}:{writeId}")[..32])).ToString("D");

    private async Task ValidateSystemOperationStateAsync(AtoCopilotContext db, Guid tenantId,
        string systemId, CapabilitySetupOperation row, CancellationToken ct)
    {
        var current = await LoadSystemCapabilityMaterialAsync(db, tenantId, systemId, ct, false);
        var plan = ReadSystemPlan(row);
        if (current.BaselineRevision != plan.BaselineRevision)
            throw new SystemCapabilityConflictException("STALE_BASELINE", "The system baseline or allocation changed. Prepare a new review.");
        if (current.RelationshipRevision != plan.RelationshipRevision)
            throw new SystemCapabilityConflictException("STALE_RELATIONSHIP", "System relationships changed. Prepare a new review.");
        if (SystemDependencyRevision(current) != plan.DependencyRevision)
            throw new SystemCapabilityConflictException("STALE_SOURCE", "A subscribed source changed. Prepare a new review.");
        ValidateSystemSelections(ReadSystemIntent(row), current, executing: true);
    }

    private static string SystemDependencyRevision(SystemCapabilityMaterial data) =>
        Hash(JsonSerializer.Serialize(data.Capabilities.Where(x => x.Source == "provider" && x.IsApplied)
            .OrderBy(x => x.RecordId).Select(x => new { x.RecordId, x.SourceRevision })));

    private static void ValidateSystemSelections(SystemSetupIntent intent, SystemCapabilityMaterial data, bool executing = false)
    {
        foreach (var selection in intent.Selections)
        {
            var item = data.Capabilities.SingleOrDefault(x => x.Source == selection.Source && x.RecordId == selection.RecordId)
                ?? throw new KeyNotFoundException("Selected capability was not found.");
            if (item.SourceRevision != selection.SourceRevision || intent.Kind == "Setup" && !item.IsAvailable)
                throw new SystemCapabilityConflictException("STALE_SOURCE", "Selected source changed or is no longer available.");
            if (intent.Kind == "Removal" && !executing && !item.IsApplied)
                throw new SystemCapabilityConflictException("STALE_RELATIONSHIP", "The capability is no longer applied.");
            var validComponents = item.Components.Select(x => (x.Source, x.RecordId)).ToHashSet();
            foreach (var support in selection.SupportingCapabilities)
            {
                if (selection.Source != "provider") throw new ArgumentException("Only provider capabilities can request supporting local capabilities.");
                var local = data.Capabilities.SingleOrDefault(x => x.Source == "local" && x.RecordId == support.RecordId && x.IsAvailable)
                    ?? throw new KeyNotFoundException("Supporting organization capability was not found.");
                if (local.SourceRevision != support.SourceRevision)
                    throw new SystemCapabilityConflictException("STALE_SOURCE", "Supporting organization capability changed.");
                validComponents.UnionWith(local.Components.Select(x => (x.Source, x.RecordId)));
            }
            foreach (var placement in selection.Placements)
            {
                if (!validComponents.Contains((placement.Source, placement.ComponentId)))
                    throw new ArgumentException("Placement must reference an actual contributor of the selected or supporting capability.");
                if (placement.Source == "provider" && placement.BoundaryId is null)
                    throw new ArgumentException("Provider component placement requires an existing boundary; otherwise leave it unassigned.");
                if (placement.BoundaryId is not null && !data.Boundaries.Any(x => x.Id == placement.BoundaryId))
                    throw new KeyNotFoundException("Placement boundary does not belong to this system.");
                var component = data.Components.Single(x => x.Source == placement.Source && x.RecordId == placement.ComponentId);
                if (placement.Source == "local" && component.ComponentType == "Person" && placement.BoundaryId is not null)
                    throw new ArgumentException("Local Person components require system-wide placement, not a boundary.");
                if (component.Placements.Any(x => x.BoundaryId == placement.BoundaryId && x.State == "Excluded"))
                    throw new SystemCapabilityConflictException("STALE_RELATIONSHIP", "An excluded component needs an explicit boundary review.");
            }
        }
    }

    private static SystemCapabilitySelection[] NormalizeSelections(IReadOnlyList<SystemCapabilitySelection> selections)
    {
        if (selections is null || selections.Count is < 1 or > 50)
            throw new ArgumentException("Select between 1 and 50 capabilities.");
        var result = selections.Select(selection =>
        {
            if (selection is null) throw new ArgumentException("Selection cannot be null.");
            ValidateSystemRecordKey(selection.Source, "capability", selection.RecordId);
            if (selection.SourceRevision is null || selection.SourceRevision.Length is < 1 or > 64
                || selection.Placements is null || selection.Placements.Count > 100
                || selection.SupportingCapabilities is null || selection.SupportingCapabilities.Count > 50)
                throw new ArgumentException("Provide source revisions and bounded placements/supporting capabilities.");
            foreach (var p in selection.Placements)
            {
                if (p is null) throw new ArgumentException("Placement cannot be null.");
                ValidateSystemRecordKey(p.Source, "component", p.ComponentId);
                if (p.BoundaryId is not null && (string.IsNullOrWhiteSpace(p.BoundaryId) || p.BoundaryId.Length > 64))
                    throw new ArgumentException("Invalid boundary ID.");
            }
            foreach (var s in selection.SupportingCapabilities)
                if (s is null || string.IsNullOrWhiteSpace(s.RecordId) || s.RecordId.Length > 64
                    || string.IsNullOrWhiteSpace(s.SourceRevision) || s.SourceRevision.Length > 64)
                    throw new ArgumentException("Supporting capability identity and revision are required.");
            return selection with
            {
                RecordId = CanonicalRecordId(selection.Source, selection.RecordId),
                Placements = selection.Placements.Select(x => x with { ComponentId = CanonicalRecordId(x.Source, x.ComponentId) })
                    .Distinct().OrderBy(x => x.Source).ThenBy(x => x.ComponentId).ThenBy(x => x.BoundaryId).ToArray(),
                SupportingCapabilities = selection.SupportingCapabilities.Distinct().OrderBy(x => x.RecordId).ToArray()
            };
        }).OrderBy(x => x.Source).ThenBy(x => x.RecordId).ToArray();
        if (result.DistinctBy(x => (x.Source, x.RecordId)).Count() != result.Length
            || result.Any(x => x.SupportingCapabilities.DistinctBy(s => s.RecordId).Count() != x.SupportingCapabilities.Count))
            throw new ArgumentException("Duplicate capability selections are not allowed.");
        return result;
    }

    private static void EnsureSystemIntent(CapabilitySetupOperation row, string systemId, string intent)
    {
        if (row.RegisteredSystemId != systemId || row.SystemIntentJson != intent)
            throw new SystemCapabilityConflictException("SETUP_INTENT_CONFLICT", "Idempotency key belongs to different setup intent.");
    }

    private static async Task<CapabilitySetupOperation> RequireSystemOperationAsync(AtoCopilotContext db, Guid tenantId,
        string systemId, Guid operationId, CancellationToken ct) =>
        await db.CapabilitySetupOperations.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TenantId == tenantId
            && x.RegisteredSystemId == systemId && x.Id == operationId && x.SystemIntentJson != null, ct)
            ?? throw new KeyNotFoundException("Setup operation was not found.");

    private static SystemSetupIntent ReadSystemIntent(CapabilitySetupOperation row) =>
        JsonSerializer.Deserialize<SystemSetupIntent>(row.SystemIntentJson ?? "")
            ?? throw new InvalidDataException("Stored system setup intent is invalid.");
    private static SystemSetupPlan ReadSystemPlan(CapabilitySetupOperation row) =>
        JsonSerializer.Deserialize<SystemSetupPlan>(row.SystemPlanJson ?? "")
            ?? throw new InvalidDataException("Stored system setup plan is invalid.");

    private static SystemCapabilityOperation ProjectSystemOperation(CapabilitySetupOperation row)
    {
        var intent = ReadSystemIntent(row);
        var plan = ReadSystemPlan(row);
        var outcomes = CapabilitySetupOutcomeReader.Read(row);
        var state = row.RecordState == "Completed" && row.ComponentLinksState == "Completed" ? "Completed"
            : row.ExecutionClaimId.HasValue && row.ClaimedAt > DateTimeOffset.UtcNow.Subtract(SetupExecutionLease) ? "InProgress"
            : outcomes.Any(x => x.State is "Completed" or "Failed") ? "Partial" : "Prepared";
        return new(row.Id, row.IdempotencyKey, row.TenantId, row.RegisteredSystemId!, intent.Kind, state,
            row.Revision, intent.Selections, plan.Writes, outcomes, row.LastError, row.CreatedAt, row.UpdatedAt);
    }
}

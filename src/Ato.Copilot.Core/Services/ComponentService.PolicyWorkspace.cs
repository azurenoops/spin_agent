using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services;

public partial class ComponentService
{
    private static void GuardPolicySourceType(SystemComponent source, CreateComponentRequest request)
    {
        if (source.ComponentType == ComponentType.Policy
            && Enum.TryParse<ComponentType>(request.ComponentType, true, out var requestedType)
            && requestedType != ComponentType.Policy)
            throw new PolicyReferenceWorkflowRequiredException(
                "A policy source cannot be reclassified into another component type. Keep its policy identity; use the system policy workspace to manage references.");
    }

    public async Task<PolicyWorkspaceDto> GetPolicyWorkspaceAsync(Guid tenantId, string systemId,
        PolicyWorkspacePermissions permissions, string? search, string? status, bool? sourceChanged,
        int page, int pageSize, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var system = await PolicySystemAsync(db, tenantId, systemId, ct);
        var sources = await PolicySourcesAsync(db, tenantId, systemId).ToListAsync(ct);
        var assignments = await db.ComponentSystemAssignments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.RegisteredSystemId == systemId).ToListAsync(ct);
        var indirect = await IndirectPolicyIdsAsync(db, tenantId, systemId, ct);
        var items = new List<PolicyReferenceDto>();
        foreach (var source in sources)
        {
            var direct = assignments.Where(a => a.SystemComponentId == source.Id).ToList();
            if (direct.Count == 0 && !indirect.Contains(source.Id) && source.RegisteredSystemId != systemId) continue;
            var current = await ProjectPolicySourceAsync(db, tenantId, systemId, source, direct.Count > 0, ct);
            if (direct.Count > 0)
                items.AddRange(direct.Select(a => ProjectPolicyReference(a, current, permissions.CanAssign)));
            else
                items.Add(ProjectUnassignedPolicy(source, current, indirect.Contains(source.Id)));
        }

        var unfiltered = items.Count;
        IEnumerable<PolicyReferenceDto> filtered = items;
        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(x => x.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                || x.Rationale?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true);
        if (!string.IsNullOrWhiteSpace(status))
            filtered = filtered.Where(x => string.Equals(x.SourceStatus, status, StringComparison.OrdinalIgnoreCase));
        if (sourceChanged.HasValue) filtered = filtered.Where(x => x.SourceChanged == sourceChanged);
        var result = filtered.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id).ToList();
        return new(systemId, system.Name, result.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            result.Count, unfiltered, page, pageSize, permissions);
    }

    public async Task<PolicyLibraryDto> GetPolicyLibraryAsync(Guid tenantId, string systemId,
        string? search, int page, int pageSize, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await PolicySystemAsync(db, tenantId, systemId, ct);
        var query = PolicySourcesAsync(db, tenantId, systemId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term)
                || (c.SubType != null && c.SubType.ToLower().Contains(term))
                || (c.Description != null && c.Description.ToLower().Contains(term)));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = new List<PolicySourceDto>();
        foreach (var row in rows)
            items.Add(await ProjectPolicySourceAsync(db, tenantId, systemId, row,
                await PolicyLinkedAsync(db, tenantId, systemId, row, ct), ct));
        return new(systemId, items, count, page, pageSize);
    }

    public async Task<PolicySourceDto> GetPolicySourceAsync(Guid tenantId, string systemId, string policyId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await PolicySystemAsync(db, tenantId, systemId, ct);
        var source = await PolicySourcesAsync(db, tenantId, systemId).SingleOrDefaultAsync(c => c.Id == policyId, ct)
            ?? throw new KeyNotFoundException();
        return await ProjectPolicySourceAsync(db, tenantId, systemId, source,
            await PolicyLinkedAsync(db, tenantId, systemId, source, ct), ct);
    }

    public async Task<PolicyReferenceDto> CreatePolicyReferenceAsync(Guid tenantId, string systemId,
        CreatePolicyReferenceRequest request, string actor, CancellationToken ct)
    {
        var rationale = ValidatePolicyRationale(request.Rationale);
        if (string.IsNullOrWhiteSpace(request.PolicyId) || string.IsNullOrWhiteSpace(request.ExpectedSourceRevision))
            throw new ArgumentException("policyId and expectedSourceRevision are required.");
        return await ExecutePolicyMutationAsync(async db =>
        {
            await PolicySystemAsync(db, tenantId, systemId, ct);
            var source = await PolicySourcesAsync(db, tenantId, systemId).SingleOrDefaultAsync(c => c.Id == request.PolicyId, ct)
                ?? throw new KeyNotFoundException();
            var linked = await PolicyLinkedAsync(db, tenantId, systemId, source, ct);
            var current = await ProjectPolicySourceAsync(db, tenantId, systemId, source, linked, ct);
            if (current.Revision != request.ExpectedSourceRevision)
                throw new DbUpdateConcurrencyException("The policy source changed. Reload and review it before assigning.");
            if (linked) throw new DbUpdateConcurrencyException("This policy already has an explicit reference in this system.");
            var assignment = new ComponentSystemAssignment
            {
                TenantId = tenantId, RegisteredSystemId = systemId, SystemComponentId = source.Id, CreatedBy = actor,
                PolicyRationale = rationale, PolicyRevision = 1,
                PolicyReferenceKey = $"{systemId}:{source.Id}",
                PolicySourceRevision = current.Revision, PolicySourceCapturedAt = DateTime.UtcNow,
                PolicySourceModifiedAt = source.ModifiedAt,
                PolicySourceSnapshotJson = JsonSerializer.Serialize(current with { AlreadyLinked = true })
            };
            db.ComponentSystemAssignments.Add(assignment);
            AuditPolicyReference(db, assignment, actor, "Created", null);
            return ProjectPolicyReference(assignment, current, true);
        }, ct);
    }

    public async Task<PolicyReferenceDto> UpdatePolicyReferenceAsync(Guid tenantId, string systemId, string referenceId,
        UpdatePolicyReferenceRequest request, string actor, CancellationToken ct)
    {
        var rationale = ValidatePolicyRationale(request.Rationale);
        return await ExecutePolicyMutationAsync(async db =>
        {
            await PolicySystemAsync(db, tenantId, systemId, ct);
            var assignment = await ExactPolicyAssignmentAsync(db, tenantId, systemId, referenceId, ct);
            if (assignment.PolicyRevision != request.ExpectedRevision)
                throw new DbUpdateConcurrencyException("The reference changed. Reload before editing.");
            var previous = assignment.PolicyRationale;
            assignment.PolicyRationale = rationale;
            assignment.PolicyRevision++;
            // A rationale edit never captures or refreshes a source, including legacy rows.
            AuditPolicyReference(db, assignment, actor, "Updated", previous);
            var source = await PolicySourcesAsync(db, tenantId, systemId).SingleAsync(c => c.Id == assignment.SystemComponentId, ct);
            var current = await ProjectPolicySourceAsync(db, tenantId, systemId, source, true, ct);
            return ProjectPolicyReference(assignment, current, true);
        }, ct);
    }

    public async Task RemovePolicyReferenceAsync(Guid tenantId, string systemId, string referenceId,
        int expectedRevision, string actor, CancellationToken ct)
    {
        await ExecutePolicyMutationAsync(async db =>
        {
            await PolicySystemAsync(db, tenantId, systemId, ct);
            var assignment = await ExactPolicyAssignmentAsync(db, tenantId, systemId, referenceId, ct);
            if (assignment.PolicyRevision != expectedRevision)
                throw new DbUpdateConcurrencyException("The reference changed. Reload before unlinking.");
            AuditPolicyReference(db, assignment, actor, "Unlinked", assignment.PolicyRationale);
            db.ComponentSystemAssignments.Remove(assignment);
            return true;
        }, ct);
    }

    private async Task<T> ExecutePolicyMutationAsync<T>(Func<AtoCopilotContext, Task<T>> mutation, CancellationToken ct)
    {
        await using var strategyContext = await _dbFactory.CreateDbContextAsync(ct);
        return await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // Fresh tracking state per retry; serializable protects both source revision and membership.
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var result = await mutation(db);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        });
    }

    public async Task<PolicyReferenceDetailDto> GetPolicyReferenceAsync(Guid tenantId, string systemId, string referenceId,
        bool canManage, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var system = await PolicySystemAsync(db, tenantId, systemId, ct);
        ComponentSystemAssignment? assignment = null;
        var pseudo = referenceId.StartsWith("indirect:", StringComparison.Ordinal)
            || referenceId.StartsWith("legacy:", StringComparison.Ordinal);
        var policyId = pseudo ? referenceId[(referenceId.IndexOf(':') + 1)..]
            : (assignment = await ExactPolicyAssignmentAsync(db, tenantId, systemId, referenceId, ct)).SystemComponentId;
        var source = await PolicySourcesAsync(db, tenantId, systemId).SingleOrDefaultAsync(c => c.Id == policyId, ct)
            ?? throw new KeyNotFoundException();
        var indirect = (await IndirectPolicyIdsAsync(db, tenantId, systemId, ct)).Contains(policyId);
        if (pseudo && (referenceId.StartsWith("indirect:", StringComparison.Ordinal) ? !indirect : source.RegisteredSystemId != systemId))
            throw new KeyNotFoundException();
        var current = await ProjectPolicySourceAsync(db, tenantId, systemId, source,
            await PolicyLinkedAsync(db, tenantId, systemId, source, ct), ct);
        var reference = assignment is null ? ProjectUnassignedPolicy(source, current, indirect)
            : ProjectPolicyReference(assignment, current, canManage);
        var historyRows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Action.StartsWith("PolicyReference.")
                && a.Details.Contains(referenceId)).OrderBy(a => a.Timestamp).ToListAsync(ct);
        var history = historyRows.Select(a => (Log: a, Data: JsonSerializer.Deserialize<PolicyReferenceAudit>(a.Details)!))
            .Where(a => a.Data.SystemId == systemId && a.Data.ReferenceId == referenceId)
            .Select(a => new PolicyReferenceHistoryDto(a.Log.Id, a.Log.Action, a.Log.UserId, a.Log.Timestamp, a.Data.Description)).ToList();
        var impact = new List<string>
        {
            "Unlinking removes only this system's explicit policy reference and rationale. The shared source and other systems' assignments are preserved.",
            "Retained exports, reviewed narratives, control mappings and authorization decisions are not rewritten."
        };
        if (indirect) impact.Add("This policy remains visible through this system's existing capability links; unlinking does not remove those links.");
        if (source.RegisteredSystemId == systemId)
            impact.Add("The system-owned legacy policy remains in the system inventory even when an explicit assignment is removed.");
        if (current.RelatedControls.Count > 0)
            impact.Add($"Related capability mappings remain: {string.Join(", ", current.RelatedControls)}.");
        return new(systemId, system.Name, reference, RetainedPolicySource(assignment), current, current.RelatedControls,
            "Applicability review is not implemented. Library status is not an approval. Assignment does not establish SSP, eMASS or authorization readiness.",
            history, impact);
    }

    private static IQueryable<SystemComponent> PolicySourcesAsync(AtoCopilotContext db, Guid tenantId, string systemId) =>
        db.SystemComponents.AsNoTracking().Where(c => c.TenantId == tenantId && c.ComponentType == ComponentType.Policy
            && (c.RegisteredSystemId == null || c.RegisteredSystemId == systemId));

    private static async Task<RegisteredSystem> PolicySystemAsync(AtoCopilotContext db, Guid tenantId, string systemId, CancellationToken ct) =>
        await db.RegisteredSystems.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId && s.Id == systemId && s.IsActive, ct)
        ?? throw new KeyNotFoundException();

    private static async Task<ComponentSystemAssignment> ExactPolicyAssignmentAsync(
        AtoCopilotContext db, Guid tenantId, string systemId, string referenceId, CancellationToken ct) =>
        await db.ComponentSystemAssignments.SingleOrDefaultAsync(a => a.Id == referenceId
            && a.TenantId == tenantId && a.RegisteredSystemId == systemId
            && db.SystemComponents.Any(c => c.Id == a.SystemComponentId && c.TenantId == tenantId
                && c.ComponentType == ComponentType.Policy && (c.RegisteredSystemId == null || c.RegisteredSystemId == systemId)), ct)
        ?? throw new KeyNotFoundException();

    private static Task<List<string>> IndirectPolicyIdsAsync(AtoCopilotContext db, Guid tenantId, string systemId, CancellationToken ct) =>
        db.ComponentCapabilityLinks.Where(c => c.TenantId == tenantId
            && db.SystemCapabilityLinks.Any(s => s.TenantId == tenantId && s.RegisteredSystemId == systemId
                && s.SecurityCapabilityId == c.SecurityCapabilityId))
            .Select(c => c.SystemComponentId).Distinct().ToListAsync(ct);

    private static Task<bool> PolicyLinkedAsync(AtoCopilotContext db, Guid tenantId, string systemId, SystemComponent source, CancellationToken ct) =>
        db.ComponentSystemAssignments.AnyAsync(a => a.TenantId == tenantId && a.RegisteredSystemId == systemId
            && a.SystemComponentId == source.Id, ct);

    private static async Task<PolicySourceDto> ProjectPolicySourceAsync(AtoCopilotContext db, Guid tenantId,
        string systemId, SystemComponent source, bool linked, CancellationToken ct)
    {
        var controls = await db.CapabilityControlMappings.Where(m => m.TenantId == tenantId
            && (m.RegisteredSystemId == null || m.RegisteredSystemId == systemId)
            && db.ComponentCapabilityLinks.Any(c => c.TenantId == tenantId
                && c.SystemComponentId == source.Id && c.SecurityCapabilityId == m.SecurityCapabilityId))
            .Select(m => m.ControlId).Distinct().OrderBy(c => c).ToListAsync(ct);
        var date = DateTime.SpecifyKind(source.ModifiedAt ?? source.CreatedAt, DateTimeKind.Utc);
        var label = $"{(source.ModifiedAt.HasValue ? "Updated" : "Created")} {date:yyyy-MM-dd HH:mm:ss} UTC";
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            source.Id, source.Name, source.Description, source.SubType, source.Status, source.Owner,
            source.CreatedAt, source.ModifiedAt, Controls = controls
        }))).ToLowerInvariant();
        return new(source.Id, source.Name, source.Description, source.SubType, source.Status.ToString(), source.Owner,
            fingerprint, label, source.ModifiedAt, linked, controls);
    }

    private static PolicySourceDto? RetainedPolicySource(ComponentSystemAssignment? assignment) =>
        assignment?.PolicySourceSnapshotJson is { } json ? JsonSerializer.Deserialize<PolicySourceDto>(json) : null;

    private static PolicyReferenceDto ProjectPolicyReference(ComponentSystemAssignment assignment, PolicySourceDto current, bool canManage)
    {
        var retained = RetainedPolicySource(assignment);
        return new(assignment.Id, assignment.SystemComponentId, retained?.Name ?? current.Name, assignment.PolicyRationale,
            retained?.VersionLabel, current.Status, retained is not null && assignment.PolicySourceRevision != current.Revision,
            retained is null ? "LegacyUnretained" : "Retained", assignment.PolicyRevision, canManage, canManage,
            canManage ? null : "System management permission is required.");
    }

    private static PolicyReferenceDto ProjectUnassignedPolicy(SystemComponent source, PolicySourceDto current, bool indirect) =>
        new($"{(indirect ? "indirect" : "legacy")}:{source.Id}", source.Id, current.Name, null, null,
            current.Status, false, indirect ? "Indirect" : "LegacyUnretained", 0, false, false,
            indirect ? "This policy applies through existing capability links, not an explicit assignment."
                : "This is a system-owned legacy policy, not an explicit assignment.");

    private static string ValidatePolicyRationale(string? rationale)
    {
        var value = rationale?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 500)
            throw new ArgumentException("Rationale must contain 1–500 trimmed characters.");
        return value;
    }

    private static void AuditPolicyReference(AtoCopilotContext db, ComponentSystemAssignment assignment, string actor,
        string action, string? previousRationale)
    {
        db.AuditLogs.Add(new AuditLogEntry
        {
            TenantId = assignment.TenantId, UserId = actor, Action = "PolicyReference." + action,
            AffectedResources = [assignment.RegisteredSystemId, assignment.Id, assignment.SystemComponentId],
            Details = JsonSerializer.Serialize(new PolicyReferenceAudit(assignment.RegisteredSystemId, assignment.Id,
                assignment.SystemComponentId, assignment.PolicySourceRevision, assignment.PolicyRevision,
                previousRationale, assignment.PolicyRationale, $"{action} system policy reference; source and reviewed narratives are unchanged."))
        });
    }

    private sealed record PolicyReferenceAudit(string SystemId, string ReferenceId, string PolicyId, string? SourceRevision,
        int Revision, string? PreviousRationale, string? Rationale, string Description);
}

public sealed class PolicyReferenceWorkflowRequiredException(string message) : InvalidOperationException(message);

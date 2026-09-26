using System.Text.Json;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services;

public sealed partial class CapabilityResponsibilityService
{
    private async Task ApplyAsync(State state, string actor, string reason, CancellationToken ct,
        IReadOnlySet<string>? controlFilter = null)
    {
        if (state.Baseline is null) return;
        var baseline = state.Baseline;
        var controls = baseline.ControlIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = state.Sources.SelectMany(s => SourceControls(state, s)).Where(controls.Contains)
            .Concat(state.Projections.Where(p => p.ControlBaselineId == baseline.Id).Select(p => p.ControlId))
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(c => controlFilter is null || controlFilter.Contains(c)).ToArray();
        foreach (var control in candidates)
            ReconcileControl(state, control, actor, reason);
        await db.SaveChangesAsync(ct);
        var rows = await db.ControlInheritances.Where(i => i.TenantId == TenantId && i.ControlBaselineId == baseline.Id).ToListAsync(ct);
        baseline.InheritedControls = rows.Count(i => controls.Contains(i.ControlId) && i.InheritanceType == InheritanceType.Inherited);
        baseline.SharedControls = rows.Count(i => controls.Contains(i.ControlId) && i.InheritanceType == InheritanceType.Shared);
        baseline.CustomerControls = rows.Count(i => controls.Contains(i.ControlId) && i.InheritanceType == InheritanceType.Customer);
        await db.SaveChangesAsync(ct);
    }

    private void ReconcileControl(State state, string control, string actor, string reason)
    {
        var baseline = state.Baseline!;
        var contributors = state.Sources.Where(s => Contributes(state, s, control)).ToArray();
        var confirmations = contributors.Select(s => Confirmation(state, s, control)).ToArray();
        var resolved = contributors.Length > 0 && contributors.All(s => SourceState(state, s, control) == "Ready")
            && confirmations.Select(c => Allocation(c!)).Distinct().Count() == 1;
        var desired = resolved ? Allocation(confirmations[0]!) : null;
        var existing = baseline.Inheritances.SingleOrDefault(i => i.ControlId.Equals(control, StringComparison.OrdinalIgnoreCase));
        var projection = state.Projections.SingleOrDefault(p => p.ControlBaselineId == baseline.Id && p.ControlId == control);
        if (projection is null)
        {
            projection = new() { TenantId = TenantId, RegisteredSystemId = state.SystemId,
                ControlBaselineId = baseline.Id, ControlId = control };
            state.Projections.Add(projection);
            db.Set<CapabilityResponsibilityProjection>().Add(projection);
        }
        var owned = existing is not null && existing.Id == projection.ControlInheritanceId
            && existing.DesignationSource == DesignationSource && InheritanceHash(existing) == projection.AppliedHash;
        var previousInheritanceType = existing?.InheritanceType.ToString();
        var currentInheritanceType = existing is not null && !owned ? previousInheritanceType : desired?.InheritanceType;
        var sources = JsonSerializer.Serialize(state.Sources
            .Where(s => SourceControls(state, s).Contains(control, StringComparer.OrdinalIgnoreCase))
            .OrderBy(s => s.Subscription.Id).Select(s => new
            {
                SubscriptionId = s.Subscription.Id, CapabilityId = s.Subscription.CspInheritedCapabilityId,
                ComponentId = SourceProviderId(state, s, control, "CspInheritedComponentId"),
                CspProfileId = SourceProviderId(state, s, control, "CspProfileId"),
                s.Subscription.IsActive, SourceRevision = s.Revision,
                State = SourceState(state, s, control), ConfirmationId = Confirmation(state, s, control)?.Id,
                PreviousInheritanceType = previousInheritanceType, CurrentInheritanceType = currentInheritanceType
            }));
        var semanticSources = JsonSerializer.Serialize(state.Sources
            .Where(s => SourceControls(state, s).Contains(control, StringComparer.OrdinalIgnoreCase))
            .OrderBy(s => s.Subscription.Id).Select(s => new { s.Subscription.Id, s.Subscription.IsActive,
                s.Revision, ConfirmationId = Confirmation(state, s, control)?.Id }));
        var hash = Hash(JsonSerializer.Serialize(new { Sources = semanticSources, desired,
            Preserved = existing is not null && !owned ? InheritanceHash(existing) : null }));
        if (hash == projection.StateHash && (existing is null || owned || projection.ControlInheritanceId is null))
            return;

        if (existing is null && desired is not null)
        {
            existing = new() { TenantId = TenantId, ControlBaselineId = baseline.Id, ControlId = control,
                DesignationSource = DesignationSource, SetBy = actor };
            db.ControlInheritances.Add(existing);
            owned = true;
        }
        if (owned && existing is not null)
        {
            var previous = projection.ControlInheritanceId is null ? null : Allocation(existing);
            if (desired is null)
            {
                db.ControlInheritances.Remove(existing);
                baseline.Inheritances.Remove(existing);
                AddAudit(baseline.Id, existing.Id, control, actor, previous, null);
                projection.ControlInheritanceId = null;
                projection.AppliedHash = null;
            }
            else
            {
                if (previous != desired)
                {
                    existing.InheritanceType = Enum.Parse<InheritanceType>(desired.InheritanceType);
                    existing.Provider = desired.Provider;
                    existing.CustomerResponsibility = desired.CustomerResponsibility;
                    existing.SetBy = actor;
                    existing.SetAt = DateTime.UtcNow;
                    AddAudit(baseline.Id, existing.Id, control, actor, previous, desired);
                }
                projection.ControlInheritanceId = existing.Id;
                projection.AppliedHash = InheritanceHash(existing);
            }
        }
        else
        {
            projection.ControlInheritanceId = null;
            projection.AppliedHash = null;
        }
        projection.StateHash = hash;
        projection.Revision++;
        var impact = new CapabilityResponsibilityImpact
        {
            TenantId = TenantId, RegisteredSystemId = state.SystemId, ControlBaselineId = baseline.Id,
            ControlId = control, ProjectionId = projection.Id, Revision = projection.Revision,
            StateHash = hash, SourcesJson = sources, Reason = reason, Actor = actor
        };
        db.Set<CapabilityResponsibilityImpact>().Add(impact);
        CapabilityResponsibilityRouting.StageImpact(db, impact);
    }

    private async Task<CapabilityResponsibilityResponse> ResponseAsync(State state, bool canConfirm, CancellationToken ct)
    {
        var items = state.Sources.SelectMany(source => SourceControls(state, source).Select(control =>
        {
            var confirmation = Confirmation(state, source, control);
            var sourceAvailable = source.Capability is not null && Available(source.Capability);
            var existing = state.Baseline?.Inheritances.FirstOrDefault(i => i.ControlId.Equals(control, StringComparison.OrdinalIgnoreCase));
            var status = SourceState(state, source, control);
            if (status == "Ready")
            {
                var others = state.Sources.Where(s => Contributes(state, s, control)).ToArray();
                if (others.Any(s => SourceState(state, s, control) != "Ready")) status = "PendingReview";
                else if (others.Select(s => Allocation(Confirmation(state, s, control)!)).Distinct().Count() != 1) status = "ConflictingAllocations";
                else status = existing?.DesignationSource == DesignationSource ? "Applied" : existing is not null ? "PreservedOverride" : "Ready";
            }
            return new CapabilityResponsibilityItem(source.Subscription.Id, Guid.Parse(source.Subscription.CspInheritedCapabilityId),
                source.Capability?.CspInheritedComponentId, source.Capability?.CspInheritedComponent.CspProfileId,
                control, source.Revision, ReviewRevision(state, source), status,
                confirmation?.SourceRevision, confirmation?.ConfirmedBy, confirmation?.ConfirmedAt,
                confirmation is null ? null : Allocation(confirmation), existing?.InheritanceType.ToString(), existing?.DesignationSource,
                sourceAvailable, sourceAvailable ? CspResponsibilitySourceTracker.RedactSnapshot(source.Snapshot) : null,
                confirmation is null ? null : CspResponsibilitySourceTracker.RedactSnapshot(confirmation.SourceSnapshotJson),
                confirmation?.ProviderCoverageVerified, confirmation?.CustomerDutiesReviewed, confirmation?.ReviewNotes);
        })).OrderBy(i => i.ControlId).ThenBy(i => i.SubscriptionId).ToArray();
        var impacts = await db.Set<CapabilityResponsibilityImpact>()
            .Where(i => i.TenantId == state.TenantId && i.RegisteredSystemId == state.SystemId && i.AcknowledgedAt == null)
            .Select(i => new CapabilityResponsibilityImpactResponse(i.Id, i.ControlBaselineId, i.ControlId,
                i.StateHash, i.Reason, i.SourcesJson, i.CreatedAt)).ToListAsync(ct);
        return new(state.SystemId, state.Baseline?.Id, canConfirm, items, impacts);
    }

    private static IEnumerable<string> SourceControls(State state, Source source) =>
        (source.Capability?.MappedNistControlIds ?? [])
        .Concat(state.Confirmations.Where(c => c.SubscriptionId == source.Subscription.Id).Select(c => c.ControlId))
        .Select(c => c.ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool Contributes(State state, Source source, string control)
    {
        if (!source.Subscription.IsActive || source.Capability is null
            || source.Capability.Status == CspInheritedCapabilityStatus.Archived
            || source.Capability.CspInheritedComponent.Status == CspInheritedComponentStatus.Archived)
            return false;
        // Unpublished review work is unresolved, not an authoritative mapping withdrawal.
        return Available(source.Capability)
            ? source.Capability.MappedNistControlIds.Contains(control, StringComparer.OrdinalIgnoreCase)
            : SourceControls(state, source).Contains(control, StringComparer.OrdinalIgnoreCase);
    }

    private static CapabilityResponsibilityConfirmation? Confirmation(State state, Source source, string control) =>
        state.Confirmations.SingleOrDefault(c => c.IsCurrent && c.SubscriptionId == source.Subscription.Id && c.ControlId == control);

    private static Guid? SourceProviderId(State state, Source source, string control, string property)
    {
        if (source.Capability is not null)
            return property == "CspProfileId" ? source.Capability.CspInheritedComponent.CspProfileId : source.Capability.CspInheritedComponentId;
        var confirmation = Confirmation(state, source, control);
        if (confirmation is null) return null;
        using var snapshot = JsonDocument.Parse(confirmation.SourceSnapshotJson);
        return snapshot.RootElement.GetProperty("Component").GetProperty(property).GetGuid();
    }

    private static string SourceState(State state, Source source, string control)
    {
        if (!source.Subscription.IsActive) return "Inactive";
        if (state.Baseline is null) return "MissingBaseline";
        if (!state.Baseline.ControlIds.Contains(control, StringComparer.OrdinalIgnoreCase)) return "OutsideBaseline";
        var confirmation = Confirmation(state, source, control);
        if (source.Capability is null || !Available(source.Capability)
            || !source.Capability.MappedNistControlIds.Contains(control, StringComparer.OrdinalIgnoreCase))
            return "PendingReview";
        if (confirmation is null) return "MissingAllocation";
        return confirmation.SourceRevision != source.Revision ? "PendingReview" : "Ready";
    }

    private static CapabilityResponsibilityAllocation Allocation(CapabilityResponsibilityConfirmation c) =>
        new(c.ControlId, c.InheritanceType.ToString(), c.Provider, c.CustomerResponsibility);
    private static CapabilityResponsibilityAllocation Allocation(ControlInheritance c) =>
        new(c.ControlId, c.InheritanceType.ToString(), c.Provider, c.CustomerResponsibility);
    private static string InheritanceHash(ControlInheritance row) =>
        // Relational datetime columns preserve ticks, not DateTime.Kind.
        Hash(JsonSerializer.Serialize(new { Allocation = Allocation(row), row.DesignationSource, row.SetBy,
            SetAtTicks = row.SetAt.Ticks, row.OrgInheritanceDefaultId }));

    private void AddAudit(string baselineId, string id, string control, string actor,
        CapabilityResponsibilityAllocation? previous, CapabilityResponsibilityAllocation? next) =>
        db.InheritanceAuditEntries.Add(new()
        {
            TenantId = TenantId, ControlBaselineId = baselineId, ControlInheritanceId = id, ControlId = control, Actor = actor,
            PreviousInheritanceType = previous?.InheritanceType, NewInheritanceType = next?.InheritanceType ?? "Undesignated",
            PreviousProvider = previous?.Provider, NewProvider = next?.Provider,
            PreviousCustomerResponsibility = previous?.CustomerResponsibility, NewCustomerResponsibility = next?.CustomerResponsibility,
            ChangeSource = InheritanceChangeSource.SubscriptionReconcile
        });
}

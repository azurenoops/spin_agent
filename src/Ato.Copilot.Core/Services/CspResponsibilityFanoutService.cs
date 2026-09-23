using System.Data;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services;

/// <summary>Processes one leased routing target only after entering its explicit customer scope.</summary>
public sealed class CspResponsibilityFanoutService(AtoCopilotContext db, ITenantContext tenant,
    ISystemWorkspaceAccessService access, INarrativeChangeImpactService narratives, ILoggerFactory logs)
{
    public async Task ProcessAsync(ResponsibilityDeliveryClaim claim, CancellationToken ct = default)
    {
        CapabilityResponsibilityService.RequireBackgroundTenant(tenant);
        if (claim.TenantId != tenant.EffectiveTenantId) throw new UnauthorizedAccessException("Delivery tenant does not match the active scope.");
        var delivery = await db.Set<CapabilityResponsibilityDelivery>().SingleOrDefaultAsync(d =>
            d.Id == claim.Id && d.TenantId == tenant.EffectiveTenantId && d.RegisteredSystemId == claim.SystemId
            && d.LeaseToken == claim.LeaseToken && d.CompletedAt == null, ct)
            ?? throw new DbUpdateConcurrencyException("Responsibility delivery lease is no longer owned.");
        // Claims use set-based updates; a reused context may still track the pre-claim routing row.
        await db.Entry(delivery).ReloadAsync(ct);
        if (delivery.LeaseToken != claim.LeaseToken || delivery.CompletedAt != null || delivery.LeaseUntilUtcTicks <= DateTime.UtcNow.Ticks
            || delivery.TenantId != claim.TenantId || delivery.RegisteredSystemId != claim.SystemId)
            throw new DbUpdateConcurrencyException("Responsibility delivery changed after it was claimed.");
        if (!await db.Tenants.AnyAsync(t => t.Id == claim.TenantId && t.Status == TenantStatus.Active, ct))
        {
            await FinishAsync(delivery, "TenantInactive", false, ct);
            return;
        }
        if (!await db.RegisteredSystems.AnyAsync(s => s.Id == claim.SystemId && s.TenantId == claim.TenantId && s.IsActive, ct))
        {
            await FinishAsync(delivery, "ObsoleteRoute", true, ct);
            return;
        }

        var reconciler = new CapabilityResponsibilityService(db, tenant, access, logs.CreateLogger<CapabilityResponsibilityService>());
        if (delivery.ImpactId is { } impactId)
        {
            if (!await db.Set<CapabilityResponsibilityImpact>().AnyAsync(i => i.Id == impactId
                && i.TenantId == claim.TenantId && i.RegisteredSystemId == claim.SystemId, ct))
            {
                await FinishAsync(delivery, "ObsoleteImpact", true, ct);
                return;
            }
            var dispatcher = new CapabilityResponsibilityImpactDispatcher(db, tenant, reconciler, narratives,
                logs.CreateLogger<CapabilityResponsibilityImpactDispatcher>());
            var result = await dispatcher.DispatchProviderImpactAsync(claim.SystemId, impactId, ct);
            await FinishAsync(delivery, result.Pending == 0 ? "Completed" : result.Deferred.FirstOrDefault()?.Reason ?? "Pending",
                result.Pending == 0, ct);
            return;
        }

        CspResponsibilitySourceEvent? source = null;
        string actor;
        if (delivery.SourceEventId is { } sourceId)
        {
            source = await db.Set<CspResponsibilitySourceEvent>().AsNoTracking().SingleAsync(e => e.Id == sourceId, ct);
            var capabilityId = source.CapabilityId.ToString();
            if (!await db.CapabilitySubscriptions.AnyAsync(s => s.Id == delivery.SubscriptionId
                && s.RegisteredSystemId == claim.SystemId && s.RoutingTenantId == claim.TenantId
                && s.RoutingCapabilityId == capabilityId && s.IsActive, ct))
            {
                await FinishAsync(delivery, "ObsoleteRoute", true, ct);
                return;
            }
            if (await db.Set<CspResponsibilitySourceEvent>().AnyAsync(e => e.CapabilityId == source.CapabilityId && e.Sequence > source.Sequence, ct))
            {
                await FinishAsync(delivery, "Superseded", true, ct);
                return;
            }
            if (string.IsNullOrWhiteSpace(source.Actor))
            {
                await FinishAsync(delivery, "MissingActor", false, ct);
                return;
            }
            actor = source.Actor;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(delivery.BaselineId))
                throw new InvalidDataException("Responsibility delivery has no recorded source event, impact or baseline.");
            actor = await db.ControlBaselines.Where(b => b.TenantId == claim.TenantId && b.RegisteredSystemId == claim.SystemId)
                .Select(b => b.CreatedBy).SingleOrDefaultAsync(ct) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(actor))
            {
                await FinishAsync(delivery, "MissingBaseline", false, ct);
                return;
            }
        }
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var batch = await reconciler.ReconcileBackgroundBatchAsync(claim.SystemId, actor, source?.CapabilityId,
            delivery.BaselineId, delivery.ControlCursor, source is null ? "BaselineChanged" : "ProviderChanged", ct);
        delivery.BaselineId = batch.BaselineId;
        delivery.ControlCursor = batch.Cursor;
        await FinishAsync(delivery, batch.BaselineId is null ? "MissingBaseline" : batch.Complete ? "Completed" : "MoreControls",
            batch.Complete, ct, batch.BaselineId is not null && !batch.Complete);
        await transaction.CommitAsync(ct);
    }

    private async Task FinishAsync(CapabilityResponsibilityDelivery delivery, string outcome, bool complete,
        CancellationToken ct, bool immediate = false)
    {
        delivery.Outcome = outcome;
        delivery.CompletedAt = complete ? DateTimeOffset.UtcNow : null;
        delivery.NextAttemptUtcTicks = immediate ? DateTime.UtcNow.Ticks : DateTime.UtcNow.AddMinutes(1).Ticks;
        delivery.LeaseToken = null;
        delivery.LeaseUntilUtcTicks = 0;
        delivery.Revision++;
        await db.SaveChangesAsync(ct);
        if (!complete && !immediate)
            logs.CreateLogger<CspResponsibilityFanoutService>().LogWarning(
                "Responsibility delivery {DeliveryId} remains deferred: {Outcome}", delivery.Id, outcome);
    }
}

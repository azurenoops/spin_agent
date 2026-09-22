using System.Data;
using System.Security.Cryptography;
using System.Text;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services;

/// <summary>Bounded metadata-only provider expansion, durable routing and competing-worker claims.</summary>
public static class CapabilityResponsibilityRouting
{
    public const int EventsPerPass = 20;
    public const int RoutesPerEvent = 100;
    public const int DeliveriesPerPass = 100;
    public const int ControlsPerDelivery = 100;

    public static void StageImpact(AtoCopilotContext db, CapabilityResponsibilityImpact impact) =>
        StageImpact(db, impact.Id, impact.TenantId, impact.RegisteredSystemId);

    public static void StageImpact(AtoCopilotContext db, Guid impactId, Guid tenantId, string systemId) =>
        db.Set<CapabilityResponsibilityDelivery>().Add(new()
        {
            Id = Key($"impact:{impactId:D}"), TenantId = tenantId,
            RegisteredSystemId = systemId, ImpactId = impactId
        });

    public static void StageBaseline(AtoCopilotContext db, ControlBaseline baseline) =>
        db.Set<CapabilityResponsibilityDelivery>().Add(new()
        {
            Id = Key($"baseline:{baseline.Id}"), TenantId = baseline.TenantId,
            RegisteredSystemId = baseline.RegisteredSystemId, BaselineId = baseline.Id
        });

    public static async Task ExpandAsync(AtoCopilotContext db, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.Ticks;
        var events = await db.Set<CspResponsibilitySourceEvent>().AsNoTracking()
            .Where(e => !e.FanoutCompleted && e.NextExpansionUtcTicks <= now)
            .OrderBy(e => e.NextExpansionUtcTicks).ThenBy(e => e.Id).Take(EventsPerPass).Select(e => e.Id).ToListAsync(ct);
        foreach (var id in events)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await db.Set<CspResponsibilitySourceEvent>().SingleAsync(e => e.Id == id, ct);
            if (source.FanoutCompleted) continue;
            var capabilityId = source.CapabilityId.ToString();
            var routes = await db.CapabilitySubscriptions.AsNoTracking()
                .Where(s => s.RoutingCapabilityId == capabilityId && s.IsActive
                    && (source.LastSubscriptionId == null || string.Compare(s.Id, source.LastSubscriptionId) > 0))
                .OrderBy(s => s.Id).Take(RoutesPerEvent)
                .Select(s => new { s.Id, s.RoutingTenantId, s.RegisteredSystemId }).ToListAsync(ct);
            var keys = routes.Select(r => Key($"provider:{source.Id:D}:{r.Id}")).ToArray();
            var existing = (await db.Set<CapabilityResponsibilityDelivery>().Where(d => keys.Contains(d.Id))
                .Select(d => d.Id).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
            foreach (var route in routes)
            {
                var key = Key($"provider:{source.Id:D}:{route.Id}");
                if (existing.Contains(key)) continue;
                db.Set<CapabilityResponsibilityDelivery>().Add(new()
                {
                    Id = key, TenantId = route.RoutingTenantId, RegisteredSystemId = route.RegisteredSystemId,
                    SourceEventId = source.Id, SubscriptionId = route.Id
                });
            }
            source.LastSubscriptionId = routes.LastOrDefault()?.Id ?? source.LastSubscriptionId;
            source.FanoutCompleted = routes.Count < RoutesPerEvent;
            source.NextExpansionUtcTicks = DateTime.UtcNow.Ticks;
            source.ExpansionRevision++;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
    }

    public static async Task<IReadOnlyList<ResponsibilityDeliveryClaim>> ClaimAsync(
        AtoCopilotContext db, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.Ticks;
        var due = await db.Set<CapabilityResponsibilityDelivery>().AsNoTracking()
            .Where(d => d.CompletedAt == null && d.NextAttemptUtcTicks <= now && d.LeaseUntilUtcTicks <= now)
            .OrderBy(d => d.ImpactId == null ? 0 : 1).ThenBy(d => d.NextAttemptUtcTicks).ThenBy(d => d.Id).Take(DeliveriesPerPass)
            .Select(d => new { d.Id, d.TenantId, d.RegisteredSystemId }).ToListAsync(ct);
        var claimed = new List<ResponsibilityDeliveryClaim>();
        foreach (var route in due)
        {
            var token = Guid.NewGuid();
            var leaseUntil = DateTime.UtcNow.AddMinutes(5).Ticks;
            var count = await db.Set<CapabilityResponsibilityDelivery>()
                .Where(d => d.Id == route.Id && d.CompletedAt == null && d.NextAttemptUtcTicks <= now && d.LeaseUntilUtcTicks <= now)
                .ExecuteUpdateAsync(set => set.SetProperty(d => d.LeaseToken, token)
                    .SetProperty(d => d.LeaseUntilUtcTicks, leaseUntil).SetProperty(d => d.Outcome, "Processing")
                    .SetProperty(d => d.Attempts, d => d.Attempts + 1).SetProperty(d => d.Revision, d => d.Revision + 1), ct);
            if (count == 1) claimed.Add(new(route.Id, route.TenantId, route.RegisteredSystemId, token));
        }
        return claimed;
    }

    public static async Task RetryAsync(AtoCopilotContext db, ResponsibilityDeliveryClaim claim, string code, CancellationToken ct)
    {
        var next = DateTime.UtcNow.AddMinutes(1).Ticks;
        var count = await db.Set<CapabilityResponsibilityDelivery>().Where(d => d.Id == claim.Id && d.LeaseToken == claim.LeaseToken)
            .ExecuteUpdateAsync(set => set.SetProperty(d => d.Outcome, code).SetProperty(d => d.NextAttemptUtcTicks, next)
                .SetProperty(d => d.LeaseToken, (Guid?)null).SetProperty(d => d.LeaseUntilUtcTicks, 0)
                .SetProperty(d => d.Revision, d => d.Revision + 1), ct);
        if (count != 1) throw new DbUpdateConcurrencyException("Responsibility delivery retry could not retain the claimed lease.");
    }

    private static string Key(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>Only routing IDs leave the neutral claim phase; no customer content or human role is carried.</summary>
public sealed record ResponsibilityDeliveryClaim(string Id, Guid TenantId, string SystemId, Guid LeaseToken);

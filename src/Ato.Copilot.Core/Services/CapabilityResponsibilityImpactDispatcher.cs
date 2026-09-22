using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services;

/// <summary>At-least-once delivery to the narrative owner's idempotent mark-only entry point.</summary>
public sealed class CapabilityResponsibilityImpactDispatcher(
    AtoCopilotContext db, ITenantContext tenant, ICapabilityResponsibilityService responsibilities,
    INarrativeChangeImpactService narratives, ILogger<CapabilityResponsibilityImpactDispatcher> logger)
    : ICapabilityResponsibilityImpactDispatcher
{
    public async Task<CapabilityResponsibilityDispatchResponse> DispatchAsync(string systemId, CancellationToken ct = default)
    {
        await responsibilities.AuthorizeAsync(systemId, true, ct);
        return await DispatchCoreAsync(systemId, ct);
    }

    internal async Task<CapabilityResponsibilityDispatchResponse> DispatchProviderImpactAsync(string systemId, Guid impactId, CancellationToken ct)
    {
        CapabilityResponsibilityService.RequireBackgroundTenant(tenant);
        if (!await db.RegisteredSystems.AnyAsync(s => s.Id == systemId && s.TenantId == tenant.EffectiveTenantId && s.IsActive, ct))
            throw new KeyNotFoundException("Affected system not found in the resolved customer scope.");
        return await DispatchCoreAsync(systemId, ct, impactId);
    }

    private async Task<CapabilityResponsibilityDispatchResponse> DispatchCoreAsync(string systemId, CancellationToken ct, Guid? impactId = null)
    {
        var tenantId = tenant.EffectiveTenantId;
        var pending = await db.Set<CapabilityResponsibilityImpact>()
            .Where(i => i.TenantId == tenantId && i.RegisteredSystemId == systemId && i.AcknowledgedAt == null
                && (impactId == null || i.Id == impactId)).ToListAsync(ct);
        var baseline = await db.ControlBaselines.AsNoTracking()
            .SingleOrDefaultAsync(b => b.TenantId == tenantId && b.RegisteredSystemId == systemId, ct);
        var neededControls = pending.Select(i => i.ControlId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var narrativeControls = (await db.ControlImplementations.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.RegisteredSystemId == systemId && neededControls.Contains(i.ControlId))
            .Select(i => i.ControlId).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var delivered = 0;
        var proposalIds = new HashSet<Guid>();
        var deferred = new List<CapabilityResponsibilityDeliveryDeferral>();
        foreach (var impact in pending.OrderBy(i => i.CreatedAt).ThenBy(i => i.Revision))
        {
            if (delivered == 100) break;
            try
            {
                if (baseline is null)
                {
                    deferred.Add(new(impact.Id, impact.ControlId, "MissingBaseline"));
                    continue;
                }
                if (baseline.ControlIds.Contains(impact.ControlId, StringComparer.OrdinalIgnoreCase))
                {
                    if (!narrativeControls.Contains(impact.ControlId))
                    {
                        deferred.Add(new(impact.Id, impact.ControlId, "MissingNarrative"));
                        continue;
                    }
                    var sources = JsonSerializer.Deserialize<ImpactSource[]>(impact.SourcesJson)
                        ?? throw new InvalidDataException("Responsibility impact sources are missing.");
                    foreach (var source in sources.DistinctBy(s => s.SubscriptionId))
                    {
                        var origin = new NarrativeChangeSourceContext(source.SourceRevision,
                            impact.Reason switch { "SubscriptionRemoved" => "SubscriptionRemoved",
                                "ResponsibilityConfirmed" or "SubscriptionAdded" or "BaselineChanged" => "ResponsibilityChanged", _ => "ProviderChanged" },
                            impact.ControlBaselineId, source.SubscriptionId, source.CspProfileId, source.ComponentId,
                            Guid.Parse(source.CapabilityId), source.PreviousInheritanceType, source.CurrentInheritanceType);
                        var result = await narratives.QueueAsync(new(tenantId, systemId, [impact.ControlId],
                            ["Policy", "Technical"], "CspCapability", source.CapabilityId, impact.Actor,
                            $"{impact.Id:D}:{source.SubscriptionId}", origin), ct);
                        proposalIds.UnionWith(result.ProposalIds);
                    }
                }
                impact.AcknowledgedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                delivered++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Responsibility impact {ImpactId} delivery failed; pending source work is retained", impact.Id);
                throw;
            }
        }
        return new(delivered, pending.Count - delivered, proposalIds.Order().ToArray(), deferred);
    }

    private sealed record ImpactSource(string CapabilityId, string SubscriptionId, string SourceRevision,
        Guid? ComponentId, Guid? CspProfileId, string? PreviousInheritanceType = null, string? CurrentInheritanceType = null);
}

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>Delivery outcome; source impact remains pending until the mark-only consumer succeeds.</summary>
public sealed record CapabilityResponsibilityDispatchResponse(
    int Delivered, int Pending, IReadOnlyList<Guid> ProposalIds, IReadOnlyList<CapabilityResponsibilityDeliveryDeferral> Deferred);

/// <summary>Explicit prerequisite preventing impact delivery; the source work remains unacknowledged.</summary>
public sealed record CapabilityResponsibilityDeliveryDeferral(Guid ImpactId, string ControlId, string Reason);

/// <summary>Explicit authorized-system outbox delivery. Never generates or approves narratives.</summary>
public interface ICapabilityResponsibilityImpactDispatcher
{
    Task<CapabilityResponsibilityDispatchResponse> DispatchAsync(string systemId, CancellationToken ct = default);
}

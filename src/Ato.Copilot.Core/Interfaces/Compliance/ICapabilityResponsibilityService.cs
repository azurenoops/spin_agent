namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>A reviewed source/baseline no longer matches the state displayed to the caller.</summary>
public sealed class ResponsibilityReviewConflictException(string message) : Exception(message);

/// <summary>Explicit reviewed allocation for a mapped control; never inferred from catalog mappings.</summary>
public sealed record CapabilityResponsibilityAllocation(
    string ControlId, string InheritanceType, string? Provider, string? CustomerResponsibility);

/// <summary>Confirmation pins the baseline, provider content, and previously displayed review state.</summary>
public sealed record ConfirmCapabilityResponsibilitiesRequest(
    string BaselineId, string SourceRevision, string ReviewRevision,
    IReadOnlyList<CapabilityResponsibilityAllocation> Allocations,
    bool? ProviderCoverageVerified = null, bool? CustomerDutiesReviewed = null, string? ReviewNotes = null)
{
    public void ValidateReviewEvidence(bool required = false)
    {
        if (!required && ProviderCoverageVerified is null && CustomerDutiesReviewed is null && ReviewNotes is null)
            return;
        if (ProviderCoverageVerified != true || CustomerDutiesReviewed != true
            || string.IsNullOrWhiteSpace(ReviewNotes) || ReviewNotes.Trim().Length > 2000)
            throw new ArgumentException("Verify provider coverage, review customer duties, and supply review notes of 1-2000 characters.");
    }
}

/// <summary>One source contribution and its persisted/effective responsibility state.</summary>
public sealed record CapabilityResponsibilityItem(
    string SubscriptionId, Guid CapabilityId, Guid? ComponentId, Guid? CspProfileId,
    string ControlId, string SourceRevision, string ReviewRevision, string State,
    string? ReviewedSourceRevision, string? ConfirmedBy, DateTimeOffset? ConfirmedAt,
    CapabilityResponsibilityAllocation? Allocation, string? EffectiveInheritanceType, string? DesignationSource,
    bool SourceAvailable, string? SourceSnapshotJson, string? ReviewedSourceSnapshotJson,
    bool? ProviderCoverageVerified = null, bool? CustomerDutiesReviewed = null, string? ReviewNotes = null);

/// <summary>Durable pending change impact, independent of narrative generation or approval.</summary>
public sealed record CapabilityResponsibilityImpactResponse(
    Guid Id, string BaselineId, string ControlId, string StateHash, string Reason,
    string SourcesJson, DateTimeOffset CreatedAt);

/// <summary>Review UI contract for the selected system and its current baseline.</summary>
public sealed record CapabilityResponsibilityResponse(
    string SystemId, string? BaselineId, bool CanConfirm,
    IReadOnlyList<CapabilityResponsibilityItem> Items,
    IReadOnlyList<CapabilityResponsibilityImpactResponse> PendingImpacts);

/// <summary>Subscription mutation result, including explicit downstream prerequisites.</summary>
public sealed record CapabilitySubscriptionChangeResponse(
    string Id, bool AlreadySubscribed, bool Unsubscribed, CapabilityResponsibilityResponse Responsibilities, bool Created = false);

/// <summary>Scoped system authorization and transactional subscription responsibility reconciliation.</summary>
public interface ICapabilityResponsibilityService
{
    /// <summary>Reconciles an authorized setup mutation in its existing transaction; does not confirm customer allocations.</summary>
    Task<CapabilityResponsibilityResponse> ReconcileSetupAsync(
        Ato.Copilot.Core.Data.Context.AtoCopilotContext context, string systemId, string actor, CancellationToken ct = default);
    Task<bool> AuthorizeAsync(string systemId, bool write, CancellationToken ct = default);
    Task<CapabilityResponsibilityResponse> PreviewAsync(string systemId, CancellationToken ct = default);
    Task<CapabilityResponsibilityResponse> ReconcileAsync(string systemId, string actor, CancellationToken ct = default);
    Task<CapabilityResponsibilityResponse> ConfirmAsync(string systemId, Guid capabilityId,
        ConfirmCapabilityResponsibilitiesRequest request, string actor, CancellationToken ct = default);
    Task<CapabilitySubscriptionChangeResponse> SubscribeAsync(string systemId, Guid capabilityId, string actor, CancellationToken ct = default);
    Task<CapabilitySubscriptionChangeResponse> UnsubscribeAsync(string systemId, Guid capabilityId, string actor, CancellationToken ct = default);
}

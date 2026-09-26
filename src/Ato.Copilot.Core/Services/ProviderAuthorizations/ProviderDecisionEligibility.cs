using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public static class ProviderDecisionEligibility
{
    private static readonly string[] SupportedPositiveStatements =
    [
        "ATO", "IATO", "Authorized", "Approved", "Authorization to Operate",
        "Interim Authorization to Operate", "FedRAMP Authorized"
    ];

    /// <summary>
    /// Returns a recorded-source blocker, or null; this is not independent verification of authority.
    /// Callers must separately validate ownership, current revision, boundary and retained evidence.
    /// Explicit inherited-reference eligibility never establishes provider decision authority.
    /// </summary>
    public static ProviderImpactBlocker? Evaluate(ProviderAuthorizationRevision revision,
        IEnumerable<ProviderAuthorizationLifecycleEvent> events, string requiredRecordKind = "ProviderDecision")
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(events);
        if (requiredRecordKind is not ("ProviderDecision" or "InheritedMicrosoftReference"))
            throw new ArgumentException("Require ProviderDecision or InheritedMicrosoftReference.", nameof(requiredRecordKind));

        var source = Read<CreateProviderDecisionRequest>(revision.SnapshotJson);
        ProviderImpactBlocker Block(string message) => new("DECISION_NOT_ELIGIBLE", message, revision.Id.ToString());
        if (source.RecordKind is not ("ProviderDecision" or "InheritedMicrosoftReference"))
            return Block("Review the source record kind; use ProviderDecision or InheritedMicrosoftReference.");
        if (source.RecordKind != requiredRecordKind)
            return requiredRecordKind == "ProviderDecision"
                ? new("PROVIDER_DECISION_REQUIRED", "Select a recorded provider decision; an inherited Microsoft reference cannot substitute for provider authority.", revision.Id.ToString())
                : Block("Select an inherited Microsoft reference for this reference context, not a provider decision.");
        if (revision.MetadataReviewState != "Recorded" || string.IsNullOrWhiteSpace(revision.RecordedBy) || revision.RecordedAt is null)
            return Block("A human must record the exact source decision metadata before it can support this context.");
        if (Hash(revision.SnapshotJson) != revision.SnapshotHash)
            return Block("The source snapshot no longer matches its immutable digest. Review the retained source revision.");
        if (source.Citations.Count == 0 || string.IsNullOrWhiteSpace(source.IssuingAuthority))
            return Block("Record the source issuer and supporting citations before reviewing applicability.");
        if (source.ExpiryBasis is not ("DateStated" or "NoExpiryStated")
            || source.ExpiryBasis == "DateStated" && source.ExpiresOn is null
            || source.ExpiryBasis == "NoExpiryStated" && source.ExpiresOn is not null)
            return Block("Review the source dates and expiry basis; unknown expiry cannot be treated as no expiry.");
        if (Date(source.IssuedOn) > DateOnly.FromDateTime(DateTime.UtcNow)
            || ProviderAuthorizationService.Standing(revision, events) != "CurrentAsRecorded")
            return Block("Use a currently effective recorded source decision that is not expired, withdrawn or superseded.");
        if (!SupportedPositiveStatements.Contains(source.DecisionAsStated?.Trim(), StringComparer.OrdinalIgnoreCase))
            return Block("Denied or unrecognized source text cannot support this context. Review the retained source for an explicitly supported positive statement; do not infer authorization or rewrite the source text.");
        return null;
    }
}

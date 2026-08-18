using Ato.Copilot.Core.Models.Provenance;

namespace Ato.Copilot.Core.Interfaces.Provenance;

/// <summary>
/// Append-only ledger for LLM invocation records (#941 — Epic 10 provenance audit trail).
///
/// One call to <see cref="RecordAsync"/> per LLM round-trip.  Implementations MUST be
/// additive only — never update or delete rows — to preserve the integrity guarantee.
/// </summary>
public interface IModelCallLedger
{
    /// <summary>
    /// Appends a single model-call record to the ledger.
    /// Returns the persisted <see cref="ModelCall.Id"/>.
    /// </summary>
    Task<string> RecordAsync(ModelCall record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all model-call records for the given conversation, ordered by
    /// <see cref="ModelCall.CallIndex"/> ascending.
    /// </summary>
    Task<IReadOnlyList<ModelCall>> GetByConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default);
}

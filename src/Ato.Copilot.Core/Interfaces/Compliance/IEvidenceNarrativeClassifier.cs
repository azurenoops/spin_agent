using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>Deterministically classifies evidence by available metadata.</summary>
public interface IEvidenceNarrativeClassifier
{
    /// <summary>Returns the narrative type and human-readable classification rationale.</summary>
    Task<(EvidenceNarrativeType Type, string Rationale)> ClassifyAsync(
        EvidenceArtifact artifact,
        CancellationToken cancellationToken = default);
}
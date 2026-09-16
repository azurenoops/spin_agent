using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>Reads and independently updates policy and technical narratives.</summary>
public interface IDualNarrativeService
{
    /// <summary>Gets both narrative halves and evidence grouped by classification.</summary>
    Task<DualNarrativeResponse> GetAsync(
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default);

    /// <summary>Updates only the explicitly selected narrative halves.</summary>
    Task<DualNarrativeResponse> UpdateAsync(
        string systemId,
        string controlId,
        string? policyNarrative,
        bool updatePolicy,
        string? technicalNarrative,
        bool updateTechnical,
        string role,
        string authoredBy,
        CancellationToken cancellationToken = default);

    /// <summary>Manually classifies an evidence artifact.</summary>
    Task<EvidenceArtifactSummary> ClassifyEvidenceAsync(
        string artifactId,
        EvidenceNarrativeType narrativeType,
        string? rationale,
        string classifiedBy,
        CancellationToken cancellationToken = default);
}
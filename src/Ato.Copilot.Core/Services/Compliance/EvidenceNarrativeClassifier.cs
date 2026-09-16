using System.Text.RegularExpressions;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Services.Compliance;

/// <summary>Classifies evidence using deterministic category and filename rules.</summary>
public sealed partial class EvidenceNarrativeClassifier : IEvidenceNarrativeClassifier
{
    /// <inheritdoc />
    public Task<(EvidenceNarrativeType Type, string Rationale)> ClassifyAsync(
        EvidenceArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();

        if (artifact.ArtifactCategory is ArtifactCategory.ScanResult
            or ArtifactCategory.ConfigurationExport
            or ArtifactCategory.AuditLog
            or ArtifactCategory.TestResult)
        {
            return Task.FromResult((
                EvidenceNarrativeType.Technical,
                $"ArtifactCategory={artifact.ArtifactCategory}"));
        }

        if (artifact.ArtifactCategory == ArtifactCategory.PolicyDocument ||
            PolicyDocumentPattern().IsMatch(artifact.FileName))
        {
            return Task.FromResult((
                EvidenceNarrativeType.Policy,
                "Filename matches policy document pattern"));
        }

        return Task.FromResult((EvidenceNarrativeType.Combined, "LowConfidence"));
    }

    [GeneratedRegex(
        @"(^|[^a-z])(policy|procedure|sop|plan|charter|standard)([^a-z]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PolicyDocumentPattern();
}
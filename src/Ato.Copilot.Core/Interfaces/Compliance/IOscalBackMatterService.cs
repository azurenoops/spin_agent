using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Builds OSCAL back-matter sections from evidence artifacts stored for a system.
/// EvidenceArtifact.ContentHash already contains a SHA-256 hex value — no recomputation needed.
/// Feature 076 — T008.
/// </summary>
public interface IOscalBackMatterService
{
    /// <summary>
    /// Build an OSCAL back-matter section for <paramref name="systemId"/>,
    /// including SHA-256 hashes from stored evidence artifacts.
    /// Returns null if the system has no evidence artifacts.
    /// </summary>
    Task<OscalBackMatterSection?> GetBackMatterForSystemAsync(
        string systemId,
        string documentType,
        CancellationToken cancellationToken = default);
}
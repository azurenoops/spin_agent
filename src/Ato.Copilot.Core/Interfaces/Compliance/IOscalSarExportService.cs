namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Generates OSCAL 1.1.2 Assessment Results (SAR) JSON from compliance assessment data.
/// Feature 076 — T005.
/// </summary>
public interface IOscalSarExportService
{
    /// <summary>
    /// Export the most recent completed assessment for <paramref name="systemId"/>
    /// as an OSCAL 1.1.2 assessment-results document.
    /// </summary>
    Task<OscalSarExportResult> ExportAsync(
        string systemId,
        bool prettyPrint = true,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of an OSCAL SAR export operation.</summary>
public record OscalSarExportResult(
    string OscalJson,
    List<string> Warnings,
    int FindingCount,
    int ObservationCount,
    int RiskCount);
namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Provides FedRAMP baseline control ID lists for OSCAL profile resolution.
/// Returns lowercase OSCAL control IDs (e.g. "ac-1", "sc-7").
/// Feature 076 — T010.
/// </summary>
public interface IOscalCatalogService
{
    /// <summary>
    /// Return the control IDs selected by the FedRAMP baseline for
    /// <paramref name="baselineLevel"/> ("low" | "moderate" | "high").
    /// </summary>
    Task<List<string>> GetBaselineControlIdsAsync(
        string baselineLevel,
        CancellationToken cancellationToken = default);
}
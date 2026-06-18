namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Generates OSCAL 1.1.2 POA&amp;M JSON from PoamItem data.
/// Feature 076 — T006.
/// </summary>
public interface IOscalPoamExportService
{
    /// <summary>
    /// Export all active (non-completed) POA&amp;M items for <paramref name="systemId"/>
    /// as an OSCAL 1.1.2 plan-of-action-and-milestones document.
    /// </summary>
    Task<OscalPoamExportResult> ExportAsync(
        string systemId,
        bool prettyPrint = true,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of an OSCAL POA&amp;M export operation.</summary>
public record OscalPoamExportResult(
    string OscalJson,
    List<string> Warnings,
    int PoamItemCount,
    int MilestoneCount);
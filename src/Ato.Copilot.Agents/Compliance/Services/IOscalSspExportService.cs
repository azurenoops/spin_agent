namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Produces OSCAL 1.1.2 SSP JSON from entity data (Feature 022).
/// </summary>
public interface IOscalSspExportService
{
    /// <summary>Export an OSCAL 1.1.2 System Security Plan as JSON.</summary>
    Task<OscalExportResult> ExportAsync(
        string registeredSystemId,
        bool includeBackMatter = true,
        bool prettyPrint = true,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of an OSCAL SSP export.</summary>
public record OscalExportResult(string OscalJson, List<string> Warnings, OscalStatistics Statistics)
{
    public IReadOnlyList<string> ProviderProvenanceGaps { get; init; } = [];
    public IReadOnlyList<string> ProfileSourceGaps { get; init; } = [];
    public IReadOnlyList<string> EvidenceSourceGaps { get; init; } = [];
    public Ato.Copilot.Core.Dtos.Dashboard.DocumentSourceManifest? SourceManifest { get; init; }
}

/// <summary>Counts of OSCAL structural elements.</summary>
public record OscalStatistics(
    int ControlCount,
    int ComponentCount,
    int InventoryItemCount,
    int UserCount,
    int BackMatterResourceCount);

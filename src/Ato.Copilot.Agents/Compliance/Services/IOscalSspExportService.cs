namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Produces OSCAL 1.1.2 SSP JSON from entity data (Feature 022).
/// </summary>
public interface IOscalSspExportService
{
    /// <summary>Generate review-only OSCAL containing saved working profile contributions, never approval pins.</summary>
    Task<OscalExportResult> PreviewAsync(
        string registeredSystemId,
        bool includeBackMatter = true,
        bool prettyPrint = true,
        CancellationToken cancellationToken = default);

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

    public IReadOnlyList<Ato.Copilot.Core.Dtos.Dashboard.DocumentSourceGapDto> BuildPreviewSourceGaps()
    {
        var gaps = Warnings.Concat(ProviderProvenanceGaps).Concat(ProfileSourceGaps).Concat(EvidenceSourceGaps)
            .Distinct(StringComparer.Ordinal).Select(message => new Ato.Copilot.Core.Dtos.Dashboard.DocumentSourceGapDto(
                ProviderProvenanceGaps.Contains(message) ? "PROVIDER_PROVENANCE_UNVERIFIED" :
                ProfileSourceGaps.Contains(message) ? "PROFILE_APPROVAL_UNVERIFIED" :
                EvidenceSourceGaps.Contains(message) ? "EVIDENCE_PERMISSION_UNVERIFIED" : "OSCAL_SOURCE_WARNING",
                message)).ToList();
        if (SourceManifest?.HasWorkingProfileSources == true)
            gaps.Add(new("WORKING_PROFILE_PREVIEW_ONLY",
                "Saved working profile contributions are shown for review only. Generate final documents through the approved-source export workflow."));
        return gaps;
    }
}

/// <summary>Counts of OSCAL structural elements.</summary>
public record OscalStatistics(
    int ControlCount,
    int ComponentCount,
    int InventoryItemCount,
    int UserCount,
    int BackMatterResourceCount);

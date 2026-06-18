namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Imports an OSCAL 1.1.2 SSP document into SPIN Agent control implementations.
/// Supports idempotent preview and full import modes.
/// Feature 076 — T009.
/// </summary>
public interface IOscalSspImportService
{
    Task<OscalImportResult> ImportAsync(
        string systemId,
        string oscalJson,
        ImportMode mode = ImportMode.Preview,
        CancellationToken cancellationToken = default);
}

/// <summary>Import mode discriminator.</summary>
public enum ImportMode { Preview, Full }

/// <summary>Result of an OSCAL SSP import operation.</summary>
public record OscalImportResult
{
    public string RunId { get; init; } = string.Empty;
    public ImportMode Mode { get; init; }
    public int ControlsCreated { get; init; }
    public int ControlsUpdated { get; init; }
    public int ControlsSkipped { get; init; }
    public int ControlsFailed { get; init; }
    public List<string> ValidationErrors { get; init; } = new();
    public List<OscalImportPreviewItem> Preview { get; init; } = new();
}

/// <summary>Per-control diff entry in preview mode.</summary>
public record OscalImportPreviewItem
{
    public string ControlId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty; // create | update | skip
    public string? CurrentNarrative { get; init; }
    public string? NewNarrative { get; init; }
}
namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Exports the Security Assessment Plan (Feature 018) in OSCAL 1.1.2 assessment-plan JSON format.
/// </summary>
public interface IOscalSapExportService
{
    Task<string> ExportAsync(
        string systemId,
        CancellationToken cancellationToken = default);
}

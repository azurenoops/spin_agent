namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Broadcasts authorization package generation lifecycle events to connected clients via SignalR.
/// Implemented in the Mcp layer (PackageHub); consumed by PackageBackgroundService in the Agents layer.
/// </summary>
public interface IPackageExportNotifier
{
    /// <summary>Notify that the package status has changed (e.g., Pending → Generating).</summary>
    Task SendStatusChangedAsync(string packageId, string status, CancellationToken cancellationToken = default);

    /// <summary>Notify that a specific artifact has been generated.</summary>
    Task SendArtifactGeneratedAsync(string packageId, string artifactType, CancellationToken cancellationToken = default);

    /// <summary>Notify that schema validation is complete for the package.</summary>
    Task SendValidationCompleteAsync(string packageId, bool isValid, int violationCount, CancellationToken cancellationToken = default);

    /// <summary>Notify that the package generation completed successfully.</summary>
    Task SendPackageCompleteAsync(string packageId, string downloadUrl, CancellationToken cancellationToken = default);

    /// <summary>Notify that the package generation failed.</summary>
    Task SendPackageFailedAsync(string packageId, string failedArtifact, string error, string? remediation, CancellationToken cancellationToken = default);
}

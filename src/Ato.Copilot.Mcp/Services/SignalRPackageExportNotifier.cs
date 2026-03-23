using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Mcp.Hubs;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// SignalR-based implementation of <see cref="IPackageExportNotifier"/>.
/// Pushes package generation lifecycle events to the PackageHub group for the specific package.
/// </summary>
public class SignalRPackageExportNotifier : IPackageExportNotifier
{
    private readonly IHubContext<PackageHub> _hubContext;
    private readonly ILogger<SignalRPackageExportNotifier> _logger;

    public SignalRPackageExportNotifier(
        IHubContext<PackageHub> hubContext,
        ILogger<SignalRPackageExportNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendStatusChangedAsync(string packageId, string status, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group($"package:{packageId}").SendAsync("PackageStatusChanged", new
            {
                packageId,
                status,
                timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send status change for package {PackageId}", packageId);
        }
    }

    public async Task SendArtifactGeneratedAsync(string packageId, string artifactType, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group($"package:{packageId}").SendAsync("PackageArtifactGenerated", new
            {
                packageId,
                artifactType,
                timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send artifact generated for package {PackageId}", packageId);
        }
    }

    public async Task SendValidationCompleteAsync(string packageId, bool isValid, int violationCount, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group($"package:{packageId}").SendAsync("PackageValidationComplete", new
            {
                packageId,
                isValid,
                violationCount,
                timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send validation complete for package {PackageId}", packageId);
        }
    }

    public async Task SendPackageCompleteAsync(string packageId, string downloadUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group($"package:{packageId}").SendAsync("PackageComplete", new
            {
                packageId,
                downloadUrl,
                completedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send package complete for package {PackageId}", packageId);
        }
    }

    public async Task SendPackageFailedAsync(string packageId, string failedArtifact, string error, string? remediation, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group($"package:{packageId}").SendAsync("PackageFailed", new
            {
                packageId,
                failedArtifact,
                error,
                remediation,
                failedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send package failed for package {PackageId}", packageId);
        }
    }
}

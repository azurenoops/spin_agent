using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Orchestrates authorization package assembly, background generation, and history.
/// </summary>
public interface IAuthorizationPackageService
{
    Task<PackageValidationResult> ValidateRetainedPackageAsync(string systemId, PackagePurpose purpose,
        RetainedPackageSelection? selection, string validatedBy = "mcp-user", CancellationToken cancellationToken = default);
    Task<AuthorizationPackage> EnqueueRetainedPackageAsync(string systemId, PackagePurpose purpose,
        RetainedPackageSelection selection, string generatedBy = "mcp-user", CancellationToken cancellationToken = default, string? idempotencyKey = null);
    Task<AuthorizationPackage> EnqueuePackageAsync(
        string systemId,
        PackagePurpose purpose,
        EvidenceMode evidenceMode = EvidenceMode.Embedded,
        string generatedBy = "mcp-user",
        CancellationToken cancellationToken = default);

    Task<AuthorizationPackage> EnqueuePackageAsync(
        string systemId,
        EvidenceMode evidenceMode = EvidenceMode.Embedded,
        string generatedBy = "mcp-user",
        CancellationToken cancellationToken = default);

    Task<AuthorizationPackage?> GetPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    Task<PackageListResponse> ListPackagesAsync(
        string systemId,
        int limit = 20,
        int offset = 0,
        bool includeFailed = false,
        CancellationToken cancellationToken = default);

    Task<Stream?> DownloadPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    Task<int> CleanupExpiredPackagesAsync(
        CancellationToken cancellationToken = default);
}

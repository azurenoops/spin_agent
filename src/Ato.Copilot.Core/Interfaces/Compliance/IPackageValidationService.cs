using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Pre-submission package validation — checks artifact presence, OSCAL version consistency,
/// cross-artifact control ID matching, SSP completeness, SAR status, and evidence coverage.
/// </summary>
public interface IPackageValidationService
{
    Task<PackageValidationResult> ValidateAsync(
        string systemId,
        string validatedBy = "mcp-user",
        CancellationToken cancellationToken = default);
}

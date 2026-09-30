using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Dtos.Dashboard;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Purpose-specific documentation checks over the canonical inventory register.</summary>
internal static class InventoryPackageReadiness
{
    internal static async Task<IReadOnlyList<PackageReadinessCheck>> EvaluateAsync(
        IInventoryService service, string systemId, PackagePurpose purpose, ILogger logger, CancellationToken ct)
    {
        var required = purpose == PackagePurpose.InitialSubmission;
        try
        {
            var result = await service.CheckCompletenessAsync(systemId, ct);
            if (result.SystemId != systemId) throw new InvalidOperationException("Inventory scope mismatch.");
            var missing = result.ItemsWithMissingFields;
            var complete = result.TotalItems > 0 && missing.Count == 0;
            var explanation = result.TotalItems == 0
                ? "No active hardware/software inventory records are documented for this system."
                : missing.Count > 0
                    ? "Inventory records have missing required fields: " + string.Join("; ",
                        missing.Select(item => $"{item.ItemName}: {string.Join(", ", item.MissingFields)}"))
                    : "Active inventory records contain the required type-specific documentation. This is not certification of exhaustive boundary coverage or implemented CM-8.";
            var checks = new List<PackageReadinessCheck>
            {
                PackageValidationService.Check("inventory", "Hardware/software inventory documentation",
                    complete ? "Passed" : required ? "Blocking" : "FollowUp", required, explanation,
                    "Open the hardware/software register and document active resources and software. Managed/SaaS/PaaS software needs no fabricated physical parent, serial number or IP.", "SystemOwner")
            };
            if (result.UnmatchedBoundaryResources.Count > 0 || result.HardwareWithoutSoftware.Count > 0)
                checks.Add(PackageValidationService.Check("inventory-coverage", "Inventory coverage review", "FollowUp", false,
                    $"{result.UnmatchedBoundaryResources.Count} legacy boundary resources lack inventory links; {result.HardwareWithoutSoftware.Count} hardware records have no installed software entries. Review applicability for provider-managed resources; do not invent software or physical identifiers.",
                    "Review the inventory completeness details and document applicable software and boundary coverage.", "SystemOwner", "inventory"));
            return checks;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Inventory documentation could not be evaluated for system {SystemId}", systemId);
            return [PackageValidationService.Check("inventory", "Hardware/software inventory documentation", "Unavailable", required,
                "The canonical inventory register could not be evaluated.", "Restore inventory source access and recheck readiness.", "SystemOwner")];
        }
    }
}

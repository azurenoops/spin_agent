using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.EvidenceCollectors;

/// <summary>
/// Collects evidence for the NIST SP 800-53 Maintenance (MA) control family.
/// Gathers maintenance configurations, assignment schedules, patching policy compliance,
/// VM maintenance scope, and resource locks to support MA-2 through MA-6 control requirements.
/// </summary>
public class MaintenanceEvidenceCollector : BaseEvidenceCollector
{
    private readonly IAzureResourceService _azureResourceService;
    private readonly IAzurePolicyComplianceService _policyService;

    public override string FamilyCode => ControlFamilies.Maintenance;

    public MaintenanceEvidenceCollector(
        IAzureResourceService azureResourceService,
        IAzurePolicyComplianceService policyService,
        ILogger<MaintenanceEvidenceCollector> logger) : base(logger)
    {
        _azureResourceService = azureResourceService;
        _policyService = policyService;
    }

    protected override async Task<List<EvidenceItem>> CollectFamilyEvidenceAsync(
        string subscriptionId,
        string? resourceGroup,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = new List<EvidenceItem>();

        // Evidence 1 — Configuration: Maintenance configurations inventory (MA-2 Controlled Maintenance)
        try
        {
            var maintenanceConfigs = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.Maintenance/maintenanceConfigurations", cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "MA-2",
                Description = "Inventory of Azure Maintenance configurations defining controlled maintenance windows and policies.",
                TotalCount = maintenanceConfigs?.Count ?? 0,
                MaintenanceConfigurations = maintenanceConfigs
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Configuration,
                "Maintenance Configuration Inventory",
                "MA-2: Inventory of maintenance configurations demonstrating that controlled, scheduled maintenance windows are defined for system resources.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MA-2: Failed to collect maintenance configuration inventory.");
        }

        // Evidence 2 — Log: Maintenance assignment schedule (MA-2 Controlled Maintenance)
        try
        {
            var maintenanceAssignments = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.Maintenance/maintenanceAssignments", cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "MA-2",
                Description = "Maintenance assignment schedule records associating resources with maintenance configurations.",
                TotalCount = maintenanceAssignments?.Count ?? 0,
                MaintenanceAssignments = maintenanceAssignments
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Log,
                "Maintenance Assignment Schedule",
                "MA-2: Maintenance assignment resources showing scheduled update associations that support controlled maintenance audit trails.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MA-2: Failed to collect maintenance assignment schedule.");
        }

        // Evidence 3 — Policy: Policy compliance for patching (MA-3 Maintenance Tools)
        try
        {
            var policyStates = await _policyService.GetPolicyStatesAsync(subscriptionId, cancellationToken: cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "MA-3",
                Description = "Azure Policy compliance state for patching and maintenance tool governance policies.",
                TotalPolicyStates = policyStates?.Count ?? 0,
                PolicyStates = policyStates
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Policy,
                "Maintenance Policy Compliance",
                "MA-3: Azure Policy compliance state demonstrating enforcement of patching and maintenance tooling controls.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MA-3: Failed to collect maintenance policy compliance state.");
        }

        // Evidence 4 — Metric: VM count as maintenance scope (MA-6 Timely Maintenance)
        try
        {
            var vms = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.Compute/virtualMachines", cancellationToken);

            var vmCount = vms?.Count ?? 0;

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "MA-6",
                Description = "Count and inventory of virtual machines requiring timely maintenance coverage.",
                TotalVirtualMachines = vmCount,
                VirtualMachines = vms
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Metric,
                "VM Maintenance Scope",
                $"MA-6: {vmCount} virtual machine(s) identified as in-scope for timely maintenance scheduling and tracking.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MA-6: Failed to collect VM maintenance scope metric.");
        }

        // Evidence 5 — AccessControl: Resource locks as change control evidence (MA-5 Maintenance Personnel)
        try
        {
            var resourceLocks = await _azureResourceService.GetResourceLocksAsync(
                subscriptionId, resourceGroup, cancellationToken);

            var lockCount = resourceLocks?.Count ?? 0;

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "MA-5",
                Description = "Resource lock inventory providing change control evidence for maintenance personnel access restrictions.",
                TotalLocks = lockCount,
                ResourceLocks = resourceLocks
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.AccessControl,
                "Change Control Locks",
                $"MA-5: {lockCount} resource lock(s) in place, demonstrating change control restrictions that govern maintenance personnel access.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MA-5: Failed to collect resource locks for change control evidence.");
        }

        return items;
    }
}

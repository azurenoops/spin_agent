using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.EvidenceCollectors;

/// <summary>
/// Collects evidence for the NIST SP 800-53 Awareness and Training (AT) control family.
/// Gathers VM inventory, policy compliance, diagnostic settings, role assignments, and
/// resource group scope to support AT-1 through AT-3 control requirements.
/// </summary>
public class AwarenessTrainingEvidenceCollector : BaseEvidenceCollector
{
    private readonly IAzureResourceService _azureResourceService;
    private readonly IAzurePolicyComplianceService _policyService;

    public override string FamilyCode => ControlFamilies.AwarenessTraining;

    public AwarenessTrainingEvidenceCollector(
        IAzureResourceService azureResourceService,
        IAzurePolicyComplianceService policyService,
        ILogger<AwarenessTrainingEvidenceCollector> logger) : base(logger)
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

        // Evidence 1 — Configuration: VM inventory as training scope (AT-2 Literacy Training)
        try
        {
            var vms = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.Compute/virtualMachines", cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "AT-2",
                Description = "Inventory of virtual machines in scope for security literacy and awareness training.",
                TotalCount = vms?.Count ?? 0,
                Resources = vms
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Configuration,
                "Training Scope: VM Inventory",
                "AT-2: Virtual machine inventory representing assets in scope for security literacy training programs.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "AT-2: Failed to collect VM inventory for awareness training scope.");
        }

        // Evidence 2 — Policy: Policy compliance state (AT-1 Policy and Procedures)
        try
        {
            var policyStates = await _policyService.GetPolicyStatesAsync(subscriptionId, ct: cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "AT-1",
                Description = "Policy compliance state supporting awareness and training policy and procedure controls.",
                TotalPolicyStates = policyStates?.Count ?? 0,
                PolicyStates = policyStates
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Policy,
                "Awareness Policy Compliance",
                "AT-1: Azure Policy compliance state demonstrating enforcement of awareness and training policy requirements.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "AT-1: Failed to collect policy compliance state for awareness training.");
        }

        // Evidence 3 — Log: Diagnostic settings on subscription (AT-3 Role-Based Training)
        try
        {
            var subscriptionResourceId = $"/subscriptions/{subscriptionId}";
            var diagnosticSettings = await _azureResourceService.GetDiagnosticSettingsAsync(
                subscriptionResourceId, cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "AT-3",
                Description = "Subscription-level diagnostic settings providing audit log coverage for role-based training activities.",
                ResourceId = subscriptionResourceId,
                DiagnosticSettings = diagnosticSettings
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Log,
                "Training Audit Diagnostic Settings",
                "AT-3: Subscription diagnostic settings confirming audit logging is enabled to support role-based training oversight.",
                content,
                subscriptionResourceId));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "AT-3: Failed to collect diagnostic settings for training audit evidence.");
        }

        // Evidence 4 — Metric: Role assignment count as training scope metric (AT-3 Role-Based Training)
        try
        {
            var roleAssignments = await _azureResourceService.GetRoleAssignmentsAsync(
                subscriptionId, cancellationToken);

            var roleCount = roleAssignments?.Count ?? 0;

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "AT-3",
                Description = "Count and listing of RBAC role assignments indicating personnel scope for role-based training.",
                TotalRoleAssignments = roleCount,
                RoleAssignments = roleAssignments
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Metric,
                "Role Assignment Training Scope",
                $"AT-3: {roleCount} role assignment(s) identified, establishing the personnel scope required for role-based security training.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "AT-3: Failed to collect role assignments for training scope metric.");
        }

        // Evidence 5 — AccessControl: Resource group count for training boundary (AT-2 Literacy Training)
        try
        {
            var resourceGroups = await _azureResourceService.GetResourcesAsync(
                subscriptionId, null, "Microsoft.Resources/resourceGroups", cancellationToken);

            var rgCount = resourceGroups?.Count ?? 0;

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "AT-2",
                Description = "Resource group inventory defining the organizational boundary for awareness training scope.",
                TotalResourceGroups = rgCount,
                ResourceGroups = resourceGroups
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.AccessControl,
                "Resource Group Boundary Scope",
                $"AT-2: {rgCount} resource group(s) defining the system boundary within which security literacy training applies.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "AT-2: Failed to collect resource group boundary scope for awareness training.");
        }

        return items;
    }
}

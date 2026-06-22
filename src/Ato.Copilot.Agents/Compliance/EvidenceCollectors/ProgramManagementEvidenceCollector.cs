using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.EvidenceCollectors;

/// <summary>
/// Evidence collector for the Program Management (PM) family.
/// Collects program scope resource inventory, policy compliance, audit diagnostic settings,
/// RBAC governance scope metrics, and enterprise change control lock evidence.
/// </summary>
public class ProgramManagementEvidenceCollector : BaseEvidenceCollector
{
    private readonly IAzureResourceService _azureResourceService;
    private readonly IAzurePolicyComplianceService _policyService;

    /// <inheritdoc />
    public override string FamilyCode => ControlFamilies.ProgramManagement;

    public ProgramManagementEvidenceCollector(
        IAzureResourceService azureResourceService,
        IAzurePolicyComplianceService policyService,
        ILogger<ProgramManagementEvidenceCollector> logger) : base(logger)
    {
        _azureResourceService = azureResourceService;
        _policyService = policyService;
    }

    /// <inheritdoc />
    protected override async Task<List<EvidenceItem>> CollectFamilyEvidenceAsync(
        string subscriptionId,
        string? resourceGroup,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<EvidenceItem>();

        // 1. Configuration — All resources as program scope inventory
        try
        {
            var resources = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, null, cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Configuration,
                "Program Scope Resource Inventory",
                "Full subscription resource inventory for PM-5 (System Inventory).",
                $"Total resources in program scope: {resources.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect resource inventory evidence for PM");
        }

        // 2. Policy — Compliance summary for program management policies
        try
        {
            var policyState = await _policyService.GetPolicyStatesAsync(
                subscriptionId, cancellationToken: cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Policy,
                "Program Management Policy Compliance",
                "Azure Policy compliance state for program management policies (PM-1 Information Security Program Plan).",
                policyState ?? "No policy state available",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect policy evidence for PM");
        }

        // 3. Log — Diagnostic settings for program management monitoring
        try
        {
            var diagnostics = await _azureResourceService.GetDiagnosticSettingsAsync(
                $"/subscriptions/{subscriptionId}", cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Log,
                "Program Management Audit Settings",
                "Diagnostic settings supporting program management monitoring for PM-14 (Testing, Training, and Monitoring).",
                $"Diagnostic settings found: {diagnostics.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect diagnostic settings evidence for PM");
        }

        // 4. Metric — RBAC assignment count as authorization governance metric
        try
        {
            var roleAssignments = await _azureResourceService.GetRoleAssignmentsAsync(
                subscriptionId, cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Metric,
                "Enterprise RBAC Governance Scope",
                "Total RBAC role assignment count as an authorization governance metric for PM-2 (Senior Agency Information Security Officer).",
                $"Enterprise role assignments: {roleAssignments.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect RBAC governance metric evidence for PM");
        }

        // 5. AccessControl — Resource lock count for enterprise change control
        try
        {
            var locks = await _azureResourceService.GetResourceLocksAsync(
                subscriptionId, resourceGroup, cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.AccessControl,
                "Enterprise Change Control Locks",
                "Resource lock inventory enforcing enterprise change control for PM-9 (Risk Management Strategy).",
                $"Enterprise resource locks: {locks.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect resource lock evidence for PM");
        }

        return items;
    }
}

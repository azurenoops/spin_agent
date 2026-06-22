using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.EvidenceCollectors;

/// <summary>
/// Evidence collector for the System and Services Acquisition (SA) family.
/// Collects container registry approved image governance, acquisition policy compliance,
/// acquisition monitoring audit settings, VM image source inventory,
/// and deployment pipeline RBAC governance evidence.
/// </summary>
public class SystemServicesAcquisitionEvidenceCollector : BaseEvidenceCollector
{
    private readonly IAzureResourceService _azureResourceService;
    private readonly IAzurePolicyComplianceService _policyService;

    /// <inheritdoc />
    public override string FamilyCode => ControlFamilies.SystemServicesAcquisition;

    public SystemServicesAcquisitionEvidenceCollector(
        IAzureResourceService azureResourceService,
        IAzurePolicyComplianceService policyService,
        ILogger<SystemServicesAcquisitionEvidenceCollector> logger) : base(logger)
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

        // 1. Configuration — Container registry inventory for approved image governance
        try
        {
            var registries = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.ContainerRegistry/registries", cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Configuration,
                "Container Registry Approved Image Governance",
                "Container registry inventory supporting approved image governance for SA-12 (Supply Chain Protection).",
                $"Container registries found: {registries.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect container registry evidence for SA");
        }

        // 2. Policy — Acquisition and supply chain policy compliance
        try
        {
            var policyState = await _policyService.GetPolicyStatesAsync(
                subscriptionId, cancellationToken: cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Policy,
                "System Acquisition Policy Compliance",
                "Azure Policy compliance state for acquisition and supply chain policies (SA-1 Policy and Procedures).",
                policyState ?? "No policy state available",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect policy evidence for SA");
        }

        // 3. Log — Diagnostic settings for acquisition monitoring
        try
        {
            var diagnostics = await _azureResourceService.GetDiagnosticSettingsAsync(
                $"/subscriptions/{subscriptionId}", cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Log,
                "Acquisition Monitoring Audit Settings",
                "Diagnostic settings supporting acquisition activity monitoring for SA-10 (Developer Configuration Management).",
                $"Diagnostic settings found: {diagnostics.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect diagnostic settings evidence for SA");
        }

        // 4. Metric — VM image source distribution
        try
        {
            var virtualMachines = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.Compute/virtualMachines", cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Metric,
                "VM Image Source Inventory",
                "Virtual machine inventory representing VM image source distribution for SA-4 (Acquisition Process).",
                $"Virtual machines found: {virtualMachines.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect VM image source metric evidence for SA");
        }

        // 5. AccessControl — Role assignments for deployment pipeline governance
        try
        {
            var roleAssignments = await _azureResourceService.GetRoleAssignmentsAsync(
                subscriptionId, cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.AccessControl,
                "Deployment RBAC Governance",
                "Role assignments governing deployment pipeline access for SA-5 (System Documentation).",
                $"Role assignments for deployment governance: {roleAssignments.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect deployment RBAC evidence for SA");
        }

        return items;
    }
}

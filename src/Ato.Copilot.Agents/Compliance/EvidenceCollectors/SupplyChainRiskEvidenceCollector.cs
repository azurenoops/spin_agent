using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.EvidenceCollectors;

/// <summary>
/// Evidence collector for the Supply Chain Risk Management (SR) family.
/// Collects container registry supply chain inventory, supply chain risk policy compliance,
/// supply chain audit diagnostic settings, third-party service scope metrics,
/// and supply chain tamper protection lock evidence.
/// </summary>
public class SupplyChainRiskEvidenceCollector : BaseEvidenceCollector
{
    private readonly IAzureResourceService _azureResourceService;
    private readonly IAzurePolicyComplianceService _policyService;

    /// <inheritdoc />
    public override string FamilyCode => ControlFamilies.SupplyChainRisk;

    public SupplyChainRiskEvidenceCollector(
        IAzureResourceService azureResourceService,
        IAzurePolicyComplianceService policyService,
        ILogger<SupplyChainRiskEvidenceCollector> logger) : base(logger)
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

        // 1. Configuration — Container registry inventory as supply chain anchor
        try
        {
            var registries = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.ContainerRegistry/registries", cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Configuration,
                "Container Registry Supply Chain Inventory",
                "Container registry inventory as the primary supply chain artifact anchor for SR-3 (Supply Chain Controls and Processes).",
                $"Container registries found: {registries.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect container registry evidence for SR");
        }

        // 2. Policy — Supply chain risk policy compliance
        try
        {
            var policyState = await _policyService.GetPolicyStatesAsync(
                subscriptionId, cancellationToken: cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Policy,
                "Supply Chain Risk Policy Compliance",
                "Azure Policy compliance state for supply chain risk management policies (SR-1 Policy and Procedures).",
                policyState ?? "No policy state available",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect policy evidence for SR");
        }

        // 3. Log — Diagnostic settings for supply chain monitoring
        try
        {
            var diagnostics = await _azureResourceService.GetDiagnosticSettingsAsync(
                $"/subscriptions/{subscriptionId}", cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Log,
                "Supply Chain Audit Diagnostic Settings",
                "Subscription diagnostic settings supporting supply chain activity monitoring for SR-8 (Notification Agreements).",
                $"Diagnostic settings found: {diagnostics.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect diagnostic settings evidence for SR");
        }

        // 4. Metric — Web apps / Function Apps as third-party service scope
        try
        {
            var webApps = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.Web/sites", cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Metric,
                "Third-Party Service Scope",
                "Web app and Function App count representing the third-party service integration scope for SR-5 (Acquisition Strategies).",
                $"Web apps and Function Apps found: {webApps.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect third-party service scope metric evidence for SR");
        }

        // 5. AccessControl — Resource locks as supply chain tamper protection
        try
        {
            var locks = await _azureResourceService.GetResourceLocksAsync(
                subscriptionId, resourceGroup, cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.AccessControl,
                "Supply Chain Tamper Protection Locks",
                "Resource locks enforcing supply chain tamper protection for SR-11 (Component Authenticity).",
                $"Supply chain tamper protection locks: {locks.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect resource lock evidence for SR");
        }

        return items;
    }
}

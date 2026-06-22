using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.EvidenceCollectors;

/// <summary>
/// Evidence collector for the PII Processing and Transparency (PT) family.
/// Collects Key Vault encryption inventory, data classification policy compliance,
/// PII access audit diagnostic settings, storage account data store scope,
/// and RBAC assignments scoping PII access.
/// </summary>
public class PiiProcessingEvidenceCollector : BaseEvidenceCollector
{
    private readonly IAzureResourceService _azureResourceService;
    private readonly IAzurePolicyComplianceService _policyService;

    /// <inheritdoc />
    public override string FamilyCode => ControlFamilies.PiiProcessing;

    public PiiProcessingEvidenceCollector(
        IAzureResourceService azureResourceService,
        IAzurePolicyComplianceService policyService,
        ILogger<PiiProcessingEvidenceCollector> logger) : base(logger)
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

        // 1. Configuration — Key Vault inventory as PII encryption evidence
        try
        {
            var keyVaults = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.KeyVault/vaults", cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Configuration,
                "PII Encryption Key Vault Inventory",
                "Key Vault inventory as PII encryption infrastructure evidence for PT-2 (Authority to Process Personally Identifiable Information).",
                $"Key Vaults found: {keyVaults.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect Key Vault inventory evidence for PT");
        }

        // 2. Policy — Data classification and PII policy compliance
        try
        {
            var policyState = await _policyService.GetPolicyStatesAsync(
                subscriptionId, cancellationToken: cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Policy,
                "PII Data Classification Policy Compliance",
                "Azure Policy compliance state for data classification and PII handling policies (PT-1 Policy and Procedures).",
                policyState ?? "No policy state available",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect policy evidence for PT");
        }

        // 3. Log — Storage and Key Vault diagnostic settings
        try
        {
            var diagnostics = await _azureResourceService.GetDiagnosticSettingsAsync(
                $"/subscriptions/{subscriptionId}", cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Log,
                "PII Access Audit Diagnostic Settings",
                "Subscription diagnostic settings supporting PII access auditing for PT-6 (Inspection and Review).",
                $"Diagnostic settings found: {diagnostics.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect diagnostic settings evidence for PT");
        }

        // 4. Metric — Storage account count as PII data store scope
        try
        {
            var storageAccounts = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.Storage/storageAccounts", cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Metric,
                "PII Data Store Inventory",
                "Storage account count representing the PII data store scope for PT-3 (Personally Identifiable Information Processing Purposes).",
                $"Storage accounts in scope: {storageAccounts.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect storage account metric evidence for PT");
        }

        // 5. AccessControl — RBAC assignments scoping PII access
        try
        {
            var roleAssignments = await _azureResourceService.GetRoleAssignmentsAsync(
                subscriptionId, cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.AccessControl,
                "PII Access Control Role Assignments",
                "RBAC role assignments scoping PII data access for PT-5 (Privacy Notice).",
                $"Role assignments governing PII access: {roleAssignments.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect RBAC access control evidence for PT");
        }

        return items;
    }
}

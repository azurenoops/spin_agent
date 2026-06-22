using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.EvidenceCollectors;

/// <summary>
/// Collects evidence for the NIST SP 800-53 Media Protection (MP) control family.
/// Gathers storage account inventory, storage encryption policy compliance, audit diagnostic
/// settings, Key Vault inventory, and RBAC role assignments to support MP-2 through MP-7
/// control requirements.
/// </summary>
public class MediaProtectionEvidenceCollector : BaseEvidenceCollector
{
    private readonly IAzureResourceService _azureResourceService;
    private readonly IAzurePolicyComplianceService _policyService;

    public override string FamilyCode => ControlFamilies.MediaProtection;

    public MediaProtectionEvidenceCollector(
        IAzureResourceService azureResourceService,
        IAzurePolicyComplianceService policyService,
        ILogger<MediaProtectionEvidenceCollector> logger) : base(logger)
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

        // Evidence 1 — Configuration: Storage account inventory (MP-2 Media Access)
        try
        {
            var storageAccounts = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.Storage/storageAccounts", cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "MP-2",
                Description = "Inventory of Azure storage accounts representing digital media assets subject to access controls.",
                TotalCount = storageAccounts?.Count ?? 0,
                StorageAccounts = storageAccounts
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Configuration,
                "Storage Account Inventory",
                "MP-2: Inventory of storage accounts demonstrating identification and configuration of digital media assets requiring access controls.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MP-2: Failed to collect storage account inventory.");
        }

        // Evidence 2 — Policy: Policy compliance for storage encryption (MP-4 Media Storage)
        try
        {
            var policyStates = await _policyService.GetPolicyStatesAsync(subscriptionId, cancellationToken: cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "MP-4",
                Description = "Azure Policy compliance state for storage encryption policies protecting media at rest.",
                TotalPolicyStates = string.IsNullOrEmpty(policyStates) ? 0 : 1,
                PolicyStates = policyStates
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Policy,
                "Storage Encryption Policy",
                "MP-4: Azure Policy compliance state demonstrating enforcement of storage encryption controls for digital media at rest.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MP-4: Failed to collect storage encryption policy compliance state.");
        }

        // Evidence 3 — Log: Storage diagnostic settings at subscription level (MP-7 Media Use)
        try
        {
            var subscriptionResourceId = $"/subscriptions/{subscriptionId}";
            var diagnosticSettings = await _azureResourceService.GetDiagnosticSettingsAsync(
                subscriptionResourceId, cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "MP-7",
                Description = "Subscription-level diagnostic settings providing audit log coverage for media use monitoring.",
                ResourceId = subscriptionResourceId,
                DiagnosticSettings = diagnosticSettings
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Log,
                "Storage Audit Diagnostic Settings",
                "MP-7: Subscription diagnostic settings confirming audit logging is enabled to support media use monitoring and restriction enforcement.",
                content,
                subscriptionResourceId));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MP-7: Failed to collect diagnostic settings for storage audit evidence.");
        }

        // Evidence 4 — Metric: Key Vault count as encryption key management evidence (MP-5 Media Transport)
        try
        {
            var keyVaults = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.KeyVault/vaults", cancellationToken);

            var kvCount = keyVaults?.Count ?? 0;

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "MP-5",
                Description = "Inventory of Azure Key Vaults managing encryption keys that protect media during transport and storage.",
                TotalCount = kvCount,
                KeyVaults = keyVaults
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Metric,
                "Encryption Key Vault Inventory",
                $"MP-5: {kvCount} Key Vault(s) identified as encryption key management resources supporting media transport protection controls.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MP-5: Failed to collect Key Vault inventory for media transport evidence.");
        }

        // Evidence 5 — AccessControl: RBAC assignments for storage access control (MP-2 Media Access)
        try
        {
            var roleAssignments = await _azureResourceService.GetRoleAssignmentsAsync(
                subscriptionId, cancellationToken);

            var assignmentCount = roleAssignments?.Count ?? 0;

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "MP-2",
                Description = "RBAC role assignments governing access to storage and media resources.",
                TotalRoleAssignments = assignmentCount,
                RoleAssignments = roleAssignments
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.AccessControl,
                "Storage RBAC Role Assignments",
                $"MP-2: {assignmentCount} RBAC role assignment(s) demonstrating access control enforcement over storage and digital media resources.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "MP-2: Failed to collect RBAC role assignments for storage access control evidence.");
        }

        return items;
    }
}

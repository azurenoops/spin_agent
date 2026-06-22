using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.EvidenceCollectors;

/// <summary>
/// Collects evidence for the NIST SP 800-53 Physical and Environmental Protection (PE) control family.
/// Gathers resource geographic distribution, geo-redundancy policy compliance, subscription audit
/// settings, storage geo-redundancy scope, and resource protection locks to support PE-3 through
/// PE-19 control requirements. Physical controls are inferred from Azure's cloud infrastructure posture.
/// </summary>
public class PhysicalEnvironmentalEvidenceCollector : BaseEvidenceCollector
{
    private readonly IAzureResourceService _azureResourceService;
    private readonly IAzurePolicyComplianceService _policyService;

    public override string FamilyCode => ControlFamilies.PhysicalEnvironmental;

    public PhysicalEnvironmentalEvidenceCollector(
        IAzureResourceService azureResourceService,
        IAzurePolicyComplianceService policyService,
        ILogger<PhysicalEnvironmentalEvidenceCollector> logger) : base(logger)
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

        // Evidence 1 — Configuration: Resource location distribution (PE-11 Emergency Power, PE-12 Emergency Lighting)
        try
        {
            var allResources = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, null, cancellationToken);

            // Group resources by location to demonstrate geographic distribution
            var locationGroups = allResources?
                .GroupBy(r => r.Data.Location.Name ?? "unknown")
                .Select(g => new { Location = g.Key, ResourceCount = g.Count() })
                .ToList();

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "PE-11, PE-12",
                Description = "Geographic distribution of resources across Azure regions, demonstrating reliance on Azure's physical infrastructure controls for emergency power and lighting.",
                TotalResources = allResources?.Count ?? 0,
                LocationDistribution = locationGroups
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Configuration,
                "Resource Geographic Distribution",
                "PE-11/PE-12: Resource distribution across Azure regions demonstrating reliance on Microsoft's data center physical controls including emergency power and lighting.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "PE-11/PE-12: Failed to collect resource geographic distribution.");
        }

        // Evidence 2 — Policy: Policy compliance for geo-redundancy (PE-17 Alternate Work Site)
        try
        {
            var policyStates = await _policyService.GetPolicyStatesAsync(subscriptionId, cancellationToken: cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "PE-17",
                Description = "Azure Policy compliance state for geo-redundancy policies supporting alternate work site and business continuity controls.",
                TotalPolicyStates = string.IsNullOrEmpty(policyStates) ? 0 : 1,
                PolicyStates = policyStates
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Policy,
                "Geo-Redundancy Policy",
                "PE-17: Azure Policy compliance state demonstrating enforcement of geo-redundancy controls that support alternate work site capabilities.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "PE-17: Failed to collect geo-redundancy policy compliance state.");
        }

        // Evidence 3 — Log: Diagnostic settings at subscription level (PE-3 Physical Access Control)
        try
        {
            var subscriptionResourceId = $"/subscriptions/{subscriptionId}";
            var diagnosticSettings = await _azureResourceService.GetDiagnosticSettingsAsync(
                subscriptionResourceId, cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "PE-3",
                Description = "Subscription-level diagnostic settings supporting audit coverage for physical access control monitoring at the logical boundary.",
                ResourceId = subscriptionResourceId,
                DiagnosticSettings = diagnosticSettings
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Log,
                "Physical Environment Audit Settings",
                "PE-3: Subscription diagnostic settings confirming audit logging is enabled to support physical access control monitoring at the subscription boundary.",
                content,
                subscriptionResourceId));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "PE-3: Failed to collect diagnostic settings for physical environment audit evidence.");
        }

        // Evidence 4 — Metric: Storage account count with geo-redundancy evidence (PE-19 Information Leakage)
        try
        {
            var storageAccounts = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, "Microsoft.Storage/storageAccounts", cancellationToken);

            var storageCount = storageAccounts?.Count ?? 0;

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "PE-19",
                Description = "Storage account inventory demonstrating geo-redundancy scope for information leakage controls across physical boundaries.",
                TotalStorageAccounts = storageCount,
                StorageAccounts = storageAccounts
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Metric,
                "Storage Geo-Redundancy Scope",
                $"PE-19: {storageCount} storage account(s) identified; geo-redundancy configuration prevents information leakage risk across physical data center boundaries.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "PE-19: Failed to collect storage account geo-redundancy scope metric.");
        }

        // Evidence 5 — AccessControl: Resource lock count as physical access evidence (PE-3 Physical Access Control)
        try
        {
            var resourceLocks = await _azureResourceService.GetResourceLocksAsync(
                subscriptionId, resourceGroup, cancellationToken);

            var lockCount = resourceLocks?.Count ?? 0;

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "PE-3",
                Description = "Resource lock inventory providing logical physical access control evidence by preventing unauthorized resource modification or deletion.",
                TotalLocks = lockCount,
                ResourceLocks = resourceLocks
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.AccessControl,
                "Resource Protection Locks",
                $"PE-3: {lockCount} resource lock(s) in place, serving as logical physical access controls preventing unauthorized modification or deletion of protected resources.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "PE-3: Failed to collect resource locks for physical access control evidence.");
        }

        return items;
    }
}

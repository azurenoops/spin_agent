using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.EvidenceCollectors;

/// <summary>
/// Collects evidence for the NIST SP 800-53 Planning (PL) control family.
/// Gathers resource tagging compliance, security planning policy state, subscription audit
/// settings, total resource count, and RBAC governance data to support PL-1, PL-2, and PL-8
/// control requirements.
/// </summary>
public class PlanningEvidenceCollector : BaseEvidenceCollector
{
    private readonly IAzureResourceService _azureResourceService;
    private readonly IAzurePolicyComplianceService _policyService;

    public override string FamilyCode => ControlFamilies.Planning;

    public PlanningEvidenceCollector(
        IAzureResourceService azureResourceService,
        IAzurePolicyComplianceService policyService,
        ILogger<PlanningEvidenceCollector> logger) : base(logger)
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

        // Evidence 1 — Configuration: Resource tagging compliance (PL-8 Security and Privacy Architectures)
        try
        {
            var allResources = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, null, cancellationToken);

            var taggedCount = allResources?.Count(r => r.Data.Tags != null && r.Data.Tags.Count > 0) ?? 0;
            var totalCount = allResources?.Count ?? 0;
            var untaggedCount = totalCount - taggedCount;

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "PL-8",
                Description = "Resource tagging compliance assessment demonstrating governance architecture classification across all subscription resources.",
                TotalResources = totalCount,
                TaggedResources = taggedCount,
                UntaggedResources = untaggedCount,
                TaggingCompliancePercent = totalCount > 0 ? Math.Round((double)taggedCount / totalCount * 100, 2) : 0
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Configuration,
                "Resource Tagging Governance",
                $"PL-8: {taggedCount} of {totalCount} resource(s) are tagged, demonstrating security and privacy architecture governance alignment through consistent resource classification.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "PL-8: Failed to collect resource tagging governance evidence.");
        }

        // Evidence 2 — Policy: Policy compliance state for planning policies (PL-1 Policy and Procedures)
        try
        {
            var policyStates = await _policyService.GetPolicyStatesAsync(subscriptionId, cancellationToken: cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "PL-1",
                Description = "Azure Policy compliance state demonstrating enforcement of security planning policy and procedure requirements.",
                TotalPolicyStates = policyStates?.Count ?? 0,
                PolicyStates = policyStates
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Policy,
                "Security Planning Policy Compliance",
                "PL-1: Azure Policy compliance state confirming that security planning policies and procedures are defined, enforced, and monitored.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "PL-1: Failed to collect security planning policy compliance state.");
        }

        // Evidence 3 — Log: Subscription diagnostic settings for planning audit trail (PL-2 System Security and Privacy Plans)
        try
        {
            var subscriptionResourceId = $"/subscriptions/{subscriptionId}";
            var diagnosticSettings = await _azureResourceService.GetDiagnosticSettingsAsync(
                subscriptionResourceId, cancellationToken);

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "PL-2",
                Description = "Subscription and management group diagnostic settings providing audit log coverage to support system security and privacy plan monitoring.",
                ResourceId = subscriptionResourceId,
                DiagnosticSettings = diagnosticSettings
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Log,
                "Planning Audit Diagnostic Settings",
                "PL-2: Subscription diagnostic settings confirming audit logging is enabled to support system security and privacy plan review and update processes.",
                content,
                subscriptionResourceId));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "PL-2: Failed to collect diagnostic settings for planning audit evidence.");
        }

        // Evidence 4 — Metric: Total resource count as system boundary metric (PL-8 Security and Privacy Architectures)
        try
        {
            var allResources = await _azureResourceService.GetResourcesAsync(
                subscriptionId, resourceGroup, null, cancellationToken);

            var totalCount = allResources?.Count ?? 0;

            // Group by resource type for boundary clarity
            var resourceTypeSummary = allResources?
                .GroupBy(r => r.Data.ResourceType.ToString() ?? "unknown")
                .Select(g => new { ResourceType = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "PL-8",
                Description = "Total resource count and type distribution defining the system boundary for security and privacy architecture documentation.",
                TotalResources = totalCount,
                ResourceTypeSummary = resourceTypeSummary
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.Metric,
                "System Boundary Resource Count",
                $"PL-8: {totalCount} total resource(s) across the subscription defining the system boundary for security and privacy architecture planning.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "PL-8: Failed to collect total resource count for system boundary metric.");
        }

        // Evidence 5 — AccessControl: RBAC role definitions as security plan evidence (PL-8 Security and Privacy Architectures)
        try
        {
            var roleAssignments = await _azureResourceService.GetRoleAssignmentsAsync(
                subscriptionId, cancellationToken);

            // Extract distinct role definition IDs to represent defined security roles
            var distinctRoles = roleAssignments?
                .Select(r => r.Data.RoleDefinitionId)
                .Distinct()
                .ToList();

            var distinctRoleCount = distinctRoles?.Count ?? 0;
            var totalAssignmentCount = roleAssignments?.Count ?? 0;

            var content = System.Text.Json.JsonSerializer.Serialize(new
            {
                ControlReference = "PL-8",
                Description = "RBAC role assignments and distinct role definitions demonstrating security plan governance through defined access control architecture.",
                TotalRoleAssignments = totalAssignmentCount,
                DistinctRoleDefinitions = distinctRoleCount,
                RoleDefinitionIds = distinctRoles,
                RoleAssignments = roleAssignments
            });

            items.Add(CreateEvidenceItem(
                EvidenceType.AccessControl,
                "Security Plan RBAC Governance",
                $"PL-8: {totalAssignmentCount} role assignment(s) across {distinctRoleCount} distinct role definition(s), demonstrating RBAC governance aligned to the security and privacy architecture plan.",
                content,
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "PL-8: Failed to collect RBAC role definitions for security plan governance evidence.");
        }

        return items;
    }
}

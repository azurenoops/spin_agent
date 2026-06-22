using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.EvidenceCollectors;

/// <summary>
/// Evidence collector for the Personnel Security (PS) family.
/// Collects RBAC personnel authorization assignments, personnel policy compliance,
/// personnel activity audit settings, job function role separation metrics,
/// and sensitive resource access lock evidence.
/// </summary>
public class PersonnelSecurityEvidenceCollector : BaseEvidenceCollector
{
    private readonly IAzureResourceService _azureResourceService;
    private readonly IAzurePolicyComplianceService _policyService;

    /// <inheritdoc />
    public override string FamilyCode => ControlFamilies.PersonnelSecurity;

    public PersonnelSecurityEvidenceCollector(
        IAzureResourceService azureResourceService,
        IAzurePolicyComplianceService policyService,
        ILogger<PersonnelSecurityEvidenceCollector> logger) : base(logger)
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

        // 1. Configuration — RBAC role assignments as personnel authorization evidence
        try
        {
            var roleAssignments = await _azureResourceService.GetRoleAssignmentsAsync(
                subscriptionId, cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Configuration,
                "Personnel Authorization Role Assignments",
                "RBAC role assignment snapshot as personnel authorization evidence for PS-4 (Personnel Termination).",
                $"Total personnel role assignments: {roleAssignments.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect role assignment evidence for PS");
        }

        // 2. Policy — Conditional access and personnel policy compliance
        try
        {
            var policyState = await _policyService.GetPolicyStatesAsync(
                subscriptionId, cancellationToken: cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Policy,
                "Personnel Security Policy Compliance",
                "Azure Policy compliance state for conditional access and personnel policies (PS-1 Policy and Procedures).",
                policyState ?? "No policy state available",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect policy evidence for PS");
        }

        // 3. Log — Subscription diagnostic settings for personnel activity tracking
        try
        {
            var diagnostics = await _azureResourceService.GetDiagnosticSettingsAsync(
                $"/subscriptions/{subscriptionId}", cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.Log,
                "Personnel Activity Audit Settings",
                "Subscription diagnostic settings supporting personnel activity tracking for PS-8 (Personnel Sanctions).",
                $"Diagnostic settings found: {diagnostics.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect diagnostic settings evidence for PS");
        }

        // 4. Metric — Distinct role count as job function separation metric
        try
        {
            var roleAssignments = await _azureResourceService.GetRoleAssignmentsAsync(
                subscriptionId, cancellationToken);
            var distinctRoleCount = roleAssignments
                .Select(r => r.Data.RoleDefinitionId?.ToString() ?? "unknown")
                .Distinct()
                .Count();
            items.Add(CreateEvidenceItem(
                EvidenceType.Metric,
                "Job Function Role Separation Metric",
                "Distinct role definition count as a job function separation metric for PS-2 (Position Risk Designation).",
                $"Distinct role definitions in use: {distinctRoleCount}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect role separation metric evidence for PS");
        }

        // 5. AccessControl — Resource lock count for sensitive resource protection
        try
        {
            var locks = await _azureResourceService.GetResourceLocksAsync(
                subscriptionId, resourceGroup, cancellationToken);
            items.Add(CreateEvidenceItem(
                EvidenceType.AccessControl,
                "Sensitive Resource Access Locks",
                "Resource locks protecting sensitive resources for PS-6 (Access Agreements).",
                $"Sensitive resource locks: {locks.Count}",
                $"/subscriptions/{subscriptionId}"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to collect resource lock evidence for PS");
        }

        return items;
    }
}

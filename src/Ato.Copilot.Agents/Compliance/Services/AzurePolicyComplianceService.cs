using System.Text.Json;
using Azure.ResourceManager;
using Azure.ResourceManager.PolicyInsights;
using Azure.ResourceManager.PolicyInsights.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Azure Policy compliance integration service.
/// Queries policy compliance states via PolicyInsights SDK,
/// maps PolicyDefinitionGroups to NIST control IDs, paginates results.
/// </summary>
public class AzurePolicyComplianceService : IAzurePolicyComplianceService
{
    private readonly ArmClient _armClient;
    private readonly ILogger<AzurePolicyComplianceService> _logger;
    private readonly CanonicalEnvironmentCollectionGuard? _environmentGuard;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzurePolicyComplianceService"/> class.
    /// </summary>
    public AzurePolicyComplianceService(
        ArmClient armClient,
        ILogger<AzurePolicyComplianceService> logger,
        CanonicalEnvironmentCollectionGuard? environmentGuard = null)
    {
        _armClient = armClient;
        _logger = logger;
        _environmentGuard = environmentGuard;
    }

    /// <inheritdoc />
    public async Task<string> GetComplianceSummaryAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (_environmentGuard is not null)
            await _environmentGuard.EnsureSubscriptionAsync(subscriptionId, EnvironmentScopePurpose.Assessment, cancellationToken);
        try
        {
            _logger.LogInformation("Getting policy compliance summary for subscription {SubId}", subscriptionId);

            var subResource = _armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}"));

            var states = subResource.GetPolicyStateQueryResultsAsync(PolicyStateType.Latest, null, cancellationToken);

            int compliant = 0, nonCompliant = 0, unknown = 0, total = 0;

            await foreach (var state in states)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total++;

                var complianceState = state.ComplianceState?.ToLowerInvariant();
                switch (complianceState)
                {
                    case "compliant":
                        compliant++;
                        break;
                    case "noncompliant":
                        nonCompliant++;
                        break;
                    default:
                        unknown++;
                        break;
                }

                // Safety limit to avoid excessive enumeration
                if (total >= 10000) break;
            }

            var summary = new
            {
                subscriptionId,
                totalPolicies = total,
                compliant,
                nonCompliant,
                unknown,
                compliancePercentage = total > 0
                    ? Math.Round((double)compliant / total * 100, 2)
                    : 0.0,
                evaluatedAt = DateTime.UtcNow
            };

            return JsonSerializer.Serialize(summary, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get policy compliance summary for {SubId}", subscriptionId);
            return JsonSerializer.Serialize(new
            {
                status = "error",
                error = new
                {
                    errorCode = "POLICY_SCAN_FAILED",
                    message = $"Policy compliance query failed: {ex.Message}",
                    suggestion = "Verify that 'Policy Reader' role is assigned on the subscription and Azure Policy service is accessible."
                },
                metadata = new { source = "AzurePolicyComplianceService", subscriptionId }
            });
        }
    }

    /// <inheritdoc />
    public async Task<string> GetPolicyStatesAsync(
        string subscriptionId,
        string? policyDefinitionId = null,
        CancellationToken cancellationToken = default)
    {
        if (_environmentGuard is not null)
            await _environmentGuard.EnsureSubscriptionAsync(subscriptionId, EnvironmentScopePurpose.Assessment, cancellationToken);
        try
        {
            _logger.LogInformation("Getting policy states for subscription {SubId}, filter: {Filter}",
                subscriptionId, policyDefinitionId ?? "none");

            var subResource = _armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}"));

            var states = subResource.GetPolicyStateQueryResultsAsync(PolicyStateType.Latest, null, cancellationToken);

            var stateList = new List<object>();
            int processed = 0;

            await foreach (var state in states)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Filter by policy definition if specified
                if (!string.IsNullOrEmpty(policyDefinitionId) &&
                    !string.Equals(state.PolicyDefinitionId, policyDefinitionId, StringComparison.OrdinalIgnoreCase))
                    continue;

                stateList.Add(new
                {
                    policyDefinitionId = state.PolicyDefinitionId,
                    policyAssignmentId = state.PolicyAssignmentId,
                    complianceState = state.ComplianceState,
                    resourceId = state.ResourceId,
                    resourceType = state.ResourceTypeString,
                    policyDefinitionGroupNames = state.PolicyDefinitionGroupNames?.ToList() ?? new List<string>(),
                    timestamp = state.Timestamp
                });

                processed++;
                if (processed >= 5000) break; // pagination safety
            }

            var result = new
            {
                subscriptionId,
                totalStates = stateList.Count,
                states = stateList,
                truncated = processed >= 5000,
                evaluatedAt = DateTime.UtcNow
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get policy states for {SubId}", subscriptionId);
            return JsonSerializer.Serialize(new
            {
                status = "error",
                error = new
                {
                    errorCode = "POLICY_SCAN_FAILED",
                    message = $"Policy states query failed: {ex.Message}",
                    suggestion = "Check network connectivity and ensure the subscription ID is valid. Retry in a few seconds if this is a transient error."
                },
                metadata = new { source = "AzurePolicyComplianceService", subscriptionId }
            });
        }
    }

    /// <summary>
    /// Maps policy definition group names to NIST control IDs.
    /// Azure built-in NIST initiatives use group names like "NIST_SP_800-53_Rev._5_AC-2".
    /// </summary>
    /// <param name="groupNames">Policy definition group names.</param>
    /// <returns>List of mapped NIST control IDs.</returns>
    public static List<string> MapGroupsToNistControls(IEnumerable<string> groupNames)
    {
        var controls = new List<string>();
        foreach (var group in groupNames)
        {
            // Pattern: NIST_SP_800-53_Rev._5_AC-2 → AC-2
            var parts = group.Split('_');
            if (parts.Length >= 2)
            {
                var lastPart = parts[^1];
                // Check if it looks like a control ID (e.g., AC-2, AU-3)
                if (lastPart.Length >= 3 && lastPart.Contains('-'))
                {
                    var family = lastPart.Split('-')[0].ToUpperInvariant();
                    if (ControlFamilies.IsValidFamily(family))
                    {
                        controls.Add(lastPart.ToUpperInvariant());
                    }
                }
            }
        }
        return controls;
    }
}

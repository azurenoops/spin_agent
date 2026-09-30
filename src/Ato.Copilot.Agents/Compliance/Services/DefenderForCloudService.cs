using System.Text.Json;
using Azure.ResourceManager;
using Azure.ResourceManager.SecurityCenter;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Microsoft Defender for Cloud integration service.
/// Queries secure score, regulatory compliance standards/controls/assessments,
/// maps recommendations to NIST 800-53 controls.
/// </summary>
public class DefenderForCloudService : IDefenderForCloudService
{
    private readonly ArmClient _armClient;
    private readonly ILogger<DefenderForCloudService> _logger;
    private readonly CanonicalEnvironmentCollectionGuard? _environmentGuard;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="DefenderForCloudService"/> class.
    /// </summary>
    public DefenderForCloudService(
        ArmClient armClient,
        ILogger<DefenderForCloudService> logger,
        CanonicalEnvironmentCollectionGuard? environmentGuard = null)
    {
        _armClient = armClient;
        _logger = logger;
        _environmentGuard = environmentGuard;
    }

    /// <inheritdoc />
    public async Task<string> GetSecureScoreAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (_environmentGuard is not null)
            await _environmentGuard.EnsureSubscriptionAsync(subscriptionId, EnvironmentScopePurpose.Assessment, cancellationToken);
        try
        {
            _logger.LogInformation("Getting secure score for subscription {SubId}", subscriptionId);

            var subResource = _armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}"));

            var secureScores = subResource.GetSecureScores();
            var scoreList = new List<object>();

            await foreach (var score in secureScores.GetAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                scoreList.Add(new
                {
                    id = score.Data.Id?.ToString(),
                    name = score.Data.Name,
                    currentScore = score.Data.Current,
                    maxScore = score.Data.Max,
                    percentage = score.Data.Percentage,
                    weight = score.Data.Weight
                });
            }

            var result = new
            {
                subscriptionId,
                scores = scoreList,
                evaluatedAt = DateTime.UtcNow
            };

            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get secure score for {SubId}", subscriptionId);
            return JsonSerializer.Serialize(new
            {
                status = "error",
                error = new
                {
                    errorCode = "DEFENDER_SCAN_FAILED",
                    message = $"Defender for Cloud secure score query failed: {ex.Message}",
                    suggestion = "Verify that 'Security Reader' role is assigned and Microsoft Defender for Cloud is enabled on this subscription."
                },
                metadata = new { source = "DefenderForCloudService", subscriptionId }
            }, JsonOptions);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetAssessmentsAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (_environmentGuard is not null)
            await _environmentGuard.EnsureSubscriptionAsync(subscriptionId, EnvironmentScopePurpose.Assessment, cancellationToken);
        try
        {
            _logger.LogInformation("Getting security assessments for subscription {SubId}", subscriptionId);

            var subResource = _armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}"));

            var assessmentPages = _armClient.GetSecurityAssessmentsAsync(subResource.Id, cancellationToken);
            var assessmentList = new List<object>();
            int count = 0;

            await foreach (var assessment in assessmentPages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                assessmentList.Add(new
                {
                    id = assessment.Data.Id?.ToString(),
                    name = assessment.Data.Name,
                    displayName = assessment.Data.DisplayName,
                    status = assessment.Data.Status?.Code.ToString(),
                    resourceDetails = assessment.Data.ResourceDetails?.GetType().Name
                });

                count++;
                if (count >= 5000) break;
            }

            var result = new
            {
                subscriptionId,
                totalAssessments = assessmentList.Count,
                assessments = assessmentList,
                truncated = count >= 5000,
                evaluatedAt = DateTime.UtcNow
            };

            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get security assessments for {SubId}", subscriptionId);
            return JsonSerializer.Serialize(new
            {
                status = "error",
                error = new
                {
                    errorCode = "DEFENDER_SCAN_FAILED",
                    message = $"Security assessments query failed: {ex.Message}",
                    suggestion = "Ensure 'Security Reader' role is assigned and Defender for Cloud is enabled on this subscription."
                },
                metadata = new { source = "DefenderForCloudService", subscriptionId }
            }, JsonOptions);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetRecommendationsAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (_environmentGuard is not null)
            await _environmentGuard.EnsureSubscriptionAsync(subscriptionId, EnvironmentScopePurpose.Assessment, cancellationToken);
        try
        {
            _logger.LogInformation("Getting security recommendations for subscription {SubId}", subscriptionId);

            var subResource = _armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}"));

            // Use security assessments for recommendations (they include recommendation data)
            var assessmentPages = _armClient.GetSecurityAssessmentsAsync(subResource.Id, cancellationToken);
            var recommendations = new List<object>();
            int count = 0;

            await foreach (var assessment in assessmentPages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var statusCode = assessment.Data.Status?.Code.ToString();
                // Only include unhealthy/not-applicable assessments as recommendations
                if (statusCode != "Unhealthy" && statusCode != "NotApplicable")
                    continue;

                recommendations.Add(new
                {
                    id = assessment.Data.Id?.ToString(),
                    name = assessment.Data.Name,
                    displayName = assessment.Data.DisplayName,
                    status = statusCode,
                    description = assessment.Data.Status?.Description,
                    resourceId = assessment.Data.ResourceDetails?.GetType().Name
                });

                count++;
                if (count >= 2000) break;
            }

            var result = new
            {
                subscriptionId,
                totalRecommendations = recommendations.Count,
                recommendations,
                truncated = count >= 2000,
                evaluatedAt = DateTime.UtcNow
            };

            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get security recommendations for {SubId}", subscriptionId);
            return JsonSerializer.Serialize(new
            {
                status = "error",
                error = new
                {
                    errorCode = "DEFENDER_SCAN_FAILED",
                    message = $"Security recommendations query failed: {ex.Message}",
                    suggestion = "Check that the subscription has Defender for Cloud enabled and your credentials have 'Security Reader' access."
                },
                metadata = new { source = "DefenderForCloudService", subscriptionId }
            }, JsonOptions);
        }
    }

    /// <summary>
    /// Maps a Defender for Cloud recommendation to NIST control IDs.
    /// Uses recommendation name patterns and metadata to infer control mapping.
    /// </summary>
    /// <param name="recommendationName">Defender recommendation name.</param>
    /// <returns>List of NIST control IDs this recommendation maps to.</returns>
    public static List<string> MapRecommendationToNistControls(string recommendationName)
    {
        var controls = new List<string>();
        var lower = recommendationName.ToLowerInvariant();

        // Common Defender → NIST mappings based on recommendation categories
        if (lower.Contains("encryption") || lower.Contains("tls") || lower.Contains("https"))
            controls.Add("SC-8");
        if (lower.Contains("mfa") || lower.Contains("multi-factor"))
            controls.Add("IA-2");
        if (lower.Contains("access") || lower.Contains("rbac") || lower.Contains("role"))
            controls.Add("AC-2");
        if (lower.Contains("log") || lower.Contains("audit") || lower.Contains("diagnostic"))
            controls.Add("AU-3");
        if (lower.Contains("network") || lower.Contains("firewall") || lower.Contains("nsg"))
            controls.Add("SC-7");
        if (lower.Contains("patch") || lower.Contains("update") || lower.Contains("vulnerability"))
            controls.Add("SI-2");
        if (lower.Contains("backup") || lower.Contains("recovery"))
            controls.Add("CP-9");
        if (lower.Contains("key") || lower.Contains("secret") || lower.Contains("vault"))
            controls.Add("SC-12");

        return controls;
    }
}

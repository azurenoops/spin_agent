using Azure;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.PolicyInsights;
using Azure.ResourceManager.PolicyInsights.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using Azure.ResourceManager.SecurityCenter;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Performs bounded, read-only checks against the deployment's assessment ARM client.</summary>
public sealed class AzureAssessmentConnectionProbe : IAzureAssessmentConnectionProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private const int ProbePageSize = 1;
    private readonly ArmClient _armClient;
    private readonly ILogger<AzureAssessmentConnectionProbe> _logger;

    /// <summary>Creates the access probe using the same client as the assessment collectors.</summary>
    /// <param name="armClient">Configured deployment client; never an opposite-cloud fallback.</param>
    /// <param name="logger">Structured logger for safe error classification.</param>
    public AzureAssessmentConnectionProbe(ArmClient armClient, ILogger<AzureAssessmentConnectionProbe> logger)
    {
        _armClient = armClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task CheckAsync(
        IReadOnlyList<AzureAssessmentSubscription> subscriptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        if (subscriptions.Count == 0)
            throw new ArgumentException("At least one validated Azure subscription is required.", nameof(subscriptions));
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            foreach (var scope in subscriptions)
                await CheckSubscriptionAsync(scope, timeout.Token);
        }
        catch (CredentialUnavailableException)
        {
            throw AuthenticationFailure();
        }
        catch (AuthenticationFailedException)
        {
            throw AuthenticationFailure();
        }
        catch (RequestFailedException failure)
        {
            _logger.LogWarning("Azure assessment access check failed with HTTP status {Status}", failure.Status);
            throw failure.Status switch
            {
                401 => AuthenticationFailure(),
                403 => new AssessmentEnvironmentException(AssessmentEnvironmentErrors.AccessDenied,
                    "The assessment identity cannot read the required Azure services.",
                    "Ask the platform administrator to grant the required ARM, Policy and Defender read access."),
                404 => new AssessmentEnvironmentException(AssessmentEnvironmentErrors.SubscriptionUnavailable,
                    "An Azure subscription or required assessment service is unavailable.",
                    "Refresh the attachment and confirm the subscription and required Azure providers are available."),
                _ => ConnectionFailure()
            };
        }
        catch (HttpRequestException)
        {
            throw ConnectionFailure();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ConnectionFailure();
        }
    }

    private async Task CheckSubscriptionAsync(AzureAssessmentSubscription scope, CancellationToken cancellationToken)
    {
        var subscription = _armClient.GetSubscriptionResource(
            SubscriptionResource.CreateResourceIdentifier(scope.SubscriptionId.ToString()));
        var response = await subscription.GetAsync(cancellationToken);
        if (response.Value.Data.State != SubscriptionState.Enabled ||
            response.Value.Data.TenantId != scope.DirectoryTenantId)
            throw new AssessmentEnvironmentException(AssessmentEnvironmentErrors.SubscriptionUnavailable,
                "The subscription is not enabled or its Azure directory does not match the organization registration.",
                "Refresh Azure Subscription Settings and confirm the subscription's enabled state and directory.");

        await ReadFirstPageAsync(subscription.GetGenericResourcesAsync(
            top: ProbePageSize, cancellationToken: cancellationToken), cancellationToken);
        await ReadFirstPageAsync(subscription.GetPolicyStateQueryResultsAsync(
            PolicyStateType.Latest, new PolicyQuerySettings { Top = ProbePageSize }, cancellationToken), cancellationToken);
        await ReadFirstPageAsync(
            _armClient.GetSecurityAssessmentsAsync(subscription.Id, cancellationToken), cancellationToken);
    }

    private static async Task ReadFirstPageAsync<T>(AsyncPageable<T> values, CancellationToken cancellationToken)
        where T : notnull
    {
        await using var pages = values.AsPages(pageSizeHint: ProbePageSize).GetAsyncEnumerator(cancellationToken);
        await pages.MoveNextAsync();
    }

    private AssessmentEnvironmentException AuthenticationFailure()
    {
        _logger.LogWarning("Azure assessment access check could not authenticate the configured identity");
        return new AssessmentEnvironmentException(AssessmentEnvironmentErrors.AuthenticationRequired,
            "The configured assessment identity cannot authenticate to Azure.",
            "Ask the platform administrator to configure or renew the assessment identity for this deployment's Azure cloud.");
    }

    private AssessmentEnvironmentException ConnectionFailure()
    {
        _logger.LogWarning("Azure assessment access check could not reach a required service within the probe deadline");
        return new AssessmentEnvironmentException(AssessmentEnvironmentErrors.ConnectionUnavailable,
            "Azure assessment connectivity could not be verified.",
            "Check network and Azure service availability, then retry. No documentation-only assessment was run.");
    }
}

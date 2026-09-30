using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.PolicyInsights;
using Azure.ResourceManager.PolicyInsights.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using Azure.ResourceManager.SecurityCenter;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Environments;

/// <summary>Explicit-cloud ARM reads; no default-cloud retry or subscription-wide collection fallback.</summary>
public sealed class SystemEnvironmentAzureSource(ArmClientFactory clients, ILogger<SystemEnvironmentAzureSource> logger)
    : ISystemEnvironmentAzureSource
{
    private ArmClient Client(EnvironmentRegistration identity) => identity.Cloud switch
    {
        "AzureCloud" => clients.GetClient("AzureCloud"),
        "AzureUSGovernment" => clients.GetClient("AzureGovernment"),
        _ => throw new ArgumentException("Unsupported registered Azure cloud.")
    };

    private static async Task<SubscriptionResource> VerifyAsync(ArmClient client, EnvironmentRegistration identity, CancellationToken ct)
    {
        var subscription = client.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(identity.SubscriptionId.ToString()));
        var response = await subscription.GetAsync(ct);
        if (response.Value.Data.TenantId != identity.DirectoryTenantId || response.Value.Data.State != SubscriptionState.Enabled)
            throw new UnauthorizedAccessException("The subscription directory or enabled state differs from canonical registration.");
        return subscription;
    }

    public async Task<IReadOnlyList<EnvironmentResource>> DiscoverAsync(EnvironmentRegistration registration,
        IReadOnlyList<string> permittedScopes, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var client = Client(registration);
            var subscription = await VerifyAsync(client, registration, timeout.Token);
            var result = new Dictionary<string, EnvironmentResource>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in permittedScopes)
            {
                var scope = SystemEnvironmentService.NormalizeResource(registration, raw);
                var id = new ResourceIdentifier(scope);
                if (scope.Split('/').Length > 5)
                {
                    var resource = await client.GetGenericResource(id).GetAsync(timeout.Token);
                    Add(resource.Value);
                }
                else
                {
                    var resources = scope.Split('/').Length == 3
                        ? subscription.GetGenericResourcesAsync(cancellationToken: timeout.Token)
                        : client.GetResourceGroupResource(id).GetGenericResourcesAsync(cancellationToken: timeout.Token);
                    await foreach (var resource in resources.WithCancellation(timeout.Token)) Add(resource);
                }
            }
            return result.Values.OrderBy(x => x.ResourceId).ToArray();

            void Add(GenericResource resource)
            {
                if (result.Count >= 10000) throw new InvalidOperationException("Discovery exceeds the 10,000-resource limit; narrow the source.");
                var id = resource.Id.ToString().ToLowerInvariant();
                if (!permittedScopes.Any(scope => SystemEnvironmentService.Inside(scope, id)))
                    throw new InvalidDataException("Azure returned a resource outside permitted scope.");
                result[id] = new(id, resource.Data.Name, resource.Data.ResourceType.ToString(),
                    resource.Id.ResourceGroupName ?? "", resource.Data.Location.ToString());
            }
        }
        catch (RequestFailedException failure)
        {
            logger.LogWarning("Environment discovery unavailable with ARM status {Status}", failure.Status);
            throw new EnvironmentSourceUnavailableException("Azure resource discovery is unavailable; no empty successful result was substituted.", failure);
        }
        catch (Exception failure) when (failure is AuthenticationFailedException or CredentialUnavailableException or HttpRequestException)
        {
            logger.LogWarning("Environment discovery unavailable: {FailureKind}", failure.GetType().Name);
            throw new EnvironmentSourceUnavailableException("Azure resource discovery could not authenticate or connect.", failure);
        }
        catch (OperationCanceledException failure) when (!ct.IsCancellationRequested)
        {
            throw new EnvironmentSourceUnavailableException("Azure resource discovery timed out.", failure);
        }
    }

    public async Task<EnvironmentCheckState> CheckAccessAsync(ResolvedSystemEnvironmentScope scope,
        EnvironmentScopePurpose purpose, CancellationToken ct = default)
    {
        var ids = purpose == EnvironmentScopePurpose.Assessment
            ? new[] { "arm", "policy", "defender" } : new[] { "arm", "activity-log" };
        var checks = ids.Select(id => new EnvironmentSourceCheck(id, "Access", "NotChecked", true, null, null,
            "This required service has not been checked.", "SOURCE_NOT_CHECKED",
            $"{scope.ScopeRevisionId:D}:{scope.ScopeVersion}", null)).ToArray();
        var current = 0;
        if (!scope.Eligible || scope.ResourceIds.Count == 0)
            return Result("Blocked", scope.IneligibleReason ?? "No exact resource selection.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            Start(0);
            var client = Client(scope.Registration);
            var subscription = await VerifyAsync(client, scope.Registration, timeout.Token);
            foreach (var id in scope.ResourceIds)
            {
                var exact = SystemEnvironmentService.NormalizeResource(scope.Registration, id, true);
                await client.GetGenericResource(new ResourceIdentifier(exact)).GetAsync(timeout.Token);
            }
            Succeeded();
            if (purpose == EnvironmentScopePurpose.Assessment)
            {
                Start(1);
                foreach (var id in scope.ResourceIds)
                {
                    var exact = SystemEnvironmentService.NormalizeResource(scope.Registration, id, true);
                    await FirstPageAsync(subscription.GetPolicyStateQueryResultsAsync(PolicyStateType.Latest,
                        new PolicyQuerySettings { Top = 1, Filter = $"ResourceId eq '{exact.Replace("'", "''")}'" }, timeout.Token), timeout.Token);
                }
                Succeeded();
                Start(2);
                foreach (var id in scope.ResourceIds)
                {
                    var exact = SystemEnvironmentService.NormalizeResource(scope.Registration, id, true);
                    await FirstPageAsync(client.GetSecurityAssessmentsAsync(new ResourceIdentifier(exact), timeout.Token), timeout.Token);
                }
                Succeeded();
            }
            return checks.All(x => x.State == "Available")
                ? Result("Available", null)
                : Result("NotChecked", "ARM access was verified, but required telemetry-service checks remain incomplete. This is not collection health.");
        }
        catch (RequestFailedException failure)
        {
            logger.LogWarning("Environment {Purpose} access failed with ARM status {Status}", purpose, failure.Status);
            return Failed(failure.Status is 401 or 403 ? "Denied" : "Unavailable", "AZURE_ACCESS_CHECK_FAILED",
                "Required Azure source access could not be verified.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failed("Denied", "AZURE_IDENTITY_MISMATCH", "Canonical Azure directory or enabled state does not match.");
        }
        catch (Exception failure) when (failure is AuthenticationFailedException or CredentialUnavailableException or HttpRequestException)
        {
            logger.LogWarning("Environment access unavailable: {FailureKind}", failure.GetType().Name);
            return Failed("Unavailable", "AZURE_CONNECTION_UNAVAILABLE", "The configured Azure identity or connection is unavailable.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failed("Unavailable", "AZURE_ACCESS_TIMEOUT", "The Azure access check timed out; later services were not checked.");
        }

        void Start(int index)
        {
            current = index;
            checks[index] = checks[index] with { AttemptedAt = DateTimeOffset.UtcNow };
        }
        void Succeeded() => checks[current] = checks[current] with
        { State = "Available", LastSucceededAt = DateTimeOffset.UtcNow, Reason = null, ErrorCode = null };
        EnvironmentCheckState Result(string state, string? reason) => new(state, DateTimeOffset.UtcNow, reason) { Sources = checks };
        EnvironmentCheckState Failed(string state, string code, string reason)
        {
            checks[current] = checks[current] with { State = state, Reason = reason, ErrorCode = code };
            return Result(state, reason);
        }
    }

    private static async Task FirstPageAsync<T>(AsyncPageable<T> values, CancellationToken ct) where T : notnull
    {
        await using var pages = values.AsPages(pageSizeHint: 1).GetAsyncEnumerator(ct);
        await pages.MoveNextAsync();
    }
}

public sealed class EnvironmentSourceUnavailableException(string message, Exception inner) : Exception(message, inner);

using Azure.ResourceManager;
using Azure.ResourceManager.Authorization;
using Azure.ResourceManager.Monitor;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// ARM SDK wrapper for Azure resource queries with per-subscription+type caching
/// (5-minute TTL), pre-warming, and safety limits.
/// </summary>
public class AzureResourceService : IAzureResourceService
{
    private readonly ArmClient _armClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AzureResourceService> _logger;
    private readonly CanonicalEnvironmentCollectionGuard? _environmentGuard;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private const int MaxResourcesPerQuery = 5000;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureResourceService"/> class.
    /// </summary>
    /// <param name="armClient">ARM client for Azure resource management.</param>
    /// <param name="cache">Memory cache for resource query results.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="environmentGuard">Current environment authority checked before cache or Azure access.</param>
    public AzureResourceService(
        ArmClient armClient,
        IMemoryCache cache,
        ILogger<AzureResourceService> logger,
        CanonicalEnvironmentCollectionGuard? environmentGuard = null)
    {
        _armClient = armClient;
        _cache = cache;
        _logger = logger;
        _environmentGuard = environmentGuard;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GenericResource>> GetResourcesAsync(
        string subscriptionId,
        string? resourceGroup = null,
        string? resourceType = null,
        CancellationToken cancellationToken = default)
    {
        if (_environmentGuard is not null)
            await _environmentGuard.EnsureSubscriptionAsync(subscriptionId, EnvironmentScopePurpose.Assessment, cancellationToken);
        var cacheKey = $"resources:{subscriptionId}:{resourceGroup ?? "all"}:{resourceType ?? "all"}";

        if (_cache.TryGetValue<IReadOnlyList<GenericResource>>(cacheKey, out var cached) && cached is not null)
        {
            _logger.LogDebug("Cache hit for {CacheKey} ({Count} resources)", cacheKey, cached.Count);
            return cached;
        }

        _logger.LogInformation("Querying resources: Sub={Sub}, RG={RG}, Type={Type}",
            subscriptionId, resourceGroup ?? "all", resourceType ?? "all");

        try
        {
            var subscription = _armClient.GetSubscriptionResource(
                SubscriptionResource.CreateResourceIdentifier(subscriptionId));

            var resources = new List<GenericResource>();

            if (!string.IsNullOrEmpty(resourceGroup))
            {
                var rg = (await subscription.GetResourceGroupAsync(resourceGroup, cancellationToken)).Value;
                await foreach (var resource in rg.GetGenericResourcesAsync(cancellationToken: cancellationToken))
                {
                    if (resourceType is not null &&
                        !string.Equals(resource.Data.ResourceType.ToString(), resourceType, StringComparison.OrdinalIgnoreCase))
                        continue;

                    resources.Add(resource);
                    if (resources.Count >= MaxResourcesPerQuery) break;
                }
            }
            else
            {
                await foreach (var resource in subscription.GetGenericResourcesAsync(cancellationToken: cancellationToken))
                {
                    if (resourceType is not null &&
                        !string.Equals(resource.Data.ResourceType.ToString(), resourceType, StringComparison.OrdinalIgnoreCase))
                        continue;

                    resources.Add(resource);
                    if (resources.Count >= MaxResourcesPerQuery) break;
                }
            }

            IReadOnlyList<GenericResource> result = resources.AsReadOnly();
            _cache.Set(cacheKey, result, CacheTtl);

            _logger.LogInformation("Cached {Count} resources for {CacheKey}", result.Count, cacheKey);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query resources for Sub={Sub}", subscriptionId);
            return Array.Empty<GenericResource>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleAssignmentResource>> GetRoleAssignmentsAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (_environmentGuard is not null)
            await _environmentGuard.EnsureSubscriptionAsync(subscriptionId, EnvironmentScopePurpose.Assessment, cancellationToken);
        var cacheKey = $"roleassignments:{subscriptionId}";

        if (_cache.TryGetValue<IReadOnlyList<RoleAssignmentResource>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var subscription = _armClient.GetSubscriptionResource(
                SubscriptionResource.CreateResourceIdentifier(subscriptionId));

            var assignments = new List<RoleAssignmentResource>();
            await foreach (var assignment in subscription.GetRoleAssignments().GetAllAsync(cancellationToken: cancellationToken))
            {
                assignments.Add(assignment);
                if (assignments.Count >= MaxResourcesPerQuery) break;
            }

            IReadOnlyList<RoleAssignmentResource> result = assignments.AsReadOnly();
            _cache.Set(cacheKey, result, CacheTtl);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query role assignments for Sub={Sub}", subscriptionId);
            return Array.Empty<RoleAssignmentResource>();
        }
    }

    /// <inheritdoc />
    public async Task PreWarmCacheAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (_environmentGuard is not null)
            await _environmentGuard.EnsureSubscriptionAsync(subscriptionId, EnvironmentScopePurpose.Assessment, cancellationToken);
        _logger.LogInformation("Pre-warming resource cache for Sub={Sub}", subscriptionId);

        // Pre-warm common resource types in parallel
        var tasks = new Task[]
        {
            GetResourcesAsync(subscriptionId, cancellationToken: cancellationToken),
            GetRoleAssignmentsAsync(subscriptionId, cancellationToken)
        };

        try
        {
            await Task.WhenAll(tasks);
            _logger.LogInformation("Cache pre-warm complete for Sub={Sub}", subscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Partial cache pre-warm failure for Sub={Sub}", subscriptionId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiagnosticSettingResource>> GetDiagnosticSettingsAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        if (_environmentGuard is not null)
            await _environmentGuard.EnsureResourceAsync(resourceId, cancellationToken);
        var cacheKey = $"diagnostics:{resourceId}";

        if (_cache.TryGetValue<IReadOnlyList<DiagnosticSettingResource>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var resource = _armClient.GetDiagnosticSettings(new Azure.Core.ResourceIdentifier(resourceId));
            var settings = new List<DiagnosticSettingResource>();

            await foreach (var setting in resource.GetAllAsync(cancellationToken))
            {
                settings.Add(setting);
            }

            IReadOnlyList<DiagnosticSettingResource> result = settings.AsReadOnly();
            _cache.Set(cacheKey, result, CacheTtl);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get diagnostic settings for {ResourceId}", resourceId);
            return Array.Empty<DiagnosticSettingResource>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagementLockResource>> GetResourceLocksAsync(
        string subscriptionId,
        string? resourceGroup = null,
        CancellationToken cancellationToken = default)
    {
        if (_environmentGuard is not null)
            await _environmentGuard.EnsureSubscriptionAsync(subscriptionId, EnvironmentScopePurpose.Assessment, cancellationToken);
        var cacheKey = $"locks:{subscriptionId}:{resourceGroup ?? "all"}";

        if (_cache.TryGetValue<IReadOnlyList<ManagementLockResource>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var subscription = _armClient.GetSubscriptionResource(
                SubscriptionResource.CreateResourceIdentifier(subscriptionId));

            var locks = new List<ManagementLockResource>();

            if (!string.IsNullOrEmpty(resourceGroup))
            {
                var rg = (await subscription.GetResourceGroupAsync(resourceGroup, cancellationToken)).Value;
                await foreach (var lockResource in rg.GetManagementLocks().GetAllAsync(cancellationToken: cancellationToken))
                {
                    locks.Add(lockResource);
                }
            }
            else
            {
                await foreach (var lockResource in subscription.GetManagementLocks().GetAllAsync(cancellationToken: cancellationToken))
                {
                    locks.Add(lockResource);
                }
            }

            IReadOnlyList<ManagementLockResource> result = locks.AsReadOnly();
            _cache.Set(cacheKey, result, CacheTtl);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get resource locks for Sub={Sub}", subscriptionId);
            return Array.Empty<ManagementLockResource>();
        }
    }
}

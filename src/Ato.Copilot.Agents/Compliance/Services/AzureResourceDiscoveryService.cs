using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Discovers Azure resources via Resource Graph queries and groups them
/// by resource group as suggested authorization boundaries.
/// </summary>
public class AzureResourceDiscoveryService
{
    private readonly ArmClient _armClient;
    private readonly ArmClientFactory? _armClientFactory;
    private readonly ILogger<AzureResourceDiscoveryService> _logger;
    private readonly IDbContextFactory<AtoCopilotContext>? _dbFactory;

    /// <summary>Safety cap: maximum pages to fetch (10 × 1000 = 10,000 resources max).</summary>
    internal const int MaxPages = 10;

    public AzureResourceDiscoveryService(ArmClient armClient, ILogger<AzureResourceDiscoveryService> logger)
    {
        _armClient = armClient;
        _logger = logger;
    }

    public AzureResourceDiscoveryService(
        ArmClient armClient,
        ILogger<AzureResourceDiscoveryService> logger,
        IDbContextFactory<AtoCopilotContext> dbFactory) : this(armClient, logger)
    {
        _dbFactory = dbFactory;
    }

    public AzureResourceDiscoveryService(
        ArmClient armClient,
        ArmClientFactory armClientFactory,
        ILogger<AzureResourceDiscoveryService> logger,
        IDbContextFactory<AtoCopilotContext> dbFactory) : this(armClient, logger, dbFactory)
    {
        _armClientFactory = armClientFactory;
    }

    /// <summary>
    /// Discovers Azure resources for a subscription, grouped by resource group.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="existingResourceIds">Resource IDs already in the boundary (for dedup badges).</param>
    /// <param name="existingBoundaryNames">Existing boundary definition names (for alreadyExists flags).</param>
    /// <param name="resourceGroupFilter">Optional filter to a specific resource group.</param>
    /// <param name="resourceTypeFilter">Optional filter to a specific resource type.</param>
    /// <param name="searchFilter">Optional text search on resource name.</param>
    /// <param name="cursor">SkipToken for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Discovery result with suggested boundaries.</returns>
    public async Task<AzureDiscoveryResult> DiscoverResourcesAsync(
        string subscriptionId,
        HashSet<string> existingResourceIds,
        HashSet<string> existingBoundaryNames,
        string? resourceGroupFilter = null,
        string? resourceTypeFilter = null,
        string? searchFilter = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var resources = new List<AzureDiscoveredResource>();
        string? nextCursor = null;

        // Build the KQL query
        var query = BuildQuery(subscriptionId, resourceGroupFilter, resourceTypeFilter, searchFilter);

        _logger.LogInformation("Executing Resource Graph query for subscription {SubscriptionId}", subscriptionId);

        var client = await ResolveArmClientAsync(cancellationToken);
        var tenant = client.GetTenants().First();
        var pageCount = 0;
        var currentCursor = cursor;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = new ResourceQueryContent(query)
            {
                Options = new ResourceQueryRequestOptions
                {
                    ResultFormat = ResultFormat.ObjectArray
                }
            };

            if (!string.IsNullOrEmpty(currentCursor))
                content.Options.SkipToken = currentCursor;

            var response = await tenant.GetResourcesAsync(content, cancellationToken);
            var result = response.Value;

            if (result.Data != null)
            {
                var jsonData = result.Data.ToObjectFromJson<JsonElement>();
                if (jsonData.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in jsonData.EnumerateArray())
                    {
                        var resource = ParseResource(element);
                        if (resource != null)
                            resources.Add(resource);
                    }
                }
            }

            nextCursor = result.SkipToken;
            currentCursor = nextCursor;
            pageCount++;
        } while (!string.IsNullOrEmpty(currentCursor) && pageCount < MaxPages);

        _logger.LogInformation("Discovered {ResourceCount} resources across {Pages} page(s)", resources.Count, pageCount);

        // Deduplicate by resource ID
        resources = resources
            .GroupBy(r => r.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Mark resources already in boundary
        foreach (var resource in resources)
        {
            resource.AlreadyInBoundary = existingResourceIds.Contains(resource.ResourceId);
        }

        // Group by resource group as suggested boundaries
        var suggestedBoundaries = resources
            .GroupBy(r => r.ResourceGroup, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .Select(g => new AzureSuggestedBoundary
            {
                ResourceGroupName = g.Key,
                BoundaryType = "Logical",
                ResourceCount = g.Count(),
                AlreadyExists = existingBoundaryNames.Contains(g.Key),
                Resources = g.OrderBy(r => r.Type).ThenBy(r => r.Name).ToList()
            })
            .ToList();

        return new AzureDiscoveryResult
        {
            SuggestedBoundaries = suggestedBoundaries,
            NextCursor = nextCursor,
            TotalResourceCount = resources.Count
        };
    }

    /// <summary>
    /// Discovers Azure resources for component import (flat list, no boundary grouping).
    /// Marks resources that are already imported as SystemComponents.
    /// Tracks per-resource-group partial failures.
    /// </summary>
    public async Task<ComponentDiscoveryResult> DiscoverForComponentsAsync(
        string subscriptionId,
        string? systemId = null,
        string? resourceGroupFilter = null,
        string? resourceTypeFilter = null,
        string? searchFilter = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var resources = new List<ComponentDiscoveredResource>();
        string? nextCursor = null;
        var failedResourceGroups = new List<string>();

        var query = BuildQuery(subscriptionId, resourceGroupFilter, resourceTypeFilter, searchFilter);

        _logger.LogInformation("Discovering Azure resources for component import in subscription {SubscriptionId}", subscriptionId);

        try
        {
            var client = await ResolveArmClientAsync(cancellationToken);
            var tenant = client.GetTenants().First();
            var pageCount = 0;
            var currentCursor = cursor;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var content = new ResourceQueryContent(query)
                {
                    Options = new ResourceQueryRequestOptions { ResultFormat = ResultFormat.ObjectArray }
                };
                if (!string.IsNullOrEmpty(currentCursor))
                    content.Options.SkipToken = currentCursor;

                var response = await tenant.GetResourcesAsync(content, cancellationToken);
                var result = response.Value;

                if (result.Data != null)
                {
                    var jsonData = result.Data.ToObjectFromJson<JsonElement>();
                    if (jsonData.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in jsonData.EnumerateArray())
                        {
                            var parsed = ParseResource(element);
                            if (parsed != null)
                            {
                                resources.Add(new ComponentDiscoveredResource
                                {
                                    ResourceId = parsed.ResourceId,
                                    Name = parsed.Name,
                                    Type = parsed.Type,
                                    ResourceGroup = parsed.ResourceGroup,
                                    Location = parsed.Location,
                                });
                            }
                        }
                    }
                }

                nextCursor = result.SkipToken;
                currentCursor = nextCursor;
                pageCount++;
            } while (!string.IsNullOrEmpty(currentCursor) && pageCount < MaxPages);

            _logger.LogInformation("Discovered {ResourceCount} resources for component import", resources.Count);
        }
        catch (RequestFailedException ex) when (ex.Status == 429 || ex.Status >= 500)
        {
            _logger.LogWarning(ex, "Partial failure during resource discovery for subscription {SubscriptionId}", subscriptionId);
            if (!string.IsNullOrEmpty(resourceGroupFilter))
                failedResourceGroups.Add(resourceGroupFilter);
        }

        // Dedup by resource ID
        resources = resources
            .GroupBy(r => r.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Mark already-imported resources
        if (_dbFactory != null)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var resourceIds = resources.Select(r => r.ResourceId).ToList();
            var importedIds = await db.SystemComponents
                .Where(c => c.AzureResourceId != null && resourceIds.Contains(c.AzureResourceId))
                .Select(c => new { c.AzureResourceId, c.RegisteredSystemId, c.Id })
                .ToListAsync(cancellationToken);

            var orgImported = importedIds
                .Where(c => c.RegisteredSystemId == null)
                .ToDictionary(c => c.AzureResourceId!, c => c.Id, StringComparer.OrdinalIgnoreCase);

            var systemImported = systemId != null
                ? importedIds
                    .Where(c => c.RegisteredSystemId == systemId)
                    .Select(c => c.AzureResourceId!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in resources)
            {
                r.AlreadyImported = systemId != null
                    ? systemImported.Contains(r.ResourceId)
                    : orgImported.ContainsKey(r.ResourceId);

                if (systemId != null && orgImported.TryGetValue(r.ResourceId, out var orgCompId))
                {
                    r.ExistsInOrgLibrary = true;
                    r.OrgLibraryComponentId = orgCompId;
                }
            }

            // ─── FR-026: Stale resource detection ──────────────────────────
            // Find previously imported components whose Azure resource was NOT returned by discovery.
            var discoveredIds = resources.Select(r => r.ResourceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var importedComponents = systemId != null
                ? await db.SystemComponents
                    .Where(c => c.RegisteredSystemId == systemId && c.AzureResourceId != null && c.ComponentType == ComponentType.Thing)
                    .Select(c => new { c.AzureResourceId, c.Name, c.AzureResourceType, c.AzureResourceGroup, c.AzureLocation })
                    .ToListAsync(cancellationToken)
                : await db.SystemComponents
                    .Where(c => c.RegisteredSystemId == null && c.AzureResourceId != null && c.ComponentType == ComponentType.Thing)
                    .Select(c => new { c.AzureResourceId, c.Name, c.AzureResourceType, c.AzureResourceGroup, c.AzureLocation })
                    .ToListAsync(cancellationToken);

            foreach (var comp in importedComponents)
            {
                if (comp.AzureResourceId != null && !discoveredIds.Contains(comp.AzureResourceId))
                {
                    resources.Add(new ComponentDiscoveredResource
                    {
                        ResourceId = comp.AzureResourceId,
                        Name = comp.Name,
                        Type = comp.AzureResourceType ?? "",
                        ResourceGroup = comp.AzureResourceGroup ?? "",
                        Location = comp.AzureLocation ?? "",
                        AlreadyImported = true,
                        NotFoundInAzure = true,
                    });
                }
            }
        }

        return new ComponentDiscoveryResult
        {
            Resources = resources,
            NextCursor = nextCursor,
            TotalCount = resources.Count,
            FailedResourceGroups = failedResourceGroups,
        };
    }

    /// <summary>
    /// Resolves the best ArmClient by trying the factory fallback (Gov → Commercial or vice-versa).
    /// Falls back to the injected singleton if no factory is available.
    /// </summary>
    private async Task<ArmClient> ResolveArmClientAsync(CancellationToken ct)
    {
        if (_armClientFactory == null)
            return _armClient;

        var (client, cloud) = await _armClientFactory.GetClientWithFallbackAsync(ct);
        _logger.LogInformation("Resolved ARM client for {Cloud}", cloud);
        return client;
    }

    /// <summary>Builds a Resource Graph KQL query with optional filters.</summary>
    internal static string BuildQuery(string subscriptionId, string? resourceGroup, string? resourceType, string? search)
    {
        var parts = new List<string>
        {
            "Resources",
            $"| where subscriptionId == '{EscapeKql(subscriptionId)}'"
        };

        if (!string.IsNullOrWhiteSpace(resourceGroup))
            parts.Add($"| where resourceGroup =~ '{EscapeKql(resourceGroup)}'");

        if (!string.IsNullOrWhiteSpace(resourceType))
            parts.Add($"| where type =~ '{EscapeKql(resourceType)}'");

        if (!string.IsNullOrWhiteSpace(search))
            parts.Add($"| where name contains '{EscapeKql(search)}'");

        parts.Add("| project id, name, type, resourceGroup, location");

        return string.Join(" ", parts);
    }

    /// <summary>Extracts resource group name from an ARM resource ID.</summary>
    public static string ExtractResourceGroup(string resourceId)
    {
        const string marker = "/resourceGroups/";
        var idx = resourceId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;

        var start = idx + marker.Length;
        var end = resourceId.IndexOf('/', start);
        return end < 0 ? resourceId[start..] : resourceId[start..end];
    }

    /// <summary>Escapes single quotes in KQL string literals to prevent injection.</summary>
    internal static string EscapeKql(string value)
    {
        return value.Replace("'", "\\'");
    }

    private static AzureDiscoveredResource? ParseResource(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idProp)) return null;
        var resourceId = idProp.GetString();
        if (string.IsNullOrEmpty(resourceId)) return null;

        return new AzureDiscoveredResource
        {
            ResourceId = resourceId,
            Name = element.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            Type = element.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
            ResourceGroup = element.TryGetProperty("resourceGroup", out var rg) ? rg.GetString() ?? "" : ExtractResourceGroup(resourceId),
            Location = element.TryGetProperty("location", out var l) ? l.GetString() ?? "" : ""
        };
    }
}

// ─── DTOs ────────────────────────────────────────────────────────────────────

public class AzureDiscoveryResult
{
    public List<AzureSuggestedBoundary> SuggestedBoundaries { get; set; } = [];
    public string? NextCursor { get; set; }
    public int TotalResourceCount { get; set; }
}

public class AzureSuggestedBoundary
{
    public string ResourceGroupName { get; set; } = string.Empty;
    public string BoundaryType { get; set; } = "Logical";
    public int ResourceCount { get; set; }
    public bool AlreadyExists { get; set; }
    public List<AzureDiscoveredResource> Resources { get; set; } = [];
}

public class AzureDiscoveredResource
{
    public string ResourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool AlreadyInBoundary { get; set; }
}

public class ApplyDiscoveryRequest
{
    public List<ApplyBoundaryItem> Boundaries { get; set; } = [];
    public List<ApplyComponentItem> Components { get; set; } = [];
}

public class ApplyBoundaryItem
{
    public string ResourceGroupName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BoundaryType { get; set; } = "Logical";
    public string? Description { get; set; }
}

public class ApplyComponentItem
{
    public string? BoundaryDefinitionId { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? SubType { get; set; }
}

public class ApplyDiscoveryResponse
{
    public int BoundariesCreated { get; set; }
    public int ComponentsCreated { get; set; }
    public int Skipped { get; set; }
}

// ─── Component Discovery DTOs (Feature 040) ─────────────────────────────────

/// <summary>Result of component-oriented Azure resource discovery.</summary>
public class ComponentDiscoveryResult
{
    public List<ComponentDiscoveredResource> Resources { get; set; } = [];
    public string? NextCursor { get; set; }
    public int TotalCount { get; set; }
    public List<string> FailedResourceGroups { get; set; } = [];
}

/// <summary>An Azure resource discovered for potential component import.</summary>
public class ComponentDiscoveredResource
{
    public string ResourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool AlreadyImported { get; set; }
    public bool ExistsInOrgLibrary { get; set; }
    public string? OrgLibraryComponentId { get; set; }
    /// <summary>True when a previously imported component's resource is no longer found in the subscription (FR-026).</summary>
    public bool NotFoundInAzure { get; set; }
}

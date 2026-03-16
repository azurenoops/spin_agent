using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Discovers Azure resources via Resource Graph queries and groups them
/// by resource group as suggested authorization boundaries.
/// </summary>
public class AzureResourceDiscoveryService
{
    private readonly ArmClient _armClient;
    private readonly ILogger<AzureResourceDiscoveryService> _logger;

    /// <summary>Safety cap: maximum pages to fetch (10 × 1000 = 10,000 resources max).</summary>
    internal const int MaxPages = 10;

    public AzureResourceDiscoveryService(ArmClient armClient, ILogger<AzureResourceDiscoveryService> logger)
    {
        _armClient = armClient;
        _logger = logger;
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

        var tenant = _armClient.GetTenants().First();
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

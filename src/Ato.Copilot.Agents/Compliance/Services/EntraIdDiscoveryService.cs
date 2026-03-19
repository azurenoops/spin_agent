using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Discovers Entra ID users and groups via Microsoft Graph API
/// for import as org-wide "Person" components.
/// Gated behind organization-level setting (disabled by default).
/// </summary>
public class EntraIdDiscoveryService
{
    private readonly GraphServiceClient? _graphClient;
    private readonly ILogger<EntraIdDiscoveryService> _logger;

    public EntraIdDiscoveryService(
        ILogger<EntraIdDiscoveryService> logger,
        GraphServiceClient? graphClient = null)
    {
        _logger = logger;
        _graphClient = graphClient;
    }

    /// <summary>
    /// Discover users and security groups from Entra ID.
    /// Returns entries with dedup flags for already-imported components.
    /// </summary>
    public async Task<EntraDiscoveryResult> DiscoverUsersAndGroupsAsync(
        AtoCopilotContext db,
        string? searchFilter = null,
        CancellationToken cancellationToken = default)
    {
        if (_graphClient == null)
        {
            _logger.LogWarning("GraphServiceClient not configured — returning empty Entra ID discovery");
            return new EntraDiscoveryResult { Items = [], PartialFailure = true, FailureMessage = "Microsoft Graph not configured" };
        }

        var items = new List<EntraDiscoveryItem>();

        try
        {
            // Discover users
            var usersResponse = await _graphClient.Users.GetAsync(config =>
            {
                config.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName", "jobTitle", "department" };
                config.QueryParameters.Top = 100;
                if (!string.IsNullOrWhiteSpace(searchFilter))
                    config.QueryParameters.Filter = $"startsWith(displayName,'{searchFilter}') or startsWith(mail,'{searchFilter}')";
            }, cancellationToken);

            if (usersResponse?.Value != null)
            {
                foreach (var user in usersResponse.Value)
                {
                    items.Add(new EntraDiscoveryItem
                    {
                        EntraObjectId = user.Id ?? "",
                        DisplayName = user.DisplayName ?? user.UserPrincipalName ?? "Unknown",
                        Email = user.Mail ?? user.UserPrincipalName,
                        Kind = "User",
                        Department = user.Department,
                        JobTitle = user.JobTitle,
                    });
                }
            }

            // Discover security groups
            var groupsResponse = await _graphClient.Groups.GetAsync(config =>
            {
                config.QueryParameters.Select = new[] { "id", "displayName", "description", "mail", "securityEnabled" };
                config.QueryParameters.Top = 100;
                config.QueryParameters.Filter = "securityEnabled eq true";
            }, cancellationToken);

            if (groupsResponse?.Value != null)
            {
                foreach (var group in groupsResponse.Value)
                {
                    items.Add(new EntraDiscoveryItem
                    {
                        EntraObjectId = group.Id ?? "",
                        DisplayName = group.DisplayName ?? "Unknown Group",
                        Email = group.Mail,
                        Kind = "Group",
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Entra ID discovery failed");
            return new EntraDiscoveryResult { Items = items, PartialFailure = true, FailureMessage = ex.Message };
        }

        // Mark already-imported items
        var existingObjectIds = await db.SystemComponents
            .Where(c => c.ComponentType == ComponentType.Person && c.RegisteredSystemId == null)
            .Where(c => c.AzureResourceId != null)
            .Select(c => c.AzureResourceId!)
            .ToListAsync(cancellationToken);
        var existingSet = new HashSet<string>(existingObjectIds, StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            item.AlreadyImported = existingSet.Contains(item.EntraObjectId);
        }

        _logger.LogInformation("Entra ID discovery found {UserCount} users + {GroupCount} groups",
            items.Count(i => i.Kind == "User"), items.Count(i => i.Kind == "Group"));

        return new EntraDiscoveryResult { Items = items };
    }
}

/// <summary>Single discovered Entra ID entry.</summary>
public class EntraDiscoveryItem
{
    public string EntraObjectId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public string Kind { get; set; } = "User"; // "User" or "Group"
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public bool AlreadyImported { get; set; }
}

/// <summary>Result of Entra ID discovery.</summary>
public class EntraDiscoveryResult
{
    public List<EntraDiscoveryItem> Items { get; set; } = [];
    public bool PartialFailure { get; set; }
    public string? FailureMessage { get; set; }
}

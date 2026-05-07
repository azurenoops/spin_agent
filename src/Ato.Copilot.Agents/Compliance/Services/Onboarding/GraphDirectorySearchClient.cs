using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Ato.Copilot.Core.Interfaces.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding;

/// <summary>
/// Microsoft-Graph-backed implementation of <see cref="IDirectorySearchClient"/>.
/// Returns an empty list when no <see cref="GraphServiceClient"/> is registered (dev/test).
/// </summary>
public class GraphDirectorySearchClient : IDirectorySearchClient
{
    private readonly GraphServiceClient? _graphClient;
    private readonly ILogger<GraphDirectorySearchClient> _logger;

    public GraphDirectorySearchClient(
        ILogger<GraphDirectorySearchClient> logger,
        GraphServiceClient? graphClient = null)
    {
        _logger = logger;
        _graphClient = graphClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DirectoryPersonDto>> SearchAsync(
        string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<DirectoryPersonDto>();
        }
        if (_graphClient is null)
        {
            _logger.LogDebug(
                "GraphServiceClient not registered — directory search returns empty list.");
            return Array.Empty<DirectoryPersonDto>();
        }

        try
        {
            var trimmed = query.Trim();
            var filter = $"startswith(displayName,'{trimmed}') or startswith(mail,'{trimmed}') or startswith(userPrincipalName,'{trimmed}')";
            var users = await _graphClient.Users.GetAsync(req =>
            {
                req.QueryParameters.Filter = filter;
                req.QueryParameters.Top = 25;
                req.QueryParameters.Select = new[] { "id", "displayName", "mail", "userPrincipalName", "department" };
            }, ct);

            var list = new List<DirectoryPersonDto>();
            if (users?.Value is null) return list;
            foreach (var u in users.Value)
            {
                if (!Guid.TryParse(u.Id, out var oid)) continue;
                list.Add(new DirectoryPersonDto(
                    EntraObjectId: oid,
                    DisplayName: u.DisplayName ?? string.Empty,
                    Email: u.Mail ?? u.UserPrincipalName ?? string.Empty,
                    Department: u.Department));
            }
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Directory search failed for query '{Query}' — returning empty list.", query);
            return Array.Empty<DirectoryPersonDto>();
        }
    }
}

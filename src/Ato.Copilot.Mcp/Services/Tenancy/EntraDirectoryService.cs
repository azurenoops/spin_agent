using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Services.Tenancy;

public sealed class EntraDirectoryOptions
{
    public List<EntraDirectoryConnection> Connections { get; set; } = [];
}

public sealed class EntraDirectoryConnection
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string AdminDirectoryTenantId { get; set; } = "";
    public string DirectoryTenantId { get; set; } = "";
    public string Cloud { get; set; } = "Government";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public bool Configured => Guid.TryParse(DirectoryTenantId, out var tenant) && tenant != Guid.Empty
        && Guid.TryParse(ClientId, out var client) && client != Guid.Empty && !string.IsNullOrWhiteSpace(ClientSecret)
        && Cloud is "Public" or "Government" or "DoD";
}

public sealed record DirectoryConnectionResponse(string Id, string Name, string DirectoryTenantId, string Cloud, bool Configured);
public sealed record DirectoryUserResponse(string DirectoryTenantId, string ObjectId, string DisplayName, string Email, string UserPrincipalName);
public sealed record DirectorySearchResponse(IReadOnlyList<DirectoryUserResponse> Users, bool HasMore);

/// <summary>Read-only directory lookup with provider authority and server-owned cloud/tenant destinations.</summary>
public sealed class EntraDirectoryService(HttpClient http, IOptions<EntraDirectoryOptions> options,
    IWorkspaceService workspace, Func<EntraDirectoryConnection, TokenCredential> credentials)
{
    private IEnumerable<EntraDirectoryConnection> AuthorizedConnections(ClaimsPrincipal actor)
    {
        var identity = WorkspaceService.Identity(actor);
        if (!workspace.IsCspAdministrator(actor) || workspace.Current?.Kind != "csp" || workspace.SupportSession is not null)
            throw new WorkspaceException(403, "DIRECTORY_LOOKUP_FORBIDDEN", "Directory lookup requires the provider administrator workspace.");
        return options.Value.Connections.Where(c => Guid.TryParse(c.AdminDirectoryTenantId, out var tenant) && tenant == identity.DirectoryId);
    }

    public IReadOnlyList<DirectoryConnectionResponse> Connections(ClaimsPrincipal actor) => AuthorizedConnections(actor)
        .Select(c => new DirectoryConnectionResponse(c.Id, c.Name, c.DirectoryTenantId, c.Cloud, c.Configured)).ToArray();

    public async Task<DirectorySearchResponse> SearchAsync(ClaimsPrincipal actor, string connectionId, string query, CancellationToken ct)
    {
        var connection = AuthorizedConnections(actor).SingleOrDefault(c => c.Id == connectionId)
            ?? throw new WorkspaceException(404, "DIRECTORY_NOT_FOUND", "This directory connection is not available to your workspace.");
        if (!connection.Configured) throw new WorkspaceException(503, "DIRECTORY_NOT_CONFIGURED", "An administrator must configure this Entra connection and grant directory-read consent.");
        query = query.Trim();
        if (query.Length is < 2 or > 100 || query.Any(char.IsControl))
            throw new WorkspaceException(400, "INVALID_DIRECTORY_QUERY", "Enter between 2 and 100 characters of a name or email.");
        var host = connection.Cloud switch { "Public" => "graph.microsoft.com", "DoD" => "dod-graph.microsoft.us", _ => "graph.microsoft.us" };
        var literal = query.Replace("'", "''", StringComparison.Ordinal);
        var filter = $"startswith(displayName,'{literal}') or startswith(mail,'{literal}') or startswith(userPrincipalName,'{literal}')";
        try
        {
            var token = await credentials(connection).GetTokenAsync(new TokenRequestContext([$"https://{host}/.default"]), ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}/v1.0/users?$select=id,displayName,mail,userPrincipalName&$top=20&$filter={Uri.EscapeDataString(filter)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new WorkspaceException(429, "DIRECTORY_THROTTLED", "Entra is limiting requests. Wait briefly and search again.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                throw new WorkspaceException(503, "DIRECTORY_CONSENT_REQUIRED", "The directory connection needs valid credentials and Microsoft Graph User.Read.All administrator consent.");
            if (!response.IsSuccessStatusCode)
                throw new WorkspaceException(502, "DIRECTORY_UNAVAILABLE", "Entra could not complete the search. Try again or contact your connection administrator.");
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!payload.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
                throw new JsonException("Directory result collection missing.");
            static string Read(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
            var users = values.EnumerateArray().Take(20).Select(row => {
                var id = Read(row, "id");
                if (!Guid.TryParse(id, out var objectId) || objectId == Guid.Empty) throw new JsonException("Invalid directory identity.");
                var mail = Read(row, "mail"); var upn = Read(row, "userPrincipalName");
                return new DirectoryUserResponse(connection.DirectoryTenantId, id, Read(row, "displayName"), string.IsNullOrWhiteSpace(mail) ? upn : mail, upn);
            }).ToArray();
            return new(users, payload.RootElement.TryGetProperty("@odata.nextLink", out _));
        }
        catch (AuthenticationFailedException) { throw new WorkspaceException(503, "DIRECTORY_AUTH_FAILED", "The Entra connection could not authenticate. Ask your connection administrator to check its credentials."); }
        catch (HttpRequestException) { throw new WorkspaceException(502, "DIRECTORY_UNAVAILABLE", "Entra is unreachable. Try the search again."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new WorkspaceException(504, "DIRECTORY_TIMEOUT", "Entra took too long to respond. Try the search again."); }
        catch (JsonException) { throw new WorkspaceException(502, "DIRECTORY_INVALID_RESPONSE", "Entra returned an unexpected response. No user was selected."); }
    }
}

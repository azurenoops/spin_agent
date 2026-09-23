using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Ato.Copilot.Chat.Services.Auth;

/// <summary>Validated per-operation identity. Never constructed from body ownership fields.</summary>
public sealed record ChatWorkspace(
    Guid DirectoryId, Guid ObjectId, string Kind, Guid? TenantId, string Mode,
    Guid? PersonId, string Token, string? SupportCookie)
{
    public string ActorId => $"{DirectoryId:D}/{ObjectId:D}";
    public string OwnerKey => "v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new[] { DirectoryId.ToString("D"), ObjectId.ToString("D"),
            Kind, TenantId?.ToString("D"), Mode, PersonId?.ToString("D") }))));

    public HttpRequestMessage Request(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        request.Headers.Add("X-Workspace-Kind", Kind);
        request.Headers.Add("X-Workspace-Mode", Mode);
        if (TenantId.HasValue)
            request.Headers.Add("X-Workspace-Tenant-Id", TenantId.Value.ToString("D"));
        if (Mode == "support" && SupportCookie is not null)
            request.Headers.Add("Cookie", $"ato-impersonate={Uri.EscapeDataString(SupportCookie)}");
        return request;
    }
}

/// <summary>Explicit fail-closed authorization errors shared by MVC and service consumers.</summary>
public sealed class ChatWorkspaceException(int statusCode, string code, string message)
    : UnauthorizedAccessException(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

/// <summary>
/// Revalidates the existing local bearer ticket and authoritative MCP membership before DB access.
/// No cached membership, default actor, service credentials, or ownership adoption is supported.
/// </summary>
public sealed class ChatWorkspaceResolver(IHttpContextAccessor httpContextAccessor, IHttpClientFactory clients)
{
    internal static readonly AsyncLocal<HttpContext?> HubRequest = new();
    private HttpContext? CurrentRequest => HubRequest.Value ?? httpContextAccessor.HttpContext;
    public CancellationToken RequestAborted => CurrentRequest?.RequestAborted ?? default;

    public async Task<ChatWorkspace> ResolveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var http = CurrentRequest;
        if (http?.User.Identity?.IsAuthenticated != true)
            throw Denied(401, "CHAT_AUTH_REQUIRED", "An authenticated user bearer token is required.");

        var localActor = Actor(http.User);
        var token = Bearer(http);
        var authentication = await http.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!authentication.Succeeded || authentication.Principal is null
            || Actor(authentication.Principal) != localActor
            || authentication.Properties?.GetTokenValue("access_token") != token)
            throw Denied(401, "CHAT_AUTH_REQUIRED", "The incoming bearer must match the validated local identity.");

        var kind = Header(http, "X-Workspace-Kind");
        var mode = Header(http, "X-Workspace-Mode");
        var tenant = Header(http, "X-Workspace-Tenant-Id");
        if (kind is not ("csp" or "organization") || mode is not ("ordinary" or "support"))
            throw Denied(400, "WORKSPACE_REQUIRED", "Select an explicit workspace kind and mode.");
        Guid? tenantId = null;
        if (kind == "organization")
        {
            if (!Guid.TryParse(tenant, out var id) || id == Guid.Empty)
                throw Denied(400, "INVALID_WORKSPACE_CONTEXT", "An internal organization tenant ID is required.");
            tenantId = id;
        }
        else if (tenant is not null || mode != "ordinary")
            throw Denied(400, "INVALID_WORKSPACE_CONTEXT", "CSP cannot specify an organization or support mode.");

        var cookie = mode == "support" ? http.Request.Cookies["ato-impersonate"] : null;
        if (mode == "support" && string.IsNullOrEmpty(cookie))
            throw Denied(403, "SUPPORT_SESSION_INVALID", "A valid support session is required.");
        var selected = new ChatWorkspace(localActor.Directory, localActor.Object, kind, tenantId, mode, null, token, cookie);
        using var request = selected.Request(HttpMethod.Get, "/api/auth/me");
        using var document = await GetValidatedJsonAsync(request, cancellationToken);
        var root = document.RootElement.GetProperty("data");
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("oid", out var oid)
            || oid.ValueKind != JsonValueKind.String || !oid.TryGetGuid(out var upstreamOid)
            || upstreamOid != localActor.Object
            || !root.TryGetProperty("directoryTenantId", out var directory)
            || directory.ValueKind != JsonValueKind.String || !directory.TryGetGuid(out var upstreamDirectory)
            || upstreamDirectory != localActor.Directory
            || !root.TryGetProperty("workspace", out var workspace)
            || workspace.ValueKind != JsonValueKind.Object
            || String(workspace, "kind") != kind || String(workspace, "mode") != mode
            || GuidValue(workspace, "tenantId") != tenantId
            || !workspace.TryGetProperty("permissions", out var permissions) || permissions.ValueKind != JsonValueKind.Object
            || !workspace.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
            throw Denied(403, "WORKSPACE_IDENTITY_MISMATCH", "The upstream identity or workspace does not match this request.");
        var person = GuidValue(workspace, "personId");
        if ((kind == "organization" && mode == "ordinary" && (person is null || person == Guid.Empty))
            || ((kind == "csp" || mode == "support") && person is not null))
            throw Denied(403, "WORKSPACE_IDENTITY_MISMATCH", "The upstream workspace person is invalid.");
        return selected with { PersonId = person };
    }

    public async Task RequireSystemAsync(ChatWorkspace workspace, string? systemId, CancellationToken ct)
    {
        if (systemId is null) return;
        using var request = workspace.Request(HttpMethod.Get,
            $"/api/dashboard/systems/{Uri.EscapeDataString(systemId)}/workspace-access");
        using var document = await GetValidatedJsonAsync(request, ct);
        var data = document.RootElement.GetProperty("data");
        if (!data.TryGetProperty("permissions", out var permissions)
            || permissions.ValueKind != JsonValueKind.Object
            || !permissions.TryGetProperty("canRead", out var read) || read.ValueKind != JsonValueKind.True)
            throw Denied(403, "SYSTEM_ACCESS_DENIED", "The selected system is not accessible in this workspace.");
    }

    private async Task<JsonDocument> GetValidatedJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await clients.CreateClient("McpWorkspace").SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw Denied(response.StatusCode is System.Net.HttpStatusCode.Unauthorized ? 401
                    : response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound ? 403 : 503,
                    "WORKSPACE_VALIDATION_FAILED", "Authoritative workspace validation failed.");
            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var envelope = document.RootElement;
            if (envelope.ValueKind != JsonValueKind.Object || String(envelope, "status") != "success"
                || !envelope.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw Denied(503, "WORKSPACE_VALIDATION_UNAVAILABLE", "Authoritative workspace validation returned an invalid response.");
            }
            return document;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            throw Denied(503, "WORKSPACE_VALIDATION_UNAVAILABLE", "Authoritative workspace validation is unavailable.");
        }
    }

    private static (Guid Directory, Guid Object) Actor(ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(principal.FindFirstValue("tid"), out var directory) || directory == Guid.Empty
            || !Guid.TryParse(principal.FindFirstValue("oid"), out var subject) || subject == Guid.Empty)
            throw Denied(401, "CHAT_IDENTITY_REQUIRED", "A validated directory and object identity are required.");
        return (directory, subject);
    }

    private static string Bearer(HttpContext http)
    {
        var authorization = http.Request.Headers.Authorization;
        var queryToken = http.Request.Path.StartsWithSegments("/hubs/chat")
            ? http.Request.Query["access_token"].ToString() : null;
        if (authorization.Count > 1)
            throw Denied(401, "CHAT_AUTH_REQUIRED", "A single bearer token is required.");
        string? token = null;
        if (authorization.Count == 1 && AuthenticationHeaderValue.TryParse(authorization[0], out var header)
            && header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            token = header.Parameter;
        if (!string.IsNullOrEmpty(queryToken))
        {
            if (token is not null && token != queryToken)
                throw Denied(401, "CHAT_AUTH_REQUIRED", "Conflicting bearer tokens are not allowed.");
            token = queryToken;
        }
        return !string.IsNullOrWhiteSpace(token) ? token
            : throw Denied(401, "CHAT_AUTH_REQUIRED", "The incoming user's bearer token is required.");
    }

    private static string? Header(HttpContext http, string name)
    {
        var values = http.Request.Headers[name];
        if (values.Count > 1 || (values.Count == 1 && string.IsNullOrWhiteSpace(values[0])))
            throw Denied(400, "INVALID_WORKSPACE_CONTEXT", "Workspace selectors must be single nonempty values.");
        return values.Count == 0 ? null : values[0];
    }

    private static string? String(JsonElement value, string name) =>
        value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private static Guid? GuidValue(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var item) || item.ValueKind == JsonValueKind.Null) return null;
        if (item.ValueKind == JsonValueKind.String && item.TryGetGuid(out var result)) return result;
        throw Denied(403, "WORKSPACE_IDENTITY_MISMATCH", "The upstream workspace identifier is invalid.");
    }

    private static ChatWorkspaceException Denied(int status, string code, string message) => new(status, code, message);
}

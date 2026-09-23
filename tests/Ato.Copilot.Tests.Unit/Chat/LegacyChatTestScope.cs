using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Chat.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Ato.Copilot.Tests.Unit.Chat;

internal sealed class LegacyChatTestScope : IDisposable
{
    public static readonly Guid Directory = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Subject = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Organization = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Person = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static string DefaultOwnerKey => new ChatWorkspace(Directory, Subject, "organization",
        Organization, "ordinary", Person, "test-user-token", null).OwnerKey;
    public static string ActorId => $"{Directory:D}/{Subject:D}";
    public Guid DirectoryId { get; set; } = Directory;
    public Guid ObjectId { get; set; } = Subject;
    public Guid? TenantId { get; set; } = Organization;
    public Guid? PersonId { get; set; } = Person;
    public Guid? UpstreamObjectId { get; set; }
    public string Kind { get; set; } = "organization";
    public string Mode { get; set; } = "ordinary";
    public HttpStatusCode UpstreamStatus { get; set; } = HttpStatusCode.OK;
    public bool SystemReadable { get; set; } = true;
    public bool Authenticated { get; set; } = true;
    public bool LocalTokenValid { get; set; } = true;
    public string? ResponseOverride { get; set; }
    public Exception? UpstreamException { get; set; }
    public string Token { get; set; } = "test-user-token";
    public List<(string Path, string? Token, string? Kind, string? Mode, string? Tenant, string? Cookie)> Requests { get; } = new();
    public DefaultHttpContext Http { get; } = new();
    public HttpContextAccessor Accessor { get; }
    public ChatWorkspaceResolver Resolver { get; }
    private readonly ServiceProvider _services;
    private readonly HttpClient _client;

    public LegacyChatTestScope()
    {
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), JwtBearerDefaults.AuthenticationScheme))
            .Returns(() =>
            {
                var properties = new AuthenticationProperties();
                properties.StoreTokens(new[] { new AuthenticationToken { Name = "access_token", Value = Token } });
                return Task.FromResult(LocalTokenValid
                    ? AuthenticateResult.Success(new AuthenticationTicket(Http.User, properties, JwtBearerDefaults.AuthenticationScheme))
                    : AuthenticateResult.Fail("Rejected token"));
            });
        _services = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider();
        Http.RequestServices = _services;
        Accessor = new HttpContextAccessor { HttpContext = Http };
        _client = new HttpClient(new Handler(this)) { BaseAddress = new Uri("https://mcp.test") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("McpWorkspace")).Returns(_client);
        Resolver = new ChatWorkspaceResolver(Accessor, factory.Object);
        Apply();
    }

    public void Apply()
    {
        Http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tid", DirectoryId.ToString()), new Claim("oid", ObjectId.ToString())
        }, Authenticated ? JwtBearerDefaults.AuthenticationScheme : null));
        Http.Request.Headers.Authorization = $"Bearer {Token}";
        Http.Request.Headers["X-Workspace-Kind"] = Kind;
        Http.Request.Headers["X-Workspace-Mode"] = Mode;
        Http.Request.Headers.Remove("X-Workspace-Tenant-Id");
        if (TenantId.HasValue) Http.Request.Headers["X-Workspace-Tenant-Id"] = TenantId.Value.ToString();
        Http.Request.Headers.Cookie = "ato-impersonate=support-ticket; unrelated=do-not-forward";
        Accessor.HttpContext = Http;
    }

    private sealed class Handler(LegacyChatTestScope scope) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string? Header(string name) => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
            scope.Requests.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter,
                Header("X-Workspace-Kind"), Header("X-Workspace-Mode"), Header("X-Workspace-Tenant-Id"), Header("Cookie")));
            if (scope.UpstreamException is not null) throw scope.UpstreamException;
            var payload = request.RequestUri.AbsolutePath == "/api/auth/me"
                ? JsonSerializer.Serialize(new
                {
                    status = "success",
                    data = new
                    {
                        oid = scope.UpstreamObjectId ?? scope.ObjectId,
                        directoryTenantId = scope.DirectoryId,
                        workspace = new
                        {
                            kind = scope.Kind, tenantId = scope.TenantId, mode = scope.Mode, personId = scope.PersonId,
                            roles = new[] { "ISSO" }, permissions = new { canAccessCsp = scope.Kind == "csp" }
                        }
                    }
                })
                : JsonSerializer.Serialize(new { status = "success", data = new { permissions = new { canRead = scope.SystemReadable } } });
            return Task.FromResult(new HttpResponseMessage(scope.UpstreamStatus)
            {
                Content = new StringContent(scope.ResponseOverride ?? payload, Encoding.UTF8, "application/json")
            });
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _services.Dispose();
    }
}

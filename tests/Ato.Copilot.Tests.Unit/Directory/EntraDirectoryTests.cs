using System.Net;
using System.Security.Claims;
using Azure.Core;
using Ato.Copilot.Mcp.Services.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Entra;

public class EntraDirectoryTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private static readonly ClaimsPrincipal Actor = new(new ClaimsIdentity(new[] {
        new Claim("tid", Tenant), new Claim("oid", "22222222-2222-2222-2222-222222222222") }, "test"));
    private sealed class Handler : HttpMessageHandler
    {
        public Uri? Uri; public string? Bearer; public HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri; Bearer = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent("{\"value\":[{\"id\":\"33333333-3333-3333-3333-333333333333\",\"displayName\":\"Test User\",\"mail\":null,\"userPrincipalName\":\"test@example.mil\"}],\"@odata.nextLink\":\"https://untrusted.example/next\"}") });
        }
    }
    private sealed class Credential : TokenCredential
    {
        public TokenRequestContext? Context;
        public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct) { Context = context; return new("test-token", DateTimeOffset.UtcNow.AddHours(1)); }
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct) => ValueTask.FromResult(GetToken(context, ct));
    }
    private static EntraDirectoryService Service(Handler handler, Credential credential, bool allowed = true, string cloud = "Government")
    {
        var workspace = new Mock<IWorkspaceService>();
        workspace.Setup(x => x.IsCspAdministrator(It.IsAny<ClaimsPrincipal>())).Returns(allowed);
        workspace.SetupGet(x => x.Current).Returns(new WorkspaceResponse("csp", null, "Provider", "ordinary", null, [], new(true, true, true)));
        return new(new HttpClient(handler), Options.Create(new EntraDirectoryOptions { Connections = [new() {
            Id = "provider", Name = "Provider directory", AdminDirectoryTenantId = Tenant, DirectoryTenantId = Tenant,
            Cloud = cloud, ClientId = Tenant, ClientSecret = "test-only" }] }), workspace.Object, _ => credential);
    }
    [Theory]
    [InlineData("Public", "graph.microsoft.com")]
    [InlineData("Government", "graph.microsoft.us")]
    [InlineData("DoD", "dod-graph.microsoft.us")]
    public async Task UsesExplicitCloudEscapesSearchAndBoundsResults(string cloud, string host)
    {
        // Arrange
        var handler = new Handler(); var credential = new Credential(); var service = Service(handler, credential, cloud: cloud);
        // Act
        var result = await service.SearchAsync(Actor, "provider", "O'Brien", default);
        // Assert
        Assert.Equal(host, handler.Uri!.Host);
        Assert.Contains("O''Brien", Uri.UnescapeDataString(handler.Uri.Query));
        Assert.Contains("$top=20", Uri.UnescapeDataString(handler.Uri.Query));
        Assert.Equal($"https://{host}/.default", credential.Context!.Value.Scopes.Single());
        Assert.Equal("test@example.mil", result.Users.Single().Email);
        Assert.Equal(Tenant, result.Users.Single().DirectoryTenantId);
        Assert.True(result.HasMore);
    }
    [Fact]
    public async Task DeniesBeforeMakingDirectoryRequest()
    {
        // Arrange
        var handler = new Handler(); var service = Service(handler, new(), false);
        // Act
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => service.SearchAsync(Actor, "provider", "test", default));
        // Assert
        Assert.Equal(403, error.StatusCode); Assert.Null(handler.Uri);
    }
    [Fact]
    public async Task RejectsUnknownConnectionAndShortQuery()
    {
        // Arrange
        var handler = new Handler(); var service = Service(handler, new());
        // Act / Assert
        await Assert.ThrowsAsync<WorkspaceException>(() => service.SearchAsync(Actor, "other-directory", "test", default));
        await Assert.ThrowsAsync<WorkspaceException>(() => service.SearchAsync(Actor, "provider", "t", default));
        Assert.Null(handler.Uri);
    }
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, 503)]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    public async Task SurfacesFailuresInsteadOfEmptyResults(HttpStatusCode status, int expected)
    {
        // Arrange
        var handler = new Handler { Status = status }; var service = Service(handler, new());
        // Act
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => service.SearchAsync(Actor, "provider", "test", default));
        // Assert
        Assert.Equal(expected, error.StatusCode);
    }
    [Fact]
    public async Task AnotherOperatorDirectoryCannotDiscoverOrSearchTheConnection()
    {
        // Arrange
        var handler = new Handler(); var service = Service(handler, new());
        var other = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim("tid", "44444444-4444-4444-4444-444444444444"),
            new Claim("oid", "22222222-2222-2222-2222-222222222222") }, "test"));
        // Act / Assert
        Assert.Empty(service.Connections(other));
        await Assert.ThrowsAsync<WorkspaceException>(() => service.SearchAsync(other, "provider", "test", default));
        Assert.Null(handler.Uri);
    }
    [Fact]
    public async Task AnonymousRequestsNeverReachGraph()
    {
        // Arrange
        var handler = new Handler(); var service = Service(handler, new());
        // Act
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => service.SearchAsync(new ClaimsPrincipal(), "provider", "test", default));
        // Assert
        Assert.Equal(401, error.StatusCode); Assert.Null(handler.Uri);
    }

}

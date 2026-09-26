using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Mcp.Endpoints.Csp;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class OfferingOverviewEndpointTests : IAsyncLifetime
{
    private readonly Mock<IProviderAuthorizationService> _service = new(MockBehavior.Strict);
    private readonly Guid _offering = Guid.NewGuid();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string Path => $"/api/csp/offerings/{_offering}/overview";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_service.Object);
        builder.Services.AddAuthentication("Synthetic")
            .AddScheme<AuthenticationSchemeOptions, SyntheticAuthHandler>("Synthetic", _ => { });
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapProviderAuthorizationEndpoints();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Anonymous_IsChallengedBeforeService()
    {
        // Arrange
        _client.DefaultRequestHeaders.Add("X-Synthetic-Anonymous", "true");
        // Act
        var response = await _client.GetAsync(Path);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", 1, 1, 10)]
    [InlineData("?authorizationPage=2&packagePage=3&pageSize=5", 2, 3, 5)]
    public async Task Get_ReturnsContractAndIndependentPages(string query, int authorizationPage, int packagePage, int pageSize)
    {
        // Arrange
        var package = new PackageStatus(Guid.NewGuid(), Guid.NewGuid(), "Retained package", 3,
            "ReadyForReview", "Unpublished", new(1, 0, 1, 0, 0, 0, 0), null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _service.Setup(x => x.OverviewAsync(_offering, authorizationPage, packagePage, pageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OfferingOverview(_offering, 7,
                new([], authorizationPage, pageSize, 11, 5, 4, 2),
                new([new(package, null, null, null, 1, 1)], packagePage, pageSize, 23, 1, 2, 30,
                    new(package.PackageId, package.Name, "AuthorizationDecisionClaim")),
                new(10, 3, 4, 5, 6), new(null, false, 0, 7, 2)));
        // Act
        var response = await _client.GetAsync(Path + query);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("status").GetString().Should().Be("success");
        json.TryGetProperty("metadata", out _).Should().BeTrue();
        var data = json.GetProperty("data");
        data.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(
            "offeringId", "offeringRevision", "authorizations", "packages", "capabilities", "hosting");
        data.GetProperty("authorizations").GetProperty("total").GetInt32().Should().Be(11);
        data.GetProperty("authorizations").GetProperty("page").GetInt32().Should().Be(authorizationPage);
        data.GetProperty("packages").GetProperty("page").GetInt32().Should().Be(packagePage);
        data.GetProperty("packages").GetProperty("pageSize").GetInt32().Should().Be(pageSize);
        var item = data.GetProperty("packages").GetProperty("items")[0];
        item.GetProperty("package").GetProperty("processingState").GetString().Should().Be("ReadyForReview");
        foreach (var field in new[] { "packageVersionId", "version", "boundaryRevisionId" })
            item.GetProperty(field).ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("packages").GetProperty("preferredAuthorizationReview").GetProperty("type")
            .GetString().Should().Be("AuthorizationDecisionClaim");
        data.GetProperty("hosting").GetProperty("name").ValueKind.Should().Be(JsonValueKind.Null);
        _service.VerifyAll();
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("forbidden", HttpStatusCode.Forbidden, "PROVIDER_ACCESS_DENIED")]
    [InlineData("missing", HttpStatusCode.NotFound, "PROVIDER_RECORD_NOT_FOUND")]
    [InlineData("invalid", HttpStatusCode.BadRequest, "INVALID_PROVIDER_REQUEST")]
    [InlineData("corrupt", HttpStatusCode.ServiceUnavailable, "PROVIDER_OFFERING_OVERVIEW_UNAVAILABLE")]
    [InlineData("json", HttpStatusCode.ServiceUnavailable, "PROVIDER_OFFERING_OVERVIEW_UNAVAILABLE")]
    [InlineData("database", HttpStatusCode.ServiceUnavailable, "PROVIDER_OFFERING_OVERVIEW_UNAVAILABLE")]
    public async Task Failure_IsExplicitAndDoesNotInventData(string kind, HttpStatusCode status, string code)
    {
        // Arrange
        Exception error = kind switch
        {
            "forbidden" => new UnauthorizedAccessException("Provider context required"),
            "missing" => new KeyNotFoundException("Not found"),
            "invalid" => new ArgumentException("Invalid paging"),
            "corrupt" => new InvalidDataException("PRIVATE retained context"),
            "json" => new JsonException("PRIVATE JSON"),
            _ => new SqliteException("PRIVATE database", 1)
        };
        _service.Setup(x => x.OverviewAsync(_offering, 1, 1, 10, It.IsAny<CancellationToken>())).ThrowsAsync(error);
        // Act
        var response = await _client.GetAsync(Path);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Assert
        response.StatusCode.Should().Be(status);
        json.GetProperty("error").GetProperty("errorCode").GetString().Should().Be(code);
        json.TryGetProperty("data", out _).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().NotContain("PRIVATE");
    }

    [Theory]
    [InlineData("?authorizationPage=abc")]
    [InlineData("?packagePage=2147483648")]
    [InlineData("?pageSize=abc")]
    public async Task InvalidQueryType_IsRejectedBeforeService(string query)
    {
        // Arrange
        // Act
        var response = await _client.GetAsync(Path + query);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Overview_IsGetOnly()
    {
        // Arrange
        using var content = JsonContent.Create(new { });
        // Act
        var response = await _client.PostAsync(Path, content);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        _service.VerifyNoOtherCalls();
    }

    private sealed class SyntheticAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.ContainsKey("X-Synthetic-Anonymous") ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "synthetic-actor")], Scheme.Name)), Scheme.Name)));
    }
}

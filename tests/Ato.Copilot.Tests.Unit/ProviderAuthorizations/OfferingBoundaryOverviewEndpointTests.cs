using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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

public sealed class OfferingBoundaryOverviewEndpointTests : IAsyncLifetime
{
    private readonly Mock<IProviderAuthorizationService> _service = new(MockBehavior.Strict);
    private readonly Guid _offering = Guid.NewGuid();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string Path => $"/api/csp/offerings/{_offering}/boundary-overview";

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

    [Theory]
    [InlineData("", 1, 1, 10)]
    [InlineData("?capabilityPage=2&missionPage=3&pageSize=5", 2, 3, 5)]
    public async Task Get_UsesExactPagingEnvelopeAndNullableWireFields(string query, int capabilityPage, int missionPage, int pageSize)
    {
        // Arrange
        var source = new OfferingBoundaryCapability(null, Guid.NewGuid(), Guid.NewGuid(),
            "Synthetic proposal", "NeedsReview", "Unpublished", null, Guid.NewGuid());
        var mission = new OfferingBoundaryMission(Guid.NewGuid(), Guid.NewGuid().ToString(), null,
            "Undetermined", false, 0, []);
        _service.Setup(x => x.BoundaryOverviewAsync(_offering, capabilityPage, missionPage, pageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OfferingBoundaryOverview(_offering, 4,
                new([source], capabilityPage, pageSize, 31, 25, 6), new([mission], missionPage, pageSize, 20)));

        // Act
        var response = await _client.GetAsync(Path + query);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("status").GetString().Should().Be("success");
        json.TryGetProperty("metadata", out _).Should().BeTrue();
        var data = json.GetProperty("data");
        data.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo("offeringId", "offeringRevision", "capabilities", "missionSystems");
        data.GetProperty("capabilities").GetProperty("total").GetInt32().Should().Be(31);
        data.GetProperty("capabilities").GetProperty("page").GetInt32().Should().Be(capabilityPage);
        data.GetProperty("capabilities").GetProperty("pageSize").GetInt32().Should().Be(pageSize);
        data.GetProperty("capabilities").GetProperty("items")[0].GetProperty("capabilityId").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("capabilities").GetProperty("items")[0].GetProperty("releaseId").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("missionSystems").GetProperty("page").GetInt32().Should().Be(missionPage);
        data.GetProperty("missionSystems").GetProperty("items")[0].GetProperty("systemName").ValueKind.Should().Be(JsonValueKind.Null);
        _service.VerifyAll();
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("forbidden", HttpStatusCode.Forbidden, "PROVIDER_ACCESS_DENIED")]
    [InlineData("missing", HttpStatusCode.NotFound, "PROVIDER_RECORD_NOT_FOUND")]
    [InlineData("invalid", HttpStatusCode.BadRequest, "INVALID_PROVIDER_REQUEST")]
    [InlineData("corrupt", HttpStatusCode.ServiceUnavailable, "PROVIDER_BOUNDARY_OVERVIEW_UNAVAILABLE")]
    [InlineData("json", HttpStatusCode.ServiceUnavailable, "PROVIDER_BOUNDARY_OVERVIEW_UNAVAILABLE")]
    [InlineData("database", HttpStatusCode.ServiceUnavailable, "PROVIDER_BOUNDARY_OVERVIEW_UNAVAILABLE")]
    public async Task Failures_AreExplicitStandardEnvelopes(string kind, HttpStatusCode status, string code)
    {
        // Arrange
        Exception error = kind switch
        {
            "forbidden" => new UnauthorizedAccessException("Provider context required"),
            "missing" => new KeyNotFoundException("Not found"),
            "invalid" => new ArgumentException("Invalid paging"),
            "corrupt" => new InvalidDataException("PRIVATE stored context details"),
            "json" => new JsonException("PRIVATE stored JSON"),
            _ => new SqliteException("PRIVATE database details", 1)
        };
        _service.Setup(x => x.BoundaryOverviewAsync(_offering, 1, 1, 10, It.IsAny<CancellationToken>())).ThrowsAsync(error);

        // Act
        var response = await _client.GetAsync(Path);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        response.StatusCode.Should().Be(status);
        json.GetProperty("status").GetString().Should().Be("error");
        json.GetProperty("error").GetProperty("errorCode").GetString().Should().Be(code);
        json.TryGetProperty("data", out _).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().NotContain("PRIVATE");
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

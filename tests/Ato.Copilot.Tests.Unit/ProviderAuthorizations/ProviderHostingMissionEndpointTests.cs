using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Interfaces.Compliance;
using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Mcp.Endpoints.Csp;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ProviderHostingMissionEndpointTests : IAsyncLifetime
{
    private readonly Mock<IProviderHostingService> _hosting = new(MockBehavior.Strict);
    private readonly Mock<IProviderMissionService> _mission = new(MockBehavior.Strict);
    private readonly Mock<IProviderAuthorizationService> _authorizations = new(MockBehavior.Strict);
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Guid _offering = Guid.NewGuid();
    private readonly string _system = Guid.NewGuid().ToString();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_hosting.Object);
        builder.Services.AddSingleton(_mission.Object);
        builder.Services.AddSingleton(_authorizations.Object);
        builder.Services.AddAuthentication("Synthetic").AddScheme<AuthenticationSchemeOptions, Auth>("Synthetic", _ => { });
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapProviderAuthorizationEndpoints();
        _app.MapProviderHostingEndpoints();
        _app.MapProviderMissionEndpoints();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); }

    [Fact]
    public async Task HostingLists_AreMappedWithPagedEnvelopes_NotEmpty404s()
    {
        // Arrange
        _hosting.Setup(x => x.ScopesAsync(_offering, 2, 3, It.IsAny<CancellationToken>())).ReturnsAsync(new PagedResult<ProviderHostingScopeResponse>([], 2, 3, 7));
        _hosting.Setup(x => x.AssignmentsAsync(_offering, 1, 25, It.IsAny<CancellationToken>())).ReturnsAsync(new PagedResult<ProviderHostingAssignmentResponse>([], 1, 25, 0));
        // Act
        var scopes = await _client.GetAsync($"/api/csp/offerings/{_offering}/hosting-scope-revisions?page=2&pageSize=3");
        var assignments = await _client.GetAsync($"/api/csp/offerings/{_offering}/hosting-assignments?page=1&pageSize=25");
        // Assert
        scopes.StatusCode.Should().Be(HttpStatusCode.OK);
        assignments.StatusCode.Should().Be(HttpStatusCode.OK);
        (await scopes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("total").GetInt32().Should().Be(7);
        _hosting.VerifyAll();
    }

    [Fact]
    public async Task CreateScope_ReturnsExactReachableLocationAndKey()
    {
        // Arrange
        var id = Guid.NewGuid();
        var body = new CreateProviderHostingScopeRequest(1, null, "Test", [], [], []);
        var expected = new ProviderHostingScopeResponse(_offering, 2, new(id, 1, "hash"), null, null, "Test", [], [], []);
        _hosting.Setup(x => x.CreateScopeAsync(_offering, It.Is<CreateProviderHostingScopeRequest>(r => r.Name == "Test"),
            "scope-key", "actor", It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        _hosting.Setup(x => x.ScopeAsync(_offering, id, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        _client.DefaultRequestHeaders.Add("Idempotency-Key", "scope-key");
        // Act
        var created = await _client.PostAsJsonAsync($"/api/csp/offerings/{_offering}/hosting-scope-revisions", body);
        var exact = await _client.GetAsync(created.Headers.Location);
        // Assert
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.Location!.OriginalString.Should().EndWith($"/hosting-scope-revisions/{id}");
        exact.StatusCode.Should().Be(HttpStatusCode.OK);
        _hosting.VerifyAll();
    }

    [Fact]
    public async Task MissionListAndAssociation_BindOnlyRouteSystemAndExactAllocation()
    {
        // Arrange
        var assignment = Guid.NewGuid();
        var relationship = new MissionProviderRelationshipResponse(Guid.NewGuid(), 1, assignment, 1, _offering,
            _system, "Undetermined", true, null, null, null, null, [], "Offering", "Provider", "Mission", "Hosting");
        _mission.Setup(x => x.RelationshipsAsync(_system, 1, 25, It.IsAny<CancellationToken>())).ReturnsAsync(new PagedResult<MissionProviderRelationshipResponse>([relationship], 1, 25, 1));
        _mission.Setup(x => x.AssociateAsync(_system, new(assignment, 1), "actor", It.IsAny<CancellationToken>(), "associate-key"))
            .ReturnsAsync(relationship);
        _client.DefaultRequestHeaders.Add("Idempotency-Key", "associate-key");
        // Act
        var list = await _client.GetAsync($"/api/dashboard/systems/{_system}/provider-relationships");
        var associated = await _client.PostAsJsonAsync($"/api/dashboard/systems/{_system}/provider-relationships", new CreateMissionProviderRelationshipRequest(assignment, 1));
        // Assert
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        associated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await associated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("offeringName").GetString().Should().Be("Offering");
        _mission.VerifyAll();
    }

    [Fact]
    public async Task MissingMutationKey_IsExplicitBadRequestBeforeService()
    {
        // Arrange
        var body = new CreateMissionProviderRelationshipRequest(Guid.NewGuid(), 1);
        // Act
        var response = await _client.PostAsJsonAsync($"/api/dashboard/systems/{_system}/provider-relationships", body);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("errorCode")
            .GetString().Should().Be("INVALID_PROVIDER_REQUEST");
        _mission.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AllocationCreate_ReturnsReachableExactLocation()
    {
        // Arrange
        var assignment = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var expected = new ProviderHostingAssignmentResponse(assignment, 1, _offering, _system, new(scopeId, 1, "hash"), [], "Undetermined");
        _hosting.Setup(x => x.AssignAsync(_offering, It.IsAny<CreateProviderHostingAssignmentRequest>(),
            "allocation-key", "actor", It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        _hosting.Setup(x => x.AssignmentAsync(_offering, assignment, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        _client.DefaultRequestHeaders.Add("Idempotency-Key", "allocation-key");
        // Act
        var result = await _client.PostAsJsonAsync($"/api/csp/offerings/{_offering}/hosting-assignments",
            new CreateProviderHostingAssignmentRequest(Guid.NewGuid(), _system, scopeId, [], []));
        var exact = await _client.GetAsync(result.Headers.Location);
        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Created);
        exact.StatusCode.Should().Be(HttpStatusCode.OK);
        _hosting.VerifyAll();
    }

    [Theory]
    [InlineData("corrupt", HttpStatusCode.ServiceUnavailable, "PROVIDER_PROJECTION_UNAVAILABLE")]
    [InlineData("database", HttpStatusCode.ServiceUnavailable, "PROVIDER_PROJECTION_UNAVAILABLE")]
    [InlineData("conflict", HttpStatusCode.Conflict, "RESPONSIBILITY_CONTEXT_STALE")]
    public async Task ProjectionFailures_AreExplicitAndDoNotExposeSourceDetails(string failure, HttpStatusCode status, string code)
    {
        // Arrange
        Exception error = failure switch
        {
            "corrupt" => new InvalidDataException("PRIVATE source"),
            "database" => new DbUpdateException("PRIVATE database"),
            _ => new ResponsibilityReviewConflictException("PRIVATE context")
        };
        _mission.Setup(x => x.RelationshipsAsync(_system, 1, 25, It.IsAny<CancellationToken>())).ThrowsAsync(error);
        // Act
        var response = await _client.GetAsync($"/api/dashboard/systems/{_system}/provider-relationships");
        // Assert
        response.StatusCode.Should().Be(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("errorCode").GetString().Should().Be(code);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("PRIVATE").And.NotContain("\"data\"");
    }

    [Fact]
    public async Task AuthorizationRecordKind_IsForwardedBeforeServerPaging()
    {
        // Arrange
        _authorizations.Setup(x => x.DecisionsAsync(_offering, 2, 5, null, It.IsAny<CancellationToken>(), "InheritedMicrosoftReference"))
            .ReturnsAsync(new PagedResult<ProviderDecisionResponse>([], 2, 5, 4));
        // Act
        var response = await _client.GetAsync($"/api/csp/offerings/{_offering}/authorization-records?page=2&pageSize=5&recordKind=InheritedMicrosoftReference");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _authorizations.VerifyAll();
    }

    [Theory]
    [InlineData("provider-relationships")]
    [InlineData("applicable-provider-capabilities")]
    public async Task AnonymousMissionRequest_ChallengesBeforeService(string suffix)
    {
        // Arrange
        _client.DefaultRequestHeaders.Add("X-Anonymous", "true");
        // Act
        var response = await _client.GetAsync($"/api/dashboard/systems/{_system}/{suffix}");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _mission.VerifyNoOtherCalls();
    }

    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.ContainsKey("X-Anonymous")
            ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "actor")], Scheme.Name)), Scheme.Name)));
    }
}

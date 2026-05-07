using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// Integration tests for <see cref="PersonEndpoints"/> (T059 / FR-022 / research §R1).
/// </summary>
public class PersonEndpointsTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";
    private static readonly Guid AdminTenantId = Guid.NewGuid();
    private static readonly Guid AdminUserId = Guid.NewGuid();

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Mock<IWizardAuditService> _auditMock = new();
    private readonly Mock<IDirectorySearchClient> _directoryMock = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        var dbName = $"PersonEndpoints_{Guid.NewGuid():N}";
        builder.Services.AddDbContextFactory<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddScoped<IWizardAuditService>(_ => _auditMock.Object);
        builder.Services.AddScoped<IDirectorySearchClient>(_ => _directoryMock.Object);
        builder.Services.AddScoped<IPersonService, PersonService>();
        builder.Services.AddSingleton<ILogger<PersonService>>(NullLogger<PersonService>.Instance);

        builder.Services.AddAuthentication(AuthScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(AuthScheme, _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(
                OnboardingAdministratorRequirement.PolicyName,
                p => p.RequireAssertion(_ => true));
        });

        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapPersonEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-User", $"{AdminTenantId}|{AdminUserId}");
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _client.Dispose();
    }

    [Fact]
    public async Task Post_CreatesPerson_ReturnsOk()
    {
        var response = await _client.PostAsJsonAsync("/api/onboarding/persons", new
        {
            displayName = "Carol Roberts",
            email = "carol@example.mil",
            phoneNumber = "+1-555-0100",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("displayName").GetString().Should().Be("Carol Roberts");
        body.GetProperty("data").GetProperty("isLinkedToDirectory").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Get_ListsAllForTenant()
    {
        await _client.PostAsJsonAsync("/api/onboarding/persons", new { displayName = "Alice", email = "alice@x.mil" });
        await _client.PostAsJsonAsync("/api/onboarding/persons", new { displayName = "Bob", email = "bob@x.mil" });

        var response = await _client.GetAsync("/api/onboarding/persons");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task DirectorySearch_DelegatesToGraphMock()
    {
        _directoryMock.Setup(d => d.SearchAsync("ja", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new DirectoryPersonDto(Guid.NewGuid(), "Jane Directory", "jane@dir.mil", "Cyber"),
            });

        var response = await _client.GetAsync("/api/onboarding/persons/directory?query=ja");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetArrayLength().Should().Be(1);
        body.GetProperty("data")[0].GetProperty("displayName").GetString().Should().Be("Jane Directory");
    }

    [Fact]
    public async Task Promote_HappyPath_ReturnsOk()
    {
        var create = await _client.PostAsJsonAsync("/api/onboarding/persons",
            new { displayName = "Carol", email = "carol@x.mil" });
        var createBody = await create.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var personId = createBody.GetProperty("data").GetProperty("id").GetGuid();

        var promote = await _client.PostAsJsonAsync(
            $"/api/onboarding/persons/{personId}/promote",
            new { entraObjectId = Guid.NewGuid() });

        promote.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await promote.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetProperty("isLinkedToDirectory").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("id").GetGuid().Should().Be(
            personId, "research §R1: id is stable across promotion");
    }

    [Fact]
    public async Task Promote_AlreadyLinked_Returns409()
    {
        var create = await _client.PostAsJsonAsync("/api/onboarding/persons",
            new { displayName = "Carol", email = "carol2@x.mil" });
        var createBody = await create.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var personId = createBody.GetProperty("data").GetProperty("id").GetGuid();

        await _client.PostAsJsonAsync(
            $"/api/onboarding/persons/{personId}/promote",
            new { entraObjectId = Guid.NewGuid() });

        var second = await _client.PostAsJsonAsync(
            $"/api/onboarding/persons/{personId}/promote",
            new { entraObjectId = Guid.NewGuid() });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var header))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var parts = header.ToString().Split('|', 2);
            var claims = new List<Claim>();
            if (!string.IsNullOrEmpty(parts[0])) claims.Add(new Claim("tid", parts[0]));
            if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1])) claims.Add(new Claim("oid", parts[1]));
            claims.Add(new Claim(ClaimTypes.Name, "test-user"));
            var identity = new ClaimsIdentity(claims, AuthScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, AuthScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

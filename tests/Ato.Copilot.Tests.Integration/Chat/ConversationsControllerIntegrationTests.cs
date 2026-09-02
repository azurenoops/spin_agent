using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Ato.Copilot.Chat.Data;
using Ato.Copilot.Chat.Hubs;
using Ato.Copilot.Chat.Models;
using Ato.Copilot.Chat.Services;
using Ato.Copilot.Core.Interfaces;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Integration.Chat;

/// <summary>
/// Integration tests for conversation endpoints (US2).
/// Uses TestServer with manually configured WebApplication.
/// </summary>
public class ConversationsControllerIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        var dbName = $"ChatIntegration_Conversations_{Guid.NewGuid():N}";

        builder.Services.AddDbContext<ChatDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        builder.Services.AddSingleton<IPathSanitizationService, PathSanitizationService>();
        builder.Services.AddScoped<IChatService, ChatService>();
        builder.Services.AddHttpClient("McpServer", client =>
        {
            client.BaseAddress = new Uri("http://localhost:3001");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        builder.Services.AddControllers()
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            })
            .AddApplicationPart(typeof(Ato.Copilot.Chat.Controllers.ConversationsController).Assembly);
        builder.Services.AddSignalR();
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        // ConversationsController carries [Authorize]. Register authentication and
        // authorization services so the middleware pipeline can resolve the metadata —
        // otherwise ASP.NET Core throws InvalidOperationException at request time.
        // The default scheme allows all requests through (no real identity provider
        // needed for these integration tests).
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Test";
            options.DefaultChallengeScheme = "Test";
        })
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
            TestPassThroughAuthHandler>("Test", _ => { });
        builder.Services.AddAuthorization();

        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseCors();
        _app.UseRouting();
        // UseAuthentication + UseAuthorization MUST be ordered between UseRouting()
        // and endpoint mapping (MapControllers). Without this, any endpoint with
        // [Authorize] metadata raises InvalidOperationException at runtime.
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        _app.MapHub<ChatHub>("/hubs/chat");

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ─── Happy Path Tests ────────────────────────────────────────

    [Fact]
    public async Task CreateConversation_ReturnsConversationWithId()
    {
        // Arrange
        var request = new CreateConversationRequest { Title = "Integration Test Conv", UserId = "test-user" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/conversations", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var conversation = JsonSerializer.Deserialize<Conversation>(content, _jsonOptions);
        conversation.Should().NotBeNull();
        conversation!.Id.Should().NotBeNullOrEmpty();
        conversation.Title.Should().Be("Integration Test Conv");
    }

    [Fact]
    public async Task GetConversations_ReturnsCreatedConversations()
    {
        // Arrange
        // DEF-001: userId is now derived from auth claims (NameIdentifier = "test-user"),
        // not from the query string. Create conversations owned by the same identity.
        await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "Conv 1", UserId = "test-user" }, _jsonOptions);
        await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "Conv 2", UserId = "test-user" }, _jsonOptions);

        // Act — userId query param is ignored post-DEF-001; identity comes from claims.
        var response = await _client.GetAsync("/api/conversations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var conversations = JsonSerializer.Deserialize<List<Conversation>>(content, _jsonOptions);
        conversations.Should().NotBeNull();
        conversations!.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task GetConversationById_ReturnsConversation()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "Detail Conv", UserId = "test-user" }, _jsonOptions);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<Conversation>(createContent, _jsonOptions)!;

        // Act
        var response = await _client.GetAsync($"/api/conversations/{created.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var conversation = JsonSerializer.Deserialize<Conversation>(content, _jsonOptions);
        conversation.Should().NotBeNull();
        conversation!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task DeleteConversation_ThenGetReturns404()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "To Delete", UserId = "test-user" }, _jsonOptions);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<Conversation>(createContent, _jsonOptions)!;

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/conversations/{created.Id}");
        var getResponse = await _client.GetAsync($"/api/conversations/{created.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Error Path Tests ────────────────────────────────────────

    [Fact]
    public async Task DeleteConversation_WithNonExistentId_Returns404()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/conversations/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetConversation_WithNonExistentId_Returns404()
    {
        // Act
        var response = await _client.GetAsync($"/api/conversations/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Search Tests ────────────────────────────────────────────

    [Fact]
    public async Task SearchConversations_ReturnsMatchingResults()
    {
        // Arrange
        // DEF-001: userId is now derived from auth claims (NameIdentifier = "test-user").
        await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "Compliance Review", UserId = "test-user" }, _jsonOptions);
        await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "General Chat", UserId = "test-user" }, _jsonOptions);

        // Act — userId query param is ignored post-DEF-001; identity comes from claims.
        var response = await _client.GetAsync("/api/conversations/search?query=compliance");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var conversations = JsonSerializer.Deserialize<List<Conversation>>(content, _jsonOptions);
        conversations.Should().NotBeNull();
        conversations!.Should().HaveCount(1);
        conversations.First().Title.Should().Contain("Compliance");
    }

    [Fact]
    public async Task SearchConversations_WithEmptyQuery_Returns400()
    {
        // Act
        var response = await _client.GetAsync("/api/conversations/search?query=&userId=test-user");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

/// <summary>
/// Minimal authentication handler for integration tests.
/// Authenticates every request as an anonymous authenticated principal so
/// endpoints carrying [Authorize] don't throw the "middleware not found"
/// InvalidOperationException. No real identity provider is needed.
/// </summary>
internal sealed class TestPassThroughAuthHandler(
    Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
    Microsoft.Extensions.Logging.ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : Microsoft.AspNetCore.Authentication.AuthenticationHandler<
        Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.NameIdentifier, "test-user"),
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Test");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, "Test");
        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
    }
}

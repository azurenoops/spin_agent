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
/// Integration tests for POST /api/messages and GET /api/messages.
/// Uses TestServer with manually configured WebApplication.
/// </summary>
public class MessagesControllerIntegrationTests : IAsyncLifetime
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

        var dbName = $"ChatIntegration_Messages_{Guid.NewGuid():N}";

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
            .AddApplicationPart(typeof(Ato.Copilot.Chat.Controllers.MessagesController).Assembly);
        builder.Services.AddSignalR();
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        // MessagesController carries [Authorize]. Register authentication + authorization
        // services so the middleware pipeline can resolve the metadata — otherwise
        // ASP.NET Core throws InvalidOperationException at request time.
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
        // and endpoint mapping. Without this, any endpoint with [Authorize] metadata
        // raises InvalidOperationException at runtime.
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

    private async Task<string> CreateConversation(HttpClient client)
    {
        var request = new CreateConversationRequest { Title = "Test Conversation", UserId = "test-user" };
        var response = await client.PostAsJsonAsync("/api/conversations", request, _jsonOptions);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var conversation = JsonSerializer.Deserialize<Conversation>(content, _jsonOptions);
        return conversation!.Id;
    }

    // ─── Happy Path Tests ────────────────────────────────────────

    [Fact]
    public async Task PostMessage_WithValidRequest_Returns200WithChatResponse()
    {
        // Arrange
        var conversationId = await CreateConversation(_client);

        var request = new SendMessageRequest
        {
            ConversationId = conversationId,
            Message = "Hello, AI!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/messages", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(content, _jsonOptions);
        chatResponse.Should().NotBeNull();
        chatResponse!.MessageId.Should().NotBeNullOrEmpty();
    }

    // ─── Error Path Tests ────────────────────────────────────────

    [Fact]
    public async Task PostMessage_WithEmptyConversationId_Returns400()
    {
        // Arrange
        var request = new SendMessageRequest
        {
            ConversationId = "",
            Message = "Test message"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/messages", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostMessage_WithEmptyMessage_Returns400()
    {
        // Arrange
        var request = new SendMessageRequest
        {
            ConversationId = Guid.NewGuid().ToString(),
            Message = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/messages", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── Pagination / Ordering Tests ─────────────────────────────

    [Fact]
    public async Task GetMessages_ReturnsMessagesOrderedByTimestampAscending()
    {
        // Arrange
        var conversationId = await CreateConversation(_client);

        // Send multiple messages to create data
        for (int i = 0; i < 3; i++)
        {
            var request = new SendMessageRequest
            {
                ConversationId = conversationId,
                Message = $"Message {i + 1}"
            };
            await _client.PostAsJsonAsync("/api/messages", request, _jsonOptions);
        }

        // Act
        var response = await _client.GetAsync($"/api/messages?conversationId={conversationId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(content, _jsonOptions);
        messages.Should().NotBeNull();
        messages!.Should().BeInAscendingOrder(m => m.Timestamp);
    }

    [Fact]
    public async Task GetMessages_WithPagination_ReturnsCorrectSubset()
    {
        // Arrange
        var conversationId = await CreateConversation(_client);

        // Create some messages
        for (int i = 0; i < 5; i++)
        {
            var request = new SendMessageRequest
            {
                ConversationId = conversationId,
                Message = $"Message {i + 1}"
            };
            await _client.PostAsJsonAsync("/api/messages", request, _jsonOptions);
        }

        // Act — get page with take=2 skip=0
        var response = await _client.GetAsync($"/api/messages?conversationId={conversationId}&skip=0&take=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(content, _jsonOptions);
        messages.Should().NotBeNull();
        messages!.Count.Should().BeLessOrEqualTo(2);
    }
}

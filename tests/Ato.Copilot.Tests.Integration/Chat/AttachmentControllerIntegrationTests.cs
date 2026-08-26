using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
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
/// Integration tests for POST /api/messages/{messageId}/attachments (US5).
/// Uses TestServer with manually configured WebApplication.
/// </summary>
public class AttachmentControllerIntegrationTests : IAsyncLifetime
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

        var dbName = $"ChatIntegration_Attachments_{Guid.NewGuid():N}";

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

        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseCors();
        _app.UseRouting();
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

    private async Task<(string conversationId, string messageId)> CreateConversationAndMessageAsync()
    {
        // Create conversation
        var convResponse = await _client.PostAsJsonAsync("/api/conversations",
            new CreateConversationRequest { Title = "Test", UserId = "test-user" }, _jsonOptions);
        var convContent = await convResponse.Content.ReadAsStringAsync();
        var conversation = JsonSerializer.Deserialize<Conversation>(convContent, _jsonOptions)!;

        // Send a message to get a messageId
        var msgRequest = new SendMessageRequest
        {
            ConversationId = conversation.Id,
            Message = "Test message for attachment"
        };
        var msgResponse = await _client.PostAsJsonAsync("/api/messages", msgRequest, _jsonOptions);
        var msgContent = await msgResponse.Content.ReadAsStringAsync();
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(msgContent, _jsonOptions)!;

        return (conversation.Id, chatResponse.MessageId);
    }

    private static MultipartFormDataContent CreateFileContent(string fileName, byte[] content, string contentType = "text/plain")
    {
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        formData.Add(fileContent, "file", fileName);
        return formData;
    }

    // ─── Happy Path Tests ────────────────────────────────────────

    [Fact]
    public async Task UploadAttachment_WithValidFile_Returns200WithAttachment()
    {
        // Arrange
        var (_, messageId) = await CreateConversationAndMessageAsync();
        var content = "Hello, this is test content."u8.ToArray();
        var formData = CreateFileContent("test.txt", content);

        // Act
        var response = await _client.PostAsync($"/api/messages/{messageId}/attachments", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseContent = await response.Content.ReadAsStringAsync();
        var attachment = JsonSerializer.Deserialize<MessageAttachment>(responseContent, _jsonOptions);
        attachment.Should().NotBeNull();
        attachment!.FileName.Should().Be("test.txt");
        attachment.ContentType.Should().Be("text/plain");
        attachment.Size.Should().BeGreaterThan(0);
    }

    // ─── Error Path Tests ────────────────────────────────────────

    [Fact]
    public async Task UploadAttachment_WithNoFile_Returns400()
    {
        // Arrange
        var formData = new MultipartFormDataContent();

        // Act
        var response = await _client.PostAsync($"/api/messages/{Guid.NewGuid()}/attachments", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadAttachment_WithEmptyFile_Returns400()
    {
        // Arrange
        var formData = CreateFileContent("empty.txt", Array.Empty<byte>());

        // Act
        var response = await _client.PostAsync($"/api/messages/{Guid.NewGuid()}/attachments", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

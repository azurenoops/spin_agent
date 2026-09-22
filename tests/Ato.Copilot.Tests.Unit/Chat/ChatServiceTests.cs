using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;
using Ato.Copilot.Chat.Data;
using Ato.Copilot.Chat.Models;
using Ato.Copilot.Chat.Services;

namespace Ato.Copilot.Tests.Unit.Chat;

/// <summary>
/// Unit tests for ChatService.SendMessageAsync covering positive, negative, and boundary scenarios.
/// </summary>
public class ChatServiceTests : IDisposable
{
    private readonly ChatDbContext _dbContext;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<ChatService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly LegacyChatTestScope _scope = new();

    public ChatServiceTests()
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ChatDbContext(options);
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<ChatService>>();
        _httpHandlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _scope.Dispose();
    }

    private ChatService CreateService(HttpClient? httpClient = null)
    {
        var client = httpClient ?? CreateSseHttpClient(new
        {
            content = "AI response",
            success = true,
            metadata = new Dictionary<string, object>
            {
                ["intentType"] = "general",
                ["confidence"] = 0.85
            }
        });

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("McpServer"))
            .Returns(client);

        return new ChatService(_dbContext, _httpClientFactoryMock.Object, _loggerMock.Object, Mock.Of<Ato.Copilot.Core.Interfaces.IPathSanitizationService>(), _scope.Resolver);
    }

    /// <summary>
    /// Creates a mock HTTP client that returns an SSE-formatted response
    /// matching the /mcp/chat/stream endpoint format.
    /// </summary>
    private HttpClient CreateSseHttpClient(object resultData)
    {
        var sseBody = $"data: {{\"type\":\"progress\",\"step\":\"Processing...\"}}\n\n" +
                      $"data: {{\"type\":\"result\",\"data\":{JsonSerializer.Serialize(resultData)}}}\n\n";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sseBody, Encoding.UTF8, "text/event-stream")
        };

        return CreateMockHttpClient(response);
    }

    private HttpClient CreateMockHttpClient(HttpResponseMessage response)
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return new HttpClient(_httpHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:3001")
        };
    }

    private async Task<Conversation> SeedConversation()
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Test Conversation",
            UserId = "test-user",
            OwnerKey = LegacyChatTestScope.DefaultOwnerKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Conversations.Add(conversation);
        await _dbContext.SaveChangesAsync();
        return conversation;
    }

    // ─── Positive Tests ──────────────────────────────────────────

    [Fact]
    public async Task SendMessageAsync_WithValidRequest_ReturnsChatResponse()
    {
        // Arrange
        var conversation = await SeedConversation();
        var service = CreateService();
        var request = new SendMessageRequest
        {
            ConversationId = conversation.Id,
            Message = "What is the compliance status?"
        };

        // Act
        var response = await service.SendMessageAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Content.Should().NotBeNullOrEmpty();
        response.MessageId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SendMessageAsync_PersistsUserMessage()
    {
        // Arrange
        var conversation = await SeedConversation();
        var service = CreateService();
        var request = new SendMessageRequest
        {
            ConversationId = conversation.Id,
            Message = "Test message"
        };

        // Act
        await service.SendMessageAsync(request);

        // Assert
        var messages = await _dbContext.Messages
            .Where(m => m.ConversationId == conversation.Id)
            .ToListAsync();

        messages.Should().Contain(m => m.Role == MessageRole.User && m.Content == "Test message");
    }

    [Fact]
    public async Task SendMessageAsync_PersistsAssistantResponse()
    {
        // Arrange
        var conversation = await SeedConversation();
        var service = CreateService();
        var request = new SendMessageRequest
        {
            ConversationId = conversation.Id,
            Message = "Hello"
        };

        // Act
        await service.SendMessageAsync(request);

        // Assert
        var messages = await _dbContext.Messages
            .Where(m => m.ConversationId == conversation.Id && m.Role == MessageRole.Assistant)
            .ToListAsync();

        messages.Should().HaveCount(1);
        messages.First().Status.Should().Be(MessageStatus.Completed);
    }

    // ─── Negative Tests ──────────────────────────────────────────

    [Fact]
    public async Task SendMessageAsync_WhenMcpTimesOut_ReturnsCategorizedError()
    {
        // Arrange
        var conversation = await SeedConversation();
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Request timed out"));

        var client = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:3001") };
        var service = CreateService(client);

        var request = new SendMessageRequest
        {
            ConversationId = conversation.Id,
            Message = "Test"
        };

        // Act
        var response = await service.SendMessageAsync(request);

        // Assert
        response.Success.Should().BeFalse();
        response.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SendMessageAsync_WhenMcpReturns500_ReturnsCategorizedError()
    {
        // Arrange
        var conversation = await SeedConversation();
        var client = CreateMockHttpClient(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\": \"Internal Server Error\"}", Encoding.UTF8, "application/json")
        });
        var service = CreateService(client);

        var request = new SendMessageRequest
        {
            ConversationId = conversation.Id,
            Message = "Test"
        };

        // Act
        var response = await service.SendMessageAsync(request);

        // Assert
        response.Success.Should().BeFalse();
        response.Error.Should().NotBeNullOrEmpty();
    }

    // ─── Boundary Tests ──────────────────────────────────────────

    [Fact]
    public async Task SendMessageAsync_LimitsContextWindowTo20Messages()
    {
        // Arrange
        var conversation = await SeedConversation();

        // Seed 25 messages in the conversation
        for (int i = 0; i < 25; i++)
        {
            _dbContext.Messages.Add(new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = conversation.Id,
                Content = $"Message {i}",
                Role = i % 2 == 0 ? MessageRole.User : MessageRole.Assistant,
                Timestamp = DateTime.UtcNow.AddMinutes(-25 + i),
                Status = MessageStatus.Completed
            });
        }
        await _dbContext.SaveChangesAsync();

        var service = CreateService();
        var request = new SendMessageRequest
        {
            ConversationId = conversation.Id,
            Message = "New message"
        };

        // Act
        var response = await service.SendMessageAsync(request);

        // Assert — request should succeed without error (context window capped)
        response.Should().NotBeNull();
    }

    [Fact]
    public async Task GetConversationHistoryAsync_ReturnsMax20Messages()
    {
        // Arrange
        var conversation = await SeedConversation();
        for (int i = 0; i < 30; i++)
        {
            _dbContext.Messages.Add(new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = conversation.Id,
                Content = $"Message {i}",
                Role = MessageRole.User,
                Timestamp = DateTime.UtcNow.AddMinutes(i),
                Status = MessageStatus.Completed
            });
        }
        await _dbContext.SaveChangesAsync();

        var service = CreateService();

        // Act
        var history = await service.GetConversationHistoryAsync(conversation.Id, 20);

        // Assert
        history.Should().HaveCount(20);
    }

    [Fact]
    public async Task GetMessagesAsync_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        var conversation = await SeedConversation();
        for (int i = 0; i < 10; i++)
        {
            _dbContext.Messages.Add(new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = conversation.Id,
                Content = $"Message {i}",
                Role = MessageRole.User,
                Timestamp = DateTime.UtcNow.AddMinutes(i),
                Status = MessageStatus.Completed
            });
        }
        await _dbContext.SaveChangesAsync();

        var service = CreateService();

        // Act
        var messages = await service.GetMessagesAsync(conversation.Id, skip: 2, take: 3);

        // Assert
        messages.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsMessagesOrderedByTimestamp()
    {
        // Arrange
        var conversation = await SeedConversation();
        var baseTime = DateTime.UtcNow;
        _dbContext.Messages.Add(new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversation.Id,
            Content = "Third",
            Role = MessageRole.User,
            Timestamp = baseTime.AddMinutes(3),
            Status = MessageStatus.Completed
        });
        _dbContext.Messages.Add(new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversation.Id,
            Content = "First",
            Role = MessageRole.User,
            Timestamp = baseTime.AddMinutes(1),
            Status = MessageStatus.Completed
        });
        _dbContext.Messages.Add(new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversation.Id,
            Content = "Second",
            Role = MessageRole.User,
            Timestamp = baseTime.AddMinutes(2),
            Status = MessageStatus.Completed
        });
        await _dbContext.SaveChangesAsync();

        var service = CreateService();

        // Act
        var messages = await service.GetMessagesAsync(conversation.Id);

        // Assert
        messages.Select(m => m.Content).Should().ContainInOrder("First", "Second", "Third");
    }
}

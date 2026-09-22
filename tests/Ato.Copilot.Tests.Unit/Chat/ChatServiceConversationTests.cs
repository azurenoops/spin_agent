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
/// Unit tests for ChatService conversation management methods (US2):
/// CreateConversationAsync, GetConversationsAsync, GetConversationAsync,
/// SearchConversationsAsync, DeleteConversationAsync.
/// </summary>
public class ChatServiceConversationTests : IDisposable
{
    private readonly ChatDbContext _dbContext;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<ChatService>> _loggerMock;
    private readonly ChatService _service;
    private readonly LegacyChatTestScope _scope = new();

    public ChatServiceConversationTests()
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ChatDbContext(options);
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<ChatService>>();

        // Setup a default HttpClient (not used for conversation methods but required by constructor)
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Loose);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var client = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:3001") };
        _httpClientFactoryMock.Setup(f => f.CreateClient("McpServer")).Returns(client);

        _service = new ChatService(_dbContext, _httpClientFactoryMock.Object, _loggerMock.Object, Mock.Of<Ato.Copilot.Core.Interfaces.IPathSanitizationService>(), _scope.Resolver);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _scope.Dispose();
    }

    // ─── Positive Tests ──────────────────────────────────────────

    [Fact]
    public async Task CreateConversationAsync_WithTitle_ReturnsConversation()
    {
        // Arrange
        var request = new CreateConversationRequest
        {
            Title = "Test Conversation",
            UserId = "user-1"
        };

        // Act
        var result = await _service.CreateConversationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeNullOrEmpty();
        result.Title.Should().Be("Test Conversation");
        result.UserId.Should().Be(LegacyChatTestScope.ActorId);
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetConversationsAsync_ReturnsSortedByUpdatedAtDescending()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
        {
            _dbContext.Conversations.Add(new Conversation
            {
                Id = Guid.NewGuid().ToString(),
                Title = $"Conversation {i}",
                UserId = "user-1",
                OwnerKey = LegacyChatTestScope.DefaultOwnerKey,
                CreatedAt = baseTime.AddMinutes(-10 + i),
                UpdatedAt = baseTime.AddMinutes(-10 + i)
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetConversationsAsync("user-1");

        // Assert
        result.Should().HaveCount(5);
        result.Should().BeInDescendingOrder(c => c.UpdatedAt);
    }

    [Fact]
    public async Task GetConversationAsync_WithExistingId_ReturnsConversationWithMessages()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Test",
            UserId = "user-1",
            OwnerKey = LegacyChatTestScope.DefaultOwnerKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Conversations.Add(conversation);

        _dbContext.Messages.Add(new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversation.Id,
            Content = "Hello",
            Role = MessageRole.User,
            Timestamp = DateTime.UtcNow,
            Status = MessageStatus.Completed
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetConversationAsync(conversation.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(conversation.Id);
        result.Messages.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchConversationsAsync_MatchesTitleAndContent()
    {
        // Arrange
        var conv1 = new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Compliance Review",
            UserId = "user-1",
            OwnerKey = LegacyChatTestScope.DefaultOwnerKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var conv2 = new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            Title = "General Chat",
            UserId = "user-1",
            OwnerKey = LegacyChatTestScope.DefaultOwnerKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Conversations.AddRange(conv1, conv2);

        _dbContext.Messages.Add(new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conv2.Id,
            Content = "Let's discuss compliance issues",
            Role = MessageRole.User,
            Timestamp = DateTime.UtcNow,
            Status = MessageStatus.Completed
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.SearchConversationsAsync("compliance", "user-1");

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteConversationAsync_RemovesConversationAndMessages()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            Title = "To Delete",
            UserId = "user-1",
            OwnerKey = LegacyChatTestScope.DefaultOwnerKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Conversations.Add(conversation);

        _dbContext.Messages.Add(new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversation.Id,
            Content = "Some message",
            Role = MessageRole.User,
            Timestamp = DateTime.UtcNow,
            Status = MessageStatus.Completed
        });
        await _dbContext.SaveChangesAsync();

        // Act
        await _service.DeleteConversationAsync(conversation.Id);

        // Assert
        var deletedConv = await _dbContext.Conversations.FindAsync(conversation.Id);
        deletedConv.Should().BeNull();

        var remainingMessages = await _dbContext.Messages
            .Where(m => m.ConversationId == conversation.Id)
            .ToListAsync();
        remainingMessages.Should().BeEmpty();
    }

    // ─── Negative Tests ──────────────────────────────────────────

    [Fact]
    public async Task GetConversationAsync_WithNonExistentId_ReturnsNull()
    {
        // Act
        var result = await _service.GetConversationAsync("non-existent-id");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteConversationAsync_WithNonExistentId_DeniesAccess()
    {
        // Act
        var act = () => _service.DeleteConversationAsync("non-existent-id");

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*not found*");
    }

    // ─── Boundary Tests ──────────────────────────────────────────

    [Fact]
    public async Task CreateConversationAsync_WithEmptyTitle_DefaultsToNewConversation()
    {
        // Arrange
        var request = new CreateConversationRequest
        {
            Title = "",
            UserId = "user-1"
        };

        // Act
        var result = await _service.CreateConversationAsync(request);

        // Assert
        result.Title.Should().Be("New Conversation");
    }

    [Fact]
    public async Task CreateConversationAsync_WithNullUserId_UsesValidatedActor()
    {
        // Arrange
        var request = new CreateConversationRequest
        {
            Title = "Test"
        };

        // Act
        var result = await _service.CreateConversationAsync(request);

        // Assert
        result.UserId.Should().Be(LegacyChatTestScope.ActorId);
    }

    [Fact]
    public async Task GetConversationsAsync_WithPagination_ReturnsCorrectSubset()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            _dbContext.Conversations.Add(new Conversation
            {
                Id = Guid.NewGuid().ToString(),
                Title = $"Conversation {i}",
                UserId = "user-1",
                OwnerKey = LegacyChatTestScope.DefaultOwnerKey,
                CreatedAt = DateTime.UtcNow.AddMinutes(i),
                UpdatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetConversationsAsync("user-1", skip: 3, take: 4);

        // Assert
        result.Should().HaveCount(4);
    }

    [Fact]
    public async Task SearchConversationsAsync_ReturnsMax20Results()
    {
        // Arrange
        for (int i = 0; i < 25; i++)
        {
            _dbContext.Conversations.Add(new Conversation
            {
                Id = Guid.NewGuid().ToString(),
                Title = $"Compliance Item {i}",
                UserId = "user-1",
                OwnerKey = LegacyChatTestScope.DefaultOwnerKey,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.SearchConversationsAsync("Compliance", "user-1");

        // Assert
        result.Should().HaveCountLessOrEqualTo(20);
    }

    [Fact]
    public async Task GetConversationsAsync_ExcludesArchivedConversations()
    {
        // Arrange
        _dbContext.Conversations.Add(new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Active",
            UserId = "user-1",
            OwnerKey = LegacyChatTestScope.DefaultOwnerKey,
            IsArchived = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _dbContext.Conversations.Add(new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Archived",
            UserId = "user-1",
            OwnerKey = LegacyChatTestScope.DefaultOwnerKey,
            IsArchived = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetConversationsAsync("user-1");

        // Assert
        result.Should().HaveCount(1);
        result.First().Title.Should().Be("Active");
    }
}

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Channels.Abstractions;
using Ato.Copilot.Channels.Models;
using Ato.Copilot.Chat.Hubs;
using Ato.Copilot.Chat.Models;
using Ato.Copilot.Chat.Services;

namespace Ato.Copilot.Tests.Unit.Chat;

/// <summary>
/// Unit tests for ChatHub SignalR methods (US3):
/// JoinConversation, LeaveConversation, SendMessage, NotifyTyping.
/// Updated to verify delegation through IChannelManager and IChannel adapters.
/// </summary>
public class ChatHubTests
{
    private readonly Mock<IChannelManager> _channelManagerMock;
    private readonly Mock<IChannel> _channelMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<ILogger<ChatHub>> _loggerMock;
    private readonly Mock<IHubCallerClients> _clientsMock;
    private readonly Mock<IGroupManager> _groupsMock;
    private readonly Mock<HubCallerContext> _contextMock;
    private readonly Mock<IClientProxy> _clientProxyMock;

    public ChatHubTests()
    {
        _channelManagerMock = new Mock<IChannelManager>();
        _channelMock = new Mock<IChannel>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _loggerMock = new Mock<ILogger<ChatHub>>();
        _clientsMock = new Mock<IHubCallerClients>();
        _groupsMock = new Mock<IGroupManager>();
        _contextMock = new Mock<HubCallerContext>();
        _clientProxyMock = new Mock<IClientProxy>();

        _contextMock.Setup(c => c.ConnectionId).Returns("test-connection-id");
    }

    private ChatHub CreateHub()
    {
        var hub = new ChatHub(_channelManagerMock.Object, _channelMock.Object, _scopeFactoryMock.Object, _loggerMock.Object)
        {
            Clients = _clientsMock.Object,
            Groups = _groupsMock.Object,
            Context = _contextMock.Object
        };
        return hub;
    }

    // ─── Positive Tests ──────────────────────────────────────────

    [Fact]
    public async Task JoinConversation_DelegatesToChannelManager()
    {
        // Arrange
        var hub = CreateHub();
        var conversationId = "conv-123";

        // Act
        await hub.JoinConversation(conversationId);

        // Assert
        _channelManagerMock.Verify(
            m => m.JoinConversationAsync("test-connection-id", conversationId, default),
            Times.Once);
    }

    [Fact]
    public async Task LeaveConversation_DelegatesToChannelManager()
    {
        // Arrange
        var hub = CreateHub();
        var conversationId = "conv-123";

        // Act
        await hub.LeaveConversation(conversationId);

        // Assert
        _channelManagerMock.Verify(
            m => m.LeaveConversationAsync("test-connection-id", conversationId, default),
            Times.Once);
    }

    [Fact]
    public async Task SendMessage_SendsProcessingAndResponseViaChannel()
    {
        // Arrange
        var hub = CreateHub();
        var conversationId = "conv-456";

        // Setup scoped ChatService
        var chatServiceMock = new Mock<IChatService>();
        chatServiceMock.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<IProgress<string>?>()))
            .ReturnsAsync(new ChatResponse
            {
                MessageId = "msg-789",
                Content = "AI response",
                Success = true,
                Metadata = new Dictionary<string, object> { ["processingTimeMs"] = 150 }
            });

        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(p => p.GetService(typeof(IChatService))).Returns(chatServiceMock.Object);
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var request = new SendMessageRequest
        {
            ConversationId = conversationId,
            Message = "Hello AI"
        };

        // Act
        await hub.SendMessage(request);

        // Assert — processing + response sent via IChannel
        _channelMock.Verify(
            c => c.SendToConversationAsync(conversationId,
                It.Is<ChannelMessage>(m => m.Type == MessageType.AgentThinking),
                default),
            Times.Once);
        _channelMock.Verify(
            c => c.SendToConversationAsync(conversationId,
                It.Is<ChannelMessage>(m => m.Type == MessageType.AgentResponse),
                default),
            Times.Once);
    }

    [Fact]
    public async Task NotifyTyping_BroadcastsToOthersInGroup()
    {
        // Arrange
        var hub = CreateHub();
        var conversationId = "conv-123";
        // DEF-001: userId is now derived from authenticated claims server-side, not passed by client.

        _clientsMock.Setup(c => c.OthersInGroup(conversationId)).Returns(_clientProxyMock.Object);

        // Act
        await hub.NotifyTyping(conversationId);

        // Assert
        _clientProxyMock.Verify(
            p => p.SendCoreAsync("UserTyping", It.IsAny<object?[]>(), default),
            Times.Once);
    }

    // ─── Negative Tests ──────────────────────────────────────────

    [Fact]
    public async Task JoinConversation_WithNullConversationId_ThrowsHubException()
    {
        // Arrange
        var hub = CreateHub();

        // Act
        var act = () => hub.JoinConversation(null!);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*required*");
    }

    [Fact]
    public async Task JoinConversation_WithEmptyConversationId_ThrowsHubException()
    {
        // Arrange
        var hub = CreateHub();

        // Act
        var act = () => hub.JoinConversation("");

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*required*");
    }

    [Fact]
    public async Task SendMessage_WithEmptyConversationId_ThrowsHubException()
    {
        // Arrange
        var hub = CreateHub();
        var request = new SendMessageRequest
        {
            ConversationId = "",
            Message = "Test"
        };

        // Act
        var act = () => hub.SendMessage(request);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*required*");
    }

    [Fact]
    public async Task SendMessage_WithContentOver10000Chars_ThrowsHubException()
    {
        // Arrange
        var hub = CreateHub();
        var request = new SendMessageRequest
        {
            ConversationId = "conv-123",
            Message = new string('A', 10_001)
        };

        // Act
        var act = () => hub.SendMessage(request);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*10,000*");
    }

    // ─── Boundary Tests ──────────────────────────────────────────

    [Fact]
    public async Task SendMessage_WithExactly10000Chars_DoesNotThrow()
    {
        // Arrange
        var hub = CreateHub();
        var conversationId = "conv-boundary";

        var chatServiceMock = new Mock<IChatService>();
        chatServiceMock.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<IProgress<string>?>()))
            .ReturnsAsync(new ChatResponse
            {
                MessageId = "msg-boundary",
                Content = "Response",
                Success = true
            });

        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(p => p.GetService(typeof(IChatService))).Returns(chatServiceMock.Object);
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var request = new SendMessageRequest
        {
            ConversationId = conversationId,
            Message = new string('A', 10_000)
        };

        // Act
        var act = () => hub.SendMessage(request);

        // Assert — should not throw
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyTyping_WithEmptyConversationId_DoesNotBroadcast()
    {
        // Arrange
        var hub = CreateHub();

        // Act — DEF-001: userId param removed, only conversationId needed
        await hub.NotifyTyping("");

        // Assert — should not call OthersInGroup
        _clientsMock.Verify(c => c.OthersInGroup(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendMessage_WhenServiceFails_SendsErrorViaChannel()
    {
        // Arrange
        var hub = CreateHub();
        var conversationId = "conv-error";

        var chatServiceMock = new Mock<IChatService>();
        chatServiceMock.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<IProgress<string>?>()))
            .ThrowsAsync(new Exception("Unexpected error"));

        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(p => p.GetService(typeof(IChatService))).Returns(chatServiceMock.Object);
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var request = new SendMessageRequest
        {
            ConversationId = conversationId,
            Message = "Test"
        };

        // Act
        await hub.SendMessage(request);

        // Assert — error sent via IChannel
        _channelMock.Verify(
            c => c.SendToConversationAsync(conversationId,
                It.Is<ChannelMessage>(m => m.Type == MessageType.Error),
                default),
            Times.Once);
    }
}

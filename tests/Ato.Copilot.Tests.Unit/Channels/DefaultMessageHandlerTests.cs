using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Channels.Abstractions;
using Ato.Copilot.Channels.Configuration;
using Ato.Copilot.Channels.Implementations;
using Ato.Copilot.Channels.Models;

namespace Ato.Copilot.Tests.Unit.Channels;

public class DefaultMessageHandlerTests
{
    private readonly Mock<IChannelManager> _channelManagerMock;
    private readonly Mock<IConversationStateManager> _conversationStateMock;
    private readonly IOptions<ChannelOptions> _options;
    private readonly Mock<ILogger<DefaultMessageHandler>> _loggerMock;

    public DefaultMessageHandlerTests()
    {
        _channelManagerMock = new Mock<IChannelManager>();
        _conversationStateMock = new Mock<IConversationStateManager>();
        _loggerMock = new Mock<ILogger<DefaultMessageHandler>>();
        _options = Options.Create(new ChannelOptions());

        // Default: conversation state returns null (new conversation)
        _conversationStateMock
            .Setup(x => x.GetConversationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationState?)null);
        _conversationStateMock
            .Setup(x => x.SaveConversationAsync(It.IsAny<ConversationState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private DefaultMessageHandler CreateHandler(
        Func<IncomingMessage, CancellationToken, Task<ChannelMessage>>? agentInvoker = null,
        DefaultHandlerBehavior behavior = DefaultHandlerBehavior.Echo)
    {
        var opts = Options.Create(new ChannelOptions { DefaultHandlerBehavior = behavior });
        return new DefaultMessageHandler(
            _channelManagerMock.Object,
            _conversationStateMock.Object,
            opts,
            _loggerMock.Object,
            new NullTenantScopeBinder(),
            agentInvoker);
    }

    private IncomingMessage CreateMessage(string content = "test message", string conversationId = "conv-1", string connectionId = "conn-1")
    {
        return new IncomingMessage
        {
            ConnectionId = connectionId,
            ConversationId = conversationId,
            Content = content,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    #region With AgentInvoker

    [Fact]
    public async Task HandleMessageAsync_WithInvoker_StoresUserMessageAndReturnsResponse()
    {
        // Arrange
        var agentResponse = new ChannelMessage
        {
            ConversationId = "conv-1",
            Type = MessageType.AgentResponse,
            Content = "Agent reply",
            AgentType = "ComplianceAgent",
            IsComplete = true
        };

        var handler = CreateHandler(agentInvoker: (msg, ct) => Task.FromResult(agentResponse));
        var incoming = CreateMessage("What is AC-2?");

        // Act
        var result = await handler.HandleMessageAsync(incoming);

        // Assert
        result.Type.Should().Be(MessageType.AgentResponse);
        result.Content.Should().Be("Agent reply");

        // Verify user message was stored
        _conversationStateMock.Verify(
            x => x.SaveConversationAsync(
                It.Is<ConversationState>(s => s.Messages.Any(m => m.Role == "user" && m.Content == "What is AC-2?")),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task HandleMessageAsync_WithInvoker_SendsAgentThinkingNotification()
    {
        // Arrange
        var handler = CreateHandler(agentInvoker: (msg, ct) =>
            Task.FromResult(new ChannelMessage { ConversationId = msg.ConversationId, Type = MessageType.AgentResponse, Content = "response", IsComplete = true }));

        var incoming = CreateMessage();

        // Act
        await handler.HandleMessageAsync(incoming);

        // Assert — AgentThinking notification sent to conversation
        _channelManagerMock.Verify(
            x => x.SendToConversationAsync(
                "conv-1",
                It.Is<ChannelMessage>(m => m.Type == MessageType.AgentThinking),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_WithInvoker_StoresAssistantResponse()
    {
        // Arrange
        var existingState = new ConversationState { Id = "conv-1" };
        _conversationStateMock
            .Setup(x => x.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);

        var handler = CreateHandler(agentInvoker: (msg, ct) =>
            Task.FromResult(new ChannelMessage { ConversationId = "conv-1", Type = MessageType.AgentResponse, Content = "reply", IsComplete = true }));

        // Act
        await handler.HandleMessageAsync(CreateMessage());

        // Assert — assistant message stored
        _conversationStateMock.Verify(
            x => x.SaveConversationAsync(
                It.Is<ConversationState>(s => s.Messages.Any(m => m.Role == "assistant" && m.Content == "reply")),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Echo Behavior

    [Fact]
    public async Task HandleMessageAsync_EchoMode_ReturnsUserContentAsAgentResponse()
    {
        // Arrange
        var handler = CreateHandler(behavior: DefaultHandlerBehavior.Echo);
        var incoming = CreateMessage("Echo this");

        // Act
        var result = await handler.HandleMessageAsync(incoming);

        // Assert
        result.Type.Should().Be(MessageType.AgentResponse);
        result.Content.Should().Be("Echo this");
        result.IsComplete.Should().BeTrue();
    }

    #endregion

    #region Error Behavior

    [Fact]
    public async Task HandleMessageAsync_ErrorMode_ReturnsErrorType()
    {
        // Arrange
        var handler = CreateHandler(behavior: DefaultHandlerBehavior.Error);
        var incoming = CreateMessage();

        // Act
        var result = await handler.HandleMessageAsync(incoming);

        // Assert
        result.Type.Should().Be(MessageType.Error);
        result.Content.Should().Contain("No agent invoker configured");
        result.IsComplete.Should().BeTrue();
    }

    #endregion

    #region Exception Handling

    [Fact]
    public async Task HandleMessageAsync_ExceptionAlwaysReturnsError_RegardlessOfSetting()
    {
        // Arrange — Echo mode, but invoker throws
        var handler = CreateHandler(
            behavior: DefaultHandlerBehavior.Echo,
            agentInvoker: (msg, ct) => throw new InvalidOperationException("Agent failed"));

        var incoming = CreateMessage();

        // Act
        var result = await handler.HandleMessageAsync(incoming);

        // Assert (FR-012)
        result.Type.Should().Be(MessageType.Error);
        result.Content.Should().Contain("Agent failed");
        result.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task HandleMessageAsync_ExceptionDuringStateSave_ReturnsError()
    {
        // Arrange
        _conversationStateMock
            .Setup(x => x.SaveConversationAsync(It.IsAny<ConversationState>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB down"));

        var handler = CreateHandler();

        // Act
        var result = await handler.HandleMessageAsync(CreateMessage());

        // Assert
        result.Type.Should().Be(MessageType.Error);
        result.Content.Should().Contain("DB down");
    }

    #endregion

    #region ConversationState Integration

    [Fact]
    public async Task HandleMessageAsync_SaveConversationAsync_CalledForUserMessage()
    {
        // Arrange
        var handler = CreateHandler(behavior: DefaultHandlerBehavior.Echo);

        // Act
        await handler.HandleMessageAsync(CreateMessage("Hello"));

        // Assert
        _conversationStateMock.Verify(
            x => x.SaveConversationAsync(It.IsAny<ConversationState>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_ExistingConversation_AppendsToExistingMessages()
    {
        // Arrange
        var existingState = new ConversationState
        {
            Id = "conv-1",
            Messages = new List<ConversationMessage>
            {
                new() { Role = "user", Content = "previous" }
            }
        };
        _conversationStateMock
            .Setup(x => x.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);

        var handler = CreateHandler(behavior: DefaultHandlerBehavior.Echo);

        // Act
        await handler.HandleMessageAsync(CreateMessage("new message"));

        // Assert
        _conversationStateMock.Verify(
            x => x.SaveConversationAsync(
                It.Is<ConversationState>(s => s.Messages.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}

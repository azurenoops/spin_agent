using Ato.Copilot.Chat.Hubs;
using Ato.Copilot.Chat.Services.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Chat;

public class LegacyChatHubFilterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Filter_BindsActualHandshakeContextAndRestoresPrevious(bool deny)
    {
        // Arrange
        var previous = new DefaultHttpContext();
        using var scope = new LegacyChatTestScope();
        var actual = scope.Http;
        scope.Accessor.HttpContext = previous;
        var filter = new ChatWorkspaceHubFilter();
        var features = new FeatureCollection();
        features.Set<IHttpContextFeature>(new HttpFeature { HttpContext = actual });
        var caller = new Mock<HubCallerContext>();
        caller.Setup(c => c.Features).Returns(features);
        var invocation = new HubInvocationContext(caller.Object, Mock.Of<IServiceProvider>(),
            Mock.Of<Hub>(), typeof(ChatHub).GetMethod(nameof(ChatHub.JoinConversation))!, ["conversation"]);
        var invoked = false;

        // Act
        var action = async () => await filter.InvokeMethodAsync(invocation, async _ =>
        {
            invoked = true;
            (await scope.Resolver.ResolveAsync()).ActorId.Should().Be(LegacyChatTestScope.ActorId);
            if (deny) throw new ChatWorkspaceException(403, "WORKSPACE_DENIED", "Denied");
            return "allowed";
        });

        // Assert
        if (deny) await action.Should().ThrowAsync<HubException>().WithMessage("WORKSPACE_DENIED:*");
        else (await action()).Should().Be("allowed");
        invoked.Should().BeTrue();
        scope.Accessor.HttpContext.Should().BeSameAs(previous);
        var outside = () => scope.Resolver.ResolveAsync();
        await outside.Should().ThrowAsync<ChatWorkspaceException>();
    }

    [Fact]
    public async Task Filter_WithoutHandshakeContext_DoesNotInvokeHub()
    {
        // Arrange
        var filter = new ChatWorkspaceHubFilter();
        var caller = new Mock<HubCallerContext>();
        caller.Setup(c => c.Features).Returns(new FeatureCollection());
        var invocation = new HubInvocationContext(caller.Object, Mock.Of<IServiceProvider>(),
            Mock.Of<Hub>(), typeof(ChatHub).GetMethod(nameof(ChatHub.JoinConversation))!, ["conversation"]);
        var invoked = false;

        // Act
        var action = async () => await filter.InvokeMethodAsync(invocation, _ =>
        {
            invoked = true;
            return ValueTask.FromResult<object?>(null);
        });

        // Assert
        await action.Should().ThrowAsync<HubException>();
        invoked.Should().BeFalse();
    }

    private sealed class HttpFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }
}

using Ato.Copilot.State.Abstractions;
using Ato.Copilot.State.Implementations;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

/// <summary>#1002: state ownership is server-derived, not the client's conversation or user ID.</summary>
public class WorkspaceConversationStateTests
{
    [Theory]
    [InlineData("organization")]
    [InlineData("directory")]
    [InlineData("subject")]
    [InlineData("mode")]
    [InlineData("system")]
    public async Task LookupAndSave_AreBoundToConversationOwner(string change)
    {
        // Arrange
        var identity = new ConversationIdentity(Guid.NewGuid(), Guid.NewGuid(), "organization",
            Guid.NewGuid(), "ordinary", Guid.NewGuid(), "system-a");
        var accessor = new IdentityAccessor { Current = identity };
        var manager = new InMemoryConversationStateManager(accessor);
        var state = new ConversationState { Id = "client-chosen", UserId = "forged" };
        state.Messages.Add(new() { Role = "user", Content = "private history" });
        state.Variables["threadId"] = "private-thread";
        await manager.SaveConversationAsync(state);

        // Act
        var owned = await manager.GetConversationAsync("client-chosen");
        accessor.Current = change switch
        {
            "organization" => identity with { TenantId = Guid.NewGuid() },
            "directory" => identity with { DirectoryId = Guid.NewGuid() },
            "subject" => identity with { ObjectId = Guid.NewGuid() },
            "mode" => identity with { Mode = "support" },
            _ => identity with { SystemId = "system-b" }
        };
        var other = await manager.GetConversationAsync("client-chosen");
        var forgedSave = () => manager.SaveConversationAsync(owned!);

        // Assert
        owned!.UserId.Should().Be(identity.ActorId);
        other.Should().BeNull();
        await forgedSave.Should().ThrowAsync<UnauthorizedAccessException>();
        accessor.Current = identity;
        (await manager.GetConversationAsync("client-chosen"))!.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task CancelledSave_DoesNotMutateStoredHistory()
    {
        // Arrange
        var accessor = new IdentityAccessor
        {
            Current = new(Guid.NewGuid(), Guid.NewGuid(), "organization",
                Guid.NewGuid(), "ordinary", Guid.NewGuid())
        };
        var manager = new InMemoryConversationStateManager(accessor);
        await manager.SaveConversationAsync(new() { Id = "existing" });
        var state = (await manager.GetConversationAsync("existing"))!;
        state.Messages.Add(new() { Content = "not saved" });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var save = () => manager.SaveConversationAsync(state, cancellation.Token);

        // Assert
        await save.Should().ThrowAsync<OperationCanceledException>();
        (await manager.GetConversationAsync("existing"))!.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ClientGeneratedIdAndServerGeneratedId_SupportAuthorizedContinuation()
    {
        // Arrange
        var accessor = new IdentityAccessor
        {
            Current = new(Guid.NewGuid(), Guid.NewGuid(), "organization",
                Guid.NewGuid(), "ordinary", Guid.NewGuid())
        };
        var manager = new InMemoryConversationStateManager(accessor);

        // Act
        var id = await manager.CreateConversationAsync();
        var state = (await manager.GetConversationAsync(id))!;
        state.Messages.Add(new() { Content = "authorized" });
        await manager.SaveConversationAsync(state);
        await manager.SaveConversationAsync(new() { Id = "client-generated" });

        // Assert
        (await manager.GetConversationAsync(id))!.Messages.Should().ContainSingle();
        (await manager.GetConversationAsync("client-generated")).Should().NotBeNull();
    }

    private sealed class IdentityAccessor : IConversationIdentityAccessor
    {
        public ConversationIdentity? Current { get; set; }
    }
}

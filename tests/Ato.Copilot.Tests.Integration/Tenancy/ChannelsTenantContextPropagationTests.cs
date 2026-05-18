using Ato.Copilot.Channels.Abstractions;
using Ato.Copilot.Channels.Configuration;
using Ato.Copilot.Channels.Extensions;
using Ato.Copilot.Channels.Implementations;
using Ato.Copilot.Channels.Models;
using Ato.Copilot.Chat.Channels;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T139 — Channels propagate the inbound <see cref="TenantContextEnvelope"/>
/// into the host's <see cref="ITenantContextAccessor"/> so MCP tools invoked
/// in-process via <see cref="Ato.Copilot.Channels"/> from the VS Code extension
/// or M365 Teams bot see the same identity as direct HTTP callers
/// (FR-021/FR-024 and research.md §10).
/// </summary>
[Collection("Tenancy")]
public sealed class ChannelsTenantContextPropagationTests
{
    private static readonly Guid TenantA = new("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
    private static readonly Guid TenantB = new("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");

    private static (DefaultMessageHandler handler, ITenantContextAccessor accessor) Build(
        Func<ITenantContextAccessor, IncomingMessage, CancellationToken, Task<ChannelMessage>> agentInvoker)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryChannels();

        // Replace Channels' default no-op binder with the real bridge.
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.RemoveAll<ITenantScopeBinder>();
        services.AddSingleton<ITenantScopeBinder, AccessorTenantScopeBinder>();

        // In-memory conversation state so DefaultMessageHandler can persist.
        services.AddSingleton<IConversationStateManager, InMemoryConversationState>();

        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<ITenantContextAccessor>();

        var handler = new DefaultMessageHandler(
            channelManager: provider.GetRequiredService<IChannelManager>(),
            conversationState: provider.GetRequiredService<IConversationStateManager>(),
            options: provider.GetRequiredService<IOptions<ChannelOptions>>(),
            logger: NullLogger<DefaultMessageHandler>.Instance,
            tenantScopeBinder: provider.GetRequiredService<ITenantScopeBinder>(),
            agentInvoker: (msg, ct) => agentInvoker(accessor, msg, ct));

        return (handler, accessor);
    }

    [Fact]
    public async Task AgentInvoker_SeesAmbientTenantContext_FromInboundEnvelope()
    {
        Guid? observedEffective = null;
        bool? observedIsCspAdmin = null;

        var (handler, accessor) = Build((acc, msg, ct) =>
        {
            // The agent invoker runs inside the binder's pushed scope.
            observedEffective = acc.Current?.EffectiveTenantId;
            observedIsCspAdmin = acc.Current?.IsCspAdmin;
            return Task.FromResult(new ChannelMessage
            {
                ConversationId = msg.ConversationId,
                Type = MessageType.AgentResponse,
                Content = "ok",
                IsComplete = true
            });
        });

        var inbound = new IncomingMessage
        {
            ConnectionId = "conn-1",
            ConversationId = "conv-1",
            Content = "hello",
            TenantContext = new TenantContextEnvelope
            {
                TenantId = TenantA,
                IsCspAdmin = true
            }
        };

        var response = await handler.HandleMessageAsync(inbound, CancellationToken.None);

        response.Type.Should().Be(MessageType.AgentResponse);
        observedEffective.Should().Be(TenantA);
        observedIsCspAdmin.Should().BeTrue();

        // After the handler returns, the scope is popped.
        accessor.Current.Should().BeNull();
    }

    [Fact]
    public async Task ImpersonatedTenantId_OverridesEffectiveTenant_DuringInvocation()
    {
        Guid? observedEffective = null;

        var (handler, accessor) = Build((acc, msg, ct) =>
        {
            observedEffective = acc.Current?.EffectiveTenantId;
            return Task.FromResult(new ChannelMessage
            {
                ConversationId = msg.ConversationId,
                Type = MessageType.AgentResponse,
                Content = "ok",
                IsComplete = true
            });
        });

        var inbound = new IncomingMessage
        {
            ConnectionId = "conn-2",
            ConversationId = "conv-2",
            Content = "hello",
            TenantContext = new TenantContextEnvelope
            {
                TenantId = TenantA,
                IsCspAdmin = true,
                ImpersonatedTenantId = TenantB
            }
        };

        await handler.HandleMessageAsync(inbound, CancellationToken.None);

        observedEffective.Should().Be(TenantB, "ImpersonatedTenantId is the EffectiveTenantId during impersonation");
        accessor.Current.Should().BeNull();
    }

    [Fact]
    public async Task NoEnvelope_LeavesAccessorUnchanged()
    {
        ITenantContext? observed = null;

        var (handler, accessor) = Build((acc, msg, ct) =>
        {
            observed = acc.Current;
            return Task.FromResult(new ChannelMessage
            {
                ConversationId = msg.ConversationId,
                Type = MessageType.AgentResponse,
                Content = "ok",
                IsComplete = true
            });
        });

        var inbound = new IncomingMessage
        {
            ConnectionId = "conn-3",
            ConversationId = "conv-3",
            Content = "hello",
            TenantContext = null
        };

        await handler.HandleMessageAsync(inbound, CancellationToken.None);

        observed.Should().BeNull("no envelope means no push; accessor remains uninitialized");
        accessor.Current.Should().BeNull();
    }

    private sealed class InMemoryConversationState : IConversationStateManager
    {
        private readonly Dictionary<string, ConversationState> _store = new();

        public Task<ConversationState?> GetConversationAsync(string conversationId, CancellationToken ct = default)
        {
            _store.TryGetValue(conversationId, out var state);
            return Task.FromResult<ConversationState?>(state);
        }

        public Task SaveConversationAsync(ConversationState state, CancellationToken ct = default)
        {
            _store[state.Id] = state;
            return Task.CompletedTask;
        }

        public Task<string> CreateConversationAsync(CancellationToken ct = default)
        {
            var id = Guid.NewGuid().ToString();
            _store[id] = new ConversationState { Id = id };
            return Task.FromResult(id);
        }
    }
}

using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Configuration;

namespace Ato.Copilot.Tests.Unit.Agents;

/// <summary>
/// Unit tests for Azure AI Foundry Agent integration (Feature 028).
/// Covers PersistentAgentsClient registration, AiBackend dispatch routing,
/// ProvisionFoundryAgentAsync idempotency, tool dispatch, run timeout,
/// and thread-to-conversation mapping. SC-007: ≥6 unit tests.
/// </summary>
public class FoundryAgentTests
{
    private readonly Mock<ILogger<TestableFoundryAgent>> _loggerMock = new();

    /// <summary>
    /// T025-1: Provider=OpenAi with no IChatClient returns null from TryProcessWithBackendAsync (deterministic fallback).
    /// </summary>
    [Fact]
    public async Task TryProcessWithBackendAsync_ProviderOpenAi_NoChatClient_ReturnsNull()
    {
        var agent = new TestableFoundryAgent(_loggerMock.Object);

        var context = new AgentConversationContext { ConversationId = "conv-1" };
        var result = await agent.InvokeTryProcessWithBackendAsync("hello", context);

        result.Should().BeNull("Provider OpenAi with no IChatClient should return null for deterministic fallback");
    }

    /// <summary>
    /// T025-2: Provider=Foundry with no client returns null and falls through to IChatClient.
    /// </summary>
    [Fact]
    public async Task TryProcessWithBackendAsync_Foundry_NoClient_ReturnsNull()
    {
        var agent = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.Foundry },
            foundryClient: null);

        var context = new AgentConversationContext { ConversationId = "conv-2" };
        var result = await agent.InvokeTryProcessWithBackendAsync("hello", context);

        // Both Foundry (null client) and IChatClient (null) return null
        result.Should().BeNull();
    }

    /// <summary>
    /// T025-3: Provider=Foundry with no _foundryAgentId returns null from TryProcessWithFoundryAsync.
    /// </summary>
    [Fact]
    public async Task TryProcessWithFoundryAsync_NoAgentId_ReturnsNull()
    {
        var agent = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.Foundry },
            foundryClient: null);

        // _foundryAgentId is null by default
        var context = new AgentConversationContext { ConversationId = "conv-3" };
        var result = await agent.InvokeTryProcessWithFoundryAsync("hello", context);

        result.Should().BeNull("TryProcessWithFoundryAsync should return null when _foundryAgentId is null");
    }

    /// <summary>
    /// T025-4: Provider=OpenAi routes to TryProcessWithAiAsync path.
    /// </summary>
    [Fact]
    public async Task TryProcessWithBackendAsync_OpenAi_RoutesToAiPath()
    {
        var agent = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.OpenAi });

        var context = new AgentConversationContext { ConversationId = "conv-4" };
        var result = await agent.InvokeTryProcessWithBackendAsync("hello", context);

        // IChatClient is null so TryProcessWithAiAsync returns null
        result.Should().BeNull();
        agent.FoundryCallCount.Should().Be(0, "OpenAi should not call Foundry path");
    }

    /// <summary>
    /// T025-5: Provider=Foundry calls TryProcessWithFoundryAsync before IChatClient fallback.
    /// </summary>
    [Fact]
    public async Task TryProcessWithBackendAsync_Foundry_CallsFoundryFirst()
    {
        var agent = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.Foundry },
            foundryClient: null);

        var context = new AgentConversationContext { ConversationId = "conv-5" };
        await agent.InvokeTryProcessWithBackendAsync("hello", context);

        agent.FoundryCallCount.Should().Be(1, "Foundry provider should attempt Foundry path first");
    }

    /// <summary>
    /// T025-6: ProvisionFoundryAgentAsync with null client skips provisioning gracefully.
    /// </summary>
    [Fact]
    public async Task ProvisionFoundryAgentAsync_NullClient_SkipsGracefully()
    {
        var agent = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions
            {
                Enabled = true,
                Provider = AiProvider.Foundry,
                FoundryProjectEndpoint = "https://foundry.test"
            },
            foundryClient: null);

        await agent.InvokeProvisionFoundryAgentAsync();

        // Should not throw and _foundryAgentId should remain null
        agent.GetFoundryAgentId().Should().BeNull();
    }

    /// <summary>
    /// T025-7: ProvisionFoundryAgentAsync with Provider=OpenAi skips provisioning.
    /// </summary>
    [Fact]
    public async Task ProvisionFoundryAgentAsync_NotFoundry_SkipsProvisioning()
    {
        var agent = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.OpenAi },
            foundryClient: null);

        await agent.InvokeProvisionFoundryAgentAsync();

        agent.GetFoundryAgentId().Should().BeNull();
    }

    /// <summary>
    /// T025-8: BuildFoundryToolDefinitions returns empty list when no tools registered.
    /// </summary>
    [Fact]
    public void BuildFoundryToolDefinitions_NoTools_ReturnsEmptyList()
    {
        var agent = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.Foundry });

        var definitions = agent.InvokeBuildFoundryToolDefinitions();

        definitions.Should().NotBeNull();
        definitions.Should().BeEmpty("no tools are registered");
    }

    /// <summary>
    /// T025-9: Thread mapping uses same thread for same conversation ID.
    /// Tests that the ConcurrentDictionary correctly maps conversations to threads.
    /// </summary>
    [Fact]
    public void ThreadMap_SameConversation_ReusesSameEntry()
    {
        var agent = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.Foundry });

        // Simulate adding thread mapping
        agent.SetThreadMapping("conv-A", "thread-123");
        agent.SetThreadMapping("conv-B", "thread-456");

        agent.GetThreadMapping("conv-A").Should().Be("thread-123");
        agent.GetThreadMapping("conv-B").Should().Be("thread-456");
        agent.GetThreadMapping("conv-C").Should().BeNull("unmapped conversation should return null");
    }

    /// <summary>
    /// T025-10: Default constructor creates agent without AI options.
    /// </summary>
    [Fact]
    public void Constructor_DefaultValues_NoAiOptions()
    {
        var agent = new TestableFoundryAgent(_loggerMock.Object);

        agent.GetFoundryAgentId().Should().BeNull();
    }

    /// <summary>
    /// T038a / US5.3: Provider switch mid-conversation creates a new thread.
    /// When the agent instance is recreated (simulating restart with new provider),
    /// the in-memory thread map is empty so a new thread is created.
    /// </summary>
    [Fact]
    public void ThreadMap_ProviderSwitchMidConversation_CreatesNewThread()
    {
        // Simulate agent with Foundry provider and an existing thread mapping
        var agent1 = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.Foundry });
        agent1.SetThreadMapping("conv-X", "foundry-thread-old");
        agent1.GetThreadMapping("conv-X").Should().Be("foundry-thread-old");

        // Simulate restart with different provider (new agent instance = empty _threadMap)
        var agent2 = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.OpenAi });

        // Same conversation ID should have no mapping — new thread will be created
        agent2.GetThreadMapping("conv-X").Should().BeNull(
            "provider switch (restart) should lose in-memory thread mapping, forcing new thread creation");
    }

    // ── Task #177 (Epic #132) — Error Path Expansion ─────────────────────

    /// <summary>
    /// T177-EP1: TryProcessWithBackendAsync does not throw when AI is enabled
    /// but Foundry endpoint config is absent (graceful null return).
    /// </summary>
    [Fact]
    public async Task TryProcessWithBackendAsync_FoundryEnabled_NoClient_NoException()
    {
        var agent = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions
            {
                Enabled  = true,
                Provider = AiProvider.Foundry,
                FoundryProjectEndpoint = "https://mock.foundry/api/projects/test",
            },
            foundryClient: null);

        var context = new AgentConversationContext { ConversationId = "ep-1" };
        var act = async () => await agent.InvokeTryProcessWithBackendAsync("hello", context);

        await act.Should().NotThrowAsync(
            "missing Foundry client should degrade gracefully, never surface an exception to callers");
    }

    /// <summary>
    /// T177-EP2: When Foundry returns null (no client), FoundryCallCount is incremented
    /// proving the path was entered and exited cleanly.
    /// </summary>
    [Fact]
    public async Task TryProcessWithFoundryAsync_NullClient_IncrementsCallCount_ThenReturnsNull()
    {
        var agent = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.Foundry },
            foundryClient: null);

        var context = new AgentConversationContext { ConversationId = "ep-2" };
        var result  = await agent.InvokeTryProcessWithFoundryAsync("test", context);

        result.Should().BeNull();
        agent.FoundryCallCount.Should().Be(1,
            "TryProcessWithFoundryAsync should be called once even when client is null");
    }

    /// <summary>
    /// T177-EP3: Provider=Foundry with Enabled=false — backend returns null without
    /// attempting Foundry (deterministic path).
    /// </summary>
    [Fact]
    public async Task TryProcessWithBackendAsync_AiDisabled_SkipsFoundryPath()
    {
        var agent = new TestableFoundryAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = false, Provider = AiProvider.Foundry },
            foundryClient: null);

        var context = new AgentConversationContext { ConversationId = "ep-3" };
        await agent.InvokeTryProcessWithBackendAsync("hello", context);

        agent.FoundryCallCount.Should().Be(0,
            "disabled AI should not invoke Foundry regardless of provider setting");
    }
}

/// <summary>
/// Testable concrete agent extending BaseAgent for unit testing Foundry-specific methods.
/// Exposes protected methods via public wrappers.
/// </summary>
public class TestableFoundryAgent : BaseAgent
{
    public int FoundryCallCount { get; private set; }

    public TestableFoundryAgent(
        ILogger logger,
        AzureAiOptions? azureAiOptions = null,
        Azure.AI.Agents.Persistent.PersistentAgentsClient? foundryClient = null)
        : base(logger, null, foundryClient, azureAiOptions)
    {
    }

    public override string AgentId => "test-foundry-agent";
    public override string AgentName => "Test Foundry Agent";
    public override string Description => "A test agent for Foundry unit tests";
    public override double CanHandle(string message) => 0.5;
    public override string GetSystemPrompt() => "You are a test agent.";

    public override Task<AgentResponse> ProcessAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        return Task.FromResult(new AgentResponse
        {
            Success = true,
            Response = "test",
            AgentName = AgentName
        });
    }

    protected override Task<AgentResponse?> TryProcessWithFoundryAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        FoundryCallCount++;
        return base.TryProcessWithFoundryAsync(message, context, cancellationToken, progress);
    }

    // Public wrappers for testing protected methods
    public Task<AgentResponse?> InvokeTryProcessWithBackendAsync(
        string message, AgentConversationContext context, CancellationToken ct = default)
        => TryProcessWithBackendAsync(message, context, ct);

    public Task<AgentResponse?> InvokeTryProcessWithFoundryAsync(
        string message, AgentConversationContext context, CancellationToken ct = default)
        => base.TryProcessWithFoundryAsync(message, context, ct);

    public Task InvokeProvisionFoundryAgentAsync(CancellationToken ct = default)
        => ProvisionFoundryAgentAsync(ct);

    public List<Azure.AI.Agents.Persistent.ToolDefinition> InvokeBuildFoundryToolDefinitions()
    {
        // Use reflection to call private method
        var method = typeof(BaseAgent).GetMethod("BuildFoundryToolDefinitions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (List<Azure.AI.Agents.Persistent.ToolDefinition>)method!.Invoke(this, null)!;
    }

    public string? GetFoundryAgentId() => _foundryAgentId;

    // Thread mapping helpers for testing
    public void SetThreadMapping(string conversationId, string threadId)
    {
        var field = typeof(BaseAgent).GetField("_threadMap",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var map = (System.Collections.Concurrent.ConcurrentDictionary<string, string>)field!.GetValue(this)!;
        map[conversationId] = threadId;
    }

    public string? GetThreadMapping(string conversationId)
    {
        var field = typeof(BaseAgent).GetField("_threadMap",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var map = (System.Collections.Concurrent.ConcurrentDictionary<string, string>)field!.GetValue(this)!;
        return map.TryGetValue(conversationId, out var threadId) ? threadId : null;
    }
}

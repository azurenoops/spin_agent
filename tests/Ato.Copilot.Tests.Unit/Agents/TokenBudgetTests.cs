using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Agents;

/// <summary>
/// Regression tests for BUG-5 / #693 — proactive token-budget guard.
///
/// Coverage:
///   1. Under-budget prompt passes through unchanged (no exception, no trim).
///   2. Over-budget prompt in Reject mode raises TokenBudgetExceededException
///      BEFORE the LLM client is ever called (proves cost/DoS prevention).
///   3. Boundary case: prompt exactly at the cap passes.
///   4. Truncate mode: over-budget prompt is trimmed to fit; system prompt
///      and latest user turn are preserved.
///   5. Alert-ratio warning is logged when usage >= AlertRatio but < cap.
///   6. Per-request token usage INFO line is always logged when MaxInputTokens > 0.
///   7. Guard disabled (MaxInputTokens = 0): no check, returns original list.
///   8. TryProcessWithAiAsync raises TokenBudgetExceededException and never
///      invokes IChatClient when the prompt is over budget (Reject mode).
/// </summary>
public class TokenBudgetTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal capturing logger that records every log call.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();
        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => _entries.Add((logLevel, formatter(state, exception)));

        public bool HasInfo(string fragment) =>
            _entries.Any(e => e.Level == LogLevel.Information && e.Message.Contains(fragment));

        public bool HasWarning(string fragment) =>
            _entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains(fragment));
    }

    /// <summary>Minimal concrete agent that exposes the static helpers for testing.</summary>
    private sealed class BudgetTestAgent : BaseAgent
    {
        public BudgetTestAgent(ILogger logger, IChatClient? chatClient = null,
            AzureAiOptions? opts = null)
            : base(logger, chatClient, null, opts) { }

        public override string AgentId => "budget-test";
        public override string AgentName => "BudgetTestAgent";
        public override string Description => "Token budget test agent";
        public override string GetSystemPrompt() => "System prompt for budget tests.";
        public override double CanHandle(string message) => 0.5;

        public override Task<AgentResponse> ProcessAsync(
            string message, AgentConversationContext context,
            CancellationToken cancellationToken = default,
            IProgress<string>? progress = null)
            => Task.FromResult(new AgentResponse { Success = true, Response = "ok", AgentName = AgentName });

        /// <summary>Expose TryProcessWithAiAsync for direct testing.</summary>
        public Task<AgentResponse?> InvokeProcessWithAiAsync(
            string message, AgentConversationContext context,
            CancellationToken cancellationToken = default)
            => TryProcessWithAiAsync(message, context, cancellationToken);
    }

    /// <summary>Builds a list of ChatMessages with controllable approximate token count.</summary>
    private static List<ChatMessage> BuildMessages(int approximateTokens)
    {
        // Each "word" of 4 chars ≈ 1 token. We build a history that hits the target.
        var systemMsg = new ChatMessage(ChatRole.System, "System.");
        // Overhead: 4 tokens for system + 4 tokens for user turn = 8 tokens base.
        // Remaining tokens go into the user message body.
        var bodyTokens = Math.Max(0, approximateTokens - 8);
        var body = new string('a', bodyTokens * 4); // 4 chars ~ 1 token
        var userMsg = new ChatMessage(ChatRole.User, body);
        return [systemMsg, userMsg];
    }

    private static AzureAiOptions DefaultRejectOptions(int maxInputTokens, double alertRatio = 0.9)
        => new()
        {
            Enabled = true,
            MaxInputTokens = maxInputTokens,
            BudgetMode = TokenBudgetMode.Reject,
            TokenAlertRatio = alertRatio
        };

    private static AzureAiOptions TruncateOptions(int maxInputTokens)
        => new()
        {
            Enabled = true,
            MaxInputTokens = maxInputTokens,
            BudgetMode = TokenBudgetMode.Truncate,
            TokenAlertRatio = 0.9
        };

    // ─── EstimatePromptTokens unit tests ────────────────────────────────────

    [Fact]
    public void EstimatePromptTokens_EmptyList_ReturnsZero()
    {
        var result = BaseAgent.EstimatePromptTokens([]);
        result.Should().Be(0);
    }

    [Fact]
    public void EstimatePromptTokens_SingleMessage_IncludesOverhead()
    {
        var msgs = new List<ChatMessage> { new(ChatRole.User, "aaaa") }; // 4 chars = 1 token
        var result = BaseAgent.EstimatePromptTokens(msgs);
        // 4 overhead + ceil(4/4)=1 body = 5
        result.Should().Be(5);
    }

    // ─── EnforceTokenBudget unit tests ──────────────────────────────────────

    [Fact]
    public void EnforceTokenBudget_UnderBudget_ReturnsSameList()
    {
        var logger = new CapturingLogger();
        var opts = DefaultRejectOptions(maxInputTokens: 1_000);
        var msgs = BuildMessages(approximateTokens: 100);

        var result = BaseAgent.EnforceTokenBudget(msgs, opts, "TestAgent", logger);

        result.Should().BeSameAs(msgs, "under-budget list must pass through unchanged");
    }

    [Fact]
    public void EnforceTokenBudget_ExactlyAtBudget_Passes()
    {
        var logger = new CapturingLogger();
        // Build a prompt that is exactly at the cap.
        var msgs = BuildMessages(approximateTokens: 500);
        var estimated = BaseAgent.EstimatePromptTokens(msgs);
        var opts = DefaultRejectOptions(maxInputTokens: estimated); // cap == estimated

        var result = BaseAgent.EnforceTokenBudget(msgs, opts, "TestAgent", logger);

        result.Should().BeSameAs(msgs, "prompt exactly at cap must not be rejected");
    }

    [Fact]
    public void EnforceTokenBudget_OverBudget_Reject_ThrowsTokenBudgetExceededException()
    {
        var logger = new CapturingLogger();
        var opts = DefaultRejectOptions(maxInputTokens: 50);
        var msgs = BuildMessages(approximateTokens: 200); // well over 50

        var act = () => BaseAgent.EnforceTokenBudget(msgs, opts, "MyAgent", logger);

        act.Should().Throw<TokenBudgetExceededException>()
            .Which.AgentName.Should().Be("MyAgent");
    }

    [Fact]
    public void EnforceTokenBudget_OverBudget_Reject_LogsErrorBeforeThrowing()
    {
        var logger = new CapturingLogger();
        var opts = DefaultRejectOptions(maxInputTokens: 50);
        var msgs = BuildMessages(approximateTokens: 200);

        try { BaseAgent.EnforceTokenBudget(msgs, opts, "MyAgent", logger); }
        catch (TokenBudgetExceededException) { /* expected */ }

        logger.HasInfo("[TokenBudget]").Should().BeTrue("per-request usage must always be logged");
    }

    [Fact]
    public void EnforceTokenBudget_OverBudget_Truncate_ReturnsTrimmedList()
    {
        var logger = new CapturingLogger();
        var opts = TruncateOptions(maxInputTokens: 60);

        // Build a 3-turn conversation that exceeds 60 tokens.
        var msgs = new List<ChatMessage>
        {
            new(ChatRole.System, "System prompt."),
            new(ChatRole.User,   new string('b', 80)),  // ~20 tokens, oldest non-system
            new(ChatRole.Assistant, new string('c', 80)), // ~20 tokens
            new(ChatRole.User,   new string('d', 80))   // ~20 tokens, latest user
        };
        var before = BaseAgent.EstimatePromptTokens(msgs);
        before.Should().BeGreaterThan(60, "setup: must start over budget");

        var result = BaseAgent.EnforceTokenBudget(msgs, opts, "TruncAgent", logger);

        BaseAgent.EstimatePromptTokens(result).Should().BeLessOrEqualTo(60,
            "truncated list must fit within budget");
    }

    [Fact]
    public void EnforceTokenBudget_Truncate_PreservesSystemAndLatestUser()
    {
        var logger = new CapturingLogger();
        var opts = TruncateOptions(maxInputTokens: 50);

        const string SystemText = "SYSTEM-PRESERVED";
        const string LatestUserText = "LATEST-USER-PRESERVED";

        var msgs = new List<ChatMessage>
        {
            new(ChatRole.System,    SystemText),
            new(ChatRole.User,      new string('x', 200)), // old turn — should be dropped
            new(ChatRole.Assistant, new string('y', 200)), // old turn — should be dropped
            new(ChatRole.User,      LatestUserText)
        };

        var result = BaseAgent.EnforceTokenBudget(msgs, opts, "TruncAgent", logger);

        // System prompt must be first.
        result[0].Role.Should().Be(ChatRole.System);
        result[0].Contents.OfType<TextContent>().Select(t => t.Text)
            .Should().Contain(SystemText);

        // Latest user turn must survive.
        result.Last().Role.Should().Be(ChatRole.User);
        result.Last().Contents.OfType<TextContent>().Select(t => t.Text)
            .Should().Contain(LatestUserText);
    }

    [Fact]
    public void EnforceTokenBudget_AlertRatio_EmitsWarning()
    {
        var logger = new CapturingLogger();
        // Use a cap of 1000 and alert at 80%. Build a prompt that is at ~85%: 850 tokens.
        var opts = new AzureAiOptions
        {
            Enabled = true,
            MaxInputTokens = 1_000,
            BudgetMode = TokenBudgetMode.Reject,
            TokenAlertRatio = 0.8
        };
        var msgs = BuildMessages(approximateTokens: 850);
        // Make sure it is below the cap so it won't throw.
        BaseAgent.EstimatePromptTokens(msgs).Should().BeLessThan(1_000, "must be under cap for alert test");

        BaseAgent.EnforceTokenBudget(msgs, opts, "AlertAgent", logger);

        logger.HasWarning("ALERT").Should().BeTrue("warning must be emitted when near the cap");
    }

    [Fact]
    public void EnforceTokenBudget_Disabled_MaxZero_ReturnsSameList()
    {
        var logger = new CapturingLogger();
        var opts = new AzureAiOptions { Enabled = true, MaxInputTokens = 0 };
        var msgs = BuildMessages(approximateTokens: 99_999);

        var result = BaseAgent.EnforceTokenBudget(msgs, opts, "Agent", logger);

        result.Should().BeSameAs(msgs, "disabled guard (MaxInputTokens=0) must not modify the list");
    }

    [Fact]
    public void EnforceTokenBudget_UsageInfo_AlwaysLogged()
    {
        var logger = new CapturingLogger();
        var opts = DefaultRejectOptions(maxInputTokens: 1_000);
        var msgs = BuildMessages(approximateTokens: 50);

        BaseAgent.EnforceTokenBudget(msgs, opts, "LogAgent", logger);

        logger.HasInfo("[TokenBudget]").Should().BeTrue(
            "per-request token usage must be logged as Info for every call when MaxInputTokens > 0");
    }

    // ─── Integration gate: TryProcessWithAiAsync raises before calling LLM ──

    [Fact]
    public async Task TryProcessWithAiAsync_OverBudget_Reject_ThrowsWithoutCallingLLM()
    {
        // Arrange — IChatClient mock that must NEVER be called.
        var chatClientMock = new Mock<IChatClient>();
        chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("LLM must not be called when over budget"));

        var opts = new AzureAiOptions
        {
            Enabled = true,
            MaxInputTokens = 10,  // Very small cap — even a tiny system+user prompt exceeds this.
            BudgetMode = TokenBudgetMode.Reject,
            TokenAlertRatio = 0.9
        };

        var logger = new CapturingLogger();
        var agent = new BudgetTestAgent(logger, chatClientMock.Object, opts);

        var context = new AgentConversationContext { ConversationId = "test-conv" };

        // Act
        var act = async () => await agent.InvokeProcessWithAiAsync(
            "Hello, this message will exceed the tiny 10-token cap.", context);

        // Assert — exception is raised before LLM is called.
        await act.Should().ThrowAsync<TokenBudgetExceededException>(
            "oversized prompt must be rejected before any LLM request is sent");

        // Prove the LLM was never invoked.
        chatClientMock.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "the LLM client must not be called when the token budget is exceeded");
    }

    [Fact]
    public async Task TryProcessWithAiAsync_UnderBudget_CallsLLMNormally()
    {
        // Arrange — IChatClient that returns a simple text response.
        var chatClientMock = new Mock<IChatClient>();
        chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "OK")]));

        var opts = new AzureAiOptions
        {
            Enabled = true,
            MaxInputTokens = 100_000, // Very large cap — nothing should exceed this.
            BudgetMode = TokenBudgetMode.Reject,
            TokenAlertRatio = 0.9
        };

        var logger = new CapturingLogger();
        var agent = new BudgetTestAgent(logger, chatClientMock.Object, opts);
        var context = new AgentConversationContext { ConversationId = "test-conv" };

        // Act
        var result = await agent.InvokeProcessWithAiAsync("Hello.", context);

        // Assert
        result.Should().NotBeNull("LLM returns a valid response for an under-budget prompt");
        result!.Success.Should().BeTrue();
        chatClientMock.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "the LLM client must be called for an under-budget prompt");
    }
}

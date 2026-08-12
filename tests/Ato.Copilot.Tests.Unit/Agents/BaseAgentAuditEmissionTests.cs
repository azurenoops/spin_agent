using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Ato.Copilot.Agents.Common;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Agents;

/// <summary>
/// Regression gate for the audit-on-exclusion invariant introduced in PR #637 (Issue #612).
///
/// BaseAgent.SelectToolsForMessageAsync must emit exactly (total - 128) EventId 4001
/// ("ToolExcluded") log entries at Warning level whenever the registered tool set exceeds
/// the Azure OpenAI 128-tool hard cap.  This test locks that invariant so a future refactor
/// cannot silently drop audit records.
/// </summary>
public class BaseAgentAuditEmissionTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Captured logger that records every log call for later assertion.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        private readonly List<(LogLevel Level, EventId EventId, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, EventId EventId, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, eventId, formatter(state, exception)));
        }

        public int CountAuditExclusions()
            => _entries.Count(e => e.EventId.Id == 4001 && e.Level == LogLevel.Warning);
    }

    /// <summary>
    /// Minimal concrete agent used solely to expose the protected constructor and tool
    /// registration to the test.  Does not need to process messages — only tool selection
    /// is exercised via reflection.
    /// </summary>
    private sealed class StubAgent : BaseAgent
    {
        public StubAgent(ILogger logger) : base(logger) { }

        public override string AgentId => "stub-agent-test";
        public override string AgentName => "StubAgent";
        public override string Description => "Stub agent for unit testing";
        public override string GetSystemPrompt() => "stub";
        public override double CanHandle(string message) => 0.0;

        public override Task<AgentResponse> ProcessAsync(
            string message,
            AgentConversationContext context,
            CancellationToken cancellationToken = default,
            IProgress<string>? progress = null)
            => throw new NotSupportedException("StubAgent is test-only.");

        /// <summary>Expose protected RegisterTool to the test fixture.</summary>
        public void AddTool(BaseTool tool) => RegisterTool(tool);

        /// <summary>
        /// Invokes the private SelectToolsForMessageAsync via reflection so the test
        /// can assert on the captured logger without needing a live AI back-end.
        /// </summary>
        public async Task<List<BaseTool>> InvokeSelectToolsAsync(string message)
        {
            var method = typeof(BaseAgent).GetMethod(
                "SelectToolsForMessageAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMethodException(nameof(BaseAgent), "SelectToolsForMessageAsync");

            var task = (Task<List<BaseTool>>)method.Invoke(this, new object?[] { message })!;
            return await task;
        }
    }

    /// <summary>Minimal BaseTool stub for test tool registration.</summary>
    private sealed class FakeBaseTool : BaseTool
    {
        private readonly string _name;
        private readonly string _description;

        public FakeBaseTool(string name, string description)
            : base(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
        {
            _name = name;
            _description = description;
        }

        public override string Name => _name;
        public override string Description => _description;

        public override IReadOnlyDictionary<string, ToolParameter> Parameters =>
            new Dictionary<string, ToolParameter>();

        public override Task<string> ExecuteCoreAsync(
            Dictionary<string, object?> arguments,
            CancellationToken cancellationToken = default)
            => Task.FromResult("ok");
    }

    // ---------------------------------------------------------------------------
    // Core audit-emission test — locks the PR #637 / Issue #612 invariant
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Given a tool set of <paramref name="totalTools"/> entries, when the agent selects
    /// tools for a message, it must emit exactly (totalTools - 128) EventId 4001 audit
    /// records at Warning level — one for every tool excluded by the 128-tool cap.
    /// </summary>
    [Theory]
    [InlineData(150)]   // PR #637 reference scenario: 22 excluded
    [InlineData(129)]   // boundary: exactly 1 excluded
    [InlineData(200)]   // larger set: 72 excluded
    public async Task OverBudgetToolSet_EmitsExactlyCapMinusBudget_AuditRecords(int totalTools)
    {
        const int budget = 128;

        // Arrange — captured logger so we can inspect every log call
        var logger = new CapturingLogger();
        var agent = new StubAgent(logger);

        for (var i = 0; i < totalTools; i++)
        {
            agent.AddTool(new FakeBaseTool(
                $"tool_{i:D3}",
                $"Description for tool {i:D3} covering topic area {i % 10}"));
        }

        // Act — invoke SelectToolsForMessageAsync directly via reflection
        var selected = await agent.InvokeSelectToolsAsync("topic area description");

        // Assert — exactly budget tools selected
        Assert.Equal(budget, selected.Count);

        // Assert — exactly (total - budget) EventId 4001 Warning records emitted
        var expectedExclusions = totalTools - budget;
        var actualExclusions = logger.CountAuditExclusions();

        Assert.Equal(expectedExclusions, actualExclusions);
    }
}

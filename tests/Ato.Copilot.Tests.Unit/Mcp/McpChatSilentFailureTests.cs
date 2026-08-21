using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Compliance.Agents;
using Ato.Copilot.Agents.Configuration.Agents;
using Ato.Copilot.Mcp.Models;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.Mcp.Tools;

namespace Ato.Copilot.Tests.Unit.Mcp;

/// <summary>
/// Regression tests for Issue #679 — /mcp/chat silent failure.
///
/// Root cause: <see cref="McpServer.ProcessChatRequestAsync"/> returned a
/// <see cref="McpChatResponse"/> with <c>Success = false</c> while the HTTP
/// bridge and chat controller always returned HTTP 200, masking the failure
/// from the caller. These tests verify that error responses carry
/// <c>Success = false</c> and a non-empty <see cref="McpChatResponse.Errors"/>
/// list so the HTTP layer can map them to a status ≥ 400.
/// </summary>
public class McpChatSilentFailureTests
{
    private readonly Mock<ComplianceAgent> _complianceAgent;
    private readonly StubOrchestrator _orchestrator;

    public McpChatSilentFailureTests()
    {
        _complianceAgent = TestMockFactory.CreateComplianceAgentMock();
        _orchestrator = TestMockFactory.CreateOrchestrator(_complianceAgent.Object);
    }

    // ─── Issue #679 — ProcessChatRequestAsync surfacing ───────────────────────

    /// <summary>
    /// When the agent returns a failed response, ProcessChatRequestAsync must propagate
    /// Success=false so the HTTP layer can return the correct error status.
    /// Before the fix, this information was silently dropped at the HTTP boundary.
    /// </summary>
    [Fact]
    public async Task ProcessChatRequestAsync_WhenAgentFails_ReturnsFalseSuccess()
    {
        // Arrange
        _complianceAgent
            .Setup(a => a.ProcessAsync(
                It.IsAny<string>(), It.IsAny<AgentConversationContext>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<string>>()))
            .ReturnsAsync(new AgentResponse
            {
                Success = false,
                Response = "Agent could not process request",
                AgentName = "compliance-agent"
            });

        // Act
        var result = await CreateServer().ProcessChatRequestAsync("What is AC-2?");

        // Assert — Issue #679: caller must be able to detect failure
        result.Success.Should().BeFalse(
            "a failed agent response must propagate Success=false to the HTTP layer (Issue #679)");
    }

    /// <summary>
    /// When an exception is thrown inside ProcessChatRequestAsync, the response
    /// must include both Success=false AND a populated Errors list so the HTTP
    /// layer has a structured error to return (not just a silently empty body).
    /// </summary>
    [Fact]
    public async Task ProcessChatRequestAsync_WhenExceptionThrown_ReturnsFalseSuccessWithErrors()
    {
        // Arrange
        _complianceAgent
            .Setup(a => a.ProcessAsync(
                It.IsAny<string>(), It.IsAny<AgentConversationContext>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<string>>()))
            .ThrowsAsync(new InvalidOperationException("downstream service fault"));

        // Act
        var result = await CreateServer().ProcessChatRequestAsync("Generate SSP");

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty(
            "the Errors list must be populated on exception so callers receive actionable diagnostics (Issue #679)");
        result.Errors.Should().Contain(e => e.ErrorCode == "PROCESSING_ERROR");
    }


    /// <summary>
    /// Happy path must not be broken: a successful agent response propagates Success=true.
    /// </summary>
    [Fact]
    public async Task ProcessChatRequestAsync_WhenAgentSucceeds_ReturnsTrueSuccess()
    {
        // Arrange
        _complianceAgent
            .Setup(a => a.ProcessAsync(
                It.IsAny<string>(), It.IsAny<AgentConversationContext>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<string>>()))
            .ReturnsAsync(new AgentResponse
            {
                Success = true,
                Response = "AC-2 is Account Management.",
                AgentName = "compliance-agent"
            });

        // Act
        var result = await CreateServer().ProcessChatRequestAsync("What is AC-2?");

        // Assert — happy-path must not regress
        result.Success.Should().BeTrue("a successful agent response must remain success=true (Issue #679 non-regression)");
        result.Response.Should().Be("AC-2 is Account Management.");
    }

    // ─── Issue #628 — NO_TOOL_MATCHED split and EmptyResultsCount ─────────────

    /// <summary>
    /// When all tool calls the LLM requested were to unknown tools, ProcessChatRequestAsync
    /// must return Success=false with error code NO_TOOL_MATCHED and a non-zero EmptyResultsCount.
    /// This splits the "unresolvable tool" case from EMPTY_AGENT_RESPONSE (#791 contract).
    /// </summary>
    [Fact]
    public async Task ProcessChatRequestAsync_WhenAllToolCallsUnmatched_ReturnsNoToolMatchedError()
    {
        // Arrange — all tools failed with "Unknown tool" (the sentinel value BaseAgent writes)
        _complianceAgent
            .Setup(a => a.ProcessAsync(
                It.IsAny<string>(), It.IsAny<AgentConversationContext>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<string>>()))
            .ReturnsAsync(new AgentResponse
            {
                Success = true,
                Response = "I tried to help but couldn't find the right tool.",
                AgentName = "compliance-agent",
                SkippedToolCallCount = 2,
                ToolsExecuted = new List<ToolExecutionResult>
                {
                    new() { ToolName = "ghost_tool_1", Success = false, Result = "Unknown tool", ExecutionTimeMs = 0 },
                    new() { ToolName = "ghost_tool_2", Success = false, Result = "Unknown tool", ExecutionTimeMs = 0 }
                }
            });

        // Act
        var result = await CreateServer().ProcessChatRequestAsync("Use ghost tools");

        // Assert — #628: all-unknown-tool path must fail loud
        result.Success.Should().BeFalse("all-unknown-tool responses must not silently succeed (#628)");
        result.EmptyResultsCount.Should().Be(2, "skipped tool count must propagate to EmptyResultsCount (#628)");
        result.Errors.Should().ContainSingle(e => e.ErrorCode == "NO_TOOL_MATCHED",
            "error code must be NO_TOOL_MATCHED, not EMPTY_AGENT_RESPONSE (#628)");
    }

    /// <summary>
    /// When the LLM resolved at least one tool successfully, a partial skip must NOT fail loud.
    /// EmptyResultsCount should reflect skipped tools but Success stays true.
    /// </summary>
    [Fact]
    public async Task ProcessChatRequestAsync_WhenPartialToolMatch_SucceedWithSkippedCount()
    {
        // Arrange — one successful tool, one skipped (partial match)
        _complianceAgent
            .Setup(a => a.ProcessAsync(
                It.IsAny<string>(), It.IsAny<AgentConversationContext>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<string>>()))
            .ReturnsAsync(new AgentResponse
            {
                Success = true,
                Response = "Partial answer based on available tools.",
                AgentName = "compliance-agent",
                SkippedToolCallCount = 1,
                ToolsExecuted = new List<ToolExecutionResult>
                {
                    new() { ToolName = "get_control", Success = true, Result = "AC-2 details", ExecutionTimeMs = 50 },
                    new() { ToolName = "ghost_tool",  Success = false, Result = "Unknown tool", ExecutionTimeMs = 0 }
                }
            });

        // Act
        var result = await CreateServer().ProcessChatRequestAsync("What is AC-2 via ghost?");

        // Assert — partial match: success stays true, but EmptyResultsCount is non-zero (#628)
        result.Success.Should().BeTrue("partial tool match must not fail loud — a valid tool succeeded (#628)");
        result.EmptyResultsCount.Should().Be(1, "one skipped tool must still be reflected in EmptyResultsCount (#628)");
        result.Errors.Should().NotContain(e => e.ErrorCode == "NO_TOOL_MATCHED",
            "NO_TOOL_MATCHED must not fire when at least one tool succeeded (#628)");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private McpServer CreateServer(
        Ato.Copilot.Core.Services.OfflineModeService? offlineModeService = null)
    {
        offlineModeService ??= new Ato.Copilot.Core.Services.OfflineModeService(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Mock.Of<ILogger<Ato.Copilot.Core.Services.OfflineModeService>>());

        return new McpServer(
            (ComplianceMcpTools)null!,
            (KnowledgeBaseMcpTools)null!,
            _complianceAgent.Object,
            (ConfigurationAgent)null!,
            null!,
            _orchestrator,
            Enumerable.Empty<BaseTool>(),
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<Ato.Copilot.Core.Interfaces.IPathSanitizationService>(),
            new Ato.Copilot.Core.Services.ResponseCacheService(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                new Ato.Copilot.Core.Observability.HttpMetrics(),
                Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Models.CachingOptions()),
                Mock.Of<Ato.Copilot.Core.Interfaces.Tenancy.ITenantContextAccessor>(),
                Mock.Of<ILogger<Ato.Copilot.Core.Services.ResponseCacheService>>()),
            Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Models.PaginationOptions()),
            offlineModeService,
            Mock.Of<Ato.Copilot.Core.Interfaces.Provenance.IModelCallLedger>(),
            Mock.Of<ILogger<McpServer>>());
    }
}

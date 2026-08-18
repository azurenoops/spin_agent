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

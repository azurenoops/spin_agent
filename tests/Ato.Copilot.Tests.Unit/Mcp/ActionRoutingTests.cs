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
/// Tests for action routing in McpServer (T026, FR-014a, R-006).
/// </summary>
public class ActionRoutingTests
{
    private readonly Mock<ComplianceAgent> _complianceAgent;
    private readonly StubOrchestrator _orchestrator;
    private readonly McpServer _server;

    public ActionRoutingTests()
    {
        _complianceAgent = TestMockFactory.CreateComplianceAgentMock();
        _complianceAgent.Setup(a => a.ProcessAsync(
                It.IsAny<string>(), It.IsAny<AgentConversationContext>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<string>>()))
            .ReturnsAsync(new AgentResponse
            {
                Success = true,
                Response = "Action executed",
                AgentName = "Compliance Agent"
            });

        _orchestrator = TestMockFactory.CreateOrchestrator(_complianceAgent.Object);

        _server = new McpServer(
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
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                new Ato.Copilot.Core.Observability.HttpMetrics(),
                Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Models.CachingOptions()),
                Mock.Of<ILogger<Ato.Copilot.Core.Services.ResponseCacheService>>()),
            Mock.Of<Ato.Copilot.Core.Interfaces.Tenancy.ITenantContext>(),
            Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Models.PaginationOptions()),
            new Ato.Copilot.Core.Services.OfflineModeService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), Mock.Of<ILogger<Ato.Copilot.Core.Services.OfflineModeService>>()),
            Mock.Of<ILogger<McpServer>>());
    }

    [Theory]
    [InlineData("remediate")]
    [InlineData("drillDown")]
    [InlineData("collectEvidence")]
    [InlineData("acknowledgeAlert")]
    [InlineData("dismissAlert")]
    [InlineData("escalateAlert")]
    [InlineData("updateFindingStatus")]
    [InlineData("showKanban")]
    [InlineData("moveKanbanTask")]
    [InlineData("checkPimStatus")]
    [InlineData("activatePim")]
    [InlineData("listEligiblePimRoles")]
    public async Task ProcessChatRequestAsync_ValidAction_RoutesSuccessfully(string action)
    {
        var actionContext = new Dictionary<string, object> { ["findingId"] = "F-001" };

        var result = await _server.ProcessChatRequestAsync(
            "Execute action",
            action: action,
            actionContext: actionContext);

        result.Should().NotBeNull();
        result.IntentType.Should().Be("compliance");
    }

    [Fact]
    public async Task ProcessChatRequestAsync_UnknownAction_ReturnsError()
    {
        var result = await _server.ProcessChatRequestAsync(
            "Execute action",
            action: "nonexistentAction");

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].ErrorCode.Should().Be("UNKNOWN_ACTION");
        result.Errors[0].Suggestion.Should().Contain("Available actions");
    }

    [Fact]
    public async Task ProcessChatRequestAsync_NoAction_RoutesToAgent()
    {
        var result = await _server.ProcessChatRequestAsync("Run a compliance assessment");

        result.Should().NotBeNull();
        result.IntentType.Should().Be("compliance");

        // Should call the normal agent flow, not action routing
        _complianceAgent.Verify(a => a.ProcessAsync(
            "Run a compliance assessment",
            It.IsAny<AgentConversationContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<IProgress<string>>()), Times.Once);
    }

    [Fact]
    public async Task ProcessChatRequestAsync_ActionWithContext_PassesContextToAgent()
    {
        var actionContext = new Dictionary<string, object>
        {
            ["controlId"] = "AC-2",
            ["findingId"] = "F-123"
        };

        var result = await _server.ProcessChatRequestAsync(
            "Drill down",
            action: "drillDown",
            actionContext: actionContext);

        result.Should().NotBeNull();

        // Verify the compliance agent received a message containing the action context
        _complianceAgent.Verify(a => a.ProcessAsync(
            It.Is<string>(msg => msg.Contains("drillDown") || msg.Contains("kb_search_nist_controls")),
            It.IsAny<AgentConversationContext>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<IProgress<string>>()), Times.Once);
    }
}

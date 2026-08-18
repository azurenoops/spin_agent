using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Compliance.Agents;
using Ato.Copilot.Agents.Configuration.Agents;
using Ato.Copilot.Agents.KnowledgeBase.Agents;
using Ato.Copilot.Mcp.Models;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.Mcp.Tools;

namespace Ato.Copilot.Tests.Unit.Mcp;

/// <summary>
/// Tests for intent type mapping in McpServer.ProcessChatRequestAsync (T023, FR-001, R-002).
/// </summary>
public class McpServerIntentMappingTests
{
    private readonly Mock<ComplianceAgent> _complianceAgent;
    private readonly StubOrchestrator _orchestrator;
    private readonly McpServer _server;

    public McpServerIntentMappingTests()
    {
        _complianceAgent = TestMockFactory.CreateComplianceAgentMock();
        _complianceAgent.Setup(a => a.ProcessAsync(
                It.IsAny<string>(), It.IsAny<AgentConversationContext>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<string>>()))
            .ReturnsAsync(new AgentResponse
            {
                Success = true,
                Response = "Assessment complete",
                AgentName = "Compliance Agent"
            });

        _orchestrator = TestMockFactory.CreateOrchestrator();

        _server = CreateMcpServer(_complianceAgent.Object);
    }

    [Fact]
    public async Task ProcessChatRequestAsync_ComplianceAgent_ReturnsComplianceIntentType()
    {
        _orchestrator.SetSelectedAgent(_complianceAgent.Object);

        var result = await CreateMcpServerWithOrchestrator().ProcessChatRequestAsync("Run a FedRAMP assessment");

        result.IntentType.Should().Be("compliance");
    }

    [Fact]
    public async Task ProcessChatRequestAsync_UnknownAgent_ReturnsGeneralIntentType()
    {
        var unknownAgent = new Mock<BaseAgent>(MockBehavior.Loose, new object[] { Mock.Of<ILogger>() });
        unknownAgent.Setup(a => a.AgentId).Returns("unknown-agent");
        unknownAgent.Setup(a => a.AgentName).Returns("Unknown Agent");
        unknownAgent.Setup(a => a.CanHandle(It.IsAny<string>())).Returns(0.5);
        unknownAgent.Setup(a => a.ProcessAsync(
                It.IsAny<string>(), It.IsAny<AgentConversationContext>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<string>>()))
            .ReturnsAsync(new AgentResponse { Success = true, Response = "done", AgentName = "Unknown" });

        _orchestrator.SetSelectedAgent(unknownAgent.Object);

        var server = CreateMcpServerWithOrchestrator();
        var result = await server.ProcessChatRequestAsync("Something unknown");

        result.IntentType.Should().Be("general");
    }

    [Fact]
    public async Task ProcessChatRequestAsync_AgentUsed_SerializesCorrectly()
    {
        _orchestrator.SetSelectedAgent(_complianceAgent.Object);

        var result = await CreateMcpServerWithOrchestrator().ProcessChatRequestAsync("Run assessment");

        result.AgentName.Should().Be("Compliance Agent");
    }

    private McpServer CreateMcpServer(BaseAgent agent)
    {
        var orchestrator = TestMockFactory.CreateOrchestrator(agent);

        return new McpServer(
            (ComplianceMcpTools)null!,
            (KnowledgeBaseMcpTools)null!,
            _complianceAgent.Object,
            (ConfigurationAgent)null!,
            null!,
            orchestrator,
            Enumerable.Empty<BaseTool>(),
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<Ato.Copilot.Core.Interfaces.IPathSanitizationService>(),
            new Ato.Copilot.Core.Services.ResponseCacheService(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                new Ato.Copilot.Core.Observability.HttpMetrics(),
                Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Models.CachingOptions()),
                Mock.Of<Ato.Copilot.Core.Interfaces.Tenancy.ITenantContextAccessor>(),
                Mock.Of<ILogger<Ato.Copilot.Core.Services.ResponseCacheService>>()),
            Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Models.PaginationOptions()),
            new Ato.Copilot.Core.Services.OfflineModeService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), Mock.Of<ILogger<Ato.Copilot.Core.Services.OfflineModeService>>()),
            Mock.Of<Ato.Copilot.Core.Interfaces.Provenance.IModelCallLedger>(),
            Mock.Of<ILogger<McpServer>>());
    }

    private McpServer CreateMcpServerWithOrchestrator()
    {
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
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                new Ato.Copilot.Core.Observability.HttpMetrics(),
                Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Models.CachingOptions()),
                Mock.Of<Ato.Copilot.Core.Interfaces.Tenancy.ITenantContextAccessor>(),
                Mock.Of<ILogger<Ato.Copilot.Core.Services.ResponseCacheService>>()),
            Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Models.PaginationOptions()),
            new Ato.Copilot.Core.Services.OfflineModeService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), Mock.Of<ILogger<Ato.Copilot.Core.Services.OfflineModeService>>()),
            Mock.Of<Ato.Copilot.Core.Interfaces.Provenance.IModelCallLedger>(),
            Mock.Of<ILogger<McpServer>>());
    }
}

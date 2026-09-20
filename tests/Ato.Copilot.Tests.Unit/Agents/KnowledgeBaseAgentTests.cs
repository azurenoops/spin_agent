using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.KnowledgeBase.Agents;
using Ato.Copilot.Agents.KnowledgeBase.Configuration;
using Ato.Copilot.Agents.KnowledgeBase.Tools;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.State.Abstractions;

namespace Ato.Copilot.Tests.Unit.Agents;

public class KnowledgeBaseAgentTests
{
    private readonly KnowledgeBaseAgent _agent;
    private readonly Mock<IAgentStateManager> _stateManagerMock = new();

    public KnowledgeBaseAgentTests()
    {
        var options = Options.Create(new KnowledgeBaseAgentOptions());
        var logger = Mock.Of<ILogger<KnowledgeBaseAgent>>();

        // Create a real ExplainNistControlTool with mocked dependencies
        var nistServiceMock = new Mock<INistControlsService>();
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var toolOptions = Options.Create(new KnowledgeBaseAgentOptions());
        var toolLogger = Mock.Of<ILogger<ExplainNistControlTool>>();
        var explainNistControlTool = new ExplainNistControlTool(
            nistServiceMock.Object, cache, toolOptions, toolLogger);

        var searchToolLogger = Mock.Of<ILogger<SearchNistControlsTool>>();
        var searchNistControlsTool = new SearchNistControlsTool(
            nistServiceMock.Object, cache, toolOptions, searchToolLogger);

        var stigServiceMock = new Mock<IStigKnowledgeService>();
        var explainStigLogger = Mock.Of<ILogger<ExplainStigTool>>();
        var explainStigTool = new ExplainStigTool(
            stigServiceMock.Object, cache, toolOptions, explainStigLogger);

        var searchStigsLogger = Mock.Of<ILogger<SearchStigsTool>>();
        var searchStigsTool = new SearchStigsTool(
            stigServiceMock.Object, cache, toolOptions, searchStigsLogger);

        var rmfServiceMock = new Mock<IRmfKnowledgeService>();
        var dodInstructionServiceMock = new Mock<IDoDInstructionService>();
        var dodWorkflowServiceMock = new Mock<IDoDWorkflowService>();
        var explainRmfLogger = Mock.Of<ILogger<ExplainRmfTool>>();
        var explainRmfTool = new ExplainRmfTool(
            rmfServiceMock.Object, dodInstructionServiceMock.Object,
            dodWorkflowServiceMock.Object, cache, toolOptions, explainRmfLogger);

        var impactLevelServiceMock = new Mock<IImpactLevelService>();
        var explainImpactLevelLogger = Mock.Of<ILogger<ExplainImpactLevelTool>>();
        var explainImpactLevelTool = new ExplainImpactLevelTool(
            impactLevelServiceMock.Object, cache, toolOptions, explainImpactLevelLogger);

        var fedRampTemplateServiceMock = new Mock<IFedRampTemplateService>();
        var fedRampToolLogger = Mock.Of<ILogger<GetFedRampTemplateGuidanceTool>>();
        var getFedRampTemplateGuidanceTool = new GetFedRampTemplateGuidanceTool(
            fedRampTemplateServiceMock.Object, cache, toolOptions, fedRampToolLogger);

        _agent = new KnowledgeBaseAgent(
            options,
            _stateManagerMock.Object,
            explainNistControlTool,
            searchNistControlsTool,
            explainStigTool,
            searchStigsTool,
            explainRmfTool,
            explainImpactLevelTool,
            getFedRampTemplateGuidanceTool,
            logger);
    }

    // ──────────────── Identity Tests ────────────────

    [Fact]
    public void Agent_Should_Have_Correct_AgentId()
    {
        _agent.AgentId.Should().Be("knowledgebase-agent");
    }

    [Fact]
    public void Agent_Should_Have_Correct_AgentName()
    {
        _agent.AgentName.Should().Be("KnowledgeBase Agent");
    }

    [Fact]
    public void Agent_Should_Have_Description()
    {
        _agent.Description.Should().NotBeNullOrEmpty();
        _agent.Description.Should().Contain("knowledge", because: "agent is focused on knowledge/education");
    }

    [Fact]
    public void Agent_Should_Have_System_Prompt()
    {
        var prompt = _agent.GetSystemPrompt();
        prompt.Should().NotBeNullOrEmpty();
    }

    // ──────────────── CanHandle Tests ────────────────

    [Theory]
    [InlineData("What is AC-2?", 0.8)]
    [InlineData("Explain NIST control AC-2", 0.9)]
    [InlineData("Tell me about STIG V-12345", 0.9)]
    [InlineData("Define the RMF process", 0.9)]
    [InlineData("Describe impact level IL5", 0.9)]
    public void CanHandle_WithKnowledgeKeywords_ReturnsHighScore(string message, double expectedMinimum)
    {
        var score = _agent.CanHandle(message);
        score.Should().BeGreaterThanOrEqualTo(expectedMinimum);
    }

    [Theory]
    [InlineData("nist controls")]
    [InlineData("stig requirements")]
    [InlineData("rmf steps")]
    [InlineData("fedramp templates")]
    [InlineData("impact level")]
    [InlineData("dod instruction")]
    public void CanHandle_WithDomainTermsOnly_ReturnsMediumScore(string message)
    {
        var score = _agent.CanHandle(message);
        score.Should().BeGreaterThanOrEqualTo(0.4);
        score.Should().BeLessThan(0.8);
    }

    [Theory]
    [InlineData("scan my subscription")]
    [InlineData("run compliance assessment")]
    [InlineData("remediate findings")]
    public void CanHandle_WithActionKeywords_ReturnsLowScore(string message)
    {
        var score = _agent.CanHandle(message);
        score.Should().BeLessThanOrEqualTo(0.2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CanHandle_WithEmptyOrNull_ReturnsZero(string? message)
    {
        var score = _agent.CanHandle(message!);
        score.Should().Be(0.0);
    }

    [Fact]
    public void CanHandle_WithGeneralChat_ReturnsLowScore()
    {
        var score = _agent.CanHandle("hello how are you");
        score.Should().BeLessThanOrEqualTo(0.1);
    }

    // ──────────────── AnalyzeQueryType Tests ────────────────

    [Theory]
    [InlineData("What is AC-2?", KnowledgeQueryType.NistControl)]
    [InlineData("Explain NIST control SI-3", KnowledgeQueryType.NistControl)]
    [InlineData("Find controls related to encryption", KnowledgeQueryType.NistSearch)]
    [InlineData("Search NIST for access control", KnowledgeQueryType.NistSearch)]
    [InlineData("What is STIG V-12345?", KnowledgeQueryType.Stig)]
    [InlineData("Explain STIG rule SV-12345r1", KnowledgeQueryType.Stig)]
    [InlineData("Search STIGs for password", KnowledgeQueryType.StigSearch)]
    [InlineData("Find STIG findings related to encryption", KnowledgeQueryType.StigSearch)]
    [InlineData("Explain the RMF process", KnowledgeQueryType.Rmf)]
    [InlineData("What are the RMF steps?", KnowledgeQueryType.Rmf)]
    [InlineData("What is IL5?", KnowledgeQueryType.ImpactLevel)]
    [InlineData("Compare impact levels", KnowledgeQueryType.ImpactLevel)]
    [InlineData("Show FedRAMP SSP template", KnowledgeQueryType.FedRamp)]
    [InlineData("POA&M template guidance", KnowledgeQueryType.FedRamp)]
    public void AnalyzeQueryType_ClassifiesCorrectly(string message, KnowledgeQueryType expectedType)
    {
        var queryType = _agent.AnalyzeQueryType(message);
        queryType.Should().Be(expectedType);
    }

    [Fact]
    public void AnalyzeQueryType_WithUnknownQuery_ReturnsGeneralKnowledge()
    {
        var queryType = _agent.AnalyzeQueryType("Tell me about compliance in general");
        queryType.Should().Be(KnowledgeQueryType.GeneralKnowledge);
    }

    // ──────────────── ProcessAsync Tests ────────────────

    [Fact]
    public async Task ProcessAsync_WithEmptyMessage_ReturnsResponse()
    {
        var context = new AgentConversationContext
        {
            ConversationId = "test-1",
            UserId = "test-user"
        };

        var result = await _agent.ProcessAsync("", context);

        result.Should().NotBeNull();
        result.AgentName.Should().Be("KnowledgeBase Agent");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_WithKnowledgeQuery_ReturnsResponse()
    {
        var context = new AgentConversationContext
        {
            ConversationId = "test-2",
            UserId = "test-user"
        };

        var result = await _agent.ProcessAsync("What is AC-2?", context);

        result.Should().NotBeNull();
        result.AgentName.Should().Be("KnowledgeBase Agent");
    }

    [Fact]
    public async Task ProcessAsync_WithRmfQueryAndSizeLimitedCache_ReturnsStructuredSuccess()
    {
        // Arrange
        var context = new AgentConversationContext
        {
            ConversationId = "cache-regression-632",
            UserId = "test-user"
        };

        // Act
        var result = await _agent.ProcessAsync("What is the NIST RMF?", context);

        // Assert
        result.Success.Should().BeTrue();
        result.Response.Should().NotContain("Cache entry must specify a value for Size");
        result.ToolsExecuted.Should().ContainSingle(tool =>
            tool.ToolName == "kb_explain_rmf" && tool.Success);
    }

    // ──────────────── US9: Cross-Agent State Sharing Tests ────────────────

    [Fact]
    public async Task ProcessAsync_NistQuery_Should_Store_State()
    {
        var context = new AgentConversationContext
        {
            ConversationId = "state-test-1",
            UserId = "test-user"
        };

        await _agent.ProcessAsync("What is AC-2?", context);

        _stateManagerMock.Verify(
            s => s.SetStateAsync(
                "knowledgebase-agent", "kb_last_nist_control",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_StigQuery_Should_Store_State()
    {
        var context = new AgentConversationContext
        {
            ConversationId = "state-test-2",
            UserId = "test-user"
        };

        await _agent.ProcessAsync("What is STIG V-12345?", context);

        _stateManagerMock.Verify(
            s => s.SetStateAsync(
                "knowledgebase-agent", "kb_last_stig",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_RmfQuery_Should_Not_Store_Nist_Or_Stig_State()
    {
        var context = new AgentConversationContext
        {
            ConversationId = "state-test-3",
            UserId = "test-user"
        };

        await _agent.ProcessAsync("Explain the RMF process", context);

        _stateManagerMock.Verify(
            s => s.SetStateAsync(
                "knowledgebase-agent", "kb_last_nist_control",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _stateManagerMock.Verify(
            s => s.SetStateAsync(
                "knowledgebase-agent", "kb_last_stig",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ──────────────── US11: Operation Metrics Tests ────────────────

    [Fact]
    public async Task ProcessAsync_Should_Track_LastOperation()
    {
        var context = new AgentConversationContext
        {
            ConversationId = "metrics-test-1",
            UserId = "test-user"
        };

        await _agent.ProcessAsync("What is AC-2?", context);

        _stateManagerMock.Verify(
            s => s.SetStateAsync(
                "knowledgebase-agent", "last_operation",
                "NistControl",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_Should_Track_Success()
    {
        var context = new AgentConversationContext
        {
            ConversationId = "metrics-test-2",
            UserId = "test-user"
        };

        await _agent.ProcessAsync("What is AC-2?", context);

        _stateManagerMock.Verify(
            s => s.SetStateAsync(
                "knowledgebase-agent", "last_query_success",
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_Should_Track_DurationMs()
    {
        var context = new AgentConversationContext
        {
            ConversationId = "metrics-test-3",
            UserId = "test-user"
        };

        await _agent.ProcessAsync("What is AC-2?", context);

        _stateManagerMock.Verify(
            s => s.SetStateAsync(
                "knowledgebase-agent", "last_query_duration_ms",
                It.IsAny<double>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_Should_Increment_OperationCount()
    {
        _stateManagerMock.Setup(s => s.GetStateAsync<int>(
                "knowledgebase-agent", "operation_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var context = new AgentConversationContext
        {
            ConversationId = "metrics-test-4",
            UserId = "test-user"
        };

        await _agent.ProcessAsync("What is AC-2?", context);

        _stateManagerMock.Verify(
            s => s.SetStateAsync(
                "knowledgebase-agent", "operation_count",
                6,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_Should_Track_LastQuery()
    {
        var context = new AgentConversationContext
        {
            ConversationId = "metrics-test-5",
            UserId = "test-user"
        };

        await _agent.ProcessAsync("What is AC-2?", context);

        _stateManagerMock.Verify(
            s => s.SetStateAsync(
                "knowledgebase-agent", "last_query",
                "What is AC-2?",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_Should_Track_LastOperationAt()
    {
        var context = new AgentConversationContext
        {
            ConversationId = "metrics-test-6",
            UserId = "test-user"
        };

        await _agent.ProcessAsync("What is AC-2?", context);

        _stateManagerMock.Verify(
            s => s.SetStateAsync(
                "knowledgebase-agent", "last_operation_at",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

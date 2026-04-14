using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Compliance.Agents;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Agents.Compliance.Tools.Poam;
using Ato.Copilot.Agents.Configuration.Agents;
using Ato.Copilot.Agents.Configuration.Tools;
using Ato.Copilot.Agents.KnowledgeBase.Agents;
using Ato.Copilot.Agents.KnowledgeBase.Configuration;
using Ato.Copilot.Agents.KnowledgeBase.Tools;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.Mcp.Tools;
using Ato.Copilot.State.Abstractions;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Ato.Copilot.Tests.Unit.Server;

/// <summary>
/// Integration-style unit tests for McpServer AI flow (Feature 011 / T024).
/// Verifies: conversation history passes through to agents, AI-enabled agents
/// produce natural language responses, and AI-disabled agents produce deterministic output.
/// </summary>
public class McpServerAiIntegrationTests
{
    private readonly Mock<IChatClient> _chatClientMock = new();
    private readonly Mock<IAgentStateManager> _stateManagerMock = new();
    private readonly IServiceScopeFactory _scopeFactory = Mock.Of<IServiceScopeFactory>();

    private McpServer CreateMcpServer(IChatClient? chatClient = null,
        AzureAiOptions? aiOptions = null)
    {
        IOptions<AzureAiOptions>? optionsWrapper =
            aiOptions != null ? Options.Create(aiOptions) : null;

        // ── ConfigurationAgent (simplest, used as primary test agent) ────────
        _stateManagerMock.Setup(s => s.GetStateAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _stateManagerMock.Setup(s => s.GetStateAsync<ConfigurationSettings>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConfigurationSettings?)null);
        _stateManagerMock.Setup(s => s.SetStateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var configTool = new ConfigurationTool(_stateManagerMock.Object, Mock.Of<ILogger<ConfigurationTool>>());
        var configAgent = new ConfigurationAgent(configTool, Mock.Of<ILogger<ConfigurationAgent>>(), chatClient, null, optionsWrapper);

        // ── ComplianceAgent (required for McpServer constructor) ─────────────
        var complianceAgent = CreateComplianceAgent(chatClient, optionsWrapper);

        // ── KnowledgeBaseAgent ───────────────────────────────────────────────
        var kbAgent = CreateKnowledgeBaseAgent(chatClient, optionsWrapper);

        // ── MCP tools (not invoked during ProcessChatRequestAsync) ───────────
        var complianceMcpTools = CreateComplianceMcpTools();
        var kbMcpTools = CreateKnowledgeBaseMcpTools();

        // ── Orchestrator ─────────────────────────────────────────────────────
        var agents = new BaseAgent[] { complianceAgent, configAgent, kbAgent };
        var orchestrator = new AgentOrchestrator(agents, Mock.Of<ILogger<AgentOrchestrator>>());

        return new McpServer(
            complianceMcpTools,
            kbMcpTools,
            complianceAgent,
            configAgent,
            configTool,
            orchestrator,
            Enumerable.Empty<BaseTool>(),
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<Ato.Copilot.Core.Interfaces.IPathSanitizationService>(),
            new Ato.Copilot.Core.Services.ResponseCacheService(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                new Ato.Copilot.Core.Observability.HttpMetrics(),
                Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Models.CachingOptions()),
                Mock.Of<ILogger<Ato.Copilot.Core.Services.ResponseCacheService>>()),
            Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Models.PaginationOptions()),
            new Ato.Copilot.Core.Services.OfflineModeService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), Mock.Of<ILogger<Ato.Copilot.Core.Services.OfflineModeService>>()),
            Mock.Of<ILogger<McpServer>>());
    }

    private static AzureAiOptions CreateEnabledOptions() => new()
    {
        Enabled = true,
        MaxToolIterations = 5,
        Temperature = 0.3,
        Endpoint = "https://test.openai.azure.us/"
    };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessChatRequestAsync_PassesConversationHistoryToAgent()
    {
        IList<ChatMessage>? capturedMessages = null;
        _chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) =>
                capturedMessages = msgs.ToList())
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Configured.")]));

        var server = CreateMcpServer(_chatClientMock.Object, CreateEnabledOptions());

        var history = new List<(string Role, string Content)>
        {
            ("user", "What is my subscription?"),
            ("assistant", "Your subscription is sub-123."),
        };

        // "show settings" routes to ConfigurationAgent via orchestrator
        await server.ProcessChatRequestAsync(
            "show my current settings", conversationHistory: history);

        capturedMessages.Should().NotBeNull();
        // Should include: system prompt + 2 history items + 1 user message
        capturedMessages!.Count.Should().BeGreaterOrEqualTo(4);
        capturedMessages[0].Role.Should().Be(ChatRole.System);
    }

    [Fact]
    public async Task ProcessChatRequestAsync_AiEnabled_ReturnsNaturalLanguageResponse()
    {
        _chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant,
                "Your current settings: subscription sub-123, framework NIST 800-53.")]));

        var server = CreateMcpServer(_chatClientMock.Object, CreateEnabledOptions());

        var result = await server.ProcessChatRequestAsync("show my current settings");

        result.Success.Should().BeTrue();
        result.Response.Should().Contain("settings");
        result.AgentName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ProcessChatRequestAsync_HistoryIncludesUserAndAssistantRoles()
    {
        IList<ChatMessage>? capturedMessages = null;
        _chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) =>
                capturedMessages = msgs.ToList())
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done.")]));

        var server = CreateMcpServer(_chatClientMock.Object, CreateEnabledOptions());

        var history = new List<(string Role, string Content)>
        {
            ("user", "First question"),
            ("assistant", "First answer"),
            ("user", "Second question"),
            ("assistant", "Second answer"),
        };

        await server.ProcessChatRequestAsync("show settings", conversationHistory: history);

        capturedMessages.Should().NotBeNull();
        // Verify both user and assistant roles are present in the captured messages
        capturedMessages!.Any(m => m.Role == ChatRole.User).Should().BeTrue();
        capturedMessages.Any(m => m.Role == ChatRole.Assistant).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessChatRequestAsync_AiDisabled_FallsBackToDeterministic()
    {
        var server = CreateMcpServer(); // No IChatClient

        var result = await server.ProcessChatRequestAsync("show settings");

        result.Success.Should().BeTrue();
        result.AgentName.Should().NotBeNullOrEmpty();
        // Without AI, the response comes from deterministic tool execution
        _chatClientMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Factory helpers ──────────────────────────────────────────────────────

    private ComplianceAgent CreateComplianceAgent(IChatClient? chatClient, IOptions<AzureAiOptions>? aiOptions)
    {
        var e = Mock.Of<IAtoComplianceEngine>();
        var r = Mock.Of<IRemediationEngine>();
        var n = Mock.Of<INistControlsService>();
        var ev = Mock.Of<IEvidenceStorageService>();
        var m = Mock.Of<IComplianceMonitoringService>();
        var d = Mock.Of<IDocumentGenerationService>();
        var a = Mock.Of<IAssessmentAuditService>();
        var h = Mock.Of<IComplianceHistoryService>();
        var s = Mock.Of<IComplianceStatusService>();
        var w = Mock.Of<IComplianceWatchService>();
        var am = Mock.Of<IAlertManager>();
        var es = Mock.Of<IEscalationService>();
        var sf = _scopeFactory;

        var dbId = $"McpInteg_{Guid.NewGuid()}";
        Func<string, DbContextOptions<AtoCopilotContext>> dbOptions = name =>
            new DbContextOptionsBuilder<AtoCopilotContext>()
                .UseInMemoryDatabase($"{dbId}_{name}").Options;

        return new ComplianceAgent(
            new ComplianceAssessmentTool(e, sf, Mock.Of<ILogger<ComplianceAssessmentTool>>()),
            new ControlFamilyTool(n, Mock.Of<ILogger<ControlFamilyTool>>()),
            new DocumentGenerationTool(d, Mock.Of<IDocumentTemplateService>(), sf, Mock.Of<ILogger<DocumentGenerationTool>>()),
            new EvidenceCollectionTool(ev, Mock.Of<ILogger<EvidenceCollectionTool>>()),
            new RemediationExecuteTool(r, Mock.Of<ILogger<RemediationExecuteTool>>()),
            new ValidateRemediationTool(r, Mock.Of<ILogger<ValidateRemediationTool>>()),
            new RemediationPlanTool(r, Mock.Of<ILogger<RemediationPlanTool>>()),
            new AssessmentAuditLogTool(a, Mock.Of<ILogger<AssessmentAuditLogTool>>()),
            new ComplianceHistoryTool(h, Mock.Of<ILogger<ComplianceHistoryTool>>()),
            new ComplianceStatusTool(s, Mock.Of<ILogger<ComplianceStatusTool>>()),
            new ComplianceMonitoringTool(m, Mock.Of<ILogger<ComplianceMonitoringTool>>()),
            new KanbanCreateBoardTool(sf, Mock.Of<ILogger<KanbanCreateBoardTool>>()),
            new KanbanBoardShowTool(sf, Mock.Of<ILogger<KanbanBoardShowTool>>()),
            new KanbanGetTaskTool(sf, Mock.Of<ILogger<KanbanGetTaskTool>>()),
            new KanbanCreateTaskTool(sf, Mock.Of<ILogger<KanbanCreateTaskTool>>()),
            new KanbanAssignTaskTool(sf, Mock.Of<ILogger<KanbanAssignTaskTool>>()),
            new KanbanMoveTaskTool(sf, Mock.Of<ILogger<KanbanMoveTaskTool>>()),
            new KanbanTaskListTool(sf, Mock.Of<ILogger<KanbanTaskListTool>>()),
            new KanbanTaskHistoryTool(sf, Mock.Of<ILogger<KanbanTaskHistoryTool>>()),
            new KanbanValidateTaskTool(sf, Mock.Of<ILogger<KanbanValidateTaskTool>>()),
            new KanbanAddCommentTool(sf, Mock.Of<ILogger<KanbanAddCommentTool>>()),
            new KanbanTaskCommentsTool(sf, Mock.Of<ILogger<KanbanTaskCommentsTool>>()),
            new KanbanEditCommentTool(sf, Mock.Of<ILogger<KanbanEditCommentTool>>()),
            new KanbanDeleteCommentTool(sf, Mock.Of<ILogger<KanbanDeleteCommentTool>>()),
            new KanbanRemediateTaskTool(sf, Mock.Of<ILogger<KanbanRemediateTaskTool>>()),
            new KanbanCollectEvidenceTool(sf, Mock.Of<ILogger<KanbanCollectEvidenceTool>>()),
            new KanbanBulkUpdateTool(sf, Mock.Of<ILogger<KanbanBulkUpdateTool>>()),
            new KanbanExportTool(sf, Mock.Of<ILogger<KanbanExportTool>>()),
            new KanbanArchiveBoardTool(sf, Mock.Of<ILogger<KanbanArchiveBoardTool>>()),
            new KanbanGenerateScriptTool(sf, Mock.Of<ILogger<KanbanGenerateScriptTool>>()),
            new KanbanGenerateValidationTool(sf, Mock.Of<ILogger<KanbanGenerateValidationTool>>()),
            new CacStatusTool(sf, Mock.Of<ILogger<CacStatusTool>>()),
            new CacSignOutTool(sf, Mock.Of<ILogger<CacSignOutTool>>()),
            new CacSetTimeoutTool(sf, Mock.Of<ILogger<CacSetTimeoutTool>>()),
            new CacMapCertificateTool(sf, Mock.Of<ILogger<CacMapCertificateTool>>()),
            new PimListEligibleTool(sf, Mock.Of<ILogger<PimListEligibleTool>>()),
            new PimActivateRoleTool(sf, Mock.Of<ILogger<PimActivateRoleTool>>()),
            new PimDeactivateRoleTool(sf, Mock.Of<ILogger<PimDeactivateRoleTool>>()),
            new PimListActiveTool(sf, Mock.Of<ILogger<PimListActiveTool>>()),
            new PimExtendRoleTool(sf, Mock.Of<ILogger<PimExtendRoleTool>>()),
            new PimApproveRequestTool(sf, Mock.Of<ILogger<PimApproveRequestTool>>()),
            new PimDenyRequestTool(sf, Mock.Of<ILogger<PimDenyRequestTool>>()),
            new JitRequestAccessTool(sf, Mock.Of<ILogger<JitRequestAccessTool>>()),
            new JitListSessionsTool(sf, Mock.Of<ILogger<JitListSessionsTool>>()),
            new JitRevokeAccessTool(sf, Mock.Of<ILogger<JitRevokeAccessTool>>()),
            new PimHistoryTool(sf, Mock.Of<ILogger<PimHistoryTool>>()),
            new WatchEnableMonitoringTool(w, Mock.Of<ILogger<WatchEnableMonitoringTool>>()),
            new WatchDisableMonitoringTool(w, Mock.Of<ILogger<WatchDisableMonitoringTool>>()),
            new WatchConfigureMonitoringTool(w, Mock.Of<ILogger<WatchConfigureMonitoringTool>>()),
            new WatchMonitoringStatusTool(w, Mock.Of<ILogger<WatchMonitoringStatusTool>>()),
            new WatchShowAlertsTool(am, Mock.Of<ILogger<WatchShowAlertsTool>>()),
            new WatchGetAlertTool(am, Mock.Of<ILogger<WatchGetAlertTool>>()),
            new WatchAcknowledgeAlertTool(am, Mock.Of<ILogger<WatchAcknowledgeAlertTool>>()),
            new WatchFixAlertTool(am, e, Mock.Of<ILogger<WatchFixAlertTool>>()),
            new WatchDismissAlertTool(am, Mock.Of<ILogger<WatchDismissAlertTool>>()),
            new WatchCreateRuleTool(w, Mock.Of<ILogger<WatchCreateRuleTool>>()),
            new WatchListRulesTool(w, Mock.Of<ILogger<WatchListRulesTool>>()),
            new WatchSuppressAlertsTool(w, Mock.Of<ILogger<WatchSuppressAlertsTool>>()),
            new WatchListSuppressionsTool(w, Mock.Of<ILogger<WatchListSuppressionsTool>>()),
            new WatchConfigureQuietHoursTool(w, Mock.Of<ILogger<WatchConfigureQuietHoursTool>>()),
            new WatchConfigureNotificationsTool(es, Mock.Of<ILogger<WatchConfigureNotificationsTool>>()),
            new WatchConfigureEscalationTool(es, Mock.Of<ILogger<WatchConfigureEscalationTool>>()),
            new WatchAlertHistoryTool(am, Mock.Of<ILogger<WatchAlertHistoryTool>>()),
            new WatchComplianceTrendTool(new InMemoryDbContextFactory(dbOptions("Trend")), Mock.Of<ILogger<WatchComplianceTrendTool>>()),
            new WatchAlertStatisticsTool(new InMemoryDbContextFactory(dbOptions("Stats")), Mock.Of<ILogger<WatchAlertStatisticsTool>>()),
            new WatchCreateTaskFromAlertTool(am, sf, Mock.Of<ILogger<WatchCreateTaskFromAlertTool>>()),
            new WatchCollectEvidenceFromAlertTool(am, new InMemoryDbContextFactory(dbOptions("Ev")), Mock.Of<ILogger<WatchCollectEvidenceFromAlertTool>>()),
            new WatchCreateAutoRemediationRuleTool(w, Mock.Of<ILogger<WatchCreateAutoRemediationRuleTool>>()),
            new WatchListAutoRemediationRulesTool(w, Mock.Of<ILogger<WatchListAutoRemediationRulesTool>>()),
            new NistControlSearchTool(n, Mock.Of<ILogger<NistControlSearchTool>>()),
            new NistControlExplainerTool(n, Mock.Of<ILogger<NistControlExplainerTool>>()),
            Enumerable.Empty<BaseTool>(),
            new InMemoryDbContextFactory(dbOptions("Main")),
            sf,
            Mock.Of<ISystemIdResolver>(),
            Mock.Of<ILogger<ComplianceAgent>>(),
            chatClient,
            null,
            aiOptions);
    }

    private KnowledgeBaseAgent CreateKnowledgeBaseAgent(IChatClient? chatClient, IOptions<AzureAiOptions>? aiOptions)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var opts = Options.Create(new KnowledgeBaseAgentOptions());
        var nist = Mock.Of<INistControlsService>();
        var stig = Mock.Of<IStigKnowledgeService>();
        var rmf = Mock.Of<IRmfKnowledgeService>();
        var dodI = Mock.Of<IDoDInstructionService>();
        var dodW = Mock.Of<IDoDWorkflowService>();
        var il = Mock.Of<IImpactLevelService>();
        var fr = Mock.Of<IFedRampTemplateService>();

        return new KnowledgeBaseAgent(
            opts, _stateManagerMock.Object,
            new ExplainNistControlTool(nist, cache, opts, Mock.Of<ILogger<ExplainNistControlTool>>()),
            new SearchNistControlsTool(nist, cache, opts, Mock.Of<ILogger<SearchNistControlsTool>>()),
            new ExplainStigTool(stig, cache, opts, Mock.Of<ILogger<ExplainStigTool>>()),
            new SearchStigsTool(stig, cache, opts, Mock.Of<ILogger<SearchStigsTool>>()),
            new ExplainRmfTool(rmf, dodI, dodW, cache, opts, Mock.Of<ILogger<ExplainRmfTool>>()),
            new ExplainImpactLevelTool(il, cache, opts, Mock.Of<ILogger<ExplainImpactLevelTool>>()),
            new GetFedRampTemplateGuidanceTool(fr, cache, opts, Mock.Of<ILogger<GetFedRampTemplateGuidanceTool>>()),
            Mock.Of<ILogger<KnowledgeBaseAgent>>(),
            chatClient,
            null,
            aiOptions);
    }

    private ComplianceMcpTools CreateComplianceMcpTools()
    {
        var e = Mock.Of<IAtoComplianceEngine>();
        var r = Mock.Of<IRemediationEngine>();
        var n = Mock.Of<INistControlsService>();
        var ev = Mock.Of<IEvidenceStorageService>();
        var m = Mock.Of<IComplianceMonitoringService>();
        var d = Mock.Of<IDocumentGenerationService>();
        var a = Mock.Of<IAssessmentAuditService>();
        var h = Mock.Of<IComplianceHistoryService>();
        var s = Mock.Of<IComplianceStatusService>();
        var w = Mock.Of<IComplianceWatchService>();
        var am = Mock.Of<IAlertManager>();
        var es = Mock.Of<IEscalationService>();
        var sf = _scopeFactory;

        var dbId = $"Mcp_{Guid.NewGuid()}";
        Func<string, DbContextOptions<AtoCopilotContext>> dbOptions = name =>
            new DbContextOptionsBuilder<AtoCopilotContext>()
                .UseInMemoryDatabase($"{dbId}_{name}").Options;

        return new ComplianceMcpTools(
            new ComplianceAssessmentTool(e, sf, Mock.Of<ILogger<ComplianceAssessmentTool>>()),
            new ControlFamilyTool(n, Mock.Of<ILogger<ControlFamilyTool>>()),
            new DocumentGenerationTool(d, Mock.Of<IDocumentTemplateService>(), sf, Mock.Of<ILogger<DocumentGenerationTool>>()),
            new EvidenceCollectionTool(ev, Mock.Of<ILogger<EvidenceCollectionTool>>()),
            new RemediationExecuteTool(r, Mock.Of<ILogger<RemediationExecuteTool>>()),
            new ValidateRemediationTool(r, Mock.Of<ILogger<ValidateRemediationTool>>()),
            new RemediationPlanTool(r, Mock.Of<ILogger<RemediationPlanTool>>()),
            new AssessmentAuditLogTool(a, Mock.Of<ILogger<AssessmentAuditLogTool>>()),
            new ComplianceHistoryTool(h, Mock.Of<ILogger<ComplianceHistoryTool>>()),
            new ComplianceStatusTool(s, Mock.Of<ILogger<ComplianceStatusTool>>()),
            new ComplianceMonitoringTool(m, Mock.Of<ILogger<ComplianceMonitoringTool>>()),
            new KanbanCreateBoardTool(sf, Mock.Of<ILogger<KanbanCreateBoardTool>>()),
            new KanbanBoardShowTool(sf, Mock.Of<ILogger<KanbanBoardShowTool>>()),
            new KanbanGetTaskTool(sf, Mock.Of<ILogger<KanbanGetTaskTool>>()),
            new KanbanCreateTaskTool(sf, Mock.Of<ILogger<KanbanCreateTaskTool>>()),
            new KanbanAssignTaskTool(sf, Mock.Of<ILogger<KanbanAssignTaskTool>>()),
            new KanbanMoveTaskTool(sf, Mock.Of<ILogger<KanbanMoveTaskTool>>()),
            new KanbanTaskListTool(sf, Mock.Of<ILogger<KanbanTaskListTool>>()),
            new KanbanTaskHistoryTool(sf, Mock.Of<ILogger<KanbanTaskHistoryTool>>()),
            new KanbanValidateTaskTool(sf, Mock.Of<ILogger<KanbanValidateTaskTool>>()),
            new KanbanAddCommentTool(sf, Mock.Of<ILogger<KanbanAddCommentTool>>()),
            new KanbanTaskCommentsTool(sf, Mock.Of<ILogger<KanbanTaskCommentsTool>>()),
            new KanbanEditCommentTool(sf, Mock.Of<ILogger<KanbanEditCommentTool>>()),
            new KanbanDeleteCommentTool(sf, Mock.Of<ILogger<KanbanDeleteCommentTool>>()),
            new KanbanRemediateTaskTool(sf, Mock.Of<ILogger<KanbanRemediateTaskTool>>()),
            new KanbanCollectEvidenceTool(sf, Mock.Of<ILogger<KanbanCollectEvidenceTool>>()),
            new KanbanBulkUpdateTool(sf, Mock.Of<ILogger<KanbanBulkUpdateTool>>()),
            new KanbanExportTool(sf, Mock.Of<ILogger<KanbanExportTool>>()),
            new KanbanArchiveBoardTool(sf, Mock.Of<ILogger<KanbanArchiveBoardTool>>()),
            new KanbanGenerateScriptTool(sf, Mock.Of<ILogger<KanbanGenerateScriptTool>>()),
            new KanbanGenerateValidationTool(sf, Mock.Of<ILogger<KanbanGenerateValidationTool>>()),
            new CacStatusTool(sf, Mock.Of<ILogger<CacStatusTool>>()),
            new CacSignOutTool(sf, Mock.Of<ILogger<CacSignOutTool>>()),
            new CacSetTimeoutTool(sf, Mock.Of<ILogger<CacSetTimeoutTool>>()),
            new CacMapCertificateTool(sf, Mock.Of<ILogger<CacMapCertificateTool>>()),
            new PimListEligibleTool(sf, Mock.Of<ILogger<PimListEligibleTool>>()),
            new PimActivateRoleTool(sf, Mock.Of<ILogger<PimActivateRoleTool>>()),
            new PimDeactivateRoleTool(sf, Mock.Of<ILogger<PimDeactivateRoleTool>>()),
            new PimListActiveTool(sf, Mock.Of<ILogger<PimListActiveTool>>()),
            new PimExtendRoleTool(sf, Mock.Of<ILogger<PimExtendRoleTool>>()),
            new PimApproveRequestTool(sf, Mock.Of<ILogger<PimApproveRequestTool>>()),
            new PimDenyRequestTool(sf, Mock.Of<ILogger<PimDenyRequestTool>>()),
            new JitRequestAccessTool(sf, Mock.Of<ILogger<JitRequestAccessTool>>()),
            new JitListSessionsTool(sf, Mock.Of<ILogger<JitListSessionsTool>>()),
            new JitRevokeAccessTool(sf, Mock.Of<ILogger<JitRevokeAccessTool>>()),
            new PimHistoryTool(sf, Mock.Of<ILogger<PimHistoryTool>>()),
            new WatchEnableMonitoringTool(w, Mock.Of<ILogger<WatchEnableMonitoringTool>>()),
            new WatchDisableMonitoringTool(w, Mock.Of<ILogger<WatchDisableMonitoringTool>>()),
            new WatchConfigureMonitoringTool(w, Mock.Of<ILogger<WatchConfigureMonitoringTool>>()),
            new WatchMonitoringStatusTool(w, Mock.Of<ILogger<WatchMonitoringStatusTool>>()),
            new WatchShowAlertsTool(am, Mock.Of<ILogger<WatchShowAlertsTool>>()),
            new WatchGetAlertTool(am, Mock.Of<ILogger<WatchGetAlertTool>>()),
            new WatchAcknowledgeAlertTool(am, Mock.Of<ILogger<WatchAcknowledgeAlertTool>>()),
            new WatchFixAlertTool(am, e, Mock.Of<ILogger<WatchFixAlertTool>>()),
            new WatchDismissAlertTool(am, Mock.Of<ILogger<WatchDismissAlertTool>>()),
            new WatchCreateRuleTool(w, Mock.Of<ILogger<WatchCreateRuleTool>>()),
            new WatchListRulesTool(w, Mock.Of<ILogger<WatchListRulesTool>>()),
            new WatchSuppressAlertsTool(w, Mock.Of<ILogger<WatchSuppressAlertsTool>>()),
            new WatchListSuppressionsTool(w, Mock.Of<ILogger<WatchListSuppressionsTool>>()),
            new WatchConfigureQuietHoursTool(w, Mock.Of<ILogger<WatchConfigureQuietHoursTool>>()),
            new WatchConfigureNotificationsTool(es, Mock.Of<ILogger<WatchConfigureNotificationsTool>>()),
            new WatchConfigureEscalationTool(es, Mock.Of<ILogger<WatchConfigureEscalationTool>>()),
            new WatchAlertHistoryTool(am, Mock.Of<ILogger<WatchAlertHistoryTool>>()),
            new WatchComplianceTrendTool(new InMemoryDbContextFactory(dbOptions("Trend")), Mock.Of<ILogger<WatchComplianceTrendTool>>()),
            new WatchAlertStatisticsTool(new InMemoryDbContextFactory(dbOptions("Stats")), Mock.Of<ILogger<WatchAlertStatisticsTool>>()),
            new WatchCreateTaskFromAlertTool(am, sf, Mock.Of<ILogger<WatchCreateTaskFromAlertTool>>()),
            new WatchCollectEvidenceFromAlertTool(am, new InMemoryDbContextFactory(dbOptions("Ev")), Mock.Of<ILogger<WatchCollectEvidenceFromAlertTool>>()),
            new WatchCreateAutoRemediationRuleTool(w, Mock.Of<ILogger<WatchCreateAutoRemediationRuleTool>>()),
            new WatchListAutoRemediationRulesTool(w, Mock.Of<ILogger<WatchListAutoRemediationRulesTool>>()),
            new NistControlSearchTool(n, Mock.Of<ILogger<NistControlSearchTool>>()),
            new NistControlExplainerTool(n, Mock.Of<ILogger<NistControlExplainerTool>>()),
            new IacComplianceScanTool(Mock.Of<ILogger<IacComplianceScanTool>>()),
            new RegisterSystemTool(Mock.Of<IRmfLifecycleService>(), Mock.Of<ILogger<RegisterSystemTool>>()),
            new ListSystemsTool(Mock.Of<IRmfLifecycleService>(), Mock.Of<ILogger<ListSystemsTool>>()),
            new GetSystemTool(Mock.Of<IRmfLifecycleService>(), Mock.Of<ILogger<GetSystemTool>>()),
            new AdvanceRmfStepTool(Mock.Of<IRmfLifecycleService>(), Mock.Of<ILogger<AdvanceRmfStepTool>>()),
            new DefineBoundaryTool(Mock.Of<IBoundaryService>(), sf, Mock.Of<ILogger<DefineBoundaryTool>>()),
            new ExcludeFromBoundaryTool(Mock.Of<IBoundaryService>(), Mock.Of<ILogger<ExcludeFromBoundaryTool>>()),
            new AssignRmfRoleTool(Mock.Of<IBoundaryService>(), Mock.Of<IProfileNotificationService>(), Mock.Of<ILogger<AssignRmfRoleTool>>()),
            new ListRmfRolesTool(Mock.Of<IBoundaryService>(), Mock.Of<ILogger<ListRmfRolesTool>>()),
            new ListBoundaryDefinitionsTool(sf, Mock.Of<ILogger<ListBoundaryDefinitionsTool>>()),
            new CreateBoundaryDefinitionTool(sf, Mock.Of<ILogger<CreateBoundaryDefinitionTool>>()),
            new DeleteBoundaryDefinitionTool(sf, Mock.Of<ILogger<DeleteBoundaryDefinitionTool>>()),
            new BoundaryGapAnalysisTool(sf, Mock.Of<ILogger<BoundaryGapAnalysisTool>>()),
            new CategorizeSystemTool(Mock.Of<ICategorizationService>(), Mock.Of<ILogger<CategorizeSystemTool>>()),
            new GetCategorizationTool(Mock.Of<ICategorizationService>(), Mock.Of<ILogger<GetCategorizationTool>>()),
            new SuggestInfoTypesTool(Mock.Of<ICategorizationService>(), Mock.Of<ILogger<SuggestInfoTypesTool>>()),
            new SelectBaselineTool(Mock.Of<IBaselineService>(), Mock.Of<ILogger<SelectBaselineTool>>()),
            new TailorBaselineTool(Mock.Of<IBaselineService>(), Mock.Of<ILogger<TailorBaselineTool>>()),
            new SetInheritanceTool(Mock.Of<IBaselineService>(), Mock.Of<ILogger<SetInheritanceTool>>()),
            new GetBaselineTool(Mock.Of<IBaselineService>(), Mock.Of<ILogger<GetBaselineTool>>()),
            new GenerateCrmTool(Mock.Of<IBaselineService>(), Mock.Of<ILogger<GenerateCrmTool>>()),
            new ShowStigMappingTool(Mock.Of<IStigKnowledgeService>(), Mock.Of<ILogger<ShowStigMappingTool>>()),
            new WriteNarrativeTool(Mock.Of<ISspService>(), Mock.Of<ILogger<WriteNarrativeTool>>()),
            new SuggestNarrativeTool(Mock.Of<ISspService>(), Mock.Of<ILogger<SuggestNarrativeTool>>()),
            new BatchPopulateNarrativesTool(Mock.Of<ISspService>(), Mock.Of<ILogger<BatchPopulateNarrativesTool>>()),
            new NarrativeProgressTool(Mock.Of<ISspService>(), Mock.Of<INarrativeGovernanceService>(), Mock.Of<ILogger<NarrativeProgressTool>>()),
            new GenerateSspTool(Mock.Of<ISspService>(), Mock.Of<ILogger<GenerateSspTool>>()),
            new AssessControlTool(Mock.Of<IAssessmentArtifactService>(), Mock.Of<ILogger<AssessControlTool>>()),
            new TakeSnapshotTool(Mock.Of<IAssessmentArtifactService>(), Mock.Of<ILogger<TakeSnapshotTool>>()),
            new CompareSnapshotsTool(Mock.Of<IAssessmentArtifactService>(), Mock.Of<ILogger<CompareSnapshotsTool>>()),
            new VerifyEvidenceTool(Mock.Of<IAssessmentArtifactService>(), Mock.Of<ILogger<VerifyEvidenceTool>>()),
            new CheckEvidenceCompletenessTool(Mock.Of<IAssessmentArtifactService>(), Mock.Of<ILogger<CheckEvidenceCompletenessTool>>()),
            new GenerateSarTool(Mock.Of<IAssessmentArtifactService>(), Mock.Of<ILogger<GenerateSarTool>>()),
            new IssueAuthorizationTool(Mock.Of<IAuthorizationService>(), Mock.Of<ILogger<IssueAuthorizationTool>>()),
            new AcceptRiskTool(Mock.Of<IDeviationService>(), Mock.Of<ILogger<AcceptRiskTool>>()),
            new ShowRiskRegisterTool(Mock.Of<IAuthorizationService>(), Mock.Of<ILogger<ShowRiskRegisterTool>>()),
            new CreatePoamTool(Mock.Of<IAuthorizationService>(), Mock.Of<ILogger<CreatePoamTool>>()),
            new ListPoamTool(Mock.Of<IAuthorizationService>(), Mock.Of<ILogger<ListPoamTool>>()),
            new GetPoamTool(sf, Mock.Of<ILogger<GetPoamTool>>()),
            new UpdatePoamTool(sf, Mock.Of<ILogger<UpdatePoamTool>>()),
            new ClosePoamTool(sf, Mock.Of<ILogger<ClosePoamTool>>()),
            new UpdatePoamMilestoneTool(sf, Mock.Of<ILogger<UpdatePoamMilestoneTool>>()),
            new BulkUpdatePoamTool(sf, Mock.Of<ILogger<BulkUpdatePoamTool>>()),
            new LinkPoamTaskTool(sf, Mock.Of<ILogger<LinkPoamTaskTool>>()),
            new UnlinkPoamTaskTool(sf, Mock.Of<ILogger<UnlinkPoamTaskTool>>()),
            new CreateTaskFromPoamTool(sf, Mock.Of<ILogger<CreateTaskFromPoamTool>>()),
            new GenerateRarTool(Mock.Of<IAuthorizationService>(), Mock.Of<ILogger<GenerateRarTool>>()),
            new BundleAuthorizationPackageTool(Mock.Of<IAuthorizationService>(), Mock.Of<ILogger<BundleAuthorizationPackageTool>>()),
            // US9: Continuous Monitoring tools
            new CreateConMonPlanTool(Mock.Of<IConMonService>(), Mock.Of<ILogger<CreateConMonPlanTool>>()),
            new GenerateConMonReportTool(Mock.Of<IConMonService>(), Mock.Of<ILogger<GenerateConMonReportTool>>()),
            new ReportSignificantChangeTool(Mock.Of<IConMonService>(), Mock.Of<ILogger<ReportSignificantChangeTool>>()),
            new TrackAtoExpirationTool(Mock.Of<IConMonService>(), Mock.Of<ILogger<TrackAtoExpirationTool>>()),
            new MultiSystemDashboardTool(Mock.Of<IConMonService>(), Mock.Of<ILogger<MultiSystemDashboardTool>>()),
            new ReauthorizationWorkflowTool(Mock.Of<IConMonService>(), Mock.Of<ILogger<ReauthorizationWorkflowTool>>()),
            new NotificationDeliveryTool(Mock.Of<IConMonService>(), Mock.Of<ILogger<NotificationDeliveryTool>>()),
            // US10: eMASS & OSCAL tools
            new ExportEmassTool(Mock.Of<IEmassExportService>(), Mock.Of<ILogger<ExportEmassTool>>()),
            new ImportEmassTool(Mock.Of<IEmassExportService>(), Mock.Of<ILogger<ImportEmassTool>>()),
            new ExportOscalTool(Mock.Of<IEmassExportService>(), Mock.Of<IOscalSapExportService>(), Mock.Of<ILogger<ExportOscalTool>>()),
            // US11: Document Templates & PDF Export tools
            new UploadTemplateTool(Mock.Of<IDocumentTemplateService>(), Mock.Of<ILogger<UploadTemplateTool>>()),
            new ListTemplatesTool(Mock.Of<IDocumentTemplateService>(), Mock.Of<ILogger<ListTemplatesTool>>()),
            new UpdateTemplateTool(Mock.Of<IDocumentTemplateService>(), Mock.Of<ILogger<UpdateTemplateTool>>()),
            new DeleteTemplateTool(Mock.Of<IDocumentTemplateService>(), Mock.Of<ILogger<DeleteTemplateTool>>()),
            // Feature 017: SCAP/STIG Import tools
            new ImportCklTool(Mock.Of<IScanImportService>(), sf, Mock.Of<ILogger<ImportCklTool>>()),
            new ImportXccdfTool(Mock.Of<IScanImportService>(), Mock.Of<ILogger<ImportXccdfTool>>()),
            new ExportCklTool(Mock.Of<IScanImportService>(), Mock.Of<ILogger<ExportCklTool>>()),
            new ListImportsTool(Mock.Of<IScanImportService>(), Mock.Of<ILogger<ListImportsTool>>()),
            new GetImportSummaryTool(Mock.Of<IScanImportService>(), Mock.Of<ILogger<GetImportSummaryTool>>()),
            new GenerateSapTool(Mock.Of<ISapService>(), Mock.Of<ILogger<GenerateSapTool>>()),
            new UpdateSapTool(Mock.Of<ISapService>(), Mock.Of<ILogger<UpdateSapTool>>()),
            new FinalizeSapTool(Mock.Of<ISapService>(), Mock.Of<ILogger<FinalizeSapTool>>()),
            new GetSapTool(Mock.Of<ISapService>(), Mock.Of<ILogger<GetSapTool>>()),
            new ListSapsTool(Mock.Of<ISapService>(), Mock.Of<ILogger<ListSapsTool>>()),
            // Feature 019: Prisma Cloud Import tools
            new ImportPrismaCsvTool(Mock.Of<IScanImportService>(), sf, Mock.Of<ILogger<ImportPrismaCsvTool>>()),
            new ImportPrismaApiTool(Mock.Of<IScanImportService>(), sf, Mock.Of<ILogger<ImportPrismaApiTool>>()),
            new ListPrismaPoliciesTool(Mock.Of<IScanImportService>(), Mock.Of<ILogger<ListPrismaPoliciesTool>>()),
            new PrismaTrendTool(Mock.Of<IScanImportService>(), Mock.Of<ILogger<PrismaTrendTool>>()));
    }

    private KnowledgeBaseMcpTools CreateKnowledgeBaseMcpTools()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var opts = Options.Create(new KnowledgeBaseAgentOptions());
        var nist = Mock.Of<INistControlsService>();
        var stig = Mock.Of<IStigKnowledgeService>();
        var rmf = Mock.Of<IRmfKnowledgeService>();
        var dodI = Mock.Of<IDoDInstructionService>();
        var dodW = Mock.Of<IDoDWorkflowService>();
        var il = Mock.Of<IImpactLevelService>();
        var fr = Mock.Of<IFedRampTemplateService>();

        return new KnowledgeBaseMcpTools(
            new ExplainNistControlTool(nist, cache, opts, Mock.Of<ILogger<ExplainNistControlTool>>()),
            new SearchNistControlsTool(nist, cache, opts, Mock.Of<ILogger<SearchNistControlsTool>>()),
            new ExplainStigTool(stig, cache, opts, Mock.Of<ILogger<ExplainStigTool>>()),
            new SearchStigsTool(stig, cache, opts, Mock.Of<ILogger<SearchStigsTool>>()),
            new ExplainRmfTool(rmf, dodI, dodW, cache, opts, Mock.Of<ILogger<ExplainRmfTool>>()),
            new ExplainImpactLevelTool(il, cache, opts, Mock.Of<ILogger<ExplainImpactLevelTool>>()),
            new GetFedRampTemplateGuidanceTool(fr, cache, opts, Mock.Of<ILogger<GetFedRampTemplateGuidanceTool>>()));
    }

    private class InMemoryDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public InMemoryDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
        public Task<AtoCopilotContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

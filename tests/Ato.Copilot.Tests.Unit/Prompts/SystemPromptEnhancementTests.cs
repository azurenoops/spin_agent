using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Compliance.Agents;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Agents.Configuration.Agents;
using Ato.Copilot.Agents.Configuration.Tools;
using Ato.Copilot.Agents.KnowledgeBase.Agents;
using Ato.Copilot.Agents.KnowledgeBase.Configuration;
using Ato.Copilot.Agents.KnowledgeBase.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.State.Abstractions;

namespace Ato.Copilot.Tests.Unit.Prompts;

/// <summary>
/// Verifies that all 5 agent system prompts contain the "Response Guidelines" and
/// "Tool Selection" sections added by Feature 011 (US4), and that original content is preserved.
/// </summary>
public class SystemPromptEnhancementTests
{
    // ── ComplianceAgent ──────────────────────────────────────────────────────

    [Fact]
    public void ComplianceAgent_GetSystemPrompt_ReturnsNonEmpty()
    {
        var agent = CreateComplianceAgent();
        agent.GetSystemPrompt().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ComplianceAgent_Prompt_ContainsResponseGuidelines()
    {
        var prompt = CreateComplianceAgent().GetSystemPrompt();
        prompt.Should().Contain("Response Format");
    }

    [Fact]
    public void ComplianceAgent_Prompt_ContainsToolSelection()
    {
        var prompt = CreateComplianceAgent().GetSystemPrompt();
        prompt.Should().Contain("Tool Selection");
    }

    [Fact]
    public void ComplianceAgent_Prompt_PreservesOriginalContent()
    {
        var prompt = CreateComplianceAgent().GetSystemPrompt();
        prompt.Should().Contain("Security Posture Intelligence Navigator");
        prompt.Should().Contain("compliance_assess");
        prompt.Should().Contain("RMF Workflow Guidance");
    }

    // ── ConfigurationAgent ───────────────────────────────────────────────────

    [Fact]
    public void ConfigurationAgent_GetSystemPrompt_ReturnsNonEmpty()
    {
        var agent = CreateConfigurationAgent();
        agent.GetSystemPrompt().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ConfigurationAgent_Prompt_ContainsResponseGuidelines()
    {
        var prompt = CreateConfigurationAgent().GetSystemPrompt();
        prompt.Should().Contain("Response Guidelines");
    }

    [Fact]
    public void ConfigurationAgent_Prompt_ContainsToolSelection()
    {
        var prompt = CreateConfigurationAgent().GetSystemPrompt();
        prompt.Should().Contain("Tool Selection");
    }

    [Fact]
    public void ConfigurationAgent_Prompt_PreservesOriginalContent()
    {
        var prompt = CreateConfigurationAgent().GetSystemPrompt();
        prompt.Should().Contain("Configuration Agent");
        prompt.Should().Contain("configuration_manage");
        prompt.Should().Contain("Behavior Rules");
    }

    // ── KnowledgeBaseAgent ───────────────────────────────────────────────────

    [Fact]
    public void KnowledgeBaseAgent_GetSystemPrompt_ReturnsNonEmpty()
    {
        var agent = CreateKnowledgeBaseAgent();
        agent.GetSystemPrompt().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void KnowledgeBaseAgent_Prompt_ContainsResponseGuidelines()
    {
        var prompt = CreateKnowledgeBaseAgent().GetSystemPrompt();
        prompt.Should().Contain("Response Guidelines");
    }

    [Fact]
    public void KnowledgeBaseAgent_Prompt_ContainsToolSelection()
    {
        var prompt = CreateKnowledgeBaseAgent().GetSystemPrompt();
        prompt.Should().Contain("Tool Selection");
    }

    [Fact]
    public void KnowledgeBaseAgent_Prompt_PreservesOriginalContent()
    {
        var prompt = CreateKnowledgeBaseAgent().GetSystemPrompt();
        prompt.Should().Contain("KnowledgeBase Agent");
        prompt.Should().Contain("Strict Boundaries");
        prompt.Should().Contain("Response Quality");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ComplianceAgent CreateComplianceAgent()
    {
        var sf = Mock.Of<IServiceScopeFactory>();
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

        var dbId = $"Prompt_{Guid.NewGuid()}";
        Func<string, DbContextOptions<AtoCopilotContext>> dbOpts = name =>
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
            new WatchComplianceTrendTool(new InMemoryDbContextFactory(dbOpts("Trend")), Mock.Of<ILogger<WatchComplianceTrendTool>>()),
            new WatchAlertStatisticsTool(new InMemoryDbContextFactory(dbOpts("Stats")), Mock.Of<ILogger<WatchAlertStatisticsTool>>()),
            new WatchCreateTaskFromAlertTool(am, sf, Mock.Of<ILogger<WatchCreateTaskFromAlertTool>>()),
            new WatchCollectEvidenceFromAlertTool(am, new InMemoryDbContextFactory(dbOpts("Ev")), Mock.Of<ILogger<WatchCollectEvidenceFromAlertTool>>()),
            new WatchCreateAutoRemediationRuleTool(w, Mock.Of<ILogger<WatchCreateAutoRemediationRuleTool>>()),
            new WatchListAutoRemediationRulesTool(w, Mock.Of<ILogger<WatchListAutoRemediationRulesTool>>()),
            new NistControlSearchTool(n, Mock.Of<ILogger<NistControlSearchTool>>()),
            new NistControlExplainerTool(n, Mock.Of<ILogger<NistControlExplainerTool>>()),
            Enumerable.Empty<BaseTool>(),
            new InMemoryDbContextFactory(dbOpts("Main")),
            sf,
            Mock.Of<ISystemIdResolver>(),
            Mock.Of<ILogger<ComplianceAgent>>());
    }

    private static ConfigurationAgent CreateConfigurationAgent()
    {
        var stateMock = new Mock<IAgentStateManager>();
        stateMock.Setup(s => s.GetStateAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        stateMock.Setup(s => s.GetStateAsync<ConfigurationSettings>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConfigurationSettings?)null);

        var tool = new ConfigurationTool(stateMock.Object, Mock.Of<ILogger<ConfigurationTool>>());
        return new ConfigurationAgent(tool, Mock.Of<ILogger<ConfigurationAgent>>());
    }

    private static KnowledgeBaseAgent CreateKnowledgeBaseAgent()
    {
        var options = Options.Create(new KnowledgeBaseAgentOptions());
        var stateManager = Mock.Of<IAgentStateManager>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var nist = Mock.Of<INistControlsService>();
        var stig = Mock.Of<IStigKnowledgeService>();
        var rmf = Mock.Of<IRmfKnowledgeService>();
        var dodI = Mock.Of<IDoDInstructionService>();
        var dodW = Mock.Of<IDoDWorkflowService>();
        var il = Mock.Of<IImpactLevelService>();
        var fr = Mock.Of<IFedRampTemplateService>();

        return new KnowledgeBaseAgent(
            options, stateManager,
            new ExplainNistControlTool(nist, cache, options, Mock.Of<ILogger<ExplainNistControlTool>>()),
            new SearchNistControlsTool(nist, cache, options, Mock.Of<ILogger<SearchNistControlsTool>>()),
            new ExplainStigTool(stig, cache, options, Mock.Of<ILogger<ExplainStigTool>>()),
            new SearchStigsTool(stig, cache, options, Mock.Of<ILogger<SearchStigsTool>>()),
            new ExplainRmfTool(rmf, dodI, dodW, cache, options, Mock.Of<ILogger<ExplainRmfTool>>()),
            new ExplainImpactLevelTool(il, cache, options, Mock.Of<ILogger<ExplainImpactLevelTool>>()),
            new GetFedRampTemplateGuidanceTool(fr, cache, options, Mock.Of<ILogger<GetFedRampTemplateGuidanceTool>>()),
            Mock.Of<ILogger<KnowledgeBaseAgent>>());
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

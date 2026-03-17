using Microsoft.Extensions.Logging;
using Moq;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Compliance.Agents;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Mcp.Server;

namespace Ato.Copilot.Tests.Unit;

/// <summary>
/// Shared factory methods for constructing mocks of types with large constructor signatures.
/// Avoids duplicating 74+ null constructor args across multiple test files.
/// </summary>
internal static class TestMockFactory
{
    /// <summary>
    /// Creates a <see cref="Mock{ComplianceAgent}"/> with all 74 required constructor params
    /// supplied. The ILogger is positioned at index 73 (the correct constructor position).
    /// All virtual members (AgentId, AgentName, CanHandle, ProcessAsync) are pre-configured.
    /// </summary>
    public static Mock<ComplianceAgent> CreateComplianceAgentMock()
    {
        // ComplianceAgent constructor: 76 required params (72 tools + dbFactory + scopeFactory + systemIdResolver + logger)
        // plus 2 optional (chatClient, aiOptions). See ComplianceAgent.cs lines 136-216.
        var mock = new Mock<ComplianceAgent>(MockBehavior.Loose,
            /* 01  assessmentTool          */ null!,
            /* 02  controlFamilyTool       */ null!,
            /* 03  documentGenerationTool  */ null!,
            /* 04  evidenceCollectionTool  */ null!,
            /* 05  remediationTool         */ null!,
            /* 06  validateRemediationTool */ null!,
            /* 07  remediationPlanTool     */ null!,
            /* 08  auditLogTool            */ null!,
            /* 09  historyTool             */ null!,
            /* 10  statusTool              */ null!,
            /* 11  monitoringTool          */ null!,
            /* 12  kanbanCreateBoard       */ null!,
            /* 13  kanbanBoardShow         */ null!,
            /* 14  kanbanGetTask           */ null!,
            /* 15  kanbanCreateTask        */ null!,
            /* 16  kanbanAssignTask        */ null!,
            /* 17  kanbanMoveTask          */ null!,
            /* 18  kanbanTaskList          */ null!,
            /* 19  kanbanTaskHistory       */ null!,
            /* 20  kanbanValidateTask      */ null!,
            /* 21  kanbanAddComment        */ null!,
            /* 22  kanbanTaskComments      */ null!,
            /* 23  kanbanEditComment       */ null!,
            /* 24  kanbanDeleteComment     */ null!,
            /* 25  kanbanRemediateTask     */ null!,
            /* 26  kanbanCollectEvidence   */ null!,
            /* 27  kanbanBulkUpdate        */ null!,
            /* 28  kanbanExport            */ null!,
            /* 29  kanbanArchiveBoard      */ null!,
            /* 30  kanbanGenerateScript    */ null!,
            /* 31  kanbanGenerateValidation*/ null!,
            /* 32  cacStatus               */ null!,
            /* 33  cacSignOut              */ null!,
            /* 34  cacSetTimeout           */ null!,
            /* 35  cacMapCertificate       */ null!,
            /* 36  pimListEligible         */ null!,
            /* 37  pimActivateRole         */ null!,
            /* 38  pimDeactivateRole       */ null!,
            /* 39  pimListActive           */ null!,
            /* 40  pimExtendRole           */ null!,
            /* 41  pimApproveRequest       */ null!,
            /* 42  pimDenyRequest          */ null!,
            /* 43  jitRequestAccess        */ null!,
            /* 44  jitListSessions         */ null!,
            /* 45  jitRevokeAccess         */ null!,
            /* 46  pimHistory              */ null!,
            /* 47  watchEnableMonitoring   */ null!,
            /* 48  watchDisableMonitoring  */ null!,
            /* 49  watchConfigureMonitoring*/ null!,
            /* 50  watchMonitoringStatus   */ null!,
            /* 51  watchShowAlerts         */ null!,
            /* 52  watchGetAlert           */ null!,
            /* 53  watchAcknowledgeAlert   */ null!,
            /* 54  watchFixAlert           */ null!,
            /* 55  watchDismissAlert       */ null!,
            /* 56  watchCreateRule         */ null!,
            /* 57  watchListRules          */ null!,
            /* 58  watchSuppressAlerts     */ null!,
            /* 59  watchListSuppressions   */ null!,
            /* 60  watchConfigureQuietHours*/ null!,
            /* 61  watchConfigureNotif     */ null!,
            /* 62  watchConfigureEscalation*/ null!,
            /* 63  watchAlertHistory       */ null!,
            /* 64  watchComplianceTrend    */ null!,
            /* 65  watchAlertStatistics    */ null!,
            /* 66  watchCreateTaskFromAlert*/ null!,
            /* 67  watchCollectEvidence    */ null!,
            /* 68  watchCreateAutoRemed    */ null!,
            /* 69  watchListAutoRemed      */ null!,
            /* 70  nistControlSearch       */ null!,
            /* 71  nistControlExplainer    */ null!,
            /* 72  allRegisteredTools      */ Enumerable.Empty<BaseTool>(),
            /* 73  dbFactory               */ null!,
            /* 74  scopeFactory            */ null!,
            /* 75  systemIdResolver        */ Mock.Of<ISystemIdResolver>(),
            /* 76  logger                  */ Mock.Of<ILogger<ComplianceAgent>>(),
            /* 77  chatClient (optional)   */ (object?)null,
            /* 78  foundryClient (optional)*/ (object?)null,
            /* 79  azureAiOptions (optional)*/ (object?)null
        );

        mock.Setup(a => a.AgentId).Returns("compliance-agent");
        mock.Setup(a => a.AgentName).Returns("Compliance Agent");
        mock.Setup(a => a.CanHandle(It.IsAny<string>())).Returns(0.9);
        mock.Setup(a => a.ProcessAsync(
                It.IsAny<string>(), It.IsAny<AgentConversationContext>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<string>>()))
            .ReturnsAsync(new AgentResponse
            {
                Success = true,
                Response = "done",
                AgentName = "Compliance Agent"
            });

        return mock;
    }

    /// <summary>
    /// Creates a <see cref="StubOrchestrator"/> that returns a preconfigured agent.
    /// </summary>
    public static StubOrchestrator CreateOrchestrator(BaseAgent? selectedAgent = null)
    {
        var orchestrator = new StubOrchestrator();
        if (selectedAgent != null)
            orchestrator.SetSelectedAgent(selectedAgent);
        return orchestrator;
    }
}

/// <summary>
/// Lightweight test double for <see cref="AgentOrchestrator"/> that avoids
/// Castle DynamicProxy constructor-matching issues.
/// </summary>
internal class StubOrchestrator : AgentOrchestrator
{
    private BaseAgent? _selectedAgent;

    public StubOrchestrator()
        : base(new List<BaseAgent>(), new LoggerFactory().CreateLogger<AgentOrchestrator>())
    {
    }

    public void SetSelectedAgent(BaseAgent? agent) => _selectedAgent = agent;

    public override BaseAgent? SelectAgent(string message, IDictionary<string, object?>? context = null) => _selectedAgent;
}

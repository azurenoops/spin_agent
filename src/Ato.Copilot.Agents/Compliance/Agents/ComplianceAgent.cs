using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Azure.AI.Agents.Persistent;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Agents;

/// <summary>
/// Compliance Agent - handles all NIST 800-53, FedRAMP, and ATO compliance operations.
/// Extends BaseAgent per Constitution Principle II.
/// </summary>
public class ComplianceAgent : BaseAgent
{
    // ─── RMF Step-Aware Routing (US12) ──────────────────────────────────────
    // AsyncLocal because the agent is a singleton — concurrent callers each
    // get their own copy of the current step for GetSystemPrompt().
    private static readonly AsyncLocal<RmfPhase?> _activeRmfStep = new();
    private static readonly AsyncLocal<string?> _activeSystemName = new();

    private readonly ComplianceAssessmentTool _assessmentTool;
    private readonly ControlFamilyTool _controlFamilyTool;
    private readonly DocumentGenerationTool _documentGenerationTool;
    private readonly EvidenceCollectionTool _evidenceCollectionTool;
    private readonly RemediationExecuteTool _remediationTool;
    private readonly ValidateRemediationTool _validateRemediationTool;
    private readonly RemediationPlanTool _remediationPlanTool;
    private readonly AssessmentAuditLogTool _auditLogTool;
    private readonly ComplianceHistoryTool _historyTool;
    private readonly ComplianceStatusTool _statusTool;
    private readonly ComplianceMonitoringTool _monitoringTool;
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISystemIdResolver _systemIdResolver;

    // Kanban tools (Phase 3–6)
    private readonly KanbanCreateBoardTool _kanbanCreateBoard;
    private readonly KanbanBoardShowTool _kanbanBoardShow;
    private readonly KanbanGetTaskTool _kanbanGetTask;
    private readonly KanbanCreateTaskTool _kanbanCreateTask;
    private readonly KanbanAssignTaskTool _kanbanAssignTask;
    private readonly KanbanMoveTaskTool _kanbanMoveTask;
    private readonly KanbanTaskListTool _kanbanTaskList;
    private readonly KanbanTaskHistoryTool _kanbanTaskHistory;
    private readonly KanbanValidateTaskTool _kanbanValidateTask;
    private readonly KanbanAddCommentTool _kanbanAddComment;
    private readonly KanbanTaskCommentsTool _kanbanTaskComments;
    private readonly KanbanEditCommentTool _kanbanEditComment;
    private readonly KanbanDeleteCommentTool _kanbanDeleteComment;
    private readonly KanbanRemediateTaskTool _kanbanRemediateTask;
    private readonly KanbanCollectEvidenceTool _kanbanCollectEvidence;
    private readonly KanbanBulkUpdateTool _kanbanBulkUpdate;
    private readonly KanbanExportTool _kanbanExport;
    private readonly KanbanArchiveBoardTool _kanbanArchiveBoard;
    private readonly KanbanGenerateScriptTool _kanbanGenerateScript;
    private readonly KanbanGenerateValidationTool _kanbanGenerateValidation;

    // Auth/PIM tools (Phase 3 — US1)
    private readonly CacStatusTool _cacStatus;
    private readonly CacSignOutTool _cacSignOut;

    // CAC session config (Phase 10 — US8)
    private readonly CacSetTimeoutTool _cacSetTimeout;

    // Certificate mapping (Phase 11 — US9)
    private readonly CacMapCertificateTool _cacMapCertificate;

    // PIM tools (Phase 5 — US3)
    private readonly PimListEligibleTool _pimListEligible;
    private readonly PimActivateRoleTool _pimActivateRole;
    private readonly PimDeactivateRoleTool _pimDeactivateRole;

    // PIM session management tools (Phase 6 — US4)
    private readonly PimListActiveTool _pimListActive;
    private readonly PimExtendRoleTool _pimExtendRole;

    // PIM approval workflow tools (Phase 7 — US5)
    private readonly PimApproveRequestTool _pimApproveRequest;
    private readonly PimDenyRequestTool _pimDenyRequest;

    // JIT VM access tools (Phase 9 — US7)
    private readonly JitRequestAccessTool _jitRequestAccess;
    private readonly JitListSessionsTool _jitListSessions;
    private readonly JitRevokeAccessTool _jitRevokeAccess;

    // PIM audit trail (Phase 12 — US10)
    private readonly PimHistoryTool _pimHistory;

    // Compliance Watch monitoring tools (Feature 005)
    private readonly WatchEnableMonitoringTool _watchEnableMonitoring;
    private readonly WatchDisableMonitoringTool _watchDisableMonitoring;
    private readonly WatchConfigureMonitoringTool _watchConfigureMonitoring;
    private readonly WatchMonitoringStatusTool _watchMonitoringStatus;

    // Compliance Watch alert lifecycle tools (Feature 005 — US2)
    private readonly WatchShowAlertsTool _watchShowAlerts;
    private readonly WatchGetAlertTool _watchGetAlert;
    private readonly WatchAcknowledgeAlertTool _watchAcknowledgeAlert;
    private readonly WatchFixAlertTool _watchFixAlert;
    private readonly WatchDismissAlertTool _watchDismissAlert;

    // Compliance Watch alert rules & suppression tools (Feature 005 — US3)
    private readonly WatchCreateRuleTool _watchCreateRule;
    private readonly WatchListRulesTool _watchListRules;
    private readonly WatchSuppressAlertsTool _watchSuppressAlerts;
    private readonly WatchListSuppressionsTool _watchListSuppressions;
    private readonly WatchConfigureQuietHoursTool _watchConfigureQuietHours;

    // Compliance Watch notification & escalation tools (US4)
    private readonly WatchConfigureNotificationsTool _watchConfigureNotifications;
    private readonly WatchConfigureEscalationTool _watchConfigureEscalation;

    // Compliance Watch dashboard & reporting tools (US5)
    private readonly WatchAlertHistoryTool _watchAlertHistory;
    private readonly WatchComplianceTrendTool _watchComplianceTrend;
    private readonly WatchAlertStatisticsTool _watchAlertStatistics;

    // Compliance Watch integration tools (US8)
    private readonly WatchCreateTaskFromAlertTool _watchCreateTaskFromAlert;
    private readonly WatchCollectEvidenceFromAlertTool _watchCollectEvidenceFromAlert;

    // Compliance Watch auto-remediation tools (US9)
    private readonly WatchCreateAutoRemediationRuleTool _watchCreateAutoRemediationRule;
    private readonly WatchListAutoRemediationRulesTool _watchListAutoRemediationRules;

    // NIST Controls knowledge tools (Feature 007)
    private readonly NistControlSearchTool _nistControlSearchTool;
    private readonly NistControlExplainerTool _nistControlExplainerTool;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplianceAgent"/> class.
    /// </summary>
    public ComplianceAgent(
        ComplianceAssessmentTool assessmentTool,
        ControlFamilyTool controlFamilyTool,
        DocumentGenerationTool documentGenerationTool,
        EvidenceCollectionTool evidenceCollectionTool,
        RemediationExecuteTool remediationTool,
        ValidateRemediationTool validateRemediationTool,
        RemediationPlanTool remediationPlanTool,
        AssessmentAuditLogTool auditLogTool,
        ComplianceHistoryTool historyTool,
        ComplianceStatusTool statusTool,
        ComplianceMonitoringTool monitoringTool,
        KanbanCreateBoardTool kanbanCreateBoard,
        KanbanBoardShowTool kanbanBoardShow,
        KanbanGetTaskTool kanbanGetTask,
        KanbanCreateTaskTool kanbanCreateTask,
        KanbanAssignTaskTool kanbanAssignTask,
        KanbanMoveTaskTool kanbanMoveTask,
        KanbanTaskListTool kanbanTaskList,
        KanbanTaskHistoryTool kanbanTaskHistory,
        KanbanValidateTaskTool kanbanValidateTask,
        KanbanAddCommentTool kanbanAddComment,
        KanbanTaskCommentsTool kanbanTaskComments,
        KanbanEditCommentTool kanbanEditComment,
        KanbanDeleteCommentTool kanbanDeleteComment,
        KanbanRemediateTaskTool kanbanRemediateTask,
        KanbanCollectEvidenceTool kanbanCollectEvidence,
        KanbanBulkUpdateTool kanbanBulkUpdate,
        KanbanExportTool kanbanExport,
        KanbanArchiveBoardTool kanbanArchiveBoard,
        KanbanGenerateScriptTool kanbanGenerateScript,
        KanbanGenerateValidationTool kanbanGenerateValidation,
        CacStatusTool cacStatus,
        CacSignOutTool cacSignOut,
        CacSetTimeoutTool cacSetTimeout,
        CacMapCertificateTool cacMapCertificate,
        PimListEligibleTool pimListEligible,
        PimActivateRoleTool pimActivateRole,
        PimDeactivateRoleTool pimDeactivateRole,
        PimListActiveTool pimListActive,
        PimExtendRoleTool pimExtendRole,
        PimApproveRequestTool pimApproveRequest,
        PimDenyRequestTool pimDenyRequest,
        JitRequestAccessTool jitRequestAccess,
        JitListSessionsTool jitListSessions,
        JitRevokeAccessTool jitRevokeAccess,
        PimHistoryTool pimHistory,
        WatchEnableMonitoringTool watchEnableMonitoring,
        WatchDisableMonitoringTool watchDisableMonitoring,
        WatchConfigureMonitoringTool watchConfigureMonitoring,
        WatchMonitoringStatusTool watchMonitoringStatus,
        WatchShowAlertsTool watchShowAlerts,
        WatchGetAlertTool watchGetAlert,
        WatchAcknowledgeAlertTool watchAcknowledgeAlert,
        WatchFixAlertTool watchFixAlert,
        WatchDismissAlertTool watchDismissAlert,
        WatchCreateRuleTool watchCreateRule,
        WatchListRulesTool watchListRules,
        WatchSuppressAlertsTool watchSuppressAlerts,
        WatchListSuppressionsTool watchListSuppressions,
        WatchConfigureQuietHoursTool watchConfigureQuietHours,
        WatchConfigureNotificationsTool watchConfigureNotifications,
        WatchConfigureEscalationTool watchConfigureEscalation,
        WatchAlertHistoryTool watchAlertHistory,
        WatchComplianceTrendTool watchComplianceTrend,
        WatchAlertStatisticsTool watchAlertStatistics,
        WatchCreateTaskFromAlertTool watchCreateTaskFromAlert,
        WatchCollectEvidenceFromAlertTool watchCollectEvidenceFromAlert,
        WatchCreateAutoRemediationRuleTool watchCreateAutoRemediationRule,
        WatchListAutoRemediationRulesTool watchListAutoRemediationRules,
        NistControlSearchTool nistControlSearchTool,
        NistControlExplainerTool nistControlExplainerTool,
        IEnumerable<BaseTool> allRegisteredTools,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IServiceScopeFactory scopeFactory,
        ISystemIdResolver systemIdResolver,
        ILogger<ComplianceAgent> logger,
        IChatClient? chatClient = null,
        PersistentAgentsClient? foundryClient = null,
        IOptions<AzureAiOptions>? azureAiOptions = null)
        : base(logger, chatClient, foundryClient, azureAiOptions?.Value)
    {
        _systemIdResolver = systemIdResolver;
        _assessmentTool = assessmentTool;
        _controlFamilyTool = controlFamilyTool;
        _documentGenerationTool = documentGenerationTool;
        _evidenceCollectionTool = evidenceCollectionTool;
        _remediationTool = remediationTool;
        _validateRemediationTool = validateRemediationTool;
        _remediationPlanTool = remediationPlanTool;
        _auditLogTool = auditLogTool;
        _historyTool = historyTool;
        _statusTool = statusTool;
        _monitoringTool = monitoringTool;
        _kanbanCreateBoard = kanbanCreateBoard;
        _kanbanBoardShow = kanbanBoardShow;
        _kanbanGetTask = kanbanGetTask;
        _kanbanCreateTask = kanbanCreateTask;
        _kanbanAssignTask = kanbanAssignTask;
        _kanbanMoveTask = kanbanMoveTask;
        _kanbanTaskList = kanbanTaskList;
        _kanbanTaskHistory = kanbanTaskHistory;
        _kanbanValidateTask = kanbanValidateTask;
        _kanbanAddComment = kanbanAddComment;
        _kanbanTaskComments = kanbanTaskComments;
        _kanbanEditComment = kanbanEditComment;
        _kanbanDeleteComment = kanbanDeleteComment;
        _kanbanRemediateTask = kanbanRemediateTask;
        _kanbanCollectEvidence = kanbanCollectEvidence;
        _kanbanBulkUpdate = kanbanBulkUpdate;
        _kanbanExport = kanbanExport;
        _kanbanArchiveBoard = kanbanArchiveBoard;
        _kanbanGenerateScript = kanbanGenerateScript;
        _kanbanGenerateValidation = kanbanGenerateValidation;
        _cacStatus = cacStatus;
        _cacSignOut = cacSignOut;
        _cacSetTimeout = cacSetTimeout;
        _cacMapCertificate = cacMapCertificate;
        _pimListEligible = pimListEligible;
        _pimActivateRole = pimActivateRole;
        _pimDeactivateRole = pimDeactivateRole;
        _pimListActive = pimListActive;
        _pimExtendRole = pimExtendRole;
        _pimApproveRequest = pimApproveRequest;
        _pimDenyRequest = pimDenyRequest;
        _jitRequestAccess = jitRequestAccess;
        _jitListSessions = jitListSessions;
        _jitRevokeAccess = jitRevokeAccess;
        _pimHistory = pimHistory;
        _watchEnableMonitoring = watchEnableMonitoring;
        _watchDisableMonitoring = watchDisableMonitoring;
        _watchConfigureMonitoring = watchConfigureMonitoring;
        _watchMonitoringStatus = watchMonitoringStatus;
        _watchShowAlerts = watchShowAlerts;
        _watchGetAlert = watchGetAlert;
        _watchAcknowledgeAlert = watchAcknowledgeAlert;
        _watchFixAlert = watchFixAlert;
        _watchDismissAlert = watchDismissAlert;
        _watchCreateRule = watchCreateRule;
        _watchListRules = watchListRules;
        _watchSuppressAlerts = watchSuppressAlerts;
        _watchListSuppressions = watchListSuppressions;
        _watchConfigureQuietHours = watchConfigureQuietHours;
        _watchConfigureNotifications = watchConfigureNotifications;
        _watchConfigureEscalation = watchConfigureEscalation;
        _watchAlertHistory = watchAlertHistory;
        _watchComplianceTrend = watchComplianceTrend;
        _watchAlertStatistics = watchAlertStatistics;
        _watchCreateTaskFromAlert = watchCreateTaskFromAlert;
        _watchCollectEvidenceFromAlert = watchCollectEvidenceFromAlert;
        _watchCreateAutoRemediationRule = watchCreateAutoRemediationRule;
        _watchListAutoRemediationRules = watchListAutoRemediationRules;
        _nistControlSearchTool = nistControlSearchTool;
        _nistControlExplainerTool = nistControlExplainerTool;
        _dbFactory = dbFactory;
        _scopeFactory = scopeFactory;

        // Register all tools per Constitution Principle II
        RegisterTool(_assessmentTool);
        RegisterTool(_controlFamilyTool);
        RegisterTool(_documentGenerationTool);
        RegisterTool(_evidenceCollectionTool);
        RegisterTool(_remediationTool);
        RegisterTool(_validateRemediationTool);
        RegisterTool(_remediationPlanTool);
        RegisterTool(_auditLogTool);
        RegisterTool(_historyTool);
        RegisterTool(_statusTool);
        RegisterTool(_monitoringTool);

        // Register Kanban tools
        RegisterTool(_kanbanCreateBoard);
        RegisterTool(_kanbanBoardShow);
        RegisterTool(_kanbanGetTask);
        RegisterTool(_kanbanCreateTask);
        RegisterTool(_kanbanAssignTask);
        RegisterTool(_kanbanMoveTask);
        RegisterTool(_kanbanTaskList);
        RegisterTool(_kanbanTaskHistory);
        RegisterTool(_kanbanValidateTask);
        RegisterTool(_kanbanAddComment);
        RegisterTool(_kanbanTaskComments);
        RegisterTool(_kanbanEditComment);
        RegisterTool(_kanbanDeleteComment);
        RegisterTool(_kanbanRemediateTask);
        RegisterTool(_kanbanCollectEvidence);
        RegisterTool(_kanbanBulkUpdate);
        RegisterTool(_kanbanExport);
        RegisterTool(_kanbanArchiveBoard);

        // Register Task Enrichment tools (Feature 012)
        RegisterTool(_kanbanGenerateScript);
        RegisterTool(_kanbanGenerateValidation);

        // Register Auth/PIM tools
        RegisterTool(_cacStatus);
        RegisterTool(_cacSignOut);
        RegisterTool(_cacSetTimeout);
        RegisterTool(_cacMapCertificate);
        RegisterTool(_pimListEligible);
        RegisterTool(_pimActivateRole);
        RegisterTool(_pimDeactivateRole);
        RegisterTool(_pimListActive);
        RegisterTool(_pimExtendRole);
        RegisterTool(_pimApproveRequest);
        RegisterTool(_pimDenyRequest);
        RegisterTool(_jitRequestAccess);
        RegisterTool(_jitListSessions);
        RegisterTool(_jitRevokeAccess);
        RegisterTool(_pimHistory);

        // Register Compliance Watch tools
        RegisterTool(_watchEnableMonitoring);
        RegisterTool(_watchDisableMonitoring);
        RegisterTool(_watchConfigureMonitoring);
        RegisterTool(_watchMonitoringStatus);

        // Register Compliance Watch alert lifecycle tools
        RegisterTool(_watchShowAlerts);
        RegisterTool(_watchGetAlert);
        RegisterTool(_watchAcknowledgeAlert);
        RegisterTool(_watchFixAlert);
        RegisterTool(_watchDismissAlert);

        // Register Compliance Watch alert rules & suppression tools
        RegisterTool(_watchCreateRule);
        RegisterTool(_watchListRules);
        RegisterTool(_watchSuppressAlerts);
        RegisterTool(_watchListSuppressions);
        RegisterTool(_watchConfigureQuietHours);

        // Register Compliance Watch notification & escalation tools
        RegisterTool(_watchConfigureNotifications);
        RegisterTool(_watchConfigureEscalation);

        // Register Compliance Watch dashboard & reporting tools
        RegisterTool(_watchAlertHistory);
        RegisterTool(_watchComplianceTrend);
        RegisterTool(_watchAlertStatistics);

        // Register Compliance Watch integration tools
        RegisterTool(_watchCreateTaskFromAlert);
        RegisterTool(_watchCollectEvidenceFromAlert);

        // Register Compliance Watch auto-remediation tools
        RegisterTool(_watchCreateAutoRemediationRule);
        RegisterTool(_watchListAutoRemediationRules);

        // Register NIST Controls knowledge tools (Feature 007)
        RegisterTool(_nistControlSearchTool);
        RegisterTool(_nistControlExplainerTool);

        // Auto-register any remaining BaseTool instances (e.g. RMF, ConMon, eMASS,
        // Document Template tools) that were added to DI but not explicitly listed
        // above. This ensures new tools become available to the AI without requiring
        // individual constructor parameters.
        var registeredNames = new HashSet<string>(
            Tools.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var tool in allRegisteredTools)
        {
            if (!registeredNames.Contains(tool.Name))
            {
                RegisterTool(tool);
                registeredNames.Add(tool.Name);
            }
        }

        // Inject SystemIdResolver into all tools so system_id parameters
        // transparently accept names/acronyms in addition to GUIDs.
        foreach (var tool in Tools)
        {
            tool.SystemIdResolver = _systemIdResolver;
        }

        // Provision Foundry agent in background when enabled
        if (_azureAiOptions?.IsFoundry == true)
            _ = Task.Run(async () => await ProvisionFoundryAgentAsync());
    }

    /// <inheritdoc />
    public override string AgentId => "compliance-agent";
    /// <inheritdoc />
    public override string AgentName => "Compliance Agent";
    /// <inheritdoc />
    public override string Description => "Handles NIST 800-53, FedRAMP, and ATO compliance assessments, remediation, and documentation";

    /// <summary>
    /// Evaluates confidence that this agent can handle the given message.
    /// Action-intent keywords (scan, assess, check, validate, run, monitor, remediate) score high (0.7-0.9).
    /// Compliance domain terms alone score moderate (0.4-0.6).
    /// Default baseline is 0.2 for unrecognized queries.
    /// </summary>
    public override double CanHandle(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return 0.0;

        var lower = message.ToLowerInvariant();
        var score = 0.0;

        // RMF lifecycle action keywords — strongest indicators (0.9)
        string[] rmfActionKeywords = [
            "register system", "register a new system", "register a system", "new system called",
            "list systems", "show systems", "registered systems", "my systems", "all systems",
            "define boundary", "authorization boundary", "exclude from boundary",
            "assign rmf role", "assign issm", "assign isso", "assign ao", "rmf role",
            "list rmf roles", "role assignments", "who is assigned",
            "advance rmf", "advance phase", "advance step", "next phase",
            "categorize system", "security categorization", "fips 199", "impact level",
            "get categorization", "show categorization",
            "suggest info types", "information types",
            "select baseline", "control baseline", "tailor baseline", "customize baseline",
            "control inheritance", "set inheritance", "inherited controls", "common controls",
            "inherited from", "as inherited", "inherit from", "mark as inherited",
            "remove control", "add control", "set controls",
            "generate the customer", "responsibility matrix",
            "get baseline", "show baseline", "view baseline",
            "generate crm", "customer responsibility matrix",
            "stig mapping", "show stig",
            "write narrative", "control narrative", "suggest narrative", "batch narrative",
            "narrative progress", "ssp progress",
            "assess control", "control assessment", "test control",
            "take snapshot", "compliance snapshot", "compare snapshot",
            "verify evidence", "evidence completeness", "evidence gaps", "missing evidence",
            "generate sar", "security assessment report",
            "issue authorization", "issue ato", "grant ato", "authorize system",
            "accept risk", "risk acceptance", "risk register", "show risks",
            "create poam", "list poam", "show poam", "poam items",
            "generate rar", "risk assessment report",
            "bundle package", "authorization package", "ato package",
            "conmon plan", "continuous monitoring plan", "conmon report", "monitoring report",
            "significant change", "system change",
            "ato expiration", "track expiration",
            "multi system dashboard", "portfolio dashboard",
            "reauthorization", "reauthorize",
            "rmf status", "rmf phase", "rmf progress"
        ];
        foreach (var keyword in rmfActionKeywords)
        {
            if (lower.Contains(keyword))
            {
                score = Math.Max(score, 0.9);
                break;
            }
        }

        // Action-intent keywords — strong indicators for compliance agent
        string[] actionKeywords = ["scan", "assess", "check my", "validate", "run ", "monitor",
            "remediate", "fix ", "deploy", "evaluate", "audit", "generate report",
            "create assessment", "start assessment", "check compliance"];
        foreach (var keyword in actionKeywords)
        {
            if (lower.Contains(keyword))
            {
                score = Math.Max(score, 0.8);
                break;
            }
        }

        // Compliance-specific action verbs
        if (lower.Contains("scan my") || lower.Contains("assess my") || lower.Contains("check my subscription"))
            score = Math.Max(score, 0.9);

        // Domain terms without action intent — moderate score
        string[] domainTerms = ["compliance", "assessment", "finding", "poam", "ssp",
            "authorization", "ato", "remediation", "control family", "baseline",
            "system", "boundary", "categorize", "narrative", "snapshot", "conmon"];
        foreach (var term in domainTerms)
        {
            if (lower.Contains(term) && score < 0.5)
            {
                score = Math.Max(score, 0.5);
            }
        }

        // Word-level action + entity combination — catches natural phrasing
        // like "list open poams" or "show me the assessment findings"
        if (score < 0.9)
        {
            string[] actionWords = ["list", "show", "get", "create", "update", "delete",
                "run", "check", "view", "open", "find", "query", "search", "generate",
                "export", "import", "assign", "set", "add", "remove", "compare"];
            string[] entityWords = ["poam", "poa&m", "finding", "assessment", "boundary",
                "narrative", "control", "baseline", "system", "ato", "conmon", "sar",
                "role", "categorization", "ssp", "snapshot", "capability", "component",
                "risk", "authorization", "remediation", "package"];
            bool hasAction = false;
            bool hasEntity = false;
            foreach (var w in actionWords) { if (lower.Contains(w)) { hasAction = true; break; } }
            foreach (var w in entityWords) { if (lower.Contains(w)) { hasEntity = true; break; } }
            if (hasAction && hasEntity)
                score = Math.Max(score, 0.85);
        }

        // Framework mentions — moderate
        if ((lower.Contains("nist") || lower.Contains("fedramp") || lower.Contains("dod") || lower.Contains("rmf")) && score < 0.4)
            score = Math.Max(score, 0.4);

        // Default baseline for unrecognized messages
        if (score == 0.0)
            score = 0.2;

        return score;
    }

    /// <inheritdoc />
    public override string GetSystemPrompt()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Ato.Copilot.Agents.Compliance.Prompts.ComplianceAgent.prompt.txt";
        
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Logger.LogWarning("System prompt resource not found: {Resource}", resourceName);
            return "You are a compliance agent for Azure Government NIST 800-53 assessments.";
        }

        using var reader = new StreamReader(stream);
        var basePrompt = reader.ReadToEnd();

        // ── US12: Append dynamic RMF step context when a system is active ────
        var activeStep = _activeRmfStep.Value;
        if (activeStep.HasValue)
        {
            basePrompt += GetRmfStepContextSupplement(activeStep.Value, _activeSystemName.Value);
        }

        return basePrompt;
    }

    /// <summary>
    /// Processes a compliance request, routing to the appropriate tool and logging the action.
    /// </summary>
    public override async Task<AgentResponse> ProcessAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogInformation("ComplianceAgent processing: {Message}", message[..Math.Min(100, message.Length)]);
        var actionType = ClassifyIntent(message);

        try
        {
            progress?.Report("Checking authorization...");

            // ── US12: Resolve current system's RMF step for prompt routing ──
            await ResolveRmfStepContextAsync(context, cancellationToken);

            // ── Auth-gate: check PIM eligibility for Tier 2 operations (FR-019) ──
            var authGateResult = await CheckAuthGateAsync(message, context, cancellationToken);
            if (authGateResult != null)
            {
                stopwatch.Stop();
                return new AgentResponse
                {
                    Success = true,
                    Response = authGateResult,
                    AgentName = AgentName,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            progress?.Report("Routing to ATO Copilot agent...");

            // ── AI-powered processing path (Feature 011) ────────────────────
            var aiResponse = await TryProcessWithBackendAsync(message, context, cancellationToken, progress);
            if (aiResponse != null)
            {
                progress?.Report("Generating response...");

                // Populate suggestions so quick-action buttons appear after AI responses
                if (aiResponse.Suggestions.Count == 0)
                {
                    aiResponse.Suggestions = BuildSuggestions(actionType, aiResponse.Response);
                }

                // Log successful AI-processed action to audit trail
                await LogAuditEntryAsync(actionType, GetContextValue(context, "subscription_id"),
                    AuditOutcome.Success, $"AI-processed: {message[..Math.Min(200, message.Length)]}",
                    stopwatch.Elapsed, cancellationToken);
                return aiResponse;
            }

            progress?.Report("Analyzing intent...");

            // Analyze intent and route to appropriate tool
            progress?.Report("Executing tool...");
            var toolResult = await RouteToToolAsync(message, context, cancellationToken);
            progress?.Report("Processing results...");

            // ── Post-operation deactivation offer (FR-020/FR-021) ────────────
            toolResult = await AppendDeactivationOfferAsync(toolResult, message, context, cancellationToken);

            // ── Format raw JSON tool results into human-readable markdown ────
            progress?.Report("Formatting response...");
            var formattedResult = FormatToolResultAsMarkdown(toolResult, actionType);

            stopwatch.Stop();

            // Log successful action to audit trail
            await LogAuditEntryAsync(actionType, GetContextValue(context, "subscription_id"),
                AuditOutcome.Success, $"Processed: {message[..Math.Min(200, message.Length)]}",
                stopwatch.Elapsed, cancellationToken);

            return new AgentResponse
            {
                Success = true,
                Response = formattedResult,
                AgentName = AgentName,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                // T022a/T022b: Populate ResponseData and Suggestions from tool results
                ResponseData = BuildResponseData(actionType, toolResult),
                Suggestions = BuildSuggestions(actionType, toolResult)
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Error in ComplianceAgent processing");

            // Log failed action to audit trail
            await LogAuditEntryAsync(actionType, GetContextValue(context, "subscription_id"),
                AuditOutcome.Failure, $"Error: {ex.Message}", stopwatch.Elapsed, cancellationToken);

            return new AgentResponse
            {
                Success = false,
                Response = $"Error processing compliance request: {ex.Message}",
                AgentName = AgentName,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Builds intent-specific structured data from the tool result (T022a, FR-007a).
    /// The "type" key determines Adaptive Card routing on the client side.
    /// </summary>
    private Dictionary<string, object>? BuildResponseData(string actionType, string toolResult)
    {
        if (string.IsNullOrEmpty(toolResult))
            return null;

        try
        {
            var data = new Dictionary<string, object>();

            switch (actionType.ToLowerInvariant())
            {
                case "assess":
                case "scan":
                case "audit":
                    data["type"] = "assessment";
                    // Try to extract structured assessment data from the JSON result
                    if (TryParseJsonProperty(toolResult, "complianceScore", out var score))
                        data["complianceScore"] = score;
                    if (TryParseJsonProperty(toolResult, "passedControls", out var passed))
                        data["passedControls"] = passed;
                    if (TryParseJsonProperty(toolResult, "warningControls", out var warnings))
                        data["warningControls"] = warnings;
                    if (TryParseJsonProperty(toolResult, "failedControls", out var failed))
                        data["failedControls"] = failed;
                    if (TryParseJsonProperty(toolResult, "findings", out var findings))
                        data["findings"] = findings;
                    if (TryParseJsonProperty(toolResult, "framework", out var fw))
                        data["framework"] = fw;
                    if (TryParseJsonProperty(toolResult, "assessmentScope", out var scope))
                        data["assessmentScope"] = scope;
                    return data.Count > 1 ? data : null; // Must have more than just "type"

                case "finding":
                case "control_family":
                    data["type"] = "finding";
                    if (TryParseJsonProperty(toolResult, "controlId", out var ctrlId))
                        data["controlId"] = ctrlId;
                    if (TryParseJsonProperty(toolResult, "severity", out var severity))
                        data["severity"] = severity;
                    if (TryParseJsonProperty(toolResult, "description", out var desc))
                        data["description"] = desc;
                    return data.Count > 1 ? data : null;

                case "remediate":
                case "remediation_plan":
                    data["type"] = "remediationPlan";
                    if (TryParseJsonProperty(toolResult, "steps", out var steps))
                        data["steps"] = steps;
                    if (TryParseJsonProperty(toolResult, "riskReduction", out var risk))
                        data["riskReduction"] = risk;
                    return data.Count > 1 ? data : null;

                case "kanban_show":
                case "kanban_list":
                    data["type"] = "kanban";
                    if (TryParseJsonProperty(toolResult, "board", out var board))
                        data["board"] = board;
                    if (TryParseJsonProperty(toolResult, "columns", out var cols))
                        data["columns"] = cols;
                    return data.Count > 1 ? data : null;

                case "alert":
                case "monitor":
                    data["type"] = "alert";
                    if (TryParseJsonProperty(toolResult, "alerts", out var alerts))
                        data["alerts"] = alerts;
                    return data.Count > 1 ? data : null;

                case "history":
                case "trend":
                    data["type"] = "trend";
                    if (TryParseJsonProperty(toolResult, "history", out var history))
                        data["history"] = history;
                    return data.Count > 1 ? data : null;

                case "evidence":
                    data["type"] = "evidence";
                    if (TryParseJsonProperty(toolResult, "evidence", out var evidence))
                        data["evidence"] = evidence;
                    return data.Count > 1 ? data : null;

                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to build ResponseData for action {ActionType}", actionType);
            return null;
        }
    }

    /// <summary>
    /// Builds contextually relevant RMF-phase-aware follow-up suggestions based on
    /// the classified intent and tool result (T022b, FR-007d).
    /// Each suggestion has a display title and a pre-filled prompt sent on click.
    /// </summary>
    private List<AgentSuggestedAction> BuildSuggestions(string actionType, string toolResult)
    {
        var suggestions = new List<AgentSuggestedAction>();

        // Helper to shorten construction
        static AgentSuggestedAction S(string title, string? prompt = null) => new(title, prompt ?? title);

        if (string.IsNullOrEmpty(toolResult))
        {
            suggestions.Add(S("Register a New System", "Register a new system"));
            suggestions.Add(S("Show RMF Status", "Show RMF status"));
            return suggestions;
        }

        // Try to extract a system name from the tool result for contextual prompts
        var systemName = ExtractSystemNameFromResult(toolResult);
        var forSystem = !string.IsNullOrEmpty(systemName) ? $" for {systemName}" : "";

        switch (actionType)
        {
            // ── RMF Phase 1: Prepare ─────────────────────────────────────
            case "RmfRegistration":
                suggestions.Add(S("Define Authorization Boundary", $"Define the authorization boundary{forSystem}"));
                suggestions.Add(S("Assign RMF Roles", $"Assign RMF roles{forSystem}"));
                suggestions.Add(S("Show System Details", $"Show system details{forSystem}"));
                break;

            case "RmfListSystems":
                suggestions.Add(S("Register a New System", "Register a new system"));
                suggestions.Add(S("Define Authorization Boundary", $"Define the authorization boundary{forSystem}"));
                suggestions.Add(S("Assign RMF Roles", $"Assign RMF roles{forSystem}"));
                break;

            case "RmfBoundary":
                suggestions.Add(S("Assign RMF Roles", $"Assign RMF roles{forSystem}"));
                suggestions.Add(S("Categorize System", $"Categorize{forSystem}"));
                suggestions.Add(S("List Boundary Components", $"Show authorization boundary{forSystem}"));
                break;

            case "RmfRoleAssignment":
                suggestions.Add(S("Define Authorization Boundary", $"Define the authorization boundary{forSystem}"));
                suggestions.Add(S("Advance to Categorize", $"Advance to Categorize phase{forSystem}"));
                suggestions.Add(S("Show RMF Status", $"Show RMF status{forSystem}"));
                break;

            // ── RMF Phase 2: Categorize ──────────────────────────────────
            case "RmfCategorize":
                suggestions.Add(S("Select Control Baseline", $"Select control baseline{forSystem}"));
                suggestions.Add(S("Suggest Information Types", $"Suggest information types{forSystem}"));
                suggestions.Add(S("Advance to Select Phase", $"Advance to Select phase{forSystem}"));
                break;

            // ── RMF Phase 3: Select ──────────────────────────────────────
            case "RmfBaseline":
                suggestions.Add(S("Tailor Baseline Controls", $"Tailor baseline{forSystem}"));
                suggestions.Add(S("Generate CRM", $"Generate CRM{forSystem}"));
                suggestions.Add(S("Advance to Implement Phase", $"Advance to Implement phase{forSystem}"));
                break;

            // ── RMF Phase 4: Implement ───────────────────────────────────
            case "RmfImplement":
                suggestions.Add(S("Write Control Narrative", $"Write control narrative{forSystem}"));
                suggestions.Add(S("Check Narrative Progress", $"Show narrative progress{forSystem}"));
                suggestions.Add(S("Advance to Assess Phase", $"Advance to Assess phase{forSystem}"));
                break;

            // ── RMF Phase 5: Assess ──────────────────────────────────────
            case "RmfAssess":
                suggestions.Add(S("Verify Evidence", $"Verify evidence{forSystem}"));
                suggestions.Add(S("Check Evidence Completeness", $"Check evidence completeness{forSystem}"));
                suggestions.Add(S("Generate SAR", $"Generate SAR{forSystem}"));
                break;

            // ── RMF Phase 6: Authorize ───────────────────────────────────
            case "RmfAuthorize":
                suggestions.Add(S("Create POAM", $"Create POAM{forSystem}"));
                suggestions.Add(S("Bundle Authorization Package", $"Bundle authorization package{forSystem}"));
                suggestions.Add(S("Advance to Monitor Phase", $"Advance to Monitor phase{forSystem}"));
                break;

            case "RmfRisk":
                suggestions.Add(S("View Risk Register", $"Show risk register{forSystem}"));
                suggestions.Add(S("Issue Authorization", $"Issue authorization{forSystem}"));
                suggestions.Add(S("Create POAM", $"Create POAM{forSystem}"));
                break;

            // ── RMF Phase 7: Monitor ─────────────────────────────────────
            case "RmfMonitor":
                suggestions.Add(S("Generate ConMon Report", $"Generate ConMon report{forSystem}"));
                suggestions.Add(S("Check ATO Expiration", $"Check ATO expiration{forSystem}"));
                suggestions.Add(S("Trigger Reauthorization", $"Start reauthorization{forSystem}"));
                break;

            case "RmfAdvance":
                suggestions.Add(S("Show RMF Status", $"Show RMF status{forSystem}"));
                suggestions.Add(S("Show System Details", $"Show system details{forSystem}"));
                break;

            case "RmfStatus":
                suggestions.Add(S("Register a New System", "Register a new system"));
                suggestions.Add(S("Run Compliance Assessment", $"Run compliance assessment{forSystem}"));
                suggestions.Add(S("Show All Systems", "Show all registered systems"));
                break;

            // ── Legacy compliance actions ─────────────────────────────────
            case "Assessment":
                var hasFailures = toolResult.Contains("\"failed\"", StringComparison.OrdinalIgnoreCase)
                               || toolResult.Contains("failedControls", StringComparison.OrdinalIgnoreCase)
                               || toolResult.Contains("\"severity\":\"Critical\"", StringComparison.OrdinalIgnoreCase)
                               || toolResult.Contains("\"severity\":\"High\"", StringComparison.OrdinalIgnoreCase);
                if (hasFailures)
                {
                    suggestions.Add(S("Generate Remediation Plan", "Generate remediation plan"));
                    suggestions.Add(S("View Detailed Findings", "Show detailed findings"));
                    suggestions.Add(S("Show Kanban Board", "Show kanban board"));
                }
                else
                {
                    suggestions.Add(S("Export Compliance Report", "Export compliance report"));
                    suggestions.Add(S("View Compliance Trend", "View compliance trend"));
                }
                break;

            case "Remediation":
                suggestions.Add(S("Run Assessment", "Run compliance assessment"));
                suggestions.Add(S("View Remediation Status", "View remediation status"));
                suggestions.Add(S("Show Kanban Board", "Show kanban board"));
                break;

            case "EvidenceCollection":
                suggestions.Add(S("Run Assessment", "Run compliance assessment"));
                suggestions.Add(S("Generate SSP", "Generate SSP document"));
                break;

            case "DocumentGeneration":
                suggestions.Add(S("Run Assessment", "Run compliance assessment"));
                suggestions.Add(S("Show RMF Status", "Show RMF status"));
                break;

            case "Monitoring":
                suggestions.Add(S("View Alert Details", "View alert details"));
                suggestions.Add(S("View Compliance Trend", "View compliance trend"));
                break;

            case "KanbanQuery":
            case "KanbanTask":
                suggestions.Add(S("Run Assessment", "Run compliance assessment"));
                suggestions.Add(S("View Overdue Tasks", "Show overdue tasks"));
                break;

            case "HistoryQuery":
                suggestions.Add(S("Run Assessment", "Run compliance assessment"));
                suggestions.Add(S("Generate Remediation Plan", "Generate remediation plan"));
                break;

            default:
                suggestions.Add(S("Register a New System", "Register a new system"));
                suggestions.Add(S("Show RMF Status", "Show RMF status"));
                suggestions.Add(S("Show All Systems", "Show all registered systems"));
                break;
        }

        return suggestions;
    }

    /// <summary>
    /// Attempts to extract a system name from a JSON tool result for contextual prompts.
    /// Looks for common property names: systemName, name, system_name.
    /// </summary>
    private static string? ExtractSystemNameFromResult(string toolResult)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            var root = doc.RootElement;

            // Direct properties
            if (root.TryGetProperty("systemName", out var sn) && sn.ValueKind == JsonValueKind.String)
                return sn.GetString();
            if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                return n.GetString();

            // Inside "data" envelope
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("systemName", out var dsn) && dsn.ValueKind == JsonValueKind.String)
                    return dsn.GetString();
                if (data.TryGetProperty("name", out var dn) && dn.ValueKind == JsonValueKind.String)
                    return dn.GetString();
            }

            // First item in array (e.g. list_systems with single result)
            if (root.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.TryGetProperty("systemName", out var isn) && isn.ValueKind == JsonValueKind.String)
                        return isn.GetString();
                    if (item.TryGetProperty("name", out var iname) && iname.ValueKind == JsonValueKind.String)
                        return iname.GetString();
                    break; // Only check first
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a raw JSON tool result into a human-readable markdown response.
    /// Falls back to the original string if parsing fails or the format is unrecognized.
    /// </summary>
    private static string FormatToolResultAsMarkdown(string toolResult, string actionType)
    {
        if (string.IsNullOrWhiteSpace(toolResult))
            return "No data returned.";

        // If not JSON, return as-is (already formatted or plain text)
        var trimmed = toolResult.TrimStart();
        if (!trimmed.StartsWith("{") && !trimmed.StartsWith("["))
            return toolResult;

        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            var root = doc.RootElement;

            // Check for error responses
            if (root.TryGetProperty("status", out var statusProp) &&
                statusProp.GetString() == "error")
            {
                var errorMsg = root.TryGetProperty("message", out var msgProp)
                    ? msgProp.GetString() : "An error occurred.";
                var errorCode = root.TryGetProperty("errorCode", out var codeProp)
                    ? codeProp.GetString() : null;
                return errorCode != null
                    ? $"**Error** ({errorCode}): {errorMsg}"
                    : $"**Error**: {errorMsg}";
            }

            // Check for disambiguation / validation errors (success=false)
            if (root.TryGetProperty("success", out var successProp) &&
                successProp.ValueKind == JsonValueKind.False &&
                root.TryGetProperty("message", out var disambigMsg))
            {
                var msg = disambigMsg.GetString() ?? "An error occurred.";
                var errorCode = root.TryGetProperty("error", out var errCodeProp)
                    ? errCodeProp.GetString() : null;
                // The message already contains markdown formatting — return as-is
                return errorCode != null
                    ? $"**{errorCode}**: {msg}"
                    : msg;
            }

            // Check for conversational prompts (asking user for more info)
            if (root.TryGetProperty("conversational", out var convProp) &&
                convProp.ValueKind == JsonValueKind.True &&
                root.TryGetProperty("message", out var convMsg))
            {
                return convMsg.GetString() ?? "I need more information to proceed.";
            }

            var sb = new System.Text.StringBuilder();

            // ── compliance_register_system ─────────────────────────────────
            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("id", out _) &&
                data.TryGetProperty("current_rmf_step", out _) &&
                data.TryGetProperty("system_type", out _))
            {
                sb.AppendLine("### ✅ System Registered Successfully\n");
                sb.AppendLine("| Field | Value |");
                sb.AppendLine("|-------|-------|");
                AppendField(sb, data, "id", "System ID");
                AppendField(sb, data, "name", "Name");
                AppendField(sb, data, "acronym", "Acronym");
                AppendField(sb, data, "system_type", "System Type");
                AppendField(sb, data, "mission_criticality", "Mission Criticality");
                AppendField(sb, data, "hosting_environment", "Hosting Environment");
                AppendField(sb, data, "current_rmf_step", "Current RMF Step");
                AppendField(sb, data, "is_active", "Active");
                AppendField(sb, data, "created_at", "Created");
                AppendField(sb, data, "created_by", "Created By");
                sb.AppendLine();
                sb.AppendLine("**Next step:** Define the authorization boundary and assign RMF roles, then advance to the **Categorize** phase.");
                return sb.ToString();
            }

            // ── compliance_list_systems ────────────────────────────────────
            if (root.TryGetProperty("data", out var listData) &&
                listData.TryGetProperty("systems", out var systems) &&
                systems.ValueKind == JsonValueKind.Array)
            {
                var count = systems.GetArrayLength();
                sb.AppendLine($"### 📋 Registered Systems ({count})\n");

                if (count == 0)
                {
                    sb.AppendLine("No systems registered yet. Use **\"Register a new system\"** to get started.");
                    return sb.ToString();
                }

                sb.AppendLine("| Name | Type | RMF Phase | Active | Created |");
                sb.AppendLine("|------|------|-----------|--------|---------|");
                foreach (var sys in systems.EnumerateArray())
                {
                    var name = GetStr(sys, "name") ?? "—";
                    var sysType = GetStr(sys, "system_type") ?? "—";
                    var rmfStep = GetStr(sys, "current_rmf_step") ?? "—";
                    var active = sys.TryGetProperty("is_active", out var act) && act.GetBoolean() ? "✅" : "❌";
                    var created = GetStr(sys, "created_at") ?? "—";
                    if (created.Length > 10) created = created[..10]; // date only
                    sb.AppendLine($"| {name} | {sysType} | {rmfStep} | {active} | {created} |");
                }

                if (listData.TryGetProperty("pagination", out var pag))
                {
                    var total = pag.TryGetProperty("total_count", out var tc) ? tc.GetInt32() : count;
                    if (total > count)
                        sb.AppendLine($"\n*Showing {count} of {total} systems.*");
                }
                return sb.ToString();
            }

            // ── compliance_get_system ──────────────────────────────────────
            if (root.TryGetProperty("data", out var sysData) &&
                sysData.TryGetProperty("id", out _) &&
                sysData.TryGetProperty("current_rmf_step", out _) &&
                !sysData.TryGetProperty("system_type", out _) == false)
            {
                sb.AppendLine("### 📄 System Details\n");
                sb.AppendLine("| Field | Value |");
                sb.AppendLine("|-------|-------|");
                AppendField(sb, sysData, "id", "System ID");
                AppendField(sb, sysData, "name", "Name");
                AppendField(sb, sysData, "acronym", "Acronym");
                AppendField(sb, sysData, "system_type", "System Type");
                AppendField(sb, sysData, "mission_criticality", "Mission Criticality");
                AppendField(sb, sysData, "hosting_environment", "Hosting Environment");
                AppendField(sb, sysData, "current_rmf_step", "Current RMF Step");
                AppendField(sb, sysData, "is_active", "Active");
                AppendField(sb, sysData, "created_at", "Created");
                AppendField(sb, sysData, "created_by", "Created By");
                return sb.ToString();
            }

            // ── Generic success with data ──────────────────────────────────
            if (root.TryGetProperty("status", out var genStatus) &&
                genStatus.GetString() == "success" &&
                root.TryGetProperty("data", out var genData))
            {
                sb.AppendLine("### ✅ Operation Completed\n");
                FormatJsonObjectAsTable(sb, genData);

                if (root.TryGetProperty("metadata", out var meta) &&
                    meta.TryGetProperty("tool", out var toolProp))
                {
                    sb.AppendLine($"\n*Tool: `{toolProp.GetString()}`*");
                }
                return sb.ToString();
            }

            // ── Generic unknown status ─────────────────────────────────────
            if (root.TryGetProperty("status", out var unkStatus) &&
                unkStatus.GetString() == "unknown")
            {
                var msg = root.TryGetProperty("message", out var unkMsg)
                    ? unkMsg.GetString() : "Unknown status.";
                return $"ℹ️ {msg}";
            }

            // Fallback: wrap entire JSON in a code block for readability
            return $"```json\n{toolResult}\n```";
        }
        catch (JsonException)
        {
            // Not valid JSON — return as-is
            return toolResult;
        }
    }

    /// <summary>Appends a table row for a JSON field if it exists.</summary>
    private static void AppendField(System.Text.StringBuilder sb, JsonElement element, string property, string label)
    {
        if (element.TryGetProperty(property, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            var value = prop.ValueKind switch
            {
                JsonValueKind.True => "✅ Yes",
                JsonValueKind.False => "❌ No",
                JsonValueKind.String => prop.GetString() ?? "—",
                _ => prop.ToString()
            };
            // Truncate long IDs for display
            if (property == "id" && value.Length > 8)
                value = $"`{value[..8]}…`";
            if (property.EndsWith("_at") && value.Length > 19)
                value = value[..19].Replace("T", " ");
            sb.AppendLine($"| {label} | {value} |");
        }
    }

    /// <summary>Formats a JSON object as a markdown table of key-value pairs.</summary>
    private static void FormatJsonObjectAsTable(System.Text.StringBuilder sb, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            sb.AppendLine("| Field | Value |");
            sb.AppendLine("|-------|-------|");
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                    continue; // Skip nested objects in the simple table
                var value = prop.Value.ValueKind switch
                {
                    JsonValueKind.Null => "—",
                    JsonValueKind.True => "✅",
                    JsonValueKind.False => "❌",
                    JsonValueKind.String => prop.Value.GetString() ?? "—",
                    _ => prop.Value.ToString()
                };
                sb.AppendLine($"| {prop.Name} | {value} |");
            }
        }
    }

    /// <summary>Gets a string property from a JsonElement, or null if missing.</summary>
    private static string? GetStr(JsonElement element, string property) =>
        element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() : null;

    /// <summary>
    /// Attempts to extract a JSON property value from a tool result string.
    /// </summary>
    private static bool TryParseJsonProperty(string json, string propertyName, out object value)
    {
        value = default!;
        try
        {
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{"))
                return false;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(propertyName, out var prop))
            {
                value = prop.ValueKind switch
                {
                    JsonValueKind.Number => prop.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => prop.GetString()!,
                    _ => JsonSerializer.Deserialize<object>(prop.GetRawText())!
                };
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Routes a user message to the appropriate compliance tool based on intent analysis.</summary>
    private async Task<string> RouteToToolAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken)
    {
        var lowerMessage = message.ToLowerInvariant();

        // ── Context-aware task resolution ────────────────────────────────────
        // Resolve ordinal references ("the first one", "the second task", etc.)
        // from previously stored task list context.
        var resolvedTaskId = ResolveTaskFromContext(lowerMessage, context);
        if (resolvedTaskId != null)
        {
            context.WorkflowState["task_id"] = resolvedTaskId;
        }

        // Route based on intent keywords
        if (ContainsAny(lowerMessage, "assess", "scan", "audit", "check compliance", "run assessment"))
        {
            return await _assessmentTool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["subscription_id"] = GetContextValue(context, "subscription_id"),
                ["scan_type"] = "quick"
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "control family", "nist control", "control details"))
        {
            var family = ExtractControlFamily(lowerMessage);
            return await _controlFamilyTool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["family_id"] = family
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "generate ssp", "generate document", "poam", "poa&m", "sar", "system security plan"))
        {
            var docType = ExtractDocumentType(lowerMessage);
            return await _documentGenerationTool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["document_type"] = docType,
                ["subscription_id"] = GetContextValue(context, "subscription_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "collect evidence", "evidence collection", "gather evidence"))
        {
            return await _evidenceCollectionTool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["subscription_id"] = GetContextValue(context, "subscription_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "remediate", "fix finding", "apply fix"))
        {
            return await _remediationTool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["dry_run"] = true
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "remediation plan", "plan remediation"))
        {
            return await _remediationPlanTool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["subscription_id"] = GetContextValue(context, "subscription_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "show findings", "open findings", "view findings", "list findings", "show open findings"))
        {
            var showFindingsTool = Tools.FirstOrDefault(t => t.Name == "compliance_show_findings");
            if (showFindingsTool != null)
            {
                return await showFindingsTool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
            }
        }

        if (ContainsAny(lowerMessage, "compliance status", "current status", "posture"))
        {
            return await _statusTool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["subscription_id"] = GetContextValue(context, "subscription_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "compliance history", "trend", "historical"))
        {
            return await _historyTool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["subscription_id"] = GetContextValue(context, "subscription_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "audit log", "audit trail"))
        {
            return await _auditLogTool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["subscription_id"] = GetContextValue(context, "subscription_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "monitor", "alert", "continuous"))
        {
            return await _monitoringTool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["action"] = "status",
                ["subscription_id"] = GetContextValue(context, "subscription_id")
            }, cancellationToken);
        }

        // ─── Kanban routing ──────────────────────────────────────────────────
        if (ContainsAny(lowerMessage, "create board", "new board", "kanban board create"))
        {
            return await _kanbanCreateBoard.ExecuteAsync(new Dictionary<string, object?>
            {
                ["name"] = "New Remediation Board",
                ["subscription_id"] = GetContextValue(context, "subscription_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "show board", "board overview", "board status", "kanban board"))
        {
            return await _kanbanBoardShow.ExecuteAsync(new Dictionary<string, object?>
            {
                ["board_id"] = GetContextValue(context, "board_id"),
                ["include_task_summaries"] = true
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "get task", "task detail", "rem-", "task info"))
        {
            return await _kanbanGetTask.ExecuteAsync(new Dictionary<string, object?>
            {
                ["task_id"] = GetContextValue(context, "task_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "create task", "new task", "add task"))
        {
            return await _kanbanCreateTask.ExecuteAsync(new Dictionary<string, object?>
            {
                ["board_id"] = GetContextValue(context, "board_id"),
                ["title"] = "New Remediation Task", ["control_id"] = "AC-1"
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "assign task", "reassign", "unassign"))
        {
            return await _kanbanAssignTask.ExecuteAsync(new Dictionary<string, object?>
            {
                ["task_id"] = GetContextValue(context, "task_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "move task", "transition", "change status"))
        {
            return await _kanbanMoveTask.ExecuteAsync(new Dictionary<string, object?>
            {
                ["task_id"] = GetContextValue(context, "task_id"),
                ["target_status"] = "InProgress"
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "list task", "task list", "tasks on board", "show tasks"))
        {
            var result = await _kanbanTaskList.ExecuteAsync(new Dictionary<string, object?>
            {
                ["board_id"] = GetContextValue(context, "board_id")
            }, cancellationToken);

            // Store task IDs in context for ordinal resolution
            StoreTaskListResults(result, context);
            return result;
        }

        if (ContainsAny(lowerMessage, "task history", "history of task", "audit task"))
        {
            return await _kanbanTaskHistory.ExecuteAsync(new Dictionary<string, object?>
            {
                ["task_id"] = GetContextValue(context, "task_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "validate task", "verify task", "check task fix"))
        {
            return await _kanbanValidateTask.ExecuteAsync(new Dictionary<string, object?>
            {
                ["task_id"] = GetContextValue(context, "task_id"),
                ["subscription_id"] = GetContextValue(context, "subscription_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "add comment", "comment on task"))
        {
            return await _kanbanAddComment.ExecuteAsync(new Dictionary<string, object?>
            {
                ["task_id"] = GetContextValue(context, "task_id"),
                ["content"] = "Comment added via agent"
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "task comments", "list comments", "show comments"))
        {
            return await _kanbanTaskComments.ExecuteAsync(new Dictionary<string, object?>
            {
                ["task_id"] = GetContextValue(context, "task_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "remediate task", "run remediation", "execute fix", "apply remediation"))
        {
            return await _kanbanRemediateTask.ExecuteAsync(new Dictionary<string, object?>
            {
                ["task_id"] = GetContextValue(context, "task_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "collect evidence", "task evidence", "gather evidence"))
        {
            return await _kanbanCollectEvidence.ExecuteAsync(new Dictionary<string, object?>
            {
                ["task_id"] = GetContextValue(context, "task_id"),
                ["subscription_id"] = GetContextValue(context, "subscription_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "bulk update", "bulk assign", "bulk move"))
        {
            return await _kanbanBulkUpdate.ExecuteAsync(new Dictionary<string, object?>
            {
                ["board_id"] = GetContextValue(context, "board_id"),
                ["operation"] = "assign", ["task_ids"] = new string[0]
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "export board", "export csv", "export poam", "kanban export"))
        {
            return await _kanbanExport.ExecuteAsync(new Dictionary<string, object?>
            {
                ["board_id"] = GetContextValue(context, "board_id"),
                ["format"] = "csv"
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "archive board", "close board"))
        {
            return await _kanbanArchiveBoard.ExecuteAsync(new Dictionary<string, object?>
            {
                ["board_id"] = GetContextValue(context, "board_id"),
                ["confirm"] = false
            }, cancellationToken);
        }

        // ─── Auth/PIM routing ────────────────────────────────────────────────
        if (ContainsAny(lowerMessage, "cac status", "auth status", "am i authenticated", "authentication status"))
        {
            return await _cacStatus.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "sign out", "cac sign out", "log out", "logout", "cac logout"))
        {
            return await _cacSignOut.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "set timeout", "session timeout", "cac timeout", "change timeout", "set my timeout", "timeout to"))
        {
            return await _cacSetTimeout.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id"),
                ["timeoutHours"] = ExtractHours(lowerMessage)
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "map certificate", "map cert", "certificate mapping", "cert mapping", "map my cert", "map my cac", "assign role to cert"))
        {
            return await _cacMapCertificate.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id"),
                ["role"] = ExtractRole(lowerMessage)
            }, cancellationToken);
        }

        // ─── PIM routing ─────────────────────────────────────────────────────
        if (ContainsAny(lowerMessage, "eligible roles", "pim eligible", "list eligible", "what roles can i activate"))
        {
            return await _pimListEligible.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id"),
                ["scope"] = GetContextValue(context, "subscription_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "activate role", "pim activate", "i need", "give me access", "enable role"))
        {
            return await _pimActivateRole.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id"),
                ["session_id"] = GetContextValue(context, "session_id"),
                ["roleName"] = ExtractRoleName(lowerMessage),
                ["scope"] = GetContextValue(context, "subscription_id") ?? "default",
                ["justification"] = message
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "deactivate role", "pim deactivate", "remove access", "revoke role", "disable role"))
        {
            return await _pimDeactivateRole.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id"),
                ["roleName"] = ExtractRoleName(lowerMessage),
                ["scope"] = GetContextValue(context, "subscription_id") ?? "default"
            }, cancellationToken);
        }

        // ─── PIM Session Management routing ──────────────────────────────────
        if (ContainsAny(lowerMessage, "active roles", "pim active", "list active", "my active pim", "show my active", "current roles"))
        {
            return await _pimListActive.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "extend role", "pim extend", "extend by", "extend access", "more time"))
        {
            return await _pimExtendRole.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id"),
                ["roleName"] = ExtractRoleName(lowerMessage),
                ["scope"] = GetContextValue(context, "subscription_id") ?? "default",
                ["additionalHours"] = ExtractHours(lowerMessage)
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "approve request", "pim approve", "approve role", "approve activation"))
        {
            return await _pimApproveRequest.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id"),
                ["user_role"] = GetContextValue(context, "user_role"),
                ["requestId"] = ExtractRequestId(lowerMessage),
                ["comments"] = null
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "deny request", "pim deny", "reject request", "deny activation"))
        {
            return await _pimDenyRequest.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id"),
                ["user_role"] = GetContextValue(context, "user_role"),
                ["requestId"] = ExtractRequestId(lowerMessage),
                ["reason"] = null
            }, cancellationToken);
        }

        // ─── JIT VM Access routing (Phase 9 — US7) ──────────────────────
        if (ContainsAny(lowerMessage, "ssh access", "rdp access", "vm access", "jit access", "jit request", "i need ssh", "i need rdp", "connect to vm"))
        {
            var vmName = ExtractVmName(lowerMessage);
            return await _jitRequestAccess.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id"),
                ["session_id"] = GetContextValue(context, "session_id"),
                ["vmName"] = vmName,
                ["resourceGroup"] = GetContextValue(context, "resource_group") ?? "default-rg",
                ["justification"] = message
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "jit sessions", "list jit", "active jit", "my jit sessions", "vm sessions"))
        {
            return await _jitListSessions.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id")
            }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "revoke jit", "revoke vm", "revoke access", "jit revoke", "remove jit", "close jit"))
        {
            var vmName = ExtractVmName(lowerMessage);
            return await _jitRevokeAccess.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id"),
                ["vmName"] = vmName,
                ["resourceGroup"] = GetContextValue(context, "resource_group") ?? "default-rg"
            }, cancellationToken);
        }

        // ─── PIM Audit Trail routing (Phase 12 — US10) ──────────────────
        if (ContainsAny(lowerMessage, "pim history", "pim audit", "pim log", "activation history", "role history", "audit trail", "compliance evidence"))
        {
            return await _pimHistory.ExecuteAsync(new Dictionary<string, object?>
            {
                ["user_id"] = GetContextValue(context, "user_id"),
                ["is_auditor"] = GetContextValue(context, "user_role")?.Equals("Compliance.Auditor", StringComparison.OrdinalIgnoreCase) ?? false,
                ["scope"] = GetContextValue(context, "subscription_id")
            }, cancellationToken);
        }

        // ─── RMF Lifecycle routing (Feature 015) ─────────────────────────

        // Some commands don't need a system_id — route them first before disambiguation.
        // Phase 0: Prepare — Register, Boundary, Roles
        if (ContainsAny(lowerMessage, "register system", "register a new system", "new system called", "register a system"))
        {
            var tool = FindToolByName("compliance_register_system");
            if (tool != null)
            {
                var systemName = ExtractQuotedValue(lowerMessage) ?? "New System";
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["name"] = systemName,
                    ["system_type"] = ExtractEnumValue(lowerMessage, new[] { "majorapplication", "enclave", "platformit" }, "MajorApplication"),
                    ["mission_criticality"] = ExtractEnumValue(lowerMessage, new[] { "missioncritical", "mission-critical", "missionessential", "mission-essential", "missionsupport", "mission-support" }, "MissionEssential"),
                    ["hosting_environment"] = ExtractEnumValue(lowerMessage, new[] { "azuregovernment", "azure government", "azurecommercial", "azure commercial", "onpremises", "on-premises", "hybrid" }, "AzureGovernment")
                }, cancellationToken);
            }
        }

        if (ContainsAny(lowerMessage, "list systems", "show systems", "registered systems", "all systems", "my systems"))
        {
            var tool = FindToolByName("compliance_list_systems");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>(), cancellationToken);
        }

        // ── Resolve system_id for routes that need it ────────────────────
        // Suggestion buttons emit prompts like "Define the authorization boundary for ACME Portal"
        // — we extract the system name and look it up in the DB.
        // When no name is given, auto-selects if exactly one active system exists.
        // If multiple exist, returns a disambiguation prompt.
        _pendingSystemChoices = null;
        if (string.IsNullOrEmpty(GetContextValue(context, "system_id")))
        {
            var resolvedId = await ResolveSystemIdFromMessageAsync(lowerMessage, cancellationToken);
            if (resolvedId != null)
            {
                context.WorkflowState["system_id"] = resolvedId;
                context.WorkflowState["systemId"] = resolvedId;
            }
            else if (_pendingSystemChoices is { Count: > 0 })
            {
                return BuildSystemDisambiguationResponse(lowerMessage, _pendingSystemChoices);
            }
        }

        if (ContainsAny(lowerMessage, "get system", "system details", "show system", "system info"))
        {
            var tool = FindToolByName("compliance_get_system");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "define boundary", "authorization boundary", "add to boundary", "set boundary"))
        {
            var systemId = GetContextValue(context, "system_id");
            // The tool requires resources — ask the user what to include
            return BuildConversationalPrompt(
                "Define Authorization Boundary",
                "I need to know which resources to include in the authorization boundary.",
                new[]
                {
                    "**Resource ID** — e.g. Azure subscription ID, VNet, Storage Account, etc.",
                    "**Resource Type** — e.g. Subscription, VirtualNetwork, StorageAccount, VM",
                    "**Resource Name** — a friendly name for each resource"
                },
                "Define boundary for eagle eye with resources: sub-001 (Subscription, 'Main Subscription'), vnet-prod (VirtualNetwork, 'Production VNet')",
                systemId);
        }

        if (ContainsAny(lowerMessage, "exclude from boundary", "remove from boundary", "exclude resource"))
        {
            var systemId = GetContextValue(context, "system_id");
            return BuildConversationalPrompt(
                "Exclude from Authorization Boundary",
                "I need to know which resource to exclude from the boundary.",
                new[]
                {
                    "**Resource ID** — the ID of the resource to exclude",
                    "**Justification** — why it should be excluded"
                },
                "Exclude resource sub-dev-01 from boundary for eagle eye, justification: dev-only environment",
                systemId);
        }

        if (ContainsAny(lowerMessage, "assign rmf role", "assign role", "assign issm", "assign isso", "assign ao"))
        {
            var systemId = GetContextValue(context, "system_id");
            return BuildConversationalPrompt(
                "Assign RMF Role",
                "I need to know which role to assign and to whom.",
                new[]
                {
                    "**Role** — AuthorizingOfficial, Issm, Isso, Sca, or SystemOwner",
                    "**User ID** — the user's identity (e.g. email or UPN)",
                    "**Display Name** — (optional) the user's display name"
                },
                "Assign ISSO role to john.doe@agency.gov for eagle eye",
                systemId);
        }

        if (ContainsAny(lowerMessage, "list rmf roles", "rmf roles", "who is assigned", "role assignments", "show roles"))
        {
            var tool = FindToolByName("compliance_list_rmf_roles");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "advance rmf", "advance phase", "advance step", "next phase", "move to categorize", "move to select", "move to implement", "move to assess", "move to authorize", "move to monitor"))
        {
            var systemId = GetContextValue(context, "system_id");
            return BuildConversationalPrompt(
                "Advance RMF Phase",
                "I need to know which RMF phase to advance to.",
                new[]
                {
                    "**Target Phase** — Prepare, Categorize, Select, Implement, Assess, Authorize, or Monitor"
                },
                "Advance eagle eye to Categorize phase",
                systemId);
        }

        // Phase 1: Categorize
        if (ContainsAny(lowerMessage, "categorize system", "security categorization", "set categorization", "fips 199", "impact level"))
        {
            var systemId = GetContextValue(context, "system_id");
            return BuildConversationalPrompt(
                "Categorize System (FIPS 199)",
                "I need the information types and their impact levels to categorize this system.",
                new[]
                {
                    "**Information Types** — SP 800-60 types (e.g. C.2.8.1 Personnel Security)",
                    "**Confidentiality Impact** — Low, Moderate, or High",
                    "**Integrity Impact** — Low, Moderate, or High",
                    "**Availability Impact** — Low, Moderate, or High"
                },
                "Categorize eagle eye with info type C.2.8.1 'Personnel Security' confidentiality=Moderate integrity=Moderate availability=Low",
                systemId);
        }

        if (ContainsAny(lowerMessage, "get categorization", "show categorization", "current categorization"))
        {
            var tool = FindToolByName("compliance_get_categorization");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "suggest info types", "information types", "suggest info", "info type"))
        {
            var tool = FindToolByName("compliance_suggest_info_types");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        // Phase 2: Select Controls
        if (ContainsAny(lowerMessage, "select baseline", "control baseline", "choose baseline"))
        {
            var tool = FindToolByName("compliance_select_baseline");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "tailor baseline", "tailor controls", "customize baseline"))
        {
            var tool = FindToolByName("compliance_tailor_baseline");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "control inheritance", "set inheritance", "inherited controls", "common controls"))
        {
            var tool = FindToolByName("compliance_set_inheritance");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "get baseline", "show baseline", "current baseline", "view baseline"))
        {
            var tool = FindToolByName("compliance_get_baseline");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "generate crm", "customer responsibility matrix", "crm report"))
        {
            var tool = FindToolByName("compliance_generate_crm");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "stig mapping", "stig", "show stig"))
        {
            var tool = FindToolByName("compliance_show_stig_mapping");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        // Phase 3: Implement Controls — SSP Authoring
        if (ContainsAny(lowerMessage, "write narrative", "control narrative", "implementation narrative"))
        {
            var systemId = GetContextValue(context, "system_id");
            return BuildConversationalPrompt(
                "Write Control Narrative",
                "I need to know which control and the implementation narrative text.",
                new[]
                {
                    "**Control ID** — NIST 800-53 control (e.g. AC-1, SI-2)",
                    "**Narrative** — the implementation description",
                    "**Status** — (optional) Implemented, PartiallyImplemented, Planned, or NotApplicable"
                },
                "Write narrative for AC-1: 'The organization develops an access control policy reviewed annually by the ISSO'",
                systemId);
        }

        if (ContainsAny(lowerMessage, "suggest narrative", "auto-generate narrative", "ai narrative"))
        {
            var systemId = GetContextValue(context, "system_id");
            return BuildConversationalPrompt(
                "AI-Suggest Control Narrative",
                "I need to know which control to generate a narrative for.",
                new[]
                {
                    "**Control ID** — NIST 800-53 control (e.g. AC-1, SI-2)"
                },
                "Suggest narrative for AC-1",
                systemId);
        }

        if (ContainsAny(lowerMessage, "batch narrative", "populate narratives", "batch populate"))
        {
            var tool = FindToolByName("compliance_batch_populate_narratives");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "narrative progress", "ssp progress", "how many narratives"))
        {
            var tool = FindToolByName("compliance_narrative_progress");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        // Phase 4: Assess Controls
        if (ContainsAny(lowerMessage, "assess control", "control assessment", "test control"))
        {
            var systemId = GetContextValue(context, "system_id");
            return BuildConversationalPrompt(
                "Assess Security Control",
                "I need the assessment details for the control.",
                new[]
                {
                    "**Control ID** — NIST 800-53 control (e.g. AC-2)",
                    "**Determination** — Satisfied, OtherThanSatisfied, or NotApplicable",
                    "**Findings** — (optional) assessment findings or evidence reference"
                },
                "Assess control AC-2 as Satisfied with findings 'Validated via STIG scan 2025-06-15'",
                systemId);
        }

        if (ContainsAny(lowerMessage, "take snapshot", "compliance snapshot", "capture snapshot"))
        {
            var tool = FindToolByName("compliance_take_snapshot");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "compare snapshot", "snapshot diff", "snapshot comparison"))
        {
            var tool = FindToolByName("compliance_compare_snapshots");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "verify evidence", "evidence verification", "check evidence"))
        {
            var tool = FindToolByName("compliance_verify_evidence");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "evidence completeness", "evidence gaps", "missing evidence"))
        {
            var tool = FindToolByName("compliance_check_evidence_completeness");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "generate sar", "security assessment report"))
        {
            var tool = FindToolByName("compliance_generate_sar");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        // Phase 5: Authorize
        if (ContainsAny(lowerMessage, "issue authorization", "issue ato", "grant ato", "authorize system"))
        {
            var systemId = GetContextValue(context, "system_id");
            return BuildConversationalPrompt(
                "Issue Authorization Decision",
                "I need the authorization details to issue the decision.",
                new[]
                {
                    "**Decision Type** — ATO, AtoWithConditions, IATT, or DATO",
                    "**Residual Risk Level** — Low, Medium, High, or Critical",
                    "**Expiration Date** — ISO 8601 date (e.g. 2027-03-01)",
                    "**Terms and Conditions** — (optional) authorization conditions"
                },
                "Issue ATO for eagle eye, residual risk Low, expires 2027-03-01",
                systemId);
        }

        if (ContainsAny(lowerMessage, "accept risk", "risk acceptance", "risk decision"))
        {
            var tool = FindToolByName("compliance_accept_risk");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "risk register", "show risks", "view risks"))
        {
            var tool = FindToolByName("compliance_show_risk_register");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "create poam", "new poam", "add poam item"))
        {
            var systemId = GetContextValue(context, "system_id");
            return BuildConversationalPrompt(
                "Create POA&M Item",
                "I need the weakness details to create a Plan of Action & Milestones entry.",
                new[]
                {
                    "**Weakness** — description of the weakness",
                    "**Control ID** — NIST 800-53 control (e.g. AC-2)",
                    "**Severity** — CatI, CatII, or CatIII",
                    "**Point of Contact** — responsible person",
                    "**Scheduled Completion** — target date (e.g. 2026-06-01)"
                },
                "Create POA&M for eagle eye: weakness 'No MFA on admin accounts', control AC-2, severity CatII, POC john.doe@agency.gov, due 2026-06-01",
                systemId);
        }

        if (ContainsAny(lowerMessage, "list poam", "show poam", "poam items", "view poam"))
        {
            var tool = FindToolByName("compliance_list_poam");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "generate rar", "risk assessment report"))
        {
            var tool = FindToolByName("compliance_generate_rar");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "bundle package", "authorization package", "ato package"))
        {
            var tool = FindToolByName("compliance_bundle_authorization_package");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        // Phase 6: Monitor
        if (ContainsAny(lowerMessage, "conmon plan", "continuous monitoring plan", "create monitoring plan"))
        {
            var systemId = GetContextValue(context, "system_id");
            return BuildConversationalPrompt(
                "Create Continuous Monitoring Plan",
                "I need the monitoring plan details.",
                new[]
                {
                    "**Assessment Frequency** — Monthly, Quarterly, or Annually",
                    "**Annual Review Date** — ISO 8601 date (e.g. 2026-12-15)"
                },
                "Create ConMon plan for eagle eye, frequency Monthly, annual review 2026-12-15",
                systemId);
        }

        if (ContainsAny(lowerMessage, "conmon report", "monitoring report", "generate conmon"))
        {
            var tool = FindToolByName("compliance_generate_conmon_report");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "significant change", "report change", "system change"))
        {
            var tool = FindToolByName("compliance_report_significant_change");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "ato expiration", "track expiration", "when does ato expire"))
        {
            var tool = FindToolByName("compliance_track_ato_expiration");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        if (ContainsAny(lowerMessage, "multi system dashboard", "all systems dashboard", "portfolio dashboard"))
        {
            var tool = FindToolByName("compliance_multi_system_dashboard");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>(), cancellationToken);
        }

        if (ContainsAny(lowerMessage, "reauthorization", "reauthorize", "re-authorize"))
        {
            var tool = FindToolByName("compliance_reauthorization_workflow");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = GetContextValue(context, "system_id")
                }, cancellationToken);
        }

        // RMF status (cross-phase)
        if (ContainsAny(lowerMessage, "rmf status", "rmf phase", "rmf progress", "where are we in rmf"))
        {
            var tool = FindToolByName("compliance_list_systems");
            if (tool != null)
                return await tool.ExecuteAsync(new Dictionary<string, object?>(), cancellationToken);
        }

        // Default: return compliance status
        return await _statusTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["subscription_id"] = GetContextValue(context, "subscription_id")
        }, cancellationToken);
    }

    // ─── Auth-Gate: PIM Inline Activation (FR-019 / T048) ────────────────────

    /// <summary>
    /// Maps user intent keywords to the tool name that would be invoked.
    /// Used to determine if the target operation requires Tier 2 auth.
    /// </summary>
    private static string? ResolveTargetToolName(string lowerMessage)
    {
        // Tier 2 compliance tools
        if (ContainsAny(lowerMessage, "assess", "scan", "audit", "check compliance", "run assessment")) return "run_assessment";
        if (ContainsAny(lowerMessage, "collect evidence", "evidence collection", "gather evidence")) return "collect_evidence";
        if (ContainsAny(lowerMessage, "remediate", "fix finding", "apply fix")) return "execute_remediation";
        if (ContainsAny(lowerMessage, "validate remediation", "verify fix")) return "compliance_validate_remediation";
        if (ContainsAny(lowerMessage, "monitor", "alert", "continuous")) return "compliance_monitoring";

        // Tier 2 kanban tools
        if (ContainsAny(lowerMessage, "remediate task", "run remediation", "execute fix", "apply remediation")) return "kanban_remediate_task";
        if (ContainsAny(lowerMessage, "validate task")) return "kanban_validate_task";
        if (ContainsAny(lowerMessage, "collect evidence", "task evidence")) return "kanban_collect_evidence";

        // PIM tools are themselves Tier 2 but handled via PIM routing directly
        return null;
    }

    /// <summary>
    /// Maps a tool name to the Azure RBAC role typically required for that operation.
    /// </summary>
    private static string? GetRequiredRoleForTool(string toolName) => toolName switch
    {
        "run_assessment" => "Reader",
        "collect_evidence" => "Reader",
        "compliance_monitoring" => "Reader",
        "execute_remediation" => "Contributor",
        "compliance_validate_remediation" => "Reader",
        "kanban_remediate_task" => "Contributor",
        "kanban_validate_task" => "Reader",
        "kanban_collect_evidence" => "Reader",
        _ => null
    };

    /// <summary>
    /// Checks if the user needs PIM activation before executing a Tier 2 operation.
    /// Returns a JSON response with inline activation offer if eligible, null if no gate needed.
    /// Per FR-019: detects missing RBAC role, checks PIM eligibility, offers inline activation.
    /// </summary>
    private async Task<string?> CheckAuthGateAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken)
    {
        var lowerMessage = message.ToLowerInvariant();
        var targetTool = ResolveTargetToolName(lowerMessage);

        // Not a Tier 2 operation or not a tool we track
        if (targetTool == null || !AuthTierClassification.IsTier2(targetTool))
            return null;

        var userId = GetContextValue(context, "user_id");
        if (string.IsNullOrEmpty(userId))
            return null; // Auth check handled by middleware

        // Check if user already has active PIM roles for this scope
        var scope = GetContextValue(context, "subscription_id") ?? "default";
        var requiredRole = GetRequiredRoleForTool(targetTool);
        if (requiredRole == null)
            return null; // No role mapping for this tool

        using var serviceScope = _scopeFactory.CreateScope();
        var pimService = serviceScope.ServiceProvider.GetRequiredService<IPimService>();

        // Check if user already has the required role active
        var activeRoles = await pimService.ListActiveRolesAsync(userId, cancellationToken);
        var hasActiveRole = activeRoles.Any(r =>
            r.RoleName.Contains(requiredRole, StringComparison.OrdinalIgnoreCase) &&
            (r.Scope.Contains(scope, StringComparison.OrdinalIgnoreCase) ||
             scope.Equals("default", StringComparison.OrdinalIgnoreCase)));

        if (hasActiveRole)
            return null; // User has the needed role, proceed with tool execution

        // Check PIM eligibility
        var eligibleRoles = await pimService.ListEligibleRolesAsync(userId, scope, cancellationToken);
        var matchingRole = eligibleRoles.FirstOrDefault(r =>
            r.RoleName.Contains(requiredRole, StringComparison.OrdinalIgnoreCase));

        if (matchingRole == null)
            return null; // No eligible role found, let the tool handle the error

        // Store the intent for post-activation continuation
        context.WorkflowState["pending_tool"] = targetTool;
        context.WorkflowState["pending_role"] = matchingRole.RoleName;
        context.WorkflowState["pending_scope"] = scope;

        Logger.LogInformation(
            "Auth-gate: User {UserId} needs {Role} for {Tool}. Offering inline PIM activation.",
            userId, matchingRole.RoleName, targetTool);

        return JsonSerializer.Serialize(new
        {
            status = "auth_gate",
            data = new
            {
                message = $"The operation '{targetTool}' requires the '{matchingRole.RoleName}' role on scope '{scope}'.",
                eligibleRole = matchingRole.RoleName,
                scope,
                requiresApproval = matchingRole.RequiresApproval,
                maxDuration = matchingRole.MaxDuration,
                suggestion = matchingRole.RequiresApproval
                    ? $"You are eligible for '{matchingRole.RoleName}' but it requires approval. Say 'activate role {matchingRole.RoleName}' to submit an activation request."
                    : $"You are eligible for '{matchingRole.RoleName}'. Say 'activate role {matchingRole.RoleName}' to activate it and proceed.",
                action = "pim_activate_role"
            },
            metadata = new { toolName = targetTool, agentName = AgentName }
        });
    }

    // ─── Post-Operation Deactivation Offer (FR-020/FR-021 / T049) ────────────

    /// <summary>
    /// Checks if a PIM role was activated inline during this conversation and offers deactivation
    /// after successful Tier 2 operation completion.
    /// Per FR-020: offers deactivation after triggering operation completes.
    /// Per FR-021: respects AutoDeactivateAfterRemediation config.
    /// </summary>
    private async Task<string> AppendDeactivationOfferAsync(
        string toolResult,
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken)
    {
        var lowerMessage = message.ToLowerInvariant();
        var targetTool = ResolveTargetToolName(lowerMessage);

        // Only apply to Tier 2 operations
        if (targetTool == null || !AuthTierClassification.IsTier2(targetTool))
            return toolResult;

        // Check if there's a PIM role that was activated inline during this conversation
        var inlineActivatedRole = GetContextValue(context, "inline_activated_role");
        var inlineActivatedScope = GetContextValue(context, "inline_activated_scope");

        if (string.IsNullOrEmpty(inlineActivatedRole))
            return toolResult;

        // Check if AutoDeactivateAfterRemediation is enabled for remediation operations
        var isRemediation = ContainsAny(lowerMessage, "remediate", "fix finding", "apply fix",
            "remediate task", "run remediation", "execute fix", "apply remediation");

        using var serviceScope = _scopeFactory.CreateScope();
        var pimOptions = serviceScope.ServiceProvider.GetRequiredService<IOptions<PimServiceOptions>>();

        if (isRemediation && pimOptions.Value.AutoDeactivateAfterRemediation)
        {
            // Auto-deactivate per FR-021
            var userId = GetContextValue(context, "user_id");
            if (!string.IsNullOrEmpty(userId))
            {
                var pimService = serviceScope.ServiceProvider.GetRequiredService<IPimService>();
                var deactivateResult = await pimService.DeactivateRoleAsync(
                    userId, inlineActivatedRole, inlineActivatedScope ?? "default", cancellationToken);

                // Clear the inline activation tracking
                context.WorkflowState.Remove("inline_activated_role");
                context.WorkflowState.Remove("inline_activated_scope");

                Logger.LogInformation(
                    "Auto-deactivated PIM role {Role} after remediation (FR-021)",
                    inlineActivatedRole);

                // Parse and augment the tool result with deactivation info
                try
                {
                    var doc = JsonDocument.Parse(toolResult);
                    var resultObj = new Dictionary<string, object?>
                    {
                        ["status"] = doc.RootElement.GetProperty("status").GetString(),
                        ["data"] = JsonSerializer.Deserialize<object>(doc.RootElement.GetProperty("data").GetRawText()),
                        ["deactivation"] = new
                        {
                            autoDeactivated = true,
                            role = inlineActivatedRole,
                            message = $"PIM role '{inlineActivatedRole}' was automatically deactivated after remediation."
                        },
                        ["metadata"] = JsonSerializer.Deserialize<object>(doc.RootElement.GetProperty("metadata").GetRawText())
                    };
                    return JsonSerializer.Serialize(resultObj);
                }
                catch
                {
                    return toolResult;
                }
            }
        }

        // Offer to deactivate per FR-020
        try
        {
            var doc = JsonDocument.Parse(toolResult);
            var resultObj = new Dictionary<string, object?>
            {
                ["status"] = doc.RootElement.GetProperty("status").GetString(),
                ["data"] = JsonSerializer.Deserialize<object>(doc.RootElement.GetProperty("data").GetRawText()),
                ["deactivation_offer"] = new
                {
                    role = inlineActivatedRole,
                    scope = inlineActivatedScope,
                    message = $"Operation complete. The PIM role '{inlineActivatedRole}' is still active. Say 'deactivate role {inlineActivatedRole}' to restore least-privilege access."
                },
                ["metadata"] = JsonSerializer.Deserialize<object>(doc.RootElement.GetProperty("metadata").GetRawText())
            };
            return JsonSerializer.Serialize(resultObj);
        }
        catch
        {
            return toolResult;
        }
    }

    /// <summary>Returns true if the text contains any of the specified keywords (case-insensitive).</summary>
    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>Retrieves a context value from the agent conversation workflow state.</summary>
    private static string? GetContextValue(AgentConversationContext context, string key)
    {
        if (context.WorkflowState.TryGetValue(key, out var value))
            return value?.ToString();

        // Dashboard context commonly uses camelCase (e.g., systemId) while
        // tools may request snake_case (e.g., system_id). Try both forms.
        var alternateKey = key.Contains('_', StringComparison.Ordinal)
            ? SnakeToCamel(key)
            : CamelToSnake(key);

        if (!string.IsNullOrEmpty(alternateKey) && context.WorkflowState.TryGetValue(alternateKey, out value))
            return value?.ToString();

        return null;
    }

    private static string SnakeToCamel(string key)
    {
        var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return key;

        var sb = new StringBuilder(parts[0]);
        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
                continue;
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                sb.Append(part[1..]);
        }

        return sb.ToString();
    }

    private static string CamelToSnake(string key)
    {
        if (string.IsNullOrEmpty(key))
            return key;

        var sb = new StringBuilder(key.Length + 4);
        for (var i = 0; i < key.Length; i++)
        {
            var ch = key[i];
            if (char.IsUpper(ch) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    private static string? ExtractSystemNameFromForClauses(string message)
    {
        // Capture each "for <candidate>" chunk and prefer the last one.
        // This handles prompts like: "... for this system for Eagle Eye".
        var matches = Regex.Matches(message, @"\bfor\s+([^,.;!?]+?)(?=\s+for\s+|$)", RegexOptions.IgnoreCase);
        if (matches.Count == 0)
            return null;

        var candidate = matches[^1].Groups[1].Value.Trim();
        if (string.IsNullOrEmpty(candidate))
            return null;

        // Remove common filler wording from suggestion templates.
        if (candidate.Equals("this system", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("system", StringComparison.OrdinalIgnoreCase))
            return null;

        return candidate;
    }

    /// <summary>Extracts the NIST control family abbreviation from the user message.</summary>
    private static string ExtractControlFamily(string message)
    {
        var families = new[] { "AC", "AU", "AT", "CM", "CP", "IA", "IR", "MA", "MP", "PE", "PL", "PM", "PS", "RA", "SA", "SC", "SI", "SR" };
        foreach (var family in families)
        {
            if (message.Contains(family, StringComparison.OrdinalIgnoreCase))
                return family;
        }
        return "AC";
    }

    /// <summary>Extracts an Azure role name from user message text.</summary>
    private static string? ExtractRoleName(string message)
    {
        var roles = new[] { "Owner", "Contributor", "Reader", "User Access Administrator",
            "Security Administrator", "Global Administrator", "Privileged Role Administrator" };
        foreach (var role in roles)
        {
            if (message.Contains(role, StringComparison.OrdinalIgnoreCase))
                return role;
        }
        return null;
    }

    /// <summary>Extracts a numeric hours value from user message text (e.g., "extend by 2 hours" → 2).</summary>
    private static int ExtractHours(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"(\d+)\s*hour");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var hours))
            return hours;
        return 2; // Default extension of 2 hours
    }

    /// <summary>Extracts a GUID request ID from user message text.</summary>
    private static string? ExtractRequestId(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return match.Success ? match.Value : null;
    }

    /// <summary>Extracts a VM name from user message text (e.g., "SSH access to vm-web01" → "vm-web01").</summary>
    private static string? ExtractVmName(string message)
    {
        // Match common VM naming patterns: vm-xxx, hostname with dots, or alphanumeric-with-dashes
        var match = System.Text.RegularExpressions.Regex.Match(message, @"(?:to|for|on|access)\s+([a-zA-Z][a-zA-Z0-9\-\.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value;
        return null;
    }

    /// <summary>Extracts a platform role name from user message text (e.g., "map cert as Auditor" → "Auditor").</summary>
    private static string? ExtractRole(string message)
    {
        var roles = new[] { "Administrator", "Admin", "Auditor", "Analyst", "Viewer",
            "SecurityLead", "Security Lead", "PlatformEngineer", "Platform Engineer", "Engineer" };
        foreach (var role in roles)
        {
            if (message.Contains(role, StringComparison.OrdinalIgnoreCase))
                return role;
        }
        return null;
    }

    /// <summary>Finds a registered tool by name from the Tools collection.</summary>
    private BaseTool? FindToolByName(string toolName) =>
        Tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves a system_id from the user message by extracting a system name
    /// (e.g. "for ACME Portal") and looking it up in the database.
    /// When no system name is found in the message, falls back to querying all
    /// active systems — auto-selects if exactly one exists.
    /// Returns the GUID string if found, null otherwise.
    /// </summary>
    private async Task<string?> ResolveSystemIdFromMessageAsync(string message, CancellationToken ct)
    {
        // 1. Try quoted value first: 'My System' or "My System"
        var name = ExtractQuotedValue(message);

        // 2. Try "for <SystemName>" pattern (case-insensitive)
        if (string.IsNullOrEmpty(name))
        {
            name = ExtractSystemNameFromForClauses(message);
        }

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // If we extracted a name, look it up directly
            if (!string.IsNullOrEmpty(name))
            {
                var system = await db.RegisteredSystems
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Name.ToLower() == name.ToLower() && s.IsActive, ct);

                if (system != null)
                {
                    Logger.LogInformation("Resolved system name '{Name}' to ID {Id}", name, system.Id);
                    return system.Id.ToString();
                }

                Logger.LogDebug("Could not resolve system name '{Name}' — no matching active system found", name);
            }

            // No name in message — query all active systems
            var activeSystems = await db.RegisteredSystems
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderByDescending(s => s.CreatedAt)
                .Take(10)
                .ToListAsync(ct);

            if (activeSystems.Count == 1)
            {
                // Only one system — auto-select
                Logger.LogInformation("Auto-selected single active system '{Name}' ({Id})",
                    activeSystems[0].Name, activeSystems[0].Id);
                return activeSystems[0].Id.ToString();
            }

            if (activeSystems.Count > 1)
            {
                // Multiple systems — store them for the error-formatting layer to
                // produce a user-friendly "which system?" prompt.
                _pendingSystemChoices = activeSystems
                    .Select(s => new { s.Id, s.Name, s.CurrentRmfStep })
                    .Cast<object>()
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error resolving system from message");
        }

        return null;
    }

    /// <summary>
    /// When multiple active systems exist and no system was specified,
    /// this list is populated so the response can prompt the user to choose.
    /// Reset after each call to RouteToToolAsync.
    /// </summary>
    private List<object>? _pendingSystemChoices;

    /// <summary>
    /// Builds a friendly JSON response listing active systems so the user can
    /// re-issue the command with a specific system name.
    /// </summary>
    private static string BuildSystemDisambiguationResponse(string originalMessage, List<object> systems)
    {
        // Extract the action verb from the message for the prompt examples
        var actionPhrase = originalMessage.Trim();

        var systemLines = new List<string>();
        foreach (var s in systems)
        {
            // Use reflection to get Name/RmfStep from anonymous type
            var type = s.GetType();
            var name = type.GetProperty("Name")?.GetValue(s)?.ToString() ?? "Unknown";
            var step = type.GetProperty("CurrentRmfStep")?.GetValue(s)?.ToString() ?? "Prepare";
            systemLines.Add($"- **{name}** (Phase: {step})");
        }

        var firstName = systems.First().GetType().GetProperty("Name")?.GetValue(systems.First())?.ToString() ?? "MySystem";

        return JsonSerializer.Serialize(new
        {
            success = false,
            error = "SYSTEM_REQUIRED",
            message = $"You have multiple registered systems. Please specify which system you mean.\n\n" +
                      string.Join("\n", systemLines) +
                      $"\n\nTry: *\"{actionPhrase} for {firstName}\"*"
        });
    }

    /// <summary>Returns a conversational JSON response asking the user for required parameters.</summary>
    private static string BuildConversationalPrompt(
        string title,
        string description,
        string[] requiredFields,
        string example,
        string? systemId)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {title}\n");
        sb.AppendLine(description + "\n");
        sb.AppendLine("**I need the following information:**\n");
        foreach (var field in requiredFields)
            sb.AppendLine($"- {field}");

        if (string.IsNullOrEmpty(systemId))
            sb.AppendLine("\n- **System Name** — which system this applies to");

        sb.AppendLine($"\n**Example:** *\"{example}\"*");

        return JsonSerializer.Serialize(new
        {
            success = true,
            conversational = true,
            message = sb.ToString()
        });
    }

    /// <summary>Extracts a single-quoted or double-quoted value from the message.</summary>
    private static string? ExtractQuotedValue(string message)
    {
        // Try single quotes first, then double quotes
        var match = Regex.Match(message, @"[''](.*?)['']|[""](.*?)[""]");
        if (match.Success)
            return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return null;
    }

    /// <summary>Extracts an enum value from the message by matching known keywords.</summary>
    private static string ExtractEnumValue(string message, string[] knownValues, string defaultValue)
    {
        // Normalize hyphens/spaces
        var normalized = message.Replace("-", "").Replace(" ", "");
        foreach (var v in knownValues)
        {
            var normV = v.Replace("-", "").Replace(" ", "");
            if (normalized.Contains(normV, StringComparison.OrdinalIgnoreCase))
            {
                // Return PascalCase version
                return v switch
                {
                    "majorapplication" => "MajorApplication",
                    "enclave" => "Enclave",
                    "platformit" => "PlatformIt",
                    "missioncritical" or "mission-critical" => "MissionCritical",
                    "missionessential" or "mission-essential" => "MissionEssential",
                    "missionsupport" or "mission-support" => "MissionSupport",
                    "azuregovernment" or "azure government" => "AzureGovernment",
                    "azurecommercial" or "azure commercial" => "AzureCommercial",
                    "onpremises" or "on-premises" => "OnPremises",
                    "hybrid" => "Hybrid",
                    _ => defaultValue
                };
            }
        }
        return defaultValue;
    }

    /// <summary>Extracts the document type (ssp, poam, sar) from the user message.</summary>
    private static string ExtractDocumentType(string message)
    {
        if (message.Contains("ssp", StringComparison.OrdinalIgnoreCase)) return "ssp";
        if (message.Contains("poam", StringComparison.OrdinalIgnoreCase) || message.Contains("poa&m", StringComparison.OrdinalIgnoreCase)) return "poam";
        if (message.Contains("sar", StringComparison.OrdinalIgnoreCase)) return "sar";
        return "ssp";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // US12 — RMF Step-Aware Routing
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Looks up the current system's RMF step from the conversation context and
    /// stores it in <see cref="_activeRmfStep"/> for <see cref="GetSystemPrompt"/>.
    /// Falls back gracefully if no system_id is present.
    /// </summary>
    private async Task ResolveRmfStepContextAsync(
        AgentConversationContext context,
        CancellationToken cancellationToken)
    {
        var systemId = GetContextValue(context, "system_id");
        if (string.IsNullOrEmpty(systemId))
        {
            _activeRmfStep.Value = null;
            _activeSystemName.Value = null;
            return;
        }

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var system = await db.RegisteredSystems
                .AsNoTracking()
                .Where(s => s.Id == systemId && s.IsActive)
                .Select(s => new { s.CurrentRmfStep, s.Name })
                .FirstOrDefaultAsync(cancellationToken);

            if (system != null)
            {
                _activeRmfStep.Value = system.CurrentRmfStep;
                _activeSystemName.Value = system.Name;
                Logger.LogDebug(
                    "RMF step routing: System {SystemId} ({SystemName}) is at {RmfStep}",
                    systemId, system.Name, system.CurrentRmfStep);
            }
            else
            {
                _activeRmfStep.Value = null;
                _activeSystemName.Value = null;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to resolve RMF step for system {SystemId}", systemId);
            _activeRmfStep.Value = null;
            _activeSystemName.Value = null;
        }
    }

    /// <summary>
    /// Returns a dynamic prompt supplement that tells the LLM which RMF step the
    /// active system is on, which tools are most relevant, and what the next step is.
    /// Appended to the base system prompt by <see cref="GetSystemPrompt"/>.
    /// </summary>
    internal static string GetRmfStepContextSupplement(RmfPhase step, string? systemName)
    {
        var systemLabel = string.IsNullOrEmpty(systemName) ? "The active system" : $"System \"{systemName}\"";
        var stepDisplay = Core.Constants.ComplianceFrameworks.GetStepDisplayName(step);

        var supplement = $"\n\n## ⚡ Active System Context\n{systemLabel} is currently at **{stepDisplay}**.\n";

        supplement += step switch
        {
            RmfPhase.Prepare =>
                "**Prioritize**: `compliance_register_system`, `compliance_define_boundary`, " +
                "`compliance_assign_rmf_role`, `compliance_list_rmf_roles`.\n" +
                "**Suggest next**: Categorize → `compliance_categorize_system`.\n",

            RmfPhase.Categorize =>
                "**Prioritize**: `compliance_categorize_system`, `compliance_get_categorization`, " +
                "`compliance_suggest_info_types`.\n" +
                "**Suggest next**: Select baseline → `compliance_select_baseline`.\n",

            RmfPhase.Select =>
                "**Prioritize**: `compliance_select_baseline`, `compliance_tailor_baseline`, " +
                "`compliance_set_inheritance`, `compliance_get_baseline`, `compliance_generate_crm`.\n" +
                "**Suggest next**: Implement controls → `compliance_write_narrative`.\n",

            RmfPhase.Implement =>
                "**Prioritize**: `compliance_write_narrative`, `compliance_suggest_narrative`, " +
                "`compliance_batch_populate_narratives`, `compliance_narrative_progress`, " +
                "`compliance_generate_ssp`, `compliance_remediate`.\n" +
                "**Suggest next**: Assess controls → `compliance_assess_control`.\n",

            RmfPhase.Assess =>
                "**Prioritize**: `compliance_assess_control`, `compliance_take_snapshot`, " +
                "`compliance_compare_snapshots`, `compliance_verify_evidence`, " +
                "`compliance_generate_sar`, `compliance_collect_evidence`.\n" +
                "**Suggest next**: Build authorization package → `compliance_bundle_authorization_package`.\n",

            RmfPhase.Authorize =>
                "**Prioritize**: `compliance_issue_authorization`, `compliance_accept_risk`, " +
                "`compliance_show_risk_register`, `compliance_create_poam`, `compliance_generate_rar`, " +
                "`compliance_bundle_authorization_package`.\n" +
                "**Suggest next**: Set up monitoring → `compliance_create_conmon_plan`.\n",

            RmfPhase.Monitor =>
                "**Prioritize**: `compliance_create_conmon_plan`, `compliance_generate_conmon_report`, " +
                "`compliance_track_ato_expiration`, `compliance_multi_system_dashboard`, " +
                "`compliance_export_emass`, `compliance_export_oscal`, `watch_enable_monitoring`.\n" +
                "**Suggest next**: Check `compliance_track_ato_expiration` for reauthorization timeline.\n",

            _ => ""
        };

        supplement += "Always accept cross-step requests — the step context is advisory, not restrictive.\n";
        return supplement;
    }

    /// <summary>
    /// Maps each RMF phase to its prioritized set of tool names.
    /// Used by deterministic routing and test validation.
    /// </summary>
    internal static IReadOnlyList<string> GetPrioritizedToolsForStep(RmfPhase step)
    {
        return step switch
        {
            RmfPhase.Prepare => new[]
            {
                "compliance_register_system", "compliance_list_systems", "compliance_get_system",
                "compliance_define_boundary", "compliance_exclude_from_boundary",
                "compliance_assign_rmf_role", "compliance_list_rmf_roles"
            },
            RmfPhase.Categorize => new[]
            {
                "compliance_categorize_system", "compliance_get_categorization",
                "compliance_suggest_info_types"
            },
            RmfPhase.Select => new[]
            {
                "compliance_select_baseline", "compliance_tailor_baseline",
                "compliance_set_inheritance", "compliance_get_baseline",
                "compliance_generate_crm", "compliance_show_stig_mapping"
            },
            RmfPhase.Implement => new[]
            {
                "compliance_write_narrative", "compliance_suggest_narrative",
                "compliance_batch_populate_narratives", "compliance_narrative_progress",
                "compliance_generate_ssp", "compliance_generate_document",
                "compliance_remediate", "compliance_generate_plan"
            },
            RmfPhase.Assess => new[]
            {
                "compliance_assess_control", "compliance_take_snapshot",
                "compliance_compare_snapshots", "compliance_verify_evidence",
                "compliance_check_evidence_completeness", "compliance_generate_sar",
                "compliance_assess", "compliance_collect_evidence"
            },
            RmfPhase.Authorize => new[]
            {
                "compliance_issue_authorization", "compliance_accept_risk",
                "compliance_show_risk_register", "compliance_create_poam", "compliance_list_poam",
                "compliance_generate_rar", "compliance_bundle_authorization_package",
                "compliance_upload_template", "compliance_generate_document"
            },
            RmfPhase.Monitor => new[]
            {
                "compliance_create_conmon_plan", "compliance_generate_conmon_report",
                "compliance_report_significant_change", "compliance_track_ato_expiration",
                "compliance_multi_system_dashboard", "compliance_reauthorization_workflow",
                "compliance_send_notification", "compliance_export_emass", "compliance_export_oscal",
                "watch_enable_monitoring", "watch_show_alerts", "watch_compliance_trend"
            },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// Classifies the user message intent for audit logging.
    /// </summary>
    private static string ClassifyIntent(string message)
    {
        var lower = message.ToLowerInvariant();
        if (ContainsAny(lower, "register system", "register a new system", "new system")) return "RmfRegistration";
        if (ContainsAny(lower, "list systems", "show systems", "registered systems", "my systems")) return "RmfListSystems";
        if (ContainsAny(lower, "categorize", "categorization", "fips 199")) return "RmfCategorize";
        if (ContainsAny(lower, "select baseline", "control baseline", "tailor baseline")) return "RmfBaseline";
        if (ContainsAny(lower, "narrative", "ssp progress")) return "RmfImplement";
        if (ContainsAny(lower, "assess control", "snapshot", "evidence completeness")) return "RmfAssess";
        if (ContainsAny(lower, "issue authorization", "issue ato", "grant ato", "authorize system")) return "RmfAuthorize";
        if (ContainsAny(lower, "accept risk", "risk register", "risk acceptance")) return "RmfRisk";
        if (ContainsAny(lower, "conmon", "significant change", "ato expiration", "reauthoriz")) return "RmfMonitor";
        if (ContainsAny(lower, "advance rmf", "advance phase", "next phase")) return "RmfAdvance";
        if (ContainsAny(lower, "rmf status", "rmf phase", "rmf progress")) return "RmfStatus";
        if (ContainsAny(lower, "boundary", "authorization boundary")) return "RmfBoundary";
        if (ContainsAny(lower, "rmf role", "assign issm", "assign isso", "assign ao")) return "RmfRoleAssignment";
        if (ContainsAny(lower, "assess", "scan")) return "Assessment";
        if (ContainsAny(lower, "remediat", "fix")) return "Remediation";
        if (ContainsAny(lower, "evidence", "collect")) return "EvidenceCollection";
        if (ContainsAny(lower, "document", "ssp", "sar", "poam")) return "DocumentGeneration";
        if (ContainsAny(lower, "monitor", "alert")) return "Monitoring";
        if (ContainsAny(lower, "audit", "log")) return "AuditQuery";
        if (ContainsAny(lower, "history", "trend")) return "HistoryQuery";
        if (ContainsAny(lower, "status", "posture")) return "StatusQuery";
        if (ContainsAny(lower, "control", "nist")) return "ControlQuery";
        if (ContainsAny(lower, "board", "kanban")) return "KanbanQuery";
        if (ContainsAny(lower, "task", "rem-", "assign", "move task")) return "KanbanTask";
        if (ContainsAny(lower, "comment")) return "KanbanComment";
        if (ContainsAny(lower, "bulk")) return "KanbanBulk";
        if (ContainsAny(lower, "export", "csv", "poam export")) return "KanbanExport";
        return "GeneralQuery";
    }

    /// <summary>
    /// Persists an audit log entry to the database.
    /// </summary>
    private async Task LogAuditEntryAsync(
        string action,
        string? subscriptionId,
        AuditOutcome outcome,
        string details,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            db.AuditLogs.Add(new AuditLogEntry
            {
                UserId = "system",
                UserRole = "Agent",
                Action = action,
                SubscriptionId = subscriptionId,
                Outcome = outcome,
                Details = details,
                Duration = duration
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit logging should never fail the main operation
            Logger.LogWarning(ex, "Failed to persist audit log entry for action {Action}", action);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Context-aware task resolution (US14)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Key used to store the last displayed task ID list in workflow state.</summary>
    public const string LastResultsKey = "kanban:lastResults";

    /// <summary>
    /// Ordinal patterns mapped to zero-based index.
    /// Keys are regex patterns, values are the resolved index.
    /// </summary>
    private static readonly (Regex Pattern, int Index)[] OrdinalPatterns =
    [
        (new Regex(@"\b(the\s+)?first(\s+one|\s+task)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 0),
        (new Regex(@"\b(the\s+)?second(\s+one|\s+task)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 1),
        (new Regex(@"\b(the\s+)?third(\s+one|\s+task)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 2),
        (new Regex(@"\b(the\s+)?fourth(\s+one|\s+task)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 3),
        (new Regex(@"\b(the\s+)?fifth(\s+one|\s+task)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), 4),
        (new Regex(@"\b(the\s+)?last(\s+one|\s+task)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), -1),
    ];

    /// <summary>
    /// Resolves an ordinal reference ("first", "second", "last") to a task ID
    /// from the previously stored task list. Returns null if no match or no context.
    /// </summary>
    public static string? ResolveTaskFromContext(string lowerMessage, AgentConversationContext context)
    {
        // Only attempt resolution if context has stored results
        if (!context.WorkflowState.TryGetValue(LastResultsKey, out var stored) || stored is not List<string> taskIds || taskIds.Count == 0)
            return null;

        foreach (var (pattern, index) in OrdinalPatterns)
        {
            if (!pattern.IsMatch(lowerMessage)) continue;

            var resolvedIndex = index == -1 ? taskIds.Count - 1 : index;
            if (resolvedIndex >= 0 && resolvedIndex < taskIds.Count)
            {
                return taskIds[resolvedIndex];
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts task IDs from a JSON tool result (standard envelope format)
    /// and stores them in the conversation context for ordinal resolution.
    /// </summary>
    public static void StoreTaskListResults(string toolResult, AgentConversationContext context)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var data))
            {
                var taskIds = new List<string>();

                // Handle array of tasks in data
                if (data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var id))
                            taskIds.Add(id.GetString() ?? "");
                        else if (item.TryGetProperty("taskId", out var taskId))
                            taskIds.Add(taskId.GetString() ?? "");
                    }
                }
                // Handle object with tasks array inside
                else if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("tasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in tasks.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var id))
                            taskIds.Add(id.GetString() ?? "");
                        else if (item.TryGetProperty("taskId", out var taskId))
                            taskIds.Add(taskId.GetString() ?? "");
                    }
                }

                if (taskIds.Count > 0)
                {
                    context.WorkflowState[LastResultsKey] = taskIds;
                }
            }
        }
        catch
        {
            // Non-JSON results or parse errors — silently skip context storage
        }
    }
}

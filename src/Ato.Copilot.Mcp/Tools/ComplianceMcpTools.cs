using System.ComponentModel;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Agents.Compliance.Tools.Poam;

namespace Ato.Copilot.Mcp.Tools;

/// <summary>
/// MCP tools for compliance operations. Wraps Agent Framework compliance tools
/// for exposure via the MCP protocol (GitHub Copilot, Claude Desktop, etc.)
/// </summary>
public class ComplianceMcpTools
{
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

    // Kanban tools
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

    // Auth/PIM tools
    private readonly CacStatusTool _cacStatus;
    private readonly CacSignOutTool _cacSignOut;

    // CAC session config (Phase 10 — US8)
    private readonly CacSetTimeoutTool _cacSetTimeout;

    // Certificate mapping (Phase 11 — US9)
    private readonly CacMapCertificateTool _cacMapCertificate;

    // PIM role management tools
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

    // Compliance Watch notification & escalation tools (Feature 005 — US4)
    private readonly WatchConfigureNotificationsTool _watchConfigureNotifications;
    private readonly WatchConfigureEscalationTool _watchConfigureEscalation;

    // Compliance Watch dashboard & reporting tools (Feature 005 — US5)
    private readonly WatchAlertHistoryTool _watchAlertHistory;
    private readonly WatchComplianceTrendTool _watchComplianceTrend;
    private readonly WatchAlertStatisticsTool _watchAlertStatistics;

    // Compliance Watch integration tools (Feature 005 — US8)
    private readonly WatchCreateTaskFromAlertTool _watchCreateTaskFromAlert;
    private readonly WatchCollectEvidenceFromAlertTool _watchCollectEvidenceFromAlert;

    // Compliance Watch auto-remediation tools (Feature 005 — US9)
    private readonly WatchCreateAutoRemediationRuleTool _watchCreateAutoRemediationRule;
    private readonly WatchListAutoRemediationRulesTool _watchListAutoRemediationRules;

    // NIST Controls knowledge tools (Feature 007)
    private readonly NistControlSearchTool _nistControlSearchTool;
    private readonly NistControlExplainerTool _nistControlExplainerTool;

    // IaC Compliance Scan tool (Feature 014)
    private readonly IacComplianceScanTool _iacComplianceScanTool;

    // RMF Registration tools (Feature 015)
    private readonly RegisterSystemTool _registerSystemTool;
    private readonly ListSystemsTool _listSystemsTool;
    private readonly GetSystemTool _getSystemTool;
    private readonly AdvanceRmfStepTool _advanceRmfStepTool;
    private readonly DefineBoundaryTool _defineBoundaryTool;
    private readonly ExcludeFromBoundaryTool _excludeFromBoundaryTool;
    private readonly AssignRmfRoleTool _assignRmfRoleTool;
    private readonly ListRmfRolesTool _listRmfRolesTool;

    // Boundary Definition tools (Feature 033)
    private readonly ListBoundaryDefinitionsTool _listBoundaryDefinitionsTool;
    private readonly CreateBoundaryDefinitionTool _createBoundaryDefinitionTool;
    private readonly DeleteBoundaryDefinitionTool _deleteBoundaryDefinitionTool;
    private readonly BoundaryGapAnalysisTool _boundaryGapAnalysisTool;

    // RMF Categorization tools (Feature 015 - US2)
    private readonly CategorizeSystemTool _categorizeSystemTool;
    private readonly GetCategorizationTool _getCategorizationTool;
    private readonly SuggestInfoTypesTool _suggestInfoTypesTool;

    // RMF Baseline tools (Feature 015 - US3)
    private readonly SelectBaselineTool _selectBaselineTool;
    private readonly TailorBaselineTool _tailorBaselineTool;
    private readonly SetInheritanceTool _setInheritanceTool;
    private readonly GetBaselineTool _getBaselineTool;
    private readonly GenerateCrmTool _generateCrmTool;

    // RMF STIG Mapping tools (Feature 015 - US4)
    private readonly ShowStigMappingTool _showStigMappingTool;

    // SSP Authoring tools (Feature 015 - US5)
    private readonly WriteNarrativeTool _writeNarrativeTool;
    private readonly SuggestNarrativeTool _suggestNarrativeTool;
    private readonly BatchPopulateNarrativesTool _batchPopulateNarrativesTool;
    private readonly NarrativeProgressTool _narrativeProgressTool;
    private readonly GenerateSspTool _generateSspTool;

    // Assessment Artifact tools (Feature 015 - US7)
    private readonly AssessControlTool _assessControlTool;
    private readonly TakeSnapshotTool _takeSnapshotTool;
    private readonly CompareSnapshotsTool _compareSnapshotsTool;
    private readonly VerifyEvidenceTool _verifyEvidenceTool;
    private readonly CheckEvidenceCompletenessTool _checkEvidenceCompletenessTool;
    private readonly GenerateSarTool _generateSarTool;

    // Authorization Decision tools (Feature 015 - US8)
    private readonly IssueAuthorizationTool _issueAuthorizationTool;
    private readonly AcceptRiskTool _acceptRiskTool;
    private readonly ShowRiskRegisterTool _showRiskRegisterTool;
    private readonly CreatePoamTool _createPoamTool;
    private readonly ListPoamTool _listPoamTool;
    private readonly GetPoamTool _getPoamTool;
    private readonly UpdatePoamTool _updatePoamTool;
    private readonly ClosePoamTool _closePoamTool;
    private readonly UpdatePoamMilestoneTool _updatePoamMilestoneTool;
    private readonly BulkUpdatePoamTool _bulkUpdatePoamTool;
    private readonly LinkPoamTaskTool _linkPoamTaskTool;
    private readonly UnlinkPoamTaskTool _unlinkPoamTaskTool;
    private readonly CreateTaskFromPoamTool _createTaskFromPoamTool;
    private readonly GenerateRarTool _generateRarTool;
    private readonly BundleAuthorizationPackageTool _bundleAuthorizationPackageTool;

    // ─── US9: Continuous Monitoring tools ──────────────────────────────────
    private readonly CreateConMonPlanTool _createConMonPlanTool;
    private readonly GenerateConMonReportTool _generateConMonReportTool;
    private readonly ReportSignificantChangeTool _reportSignificantChangeTool;
    private readonly TrackAtoExpirationTool _trackAtoExpirationTool;
    private readonly MultiSystemDashboardTool _multiSystemDashboardTool;
    private readonly ReauthorizationWorkflowTool _reauthorizationWorkflowTool;
    private readonly NotificationDeliveryTool _notificationDeliveryTool;

    // ─── US10: eMASS & OSCAL tools ─────────────────────────────────────────
    private readonly ExportEmassTool _exportEmassTool;
    private readonly ImportEmassTool _importEmassTool;
    private readonly ExportOscalTool _exportOscalTool;

    // ─── US11: Document Templates & PDF Export tools ───────────────────────
    private readonly UploadTemplateTool _uploadTemplateTool;
    private readonly ListTemplatesTool _listTemplatesTool;
    private readonly UpdateTemplateTool _updateTemplateTool;
    private readonly DeleteTemplateTool _deleteTemplateTool;

    // ─── Feature 017: SCAP/STIG Import tools ──────────────────────────────
    private readonly ImportCklTool _importCklTool;
    private readonly ImportXccdfTool _importXccdfTool;
    private readonly ExportCklTool _exportCklTool;
    private readonly ListImportsTool _listImportsTool;
    private readonly GetImportSummaryTool _getImportSummaryTool;

    // ─── Feature 018: SAP Generation tools ─────────────────────────────────
    private readonly GenerateSapTool _generateSapTool;
    private readonly UpdateSapTool _updateSapTool;
    private readonly FinalizeSapTool _finalizeSapTool;
    private readonly GetSapTool _getSapTool;
    private readonly ListSapsTool _listSapsTool;

    // ─── Feature 019: Prisma Cloud Import tools ────────────────────────────
    private readonly ImportPrismaCsvTool _importPrismaCsvTool;
    private readonly ImportPrismaApiTool _importPrismaApiTool;
    private readonly ListPrismaPoliciesTool _listPrismaPoliciesTool;
    private readonly PrismaTrendTool _prismaTrendTool;

    public ComplianceMcpTools(
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
        IacComplianceScanTool iacComplianceScanTool,
        RegisterSystemTool registerSystemTool,
        ListSystemsTool listSystemsTool,
        GetSystemTool getSystemTool,
        AdvanceRmfStepTool advanceRmfStepTool,
        DefineBoundaryTool defineBoundaryTool,
        ExcludeFromBoundaryTool excludeFromBoundaryTool,
        AssignRmfRoleTool assignRmfRoleTool,
        ListRmfRolesTool listRmfRolesTool,
        ListBoundaryDefinitionsTool listBoundaryDefinitionsTool,
        CreateBoundaryDefinitionTool createBoundaryDefinitionTool,
        DeleteBoundaryDefinitionTool deleteBoundaryDefinitionTool,
        BoundaryGapAnalysisTool boundaryGapAnalysisTool,
        CategorizeSystemTool categorizeSystemTool,
        GetCategorizationTool getCategorizationTool,
        SuggestInfoTypesTool suggestInfoTypesTool,
        SelectBaselineTool selectBaselineTool,
        TailorBaselineTool tailorBaselineTool,
        SetInheritanceTool setInheritanceTool,
        GetBaselineTool getBaselineTool,
        GenerateCrmTool generateCrmTool,
        ShowStigMappingTool showStigMappingTool,
        WriteNarrativeTool writeNarrativeTool,
        SuggestNarrativeTool suggestNarrativeTool,
        BatchPopulateNarrativesTool batchPopulateNarrativesTool,
        NarrativeProgressTool narrativeProgressTool,
        GenerateSspTool generateSspTool,
        AssessControlTool assessControlTool,
        TakeSnapshotTool takeSnapshotTool,
        CompareSnapshotsTool compareSnapshotsTool,
        VerifyEvidenceTool verifyEvidenceTool,
        CheckEvidenceCompletenessTool checkEvidenceCompletenessTool,
        GenerateSarTool generateSarTool,
        IssueAuthorizationTool issueAuthorizationTool,
        AcceptRiskTool acceptRiskTool,
        ShowRiskRegisterTool showRiskRegisterTool,
        CreatePoamTool createPoamTool,
        ListPoamTool listPoamTool,
        GetPoamTool getPoamTool,
        UpdatePoamTool updatePoamTool,
        ClosePoamTool closePoamTool,
        UpdatePoamMilestoneTool updatePoamMilestoneTool,
        BulkUpdatePoamTool bulkUpdatePoamTool,
        LinkPoamTaskTool linkPoamTaskTool,
        UnlinkPoamTaskTool unlinkPoamTaskTool,
        CreateTaskFromPoamTool createTaskFromPoamTool,
        GenerateRarTool generateRarTool,
        BundleAuthorizationPackageTool bundleAuthorizationPackageTool,
        // US9: Continuous Monitoring tools
        CreateConMonPlanTool createConMonPlanTool,
        GenerateConMonReportTool generateConMonReportTool,
        ReportSignificantChangeTool reportSignificantChangeTool,
        TrackAtoExpirationTool trackAtoExpirationTool,
        MultiSystemDashboardTool multiSystemDashboardTool,
        ReauthorizationWorkflowTool reauthorizationWorkflowTool,
        NotificationDeliveryTool notificationDeliveryTool,
        // US10: eMASS & OSCAL tools
        ExportEmassTool exportEmassTool,
        ImportEmassTool importEmassTool,
        ExportOscalTool exportOscalTool,
        // US11: Document Templates & PDF Export tools
        UploadTemplateTool uploadTemplateTool,
        ListTemplatesTool listTemplatesTool,
        UpdateTemplateTool updateTemplateTool,
        DeleteTemplateTool deleteTemplateTool,
        // Feature 017: SCAP/STIG Import tools
        ImportCklTool importCklTool,
        ImportXccdfTool importXccdfTool,
        ExportCklTool exportCklTool,
        ListImportsTool listImportsTool,
        GetImportSummaryTool getImportSummaryTool,
        // Feature 018: SAP Generation tools
        GenerateSapTool generateSapTool,
        UpdateSapTool updateSapTool,
        FinalizeSapTool finalizeSapTool,
        GetSapTool getSapTool,
        ListSapsTool listSapsTool,
        // Feature 019: Prisma Cloud Import tools
        ImportPrismaCsvTool importPrismaCsvTool,
        ImportPrismaApiTool importPrismaApiTool,
        ListPrismaPoliciesTool listPrismaPoliciesTool,
        PrismaTrendTool prismaTrendTool)
    {
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
        _iacComplianceScanTool = iacComplianceScanTool;
        _registerSystemTool = registerSystemTool;
        _listSystemsTool = listSystemsTool;
        _getSystemTool = getSystemTool;
        _advanceRmfStepTool = advanceRmfStepTool;
        _defineBoundaryTool = defineBoundaryTool;
        _excludeFromBoundaryTool = excludeFromBoundaryTool;
        _assignRmfRoleTool = assignRmfRoleTool;
        _listRmfRolesTool = listRmfRolesTool;
        _listBoundaryDefinitionsTool = listBoundaryDefinitionsTool;
        _createBoundaryDefinitionTool = createBoundaryDefinitionTool;
        _deleteBoundaryDefinitionTool = deleteBoundaryDefinitionTool;
        _boundaryGapAnalysisTool = boundaryGapAnalysisTool;
        _categorizeSystemTool = categorizeSystemTool;
        _getCategorizationTool = getCategorizationTool;
        _suggestInfoTypesTool = suggestInfoTypesTool;
        _selectBaselineTool = selectBaselineTool;
        _tailorBaselineTool = tailorBaselineTool;
        _setInheritanceTool = setInheritanceTool;
        _getBaselineTool = getBaselineTool;
        _generateCrmTool = generateCrmTool;
        _showStigMappingTool = showStigMappingTool;
        _writeNarrativeTool = writeNarrativeTool;
        _suggestNarrativeTool = suggestNarrativeTool;
        _batchPopulateNarrativesTool = batchPopulateNarrativesTool;
        _narrativeProgressTool = narrativeProgressTool;
        _generateSspTool = generateSspTool;
        _assessControlTool = assessControlTool;
        _takeSnapshotTool = takeSnapshotTool;
        _compareSnapshotsTool = compareSnapshotsTool;
        _verifyEvidenceTool = verifyEvidenceTool;
        _checkEvidenceCompletenessTool = checkEvidenceCompletenessTool;
        _generateSarTool = generateSarTool;
        _issueAuthorizationTool = issueAuthorizationTool;
        _acceptRiskTool = acceptRiskTool;
        _showRiskRegisterTool = showRiskRegisterTool;
        _createPoamTool = createPoamTool;
        _listPoamTool = listPoamTool;
        _getPoamTool = getPoamTool;
        _updatePoamTool = updatePoamTool;
        _closePoamTool = closePoamTool;
        _updatePoamMilestoneTool = updatePoamMilestoneTool;
        _bulkUpdatePoamTool = bulkUpdatePoamTool;
        _linkPoamTaskTool = linkPoamTaskTool;
        _unlinkPoamTaskTool = unlinkPoamTaskTool;
        _createTaskFromPoamTool = createTaskFromPoamTool;
        _generateRarTool = generateRarTool;
        _bundleAuthorizationPackageTool = bundleAuthorizationPackageTool;

        // US9: Continuous Monitoring
        _createConMonPlanTool = createConMonPlanTool;
        _generateConMonReportTool = generateConMonReportTool;
        _reportSignificantChangeTool = reportSignificantChangeTool;
        _trackAtoExpirationTool = trackAtoExpirationTool;
        _multiSystemDashboardTool = multiSystemDashboardTool;
        _reauthorizationWorkflowTool = reauthorizationWorkflowTool;
        _notificationDeliveryTool = notificationDeliveryTool;

        // US10: eMASS & OSCAL
        _exportEmassTool = exportEmassTool;
        _importEmassTool = importEmassTool;
        _exportOscalTool = exportOscalTool;

        // US11: Document Templates & PDF Export
        _uploadTemplateTool = uploadTemplateTool;
        _listTemplatesTool = listTemplatesTool;
        _updateTemplateTool = updateTemplateTool;
        _deleteTemplateTool = deleteTemplateTool;

        // Feature 017: SCAP/STIG Import
        _importCklTool = importCklTool;
        _importXccdfTool = importXccdfTool;
        _exportCklTool = exportCklTool;
        _listImportsTool = listImportsTool;
        _getImportSummaryTool = getImportSummaryTool;

        // Feature 018: SAP Generation
        _generateSapTool = generateSapTool;
        _updateSapTool = updateSapTool;
        _finalizeSapTool = finalizeSapTool;
        _getSapTool = getSapTool;
        _listSapsTool = listSapsTool;

        // Feature 019: Prisma Cloud Import
        _importPrismaCsvTool = importPrismaCsvTool;
        _importPrismaApiTool = importPrismaApiTool;
        _listPrismaPoliciesTool = listPrismaPoliciesTool;
        _prismaTrendTool = prismaTrendTool;
    }

    [Description("Run a NIST 800-53 compliance assessment. Scan types: quick, policy, full.")]
    public async Task<string> RunComplianceAssessmentAsync(
        string? subscriptionId = null, string? framework = null,
        string? controlFamilies = null, string? resourceTypes = null,
        string? scanType = null, bool includePassed = false,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = subscriptionId, ["framework"] = framework,
            ["control_families"] = controlFamilies, ["resource_types"] = resourceTypes,
            ["scan_type"] = scanType, ["include_passed"] = includePassed
        };
        return await _assessmentTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Get detailed information about a NIST 800-53 control family.")]
    public async Task<string> GetControlFamilyInfoAsync(
        string familyId, bool includeControls = true, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["family_id"] = familyId, ["include_controls"] = includeControls };
        return await _controlFamilyTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Generate compliance documentation (SSP, POA&M, SAR).")]
    public async Task<string> GenerateComplianceDocumentAsync(
        string documentType, string? subscriptionId = null,
        string? framework = null, string? systemName = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["document_type"] = documentType, ["subscription_id"] = subscriptionId,
            ["framework"] = framework, ["system_name"] = systemName
        };
        return await _documentGenerationTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Collect compliance evidence from Azure resources.")]
    public async Task<string> CollectComplianceEvidenceAsync(
        string controlId, string? subscriptionId = null,
        string? resourceGroup = null, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["control_id"] = controlId, ["subscription_id"] = subscriptionId, ["resource_group"] = resourceGroup
        };
        return await _evidenceCollectionTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Remediate a compliance finding with guided or automated fixes.")]
    public async Task<string> RemediateComplianceFindingAsync(
        string findingId, bool applyRemediation = false, bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["finding_id"] = findingId, ["apply_remediation"] = applyRemediation, ["dry_run"] = dryRun
        };
        return await _remediationTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Validate that a remediation was successfully applied.")]
    public async Task<string> ValidateRemediationAsync(
        string findingId, string? executionId = null, string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["finding_id"] = findingId, ["execution_id"] = executionId, ["subscription_id"] = subscriptionId
        };
        return await _validateRemediationTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Generate a prioritized remediation plan for compliance findings.")]
    public async Task<string> GenerateRemediationPlanAsync(
        string? subscriptionId = null, string? resourceGroupName = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = subscriptionId, ["resource_group_name"] = resourceGroupName
        };
        return await _remediationPlanTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Get the audit trail of compliance assessments.")]
    public async Task<string> GetAssessmentAuditLogAsync(
        string? subscriptionId = null, int days = 7, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["subscription_id"] = subscriptionId, ["days"] = days };
        return await _auditLogTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Get compliance history and trends over time.")]
    public async Task<string> GetComplianceHistoryAsync(
        string? subscriptionId = null, int days = 30, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["subscription_id"] = subscriptionId, ["days"] = days };
        return await _historyTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Get current compliance status summary.")]
    public async Task<string> GetComplianceStatusAsync(
        string? subscriptionId = null, string? framework = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["subscription_id"] = subscriptionId, ["framework"] = framework };
        return await _statusTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Query continuous compliance monitoring status, alerts, and trends.")]
    public async Task<string> GetComplianceMonitoringAsync(
        string action, string? subscriptionId = null, int days = 30,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["action"] = action, ["subscription_id"] = subscriptionId, ["days"] = days
        };
        return await _monitoringTool.ExecuteAsync(args, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Kanban Board Tools
    // ═══════════════════════════════════════════════════════════════════════════

    [Description("Create a new Kanban remediation board, optionally from an assessment.")]
    public async Task<string> KanbanCreateBoardAsync(
        string name, string? subscriptionId = null, string? assessmentId = null,
        string? owner = null, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = name, ["subscription_id"] = subscriptionId,
            ["assessment_id"] = assessmentId, ["owner"] = owner
        };
        return await _kanbanCreateBoard.ExecuteAsync(args, cancellationToken);
    }

    [Description("Show a Kanban board overview with columns and task summaries.")]
    public async Task<string> KanbanBoardShowAsync(
        string boardId, bool includeTaskSummaries = true,
        int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["board_id"] = boardId, ["include_task_summaries"] = includeTaskSummaries,
            ["page"] = page, ["page_size"] = pageSize
        };
        return await _kanbanBoardShow.ExecuteAsync(args, cancellationToken);
    }

    [Description("Get full details of a single remediation task.")]
    public async Task<string> KanbanGetTaskAsync(
        string taskId, string? boardId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["task_id"] = taskId, ["board_id"] = boardId
        };
        return await _kanbanGetTask.ExecuteAsync(args, cancellationToken);
    }

    [Description("Create a new remediation task on a Kanban board.")]
    public async Task<string> KanbanCreateTaskAsync(
        string boardId, string title, string controlId,
        string? description = null, string? severity = null,
        string? assigneeId = null, string? assigneeName = null,
        string? dueDate = null, string? remediationScript = null,
        string? validationCriteria = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["board_id"] = boardId, ["title"] = title, ["control_id"] = controlId,
            ["description"] = description, ["severity"] = severity,
            ["assignee_id"] = assigneeId, ["assignee_name"] = assigneeName,
            ["due_date"] = dueDate, ["remediation_script"] = remediationScript,
            ["validation_criteria"] = validationCriteria
        };
        return await _kanbanCreateTask.ExecuteAsync(args, cancellationToken);
    }

    [Description("Assign or unassign a remediation task.")]
    public async Task<string> KanbanAssignTaskAsync(
        string taskId, string? boardId = null,
        string? assigneeId = null, string? assigneeName = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["task_id"] = taskId, ["board_id"] = boardId,
            ["assignee_id"] = assigneeId, ["assignee_name"] = assigneeName
        };
        return await _kanbanAssignTask.ExecuteAsync(args, cancellationToken);
    }

    [Description("Move a remediation task to a new Kanban column (status transition).")]
    public async Task<string> KanbanMoveTaskAsync(
        string taskId, string targetStatus,
        string? boardId = null, string? comment = null,
        bool skipValidation = false,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["task_id"] = taskId, ["target_status"] = targetStatus,
            ["board_id"] = boardId, ["comment"] = comment,
            ["skip_validation"] = skipValidation
        };
        return await _kanbanMoveTask.ExecuteAsync(args, cancellationToken);
    }

    [Description("List and filter remediation tasks on a Kanban board.")]
    public async Task<string> KanbanTaskListAsync(
        string boardId, string? status = null, string? severity = null,
        string? assigneeId = null, string? controlFamily = null,
        bool? isOverdue = null, string? sortBy = null, string? sortOrder = null,
        int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["board_id"] = boardId, ["status"] = status, ["severity"] = severity,
            ["assignee_id"] = assigneeId, ["control_family"] = controlFamily,
            ["is_overdue"] = isOverdue, ["sort_by"] = sortBy, ["sort_order"] = sortOrder,
            ["page"] = page, ["page_size"] = pageSize
        };
        return await _kanbanTaskList.ExecuteAsync(args, cancellationToken);
    }

    [Description("View the full audit trail and history of a remediation task.")]
    public async Task<string> KanbanTaskHistoryAsync(
        string taskId, string? boardId = null, string? eventType = null,
        int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["task_id"] = taskId, ["board_id"] = boardId,
            ["event_type"] = eventType, ["page"] = page, ["page_size"] = pageSize
        };
        return await _kanbanTaskHistory.ExecuteAsync(args, cancellationToken);
    }

    [Description("Run a targeted validation scan to verify a remediation fix.")]
    public async Task<string> KanbanValidateTaskAsync(
        string taskId, string? boardId = null, string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["task_id"] = taskId, ["board_id"] = boardId,
            ["subscription_id"] = subscriptionId
        };
        return await _kanbanValidateTask.ExecuteAsync(args, cancellationToken);
    }

    [Description("Add a comment to a remediation task (Markdown, @mentions).")]
    public async Task<string> KanbanAddCommentAsync(
        string taskId, string content,
        string? boardId = null, string? parentCommentId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["task_id"] = taskId, ["content"] = content,
            ["board_id"] = boardId, ["parent_comment_id"] = parentCommentId
        };
        return await _kanbanAddComment.ExecuteAsync(args, cancellationToken);
    }

    [Description("List all comments on a remediation task.")]
    public async Task<string> KanbanTaskCommentsAsync(
        string taskId, string? boardId = null, bool includeDeleted = false,
        int page = 1, int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["task_id"] = taskId, ["board_id"] = boardId,
            ["include_deleted"] = includeDeleted, ["page"] = page, ["page_size"] = pageSize
        };
        return await _kanbanTaskComments.ExecuteAsync(args, cancellationToken);
    }

    [Description("Edit an existing comment (within 24h window).")]
    public async Task<string> KanbanEditCommentAsync(
        string commentId, string taskId, string content,
        string? boardId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["comment_id"] = commentId, ["task_id"] = taskId,
            ["content"] = content, ["board_id"] = boardId
        };
        return await _kanbanEditComment.ExecuteAsync(args, cancellationToken);
    }

    [Description("Delete a comment on a remediation task (soft delete).")]
    public async Task<string> KanbanDeleteCommentAsync(
        string commentId, string taskId,
        string? boardId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["comment_id"] = commentId, ["task_id"] = taskId, ["board_id"] = boardId
        };
        return await _kanbanDeleteComment.ExecuteAsync(args, cancellationToken);
    }

    [Description("Execute the remediation script for a task.")]
    public async Task<string> KanbanRemediateTaskAsync(
        string taskId, string? boardId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["task_id"] = taskId, ["board_id"] = boardId
        };
        return await _kanbanRemediateTask.ExecuteAsync(args, cancellationToken);
    }

    [Description("Collect compliance evidence for a remediation task.")]
    public async Task<string> KanbanCollectEvidenceAsync(
        string taskId, string? boardId = null, string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["task_id"] = taskId, ["board_id"] = boardId,
            ["subscription_id"] = subscriptionId
        };
        return await _kanbanCollectEvidence.ExecuteAsync(args, cancellationToken);
    }

    [Description("Perform bulk operations on multiple remediation tasks.")]
    public async Task<string> KanbanBulkUpdateAsync(
        string boardId, string operation,
        string? assigneeId = null, string? assigneeName = null,
        string? targetStatus = null, string? dueDate = null,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["board_id"] = boardId, ["operation"] = operation,
            ["task_ids"] = Array.Empty<string>(),
            ["assignee_id"] = assigneeId, ["assignee_name"] = assigneeName,
            ["target_status"] = targetStatus, ["due_date"] = dueDate, ["comment"] = comment
        };
        return await _kanbanBulkUpdate.ExecuteAsync(args, cancellationToken);
    }

    [Description("Export Kanban board data as CSV or POA&M.")]
    public async Task<string> KanbanExportAsync(
        string boardId, string format = "csv",
        string? statuses = null, bool includeHistory = false,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["board_id"] = boardId, ["format"] = format,
            ["statuses"] = statuses, ["include_history"] = includeHistory
        };
        return await _kanbanExport.ExecuteAsync(args, cancellationToken);
    }

    [Description("Archive a Kanban board (read-only).")]
    public async Task<string> KanbanArchiveBoardAsync(
        string boardId, bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["board_id"] = boardId, ["confirm"] = confirm
        };
        return await _kanbanArchiveBoard.ExecuteAsync(args, cancellationToken);
    }

    [Description("Generate or regenerate a remediation script for a Kanban task using AI or template fallback.")]
    public async Task<string> KanbanGenerateScriptAsync(
        string taskId, string scriptType = "AzureCli", bool force = false,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["task_id"] = taskId, ["script_type"] = scriptType, ["force"] = force
        };
        return await _kanbanGenerateScript.ExecuteAsync(args, cancellationToken);
    }

    [Description("Generate or regenerate validation criteria for a Kanban task using AI or template fallback.")]
    public async Task<string> KanbanGenerateValidationAsync(
        string taskId, bool force = false,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["task_id"] = taskId, ["force"] = force
        };
        return await _kanbanGenerateValidation.ExecuteAsync(args, cancellationToken);
    }

    // ─── Auth/PIM MCP Wrappers ───────────────────────────────────────────

    [Description("Check current CAC authentication status, session information, and active PIM roles.")]
    public async Task<string> CacStatusAsync(
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["user_id"] = userId };
        return await _cacStatus.ExecuteAsync(args, cancellationToken);
    }

    [Description("End the current CAC session, clear cached tokens, and revert to unauthenticated state.")]
    public async Task<string> CacSignOutAsync(
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["user_id"] = userId };
        return await _cacSignOut.ExecuteAsync(args, cancellationToken);
    }

    [Description("Set the CAC session timeout duration within policy limits (1-24 hours).")]
    public async Task<string> CacSetTimeoutAsync(
        string? userId = null, int? timeoutHours = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["user_id"] = userId, ["timeoutHours"] = timeoutHours };
        return await _cacSetTimeout.ExecuteAsync(args, cancellationToken);
    }

    [Description("Map the current CAC certificate identity to a platform role for automatic role resolution.")]
    public async Task<string> CacMapCertificateAsync(
        string? userId = null, string? role = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["user_id"] = userId, ["role"] = role };
        return await _cacMapCertificate.ExecuteAsync(args, cancellationToken);
    }

    // ─── PIM Role Management MCP Wrappers ────────────────────────────────

    [Description("List all PIM-eligible role assignments for the authenticated user.")]
    public async Task<string> PimListEligibleAsync(
        string? userId = null, string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["user_id"] = userId, ["scope"] = scope };
        return await _pimListEligible.ExecuteAsync(args, cancellationToken);
    }

    [Description("Activate an eligible PIM role with justification and optional ticket number.")]
    public async Task<string> PimActivateRoleAsync(
        string? userId = null, string? roleName = null, string? scope = null,
        string? justification = null, string? ticketNumber = null,
        int? durationHours = null, string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["roleName"] = roleName, ["scope"] = scope,
            ["justification"] = justification, ["ticketNumber"] = ticketNumber,
            ["durationHours"] = durationHours, ["session_id"] = sessionId
        };
        return await _pimActivateRole.ExecuteAsync(args, cancellationToken);
    }

    [Description("Deactivate an active PIM role to restore least-privilege posture.")]
    public async Task<string> PimDeactivateRoleAsync(
        string? userId = null, string? roleName = null, string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["user_id"] = userId, ["roleName"] = roleName, ["scope"] = scope };
        return await _pimDeactivateRole.ExecuteAsync(args, cancellationToken);
    }

    // ─── PIM Session Management MCP Wrappers ─────────────────────────────

    [Description("List all currently active PIM role assignments for the authenticated user.")]
    public async Task<string> PimListActiveAsync(
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["user_id"] = userId };
        return await _pimListActive.ExecuteAsync(args, cancellationToken);
    }

    [Description("Extend an active PIM role's duration within policy limits.")]
    public async Task<string> PimExtendRoleAsync(
        string? userId = null, string? roleName = null, string? scope = null,
        int? additionalHours = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["roleName"] = roleName,
            ["scope"] = scope, ["additionalHours"] = additionalHours
        };
        return await _pimExtendRole.ExecuteAsync(args, cancellationToken);
    }

    [Description("Approve a pending PIM role activation request. Requires SecurityLead or Administrator role.")]
    public async Task<string> PimApproveRequestAsync(
        string? userId = null, string? userRole = null, string? requestId = null,
        string? comments = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["user_role"] = userRole,
            ["requestId"] = requestId, ["comments"] = comments
        };
        return await _pimApproveRequest.ExecuteAsync(args, cancellationToken);
    }

    [Description("Deny a pending PIM role activation request. Requires SecurityLead or Administrator role.")]
    public async Task<string> PimDenyRequestAsync(
        string? userId = null, string? userRole = null, string? requestId = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["user_role"] = userRole,
            ["requestId"] = requestId, ["reason"] = reason
        };
        return await _pimDenyRequest.ExecuteAsync(args, cancellationToken);
    }

    // ─── JIT VM Access Wrappers (Phase 9 — US7) ─────────────────────────

    [Description("Request Just-in-Time VM access through Azure Defender for Cloud. Creates a temporary NSG rule.")]
    public async Task<string> JitRequestAccessAsync(
        string? userId = null, string? vmName = null, string? resourceGroup = null,
        string? subscriptionId = null, int? port = null, string? protocol = null,
        string? sourceIp = null, int? durationHours = null,
        string? justification = null, string? ticketNumber = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["vmName"] = vmName, ["resourceGroup"] = resourceGroup,
            ["subscriptionId"] = subscriptionId, ["port"] = port, ["protocol"] = protocol,
            ["sourceIp"] = sourceIp, ["durationHours"] = durationHours,
            ["justification"] = justification, ["ticketNumber"] = ticketNumber,
            ["session_id"] = sessionId
        };
        return await _jitRequestAccess.ExecuteAsync(args, cancellationToken);
    }

    [Description("List all active JIT VM access sessions for the authenticated user.")]
    public async Task<string> JitListSessionsAsync(
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["user_id"] = userId };
        return await _jitListSessions.ExecuteAsync(args, cancellationToken);
    }

    [Description("Immediately revoke JIT VM access, removing the NSG rule.")]
    public async Task<string> JitRevokeAccessAsync(
        string? userId = null, string? vmName = null, string? resourceGroup = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["vmName"] = vmName, ["resourceGroup"] = resourceGroup
        };
        return await _jitRevokeAccess.ExecuteAsync(args, cancellationToken);
    }

    // ─── PIM Audit Trail MCP Wrapper ────────────────────────────────────────

    [Description("Query PIM action history for compliance evidence and audit trail.")]
    public async Task<string> PimHistoryAsync(
        string? userId = null, int? days = null, string? roleName = null,
        string? filterUserId = null, string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["days"] = days, ["roleName"] = roleName,
            ["filterUserId"] = filterUserId, ["scope"] = scope
        };
        return await _pimHistory.ExecuteAsync(args, cancellationToken);
    }

    // ─── Compliance Watch Monitoring MCP Wrappers ────────────────────────────

    [Description("Enable continuous compliance monitoring for a subscription or resource group.")]
    public async Task<string> WatchEnableMonitoringAsync(
        string subscriptionId, string? resourceGroup = null,
        string? frequency = null, string? mode = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = subscriptionId, ["resource_group"] = resourceGroup,
            ["frequency"] = frequency, ["mode"] = mode
        };
        return await _watchEnableMonitoring.ExecuteAsync(args, cancellationToken);
    }

    [Description("Disable monitoring for a subscription or resource group.")]
    public async Task<string> WatchDisableMonitoringAsync(
        string subscriptionId, string? resourceGroup = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = subscriptionId, ["resource_group"] = resourceGroup
        };
        return await _watchDisableMonitoring.ExecuteAsync(args, cancellationToken);
    }

    [Description("Update monitoring settings for an existing configuration.")]
    public async Task<string> WatchConfigureMonitoringAsync(
        string subscriptionId, string? resourceGroup = null,
        string? frequency = null, string? mode = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = subscriptionId, ["resource_group"] = resourceGroup,
            ["frequency"] = frequency, ["mode"] = mode
        };
        return await _watchConfigureMonitoring.ExecuteAsync(args, cancellationToken);
    }

    [Description("Show current monitoring configuration and status.")]
    public async Task<string> WatchMonitoringStatusAsync(
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = subscriptionId
        };
        return await _watchMonitoringStatus.ExecuteAsync(args, cancellationToken);
    }

    // ─── Alert Lifecycle Tools (US2) ────────────────────────────────────────

    [Description("List active compliance alerts with optional severity, status, control family, and date filters.")]
    public async Task<string> WatchShowAlertsAsync(
        string? subscriptionId = null,
        string? severity = null,
        string? status = null,
        string? controlFamily = null,
        int? days = null,
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = subscriptionId, ["severity"] = severity,
            ["status"] = status, ["control_family"] = controlFamily,
            ["days"] = days, ["page"] = page, ["page_size"] = pageSize
        };
        return await _watchShowAlerts.ExecuteAsync(args, cancellationToken);
    }

    [Description("Get full details of a specific compliance alert.")]
    public async Task<string> WatchGetAlertAsync(
        string alertId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["alert_id"] = alertId };
        return await _watchGetAlert.ExecuteAsync(args, cancellationToken);
    }

    [Description("Acknowledge a compliance alert, pausing the escalation timer.")]
    public async Task<string> WatchAcknowledgeAlertAsync(
        string alertId,
        string userId,
        string userRole,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["alert_id"] = alertId, ["user_id"] = userId, ["user_role"] = userRole
        };
        return await _watchAcknowledgeAlert.ExecuteAsync(args, cancellationToken);
    }

    [Description("Execute remediation for an alert and transition to Resolved.")]
    public async Task<string> WatchFixAlertAsync(
        string alertId,
        string userId,
        string userRole,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["alert_id"] = alertId, ["user_id"] = userId,
            ["user_role"] = userRole, ["dry_run"] = dryRun
        };
        return await _watchFixAlert.ExecuteAsync(args, cancellationToken);
    }

    [Description("Dismiss a compliance alert. Requires Compliance Officer role and justification.")]
    public async Task<string> WatchDismissAlertAsync(
        string alertId,
        string justification,
        string userId,
        string userRole,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["alert_id"] = alertId, ["justification"] = justification,
            ["user_id"] = userId, ["user_role"] = userRole
        };
        return await _watchDismissAlert.ExecuteAsync(args, cancellationToken);
    }

    // ─── Alert Rules & Suppression MCP Tools (US3) ───────────────────────

    [Description("Create a custom alert rule to control severity and routing for compliance alerts.")]
    public async Task<string> WatchCreateRuleAsync(
        string name,
        string userRole,
        string? description = null,
        string? subscriptionId = null,
        string? resourceGroup = null,
        string? resourceType = null,
        string? resourceId = null,
        string? controlFamily = null,
        string? controlId = null,
        string? severityOverride = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = name, ["description"] = description,
            ["subscription_id"] = subscriptionId, ["resource_group"] = resourceGroup,
            ["resource_type"] = resourceType, ["resource_id"] = resourceId,
            ["control_family"] = controlFamily, ["control_id"] = controlId,
            ["severity_override"] = severityOverride,
            ["user_role"] = userRole, ["user_id"] = userId
        };
        return await _watchCreateRule.ExecuteAsync(args, cancellationToken);
    }

    [Description("List configured alert rules for a subscription.")]
    public async Task<string> WatchListRulesAsync(
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["subscription_id"] = subscriptionId };
        return await _watchListRules.ExecuteAsync(args, cancellationToken);
    }

    [Description("Create a suppression rule to suppress alerts matching specific criteria.")]
    public async Task<string> WatchSuppressAlertsAsync(
        string type,
        string userRole,
        string? subscriptionId = null,
        string? resourceId = null,
        string? controlFamily = null,
        string? controlId = null,
        string? justification = null,
        string? expiresAt = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = subscriptionId, ["resource_id"] = resourceId,
            ["control_family"] = controlFamily, ["control_id"] = controlId,
            ["type"] = type, ["justification"] = justification,
            ["expires_at"] = expiresAt,
            ["user_role"] = userRole, ["user_id"] = userId
        };
        return await _watchSuppressAlerts.ExecuteAsync(args, cancellationToken);
    }

    [Description("List active alert suppression rules.")]
    public async Task<string> WatchListSuppressionsAsync(
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["subscription_id"] = subscriptionId };
        return await _watchListSuppressions.ExecuteAsync(args, cancellationToken);
    }

    [Description("Configure quiet hours during which non-Critical alerts are suppressed. Critical alerts always deliver.")]
    public async Task<string> WatchConfigureQuietHoursAsync(
        string subscriptionId,
        string startTime,
        string endTime,
        string userRole,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = subscriptionId,
            ["start_time"] = startTime, ["end_time"] = endTime,
            ["user_role"] = userRole, ["user_id"] = userId
        };
        return await _watchConfigureQuietHours.ExecuteAsync(args, cancellationToken);
    }

    [Description("Configure notification channels (email, webhook) for compliance alerts.")]
    public async Task<string> WatchConfigureNotificationsAsync(
        string channel,
        string target,
        string userRole,
        string? severity = null,
        string? subscriptionId = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["channel"] = channel, ["target"] = target,
            ["severity"] = severity, ["subscription_id"] = subscriptionId,
            ["role"] = userRole, ["user_id"] = userId
        };
        return await _watchConfigureNotifications.ExecuteAsync(args, cancellationToken);
    }

    [Description("Configure an escalation path for SLA violation detection and automatic escalation.")]
    public async Task<string> WatchConfigureEscalationAsync(
        string name,
        string severity,
        int delayMinutes,
        string recipients,
        string userRole,
        string? channel = null,
        int? repeatMinutes = null,
        string? webhookUrl = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = name, ["severity"] = severity,
            ["delay_minutes"] = delayMinutes.ToString(),
            ["recipients"] = recipients, ["channel"] = channel,
            ["repeat_minutes"] = repeatMinutes?.ToString(),
            ["webhook_url"] = webhookUrl,
            ["role"] = userRole, ["user_id"] = userId
        };
        return await _watchConfigureEscalation.ExecuteAsync(args, cancellationToken);
    }

    // ── Dashboard & Reporting tools (Feature 005 — US5) ─────────────────

    [Description("Query compliance alert history with natural-language support. " +
        "Supports queries like 'What drifted this week?' or structured filters by severity, status, control family.")]
    public async Task<string> WatchAlertHistoryAsync(
        string? query = null,
        string? subscriptionId = null,
        string? severity = null,
        string? status = null,
        string? controlFamily = null,
        int? days = null,
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["query"] = query, ["subscriptionId"] = subscriptionId,
            ["severity"] = severity, ["status"] = status,
            ["controlFamily"] = controlFamily,
            ["days"] = days?.ToString(), ["page"] = page?.ToString(),
            ["pageSize"] = pageSize?.ToString()
        };
        return await _watchAlertHistory.ExecuteAsync(args, cancellationToken);
    }

    [Description("View compliance score trends over time with direction indicators (improving/declining/stable).")]
    public async Task<string> WatchComplianceTrendAsync(
        string subscriptionId,
        int? days = null,
        bool? weekly = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["subscriptionId"] = subscriptionId,
            ["days"] = days?.ToString(),
            ["weekly"] = weekly?.ToString()
        };
        return await _watchComplianceTrend.ExecuteAsync(args, cancellationToken);
    }

    [Description("Get alert statistics including counts by severity, type, and status; " +
        "average resolution time; escalation count; and auto-resolved count.")]
    public async Task<string> WatchAlertStatisticsAsync(
        string? subscriptionId = null,
        int? days = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["subscriptionId"] = subscriptionId,
            ["days"] = days?.ToString()
        };
        return await _watchAlertStatistics.ExecuteAsync(args, cancellationToken);
    }

    [Description("Create a Kanban remediation task from a compliance alert. " +
        "Pre-populates title, description, severity, and control mapping.")]
    public async Task<string> WatchCreateTaskFromAlertAsync(
        string alertId,
        string? boardId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["alertId"] = alertId,
            ["boardId"] = boardId
        };
        return await _watchCreateTaskFromAlert.ExecuteAsync(args, cancellationToken);
    }

    [Description("Capture alert details, timeline, and context as compliance evidence for audit trails.")]
    public async Task<string> WatchCollectEvidenceFromAlertAsync(
        string alertId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["alertId"] = alertId
        };
        return await _watchCollectEvidenceFromAlert.ExecuteAsync(args, cancellationToken);
    }

    [Description("Create an opt-in auto-remediation rule. AC, IA, SC control families are blocked and always require human approval.")]
    public async Task<string> WatchCreateAutoRemediationRuleAsync(
        string name,
        string action,
        string? subscriptionId = null,
        string? resourceGroup = null,
        string? controlFamily = null,
        string? controlId = null,
        string? approvalMode = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["action"] = action,
            ["subscriptionId"] = subscriptionId,
            ["resourceGroup"] = resourceGroup,
            ["controlFamily"] = controlFamily,
            ["controlId"] = controlId,
            ["approvalMode"] = approvalMode
        };
        return await _watchCreateAutoRemediationRule.ExecuteAsync(args, cancellationToken);
    }

    [Description("List auto-remediation rules and their execution history.")]
    public async Task<string> WatchListAutoRemediationRulesAsync(
        string? subscriptionId = null,
        bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["subscriptionId"] = subscriptionId,
            ["includeDisabled"] = includeDisabled.ToString()
        };
        return await _watchListAutoRemediationRules.ExecuteAsync(args, cancellationToken);
    }

    [Description("Search NIST SP 800-53 Rev 5 controls by keyword, phrase, or control family.")]
    public async Task<string> SearchNistControlsAsync(
        string query,
        string? family = null,
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["query"] = query,
            ["family"] = family,
            ["max_results"] = maxResults
        };
        return await _nistControlSearchTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Get a detailed explanation of a specific NIST SP 800-53 control including statement, guidance, and assessment objectives.")]
    public async Task<string> ExplainNistControlAsync(
        string controlId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["control_id"] = controlId
        };
        return await _nistControlExplainerTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Scan Infrastructure-as-Code files (Bicep, Terraform, ARM) for NIST 800-53 / FedRAMP compliance findings.")]
    public async Task<string> ScanIacComplianceAsync(
        string filePath,
        string fileContent,
        string fileType,
        string? framework = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["filePath"] = filePath,
            ["fileContent"] = fileContent,
            ["fileType"] = fileType,
            ["framework"] = framework
        };
        return await _iacComplianceScanTool.ExecuteAsync(args, cancellationToken);
    }

    // ─── RMF Registration Tools (Feature 015) ────────────────────────────

    [Description("Register a new information system for RMF processing. Returns the system with ID and initial step 'Prepare'.")]
    public async Task<string> RegisterSystemAsync(
        string name,
        string systemType,
        string missionCriticality,
        string hostingEnvironment,
        string? acronym = null,
        string? description = null,
        string? cloudEnvironment = null,
        List<string>? subscriptionIds = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = name, ["system_type"] = systemType,
            ["mission_criticality"] = missionCriticality,
            ["hosting_environment"] = hostingEnvironment,
            ["acronym"] = acronym, ["description"] = description,
            ["cloud_environment"] = cloudEnvironment,
            ["subscription_ids"] = subscriptionIds
        };
        return await _registerSystemTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("List all registered information systems with pagination.")]
    public async Task<string> ListSystemsAsync(
        bool? activeOnly = null,
        int? page = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["active_only"] = activeOnly, ["page"] = page, ["page_size"] = pageSize
        };
        return await _listSystemsTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Get full details of a registered system including categorization, baseline, and role assignments.")]
    public async Task<string> GetSystemAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["system_id"] = systemId };
        return await _getSystemTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Advance a system to the next RMF step with gate condition validation.")]
    public async Task<string> AdvanceRmfStepAsync(
        string systemId,
        string targetStep,
        bool? force = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId, ["target_step"] = targetStep, ["force"] = force
        };
        return await _advanceRmfStepTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Define or update the authorization boundary for a system by adding resources.")]
    public async Task<string> DefineBoundaryAsync(
        string systemId,
        List<Dictionary<string, string?>> resources,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId, ["resources"] = resources
        };
        return await _defineBoundaryTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Exclude a resource from the authorization boundary with documented rationale.")]
    public async Task<string> ExcludeFromBoundaryAsync(
        string systemId,
        string resourceId,
        string rationale,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId, ["resource_id"] = resourceId, ["rationale"] = rationale
        };
        return await _excludeFromBoundaryTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Assign an RMF role (AO, ISSM, ISSO, SCA, SystemOwner) to a user for a registered system.")]
    public async Task<string> AssignRmfRoleAsync(
        string systemId,
        string role,
        string userId,
        string? userDisplayName = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId, ["role"] = role,
            ["user_id"] = userId, ["user_display_name"] = userDisplayName
        };
        return await _assignRmfRoleTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("List all active RMF role assignments for a registered system.")]
    public async Task<string> ListRmfRolesAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["system_id"] = systemId };
        return await _listRmfRolesTool.ExecuteAsync(args, cancellationToken);
    }

    // ─── Boundary Definitions (Feature 033) ─────────────────────────────────

    [Description("List all authorization boundary definitions for a system.")]
    public async Task<string> ListBoundaryDefinitionsAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["system_id"] = systemId };
        return await _listBoundaryDefinitionsTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Create a new authorization boundary definition for a system (e.g., Dev/Test, DMZ, Production).")]
    public async Task<string> CreateBoundaryDefinitionAsync(
        string systemId,
        string name,
        string boundaryType,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId, ["name"] = name,
            ["boundary_type"] = boundaryType, ["description"] = description
        };
        return await _createBoundaryDefinitionTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Delete a boundary definition. Orphaned resources and mappings are reassigned to the Primary boundary.")]
    public async Task<string> DeleteBoundaryDefinitionAsync(
        string boundaryId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["boundary_id"] = boundaryId };
        return await _deleteBoundaryDefinitionTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Run gap analysis scoped to a specific authorization boundary, or compare all boundaries side-by-side.")]
    public async Task<string> BoundaryGapAnalysisAsync(
        string systemId,
        string? boundaryId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["system_id"] = systemId, ["boundary_id"] = boundaryId };
        return await _boundaryGapAnalysisTool.ExecuteAsync(args, cancellationToken);
    }

    // ─── RMF Categorization (Feature 015 - US2) ─────────────────────────────

    [Description("Perform FIPS 199 / SP 800-60 security categorization for a system. Provide info types with C/I/A impacts.")]
    public async Task<string> CategorizeSystemAsync(
        string systemId,
        object informationTypes,
        bool? isNationalSecuritySystem = null,
        string? justification = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["information_types"] = informationTypes,
            ["is_national_security_system"] = isNationalSecuritySystem,
            ["justification"] = justification
        };
        return await _categorizeSystemTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Retrieve the FIPS 199 security categorization for a system including info types and computed fields.")]
    public async Task<string> GetCategorizationAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["system_id"] = systemId };
        return await _getCategorizationTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Suggest SP 800-60 information types based on system description. Returns ranked list with confidence scores.")]
    public async Task<string> SuggestInfoTypesAsync(
        string systemId,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["description"] = description
        };
        return await _suggestInfoTypesTool.ExecuteAsync(args, cancellationToken);
    }

    // ─── RMF Baseline Selection (Feature 015 - US3) ─────────────────────────

    [Description("Select the NIST 800-53 control baseline for a system based on FIPS 199 categorization. Optionally applies CNSSI 1253 overlay.")]
    public async Task<string> SelectBaselineAsync(
        string systemId,
        bool? applyOverlay = null,
        string? overlayName = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = applyOverlay,
            ["overlay_name"] = overlayName
        };
        return await _selectBaselineTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Tailor the NIST 800-53 baseline by adding or removing controls with documented rationale.")]
    public async Task<string> TailorBaselineAsync(
        string systemId,
        object tailoringActions,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["tailoring_actions"] = tailoringActions
        };
        return await _tailorBaselineTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Set control inheritance type (Inherited/Shared/Customer) for controls in the baseline.")]
    public async Task<string> SetInheritanceAsync(
        string systemId,
        object inheritanceMappings,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["inheritance_mappings"] = inheritanceMappings
        };
        return await _setInheritanceTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Retrieve the NIST 800-53 control baseline for a system with optional details and family filter.")]
    public async Task<string> GetBaselineAsync(
        string systemId,
        bool? includeDetails = null,
        string? familyFilter = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["include_details"] = includeDetails,
            ["family_filter"] = familyFilter
        };
        return await _getBaselineTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Generate a Customer Responsibility Matrix (CRM) showing inherited/shared/customer control breakdowns.")]
    public async Task<string> GenerateCrmAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["system_id"] = systemId };
        return await _generateCrmTool.ExecuteAsync(args, cancellationToken);
    }

    // ─── RMF STIG Mapping (Feature 015 - US4) ───────────────────────────────

    [Description("Show DISA STIG rules mapped to a NIST 800-53 control via the CCI chain. Returns STIG Rule IDs, benchmark names, and CAT severity levels.")]
    public async Task<string> ShowStigMappingAsync(
        string controlId,
        string? severity = null,
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["control_id"] = controlId,
            ["severity"] = severity,
            ["max_results"] = maxResults
        };
        return await _showStigMappingTool.ExecuteAsync(args, cancellationToken);
    }

    // ─── SSP Authoring (Feature 015 - US5) ───────────────────────────────

    [Description("Write or update an implementation narrative for a NIST 800-53 control in the system's SSP.")]
    public async Task<string> WriteNarrativeAsync(
        string systemId,
        string controlId,
        string narrative,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["control_id"] = controlId,
            ["narrative"] = narrative,
            ["status"] = status
        };
        return await _writeNarrativeTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Generate an AI-suggested implementation narrative for a NIST 800-53 control based on system context and inheritance data.")]
    public async Task<string> SuggestNarrativeAsync(
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["control_id"] = controlId
        };
        return await _suggestNarrativeTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Auto-populate implementation narratives for inherited and/or shared controls using provider templates.")]
    public async Task<string> BatchPopulateNarrativesAsync(
        string systemId,
        string? inheritanceType = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["inheritance_type"] = inheritanceType
        };
        return await _batchPopulateNarrativesTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Get SSP narrative completion status with per-family progress and overall percentage.")]
    public async Task<string> NarrativeProgressAsync(
        string systemId,
        string? familyFilter = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["family_filter"] = familyFilter
        };
        return await _narrativeProgressTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Generate the System Security Plan (SSP) document with system info, categorization, baseline, and per-control narratives.")]
    public async Task<string> GenerateSspAsync(
        string systemId,
        string? format = null,
        string? sections = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["format"] = format,
            ["sections"] = sections
        };
        return await _generateSspTool.ExecuteAsync(args, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Assessment Artifact Tools (Feature 015 — US7)
    // ═══════════════════════════════════════════════════════════════════════════

    [Description("Record an SCA's per-control effectiveness determination (Satisfied/OtherThanSatisfied) with CAT severity mapping.")]
    public async Task<string> AssessControlAsync(
        string assessmentId,
        string controlId,
        string determination,
        string? method = null,
        string? evidenceIds = null,
        string? notes = null,
        string? catSeverity = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["assessment_id"] = assessmentId,
            ["control_id"] = controlId,
            ["determination"] = determination,
            ["method"] = method,
            ["evidence_ids"] = evidenceIds,
            ["notes"] = notes,
            ["cat_severity"] = catSeverity
        };
        return await _assessControlTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Create an immutable SHA-256-hashed snapshot of assessment state for audit trail.")]
    public async Task<string> TakeSnapshotAsync(
        string systemId,
        string assessmentId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["assessment_id"] = assessmentId
        };
        return await _takeSnapshotTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Compare two assessment snapshots side-by-side showing controls changed, score delta, and findings.")]
    public async Task<string> CompareSnapshotsAsync(
        string snapshotIdA,
        string snapshotIdB,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["snapshot_id_a"] = snapshotIdA,
            ["snapshot_id_b"] = snapshotIdB
        };
        return await _compareSnapshotsTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Recompute SHA-256 hash of evidence content and verify integrity (verified or tampered).")]
    public async Task<string> VerifyEvidenceAsync(
        string evidenceId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["evidence_id"] = evidenceId
        };
        return await _verifyEvidenceTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Report which controls have verified evidence vs. missing evidence with completeness percentage.")]
    public async Task<string> CheckEvidenceCompletenessAsync(
        string systemId,
        string? assessmentId = null,
        string? familyFilter = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["assessment_id"] = assessmentId,
            ["family_filter"] = familyFilter
        };
        return await _checkEvidenceCompletenessTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Generate a Security Assessment Report (SAR) with executive summary, control results, risk summary, and CAT breakdown.")]
    public async Task<string> GenerateSarAsync(
        string systemId,
        string assessmentId,
        string? format = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["assessment_id"] = assessmentId,
            ["format"] = format
        };
        return await _generateSarTool.ExecuteAsync(args, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authorization Decision Tools (Feature 015 — US8)
    // ═══════════════════════════════════════════════════════════════════════════

    [Description("Issue an authorization decision (ATO/ATOwC/IATT/DATO) for a system. Supersedes prior active decisions. RBAC: AuthorizingOfficial ONLY.")]
    public async Task<string> IssueAuthorizationAsync(
        string systemId,
        string decisionType,
        string residualRiskLevel,
        string? expirationDate = null,
        string? termsAndConditions = null,
        string? residualRiskJustification = null,
        string? riskAcceptances = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["decision_type"] = decisionType,
            ["residual_risk_level"] = residualRiskLevel,
            ["expiration_date"] = expirationDate,
            ["terms_and_conditions"] = termsAndConditions,
            ["residual_risk_justification"] = residualRiskJustification,
            ["risk_acceptances"] = riskAcceptances
        };
        return await _issueAuthorizationTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Accept risk on a specific finding/control. Requires an active authorization decision. RBAC: AuthorizingOfficial ONLY.")]
    public async Task<string> AcceptRiskAsync(
        string systemId,
        string findingId,
        string controlId,
        string catSeverity,
        string justification,
        string expirationDate,
        string? compensatingControl = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["finding_id"] = findingId,
            ["control_id"] = controlId,
            ["cat_severity"] = catSeverity,
            ["justification"] = justification,
            ["expiration_date"] = expirationDate,
            ["compensating_control"] = compensatingControl
        };
        return await _acceptRiskTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("View the risk register showing all risk acceptances for a system. Filter by active/expired/revoked/all.")]
    public async Task<string> ShowRiskRegisterAsync(
        string systemId,
        string? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["status_filter"] = statusFilter
        };
        return await _showRiskRegisterTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Create a formal POA&M item with optional milestones. Links weakness to NIST control and CAT severity.")]
    public async Task<string> CreatePoamAsync(
        string systemId,
        string weakness,
        string controlId,
        string catSeverity,
        string poc,
        string scheduledCompletion,
        string? findingId = null,
        string? resourcesRequired = null,
        string? milestones = null,
        string? componentIds = null,
        string? remediationTaskId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["weakness"] = weakness,
            ["control_id"] = controlId,
            ["cat_severity"] = catSeverity,
            ["poc"] = poc,
            ["scheduled_completion"] = scheduledCompletion,
            ["finding_id"] = findingId,
            ["resources_required"] = resourcesRequired,
            ["milestones"] = milestones,
            ["component_ids"] = componentIds,
            ["remediation_task_id"] = remediationTaskId
        };
        return await _createPoamTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("List POA&M items for a system with status, severity, overdue-only, component, and source filters.")]
    public async Task<string> ListPoamAsync(
        string systemId,
        string? statusFilter = null,
        string? severityFilter = null,
        string? overdueOnly = null,
        string? componentId = null,
        string? source = null,
        string? includeMetrics = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["status_filter"] = statusFilter,
            ["severity_filter"] = severityFilter,
            ["overdue_only"] = overdueOnly,
            ["component_id"] = componentId,
            ["source"] = source,
            ["include_metrics"] = includeMetrics
        };
        return await _listPoamTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Retrieve a single POA&M item by ID with full detail including milestones, component links, and history.")]
    public async Task<string> GetPoamAsync(
        string poamId,
        string? includeHistory = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["poam_id"] = poamId,
            ["include_history"] = includeHistory
        };
        return await _getPoamTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Update a POA&M item's fields (weakness, control ID, POC, due date, cost estimate) with optimistic concurrency enforcement.")]
    public async Task<string> UpdatePoamAsync(
        string poamId,
        string rowVersion,
        string? weakness = null,
        string? controlId = null,
        string? poc = null,
        string? scheduledCompletion = null,
        string? comments = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["poam_id"] = poamId,
            ["row_version"] = rowVersion,
            ["weakness"] = weakness,
            ["control_id"] = controlId,
            ["poc"] = poc,
            ["scheduled_completion"] = scheduledCompletion,
            ["comments"] = comments
        };
        return await _updatePoamTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Close a POA&M item by transitioning to Completed status with finding validation and optional cascade to linked remediation task.")]
    public async Task<string> ClosePoamAsync(
        string poamId,
        string rowVersion,
        string? cascadeToTask = null,
        string? comments = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["poam_id"] = poamId,
            ["row_version"] = rowVersion,
            ["cascade_to_task"] = cascadeToTask,
            ["comments"] = comments
        };
        return await _closePoamTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Update or complete a POA&M milestone with concurrency enforcement.")]
    public async Task<string> UpdatePoamMilestoneAsync(
        string poamId,
        string milestoneId,
        string rowVersion,
        string? markComplete = null,
        string? description = null,
        string? targetDate = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["poam_id"] = poamId,
            ["milestone_id"] = milestoneId,
            ["row_version"] = rowVersion,
            ["mark_complete"] = markComplete,
            ["description"] = description,
            ["target_date"] = targetDate
        };
        return await _updatePoamMilestoneTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Bulk update POA&M item statuses with lifecycle enforcement and per-item results.")]
    public async Task<string> BulkUpdatePoamAsync(
        string poamIds,
        string status,
        string? delayReason = null,
        string? revisedDate = null,
        string? comments = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["poam_ids"] = poamIds,
            ["status"] = status,
            ["delay_reason"] = delayReason,
            ["revised_date"] = revisedDate,
            ["comments"] = comments
        };
        return await _bulkUpdatePoamTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Link an existing remediation task to a POA&M item with bidirectional FK setting.")]
    public async Task<string> LinkPoamTaskAsync(
        string poamId,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["poam_id"] = poamId,
            ["task_id"] = taskId
        };
        return await _linkPoamTaskTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Unlink a POA&M item from its linked remediation task, clearing both FKs.")]
    public async Task<string> UnlinkPoamTaskAsync(
        string poamId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["poam_id"] = poamId
        };
        return await _unlinkPoamTaskTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Create a remediation task from a POA&M item with field mapping and bidirectional linking.")]
    public async Task<string> CreateTaskFromPoamAsync(
        string poamId,
        string boardId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["poam_id"] = poamId,
            ["board_id"] = boardId
        };
        return await _createTaskFromPoamTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Generate a Risk Assessment Report (RAR) with per-family risk analysis and CAT severity breakdown.")]
    public async Task<string> GenerateRarAsync(
        string systemId,
        string assessmentId,
        string? format = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["assessment_id"] = assessmentId,
            ["format"] = format
        };
        return await _generateRarTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Bundle a complete authorization package (SSP + SAR + RAR + POA&M + CRM + ATO Letter) with document status.")]
    public async Task<string> BundleAuthorizationPackageAsync(
        string systemId,
        string? format = null,
        string? includeEvidence = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["format"] = format,
            ["include_evidence"] = includeEvidence
        };
        return await _bundleAuthorizationPackageTool.ExecuteAsync(args, cancellationToken);
    }

    // ═══ US9: Continuous Monitoring wrapper methods ════════════════════════════════════

    [Description("Create or update a continuous monitoring plan for a system. One plan per system.")]
    public async Task<string> CreateConMonPlanAsync(
        string systemId,
        string assessmentFrequency,
        string annualReviewDate,
        string? reportDistribution = null,
        string? significantChangeTriggers = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["assessment_frequency"] = assessmentFrequency,
            ["annual_review_date"] = annualReviewDate,
            ["report_distribution"] = reportDistribution,
            ["significant_change_triggers"] = significantChangeTriggers
        };
        return await _createConMonPlanTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Generate a continuous monitoring report with compliance score, delta, findings, and POA&M status.")]
    public async Task<string> GenerateConMonReportAsync(
        string systemId,
        string reportType,
        string period,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["report_type"] = reportType,
            ["period"] = period
        };
        return await _generateConMonReportTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Report a significant change that may trigger reauthorization review.")]
    public async Task<string> ReportSignificantChangeAsync(
        string systemId,
        string changeType,
        string description,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["change_type"] = changeType,
            ["description"] = description
        };
        return await _reportSignificantChangeTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Check ATO expiration status with graduated alerts at 90/60/30 days.")]
    public async Task<string> TrackAtoExpirationAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        };
        return await _trackAtoExpirationTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("View multi-system dashboard with authorization status, compliance scores, and alerts.")]
    public async Task<string> MultiSystemDashboardAsync(
        string? activeOnly = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["active_only"] = activeOnly
        };
        return await _multiSystemDashboardTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Detect reauthorization triggers and optionally initiate reauthorization workflow.")]
    public async Task<string> ReauthorizationWorkflowAsync(
        string systemId,
        string? initiate = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["initiate"] = initiate
        };
        return await _reauthorizationWorkflowTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Send continuous monitoring notifications (expiration alerts, significant change events).")]
    public async Task<string> SendNotificationAsync(
        string systemId,
        string notificationType,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["notification_type"] = notificationType
        };
        return await _notificationDeliveryTool.ExecuteAsync(args, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  US10: eMASS & OSCAL Interoperability
    // ═══════════════════════════════════════════════════════════════════════

    [Description("Export system data in eMASS-compatible Excel format (controls, POA&M, or full).")]
    public async Task<string> ExportEmassAsync(
        string systemId,
        string exportType,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["export_type"] = exportType
        };
        return await _exportEmassTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Import eMASS Excel export with conflict resolution (skip, overwrite, merge). Supports dry-run.")]
    public async Task<string> ImportEmassAsync(
        string systemId,
        string fileBase64,
        string? conflictStrategy = null,
        string? dryRun = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["file_base64"] = fileBase64,
            ["conflict_strategy"] = conflictStrategy,
            ["dry_run"] = dryRun
        };
        return await _importEmassTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Export system data in OSCAL JSON format (v1.0.6). Models: ssp, assessment-results, poam.")]
    public async Task<string> ExportOscalAsync(
        string systemId,
        string model,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["model"] = model
        };
        return await _exportOscalTool.ExecuteAsync(args, cancellationToken);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  US11: Document Templates & PDF Export
    // ═════════════════════════════════════════════════════════════════════════

    [Description("Upload a custom DOCX template for compliance document generation. Validates merge fields.")]
    public async Task<string> UploadTemplateAsync(
        string templateName,
        string documentType,
        string fileBase64,
        string uploadedBy,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["template_name"] = templateName,
            ["document_type"] = documentType,
            ["file_base64"] = fileBase64,
            ["uploaded_by"] = uploadedBy
        };
        return await _uploadTemplateTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("List available document templates. Optionally filter by document type.")]
    public async Task<string> ListTemplatesAsync(
        string? documentType = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["document_type"] = documentType
        };
        return await _listTemplatesTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Update an existing document template (replace file and/or rename).")]
    public async Task<string> UpdateTemplateAsync(
        string templateId,
        string updatedBy,
        string? fileBase64 = null,
        string? newName = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["template_id"] = templateId,
            ["file_base64"] = fileBase64,
            ["new_name"] = newName,
            ["updated_by"] = updatedBy
        };
        return await _updateTemplateTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Delete a document template by ID.")]
    public async Task<string> DeleteTemplateAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["template_id"] = templateId
        };
        return await _deleteTemplateTool.ExecuteAsync(args, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Feature 017: SCAP/STIG Viewer Import
    // ═══════════════════════════════════════════════════════════════════════

    [Description("Import a DISA STIG Viewer CKL checklist file. Creates compliance findings, control effectiveness records, and evidence. Accepts base64-encoded file content (max 5 MB).")]
    public async Task<string> ImportCklAsync(
        string systemId,
        string fileContent,
        string fileName,
        string? conflictResolution = null,
        bool dryRun = false,
        string? assessmentId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["file_content"] = fileContent,
            ["file_name"] = fileName,
            ["conflict_resolution"] = conflictResolution,
            ["dry_run"] = dryRun,
            ["assessment_id"] = assessmentId
        };
        return await _importCklTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Import a SCAP Compliance Checker XCCDF results file. Creates compliance findings and control effectiveness records from automated scan results.")]
    public async Task<string> ImportXccdfAsync(
        string systemId,
        string fileContent,
        string fileName,
        string? conflictResolution = null,
        bool dryRun = false,
        string? assessmentId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["file_content"] = fileContent,
            ["file_name"] = fileName,
            ["conflict_resolution"] = conflictResolution,
            ["dry_run"] = dryRun,
            ["assessment_id"] = assessmentId
        };
        return await _importXccdfTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Export a CKL checklist file for a system and STIG benchmark. Returns base64-encoded XML content suitable for DISA STIG Viewer or eMASS upload.")]
    public async Task<string> ExportCklAsync(
        string systemId,
        string benchmarkId,
        string? assessmentId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["benchmark_id"] = benchmarkId,
            ["assessment_id"] = assessmentId
        };
        return await _exportCklTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("List import history for a registered system. Shows CKL and XCCDF imports with summary statistics.")]
    public async Task<string> ListImportsAsync(
        string systemId,
        int page = 1,
        int pageSize = 20,
        string? benchmarkId = null,
        bool includeDryRuns = false,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["page"] = page,
            ["page_size"] = pageSize,
            ["benchmark_id"] = benchmarkId,
            ["include_dry_runs"] = includeDryRuns
        };
        return await _listImportsTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Get detailed summary of a specific import operation, including per-finding breakdown and unmatched rules.")]
    public async Task<string> GetImportSummaryAsync(
        string importId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["import_id"] = importId
        };
        return await _getImportSummaryTool.ExecuteAsync(args, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Feature 018: SAP Generation Tools
    // ═══════════════════════════════════════════════════════════════════════════

    [Description("Generate a Security Assessment Plan (SAP) for a registered system. " +
        "Auto-populates from control baseline, OSCAL assessment objectives, STIG mappings, " +
        "and evidence data. Returns Markdown SAP document with 15 sections.")]
    public async Task<string> GenerateSapAsync(
        string system_id,
        string? assessment_id = null,
        string? schedule_start = null,
        string? schedule_end = null,
        string? team_members = null,
        string? scope_notes = null,
        string? method_overrides = null,
        string? rules_of_engagement = null,
        string? format = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = system_id,
            ["assessment_id"] = assessment_id,
            ["schedule_start"] = schedule_start,
            ["schedule_end"] = schedule_end,
            ["team_members"] = team_members,
            ["scope_notes"] = scope_notes,
            ["method_overrides"] = method_overrides,
            ["rules_of_engagement"] = rules_of_engagement,
            ["format"] = format
        };
        return await _generateSapTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Update a Draft SAP's schedule, scope, team, assessment methods, or rules of engagement. Team replacement is atomic. Method overrides are additive. Finalized SAPs cannot be modified.")]
    public async Task<string> UpdateSapAsync(
        string sap_id,
        string? schedule_start = null,
        string? schedule_end = null,
        string? scope_notes = null,
        string? rules_of_engagement = null,
        string? team_members = null,
        string? method_overrides = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["sap_id"] = sap_id,
            ["schedule_start"] = schedule_start,
            ["schedule_end"] = schedule_end,
            ["scope_notes"] = scope_notes,
            ["rules_of_engagement"] = rules_of_engagement,
            ["team_members"] = team_members,
            ["method_overrides"] = method_overrides
        };
        return await _updateSapTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Finalize a Draft SAP — locks it with SHA-256 content hash. Finalized SAPs are immutable: no updates, no re-finalization.")]
    public async Task<string> FinalizeSapAsync(
        string sap_id,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["sap_id"] = sap_id
        };
        return await _finalizeSapTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Retrieve a specific SAP by ID or the latest SAP for a system. If both sap_id and system_id are provided, sap_id takes precedence. By system_id, prefers Finalized over Draft.")]
    public async Task<string> GetSapAsync(
        string? sap_id = null,
        string? system_id = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["sap_id"] = sap_id,
            ["system_id"] = system_id
        };
        return await _getSapTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("List all SAPs for a system, including Draft and Finalized history. Returns status, dates, and scope summary per SAP. Content is omitted.")]
    public async Task<string> ListSapsAsync(
        string system_id,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = system_id
        };
        return await _listSapsTool.ExecuteAsync(args, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Feature 019: Prisma Cloud Import Tools
    // ═══════════════════════════════════════════════════════════════════════════

    [Description("Import Prisma Cloud CSPM compliance CSV export. Parses alerts, maps NIST 800-53 controls, " +
        "creates findings and effectiveness records. Supports auto-resolution of Azure subscriptions " +
        "to registered systems. Returns per-system import results with finding counts and warnings.")]
    public async Task<string> ImportPrismaCsvAsync(
        string file_content,
        string file_name,
        string? system_id = null,
        string? conflict_resolution = null,
        bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["file_content"] = file_content,
            ["file_name"] = file_name,
            ["system_id"] = system_id,
            ["conflict_resolution"] = conflict_resolution,
            ["dry_run"] = dry_run
        };
        return await _importPrismaCsvTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Import Prisma Cloud API JSON (RQL alert) export. Extracts NIST 800-53 controls, " +
        "remediation scripts, alert history, and policy labels. Creates findings and effectiveness " +
        "records. Supports auto-resolution of Azure subscriptions. Returns enhanced import results " +
        "with remediable counts, CLI scripts, and alert state changes.")]
    public async Task<string> ImportPrismaApiAsync(
        string file_content,
        string file_name,
        string? system_id = null,
        string? conflict_resolution = null,
        bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["file_content"] = file_content,
            ["file_name"] = file_name,
            ["system_id"] = system_id,
            ["conflict_resolution"] = conflict_resolution,
            ["dry_run"] = dry_run
        };
        return await _importPrismaApiTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("List unique Prisma Cloud policies observed across scan imports for a system. " +
        "Returns NIST 800-53 control mappings, open/resolved/dismissed counts, " +
        "affected resource types, and last-seen import details.")]
    public async Task<string> ListPrismaPoliciesAsync(
        string system_id,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = system_id
        };
        return await _listPrismaPoliciesTool.ExecuteAsync(args, cancellationToken);
    }

    [Description("Compare Prisma Cloud findings across scan imports to track remediation progress. " +
        "Shows new, resolved, and persistent findings with remediation rate. " +
        "Supports optional group_by for resource_type or nist_control breakdowns.")]
    public async Task<string> PrismaTrendAsync(
        string system_id,
        string? import_ids = null,
        string? group_by = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = system_id,
            ["import_ids"] = import_ids,
            ["group_by"] = group_by
        };
        return await _prismaTrendTool.ExecuteAsync(args, cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http.Resilience;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Compliance.Agents;
using Ato.Copilot.Agents.Compliance.Configuration;
using Ato.Copilot.Agents.Compliance.EvidenceCollectors;
using Ato.Copilot.Agents.Compliance.Scanners;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Services.KnowledgeBase;
using Ato.Copilot.Agents.Compliance.Services.ScanImport;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Agents.Configuration.Agents;
using Ato.Copilot.Agents.Configuration.Tools;
using Ato.Copilot.Agents.KnowledgeBase.Agents;
using Ato.Copilot.Agents.KnowledgeBase.Configuration;
using Ato.Copilot.Agents.KnowledgeBase.Services;
using Ato.Copilot.Agents.KnowledgeBase.Tools;
using Ato.Copilot.Agents.Compliance.Services.Engines.Remediation;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Models;
using Polly;

namespace Ato.Copilot.Agents.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register the compliance agent and all its tools.
    /// </summary>
    public static IServiceCollection AddComplianceAgent(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind compliance agent options
        services.Configure<ComplianceAgentOptions>(configuration.GetSection("Agents:Compliance"));

        // Bind boundary options (feature flag for Azure resource validation)
        services.Configure<BoundaryOptions>(configuration.GetSection("Agents:Compliance:Boundary"));

        // Bind NIST Controls options with validation
        services.AddOptions<NistControlsOptions>()
            .Bind(configuration.GetSection("Agents:Compliance:NistControls"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Add in-memory cache for NIST catalog caching
        services.AddMemoryCache();

        // Register compliance services (Phase 4)
        // NOTE: NistControlsService is registered later via AddHttpClient<NistControlsService>()
        // (typed HttpClient with Polly resilience). The interface is forwarded as a singleton.
        services.AddSingleton<IAzurePolicyComplianceService, AzurePolicyComplianceService>();
        services.AddSingleton<IDefenderForCloudService, DefenderForCloudService>();
        services.AddSingleton<IAtoComplianceEngine, AtoComplianceEngine>();
        // ─── Remediation Engine v2 (Feature 009 — AtoRemediationEngine) ─────
        services.AddSingleton<IAiRemediationPlanGenerator, AiRemediationPlanGenerator>();
        services.AddSingleton<IRemediationScriptExecutor, RemediationScriptExecutor>();
        services.AddSingleton<INistRemediationStepsService, NistRemediationStepsService>();
        services.AddSingleton<IAzureArmRemediationService, AzureArmRemediationService>();
        services.AddSingleton<IComplianceRemediationService, ComplianceRemediationService>();
        services.AddSingleton<IScriptSanitizationService, ScriptSanitizationService>();
        services.AddSingleton<AtoRemediationEngine>();
        services.AddSingleton<IRemediationEngine>(sp => sp.GetRequiredService<AtoRemediationEngine>());
        services.AddSingleton<IEvidenceStorageService, EvidenceStorageService>();
        services.AddSingleton<IDocumentGenerationService, DocumentGenerationService>();
        services.AddSingleton<ComplianceMonitoringService>();
        services.AddSingleton<IComplianceMonitoringService>(sp => sp.GetRequiredService<ComplianceMonitoringService>());
        services.AddSingleton<IComplianceHistoryService>(sp => sp.GetRequiredService<ComplianceMonitoringService>());
        services.AddSingleton<IAssessmentAuditService>(sp => sp.GetRequiredService<ComplianceMonitoringService>());
        services.AddSingleton<IComplianceStatusService>(sp => sp.GetRequiredService<ComplianceMonitoringService>());

        // ─── Compliance Engine Infrastructure (Feature 008) ──────────────────
        services.AddSingleton<IAzureResourceService, AzureResourceService>();
        services.AddSingleton<IAssessmentPersistenceService, AssessmentPersistenceService>();
        services.AddSingleton<IScannerRegistry, ScannerRegistry>();
        services.AddSingleton<IEvidenceCollectorRegistry, EvidenceCollectorRegistry>();

        // ─── Family-Specific Scanners (Feature 008 — US2) ───────────────────
        services.AddSingleton<IComplianceScanner, AccessControlScanner>();
        services.AddSingleton<IComplianceScanner, AuditScanner>();
        services.AddSingleton<IComplianceScanner, SecurityCommunicationsScanner>();
        services.AddSingleton<IComplianceScanner, SystemIntegrityScanner>();
        services.AddSingleton<IComplianceScanner, ContingencyPlanningScanner>();
        services.AddSingleton<IComplianceScanner, IdentificationAuthScanner>();
        services.AddSingleton<IComplianceScanner, ConfigManagementScanner>();
        services.AddSingleton<IComplianceScanner, IncidentResponseScanner>();
        services.AddSingleton<IComplianceScanner, RiskAssessmentScanner>();
        services.AddSingleton<IComplianceScanner, CertAccreditationScanner>();
        services.AddSingleton<IComplianceScanner, DefaultComplianceScanner>();

        // ─── Family-Specific Evidence Collectors (Feature 008 — US3) ────────
        services.AddSingleton<IEvidenceCollector, AccessControlEvidenceCollector>();
        services.AddSingleton<IEvidenceCollector, AuditEvidenceCollector>();
        services.AddSingleton<IEvidenceCollector, SecurityCommsEvidenceCollector>();
        services.AddSingleton<IEvidenceCollector, SystemIntegrityEvidenceCollector>();
        services.AddSingleton<IEvidenceCollector, ContingencyPlanningEvidenceCollector>();
        services.AddSingleton<IEvidenceCollector, IdentificationAuthEvidenceCollector>();
        services.AddSingleton<IEvidenceCollector, ConfigMgmtEvidenceCollector>();
        services.AddSingleton<IEvidenceCollector, IncidentResponseEvidenceCollector>();
        services.AddSingleton<IEvidenceCollector, RiskAssessmentEvidenceCollector>();
        services.AddSingleton<IEvidenceCollector, CertAccreditationEvidenceCollector>();
        services.AddSingleton<IEvidenceCollector, DefaultEvidenceCollector>();

        // ─── Knowledge Base Stubs (Feature 008) ─────────────────────────────
        services.AddSingleton<IStigValidationService, StigValidationService>();
        services.AddSingleton<IRmfKnowledgeService, RmfKnowledgeService>();
        services.AddSingleton<IStigKnowledgeService, StigKnowledgeService>();
        services.AddSingleton<IDoDInstructionService, DoDInstructionService>();
        services.AddSingleton<IDoDWorkflowService, DoDWorkflowService>();
        services.AddSingleton<IImpactLevelService, ImpactLevelService>();
        services.AddSingleton<IFedRampTemplateService, FedRampTemplateService>();

        // ─── Compliance Watch Services ───────────────────────────────────────
        services.AddSingleton<AlertCorrelationService>();
        services.AddSingleton<IAlertCorrelationService>(sp => sp.GetRequiredService<AlertCorrelationService>());
        services.AddSingleton<SystemSubscriptionResolver>();
        services.AddSingleton<ISystemSubscriptionResolver>(sp => sp.GetRequiredService<SystemSubscriptionResolver>());
        services.AddSingleton<AlertManager>(sp => new AlertManager(
            sp.GetRequiredService<IDbContextFactory<AtoCopilotContext>>(),
            sp.GetRequiredService<IOptions<AlertOptions>>(),
            sp.GetRequiredService<ILogger<AlertManager>>(),
            sp,
            sp.GetService<IAlertCorrelationService>()));
        services.AddSingleton<IAlertManager>(sp => sp.GetRequiredService<AlertManager>());
        services.AddSingleton<ComplianceWatchService>();
        services.AddSingleton<IComplianceWatchService>(sp => sp.GetRequiredService<ComplianceWatchService>());
        services.AddSingleton<ActivityLogEventSource>();
        services.AddSingleton<IComplianceEventSource>(sp => sp.GetRequiredService<ActivityLogEventSource>());
        services.AddHostedService<ComplianceWatchHostedService>();

        // HttpClient for NistControlsService with shared Polly resilience pipeline (FR-005a)
        services.AddHttpClient(nameof(NistControlsService))
            .ConfigureResiliencePipeline(new ResiliencePipelineConfig
            {
                Name = "NistControlsService",
                MaxRetryAttempts = 3,
                BaseDelaySeconds = 2.0,
                UseJitter = true
            });

        // Register NistControlsService as a singleton using the named HttpClient
        // from the factory above (with Polly resilience handlers configured).
        services.AddSingleton<INistControlsService>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient(nameof(NistControlsService));
            return new NistControlsService(
                sp.GetRequiredService<ILogger<NistControlsService>>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<IOptions<NistControlsOptions>>(),
                httpClient,
                sp.GetRequiredService<IConfiguration>());
        });

        // NIST Controls cache warmup background service
        services.AddHostedService<NistControlsCacheWarmupService>();

        // Compliance validation service (validates 11 system-critical control IDs)
        services.AddSingleton<ComplianceValidationService>();

        // ─── System ID Resolver (auto-resolves system names/acronyms to GUIDs) ──
        services.AddSingleton<ISystemIdResolver, SystemIdResolver>();

        // ─── RMF Lifecycle & Boundary Services (Feature 015) ────────────────
        services.AddSingleton<IRmfLifecycleService, RmfLifecycleService>();
        services.AddSingleton<IAzureResourceValidator, AzureResourceValidator>();
        services.AddSingleton<IBoundaryService, BoundaryService>();
        services.AddSingleton<ICategorizationService, CategorizationService>();
        services.AddSingleton<IReferenceDataService, ReferenceDataService>();
        services.AddSingleton<IBaselineService, BaselineService>();

        // Register compliance tools
        services.AddSingleton<ComplianceAssessmentTool>();
        services.AddSingleton<ControlFamilyTool>();
        services.AddSingleton<DocumentGenerationTool>();
        services.AddSingleton<EvidenceCollectionTool>();
        services.AddSingleton<RemediationExecuteTool>();
        services.AddSingleton<ValidateRemediationTool>();
        services.AddSingleton<RemediationPlanTool>();
        services.AddSingleton<AssessmentAuditLogTool>();
        services.AddSingleton<ComplianceHistoryTool>();
        services.AddSingleton<ComplianceStatusTool>();
        services.AddSingleton<ComplianceMonitoringTool>();
        services.AddSingleton<ShowFindingsTool>();
        services.AddSingleton<ComplianceChatTool>();
        services.AddSingleton<IacComplianceScanTool>();

        // Compliance Watch monitoring tools
        services.AddSingleton<WatchEnableMonitoringTool>();
        services.AddSingleton<WatchDisableMonitoringTool>();
        services.AddSingleton<WatchConfigureMonitoringTool>();
        services.AddSingleton<WatchMonitoringStatusTool>();

        // Compliance Watch alert lifecycle tools (US2)
        services.AddSingleton<WatchShowAlertsTool>();
        services.AddSingleton<WatchGetAlertTool>();
        services.AddSingleton<WatchAcknowledgeAlertTool>();
        services.AddSingleton<WatchFixAlertTool>();
        services.AddSingleton<WatchDismissAlertTool>();

        // Compliance Watch alert rules & suppression tools (US3)
        services.AddSingleton<WatchCreateRuleTool>();
        services.AddSingleton<WatchListRulesTool>();
        services.AddSingleton<WatchSuppressAlertsTool>();
        services.AddSingleton<WatchListSuppressionsTool>();
        services.AddSingleton<WatchConfigureQuietHoursTool>();

        // Compliance Watch notification & escalation tools (US4)
        services.AddSingleton<WatchConfigureNotificationsTool>();
        services.AddSingleton<WatchConfigureEscalationTool>();

        // Compliance Watch dashboard & reporting tools (US5)
        services.AddSingleton<WatchAlertHistoryTool>();
        services.AddSingleton<WatchComplianceTrendTool>();
        services.AddSingleton<WatchAlertStatisticsTool>();

        // Compliance Watch integration tools (US8)
        services.AddSingleton<WatchCreateTaskFromAlertTool>();
        services.AddSingleton<WatchCollectEvidenceFromAlertTool>();

        // Compliance Watch auto-remediation tools (US9)
        services.AddSingleton<WatchCreateAutoRemediationRuleTool>();
        services.AddSingleton<WatchListAutoRemediationRulesTool>();

        // NIST Controls knowledge tools (Feature 007)
        services.AddSingleton<NistControlSearchTool>();
        services.AddSingleton<NistControlExplainerTool>();

        // RMF Registration tools (Feature 015)
        services.AddSingleton<RegisterSystemTool>();
        services.AddSingleton<ListSystemsTool>();
        services.AddSingleton<GetSystemTool>();
        services.AddSingleton<AdvanceRmfStepTool>();
        services.AddSingleton<DefineBoundaryTool>();
        services.AddSingleton<ExcludeFromBoundaryTool>();
        services.AddSingleton<AssignRmfRoleTool>();
        services.AddSingleton<ListRmfRolesTool>();

        // Boundary Definition tools (Feature 033)
        services.AddSingleton<ListBoundaryDefinitionsTool>();
        services.AddSingleton<CreateBoundaryDefinitionTool>();
        services.AddSingleton<DeleteBoundaryDefinitionTool>();
        services.AddSingleton<BoundaryGapAnalysisTool>();

        // RMF Categorization tools (Feature 015 - US2)
        services.AddSingleton<CategorizeSystemTool>();
        services.AddSingleton<GetCategorizationTool>();
        services.AddSingleton<SuggestInfoTypesTool>();

        // RMF Baseline tools (Feature 015 - US3)
        services.AddSingleton<SelectBaselineTool>();
        services.AddSingleton<TailorBaselineTool>();
        services.AddSingleton<SetInheritanceTool>();
        services.AddSingleton<GetBaselineTool>();
        services.AddSingleton<GenerateCrmTool>();

        // RMF STIG Mapping tools (Feature 015 - US4)
        services.AddSingleton<ShowStigMappingTool>();

        // SSP Authoring service and tools (Feature 015 - US5)
        services.AddSingleton<ISspService, SspService>();
        services.AddSingleton<WriteNarrativeTool>();
        services.AddSingleton<SuggestNarrativeTool>();
        services.AddSingleton<BatchPopulateNarrativesTool>();
        services.AddSingleton<NarrativeProgressTool>();
        services.AddSingleton<GenerateSspTool>();

        // Narrative Governance service (Feature 024)
        services.AddSingleton<INarrativeGovernanceService, NarrativeGovernanceService>();

        // ─── HW/SW Inventory service (Feature 025) ──────────────────────────
        services.AddSingleton<IInventoryService, InventoryService>();

        // Narrative Governance tools (Feature 024)
        services.AddSingleton<NarrativeHistoryTool>();
        services.AddSingleton<NarrativeDiffTool>();
        services.AddSingleton<RollbackNarrativeTool>();
        services.AddSingleton<SubmitNarrativeTool>();
        services.AddSingleton<ReviewNarrativeTool>();
        services.AddSingleton<BatchReviewNarrativesTool>();
        services.AddSingleton<NarrativeApprovalProgressTool>();
        services.AddSingleton<BatchSubmitNarrativesTool>();

        // HW/SW Inventory tools (Feature 025)
        services.AddSingleton<InventoryAddItemTool>();
        services.AddSingleton<InventoryUpdateItemTool>();
        services.AddSingleton<InventoryDecommissionItemTool>();
        services.AddSingleton<InventoryListTool>();
        services.AddSingleton<InventoryGetTool>();
        services.AddSingleton<InventoryExportTool>();
        services.AddSingleton<InventoryImportTool>();
        services.AddSingleton<InventoryCompletenessTool>();
        services.AddSingleton<InventoryAutoSeedTool>();

        // SSP Section Authoring tools (Feature 022)
        services.AddSingleton<WriteSspSectionTool>();
        services.AddSingleton<ReviewSspSectionTool>();
        services.AddSingleton<SspCompletenessTool>();

        // OSCAL SSP Export (Feature 022 Phase 5)
        services.AddSingleton<IOscalSspExportService, OscalSspExportService>();
        services.AddSingleton<ExportOscalSspTool>();

        // OSCAL Validation (Feature 022 Phase 6)
        services.AddSingleton<IOscalValidationService, OscalValidationService>();
        services.AddSingleton<ValidateOscalSspTool>();

        // Assessment Artifact service and tools (Feature 015 - US7)
        services.AddSingleton<IAssessmentArtifactService, AssessmentArtifactService>();
        services.AddSingleton<AssessControlTool>();
        services.AddSingleton<TakeSnapshotTool>();
        services.AddSingleton<CompareSnapshotsTool>();
        services.AddSingleton<VerifyEvidenceTool>();
        services.AddSingleton<CheckEvidenceCompletenessTool>();
        services.AddSingleton<GenerateSarTool>();

        // Authorization Decision service and tools (Feature 015 - US8)
        services.AddSingleton<IAuthorizationService, AuthorizationService>();
        services.AddSingleton<IssueAuthorizationTool>();
        services.AddSingleton<AcceptRiskTool>();
        services.AddSingleton<ShowRiskRegisterTool>();
        services.AddSingleton<CreatePoamTool>();
        services.AddSingleton<ListPoamTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.GetPoamTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.LinkPoamComponentTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.UnlinkPoamComponentTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.PoamByComponentTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.BulkCreatePoamFromFindingsTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.UpdatePoamTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.ClosePoamTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.UpdatePoamMilestoneTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.BulkUpdatePoamTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.LinkPoamTaskTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.UnlinkPoamTaskTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.CreateTaskFromPoamTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.PoamMetricsTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.PoamTrendTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.ConfigureTicketingTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.SyncPoamTicketTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.BulkSyncTicketsTool>();
        services.AddSingleton<Ato.Copilot.Agents.Compliance.Tools.Poam.ExportPoamTool>();
        services.AddSingleton<GenerateRarTool>();
        services.AddSingleton<BundleAuthorizationPackageTool>();

        // ─── US9: Continuous Monitoring tools ────────────────────────────────
        services.AddSingleton<IConMonService, ConMonService>();
        services.AddSingleton<CreateConMonPlanTool>();
        services.AddSingleton<GenerateConMonReportTool>();
        services.AddSingleton<ReportSignificantChangeTool>();
        services.AddSingleton<TrackAtoExpirationTool>();
        services.AddSingleton<MultiSystemDashboardTool>();
        services.AddSingleton<ReauthorizationWorkflowTool>();
        services.AddSingleton<NotificationDeliveryTool>();

        // ─── US10: eMASS & OSCAL Interoperability tools ──────────────────────
        services.AddSingleton<IEmassExportService, EmassExportService>();
        services.AddSingleton<ExportEmassTool>();
        services.AddSingleton<ImportEmassTool>();
        services.AddSingleton<ExportOscalTool>();

        // ─── US11: Document Templates & PDF Export tools ─────────────────────
        services.AddSingleton<IDocumentTemplateService, DocumentTemplateService>();
        services.AddSingleton<UploadTemplateTool>();
        services.AddSingleton<ListTemplatesTool>();
        services.AddSingleton<UpdateTemplateTool>();
        services.AddSingleton<DeleteTemplateTool>();

        // ─── Feature 017: SCAP/STIG Viewer Import tools ─────────────────────
        services.AddSingleton<ICklParser, CklParser>();
        services.AddSingleton<IXccdfParser, XccdfParser>();
        services.AddSingleton<ICklGenerator, CklGenerator>();
        services.AddSingleton<IScanImportService, ScanImportService>();
        services.AddSingleton<ImportCklTool>();
        services.AddSingleton<ImportXccdfTool>();
        services.AddSingleton<ExportCklTool>();
        services.AddSingleton<ListImportsTool>();
        services.AddSingleton<GetImportSummaryTool>();

        // ─── Feature 019: Prisma Cloud Scan Import ───────────────────────────
        services.AddSingleton<PrismaCsvParser>();
        services.AddSingleton<PrismaApiJsonParser>();
        services.AddSingleton<ImportPrismaCsvTool>();
        services.AddSingleton<ImportPrismaApiTool>();
        services.AddSingleton<ListPrismaPoliciesTool>();
        services.AddSingleton<PrismaTrendTool>();

        // ─── Feature 026: ACAS/Nessus Scan Import ───────────────────────────
        services.AddSingleton<INessusParser, NessusParser>();
        services.AddSingleton<PluginFamilyMappings>();
        services.AddSingleton<INessusControlMapper, NessusControlMapper>();
        services.AddSingleton<ImportNessusTool>();
        services.AddSingleton<ListNessusImportsTool>();

        // ─── Feature 018: SAP Generation service and tools ───────────────────
        services.AddSingleton<ISapService, SapService>();
        services.AddSingleton<GenerateSapTool>();
        services.AddSingleton<UpdateSapTool>();
        services.AddSingleton<FinalizeSapTool>();
        services.AddSingleton<GetSapTool>();
        services.AddSingleton<ListSapsTool>();

        // ─── Feature 021: Privacy & Interconnection Services ─────────────────
        services.AddSingleton<IPrivacyService, PrivacyService>();
        services.AddSingleton<IInterconnectionService, InterconnectionService>();
        services.AddSingleton<CreatePtaTool>();
        services.AddSingleton<GeneratePiaTool>();
        services.AddSingleton<ReviewPiaTool>();
        services.AddSingleton<CheckPrivacyComplianceTool>();
        services.AddSingleton<AddInterconnectionTool>();
        services.AddSingleton<ListInterconnectionsTool>();
        services.AddSingleton<UpdateInterconnectionTool>();
        services.AddSingleton<GenerateIsaTool>();
        services.AddSingleton<RegisterAgreementTool>();
        services.AddSingleton<UpdateAgreementTool>();
        services.AddSingleton<CertifyNoInterconnectionsTool>();
        services.AddSingleton<ValidateAgreementsTool>();

        // Compliance Watch notification & escalation services (US4)
        services.AddSingleton<AlertNotificationService>();
        services.AddSingleton<IAlertNotificationService>(sp => sp.GetRequiredService<AlertNotificationService>());
        services.AddSingleton<EscalationHostedService>();
        services.AddSingleton<IEscalationService>(sp => sp.GetRequiredService<EscalationHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<EscalationHostedService>());
        services.AddHostedService<DigestSchedulerHostedService>();

        // Register tools as BaseTool collection
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ComplianceAssessmentTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ControlFamilyTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<DocumentGenerationTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<EvidenceCollectionTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<RemediationExecuteTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ValidateRemediationTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<RemediationPlanTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<AssessmentAuditLogTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ComplianceHistoryTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ComplianceStatusTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ComplianceMonitoringTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ShowFindingsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ComplianceChatTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<IacComplianceScanTool>());

        // Compliance Watch tools as BaseTool
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchEnableMonitoringTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchDisableMonitoringTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchConfigureMonitoringTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchMonitoringStatusTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchShowAlertsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchGetAlertTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchAcknowledgeAlertTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchFixAlertTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchDismissAlertTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchCreateRuleTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchListRulesTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchSuppressAlertsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchListSuppressionsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchConfigureQuietHoursTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchConfigureNotificationsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchConfigureEscalationTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchAlertHistoryTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchComplianceTrendTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchAlertStatisticsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchCreateTaskFromAlertTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchCollectEvidenceFromAlertTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchCreateAutoRemediationRuleTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WatchListAutoRemediationRulesTool>());

        // NIST Controls knowledge tools as BaseTool (Feature 007)
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<NistControlSearchTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<NistControlExplainerTool>());

        // RMF Registration tools as BaseTool (Feature 015)
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<RegisterSystemTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ListSystemsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GetSystemTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<AdvanceRmfStepTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<DefineBoundaryTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ExcludeFromBoundaryTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<AssignRmfRoleTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ListRmfRolesTool>());

        // Boundary Definition tools as BaseTool (Feature 033)
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ListBoundaryDefinitionsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CreateBoundaryDefinitionTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<DeleteBoundaryDefinitionTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<BoundaryGapAnalysisTool>());

        // RMF Categorization tools as BaseTool (Feature 015 - US2)
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CategorizeSystemTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GetCategorizationTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<SuggestInfoTypesTool>());

        // RMF Baseline tools as BaseTool (Feature 015 - US3)
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<SelectBaselineTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<TailorBaselineTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<SetInheritanceTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GetBaselineTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GenerateCrmTool>());

        // RMF STIG Mapping tools as BaseTool (Feature 015 - US4)
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ShowStigMappingTool>());

        // SSP Authoring tools as BaseTool (Feature 015 - US5)
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WriteNarrativeTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<SuggestNarrativeTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<BatchPopulateNarrativesTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<NarrativeProgressTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GenerateSspTool>());

        // SSP Section Authoring tools as BaseTool (Feature 022)
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<WriteSspSectionTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ReviewSspSectionTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<SspCompletenessTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ExportOscalSspTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ValidateOscalSspTool>());

        // Narrative Governance tools as BaseTool (Feature 024)
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<NarrativeHistoryTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<NarrativeDiffTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<RollbackNarrativeTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<SubmitNarrativeTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ReviewNarrativeTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<BatchReviewNarrativesTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<NarrativeApprovalProgressTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<BatchSubmitNarrativesTool>());

        // HW/SW Inventory tools as BaseTool (Feature 025)
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<InventoryAddItemTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<InventoryUpdateItemTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<InventoryDecommissionItemTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<InventoryListTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<InventoryGetTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<InventoryExportTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<InventoryImportTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<InventoryCompletenessTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<InventoryAutoSeedTool>());

        // Assessment Artifact tools as BaseTool (Feature 015 - US7)
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<AssessControlTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<TakeSnapshotTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CompareSnapshotsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<VerifyEvidenceTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CheckEvidenceCompletenessTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GenerateSarTool>());

        // Authorization Decision tools as BaseTool (Feature 015 - US8)
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<IssueAuthorizationTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<AcceptRiskTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ShowRiskRegisterTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CreatePoamTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ListPoamTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.GetPoamTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.LinkPoamComponentTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.UnlinkPoamComponentTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.PoamByComponentTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.BulkCreatePoamFromFindingsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.UpdatePoamTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.ClosePoamTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.UpdatePoamMilestoneTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.BulkUpdatePoamTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.LinkPoamTaskTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.UnlinkPoamTaskTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.CreateTaskFromPoamTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.PoamMetricsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.PoamTrendTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.ConfigureTicketingTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.SyncPoamTicketTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.BulkSyncTicketsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Compliance.Tools.Poam.ExportPoamTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GenerateRarTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<BundleAuthorizationPackageTool>());

        // US9: Continuous Monitoring BaseTool wrappers
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CreateConMonPlanTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GenerateConMonReportTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ReportSignificantChangeTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<TrackAtoExpirationTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<MultiSystemDashboardTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ReauthorizationWorkflowTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<NotificationDeliveryTool>());

        // US10: eMASS & OSCAL BaseTool wrappers
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ExportEmassTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ImportEmassTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ExportOscalTool>());

        // US11: Document Templates & PDF Export BaseTool wrappers
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<UploadTemplateTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ListTemplatesTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<UpdateTemplateTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<DeleteTemplateTool>());

        // Feature 017: SCAP/STIG Import BaseTool wrappers
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ImportCklTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ImportXccdfTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ExportCklTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ListImportsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GetImportSummaryTool>());

        // Feature 018: SAP Generation BaseTool wrappers
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GenerateSapTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<UpdateSapTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<FinalizeSapTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GetSapTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ListSapsTool>());

        // Feature 019: Prisma Cloud Import BaseTool wrappers
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ImportPrismaCsvTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ImportPrismaApiTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ListPrismaPoliciesTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<PrismaTrendTool>());

        // Feature 026: ACAS/Nessus Import BaseTool wrappers
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ImportNessusTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ListNessusImportsTool>());

        // Feature 021: Privacy tools BaseTool wrappers
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CreatePtaTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GeneratePiaTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ReviewPiaTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CheckPrivacyComplianceTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<AddInterconnectionTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ListInterconnectionsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<UpdateInterconnectionTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GenerateIsaTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<RegisterAgreementTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<UpdateAgreementTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CertifyNoInterconnectionsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ValidateAgreementsTool>());

        // Feature 029: Cache management tool
        services.AddSingleton<Ato.Copilot.Agents.Tools.ClearCacheTool>();
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<Ato.Copilot.Agents.Tools.ClearCacheTool>());

        // Feature 031: Implementation Roadmap tools
        services.AddSingleton<GenerateRoadmapTool>();
        services.AddSingleton<GetRoadmapTool>();
        services.AddSingleton<GetRoadmapProgressTool>();
        services.AddSingleton<UpdateRoadmapTool>();
        services.AddSingleton<CreateBoardFromRoadmapTool>();
        services.AddSingleton<ExportRoadmapPdfTool>();
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GenerateRoadmapTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GetRoadmapTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GetRoadmapProgressTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<UpdateRoadmapTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CreateBoardFromRoadmapTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ExportRoadmapPdfTool>());

        // Feature 035: Deviation Management tools
        services.AddSingleton<RequestDeviationTool>();
        services.AddSingleton<ReviewDeviationTool>();
        services.AddSingleton<ListDeviationsTool>();
        services.AddSingleton<RevokeDeviationTool>();
        services.AddSingleton<ExtendDeviationTool>();
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<RequestDeviationTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ReviewDeviationTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ListDeviationsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<RevokeDeviationTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ExtendDeviationTool>());

        // Feature 040: Component-Centric Boundary tools
        services.AddSingleton<DiscoverAzureComponentsTool>();
        services.AddSingleton<ImportAzureComponentsTool>();
        services.AddSingleton<AssignComponentToBoundaryTool>();
        services.AddSingleton<ListBoundaryComponentsTool>();
        services.AddSingleton<UpdateComponentScopeTool>();
        services.AddSingleton<RemoveComponentFromBoundaryTool>();
        services.AddSingleton<ComponentRiskSummaryTool>();
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<DiscoverAzureComponentsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ImportAzureComponentsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<AssignComponentToBoundaryTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ListBoundaryComponentsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<UpdateComponentScopeTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<RemoveComponentFromBoundaryTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ComponentRiskSummaryTool>());

        // Register the agent
        services.AddSingleton<ComplianceAgent>();
        services.AddSingleton<BaseAgent>(sp => sp.GetRequiredService<ComplianceAgent>());

        // ─── SystemIdResolver property injection ─────────────────────────────
        // Injects the ISystemIdResolver into ALL BaseTool singletons at startup
        // so system_id parameters transparently accept names/acronyms.
        services.AddHostedService<SystemIdResolverInitializer>();

        // ─── Kanban Services ─────────────────────────────────────────────────
        services.AddScoped<IKanbanService, KanbanService>();
        services.AddScoped<ITaskEnrichmentService, TaskEnrichmentService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddHostedService<OverdueScanHostedService>();
        services.AddHostedService<SessionCleanupHostedService>();

        // ─── Kanban Tools (Singleton — uses IServiceScopeFactory for scoped IKanbanService)
        services.AddSingleton<KanbanCreateBoardTool>();
        services.AddSingleton<KanbanBoardShowTool>();
        services.AddSingleton<KanbanGetTaskTool>();
        services.AddSingleton<KanbanCreateTaskTool>();
        services.AddSingleton<KanbanAssignTaskTool>();
        services.AddSingleton<KanbanMoveTaskTool>();
        services.AddSingleton<KanbanTaskListTool>();
        services.AddSingleton<KanbanTaskHistoryTool>();
        services.AddSingleton<KanbanValidateTaskTool>();
        services.AddSingleton<KanbanAddCommentTool>();
        services.AddSingleton<KanbanTaskCommentsTool>();
        services.AddSingleton<KanbanEditCommentTool>();
        services.AddSingleton<KanbanDeleteCommentTool>();
        services.AddSingleton<KanbanRemediateTaskTool>();
        services.AddSingleton<KanbanCollectEvidenceTool>();
        services.AddSingleton<KanbanBulkUpdateTool>();
        services.AddSingleton<KanbanExportTool>();
        services.AddSingleton<KanbanArchiveBoardTool>();
        services.AddSingleton<KanbanGenerateScriptTool>();
        services.AddSingleton<KanbanGenerateValidationTool>();

        // Register Kanban tools as BaseTool collection
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanCreateBoardTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanBoardShowTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanGetTaskTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanCreateTaskTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanAssignTaskTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanMoveTaskTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanTaskListTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanTaskHistoryTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanValidateTaskTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanAddCommentTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanTaskCommentsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanEditCommentTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanDeleteCommentTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanRemediateTaskTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanCollectEvidenceTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanBulkUpdateTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanExportTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanArchiveBoardTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanGenerateScriptTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<KanbanGenerateValidationTool>());

        // ─── CAC/Auth Services ───────────────────────────────────────────────
        services.AddScoped<ICacSessionService, CacSessionService>();
        services.AddScoped<IPimService>(sp => new PimService(
            sp.GetRequiredService<IDbContextFactory<AtoCopilotContext>>(),
            sp.GetRequiredService<IOptions<PimServiceOptions>>(),
            sp.GetRequiredService<ILogger<PimService>>(),
            sp.GetRequiredService<INotificationService>()));
        services.AddScoped<IJitVmAccessService, JitVmAccessService>();
        services.AddScoped<ICertificateRoleResolver, CertificateRoleResolver>();

        // ─── Data Retention Cleanup ──────────────────────────────────────────
        var retentionConfig = configuration.GetSection(RetentionPolicyOptions.SectionName);
        var enableCleanup = retentionConfig.GetValue("EnableAutomaticCleanup", true);
        if (enableCleanup)
        {
            services.AddHostedService<RetentionCleanupHostedService>();
        }

        // ─── Auth/PIM Tools (Singleton — uses IServiceScopeFactory for scoped services)
        services.AddSingleton<CacStatusTool>();
        services.AddSingleton<CacSignOutTool>();
        services.AddSingleton<CacSetTimeoutTool>();
        services.AddSingleton<CacMapCertificateTool>();
        services.AddSingleton<PimListEligibleTool>();
        services.AddSingleton<PimActivateRoleTool>();
        services.AddSingleton<PimDeactivateRoleTool>();
        services.AddSingleton<PimListActiveTool>();
        services.AddSingleton<PimExtendRoleTool>();
        services.AddSingleton<PimApproveRequestTool>();
        services.AddSingleton<PimDenyRequestTool>();
        services.AddSingleton<JitRequestAccessTool>();
        services.AddSingleton<JitListSessionsTool>();
        services.AddSingleton<JitRevokeAccessTool>();
        services.AddSingleton<PimHistoryTool>();

        // Register Auth/PIM tools as BaseTool collection
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CacStatusTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CacSignOutTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CacSetTimeoutTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<CacMapCertificateTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<PimListEligibleTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<PimActivateRoleTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<PimDeactivateRoleTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<PimListActiveTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<PimExtendRoleTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<PimApproveRequestTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<PimDenyRequestTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<JitRequestAccessTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<JitListSessionsTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<JitRevokeAccessTool>());
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<PimHistoryTool>());

        return services;
    }

    /// <summary>
    /// Register the configuration agent and its tool.
    /// </summary>
    public static IServiceCollection AddConfigurationAgent(this IServiceCollection services)
    {
        // Register the configuration tool
        services.AddSingleton<ConfigurationTool>();
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ConfigurationTool>());

        // Register the agent
        services.AddSingleton<ConfigurationAgent>();
        services.AddSingleton<BaseAgent>(sp => sp.GetRequiredService<ConfigurationAgent>());

        return services;
    }

    /// <summary>
    /// Register the KnowledgeBase agent, its options, and service implementations.
    /// </summary>
    public static IServiceCollection AddKnowledgeBaseAgent(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind options
        services.Configure<KnowledgeBaseAgentOptions>(
            configuration.GetSection("Agents:KnowledgeBaseAgent"));

        // Register KB tools
        services.AddSingleton<ExplainNistControlTool>();
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ExplainNistControlTool>());
        services.AddSingleton<SearchNistControlsTool>();
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<SearchNistControlsTool>());
        services.AddSingleton<ExplainStigTool>();
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ExplainStigTool>());
        services.AddSingleton<SearchStigsTool>();
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<SearchStigsTool>());
        services.AddSingleton<ExplainRmfTool>();
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ExplainRmfTool>());
        services.AddSingleton<ExplainImpactLevelTool>();
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<ExplainImpactLevelTool>());
        services.AddSingleton<GetFedRampTemplateGuidanceTool>();
        services.AddSingleton<BaseTool>(sp => sp.GetRequiredService<GetFedRampTemplateGuidanceTool>());

        // Register the agent
        services.AddSingleton<KnowledgeBaseAgent>();
        services.AddSingleton<BaseAgent>(sp => sp.GetRequiredService<KnowledgeBaseAgent>());

        return services;
    }

    /// <summary>
    /// Configures a shared resilience pipeline on an <see cref="IHttpClientBuilder"/> with
    /// retry, circuit breaker, and timeout policies per FR-001 through FR-005.
    /// Pipeline order: Retry (with Retry-After support) → Circuit Breaker → Timeout.
    /// </summary>
    /// <param name="builder">The HTTP client builder to add resilience to.</param>
    /// <param name="config">Pipeline configuration with retry, circuit breaker, and timeout settings.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHttpClientBuilder ConfigureResiliencePipeline(
        this IHttpClientBuilder builder,
        ResiliencePipelineConfig config)
    {
        builder.AddResilienceHandler($"resilience-{config.Name}", pipelineBuilder =>
        {
            // Retry with Retry-After header support (FR-001, FR-002)
            pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = config.MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(config.BaseDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = config.UseJitter,
                ShouldRetryAfterHeader = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(r => r.StatusCode is
                        System.Net.HttpStatusCode.ServiceUnavailable or
                        System.Net.HttpStatusCode.TooManyRequests or
                        System.Net.HttpStatusCode.GatewayTimeout or
                        System.Net.HttpStatusCode.RequestTimeout),
                OnRetry = args =>
                {
                    var logger = args.Context.Properties.GetValue(
                        new ResiliencePropertyKey<ILogger>("logger"), null!);
                    logger?.LogWarning(
                        "Retry attempt {AttemptNumber} for {Dependency} after {Delay}ms. Status: {StatusCode}",
                        args.AttemptNumber + 1,
                        config.Name,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Result?.StatusCode);
                    return ValueTask.CompletedTask;
                }
            });

            // Circuit breaker (FR-003)
            pipelineBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                MinimumThroughput = config.CircuitBreakerFailureThreshold,
                SamplingDuration = TimeSpan.FromSeconds(config.CircuitBreakerSamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(config.CircuitBreakerBreakDurationSeconds),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(r => r.StatusCode is
                        System.Net.HttpStatusCode.ServiceUnavailable or
                        System.Net.HttpStatusCode.TooManyRequests or
                        System.Net.HttpStatusCode.GatewayTimeout or
                        System.Net.HttpStatusCode.RequestTimeout),
                OnOpened = args =>
                {
                    var logger = args.Context.Properties.GetValue(
                        new ResiliencePropertyKey<ILogger>("logger"), null!);
                    logger?.LogWarning(
                        "Circuit breaker OPENED for {Dependency}. Break duration: {BreakDuration}s",
                        config.Name,
                        config.CircuitBreakerBreakDurationSeconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    var logger = args.Context.Properties.GetValue(
                        new ResiliencePropertyKey<ILogger>("logger"), null!);
                    logger?.LogInformation(
                        "Circuit breaker CLOSED for {Dependency}. Calls will resume.",
                        config.Name);
                    return ValueTask.CompletedTask;
                }
            });

            // Timeout (FR-004)
            pipelineBuilder.AddTimeout(TimeSpan.FromSeconds(config.RequestTimeoutSeconds));
        });

        return builder;
    }
}

using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Core compliance scanning engine for NIST 800-53 assessments.
/// Orchestrates multi-scope assessments, family-specific scanning, evidence collection,
/// risk analysis, certificate generation, continuous monitoring, and data access.
/// </summary>
public interface IAtoComplianceEngine
{
    // ─── Legacy Assessment Method (backward compatible) ─────────────

    /// <summary>
    /// Run a compliance assessment with flexible parameters.
    /// Retained for backward compatibility with existing tools and services.
    /// New code should prefer <see cref="RunComprehensiveAssessmentAsync"/>.
    /// </summary>
    Task<ComplianceAssessment> RunAssessmentAsync(
        string subscriptionId,
        string? framework = null,
        string? controlFamilies = null,
        string? resourceTypes = null,
        string? scanType = null,
        bool includePassed = false,
        CancellationToken cancellationToken = default);

    // ─── Core Assessment Methods (US1) ──────────────────────────────

    /// <summary>
    /// Run a comprehensive compliance assessment for a single subscription.
    /// Orchestrates all 3 scan pillars across all 20 NIST families.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID (valid GUID format).</param>
    /// <param name="resourceGroup">Optional resource group constraint.</param>
    /// <param name="progress">Optional progress reporter for per-family updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completed assessment with findings, scores, and executive summary.</returns>
    Task<ComplianceAssessment> RunComprehensiveAssessmentAsync(
        string subscriptionId,
        string? resourceGroup = null,
        IProgress<AssessmentProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams compliance findings incrementally as each control family completes.
    /// Yields individual <see cref="ComplianceFinding"/> items without buffering the entire result set.
    /// Use when the expected finding count exceeds the streaming threshold (default 50).
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID (valid GUID format).</param>
    /// <param name="resourceGroup">Optional resource group constraint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of compliance findings yielded per family scan.</returns>
    IAsyncEnumerable<ComplianceFinding> StreamAssessmentFindingsAsync(
        string subscriptionId,
        string? resourceGroup = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run a multi-subscription environment assessment.
    /// Pre-warms caches for all subscriptions, aggregates results.
    /// </summary>
    /// <param name="subscriptionIds">Subscription IDs to assess.</param>
    /// <param name="environmentName">Environment identifier (e.g., "Production").</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated assessment across all subscriptions.</returns>
    Task<ComplianceAssessment> RunEnvironmentAssessmentAsync(
        IEnumerable<string> subscriptionIds,
        string environmentName,
        IProgress<AssessmentProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assess a single control family within a subscription.
    /// Dispatches to the appropriate scanner via IScannerRegistry.
    /// </summary>
    /// <param name="familyCode">Two-letter NIST family code (must pass ControlFamilies.IsValidFamily).</param>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="resourceGroup">Optional resource group constraint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Per-family assessment result.</returns>
    Task<ControlFamilyAssessment> AssessControlFamilyAsync(
        string familyCode,
        string subscriptionId,
        string? resourceGroup = null,
        CancellationToken cancellationToken = default);

    // ─── Evidence Collection (US3) ──────────────────────────────────

    /// <summary>
    /// Collect compliance evidence for a control family or all families.
    /// </summary>
    /// <param name="familyCode">Family code or "All" for all families.</param>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="resourceGroup">Optional resource group constraint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evidence package with completeness score and attestation.</returns>
    Task<EvidencePackage> CollectEvidenceAsync(
        string familyCode,
        string subscriptionId,
        string? resourceGroup = null,
        CancellationToken cancellationToken = default);

    // ─── Risk Assessment (US4) ──────────────────────────────────────

    /// <summary>
    /// Calculate risk profile from assessment findings.
    /// Uses severity weights: Critical=10, High=7.5, Medium=5, Low=2.5.
    /// </summary>
    /// <param name="assessment">Assessment to analyze.</param>
    /// <returns>Severity-weighted risk profile.</returns>
    RiskProfile CalculateRiskProfile(ComplianceAssessment assessment);

    /// <summary>
    /// Perform full 8-category risk assessment.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Risk assessment with 8 categories scored 1-10.</returns>
    Task<RiskAssessment> PerformRiskAssessmentAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    // ─── Certificate Generation (US5) ───────────────────────────────

    /// <summary>
    /// Generate compliance certificate if score ≥ 80%.
    /// Certificate has 6-month validity and SHA-256 verification hash.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="issuedBy">Issuer identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compliance certificate with per-family attestations.</returns>
    Task<ComplianceCertificate> GenerateCertificateAsync(
        string subscriptionId,
        string issuedBy,
        CancellationToken cancellationToken = default);

    // ─── Continuous Monitoring (US6) ────────────────────────────────

    /// <summary>
    /// Get continuous compliance status by delegating to Compliance Watch.
    /// Aggregates monitoring status, drift detection, and alert counts.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Real-time compliance posture.</returns>
    Task<ContinuousComplianceStatus> GetContinuousComplianceStatusAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate compliance timeline from historical data and Compliance Watch alerts.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="startDate">Timeline start date.</param>
    /// <param name="endDate">Timeline end date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Timeline with data points, events, trend, and insights.</returns>
    Task<ComplianceTimeline> GetComplianceTimelineAsync(
        string subscriptionId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);

    // ─── Data Access (US7) ──────────────────────────────────────────

    /// <summary>Get assessment history for a subscription.</summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="days">Number of days to look back (default 30).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of assessments ordered by date descending.</returns>
    Task<List<ComplianceAssessment>> GetAssessmentHistoryAsync(
        string subscriptionId,
        int days = 30,
        CancellationToken cancellationToken = default);

    /// <summary>Get a single finding by ID.</summary>
    /// <param name="findingId">Finding identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The finding, or null if not found.</returns>
    Task<ComplianceFinding?> GetFindingAsync(
        string findingId,
        CancellationToken cancellationToken = default);

    /// <summary>Update finding status (Open → InProgress → Remediated, etc.).</summary>
    /// <param name="findingId">Finding identifier.</param>
    /// <param name="newStatus">New finding status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if finding was found and updated.</returns>
    Task<bool> UpdateFindingStatusAsync(
        string findingId,
        FindingStatus newStatus,
        CancellationToken cancellationToken = default);

    /// <summary>Save or update an assessment (upsert semantics).</summary>
    /// <param name="assessment">Assessment to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAssessmentAsync(
        ComplianceAssessment assessment,
        CancellationToken cancellationToken = default);

    /// <summary>Get the most recent assessment for a subscription (cached 24h).</summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Latest assessment, or null if none exist.</returns>
    Task<ComplianceAssessment?> GetLatestAssessmentAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>Get assessment audit log entries.</summary>
    /// <param name="subscriptionId">Optional subscription filter.</param>
    /// <param name="days">Number of days to look back (default 7).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted audit log string.</returns>
    Task<string> GetAuditLogAsync(
        string? subscriptionId = null,
        int days = 7,
        CancellationToken cancellationToken = default);

    /// <summary>Generate executive summary markdown from assessment data.</summary>
    /// <param name="assessment">Assessment to summarize.</param>
    /// <returns>Markdown executive summary.</returns>
    string GenerateExecutiveSummary(ComplianceAssessment assessment);
}

/// <summary>
/// Remediation engine for compliance findings.
/// Provides 18 unique method names (21 total signatures including overloads)
/// across three tiers: existing backward-compatible methods, enhanced core operations,
/// and workflow/tracking/AI-enhanced capabilities.
/// </summary>
public interface IRemediationEngine
{
    // ═══════════════════════════════════════════════════════
    // TIER 1: EXISTING METHODS (backward compatible)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Generates a remediation plan for a subscription based on latest assessment.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID</param>
    /// <param name="resourceGroupName">Optional resource group filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A RemediationPlan with steps and timeline</returns>
    Task<RemediationPlan> GeneratePlanAsync(
        string subscriptionId,
        string? resourceGroupName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes remediation for a single finding (existing signature).
    /// Returns JSON string result for backward compatibility.
    /// </summary>
    /// <param name="findingId">Finding to remediate</param>
    /// <param name="applyRemediation">Whether to apply (true) or dry-run (false)</param>
    /// <param name="dryRun">Explicit dry-run flag</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON string with execution result</returns>
    Task<string> ExecuteRemediationAsync(
        string findingId,
        bool applyRemediation = false,
        bool dryRun = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a remediation execution (existing signature).
    /// Returns JSON string result for backward compatibility.
    /// </summary>
    /// <param name="findingId">Finding to validate</param>
    /// <param name="executionId">Optional execution ID</param>
    /// <param name="subscriptionId">Optional subscription scope</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON string with validation result</returns>
    Task<string> ValidateRemediationAsync(
        string findingId,
        string? executionId = null,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch remediates findings by filter criteria (existing signature).
    /// Returns JSON string result for backward compatibility.
    /// </summary>
    /// <param name="subscriptionId">Optional subscription filter</param>
    /// <param name="severity">Optional severity filter</param>
    /// <param name="family">Optional control family filter</param>
    /// <param name="dryRun">Dry-run flag</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON string with batch result</returns>
    Task<string> BatchRemediateAsync(
        string? subscriptionId = null,
        string? severity = null,
        string? family = null,
        bool dryRun = true,
        CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════
    // TIER 2: ENHANCED CORE OPERATIONS (new methods)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Generates an enhanced remediation plan from a collection of findings
    /// with filtering, prioritization, timeline, and risk scoring.
    /// </summary>
    /// <param name="findings">Findings to plan for</param>
    /// <param name="options">Plan generation options (filters)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Enhanced RemediationPlan with items, timeline, and risk metrics</returns>
    Task<RemediationPlan> GenerateRemediationPlanAsync(
        IEnumerable<ComplianceFinding> findings,
        RemediationPlanOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a remediation plan for a single finding using 3-tier fallback.
    /// </summary>
    /// <param name="finding">Single finding to plan for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>RemediationPlan for the single finding</returns>
    Task<RemediationPlan> GenerateRemediationPlanAsync(
        ComplianceFinding finding,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a remediation plan for a subscription with enhanced options.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID</param>
    /// <param name="options">Plan generation options (filters)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Enhanced RemediationPlan</returns>
    Task<RemediationPlan> GenerateRemediationPlanAsync(
        string subscriptionId,
        RemediationPlanOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Executes remediation for a single finding with typed options and result.
    /// </summary>
    /// <param name="findingId">Finding to remediate</param>
    /// <param name="options">Execution options</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Typed execution result</returns>
    Task<RemediationExecution> ExecuteRemediationAsync(
        string findingId,
        RemediationExecutionOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a remediation execution with typed result.
    /// Checks execution status, steps completed, and changes applied.
    /// </summary>
    /// <param name="executionId">Execution to validate</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Typed validation result with per-check details</returns>
    Task<RemediationValidationResult> ValidateRemediationAsync(
        string executionId,
        CancellationToken ct);

    /// <summary>
    /// Executes batch remediation with concurrency control and typed results.
    /// </summary>
    /// <param name="findingIds">Findings to remediate</param>
    /// <param name="options">Batch options</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Typed batch result</returns>
    Task<BatchRemediationResult> ExecuteBatchRemediationAsync(
        IEnumerable<string> findingIds,
        BatchRemediationOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Rolls back a previously executed remediation using the before-snapshot.
    /// </summary>
    /// <param name="executionId">Execution to roll back</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Rollback result</returns>
    Task<RemediationRollbackResult> RollbackRemediationAsync(
        string executionId,
        CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════
    // TIER 3: WORKFLOW, TRACKING & AI-ENHANCED
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Gets progress snapshot for a subscription (last 30 days).
    /// </summary>
    /// <param name="subscriptionId">Optional subscription filter</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Progress snapshot with counts and rates</returns>
    Task<RemediationProgress> GetRemediationProgressAsync(
        string? subscriptionId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets execution history for a date range with optional pagination.
    /// </summary>
    /// <param name="startDate">Range start</param>
    /// <param name="endDate">Range end</param>
    /// <param name="subscriptionId">Optional subscription filter</param>
    /// <param name="skip">Pagination offset (default 0)</param>
    /// <param name="take">Pagination page size (default 50)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Execution history with aggregate metrics</returns>
    Task<RemediationHistory> GetRemediationHistoryAsync(
        DateTime startDate,
        DateTime endDate,
        string? subscriptionId = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Analyzes remediation impact before execution.
    /// </summary>
    /// <param name="findings">Findings to analyze</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Impact analysis with risk scores and per-resource details</returns>
    Task<RemediationImpactAnalysis> AnalyzeRemediationImpactAsync(
        IEnumerable<ComplianceFinding> findings,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a manual remediation guide for a non-automatable finding.
    /// </summary>
    /// <param name="finding">Finding to generate guide for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Comprehensive manual guide</returns>
    Task<ManualRemediationGuide> GenerateManualRemediationGuideAsync(
        ComplianceFinding finding,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all active remediation workflows (pending, in-progress, recent).
    /// </summary>
    /// <param name="subscriptionId">Optional subscription filter</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Workflow status snapshot</returns>
    Task<RemediationWorkflowStatus> GetActiveRemediationWorkflowsAsync(
        string? subscriptionId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Processes an approval or rejection for a pending remediation.
    /// </summary>
    /// <param name="executionId">Execution to approve/reject</param>
    /// <param name="approve">Whether to approve (true) or reject (false)</param>
    /// <param name="approverName">Identity of the approver</param>
    /// <param name="comments">Optional comments</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Approval result</returns>
    Task<RemediationApprovalResult> ProcessRemediationApprovalAsync(
        string executionId,
        bool approve,
        string approverName,
        string? comments = null,
        CancellationToken ct = default);

    /// <summary>
    /// Schedules a remediation for future execution.
    /// </summary>
    /// <param name="findingIds">Findings to schedule</param>
    /// <param name="scheduledTime">When to execute</param>
    /// <param name="options">Batch options to use at execution time</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Schedule result</returns>
    Task<RemediationScheduleResult> ScheduleRemediationAsync(
        IEnumerable<string> findingIds,
        DateTime scheduledTime,
        BatchRemediationOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a remediation script using AI or deterministic fallback.
    /// </summary>
    /// <param name="finding">Finding to generate script for</param>
    /// <param name="scriptType">Target script language</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Generated remediation script</returns>
    Task<RemediationScript> GenerateRemediationScriptAsync(
        ComplianceFinding finding,
        ScriptType scriptType = ScriptType.AzureCli,
        CancellationToken ct = default);

    /// <summary>
    /// Gets AI-enhanced remediation guidance for a finding.
    /// </summary>
    /// <param name="finding">Finding to get guidance for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>AI-enhanced guidance with confidence score</returns>
    Task<RemediationGuidance> GetRemediationGuidanceAsync(
        ComplianceFinding finding,
        CancellationToken ct = default);

    /// <summary>
    /// Prioritizes findings using AI with business context.
    /// </summary>
    /// <param name="findings">Findings to prioritize</param>
    /// <param name="businessContext">Optional business context string</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Prioritized findings with justifications</returns>
    Task<List<PrioritizedFinding>> PrioritizeFindingsWithAiAsync(
        IEnumerable<ComplianceFinding> findings,
        string? businessContext = null,
        CancellationToken ct = default);
}

/// <summary>
/// NIST 800-53 Rev 5 controls catalog service.
/// Provides access to the full OSCAL catalog with caching, resilience,
/// and offline fallback capabilities.
/// </summary>
public interface INistControlsService
{
    /// <summary>
    /// Returns the full NIST catalog from cache, fetching on cache miss.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The catalog, or null if unavailable.</returns>
    Task<NistCatalog?> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single control by ID (case-insensitive).
    /// Supports base controls ("AC-2") and enhancements ("AC-2(1)", "ac-2.1").
    /// </summary>
    /// <param name="controlId">The control identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching control, or null if not found.</returns>
    Task<NistControl?> GetControlAsync(
        string controlId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all controls in a given family (case-insensitive prefix match).
    /// </summary>
    /// <param name="familyId">The 2-letter family prefix (e.g., "AC", "SC").</param>
    /// <param name="includeControls">If false, returns summary-only (no nested enhancements).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of controls in the family, or empty list.</returns>
    Task<List<NistControl>> GetControlFamilyAsync(
        string familyId,
        bool includeControls = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Full-text search across control IDs, titles, and statement/guidance prose.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <param name="controlFamily">Optional family filter.</param>
    /// <param name="impactLevel">Optional impact level filter.</param>
    /// <param name="maxResults">Maximum results to return (default: 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching controls, or empty list.</returns>
    Task<List<NistControl>> SearchControlsAsync(
        string query,
        string? controlFamily = null,
        string? impactLevel = null,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the catalog version string from metadata.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Version string (e.g., "5.2.0"), or "Unknown" if unavailable.</returns>
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts the statement, guidance, and assessment objectives for a control.
    /// </summary>
    /// <param name="controlId">The control identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enriched enhancement view, or null if control not found.</returns>
    Task<ControlEnhancement?> GetControlEnhancementAsync(
        string controlId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates whether a control ID exists in the loaded catalog.
    /// </summary>
    /// <param name="controlId">The control identifier to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the control exists, false otherwise.</returns>
    Task<bool> ValidateControlIdAsync(
        string controlId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all NIST controls from the cached catalog.
    /// Used for bulk operations such as syncing to a relational database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All controls, or an empty list if the catalog is not loaded.</returns>
    Task<List<NistControl>> GetAllControlsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Azure Policy compliance integration 
/// </summary>
public interface IAzurePolicyComplianceService
{
    Task<string> GetComplianceSummaryAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    Task<string> GetPolicyStatesAsync(
        string subscriptionId,
        string? policyDefinitionId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Microsoft Defender for Cloud integration
/// </summary>
public interface IDefenderForCloudService
{
    Task<string> GetSecureScoreAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    Task<string> GetAssessmentsAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    Task<string> GetRecommendationsAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Evidence storage and collection service
/// </summary>
public interface IEvidenceStorageService
{
    Task<ComplianceEvidence> CollectEvidenceAsync(
        string controlId,
        string subscriptionId,
        string? resourceGroup = null,
        CancellationToken cancellationToken = default);

    Task<List<ComplianceEvidence>> GetEvidenceAsync(
        string controlId,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Compliance monitoring for continuous compliance posture tracking
/// </summary>
public interface IComplianceMonitoringService
{
    Task<string> GetStatusAsync(
        string? subscriptionId = null,
        CancellationToken cancellationToken = default);

    Task<string> TriggerScanAsync(
        string? subscriptionId = null,
        CancellationToken cancellationToken = default);

    Task<string> GetAlertsAsync(
        string? subscriptionId = null,
        int days = 30,
        CancellationToken cancellationToken = default);

    Task<string> GetTrendAsync(
        string? subscriptionId = null,
        int days = 30,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Compliance document generation service
/// </summary>
public interface IDocumentGenerationService
{
    Task<ComplianceDocument> GenerateDocumentAsync(
        string documentType,
        string? subscriptionId = null,
        string? framework = null,
        string? systemName = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Assessment audit trail service
/// </summary>
public interface IAssessmentAuditService
{
    Task<string> GetAuditLogAsync(
        string? subscriptionId = null,
        int days = 7,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Compliance history and trending service
/// </summary>
public interface IComplianceHistoryService
{
    Task<string> GetHistoryAsync(
        string? subscriptionId = null,
        int days = 30,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Real-time compliance status summary service
/// </summary>
public interface IComplianceStatusService
{
    Task<string> GetStatusAsync(
        string? subscriptionId = null,
        string? framework = null,
        CancellationToken cancellationToken = default);
}

// ──────────────────────────── Compliance Engine Interfaces ───────────────────────────────────────

/// <summary>
/// Scanner strategy interface for family-specific compliance checks.
/// Each scanner inspects Azure resources for its family's NIST controls.
/// </summary>
public interface IComplianceScanner
{
    /// <summary>Two-letter NIST family code this scanner handles (e.g., "AC").</summary>
    string FamilyCode { get; }

    /// <summary>
    /// Execute a family-specific compliance scan.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="resourceGroup">Optional resource group constraint.</param>
    /// <param name="controls">NIST controls for this family.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Per-family assessment result with findings.</returns>
    Task<ControlFamilyAssessment> ScanAsync(
        string subscriptionId,
        string? resourceGroup,
        IEnumerable<NistControl> controls,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Scanner dispatch registry. Returns the specialized scanner for a family,
/// falling back to <c>DefaultComplianceScanner</c> for unregistered families.
/// </summary>
public interface IScannerRegistry
{
    /// <summary>
    /// Get the scanner for a specific control family.
    /// </summary>
    /// <param name="familyCode">Two-letter NIST family code.</param>
    /// <returns>Specialized scanner if registered; default scanner otherwise.</returns>
    IComplianceScanner GetScanner(string familyCode);
}

/// <summary>
/// Evidence collection strategy interface for gathering compliance artifacts.
/// Each collector gathers 5 evidence types for its family.
/// </summary>
public interface IEvidenceCollector
{
    /// <summary>Two-letter NIST family code this collector handles (e.g., "AC").</summary>
    string FamilyCode { get; }

    /// <summary>
    /// Collect evidence artifacts for this family.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="resourceGroup">Optional resource group constraint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evidence package with items and completeness score.</returns>
    Task<EvidencePackage> CollectAsync(
        string subscriptionId,
        string? resourceGroup,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Evidence collector dispatch registry. Returns the specialized collector for a family,
/// falling back to <c>DefaultEvidenceCollector</c> for unregistered families.
/// </summary>
public interface IEvidenceCollectorRegistry
{
    /// <summary>
    /// Get the evidence collector for a specific control family.
    /// </summary>
    /// <param name="familyCode">Two-letter NIST family code.</param>
    /// <returns>Specialized collector if registered; default collector otherwise.</returns>
    IEvidenceCollector GetCollector(string familyCode);
}

/// <summary>
/// ARM SDK wrapper for Azure resource queries with per-subscription+type caching
/// (5-minute TTL), pre-warming, and safety limits.
/// </summary>
public interface IAzureResourceService
{
    /// <summary>
    /// Enumerate Azure resources with optional filters.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="resourceGroup">Optional resource group constraint.</param>
    /// <param name="resourceType">Optional resource type filter (e.g., "Microsoft.Storage/storageAccounts").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Read-only list of generic ARM resources.</returns>
    Task<IReadOnlyList<Azure.ResourceManager.Resources.GenericResource>> GetResourcesAsync(
        string subscriptionId,
        string? resourceGroup = null,
        string? resourceType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get RBAC role assignments for a subscription.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Read-only list of role assignment resources.</returns>
    Task<IReadOnlyList<Azure.ResourceManager.Authorization.RoleAssignmentResource>> GetRoleAssignmentsAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pre-warm resource cache for a subscription (common resource types).
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PreWarmCacheAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get diagnostic settings for a resource.
    /// </summary>
    /// <param name="resourceId">Full Azure resource ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Read-only list of diagnostic settings.</returns>
    Task<IReadOnlyList<Azure.ResourceManager.Monitor.DiagnosticSettingResource>> GetDiagnosticSettingsAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get resource locks for a subscription or resource group.
    /// </summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="resourceGroup">Optional resource group constraint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Read-only list of management lock resources.</returns>
    Task<IReadOnlyList<Azure.ResourceManager.Resources.ManagementLockResource>> GetResourceLocksAsync(
        string subscriptionId,
        string? resourceGroup = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Database persistence abstraction for assessments and findings.
/// Separates EF Core concerns from the engine.
/// </summary>
public interface IAssessmentPersistenceService
{
    /// <summary>Save or update an assessment (upsert semantics).</summary>
    /// <param name="assessment">Assessment to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAssessmentAsync(
        ComplianceAssessment assessment,
        CancellationToken cancellationToken = default);

    /// <summary>Get a single assessment by ID.</summary>
    /// <param name="assessmentId">Assessment identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Assessment if found; null otherwise.</returns>
    Task<ComplianceAssessment?> GetAssessmentAsync(
        string assessmentId,
        CancellationToken cancellationToken = default);

    /// <summary>Get the most recent assessment for a subscription.</summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Latest assessment if found; null otherwise.</returns>
    Task<ComplianceAssessment?> GetLatestAssessmentAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>Get assessment history for a subscription.</summary>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="days">Number of days to look back.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Assessments ordered by date descending.</returns>
    Task<List<ComplianceAssessment>> GetAssessmentHistoryAsync(
        string subscriptionId,
        int days,
        CancellationToken cancellationToken = default);

    /// <summary>Get a single finding by ID.</summary>
    /// <param name="findingId">Finding identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Finding if found; null otherwise.</returns>
    Task<ComplianceFinding?> GetFindingAsync(
        string findingId,
        CancellationToken cancellationToken = default);

    /// <summary>Update finding status.</summary>
    /// <param name="findingId">Finding identifier.</param>
    /// <param name="status">New finding status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if finding was found and updated.</returns>
    Task<bool> UpdateFindingStatusAsync(
        string findingId,
        FindingStatus status,
        CancellationToken cancellationToken = default);
}

// ─── Knowledge Base Interfaces (Feature 010 — expanded) ─────────────────────

/// <summary>
/// STIG validation service. Validates family controls against STIG rules
/// and produces additional findings.
/// </summary>
public interface IStigValidationService
{
    /// <summary>
    /// Validate controls against STIG rules for a family and subscription.
    /// </summary>
    /// <param name="familyCode">Two-letter NIST family code.</param>
    /// <param name="controls">NIST controls to validate.</param>
    /// <param name="subscriptionId">Azure subscription ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>STIG-based findings.</returns>
    Task<List<ComplianceFinding>> ValidateAsync(
        string familyCode,
        IEnumerable<NistControl> controls,
        string subscriptionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// RMF (Risk Management Framework) knowledge service.
/// Provides RMF process data, step details, and service-specific guidance.
/// </summary>
public interface IRmfKnowledgeService
{
    /// <summary>
    /// Get RMF guidance for a specific control (backward-compatible).
    /// </summary>
    Task<string> GetGuidanceAsync(
        string controlId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the full RMF process data including all 6 steps, service guidance, and deliverables.
    /// </summary>
    Task<RmfProcessData?> GetRmfProcessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get details for a specific RMF step (1-6).
    /// </summary>
    /// <param name="step">Step number (1-6).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The RMF step, or null if step is invalid.</returns>
    Task<RmfStep?> GetRmfStepAsync(int step, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get service/branch-specific guidance by topic (e.g., "navy", "army").
    /// </summary>
    /// <param name="topic">Topic or organization name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Service guidance, or null if not found.</returns>
    Task<ServiceGuidance?> GetServiceGuidanceAsync(string topic, CancellationToken cancellationToken = default);
}

/// <summary>
/// STIG knowledge service. Provides STIG control lookups, searches, and cross-references.
/// </summary>
public interface IStigKnowledgeService
{
    /// <summary>
    /// Get STIG rule mapping for a NIST control (backward-compatible).
    /// </summary>
    Task<string> GetStigMappingAsync(
        string controlId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific STIG control by its STIG ID.
    /// </summary>
    /// <param name="stigId">STIG identifier (e.g., "V-12345").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The STIG control, or null if not found.</returns>
    Task<StigControl?> GetStigControlAsync(string stigId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search STIG controls by keyword and optional severity filter.
    /// </summary>
    /// <param name="query">Search query text.</param>
    /// <param name="severity">Optional severity filter.</param>
    /// <param name="maxResults">Maximum results to return (default: 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching STIG controls.</returns>
    Task<List<StigControl>> SearchStigsAsync(
        string query,
        StigSeverity? severity = null,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cross-reference data for a STIG, including related NIST controls and DoD instructions.
    /// </summary>
    /// <param name="stigId">STIG identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cross-reference data, or null if STIG not found.</returns>
    Task<StigCrossReference?> GetStigCrossReferenceAsync(string stigId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get STIG controls mapped to a NIST control via CCI chain.
    /// Resolves: NIST control → CCI IDs → STIG rules (with CCI references).
    /// </summary>
    /// <param name="controlId">NIST 800-53 control ID (e.g., "AC-2").</param>
    /// <param name="severity">Optional severity filter (High/Medium/Low).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching STIG controls with their CCI chain data.</returns>
    Task<List<StigControl>> GetStigsByCciChainAsync(
        string controlId,
        StigSeverity? severity = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get CCI mappings for a NIST control ID.
    /// </summary>
    /// <param name="controlId">NIST 800-53 control ID (e.g., "AC-2").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of CCI mappings for the control.</returns>
    Task<List<CciMapping>> GetCciMappingsAsync(
        string controlId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a STIG control by its Rule ID (e.g., "SV-254239r849090_rule").
    /// Used by scan import for XCCDF rule-result resolution and CKL fallback matching.
    /// </summary>
    /// <param name="ruleId">STIG Rule ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching STIG control, or null if not found.</returns>
    Task<StigControl?> GetStigControlByRuleIdAsync(string ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all STIG controls belonging to a specific benchmark.
    /// Used by CKL export to enumerate the full STIG checklist.
    /// </summary>
    /// <param name="benchmarkId">Benchmark identifier (e.g., "Windows_Server_2022_STIG").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of STIG controls for the benchmark (empty if none found).</returns>
    Task<List<StigControl>> GetStigControlsByBenchmarkAsync(string benchmarkId, CancellationToken cancellationToken = default);
}

/// <summary>
/// DoD instruction service. Provides DoD-specific instruction lookups and control mappings.
/// </summary>
public interface IDoDInstructionService
{
    /// <summary>
    /// Get DoD instruction for a specific control (backward-compatible).
    /// </summary>
    Task<string> GetInstructionAsync(
        string controlId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detailed DoD instruction by instruction ID.
    /// </summary>
    /// <param name="instructionId">Instruction ID (e.g., "DoDI 8510.01").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The instruction, or null if not found.</returns>
    Task<DoDInstruction?> ExplainInstructionAsync(string instructionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all DoD instructions related to a specific NIST control.
    /// </summary>
    /// <param name="controlId">NIST control ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching DoD instructions.</returns>
    Task<List<DoDInstruction>> GetInstructionsByControlAsync(string controlId, CancellationToken cancellationToken = default);
}

/// <summary>
/// DoD workflow service. Provides DoD authorization workflow data.
/// </summary>
public interface IDoDWorkflowService
{
    /// <summary>
    /// Get workflow steps for an assessment type (backward-compatible).
    /// </summary>
    Task<List<string>> GetWorkflowAsync(
        string assessmentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detailed workflow by workflow ID.
    /// </summary>
    /// <param name="workflowId">Workflow identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The workflow, or null if not found.</returns>
    Task<DoDWorkflow?> GetWorkflowDetailAsync(string workflowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all workflows for a specific organization.
    /// </summary>
    /// <param name="organization">Organization name (e.g., "Navy").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching workflows.</returns>
    Task<List<DoDWorkflow>> GetWorkflowsByOrganizationAsync(string organization, CancellationToken cancellationToken = default);
}

/// <summary>
/// Impact level service. Provides DoD Impact Level (IL2-IL6) and FedRAMP baseline information.
/// </summary>
public interface IImpactLevelService
{
    /// <summary>
    /// Get a specific impact level by normalized ID.
    /// </summary>
    /// <param name="level">Impact level (e.g., "IL5", "FedRAMP-High").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The impact level, or null if not found.</returns>
    Task<ImpactLevel?> GetImpactLevelAsync(string level, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all available impact levels (IL2-IL6).
    /// </summary>
    Task<List<ImpactLevel>> GetAllImpactLevelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific FedRAMP baseline by normalized name.
    /// </summary>
    /// <param name="baseline">Baseline name (e.g., "Low", "Moderate", "High").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The FedRAMP baseline as ImpactLevel, or null if not found.</returns>
    Task<ImpactLevel?> GetFedRampBaselineAsync(string baseline, CancellationToken cancellationToken = default);
}

/// <summary>
/// FedRAMP template service. Provides authorization package template guidance.
/// </summary>
public interface IFedRampTemplateService
{
    /// <summary>
    /// Get template guidance for a specific template type.
    /// </summary>
    /// <param name="templateType">Template type (e.g., "SSP", "POAM", "CRM").</param>
    /// <param name="baseline">FedRAMP baseline filter (default: "High").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Template guidance, or null if not found.</returns>
    Task<FedRampTemplate?> GetTemplateGuidanceAsync(
        string templateType,
        string baseline = "High",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all available FedRAMP templates.
    /// </summary>
    Task<List<FedRampTemplate>> GetAllTemplatesAsync(CancellationToken cancellationToken = default);
}

// ──────────────────────────────── Compliance Watch Interfaces ────────────────────────────────────

/// <summary>
/// Alert lifecycle manager — CRUD, state machine transitions, ID generation, role-based access.
/// </summary>
public interface IAlertManager
{
    /// <summary>Create a new compliance alert with auto-generated alert ID.</summary>
    Task<ComplianceAlert> CreateAlertAsync(
        ComplianceAlert alert,
        CancellationToken cancellationToken = default);

    /// <summary>Transition an alert to a new status with role-based validation.</summary>
    Task<ComplianceAlert> TransitionAlertAsync(
        Guid alertId,
        AlertStatus newStatus,
        string userId,
        string userRole,
        string? justification = null,
        CancellationToken cancellationToken = default);

    /// <summary>Get a single alert by internal ID.</summary>
    Task<ComplianceAlert?> GetAlertAsync(
        Guid alertId,
        CancellationToken cancellationToken = default);

    /// <summary>Get a single alert by human-readable alert ID.</summary>
    Task<ComplianceAlert?> GetAlertByAlertIdAsync(
        string alertId,
        CancellationToken cancellationToken = default);

    /// <summary>Get paginated list of alerts with optional filtering.</summary>
    Task<(List<ComplianceAlert> Alerts, int TotalCount)> GetAlertsAsync(
        string? subscriptionId = null,
        AlertSeverity? severity = null,
        AlertStatus? status = null,
        string? controlFamily = null,
        int? days = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>Generate a new sequential alert ID (ALT-YYYYMMDDNNNNN).</summary>
    Task<string> GenerateAlertIdAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Dismiss an alert with required justification (Compliance Officer only).</summary>
    Task<ComplianceAlert> DismissAlertAsync(
        Guid alertId,
        string justification,
        string userId,
        string userRole,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Compliance Watch monitoring service — manages monitoring configurations, baselines,
/// drift detection, and scheduled compliance checks.
/// </summary>
public interface IComplianceWatchService
{
    /// <summary>Enable monitoring for a subscription or resource group scope.</summary>
    Task<MonitoringConfiguration> EnableMonitoringAsync(
        string subscriptionId,
        string? resourceGroupName = null,
        MonitoringFrequency frequency = MonitoringFrequency.Hourly,
        MonitoringMode mode = MonitoringMode.Scheduled,
        string createdBy = "system",
        CancellationToken cancellationToken = default);

    /// <summary>Disable monitoring for a scope.</summary>
    Task<MonitoringConfiguration> DisableMonitoringAsync(
        string subscriptionId,
        string? resourceGroupName = null,
        CancellationToken cancellationToken = default);

    /// <summary>Update monitoring configuration (frequency, mode).</summary>
    Task<MonitoringConfiguration> ConfigureMonitoringAsync(
        string subscriptionId,
        string? resourceGroupName = null,
        MonitoringFrequency? frequency = null,
        MonitoringMode? mode = null,
        CancellationToken cancellationToken = default);

    /// <summary>Get monitoring status for all or a specific subscription.</summary>
    Task<List<MonitoringConfiguration>> GetMonitoringStatusAsync(
        string? subscriptionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Capture a compliance baseline for all resources in a scope after assessment.</summary>
    Task<List<ComplianceBaseline>> CaptureBaselineAsync(
        string subscriptionId,
        string? resourceGroupName = null,
        Guid? assessmentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Run a monitoring check for a specific configuration.</summary>
    Task<int> RunMonitoringCheckAsync(
        MonitoringConfiguration config,
        CancellationToken cancellationToken = default);

    /// <summary>Detect drift from baselines for resources in a scope.</summary>
    Task<List<ComplianceAlert>> DetectDriftAsync(
        string subscriptionId,
        string? resourceGroupName = null,
        CancellationToken cancellationToken = default);

    /// <summary>Create a custom alert rule.</summary>
    Task<AlertRule> CreateAlertRuleAsync(AlertRule rule, CancellationToken cancellationToken = default);

    /// <summary>List alert rules, optionally filtered by subscription.</summary>
    Task<List<AlertRule>> GetAlertRulesAsync(string? subscriptionId = null, CancellationToken cancellationToken = default);

    /// <summary>Create a suppression rule.</summary>
    Task<SuppressionRule> CreateSuppressionAsync(SuppressionRule rule, CancellationToken cancellationToken = default);

    /// <summary>List active suppression rules, optionally filtered by subscription.</summary>
    Task<List<SuppressionRule>> GetSuppressionsAsync(string? subscriptionId = null, CancellationToken cancellationToken = default);

    /// <summary>Configure quiet hours on an existing suppression rule or create a global quiet-hours suppression.</summary>
    Task<SuppressionRule> ConfigureQuietHoursAsync(
        string subscriptionId,
        TimeOnly start,
        TimeOnly end,
        string createdBy = "system",
        CancellationToken cancellationToken = default);

    /// <summary>Check whether an alert should be suppressed based on active rules.</summary>
    bool IsAlertSuppressed(ComplianceAlert alert, IReadOnlyList<SuppressionRule> activeSuppressions);

    /// <summary>Match alert rules against an alert and return the best-matching rule (if any).</summary>
    AlertRule? MatchAlertRule(ComplianceAlert alert, IReadOnlyList<AlertRule> rules);

    /// <summary>Seed default alert rules for a subscription on first enable.</summary>
    Task SeedDefaultRulesAsync(string subscriptionId, string createdBy = "system", CancellationToken cancellationToken = default);

    // ─── Auto-Remediation (US9) ─────────────────────────────────────────────

    /// <summary>Create an auto-remediation rule (validates blocked families AC/IA/SC).</summary>
    Task<AutoRemediationRule> CreateAutoRemediationRuleAsync(AutoRemediationRule rule, CancellationToken cancellationToken = default);

    /// <summary>List auto-remediation rules, optionally filtered by subscription.</summary>
    Task<List<AutoRemediationRule>> GetAutoRemediationRulesAsync(string? subscriptionId = null, bool? isEnabled = null, CancellationToken cancellationToken = default);

    /// <summary>Attempt auto-remediation for an alert using matching rules.</summary>
    Task<AutoRemediationResult> TryAutoRemediateAsync(ComplianceAlert alert, CancellationToken cancellationToken = default);
}

// ─── Notification & Escalation Interfaces (US4) ─────────────────────────────

/// <summary>
/// Service for sending alert notifications across multiple channels with rate limiting.
/// </summary>
public interface IAlertNotificationService
{
    /// <summary>Send a notification for an alert through the appropriate channels.</summary>
    Task SendNotificationAsync(ComplianceAlert alert, CancellationToken cancellationToken = default);

    /// <summary>Send a daily digest of lower-severity alerts.</summary>
    Task SendDigestAsync(string subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Get the audit trail of notifications sent for a specific alert.</summary>
    Task<List<AlertNotification>> GetNotificationsForAlertAsync(Guid alertId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for managing automatic escalation paths and detecting SLA violations.
/// </summary>
public interface IEscalationService
{
    /// <summary>Check for alerts that have exceeded their SLA and trigger escalation.</summary>
    Task CheckEscalationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Create or update an escalation path configuration.</summary>
    Task<EscalationPath> ConfigureEscalationPathAsync(EscalationPath path, CancellationToken cancellationToken = default);

    /// <summary>Get configured escalation paths, optionally filtered by severity.</summary>
    Task<List<EscalationPath>> GetEscalationPathsAsync(AlertSeverity? severity = null, CancellationToken cancellationToken = default);
}

// ─── Event-Driven Monitoring Interfaces (US5) ───────────────────────────────

/// <summary>
/// Represents a compliance-relevant platform event detected from Azure Activity Log or similar source.
/// </summary>
public class ComplianceEvent
{
    /// <summary>Unique event identifier.</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>Type of event (e.g., ResourceWrite, ResourceDelete, PolicyAssignmentChange, RoleAssignmentChange).</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Azure resource ID affected by this event.</summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>Identity of the actor who caused the event.</summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the event occurred.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Azure subscription ID where the event occurred.</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Resource group name (extracted from resource ID).</summary>
    public string? ResourceGroupName { get; set; }

    /// <summary>Operation name from the activity log (e.g., "Microsoft.Storage/storageAccounts/write").</summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>Additional event properties (JSON).</summary>
    public string? Properties { get; set; }
}

/// <summary>
/// Source for compliance-relevant platform events. Polls Azure Activity Log
/// or other event sources for resource changes, policy updates, and role modifications.
/// </summary>
public interface IComplianceEventSource
{
    /// <summary>
    /// Get recent compliance-relevant events since the specified timestamp.
    /// Returns events filtered for write/delete/policy/role operations.
    /// </summary>
    Task<List<ComplianceEvent>> GetRecentEventsAsync(
        string subscriptionId,
        DateTimeOffset since,
        string? resourceGroupName = null,
        CancellationToken cancellationToken = default);
}

// ─── Alert Correlation & Noise Reduction Interfaces (US6) ───────────────────

/// <summary>
/// Tracks a sliding correlation window for grouping related alerts.
/// </summary>
public class CorrelationWindow
{
    /// <summary>Correlation key (e.g., "resource:{resourceId}").</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Parent alert that groups correlated alerts.</summary>
    public ComplianceAlert ParentAlert { get; set; } = null!;

    /// <summary>Individual alert IDs grouped under this window.</summary>
    public List<Guid> ChildAlertIds { get; set; } = new();

    /// <summary>Timestamp when the window was first opened.</summary>
    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>Timestamp of the most recent match (resets expiry).</summary>
    public DateTimeOffset LastMatchAt { get; set; }

    /// <summary>Number of alerts correlated in this window.</summary>
    public int Count => ChildAlertIds.Count;
}

/// <summary>
/// Result of an alert correlation attempt.
/// </summary>
public class CorrelationResult
{
    /// <summary>True if the alert was merged into an existing correlation group.</summary>
    public bool WasMerged { get; set; }

    /// <summary>The parent (grouped) alert — existing if merged, new if first in window.</summary>
    public ComplianceAlert Alert { get; set; } = null!;

    /// <summary>Correlation key used.</summary>
    public string CorrelationKey { get; set; } = string.Empty;
}

/// <summary>
/// Alert correlation service for grouping related alerts, anomaly detection,
/// and alert storm mitigation. Uses sliding time windows per correlation key.
/// </summary>
public interface IAlertCorrelationService
{
    /// <summary>
    /// Attempt to correlate an alert with existing alerts in active windows.
    /// Returns grouped alert if merged, or the original alert if no correlation found.
    /// </summary>
    Task<CorrelationResult> CorrelateAlertAsync(
        ComplianceAlert alert,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the active correlation window for a specific key, if any.
    /// </summary>
    Task<CorrelationWindow?> GetCorrelationWindowAsync(
        string correlationKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalize all expired correlation windows (older than the sliding window duration).
    /// Called periodically to clean up stale windows.
    /// </summary>
    Task<int> FinalizeExpiredWindowsAsync(
        CancellationToken cancellationToken = default);
}

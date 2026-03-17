using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Core compliance scanning engine that orchestrates resource (via Resource Graph/Policy),
/// policy (via Azure Policy Insights), and Defender for Cloud scans.
/// Merges and correlates findings, computes compliance scores, and persists
/// assessments via EF Core.
/// </summary>
public class AtoComplianceEngine : IAtoComplianceEngine
{
    private readonly INistControlsService _nistService;
    private readonly IAzurePolicyComplianceService _policyService;
    private readonly IDefenderForCloudService _defenderService;
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly ILogger<AtoComplianceEngine> _logger;
    private readonly IScannerRegistry _scannerRegistry;
    private readonly IAssessmentPersistenceService _persistenceService;
    private readonly IAzureResourceService _azureResourceService;
    private readonly IStigValidationService _stigValidationService;
    private readonly IEvidenceCollectorRegistry _evidenceCollectorRegistry;
    private readonly IServiceProvider _serviceProvider;

    // Lazy-resolved to break circular dependency:
    // AtoComplianceEngine → IComplianceWatchService → ComplianceWatchService → IAtoComplianceEngine
    private IComplianceWatchService? _complianceWatchService;
    private IAlertManager? _alertManager;
    private bool _optionalServicesResolved;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="AtoComplianceEngine"/> class.
    /// </summary>
    public AtoComplianceEngine(
        INistControlsService nistService,
        IAzurePolicyComplianceService policyService,
        IDefenderForCloudService defenderService,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILogger<AtoComplianceEngine> logger,
        IScannerRegistry scannerRegistry,
        IAssessmentPersistenceService persistenceService,
        IAzureResourceService azureResourceService,
        IStigValidationService stigValidationService,
        IEvidenceCollectorRegistry evidenceCollectorRegistry,
        IServiceProvider serviceProvider)
    {
        _nistService = nistService;
        _policyService = policyService;
        _defenderService = defenderService;
        _dbFactory = dbFactory;
        _logger = logger;
        _scannerRegistry = scannerRegistry;
        _persistenceService = persistenceService;
        _azureResourceService = azureResourceService;
        _stigValidationService = stigValidationService;
        _evidenceCollectorRegistry = evidenceCollectorRegistry;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Lazily resolves optional services that would cause a circular dependency
    /// if injected via the constructor (IComplianceWatchService ↔ IAtoComplianceEngine).
    /// </summary>
    private void EnsureOptionalServicesResolved()
    {
        if (_optionalServicesResolved) return;
        _complianceWatchService = _serviceProvider.GetService<IComplianceWatchService>();
        _alertManager = _serviceProvider.GetService<IAlertManager>();
        _optionalServicesResolved = true;
    }

    /// <inheritdoc />
    public async Task<ComplianceAssessment> RunAssessmentAsync(
        string subscriptionId,
        string? framework = null,
        string? controlFamilies = null,
        string? resourceTypes = null,
        string? scanType = null,
        bool includePassed = false,
        CancellationToken cancellationToken = default)
    {
        var assessment = new ComplianceAssessment
        {
            SubscriptionId = subscriptionId,
            Framework = ComplianceFrameworks.Normalize(framework ?? "NIST800-53") ?? "NIST80053",
            ScanType = NormalizeScanType(scanType),
            Status = AssessmentStatus.InProgress,
            InitiatedBy = "system",
            AssessedAt = DateTime.UtcNow 
        };

        _logger.LogInformation(
            "Starting compliance assessment {Id} | Sub: {Sub} | Framework: {Fw} | Type: {Type}",
            assessment.Id, subscriptionId, assessment.Framework, assessment.ScanType);

        try
        {
            // Parse control family filter
            var familyFilter = ParseControlFamilies(controlFamilies);

            // Run scans based on scan type
            assessment.ProgressMessage = "Running scans...";

            var policySummary = new ScanSummary();
            var resourceSummary = new ScanSummary();

            if (assessment.ScanType is "policy" or "combined")
            {
                assessment.ProgressMessage = "Running policy compliance scan...";
                var policyFindings = await RunPolicyScanAsync(
                    subscriptionId, familyFilter, policySummary, cancellationToken);
                assessment.Findings.AddRange(policyFindings);
            }

            if (assessment.ScanType is "resource" or "combined")
            {
                assessment.ProgressMessage = "Running Defender for Cloud scan...";
                var defenderFindings = await RunDefenderScanAsync(
                    subscriptionId, familyFilter, resourceSummary, cancellationToken);
                assessment.Findings.AddRange(defenderFindings);
            }

            // Correlate findings from multiple sources
            if (assessment.ScanType == "combined")
            {
                assessment.ProgressMessage = "Correlating findings...";
                CorrelateFindings(assessment.Findings);
            }

            // Filter by resource types if specified
            if (!string.IsNullOrWhiteSpace(resourceTypes))
            {
                var types = resourceTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                assessment.Findings = assessment.Findings
                    .Where(f => string.IsNullOrEmpty(f.ResourceType) ||
                                types.Any(t => f.ResourceType.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            // Remove passed findings unless requested
            if (!includePassed)
            {
                assessment.Findings = assessment.Findings
                    .Where(f => f.Status != FindingStatus.Remediated && f.Status != FindingStatus.FalsePositive)
                    .ToList();
            }

            // Set assessment ID and CatSeverity on all findings
            foreach (var finding in assessment.Findings)
            {
                finding.AssessmentId = assessment.Id;
                finding.CatSeverity ??= MapToCatSeverity(finding.Severity);
            }

            // Compute compliance score using NIST catalog
            await ComputeComplianceScoreAsync(assessment, familyFilter, cancellationToken);

            // Set scan summaries
            assessment.ResourceScanSummary = resourceSummary;
            assessment.PolicyScanSummary = policySummary;

            // Mark complete
            assessment.Status = AssessmentStatus.Completed;
            assessment.CompletedAt = DateTime.UtcNow;
            assessment.ProgressMessage = "Assessment completed";

            // Persist
            await SaveAssessmentAsync(assessment, cancellationToken);

            _logger.LogInformation(
                "Assessment {Id} completed | Score: {Score:F1}% | Findings: {Count}",
                assessment.Id, assessment.ComplianceScore, assessment.Findings.Count);
        }
        catch (OperationCanceledException)
        {
            assessment.Status = AssessmentStatus.Cancelled;
            assessment.ProgressMessage = "Assessment cancelled";
            _logger.LogWarning("Assessment {Id} was cancelled", assessment.Id);
            throw;
        }
        catch (Exception ex)
        {
            assessment.Status = AssessmentStatus.Failed;
            assessment.ProgressMessage = $"Assessment failed: {ex.Message}";
            _logger.LogError(ex, "Assessment {Id} failed", assessment.Id);

            // Still persist the failed assessment for audit trail
            try { await SaveAssessmentAsync(assessment, cancellationToken); }
            catch (Exception saveEx) { _logger.LogError(saveEx, "Failed to save failed assessment {Id}", assessment.Id); }

            throw;
        }

        return assessment;
    }

    /// <inheritdoc />
    public async Task<List<ComplianceAssessment>> GetAssessmentHistoryAsync(
        string subscriptionId,
        int days = 30,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = DateTime.UtcNow.AddDays(-days);

        return await db.Assessments
            .Where(a => a.SubscriptionId == subscriptionId && a.AssessedAt >= cutoff)
            .OrderByDescending(a => a.AssessedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ComplianceFinding?> GetFindingAsync(
        string findingId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Findings.FindAsync(new object[] { findingId }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAssessmentAsync(
        ComplianceAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.Assessments
            .AsNoTracking()
            .AnyAsync(a => a.Id == assessment.Id, cancellationToken);

        if (existing)
        {
            db.Assessments.Update(assessment);
        }
        else
        {
            db.Assessments.Add(assessment);
        }

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogDebug("Saved assessment {Id}", assessment.Id);
    }

    // ─── Private Methods ────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a policy compliance scan and converts results to ComplianceFindings.
    /// </summary>
    private async Task<List<ComplianceFinding>> RunPolicyScanAsync(
        string subscriptionId,
        HashSet<string>? familyFilter,
        ScanSummary summary,
        CancellationToken cancellationToken)
    {
        var findings = new List<ComplianceFinding>();

        try
        {
            var policyJson = await _policyService.GetPolicyStatesAsync(subscriptionId, null, cancellationToken);
            using var doc = JsonDocument.Parse(policyJson);
            var root = doc.RootElement;

            // Check for error response
            if (root.TryGetProperty("error", out _))
            {
                _logger.LogWarning("Policy scan returned error for {Sub}", subscriptionId);
                return findings;
            }

            if (!root.TryGetProperty("states", out var statesArray))
                return findings;

            int compliant = 0, nonCompliant = 0;

            foreach (var state in statesArray.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var complianceState = state.TryGetProperty("complianceState", out var csElem)
                    ? csElem.GetString() : null;

                // Map policy definition groups to NIST controls
                var groupNames = new List<string>();
                if (state.TryGetProperty("policyDefinitionGroupNames", out var groupsElem))
                {
                    foreach (var g in groupsElem.EnumerateArray())
                    {
                        var name = g.GetString();
                        if (!string.IsNullOrEmpty(name))
                            groupNames.Add(name);
                    }
                }

                var controlIds = AzurePolicyComplianceService.MapGroupsToNistControls(groupNames);

                if (string.Equals(complianceState, "compliant", StringComparison.OrdinalIgnoreCase))
                {
                    compliant++;
                    continue; // Skip compliant — we create findings for non-compliant only
                }

                nonCompliant++;

                // Create a finding for each mapped control
                foreach (var controlId in controlIds)
                {
                    var family = controlId.Split('-')[0].ToUpperInvariant();
                    if (familyFilter != null && !familyFilter.Contains(family))
                        continue;

                    var resourceId = state.TryGetProperty("resourceId", out var ridElem)
                        ? ridElem.GetString() ?? "" : "";
                    var resourceType = state.TryGetProperty("resourceType", out var rtElem)
                        ? rtElem.GetString() ?? "" : "";
                    var policyDefId = state.TryGetProperty("policyDefinitionId", out var pdElem)
                        ? pdElem.GetString() : null;
                    var policyAssignId = state.TryGetProperty("policyAssignmentId", out var paElem)
                        ? paElem.GetString() : null;

                    var policySeverity = ClassifyPolicySeverity(controlId);
                    findings.Add(new ComplianceFinding
                    {
                        ControlId = controlId,
                        ControlFamily = family,
                        Title = $"Policy non-compliance: {controlId}",
                        Description = $"Azure Policy detected non-compliance for control {controlId}",
                        Severity = policySeverity,
                        CatSeverity = MapToCatSeverity(policySeverity),
                        Status = FindingStatus.Open,
                        ResourceId = resourceId,
                        ResourceType = resourceType,
                        RemediationGuidance = $"Review Azure Policy assignment and remediate the non-compliant resource.",
                        Source = "PolicyInsights",
                        ScanSource = ScanSourceType.Policy,
                        PolicyDefinitionId = policyDefId,
                        PolicyAssignmentId = policyAssignId,
                        RemediationType = RemediationType.PolicyRemediation,
                        RiskLevel = IsHighRiskFamily(family) ? RiskLevel.High : RiskLevel.Standard
                    });
                }
            }

            summary.Compliant = compliant;
            summary.NonCompliant = nonCompliant;
            summary.PoliciesEvaluated = compliant + nonCompliant;
            summary.CompliancePercentage = summary.PoliciesEvaluated > 0
                ? Math.Round((double)compliant / summary.PoliciesEvaluated * 100, 2)
                : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Policy scan failed for {Sub}", subscriptionId);
        }

        return findings;
    }

    /// <summary>
    /// Runs a Defender for Cloud scan and converts recommendations to ComplianceFindings.
    /// </summary>
    private async Task<List<ComplianceFinding>> RunDefenderScanAsync(
        string subscriptionId,
        HashSet<string>? familyFilter,
        ScanSummary summary,
        CancellationToken cancellationToken)
    {
        var findings = new List<ComplianceFinding>();

        try
        {
            var recommendationsJson = await _defenderService.GetRecommendationsAsync(subscriptionId, cancellationToken);
            using var doc = JsonDocument.Parse(recommendationsJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out _))
            {
                _logger.LogWarning("Defender scan returned error for {Sub}", subscriptionId);
                return findings;
            }

            if (!root.TryGetProperty("recommendations", out var recsArray))
                return findings;

            int scanned = 0, compliant = 0, nonCompliant = 0;

            foreach (var rec in recsArray.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;

                var displayName = rec.TryGetProperty("displayName", out var dnElem)
                    ? dnElem.GetString() ?? "" : "";
                var status = rec.TryGetProperty("status", out var sElem)
                    ? sElem.GetString() : null;
                var recId = rec.TryGetProperty("id", out var idElem)
                    ? idElem.GetString() : null;

                if (string.Equals(status, "Healthy", StringComparison.OrdinalIgnoreCase))
                {
                    compliant++;
                    continue;
                }

                nonCompliant++;

                // Map recommendation to NIST controls
                var controlIds = DefenderForCloudService.MapRecommendationToNistControls(displayName);
                if (controlIds.Count == 0)
                    controlIds.Add("SI-4"); // Default to Information System Monitoring

                foreach (var controlId in controlIds)
                {
                    var family = controlId.Split('-')[0].ToUpperInvariant();
                    if (familyFilter != null && !familyFilter.Contains(family))
                        continue;

                    var defenderSeverity = ClassifyDefenderSeverity(status);
                    findings.Add(new ComplianceFinding
                    {
                        ControlId = controlId,
                        ControlFamily = family,
                        Title = displayName,
                        Description = $"Defender for Cloud recommends action: {displayName}",
                        Severity = defenderSeverity,
                        CatSeverity = MapToCatSeverity(defenderSeverity),
                        Status = FindingStatus.Open,
                        ResourceId = "",
                        ResourceType = "",
                        RemediationGuidance = $"Follow Defender for Cloud recommendation: {displayName}",
                        Source = "DefenderForCloud",
                        ScanSource = ScanSourceType.Defender,
                        DefenderRecommendationId = recId,
                        RemediationType = RemediationType.ResourceConfiguration,
                        RiskLevel = IsHighRiskFamily(family) ? RiskLevel.High : RiskLevel.Standard
                    });
                }
            }

            summary.ResourcesScanned = scanned;
            summary.Compliant = compliant;
            summary.NonCompliant = nonCompliant;
            summary.CompliancePercentage = scanned > 0
                ? Math.Round((double)compliant / scanned * 100, 2)
                : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Defender scan failed for {Sub}", subscriptionId);
        }

        return findings;
    }

    /// <summary>
    /// Correlates findings from multiple scan sources.
    /// Deduplicates by (controlId + resourceId), keeps the higher-severity finding.
    /// </summary>
    private static void CorrelateFindings(List<ComplianceFinding> findings)
    {
        var grouped = findings
            .GroupBy(f => new { f.ControlId, f.ResourceId })
            .ToList();

        findings.Clear();

        foreach (var group in grouped)
        {
            if (group.Count() == 1)
            {
                findings.Add(group.First());
                continue;
            }

            // Keep the finding with highest severity; mark as Combined source
            var primary = group.OrderBy(f => f.Severity).First(); // Critical=0, High=1, ...
            primary.ScanSource = ScanSourceType.Combined;
            primary.Source = string.Join("+", group.Select(f => f.Source).Distinct());
            findings.Add(primary);
        }
    }

    /// <summary>
    /// Computes overall compliance score using NIST catalog as baseline.
    /// Score = (passed / total) * 100, where controls with no findings count as passed.
    /// </summary>
    private async Task ComputeComplianceScoreAsync(
        ComplianceAssessment assessment,
        HashSet<string>? familyFilter,
        CancellationToken cancellationToken)
    {
        // Get all control families to assess
        var families = familyFilter ?? ControlFamilies.AllFamilies;

        int totalControls = 0, passedControls = 0, failedControls = 0;
        var failedControlIds = assessment.Findings
            .Select(f => f.ControlId.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var family in families)
        {
            try
            {
                var controls = await _nistService.GetControlFamilyAsync(family, false, cancellationToken);
                foreach (var control in controls)
                {
                    totalControls++;
                    if (failedControlIds.Contains(control.Id.ToUpperInvariant()))
                        failedControls++;
                    else
                        passedControls++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get control family {Family} for scoring", family);
            }
        }

        assessment.TotalControls = totalControls;
        assessment.PassedControls = passedControls;
        assessment.FailedControls = failedControls;
        assessment.NotAssessedControls = totalControls - passedControls - failedControls;
        assessment.ComplianceScore = totalControls > 0
            ? Math.Round((double)passedControls / totalControls * 100, 1)
            : 0;
    }

    // ─── Helper Methods ──────────────────────────────────────────────────────────

    /// <summary>Parses a comma-separated list of control families into a normalized hash set.</summary>
    private static HashSet<string>? ParseControlFamilies(string? controlFamilies)
    {
        if (string.IsNullOrWhiteSpace(controlFamilies))
            return null;

        return controlFamilies
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.ToUpperInvariant())
            .Where(f => ControlFamilies.IsValidFamily(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Normalizes the scan type string to a known value (resource, policy, combined).</summary>
    private static string NormalizeScanType(string? scanType) =>
        scanType?.ToLowerInvariant() switch
        {
            "resource" => "resource",
            "policy" => "policy",
            "combined" => "combined",
            "quick" => "combined",
            "full" => "combined",
            _ => "combined"
        };

    /// <summary>Classifies the severity of a policy finding based on its control family.</summary>
    private static FindingSeverity ClassifyPolicySeverity(string controlId)
    {
        var family = controlId.Split('-')[0].ToUpperInvariant();
        return family switch
        {
            "AC" or "IA" or "SC" => FindingSeverity.High,
            "AU" or "SI" or "CM" => FindingSeverity.Medium,
            _ => FindingSeverity.Low
        };
    }

    /// <summary>Classifies the severity of a Defender finding based on its assessment status.</summary>
    private static FindingSeverity ClassifyDefenderSeverity(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "unhealthy" => FindingSeverity.High,
            "notapplicable" => FindingSeverity.Low,
            _ => FindingSeverity.Medium
        };

    /// <summary>Maps FindingSeverity to CatSeverity for STIG-style categorization.</summary>
    private static CatSeverity MapToCatSeverity(FindingSeverity severity) =>
        severity switch
        {
            FindingSeverity.Critical or FindingSeverity.High => CatSeverity.CatI,
            FindingSeverity.Medium => CatSeverity.CatII,
            _ => CatSeverity.CatIII,
        };

    /// <summary>Returns true if the control family is high-risk (AC, IA, SC).</summary>
    private static bool IsHighRiskFamily(string family) =>
        family is "AC" or "IA" or "SC";

    // ──────────────────────────── Feature 008 Implementations ─────────────────

    /// <inheritdoc />
    public async Task<ControlFamilyAssessment> AssessControlFamilyAsync(
        string familyCode,
        string subscriptionId,
        string? resourceGroup = null,
        CancellationToken cancellationToken = default)
    {
        if (!ControlFamilies.IsValidFamily(familyCode))
            throw new ArgumentException($"Invalid control family: {familyCode}", nameof(familyCode));

        _logger.LogInformation("Assessing control family {Family} for Sub={Sub} RG={RG}",
            familyCode, subscriptionId, resourceGroup);

        // 1. Get controls for this family
        List<NistControl> controls;
        try
        {
            controls = await _nistService.GetControlFamilyAsync(familyCode, false, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get controls for family {Family}", familyCode);
            return ControlFamilyAssessment.Failed(familyCode, $"Failed to get controls: {ex.Message}");
        }

        // 2. Dispatch to the appropriate scanner
        var scanner = _scannerRegistry.GetScanner(familyCode);
        var result = await scanner.ScanAsync(subscriptionId, resourceGroup, controls, cancellationToken);

        // 3. Run STIG validation and merge additional findings
        try
        {
            var stigFindings = await _stigValidationService.ValidateAsync(
                familyCode, controls, subscriptionId, cancellationToken);

            if (stigFindings.Count > 0)
            {
                result.Findings.AddRange(stigFindings);

                // Recalculate counts & score
                result.FailedControls = result.Findings
                    .Select(f => f.ControlId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                result.PassedControls = result.TotalControls - result.FailedControls;
                result.ComplianceScore = result.TotalControls > 0
                    ? (double)result.PassedControls / result.TotalControls * 100.0
                    : 100.0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "STIG validation failed for family {Family}, continuing with scanner results", familyCode);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<ComplianceAssessment> RunComprehensiveAssessmentAsync(
        string subscriptionId,
        string? resourceGroup = null,
        IProgress<AssessmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var overallStopwatch = Stopwatch.StartNew();

        var assessment = new ComplianceAssessment
        {
            SubscriptionId = subscriptionId,
            Framework = "NIST80053",
            ScanType = "comprehensive",
            Status = AssessmentStatus.Pending,
            InitiatedBy = "system",
            AssessedAt = DateTime.UtcNow,
            ResourceGroupFilter = resourceGroup,
            SubscriptionIds = new List<string> { subscriptionId }
        };

        _logger.LogInformation(
            "Starting comprehensive assessment {Id} | Sub: {Sub} | RG: {RG}",
            assessment.Id, subscriptionId, resourceGroup ?? "(all)");

        try
        {
            assessment.Status = AssessmentStatus.InProgress;
            assessment.ProgressMessage = "Pre-warming resource cache...";

            // Pre-warm the resource cache for this subscription
            try
            {
                await _azureResourceService.PreWarmCacheAsync(subscriptionId, cancellationToken);
                assessment.ScanPillarResults["ARM"] = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache pre-warm failed for Sub={Sub}", subscriptionId);
                assessment.ScanPillarResults["ARM"] = false;
            }

            // Iterate all 20 NIST families
            var families = ControlFamilies.AllFamilies.ToList();
            var familyScanTimes = new List<double>();
            var progressReport = new AssessmentProgress
            {
                TotalFamilies = families.Count
            };

            foreach (var familyCode in families)
            {
                cancellationToken.ThrowIfCancellationRequested();

                assessment.ProgressMessage = $"Scanning family {familyCode}...";
                progressReport.CurrentFamily = familyCode;
                progress?.Report(progressReport);

                var familyResult = await AssessControlFamilyAsync(
                    familyCode, subscriptionId, resourceGroup, cancellationToken);

                assessment.ControlFamilyResults.Add(familyResult);
                assessment.Findings.AddRange(familyResult.Findings);

                // Track per-family timing for ETA
                familyScanTimes.Add(familyResult.AssessmentDuration.TotalMilliseconds);

                // Update progress
                progressReport.CompletedFamilies++;
                progressReport.FamilyResults.Add(familyCode);
                progressReport.PercentComplete = (double)progressReport.CompletedFamilies / progressReport.TotalFamilies * 100.0;

                // ETA based on average scan time
                var avgMs = familyScanTimes.Average();
                var remainingFamilies = progressReport.TotalFamilies - progressReport.CompletedFamilies;
                progressReport.EstimatedTimeRemaining = TimeSpan.FromMilliseconds(avgMs * remainingFamilies);
                progress?.Report(progressReport);
            }

            // Correlate findings from multiple scanners
            assessment.ProgressMessage = "Correlating findings...";
            CorrelateFindings(assessment.Findings);

            // Set assessment ID and CatSeverity on all findings
            foreach (var finding in assessment.Findings)
            {
                finding.AssessmentId = assessment.Id;
                finding.CatSeverity ??= MapToCatSeverity(finding.Severity);
            }

            // Compute overall compliance scores
            ComputeScoresFromFamilyResults(assessment);

            // Record Policy and Defender pillar status based on family results
            if (!assessment.ScanPillarResults.ContainsKey("Policy"))
                assessment.ScanPillarResults["Policy"] = true;
            if (!assessment.ScanPillarResults.ContainsKey("Defender"))
                assessment.ScanPillarResults["Defender"] = true;

            // Generate executive summary
            assessment.ExecutiveSummary = GenerateExecutiveSummary(assessment);

            // Complete the assessment
            overallStopwatch.Stop();
            assessment.AssessmentDuration = overallStopwatch.Elapsed;
            assessment.Status = AssessmentStatus.Completed;
            assessment.CompletedAt = DateTime.UtcNow;
            assessment.ProgressMessage = "Assessment completed";

            // Persist — non-fatal: return assessment even if DB save fails
            try { await _persistenceService.SaveAssessmentAsync(assessment, cancellationToken); }
            catch (Exception saveEx) when (saveEx is not OperationCanceledException)
            {
                _logger.LogWarning(saveEx, "Failed to persist completed assessment {Id} — returning assessment without persistence", assessment.Id);
            }

            _logger.LogInformation(
                "Comprehensive assessment {Id} completed | Score: {Score:F1}% | Findings: {Count} | Duration: {Duration}ms",
                assessment.Id, assessment.ComplianceScore, assessment.Findings.Count, overallStopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            overallStopwatch.Stop();
            assessment.AssessmentDuration = overallStopwatch.Elapsed;
            assessment.Status = AssessmentStatus.Cancelled;
            assessment.ProgressMessage = "Assessment cancelled";
            _logger.LogWarning("Comprehensive assessment {Id} was cancelled", assessment.Id);
            throw;
        }
        catch (Exception ex)
        {
            overallStopwatch.Stop();
            assessment.AssessmentDuration = overallStopwatch.Elapsed;
            assessment.Status = AssessmentStatus.Failed;
            assessment.ProgressMessage = $"Assessment failed: {ex.Message}";
            _logger.LogError(ex, "Comprehensive assessment {Id} failed", assessment.Id);

            try { await _persistenceService.SaveAssessmentAsync(assessment, cancellationToken); }
            catch (Exception saveEx) { _logger.LogError(saveEx, "Failed to save failed assessment {Id}", assessment.Id); }

            throw;
        }

        return assessment;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ComplianceFinding> StreamAssessmentFindingsAsync(
        string subscriptionId,
        string? resourceGroup = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Streaming assessment findings | Sub: {Sub} | RG: {RG}",
            subscriptionId, resourceGroup ?? "(all)");

        // Pre-warm the resource cache
        try { await _azureResourceService.PreWarmCacheAsync(subscriptionId, cancellationToken); }
        catch (Exception ex) { _logger.LogWarning(ex, "Cache pre-warm failed for Sub={Sub}", subscriptionId); }

        var families = ControlFamilies.AllFamilies.ToList();
        foreach (var familyCode in families)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var familyResult = await AssessControlFamilyAsync(
                familyCode, subscriptionId, resourceGroup, cancellationToken);

            foreach (var finding in familyResult.Findings)
            {
                yield return finding;
            }
        }
    }

    /// <inheritdoc />
    public async Task<ComplianceAssessment> RunEnvironmentAssessmentAsync(
        IEnumerable<string> subscriptionIds,
        string environmentName,
        IProgress<AssessmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var subList = subscriptionIds.ToList();
        if (subList.Count == 0)
            throw new ArgumentException("At least one subscription ID is required.", nameof(subscriptionIds));

        var overallStopwatch = Stopwatch.StartNew();

        var assessment = new ComplianceAssessment
        {
            SubscriptionId = subList[0], // Primary subscription
            Framework = "NIST80053",
            ScanType = "environment",
            Status = AssessmentStatus.Pending,
            InitiatedBy = "system",
            AssessedAt = DateTime.UtcNow,
            EnvironmentName = environmentName,
            SubscriptionIds = subList
        };

        _logger.LogInformation(
            "Starting environment assessment {Id} | Env: {Env} | Subs: {Count}",
            assessment.Id, environmentName, subList.Count);

        try
        {
            assessment.Status = AssessmentStatus.InProgress;
            assessment.ProgressMessage = "Pre-warming resource caches...";

            // Pre-warm caches for all subscriptions
            foreach (var subId in subList)
            {
                try
                {
                    await _azureResourceService.PreWarmCacheAsync(subId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Cache pre-warm failed for Sub={Sub} in environment {Env}",
                        subId, environmentName);
                }
            }
            assessment.ScanPillarResults["ARM"] = true;

            // Iterate all 20 families across all subscriptions
            var families = ControlFamilies.AllFamilies.ToList();
            var totalSteps = families.Count * subList.Count;
            var completedSteps = 0;
            var progressReport = new AssessmentProgress
            {
                TotalFamilies = families.Count
            };

            // Aggregate per-family across all subscriptions
            foreach (var familyCode in families)
            {
                cancellationToken.ThrowIfCancellationRequested();

                assessment.ProgressMessage = $"Scanning family {familyCode} across {subList.Count} subscriptions...";
                progressReport.CurrentFamily = familyCode;
                progress?.Report(progressReport);

                var aggregatedResult = new ControlFamilyAssessment
                {
                    FamilyCode = familyCode,
                    FamilyName = ControlFamilies.FamilyNames.TryGetValue(familyCode, out var name) ? name : familyCode,
                    Status = FamilyAssessmentStatus.Pending,
                    ScannerName = "Environment"
                };

                var familyStopwatch = Stopwatch.StartNew();
                int totalControls = 0, passedControls = 0, failedControls = 0;
                var allFindings = new List<ComplianceFinding>();

                foreach (var subId in subList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var result = await AssessControlFamilyAsync(familyCode, subId, null, cancellationToken);

                    if (totalControls == 0) totalControls = result.TotalControls;
                    allFindings.AddRange(result.Findings);
                    completedSteps++;
                }

                familyStopwatch.Stop();

                aggregatedResult.Findings = allFindings;
                aggregatedResult.TotalControls = totalControls;
                failedControls = allFindings.Select(f => f.ControlId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                aggregatedResult.FailedControls = failedControls;
                aggregatedResult.PassedControls = totalControls - failedControls;
                aggregatedResult.ComplianceScore = totalControls > 0
                    ? (double)aggregatedResult.PassedControls / totalControls * 100.0
                    : 100.0;
                aggregatedResult.AssessmentDuration = familyStopwatch.Elapsed;
                aggregatedResult.Status = FamilyAssessmentStatus.Completed;

                assessment.ControlFamilyResults.Add(aggregatedResult);
                assessment.Findings.AddRange(allFindings);

                progressReport.CompletedFamilies++;
                progressReport.FamilyResults.Add(familyCode);
                progressReport.PercentComplete = (double)progressReport.CompletedFamilies / progressReport.TotalFamilies * 100.0;
                progress?.Report(progressReport);
            }

            // Correlate findings
            assessment.ProgressMessage = "Correlating findings...";
            CorrelateFindings(assessment.Findings);

            foreach (var finding in assessment.Findings)
                finding.AssessmentId = assessment.Id;

            ComputeScoresFromFamilyResults(assessment);
            assessment.ScanPillarResults["Policy"] = true;
            assessment.ScanPillarResults["Defender"] = true;
            assessment.ExecutiveSummary = GenerateExecutiveSummary(assessment);

            overallStopwatch.Stop();
            assessment.AssessmentDuration = overallStopwatch.Elapsed;
            assessment.Status = AssessmentStatus.Completed;
            assessment.CompletedAt = DateTime.UtcNow;
            assessment.ProgressMessage = "Assessment completed";

            try { await _persistenceService.SaveAssessmentAsync(assessment, cancellationToken); }
            catch (Exception saveEx) when (saveEx is not OperationCanceledException)
            {
                _logger.LogWarning(saveEx, "Failed to persist completed environment assessment {Id}", assessment.Id);
            }

            _logger.LogInformation(
                "Environment assessment {Id} completed | Env: {Env} | Score: {Score:F1}% | Findings: {Count}",
                assessment.Id, environmentName, assessment.ComplianceScore, assessment.Findings.Count);
        }
        catch (OperationCanceledException)
        {
            overallStopwatch.Stop();
            assessment.AssessmentDuration = overallStopwatch.Elapsed;
            assessment.Status = AssessmentStatus.Cancelled;
            assessment.ProgressMessage = "Assessment cancelled";
            _logger.LogWarning("Environment assessment {Id} was cancelled", assessment.Id);
            throw;
        }
        catch (Exception ex)
        {
            overallStopwatch.Stop();
            assessment.AssessmentDuration = overallStopwatch.Elapsed;
            assessment.Status = AssessmentStatus.Failed;
            assessment.ProgressMessage = $"Assessment failed: {ex.Message}";
            _logger.LogError(ex, "Environment assessment {Id} failed", assessment.Id);

            try { await _persistenceService.SaveAssessmentAsync(assessment, cancellationToken); }
            catch (Exception saveEx) { _logger.LogError(saveEx, "Failed to save failed assessment {Id}", assessment.Id); }

            throw;
        }

        return assessment;
    }

    /// <inheritdoc />
    public async Task<EvidencePackage> CollectEvidenceAsync(
        string familyCode,
        string subscriptionId,
        string? resourceGroup = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Collecting evidence for family {Family} Sub={Sub}", familyCode, subscriptionId);

        var collector = _evidenceCollectorRegistry.GetCollector(familyCode);
        var package = await collector.CollectAsync(subscriptionId, resourceGroup, cancellationToken);

        _logger.LogInformation(
            "Evidence collection complete for {Family}: {Items} items, {Score:F0}% completeness",
            familyCode, package.EvidenceItems.Count, package.CompletenessScore);

        return package;
    }

    /// <inheritdoc />
    public RiskProfile CalculateRiskProfile(ComplianceAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        var criticalCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.Critical);
        var highCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.High);
        var mediumCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.Medium);
        var lowCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.Low);

        var riskScore = (criticalCount * 10.0) + (highCount * 7.5) + (mediumCount * 5.0) + (lowCount * 2.5);

        var riskLevel = riskScore >= 100 ? ComplianceRiskLevel.Critical
            : riskScore >= 50 ? ComplianceRiskLevel.High
            : riskScore >= 20 ? ComplianceRiskLevel.Medium
            : ComplianceRiskLevel.Low;

        var topRisks = assessment.ControlFamilyResults
            .Where(f => f.Status == FamilyAssessmentStatus.Completed && f.ComplianceScore < 70)
            .OrderBy(f => f.ComplianceScore)
            .Take(5)
            .Select(f => new FamilyRisk
            {
                FamilyCode = f.FamilyCode,
                FamilyName = f.FamilyName,
                ComplianceScore = f.ComplianceScore,
                FindingCount = assessment.Findings.Count(fin => fin.ControlFamily == f.FamilyCode)
            })
            .ToList();

        return new RiskProfile
        {
            RiskScore = riskScore,
            RiskLevel = riskLevel,
            CriticalCount = criticalCount,
            HighCount = highCount,
            MediumCount = mediumCount,
            LowCount = lowCount,
            TopRisks = topRisks
        };
    }

    /// <inheritdoc />
    public async Task<RiskAssessment> PerformRiskAssessmentAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Performing risk assessment for subscription {Sub}", subscriptionId);

        // Get or run assessment
        var assessment = await _persistenceService.GetLatestAssessmentAsync(subscriptionId, cancellationToken);
        if (assessment is null)
        {
            _logger.LogInformation("No existing assessment found, running comprehensive assessment first");
            assessment = await RunComprehensiveAssessmentAsync(subscriptionId, cancellationToken: cancellationToken);
        }

        // Define the 8 risk categories with their contributing families
        var categoryFamilyMap = new Dictionary<string, string[]>
        {
            ["Data Protection"] = new[] { "SC", "MP", "SA" },
            ["Access Control"] = new[] { "AC", "IA" },
            ["Network Security"] = new[] { "SC", "SI" },
            ["Incident Response"] = new[] { "IR", "AU" },
            ["Business Continuity"] = new[] { "CP", "PL" },
            ["Compliance"] = new[] { "CA", "PM", "RA" },
            ["Third-Party Risk"] = new[] { "SR", "SA" },
            ["Configuration Management"] = new[] { "CM", "SI", "MA" }
        };

        var categories = new List<RiskCategory>();

        foreach (var (categoryName, contributingFamilies) in categoryFamilyMap)
        {
            var familyResults = assessment.ControlFamilyResults
                .Where(f => contributingFamilies.Contains(f.FamilyCode, StringComparer.OrdinalIgnoreCase)
                         && f.Status == FamilyAssessmentStatus.Completed)
                .ToList();

            var avgCompliance = familyResults.Count > 0
                ? familyResults.Average(f => f.ComplianceScore)
                : 50.0; // neutral if no data

            // Score 1-10: 1 + (avgCompliance / 100 * 9), clamped
            var score = Math.Clamp(1.0 + (avgCompliance / 100.0 * 9.0), 1.0, 10.0);
            score = Math.Round(score, 1);

            var findings = assessment.Findings
                .Count(f => contributingFamilies.Contains(f.ControlFamily, StringComparer.OrdinalIgnoreCase));

            var categoryRiskLevel = score >= 8 ? ComplianceRiskLevel.Low
                : score >= 5 ? ComplianceRiskLevel.Medium
                : score >= 3 ? ComplianceRiskLevel.High
                : ComplianceRiskLevel.Critical;

            var mitigations = new List<string>();
            if (score < 5)
            {
                mitigations.Add($"Review and remediate {categoryName} controls immediately");
                mitigations.Add($"Implement compensating controls for {string.Join(", ", contributingFamilies)} families");
            }

            categories.Add(new RiskCategory
            {
                Name = categoryName,
                Score = score,
                RiskLevel = categoryRiskLevel,
                Findings = findings,
                Mitigations = mitigations
            });
        }

        var overallScore = Math.Round(categories.Average(c => c.Score), 1);
        var overallRiskLevel = overallScore >= 8 ? ComplianceRiskLevel.Low
            : overallScore >= 5 ? ComplianceRiskLevel.Medium
            : overallScore >= 3 ? ComplianceRiskLevel.High
            : ComplianceRiskLevel.Critical;

        var recommendations = categories
            .Where(c => c.Score < 5)
            .Select(c => $"Prioritize {c.Name}: current score {c.Score}/10 requires immediate attention")
            .ToList();

        return new RiskAssessment
        {
            SubscriptionId = subscriptionId,
            AssessedAt = DateTime.UtcNow,
            Categories = categories,
            OverallScore = overallScore,
            OverallRiskLevel = overallRiskLevel,
            Recommendations = recommendations
        };
    }

    /// <inheritdoc />
    public async Task<ComplianceCertificate> GenerateCertificateAsync(
        string subscriptionId,
        string issuedBy,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating compliance certificate for subscription {Sub}", subscriptionId);

        var assessment = await _persistenceService.GetLatestAssessmentAsync(subscriptionId, cancellationToken);
        if (assessment is null)
            throw new InvalidOperationException($"No assessment found for subscription {subscriptionId}. Run an assessment first.");

        if (assessment.ComplianceScore < 80.0)
            throw new InvalidOperationException(
                $"Compliance score {assessment.ComplianceScore:F1}% is below the 80% threshold required for certification.");

        var now = DateTime.UtcNow;
        var familyAttestations = assessment.ControlFamilyResults
            .Where(f => f.Status == FamilyAssessmentStatus.Completed)
            .Select(f => new FamilyAttestation
            {
                FamilyCode = f.FamilyCode,
                FamilyName = f.FamilyName,
                ComplianceScore = f.ComplianceScore,
                ControlsAssessed = f.TotalControls,
                ControlsPassed = f.PassedControls,
                AttestationText = $"Family {f.FamilyCode} ({f.FamilyName}) assessed with {f.ComplianceScore:F1}% compliance. " +
                                  $"{f.PassedControls}/{f.TotalControls} controls passed."
            })
            .ToList();

        var certificate = new ComplianceCertificate
        {
            CertificateId = Guid.NewGuid().ToString(),
            SubscriptionId = subscriptionId,
            Framework = "NIST80053",
            ComplianceScore = assessment.ComplianceScore,
            IssuedAt = now,
            ExpiresAt = now.AddDays(180),
            IssuedBy = issuedBy,
            FamilyAttestations = familyAttestations,
            CoverageFamilies = familyAttestations.Select(a => a.FamilyCode).ToList(),
            Status = CertificateStatus.Active
        };

        // Generate SHA-256 verification hash
        var hashInput = $"{certificate.CertificateId}|{certificate.SubscriptionId}|" +
                        $"{certificate.ComplianceScore}|{certificate.IssuedAt:O}|{certificate.IssuedBy}";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hashInput));
        certificate.VerificationHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        _logger.LogInformation(
            "Certificate {CertId} issued for {Sub} (score={Score:F1}%, expires={Expiry})",
            certificate.CertificateId, subscriptionId, certificate.ComplianceScore, certificate.ExpiresAt);

        return certificate;
    }

    /// <inheritdoc />
    public async Task<ContinuousComplianceStatus> GetContinuousComplianceStatusAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting continuous compliance status for subscription {Sub}", subscriptionId);

        var status = new ContinuousComplianceStatus
        {
            SubscriptionId = subscriptionId
        };

        // Get latest assessment for score
        var assessment = await _persistenceService.GetLatestAssessmentAsync(subscriptionId, cancellationToken);
        if (assessment is not null)
        {
            status.OverallScore = assessment.ComplianceScore;
            status.LastAssessedAt = assessment.AssessedAt;

            // Build per-control statuses from findings
            status.ControlStatuses = assessment.ControlFamilyResults
                .Where(f => f.Status == FamilyAssessmentStatus.Completed)
                .SelectMany(f => assessment.Findings
                    .Where(fin => fin.ControlFamily == f.FamilyCode)
                    .Select(fin => new ControlComplianceStatus
                    {
                        ControlId = fin.ControlId,
                        Status = fin.Status,
                        DriftDetected = false,
                        LastCheckedAt = assessment.AssessedAt
                    }))
                .ToList();
        }

        // Try to get Compliance Watch monitoring status
        EnsureOptionalServicesResolved();
        try
        {
            if (_complianceWatchService is not null)
            {
                var monitorConfigs = await _complianceWatchService.GetMonitoringStatusAsync(subscriptionId, cancellationToken);
                status.MonitoringEnabled = monitorConfigs.Any(m => m.IsEnabled);

                if (status.MonitoringEnabled)
                {
                    var driftAlerts = await _complianceWatchService.DetectDriftAsync(subscriptionId, cancellationToken: cancellationToken);
                    status.DriftDetected = driftAlerts.Count > 0;
                    status.LastDriftCheckAt = DateTime.UtcNow;

                    // Merge drift info into control statuses
                    foreach (var drift in driftAlerts)
                    {
                        var controlStatus = status.ControlStatuses
                            .FirstOrDefault(c => c.ControlId == drift.ControlId);
                        if (controlStatus is not null)
                            controlStatus.DriftDetected = true;
                    }
                }

                // Count active alerts
                if (_alertManager is not null)
                {
                    var (alerts, totalCount) = await _alertManager.GetAlertsAsync(
                        subscriptionId: subscriptionId,
                        cancellationToken: cancellationToken);
                    status.ActiveAlerts = alerts.Count(a =>
                        a.Status != AlertStatus.Resolved && a.Status != AlertStatus.Dismissed);
                }

                // Check auto-remediation
                var autoRemRules = await _complianceWatchService.GetAutoRemediationRulesAsync(
                    subscriptionId, isEnabled: true, cancellationToken: cancellationToken);
                status.AutoRemediationEnabled = autoRemRules.Count > 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve Compliance Watch status for subscription {Sub}", subscriptionId);
            // Graceful degradation: return status with what we have
        }

        return status;
    }

    /// <inheritdoc />
    public async Task<ComplianceTimeline> GetComplianceTimelineAsync(
        string subscriptionId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating compliance timeline for {Sub} from {Start} to {End}",
            subscriptionId, startDate, endDate);

        var days = (int)(endDate - startDate).TotalDays;
        var assessments = await _persistenceService.GetAssessmentHistoryAsync(
            subscriptionId, days, cancellationToken);

        // Build daily data points from assessments
        var dataPoints = new List<TimelineDataPoint>();
        var significantEvents = new List<SignificantEvent>();

        // Group assessments by day and pick latest per day
        var assessmentsByDay = assessments
            .GroupBy(a => a.AssessedAt.Date)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AssessedAt).First());

        TimelineDataPoint? previousPoint = null;

        for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
        {
            if (assessmentsByDay.TryGetValue(date, out var dayAssessment))
            {
                var point = new TimelineDataPoint
                {
                    Date = date,
                    ComplianceScore = dayAssessment.ComplianceScore,
                    FindingCount = dayAssessment.Findings.Count,
                    CriticalCount = dayAssessment.Findings.Count(f => f.Severity == FindingSeverity.Critical),
                    HighCount = dayAssessment.Findings.Count(f => f.Severity == FindingSeverity.High)
                };

                dataPoints.Add(point);

                // Detect significant events
                if (previousPoint is not null)
                {
                    var scoreChange = point.ComplianceScore - previousPoint.ComplianceScore;
                    var findingChange = point.FindingCount - previousPoint.FindingCount;

                    if (Math.Abs(scoreChange) >= 10)
                    {
                        significantEvents.Add(new SignificantEvent
                        {
                            Date = date,
                            EventType = scoreChange > 0 ? TimelineEventType.ScoreImprovement : TimelineEventType.ScoreDegradation,
                            Description = $"Compliance score changed by {scoreChange:+0.0;-0.0}% (from {previousPoint.ComplianceScore:F1}% to {point.ComplianceScore:F1}%)",
                            ScoreChange = scoreChange,
                            FindingChange = findingChange
                        });
                    }

                    if (findingChange >= 5)
                    {
                        significantEvents.Add(new SignificantEvent
                        {
                            Date = date,
                            EventType = TimelineEventType.FindingSpike,
                            Description = $"Finding count increased by {findingChange} (from {previousPoint.FindingCount} to {point.FindingCount})",
                            ScoreChange = scoreChange,
                            FindingChange = findingChange
                        });
                    }

                    if (findingChange <= -5)
                    {
                        significantEvents.Add(new SignificantEvent
                        {
                            Date = date,
                            EventType = TimelineEventType.FindingResolution,
                            Description = $"Finding count decreased by {Math.Abs(findingChange)} (from {previousPoint.FindingCount} to {point.FindingCount})",
                            ScoreChange = scoreChange,
                            FindingChange = findingChange
                        });
                    }
                }

                previousPoint = point;
            }
        }

        // Calculate trend direction
        var trend = TrendDirection.Stable;
        if (dataPoints.Count >= 2)
        {
            var firstScore = dataPoints.First().ComplianceScore;
            var lastScore = dataPoints.Last().ComplianceScore;
            var delta = lastScore - firstScore;

            trend = delta > 5 ? TrendDirection.Improving
                : delta < -5 ? TrendDirection.Degrading
                : TrendDirection.Stable;
        }

        // Generate insights
        var insights = new List<string>();
        if (dataPoints.Count >= 2)
        {
            var scores = dataPoints.Select(p => p.ComplianceScore).ToList();
            var avgScore = scores.Average();
            var lastScore = scores.Last();

            // Trajectory
            insights.Add(trend switch
            {
                TrendDirection.Improving => $"Compliance is improving: score increased from {scores.First():F1}% to {lastScore:F1}%",
                TrendDirection.Degrading => $"Compliance is degrading: score decreased from {scores.First():F1}% to {lastScore:F1}%",
                _ => $"Compliance is stable around {avgScore:F1}%"
            });

            // Volatility
            if (scores.Count > 1)
            {
                var variance = scores.Sum(s => Math.Pow(s - avgScore, 2)) / scores.Count;
                var stdDev = Math.Sqrt(variance);
                if (stdDev > 10)
                    insights.Add($"High volatility detected (σ={stdDev:F1}). Investigate recurring compliance issues.");
                else if (stdDev < 3)
                    insights.Add("Compliance scores are very stable.");
            }

            // Remediation effectiveness
            var remediationEvents = significantEvents.Count(e => e.EventType == TimelineEventType.ScoreImprovement);
            if (remediationEvents > 0)
                insights.Add($"{remediationEvents} significant improvement event(s) detected, indicating effective remediation.");
        }
        else if (dataPoints.Count == 0)
        {
            insights.Add("No assessment data available for the selected period.");
        }

        return new ComplianceTimeline
        {
            SubscriptionId = subscriptionId,
            StartDate = startDate,
            EndDate = endDate,
            DataPoints = dataPoints,
            SignificantEvents = significantEvents,
            Trend = trend,
            Insights = insights
        };
    }

    /// <inheritdoc />
    public async Task<bool> UpdateFindingStatusAsync(
        string findingId,
        FindingStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating finding {FindingId} status to {Status}", findingId, newStatus);
        return await _persistenceService.UpdateFindingStatusAsync(findingId, newStatus, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ComplianceAssessment?> GetLatestAssessmentAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        return await _persistenceService.GetLatestAssessmentAsync(subscriptionId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> GetAuditLogAsync(
        string? subscriptionId = null,
        int days = 7,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving audit log (sub={Sub}, days={Days})", subscriptionId ?? "all", days);

        var assessments = subscriptionId is not null
            ? await _persistenceService.GetAssessmentHistoryAsync(subscriptionId, days, cancellationToken)
            : new List<ComplianceAssessment>();

        var sb = new StringBuilder();
        sb.AppendLine("# Assessment Audit Log");
        sb.AppendLine($"**Period**: Last {days} days");
        if (subscriptionId is not null)
            sb.AppendLine($"**Subscription**: {subscriptionId}");
        sb.AppendLine();

        if (assessments.Count == 0)
        {
            sb.AppendLine("No assessments found in the specified period.");
            return sb.ToString();
        }

        sb.AppendLine("| Date | Status | Score | Controls | Findings |");
        sb.AppendLine("|------|--------|-------|----------|----------|");

        foreach (var a in assessments.OrderByDescending(a => a.AssessedAt))
        {
            sb.AppendLine($"| {a.AssessedAt:yyyy-MM-dd HH:mm} | {a.Status} | {a.ComplianceScore:F1}% | {a.TotalControls} | {a.Findings.Count} |");
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public string GenerateExecutiveSummary(ComplianceAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        var sb = new StringBuilder();
        sb.AppendLine("# Executive Summary — NIST 800-53 Compliance Assessment");
        sb.AppendLine();
        sb.AppendLine($"**Assessment ID**: {assessment.Id}");
        sb.AppendLine($"**Date**: {assessment.AssessedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Subscription**: {assessment.SubscriptionId}");
        if (!string.IsNullOrEmpty(assessment.EnvironmentName))
            sb.AppendLine($"**Environment**: {assessment.EnvironmentName}");
        if (!string.IsNullOrEmpty(assessment.ResourceGroupFilter))
            sb.AppendLine($"**Resource Group**: {assessment.ResourceGroupFilter}");
        sb.AppendLine();

        // Overall score
        sb.AppendLine($"## Overall Compliance Score: {assessment.ComplianceScore:F1}%");
        sb.AppendLine();

        // Risk level
        var riskLevel = assessment.ComplianceScore >= 90 ? "Low"
            : assessment.ComplianceScore >= 70 ? "Medium"
            : assessment.ComplianceScore >= 50 ? "High"
            : "Critical";
        sb.AppendLine($"**Risk Level**: {riskLevel}");
        sb.AppendLine();

        // Controls summary
        sb.AppendLine("## Controls Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Total Controls | {assessment.TotalControls} |");
        sb.AppendLine($"| Passed | {assessment.PassedControls} |");
        sb.AppendLine($"| Failed | {assessment.FailedControls} |");
        sb.AppendLine($"| Not Assessed | {assessment.NotAssessedControls} |");
        sb.AppendLine();

        // Findings by severity
        var criticalCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.Critical);
        var highCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.High);
        var mediumCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.Medium);
        var lowCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.Low);

        sb.AppendLine("## Findings by Severity");
        sb.AppendLine();
        sb.AppendLine("| Severity | Count |");
        sb.AppendLine("|----------|-------|");
        sb.AppendLine($"| Critical | {criticalCount} |");
        sb.AppendLine($"| High | {highCount} |");
        sb.AppendLine($"| Medium | {mediumCount} |");
        sb.AppendLine($"| Low | {lowCount} |");
        sb.AppendLine($"| **Total** | **{assessment.Findings.Count}** |");
        sb.AppendLine();

        // Top risk families (lowest compliance scores)
        if (assessment.ControlFamilyResults.Count > 0)
        {
            var topRiskFamilies = assessment.ControlFamilyResults
                .Where(f => f.Status == FamilyAssessmentStatus.Completed)
                .OrderBy(f => f.ComplianceScore)
                .Take(5)
                .ToList();

            if (topRiskFamilies.Count > 0)
            {
                sb.AppendLine("## Top Risk Families");
                sb.AppendLine();
                sb.AppendLine("| Family | Score | Failed Controls |");
                sb.AppendLine("|--------|-------|-----------------|");
                foreach (var family in topRiskFamilies)
                {
                    sb.AppendLine($"| {family.FamilyCode} — {family.FamilyName} | {family.ComplianceScore:F1}% | {family.FailedControls} |");
                }
                sb.AppendLine();
            }
        }

        if (assessment.AssessmentDuration.HasValue)
        {
            sb.AppendLine($"**Assessment Duration**: {assessment.AssessmentDuration.Value.TotalSeconds:F1}s");
        }

        return sb.ToString();
    }

    // ─── Score Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Computes overall assessment scores by aggregating per-family results.
    /// </summary>
    private static void ComputeScoresFromFamilyResults(ComplianceAssessment assessment)
    {
        var completedFamilies = assessment.ControlFamilyResults
            .Where(f => f.Status == FamilyAssessmentStatus.Completed)
            .ToList();

        if (completedFamilies.Count == 0)
        {
            assessment.ComplianceScore = 0;
            return;
        }

        int totalControls = 0, passedControls = 0, failedControls = 0;

        foreach (var family in completedFamilies)
        {
            totalControls += family.TotalControls;
            passedControls += family.PassedControls;
            failedControls += family.FailedControls;
        }

        assessment.TotalControls = totalControls;
        assessment.PassedControls = passedControls;
        assessment.FailedControls = failedControls;
        assessment.NotAssessedControls = totalControls - passedControls - failedControls;
        assessment.ComplianceScore = totalControls > 0
            ? Math.Round((double)passedControls / totalControls * 100, 1)
            : 0;
    }}

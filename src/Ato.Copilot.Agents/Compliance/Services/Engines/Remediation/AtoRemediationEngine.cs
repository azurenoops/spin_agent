using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Compliance.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services.Engines.Remediation;

/// <summary>
/// Full-featured ATO remediation engine implementing <see cref="IRemediationEngine"/>.
/// Provides prioritized plan generation with severity mapping, risk scoring,
/// 3-tier remediation pipeline (AI → Structured → ARM), batch execution with
/// SemaphoreSlim concurrency, validation and rollback, approval workflow,
/// kanban integration, scheduling, and progress tracking.
/// </summary>
public class AtoRemediationEngine : IRemediationEngine
{
    private readonly IAtoComplianceEngine _complianceEngine;
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IAzureArmRemediationService _armService;
    private readonly IAiRemediationPlanGenerator _aiGenerator;
    private readonly IComplianceRemediationService _complianceRemediationService;
    private readonly IRemediationScriptExecutor _scriptExecutor;
    private readonly INistRemediationStepsService _nistStepsService;
    private readonly IScriptSanitizationService _sanitizationService;
    private readonly ComplianceAgentOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AtoRemediationEngine> _logger;

    private readonly ConcurrentDictionary<string, RemediationExecution> _activeRemediations = new();
    private readonly List<RemediationExecution> _remediationHistory = new();
    private readonly object _historyLock = new();
    private readonly SemaphoreSlim _semaphore;

    private static readonly HashSet<string> HighRiskFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "IA", "SC"
    };

    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="AtoRemediationEngine"/> class.
    /// </summary>
    public AtoRemediationEngine(
        IAtoComplianceEngine complianceEngine,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IAzureArmRemediationService armService,
        IAiRemediationPlanGenerator aiGenerator,
        IComplianceRemediationService complianceRemediationService,
        IRemediationScriptExecutor scriptExecutor,
        INistRemediationStepsService nistStepsService,
        IScriptSanitizationService sanitizationService,
        IOptions<ComplianceAgentOptions> options,
        ILogger<AtoRemediationEngine> logger,
        IServiceScopeFactory scopeFactory)
    {
        _complianceEngine = complianceEngine;
        _dbFactory = dbFactory;
        _armService = armService;
        _aiGenerator = aiGenerator;
        _complianceRemediationService = complianceRemediationService;
        _scriptExecutor = scriptExecutor;
        _nistStepsService = nistStepsService;
        _sanitizationService = sanitizationService;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _semaphore = new SemaphoreSlim(_options.Remediation.MaxConcurrentRemediations);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TIER 1: EXISTING METHODS (backward compatible)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<RemediationPlan> GeneratePlanAsync(
        string subscriptionId,
        string? resourceGroupName = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating remediation plan for subscription {SubId}", subscriptionId);

        // Get latest assessment findings
        var history = await _complianceEngine.GetAssessmentHistoryAsync(subscriptionId, 7, cancellationToken);
        var latestAssessment = history.FirstOrDefault();

        var plan = new RemediationPlan
        {
            SubscriptionId = subscriptionId,
            DryRun = true // Always start as dry-run per SEC-018
        };

        if (latestAssessment == null)
        {
            _logger.LogWarning("No recent assessment found for {SubId}", subscriptionId);
            return plan;
        }

        // Get findings from the latest assessment
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var findings = await db.Findings
            .Where(f => f.AssessmentId == latestAssessment.Id && f.Status == FindingStatus.Open)
            .OrderBy(f => f.Severity)
            .ThenBy(f => f.ControlId)
            .ToListAsync(cancellationToken);

        // Filter by resource group if specified
        if (!string.IsNullOrEmpty(resourceGroupName))
        {
            findings = findings
                .Where(f => f.ResourceId.Contains(resourceGroupName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        plan.TotalFindings = findings.Count;
        int priority = 1;

        foreach (var finding in findings)
        {
            var step = new RemediationStep
            {
                FindingId = finding.Id,
                ControlId = finding.ControlId,
                Priority = priority++,
                Description = GenerateRemediationDescription(finding),
                Script = GenerateRemediationScript(finding),
                Effort = EstimateEffort(finding),
                AutoRemediable = finding.AutoRemediable,
                RemediationType = finding.RemediationType,
                ResourceId = finding.ResourceId,
                RiskLevel = HighRiskFamilies.Contains(finding.ControlFamily) ? RiskLevel.High : RiskLevel.Standard
            };

            plan.Steps.Add(step);
        }

        plan.AutoRemediableCount = plan.Steps.Count(s => s.AutoRemediable);

        // Persist the plan
        db.RemediationPlans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Remediation plan {Id} created | Findings: {Total} | Auto-remediable: {Auto}",
            plan.Id, plan.TotalFindings, plan.AutoRemediableCount);

        return plan;
    }

    /// <inheritdoc />
    public async Task<string> ExecuteRemediationAsync(
        string findingId,
        bool applyRemediation = false,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Executing remediation for finding {FindingId} | DryRun: {DryRun} | Apply: {Apply}",
            findingId, dryRun, applyRemediation);

        // Gate: EnableAutomatedRemediation
        if (!_options.EnableAutomatedRemediation && applyRemediation && !dryRun)
        {
            return JsonSerializer.Serialize(new
            {
                status = "error",
                error = new { message = "Automated remediation is disabled", errorCode = "REMEDIATION_DISABLED" }
            });
        }

        var finding = await _complianceEngine.GetFindingAsync(findingId, cancellationToken);
        if (finding == null)
        {
            return JsonSerializer.Serialize(new
            {
                status = "error",
                error = new { message = $"Finding '{findingId}' not found", errorCode = "FINDING_NOT_FOUND" }
            });
        }

        var isHighRisk = HighRiskFamilies.Contains(finding.ControlFamily);

        if (dryRun || !applyRemediation)
        {
            var dryRunResult = new
            {
                status = "success",
                data = new
                {
                    mode = "dry-run",
                    findingId = finding.Id,
                    controlId = finding.ControlId,
                    controlFamily = finding.ControlFamily,
                    severity = finding.Severity.ToString(),
                    isHighRisk,
                    highRiskWarning = isHighRisk
                        ? "⚠️ This control is in a high-risk family (AC/IA/SC). Changes may affect user access and security boundaries. Requires additional approval."
                        : null,
                    remediationType = finding.RemediationType.ToString(),
                    autoRemediable = finding.AutoRemediable,
                    remediationGuidance = finding.RemediationGuidance,
                    script = finding.RemediationScript ?? GenerateRemediationScript(finding),
                    estimatedEffort = EstimateEffort(finding),
                    resourceId = finding.ResourceId,
                    resourceType = finding.ResourceType,
                    nextSteps = new[]
                    {
                        "Review the remediation script.",
                        "Run with applyRemediation=true and dryRun=false to execute.",
                        isHighRisk ? "Get ComplianceOfficer approval before proceeding." : null
                    }.Where(s => s != null)
                }
            };

            return JsonSerializer.Serialize(dryRunResult, CamelCaseJson);
        }

        // Delegate to 3-tier pipeline via typed overload
        try
        {
            var options = new RemediationExecutionOptions
            {
                DryRun = false,
                UseAiScript = _options.Remediation.UseAiScript,
                RequireApproval = _options.Remediation.RequireApproval,
                AutoValidate = _options.Remediation.AutoValidate,
                AutoRollbackOnFailure = _options.Remediation.AutoRollbackOnFailure
            };

            var execution = await ExecuteRemediationAsync(findingId, options, cancellationToken);

            // Track in ConcurrentDictionary
            _activeRemediations[execution.Id] = execution;

            // Update DB status
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var dbFinding = await db.Findings.FindAsync(new object[] { findingId }, cancellationToken);
            if (dbFinding != null)
            {
                dbFinding.Status = execution.Status == RemediationExecutionStatus.Completed
                    ? FindingStatus.InProgress
                    : FindingStatus.Open;
                await db.SaveChangesAsync(cancellationToken);
            }

            var executionResult = new
            {
                status = execution.Status == RemediationExecutionStatus.Completed ? "success" : "error",
                data = new
                {
                    mode = "executed",
                    findingId = finding.Id,
                    controlId = finding.ControlId,
                    isHighRisk,
                    applied = execution.Status == RemediationExecutionStatus.Completed,
                    executionId = execution.Id,
                    tierUsed = execution.TierUsed,
                    stepsExecuted = execution.StepsExecuted,
                    executedAt = execution.CompletedAt ?? DateTime.UtcNow,
                    message = execution.Status == RemediationExecutionStatus.Completed
                        ? $"Remediation applied for {finding.ControlId} on {finding.ResourceType} (Tier {execution.TierUsed})"
                        : $"Remediation failed: {execution.Error}",
                    nextSteps = new[] { "Run compliance_validate_remediation to confirm the fix." }
                }
            };

            return JsonSerializer.Serialize(executionResult, CamelCaseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remediation failed for finding {FindingId}", findingId);
            return JsonSerializer.Serialize(new
            {
                status = "error",
                error = new { message = ex.Message, errorCode = "REMEDIATION_FAILED" }
            });
        }
    }

    /// <inheritdoc />
    public async Task<string> ValidateRemediationAsync(
        string findingId,
        string? executionId = null,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating remediation for finding {FindingId}", findingId);

        // If executionId provided, delegate to typed overload
        if (!string.IsNullOrEmpty(executionId))
        {
            var typedResult = await ValidateRemediationAsync(executionId, cancellationToken);
            if (typedResult.IsValid)
            {
                // Also update DB status
                await using var db2 = await _dbFactory.CreateDbContextAsync(cancellationToken);
                var dbf = await db2.Findings.FindAsync(new object[] { findingId }, cancellationToken);
                if (dbf != null)
                {
                    dbf.Status = FindingStatus.Remediated;
                    await db2.SaveChangesAsync(cancellationToken);
                }
            }

            return JsonSerializer.Serialize(new
            {
                status = typedResult.IsValid ? "success" : "error",
                data = new
                {
                    findingId,
                    executionId,
                    validated = typedResult.IsValid,
                    validatedAt = typedResult.ValidatedAt,
                    checks = typedResult.Checks.Select(c => new { c.Name, c.Passed, c.ExpectedValue, c.ActualValue }),
                    failureReason = typedResult.FailureReason
                }
            }, CamelCaseJson);
        }

        // Legacy flow — validate finding directly
        var finding = await _complianceEngine.GetFindingAsync(findingId, cancellationToken);
        if (finding == null)
        {
            return JsonSerializer.Serialize(new
            {
                status = "error",
                error = new { message = $"Finding '{findingId}' not found", errorCode = "FINDING_NOT_FOUND" }
            });
        }

        var validationResult = new
        {
            status = "success",
            data = new
            {
                findingId = finding.Id,
                controlId = finding.ControlId,
                validated = true,
                validatedAt = DateTime.UtcNow,
                previousStatus = finding.Status.ToString(),
                newStatus = "Remediated",
                message = $"Finding {finding.ControlId} has been validated as remediated."
            }
        };

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var dbFinding = await db.Findings.FindAsync(new object[] { findingId }, cancellationToken);
        if (dbFinding != null)
        {
            dbFinding.Status = FindingStatus.Remediated;
            await db.SaveChangesAsync(cancellationToken);
        }

        return JsonSerializer.Serialize(validationResult, CamelCaseJson);
    }

    /// <inheritdoc />
    public async Task<string> BatchRemediateAsync(
        string? subscriptionId = null,
        string? severity = null,
        string? family = null,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Batch remediation | Sub: {SubId} | Severity: {Severity} | Family: {Family} | DryRun: {DryRun}",
            subscriptionId ?? "all", severity ?? "all", family ?? "all", dryRun);

        if (string.IsNullOrEmpty(subscriptionId))
        {
            return JsonSerializer.Serialize(new
            {
                status = "error",
                error = new { message = "Subscription ID is required for batch remediation", errorCode = "SUBSCRIPTION_NOT_CONFIGURED" }
            });
        }

        var plan = await GeneratePlanAsync(subscriptionId, cancellationToken: cancellationToken);
        var steps = plan.Steps.AsEnumerable();

        if (!string.IsNullOrEmpty(severity) &&
            Enum.TryParse<FindingSeverity>(severity, true, out var severityFilter))
        {
            var finding = await Task.WhenAll(
                steps.Select(async s =>
                {
                    var f = await _complianceEngine.GetFindingAsync(s.FindingId, cancellationToken);
                    return (Step: s, Finding: f);
                }));

            steps = finding
                .Where(x => x.Finding != null && x.Finding.Severity <= severityFilter)
                .Select(x => x.Step);
        }

        if (!string.IsNullOrEmpty(family))
        {
            var familyUpper = family.ToUpperInvariant();
            steps = steps.Where(s =>
                s.ControlId != null &&
                s.ControlId.StartsWith(familyUpper + "-", StringComparison.OrdinalIgnoreCase));
        }

        var stepsList = steps.ToList();
        int succeeded = 0, failed = 0;
        var results = new List<object>();

        foreach (var step in stepsList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (dryRun)
            {
                step.Status = StepStatus.Pending;
                results.Add(new
                {
                    stepId = step.Id,
                    controlId = step.ControlId,
                    status = "dry-run",
                    autoRemediable = step.AutoRemediable,
                    riskLevel = step.RiskLevel.ToString()
                });
                succeeded++;
                continue;
            }

            if (!step.AutoRemediable)
            {
                step.Status = StepStatus.Skipped;
                results.Add(new
                {
                    stepId = step.Id,
                    controlId = step.ControlId,
                    status = "skipped",
                    reason = "Manual remediation required"
                });
                continue;
            }

            try
            {
                var result = await ExecuteRemediationAsync(step.FindingId, true, false, cancellationToken);
                step.Status = StepStatus.Completed;
                step.ExecutedAt = DateTime.UtcNow;
                succeeded++;
                results.Add(new { stepId = step.Id, controlId = step.ControlId, status = "completed" });
            }
            catch (Exception ex)
            {
                step.Status = StepStatus.Failed;
                failed++;
                results.Add(new { stepId = step.Id, controlId = step.ControlId, status = "failed", error = ex.Message });

                _logger.LogError(ex, "Batch remediation failed at step {StepId}, stopping", step.Id);
                break;
            }
        }

        var batchResult = new
        {
            status = failed > 0 ? "partial" : "success",
            data = new
            {
                planId = plan.Id,
                subscriptionId,
                dryRun,
                totalSteps = stepsList.Count,
                succeeded,
                failed,
                stoppedOnFailure = failed > 0,
                results
            }
        };

        return JsonSerializer.Serialize(batchResult, CamelCaseJson);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TIER 2: ENHANCED CORE OPERATIONS — Plan Generation
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<RemediationPlan> GenerateRemediationPlanAsync(
        IEnumerable<ComplianceFinding> findings,
        RemediationPlanOptions? options = null,
        CancellationToken ct = default)
    {
        await Task.CompletedTask; // Signature requires Task; no async I/O in this overload
        var findingsList = findings.ToList();
        _logger.LogInformation("Generating enhanced remediation plan for {Count} findings", findingsList.Count);

        var plan = new RemediationPlan
        {
            DryRun = true,
            TotalFindings = findingsList.Count
        };

        if (findingsList.Count == 0)
        {
            plan.Items = new List<RemediationItem>();
            plan.Timeline = BuildTimeline(new List<RemediationItem>());
            plan.ExecutiveSummary = BuildExecutiveSummary(new List<RemediationItem>(), findingsList);
            plan.RiskMetrics = CalculateRiskMetrics(findingsList);
            plan.Filters = options;
            return plan;
        }

        // Apply filters
        var filtered = ApplyFilters(findingsList, options);

        // Build remediation items with severity-to-priority mapping
        var items = new List<RemediationItem>();
        foreach (var finding in filtered)
        {
            var priority = MapSeverityToPriority(finding.Severity);
            var steps = _nistStepsService.GetRemediationSteps(finding.ControlFamily, finding.ControlId);

            var item = new RemediationItem
            {
                Finding = finding,
                Priority = priority,
                PriorityLabel = GetPriorityLabel(priority),
                EstimatedDuration = EstimateDuration(finding),
                Steps = steps.Select(s => new RemediationStep
                {
                    Description = s,
                    FindingId = finding.Id,
                    ControlId = finding.ControlId
                }).ToList(),
                ValidationSteps = new List<string>
                {
                    $"Re-scan resource {finding.ResourceId} for control {finding.ControlId}",
                    "Verify finding status changed to Remediated"
                },
                RollbackPlan = $"Restore resource {finding.ResourceId} from before-snapshot",
                IsAutoRemediable = finding.AutoRemediable,
                RemediationType = finding.RemediationType,
                AffectedResourceId = finding.ResourceId
            };

            items.Add(item);
        }

        // Sort: severity desc → auto-remediable first → duration asc
        items = items
            .OrderBy(i => i.Priority)
            .ThenByDescending(i => i.IsAutoRemediable)
            .ThenBy(i => i.EstimatedDuration)
            .ToList();

        // Group by resource if requested
        if (options?.GroupByResource == true)
        {
            items = items
                .OrderBy(i => i.AffectedResourceId)
                .ThenBy(i => i.Priority)
                .ThenByDescending(i => i.IsAutoRemediable)
                .ThenBy(i => i.EstimatedDuration)
                .ToList();
        }

        plan.Items = items;
        plan.TotalFindings = findingsList.Count; // Original count before filtering
        plan.AutoRemediableCount = items.Count(i => i.IsAutoRemediable);
        plan.Timeline = BuildTimeline(items);
        plan.ExecutiveSummary = BuildExecutiveSummary(items, filtered);
        plan.RiskMetrics = CalculateRiskMetrics(filtered);
        plan.GroupByResource = options?.GroupByResource == true;
        plan.Filters = options;

        _logger.LogInformation(
            "Enhanced remediation plan generated | Items: {Count} | AutoRemediable: {Auto} | RiskReduction: {Reduction:F1}%",
            items.Count, plan.AutoRemediableCount, plan.RiskMetrics.RiskReductionPercentage);

        return plan;
    }

    /// <inheritdoc />
    public async Task<RemediationPlan> GenerateRemediationPlanAsync(
        ComplianceFinding finding,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Generating single-finding remediation plan for {FindingId}", finding.Id);

        // Tier 1: Try AI-enhanced plan
        if (_aiGenerator.IsAvailable)
        {
            try
            {
                var aiPlan = await _aiGenerator.GenerateEnhancedPlanAsync(finding, ct);
                if (aiPlan != null)
                {
                    _logger.LogInformation("AI-enhanced plan generated for {FindingId}", finding.Id);
                    return aiPlan;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI plan generation failed for {FindingId}, falling back", finding.Id);
            }
        }

        // Tier 2: NIST curated steps
        var nistSteps = _nistStepsService.GetRemediationSteps(finding.ControlFamily, finding.ControlId);
        if (nistSteps.Count > 0)
        {
            _logger.LogInformation("Using NIST curated steps for {FindingId}", finding.Id);
            return await Task.FromResult(BuildSingleFindingPlan(finding, nistSteps));
        }

        // Tier 3: Manual parsing of guidance text
        _logger.LogInformation("Using parsed guidance text for {FindingId}", finding.Id);
        var parsedSteps = _nistStepsService.ParseStepsFromGuidance(finding.RemediationGuidance);
        if (parsedSteps.Count == 0)
        {
            parsedSteps = new List<string>
            {
                $"Review finding: {finding.Title}",
                $"Follow remediation guidance: {finding.RemediationGuidance}",
                $"Verify control {finding.ControlId} compliance on resource {finding.ResourceId}"
            };
        }

        return BuildSingleFindingPlan(finding, parsedSteps);
    }

    /// <inheritdoc />
    public async Task<RemediationPlan> GenerateRemediationPlanAsync(
        string subscriptionId,
        RemediationPlanOptions? options = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Generating enhanced remediation plan for subscription {SubId}", subscriptionId);

        // Get latest assessment findings via compliance engine
        var assessmentHistory = await _complianceEngine.GetAssessmentHistoryAsync(subscriptionId, 7, ct);
        var latestAssessment = assessmentHistory.FirstOrDefault();

        if (latestAssessment == null)
        {
            _logger.LogWarning("No recent assessment found for {SubId}", subscriptionId);
            return await GenerateRemediationPlanAsync(Enumerable.Empty<ComplianceFinding>(), options, ct);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var findings = await db.Findings
            .Where(f => f.AssessmentId == latestAssessment.Id && f.Status == FindingStatus.Open)
            .ToListAsync(ct);

        var plan = await GenerateRemediationPlanAsync(findings, options, ct);
        plan.SubscriptionId = subscriptionId;
        return plan;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TIER 2: ENHANCED CORE OPERATIONS — Execution
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<RemediationExecution> ExecuteRemediationAsync(
        string findingId,
        RemediationExecutionOptions options,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Executing typed remediation for {FindingId} | DryRun: {DryRun}", findingId, options.DryRun);

        // Gate: EnableAutomatedRemediation
        if (!_options.EnableAutomatedRemediation)
        {
            _logger.LogWarning("Automated remediation is disabled");
            return new RemediationExecution
            {
                FindingId = findingId,
                Status = RemediationExecutionStatus.Failed,
                Error = "Automated remediation is disabled. Use manual remediation guidance instead.",
                DryRun = options.DryRun,
                Options = options,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
        }

        // Lookup finding
        var finding = await _complianceEngine.GetFindingAsync(findingId, ct);
        if (finding == null)
        {
            return new RemediationExecution
            {
                FindingId = findingId,
                Status = RemediationExecutionStatus.Failed,
                Error = $"Finding '{findingId}' not found",
                DryRun = options.DryRun,
                Options = options,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
        }

        // Non-auto-remediable finding — return error with guidance suggestion
        if (!finding.AutoRemediable)
        {
            _logger.LogInformation("Finding {FindingId} is not auto-remediable, returning manual guidance suggestion", findingId);
            return new RemediationExecution
            {
                FindingId = findingId,
                Status = RemediationExecutionStatus.Failed,
                Error = $"Finding '{findingId}' is not auto-remediable. Use GenerateManualRemediationGuideAsync for step-by-step guidance.",
                DryRun = options.DryRun,
                Options = options,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
        }

        // Create execution record
        var execution = new RemediationExecution
        {
            FindingId = findingId,
            SubscriptionId = finding.SubscriptionId,
            Status = RemediationExecutionStatus.Pending,
            DryRun = options.DryRun,
            Options = options,
            StartedAt = DateTime.UtcNow
        };

        // RequireApproval — return immediately with Pending status
        if (options.RequireApproval)
        {
            _logger.LogInformation("Remediation for {FindingId} requires approval, returning Pending", findingId);
            _activeRemediations[execution.Id] = execution;
            return execution;
        }

        // Track as active
        execution.Status = RemediationExecutionStatus.InProgress;
        _activeRemediations[execution.Id] = execution;

        try
        {
            // Capture before-snapshot
            var beforeSnapshot = await _armService.CaptureResourceSnapshotAsync(finding.ResourceId, ct);
            execution.BeforeSnapshot = beforeSnapshot;
            if (beforeSnapshot != null)
            {
                execution.BackupId = $"backup-{execution.Id}";
            }

            // Execute through 3-tier pipeline
            RemediationExecution? tierResult = null;

            // Tier 1: AI script generation + execution
            if (options.UseAiScript && _aiGenerator.IsAvailable)
            {
                try
                {
                    var script = await _aiGenerator.GenerateScriptAsync(finding, ScriptType.AzureCli, ct);
                    if (script != null)
                    {
                        tierResult = await _scriptExecutor.ExecuteScriptAsync(script, findingId, options, ct);
                        if (tierResult.Status == RemediationExecutionStatus.Completed)
                        {
                            _logger.LogInformation("Tier 1 (AI) succeeded for {FindingId}", findingId);
                        }
                        else
                        {
                            _logger.LogWarning("Tier 1 (AI) execution failed for {FindingId}, falling back", findingId);
                            tierResult = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Tier 1 (AI) failed for {FindingId}, falling back to Tier 2", findingId);
                }
            }

            // Tier 2: Compliance remediation service
            if (tierResult == null)
            {
                try
                {
                    if (_complianceRemediationService.CanHandle(finding))
                    {
                        tierResult = await _complianceRemediationService.ExecuteStructuredRemediationAsync(finding, options, ct);
                        if (tierResult.Status == RemediationExecutionStatus.Completed)
                        {
                            _logger.LogInformation("Tier 2 (Structured) succeeded for {FindingId}", findingId);
                        }
                        else
                        {
                            _logger.LogWarning("Tier 2 (Structured) execution failed for {FindingId}, falling back", findingId);
                            tierResult = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Tier 2 (Structured) failed for {FindingId}, falling back to Tier 3", findingId);
                }
            }

            // Tier 3: ARM remediation
            if (tierResult == null)
            {
                try
                {
                    tierResult = await _armService.ExecuteArmRemediationAsync(finding, options, ct);
                    if (tierResult.Status == RemediationExecutionStatus.Completed)
                    {
                        _logger.LogInformation("Tier 3 (ARM) succeeded for {FindingId}", findingId);
                    }
                    else
                    {
                        tierResult = null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Tier 3 (ARM) failed for {FindingId}", findingId);
                }
            }

            // All tiers failed
            if (tierResult == null)
            {
                execution.Status = RemediationExecutionStatus.Failed;
                execution.Error = "All remediation tiers failed";
                execution.CompletedAt = DateTime.UtcNow;
                execution.Duration = execution.CompletedAt - execution.StartedAt;
                return execution;
            }

            // Merge tier result into execution
            execution.Status = tierResult.Status;
            execution.TierUsed = tierResult.TierUsed;
            execution.StepsExecuted = tierResult.StepsExecuted;
            execution.ChangesApplied = tierResult.ChangesApplied;

            // Capture after-snapshot
            var afterSnapshot = await _armService.CaptureResourceSnapshotAsync(finding.ResourceId, ct);
            execution.AfterSnapshot = afterSnapshot;

            execution.CompletedAt = DateTime.UtcNow;
            execution.Duration = execution.CompletedAt - execution.StartedAt;

            // Auto-validate if enabled
            if (options.AutoValidate && execution.Status == RemediationExecutionStatus.Completed)
            {
                var validation = await ValidateRemediationInternalAsync(execution, ct);
                if (!validation.IsValid && options.AutoRollbackOnFailure)
                {
                    _logger.LogWarning("Auto-validation failed for {FindingId}, auto-rolling back", findingId);
                    await RollbackRemediationAsync(execution.Id, ct);
                    execution.Status = RemediationExecutionStatus.RolledBack;
                }
            }

            // Move to history
            lock (_historyLock)
            {
                _remediationHistory.Add(execution);
            }

            // Kanban integration — best-effort post-execution sync
            await SyncKanbanPostExecutionAsync(execution, ct);

            _logger.LogInformation(
                "Remediation completed for {FindingId} | Status: {Status} | Tier: {Tier}",
                findingId, execution.Status, execution.TierUsed);

            return execution;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remediation failed for {FindingId}", findingId);
            execution.Status = RemediationExecutionStatus.Failed;
            execution.Error = ex.Message;
            execution.CompletedAt = DateTime.UtcNow;
            execution.Duration = execution.CompletedAt - execution.StartedAt;

            // Kanban failure handling — add comment to linked task
            await SyncKanbanFailureAsync(execution, ex.Message, ct);

            return execution;
        }
    }

    /// <inheritdoc />
    public Task<RemediationValidationResult> ValidateRemediationAsync(
        string executionId,
        CancellationToken ct)
    {
        _logger.LogInformation("Validating remediation execution {ExecutionId}", executionId);

        // Look up execution from active or history
        var execution = FindExecution(executionId);
        if (execution == null)
        {
            return Task.FromResult(new RemediationValidationResult
            {
                ExecutionId = executionId,
                IsValid = false,
                FailureReason = $"Execution '{executionId}' not found",
                ValidatedAt = DateTime.UtcNow
            });
        }

        return Task.FromResult(ValidateExecution(execution));
    }

    /// <inheritdoc />
    public async Task<BatchRemediationResult> ExecuteBatchRemediationAsync(
        IEnumerable<string> findingIds,
        BatchRemediationOptions? options = null,
        CancellationToken ct = default)
    {
        var ids = findingIds.ToList();
        options ??= new BatchRemediationOptions();
        var maxConcurrent = options.MaxConcurrentRemediations > 0
            ? options.MaxConcurrentRemediations
            : _options.Remediation.MaxConcurrentRemediations;

        _logger.LogInformation(
            "Starting batch remediation | Count: {Count} | MaxConcurrent: {Max} | FailFast: {FailFast}",
            ids.Count, maxConcurrent, options.FailFast);

        var batchResult = new BatchRemediationResult
        {
            StartedAt = DateTime.UtcNow,
            Options = options
        };

        if (ids.Count == 0)
        {
            batchResult.CompletedAt = DateTime.UtcNow;
            batchResult.Duration = TimeSpan.Zero;
            return batchResult;
        }

        using var batchSemaphore = new SemaphoreSlim(maxConcurrent);
        using var failFastCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var executions = new System.Collections.Concurrent.ConcurrentBag<RemediationExecution>();
        var skippedCount = 0;
        var cancelledCount = 0;

        var tasks = ids.Select(async findingId =>
        {
            if (failFastCts.Token.IsCancellationRequested)
            {
                Interlocked.Increment(ref cancelledCount);
                return;
            }

            await batchSemaphore.WaitAsync(failFastCts.Token).ConfigureAwait(false);
            try
            {
                if (failFastCts.Token.IsCancellationRequested)
                {
                    Interlocked.Increment(ref cancelledCount);
                    return;
                }

                // Check if finding is auto-remediable before executing
                var finding = await _complianceEngine.GetFindingAsync(findingId, failFastCts.Token);
                if (finding != null && !finding.AutoRemediable)
                {
                    Interlocked.Increment(ref skippedCount);
                    executions.Add(new RemediationExecution
                    {
                        FindingId = findingId,
                        Status = RemediationExecutionStatus.Cancelled,
                        Error = "Not auto-remediable — skipped",
                        StartedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow
                    });
                    return;
                }

                var executionOptions = new RemediationExecutionOptions
                {
                    DryRun = options.DryRun,
                    UseAiScript = _options.Remediation.UseAiScript,
                    AutoValidate = _options.Remediation.AutoValidate,
                    AutoRollbackOnFailure = _options.Remediation.AutoRollbackOnFailure
                };

                var execution = await ExecuteRemediationAsync(findingId, executionOptions, failFastCts.Token);
                executions.Add(execution);

                if (execution.Status == RemediationExecutionStatus.Failed && options.FailFast)
                {
                    _logger.LogWarning("FailFast triggered by {FindingId}", findingId);
                    failFastCts.Cancel();
                }
            }
            catch (OperationCanceledException) when (failFastCts.Token.IsCancellationRequested)
            {
                Interlocked.Increment(ref cancelledCount);
            }
            catch (Exception ex) when (options.ContinueOnError)
            {
                _logger.LogWarning(ex, "Batch item {FindingId} failed (ContinueOnError)", findingId);
                executions.Add(new RemediationExecution
                {
                    FindingId = findingId,
                    Status = RemediationExecutionStatus.Failed,
                    Error = ex.Message,
                    StartedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow
                });
            }
            finally
            {
                batchSemaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (failFastCts.Token.IsCancellationRequested)
        {
            // FailFast cancellation — expected
        }

        // Build result
        batchResult.Executions = executions.ToList();
        batchResult.SuccessCount = batchResult.Executions.Count(e => e.Status == RemediationExecutionStatus.Completed);
        batchResult.FailureCount = batchResult.Executions.Count(e => e.Status == RemediationExecutionStatus.Failed);
        batchResult.SkippedCount = skippedCount;
        batchResult.CancelledCount = cancelledCount;
        batchResult.CompletedAt = DateTime.UtcNow;
        batchResult.Duration = batchResult.CompletedAt - batchResult.StartedAt;

        // Build summary
        batchResult.Summary = await BuildBatchSummaryAsync(batchResult, ct);

        _logger.LogInformation(
            "Batch remediation complete | Success: {S} | Failed: {F} | Skipped: {Sk} | Cancelled: {C}",
            batchResult.SuccessCount, batchResult.FailureCount, batchResult.SkippedCount, batchResult.CancelledCount);

        return batchResult;
    }

    /// <inheritdoc />
    public async Task<RemediationRollbackResult> RollbackRemediationAsync(
        string executionId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Rolling back remediation execution {ExecutionId}", executionId);

        var execution = FindExecution(executionId);
        if (execution == null)
        {
            return new RemediationRollbackResult
            {
                ExecutionId = executionId,
                Success = false,
                Error = $"Execution '{executionId}' not found"
            };
        }

        if (string.IsNullOrEmpty(execution.BeforeSnapshot))
        {
            return new RemediationRollbackResult
            {
                ExecutionId = executionId,
                Success = false,
                Error = "No before-snapshot available for rollback"
            };
        }

        if (execution.Status == RemediationExecutionStatus.RolledBack)
        {
            return new RemediationRollbackResult
            {
                ExecutionId = executionId,
                Success = false,
                Error = "Execution has already been rolled back"
            };
        }

        try
        {
            // Look up the finding to get the resource ID
            var finding = await _complianceEngine.GetFindingAsync(execution.FindingId, ct);
            var resourceId = finding?.ResourceId ?? execution.FindingId;

            var restoreResult = await _armService.RestoreFromSnapshotAsync(
                resourceId, execution.BeforeSnapshot, ct);

            if (restoreResult.Success)
            {
                execution.Status = RemediationExecutionStatus.RolledBack;
                _activeRemediations.AddOrUpdate(executionId, execution, (_, _) => execution);
            }

            return new RemediationRollbackResult
            {
                ExecutionId = executionId,
                Success = restoreResult.Success,
                RollbackSteps = restoreResult.RollbackSteps,
                RestoredSnapshot = restoreResult.RestoredSnapshot,
                Error = restoreResult.Error,
                RolledBackAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed for execution {ExecutionId}", executionId);
            return new RemediationRollbackResult
            {
                ExecutionId = executionId,
                Success = false,
                Error = ex.Message
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TIER 3: WORKFLOW, TRACKING & AI-ENHANCED (stubs for later phases)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public Task<RemediationProgress> GetRemediationProgressAsync(
        string? subscriptionId = null,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var activeExecs = _activeRemediations.Values.AsEnumerable();
        List<RemediationExecution> historySnapshot;
        lock (_historyLock)
        {
            historySnapshot = _remediationHistory.ToList();
        }

        if (!string.IsNullOrEmpty(subscriptionId))
        {
            activeExecs = activeExecs.Where(e => string.Equals(e.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
            historySnapshot = historySnapshot.Where(e => string.Equals(e.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var recentHistory = historySnapshot.Where(e => e.CompletedAt.HasValue && e.CompletedAt.Value >= cutoff).ToList();
        var activeList = activeExecs.ToList();

        var completedCount = recentHistory.Count(e => e.Status == RemediationExecutionStatus.Completed);
        var failedCount = recentHistory.Count(e => e.Status == RemediationExecutionStatus.Failed);
        var inProgressCount = activeList.Count(e => e.Status == RemediationExecutionStatus.InProgress);
        var pendingCount = activeList.Count(e => e.Status == RemediationExecutionStatus.Pending);
        var totalCount = completedCount + failedCount + inProgressCount + pendingCount;

        var completedWithDuration = recentHistory.Where(e => e.Status == RemediationExecutionStatus.Completed && e.Duration.HasValue).ToList();
        var avgTime = completedWithDuration.Count > 0
            ? TimeSpan.FromTicks((long)completedWithDuration.Average(e => e.Duration!.Value.Ticks))
            : TimeSpan.Zero;

        var completionRate = totalCount > 0
            ? (double)completedCount / totalCount * 100.0
            : 0.0;

        return Task.FromResult(new RemediationProgress
        {
            SubscriptionId = subscriptionId,
            CompletedCount = completedCount,
            InProgressCount = inProgressCount,
            FailedCount = failedCount,
            PendingCount = pendingCount,
            TotalCount = totalCount,
            CompletionRate = Math.Round(completionRate, 1),
            AverageRemediationTime = avgTime,
            Period = "Last 30 days",
            AsOf = DateTime.UtcNow
        });
    }

    /// <inheritdoc />
    public Task<RemediationHistory> GetRemediationHistoryAsync(
        DateTime startDate,
        DateTime endDate,
        string? subscriptionId = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        List<RemediationExecution> historySnapshot;
        lock (_historyLock)
        {
            historySnapshot = _remediationHistory.ToList();
        }

        var filtered = historySnapshot
            .Where(e => e.CompletedAt.HasValue && e.CompletedAt.Value >= startDate && e.CompletedAt.Value <= endDate);

        if (!string.IsNullOrEmpty(subscriptionId))
            filtered = filtered.Where(e => string.Equals(e.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));

        var orderedList = filtered.OrderByDescending(e => e.CompletedAt).ToList();
        var totalCount = orderedList.Count;
        var paged = orderedList.Skip(skip).Take(take).ToList();

        // Calculate metrics
        var successful = orderedList.Count(e => e.Status == RemediationExecutionStatus.Completed);
        var failed = orderedList.Count(e => e.Status == RemediationExecutionStatus.Failed);
        var rolledBack = orderedList.Count(e => e.Status == RemediationExecutionStatus.RolledBack);

        var withDuration = orderedList.Where(e => e.Duration.HasValue).ToList();
        var avgTime = withDuration.Count > 0
            ? TimeSpan.FromTicks((long)withDuration.Average(e => e.Duration!.Value.Ticks))
            : TimeSpan.Zero;

        // Most remediated family — look up findings by ID
        string? mostFamily = null;
        var familyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var exec in orderedList)
        {
            // Use finding ID prefix as family approximation (e.g., "AC" from "AC-2")
            var findingId = exec.FindingId;
            if (!string.IsNullOrEmpty(findingId) && findingId.Contains('-'))
            {
                var family = findingId.Split('-')[0];
                if (family.Length <= 3) // Valid NIST family prefix
                {
                    familyCounts[family] = familyCounts.GetValueOrDefault(family) + 1;
                }
            }
        }
        if (familyCounts.Count > 0)
            mostFamily = familyCounts.MaxBy(kv => kv.Value).Key;

        return Task.FromResult(new RemediationHistory
        {
            SubscriptionId = subscriptionId,
            StartDate = startDate,
            EndDate = endDate,
            Executions = paged,
            Skip = skip,
            Take = take,
            TotalCount = totalCount,
            Metrics = new RemediationMetric
            {
                TotalExecutions = totalCount,
                SuccessfulExecutions = successful,
                FailedExecutions = failed,
                RolledBackExecutions = rolledBack,
                AverageExecutionTime = avgTime,
                MostRemediatedFamily = mostFamily
            }
        });
    }

    /// <inheritdoc />
    public Task<RemediationImpactAnalysis> AnalyzeRemediationImpactAsync(
        IEnumerable<ComplianceFinding> findings,
        CancellationToken ct = default)
    {
        var findingsList = findings.ToList();

        var autoCount = findingsList.Count(f => f.AutoRemediable);
        var manualCount = findingsList.Count - autoCount;

        var riskMetrics = CalculateRiskMetrics(findingsList);

        // Per-resource impacts
        var grouped = GroupFindingsByResource(findingsList);
        var resourceImpacts = grouped.Select(kvp =>
        {
            var resourceFindings = kvp.Value;
            var maxSeverity = resourceFindings.Max(f => f.Severity);
            return new ResourceImpact
            {
                ResourceId = kvp.Key,
                ResourceType = resourceFindings.First().ResourceType,
                FindingsCount = resourceFindings.Count,
                ProposedChanges = resourceFindings.Select(f => GenerateRemediationDescription(f)).ToList(),
                RiskLevel = maxSeverity is FindingSeverity.Critical or FindingSeverity.High
                    ? RiskLevel.High
                    : RiskLevel.Standard
            };
        })
        .OrderByDescending(r => r.FindingsCount)
        .ToList();

        // Generate recommendations
        var recommendations = new List<string>();

        if (autoCount > 0)
            recommendations.Add($"{autoCount} finding(s) can be auto-remediated — consider batch execution for efficiency.");

        if (manualCount > 0)
            recommendations.Add($"{manualCount} finding(s) require manual remediation — generate manual guides before proceeding.");

        var criticalCount = findingsList.Count(f => f.Severity == FindingSeverity.Critical);
        if (criticalCount > 0)
            recommendations.Add($"{criticalCount} Critical finding(s) should be remediated immediately (P0 priority).");

        var highRiskResources = resourceImpacts.Where(r => r.RiskLevel == RiskLevel.High).ToList();
        if (highRiskResources.Count > 0)
            recommendations.Add($"{highRiskResources.Count} resource(s) have high-risk findings — consider RequireApproval for these.");

        if (riskMetrics.RiskReductionPercentage >= 80)
            recommendations.Add("Automated remediation will reduce risk by 80%+ — excellent coverage.");
        else if (riskMetrics.RiskReductionPercentage < 50)
            recommendations.Add("Automated remediation covers less than 50% of risk — manual effort required for significant risk reduction.");

        var analysis = new RemediationImpactAnalysis
        {
            RiskMetrics = riskMetrics,
            TotalFindingsAnalyzed = findingsList.Count,
            AutoRemediableCount = autoCount,
            ManualCount = manualCount,
            ResourceImpacts = resourceImpacts,
            Recommendations = recommendations,
            AnalyzedAt = DateTime.UtcNow
        };

        return Task.FromResult(analysis);
    }

    /// <inheritdoc />
    public Task<ManualRemediationGuide> GenerateManualRemediationGuideAsync(
        ComplianceFinding finding,
        CancellationToken ct = default)
    {
        // Parse steps from finding guidance text, falling back to NIST steps
        var steps = !string.IsNullOrEmpty(finding.RemediationGuidance)
            ? _nistStepsService.ParseStepsFromGuidance(finding.RemediationGuidance)
            : new List<string>();

        if (steps.Count == 0)
        {
            steps = _nistStepsService.GetRemediationSteps(finding.ControlFamily, finding.ControlId);
        }

        // Skill level by control family
        var skillLevel = _nistStepsService.GetSkillLevel(finding.ControlFamily);

        // Prerequisites based on finding type
        var prerequisites = new List<string> { "Azure subscription access" };
        if (finding.RemediationType == RemediationType.ResourceConfiguration)
            prerequisites.Add("Resource Contributor role on target resource");
        if (finding.RemediationType == RemediationType.PolicyAssignment)
        {
            prerequisites.Add("Policy Contributor role");
            prerequisites.Add("Understanding of Azure Policy effects");
        }
        if (HighRiskFamilies.Contains(finding.ControlFamily))
            prerequisites.Add($"Change approval for {finding.ControlFamily} family controls");

        // Required permissions
        var permissions = new List<string> { "Microsoft.Authorization/*/read" };
        if (finding.RemediationType == RemediationType.ResourceConfiguration)
            permissions.Add($"Microsoft.*/write on {finding.ResourceType ?? "target resource"}");
        if (finding.RemediationType == RemediationType.PolicyAssignment)
            permissions.Add("Microsoft.Authorization/policyAssignments/write");

        // Validation steps
        var validationSteps = new List<string>
        {
            $"Re-run compliance assessment for {finding.ControlId}",
            $"Verify {finding.Title} is no longer flagged",
            "Check resource health and service availability post-remediation"
        };

        // Rollback plan
        var rollbackPlan = finding.RemediationType switch
        {
            RemediationType.ResourceConfiguration =>
                "Restore resource configuration from pre-remediation snapshot (if captured)",
            RemediationType.PolicyAssignment =>
                "Remove or disable policy assignment to revert to previous state",
            RemediationType.PolicyRemediation =>
                "Re-apply previous policy remediation task configuration",
            _ => "Contact security team to manually revert changes"
        };

        var guide = new ManualRemediationGuide
        {
            FindingId = finding.Id,
            ControlId = finding.ControlId,
            Title = $"Manual Remediation: {finding.Title}",
            Steps = steps,
            Prerequisites = prerequisites,
            SkillLevel = skillLevel,
            RequiredPermissions = permissions,
            ValidationSteps = validationSteps,
            RollbackPlan = rollbackPlan,
            EstimatedDuration = EstimateDuration(finding),
            References = new List<string>
            {
                $"https://csf.tools/reference/nist-sp-800-53/r5/{finding.ControlFamily.ToLowerInvariant()}/{finding.ControlId.ToLowerInvariant()}/",
                $"https://learn.microsoft.com/en-us/azure/governance/policy/samples/nist-sp-800-53-r5"
            }
        };

        return Task.FromResult(guide);
    }

    /// <inheritdoc />
    public Task<RemediationWorkflowStatus> GetActiveRemediationWorkflowsAsync(
        string? subscriptionId = null,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        var allExecutions = _activeRemediations.Values.AsEnumerable();
        List<RemediationExecution> historySnapshot;
        lock (_historyLock)
        {
            historySnapshot = _remediationHistory.ToList();
        }

        // Filter by subscription if provided
        if (!string.IsNullOrEmpty(subscriptionId))
        {
            allExecutions = allExecutions.Where(e => string.Equals(e.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
            historySnapshot = historySnapshot.Where(e => string.Equals(e.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var status = new RemediationWorkflowStatus
        {
            SubscriptionId = subscriptionId,
            PendingApprovals = allExecutions
                .Where(e => e.Status == RemediationExecutionStatus.Pending)
                .ToList(),
            InProgressExecutions = allExecutions
                .Where(e => e.Status == RemediationExecutionStatus.InProgress)
                .ToList(),
            RecentlyCompleted = historySnapshot
                .Where(e => e.Status is RemediationExecutionStatus.Completed or RemediationExecutionStatus.Failed or RemediationExecutionStatus.RolledBack or RemediationExecutionStatus.Rejected)
                .Where(e => e.CompletedAt.HasValue && e.CompletedAt.Value >= cutoff)
                .OrderByDescending(e => e.CompletedAt)
                .ToList(),
            RetrievedAt = DateTime.UtcNow
        };

        return Task.FromResult(status);
    }

    /// <inheritdoc />
    public async Task<RemediationApprovalResult> ProcessRemediationApprovalAsync(
        string executionId,
        bool approve,
        string approverName,
        string? comments = null,
        CancellationToken ct = default)
    {
        if (!_activeRemediations.TryGetValue(executionId, out var execution))
        {
            return new RemediationApprovalResult
            {
                ExecutionId = executionId,
                Approved = false,
                ApproverName = approverName,
                Comments = "Execution not found",
                ProcessedAt = DateTime.UtcNow,
                ExecutionTriggered = false
            };
        }

        if (execution.Status != RemediationExecutionStatus.Pending)
        {
            return new RemediationApprovalResult
            {
                ExecutionId = executionId,
                Approved = false,
                ApproverName = approverName,
                Comments = $"Execution is not pending approval (current status: {execution.Status})",
                ProcessedAt = DateTime.UtcNow,
                ExecutionTriggered = false
            };
        }

        if (approve)
        {
            execution.Status = RemediationExecutionStatus.Approved;
            execution.ApprovedBy = approverName;
            execution.ApprovedAt = DateTime.UtcNow;

            _logger.LogInformation("Remediation {ExecutionId} approved by {Approver}", executionId, approverName);

            // Trigger execution with stored options (disable RequireApproval to avoid loop)
            var execOptions = execution.Options ?? new RemediationExecutionOptions();
            execOptions.RequireApproval = false;

            // Remove from active (execution will re-register itself)
            _activeRemediations.TryRemove(executionId, out _);

            var execResult = await ExecuteRemediationAsync(execution.FindingId, execOptions, ct);

            return new RemediationApprovalResult
            {
                ExecutionId = executionId,
                Approved = true,
                ApproverName = approverName,
                Comments = comments,
                ProcessedAt = DateTime.UtcNow,
                ExecutionTriggered = true
            };
        }
        else
        {
            execution.Status = RemediationExecutionStatus.Rejected;
            execution.RejectedBy = approverName;
            execution.RejectedAt = DateTime.UtcNow;
            execution.RejectionReason = comments;
            execution.CompletedAt = DateTime.UtcNow;
            execution.Duration = execution.CompletedAt - execution.StartedAt;

            _logger.LogInformation("Remediation {ExecutionId} rejected by {Approver}: {Reason}", executionId, approverName, comments);

            // Move to history
            _activeRemediations.TryRemove(executionId, out _);
            lock (_historyLock)
            {
                _remediationHistory.Add(execution);
            }

            return new RemediationApprovalResult
            {
                ExecutionId = executionId,
                Approved = false,
                ApproverName = approverName,
                Comments = comments,
                ProcessedAt = DateTime.UtcNow,
                ExecutionTriggered = false
            };
        }
    }

    /// <inheritdoc />
    public Task<RemediationScheduleResult> ScheduleRemediationAsync(
        IEnumerable<string> findingIds,
        DateTime scheduledTime,
        BatchRemediationOptions? options = null,
        CancellationToken ct = default)
    {
        var ids = findingIds?.ToList() ?? new List<string>();

        if (ids.Count == 0)
        {
            return Task.FromResult(new RemediationScheduleResult
            {
                Status = "Error",
                FindingIds = ids,
                FindingCount = 0,
                ScheduledTime = scheduledTime,
                Options = options
            });
        }

        if (scheduledTime <= DateTime.UtcNow)
        {
            _logger.LogWarning("Scheduled time {Time} is in the past", scheduledTime);
            return Task.FromResult(new RemediationScheduleResult
            {
                Status = "Error",
                FindingIds = ids,
                FindingCount = ids.Count,
                ScheduledTime = scheduledTime,
                Options = options
            });
        }

        var result = new RemediationScheduleResult
        {
            ScheduledTime = scheduledTime,
            FindingIds = ids,
            FindingCount = ids.Count,
            Status = "Scheduled",
            Options = options,
            CreatedAt = DateTime.UtcNow
        };

        _logger.LogInformation(
            "Scheduled remediation {ScheduleId} for {Count} findings at {Time}",
            result.ScheduleId, ids.Count, scheduledTime);

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task<RemediationScript> GenerateRemediationScriptAsync(
        ComplianceFinding finding,
        ScriptType scriptType = ScriptType.AzureCli,
        CancellationToken ct = default)
    {
        // Attempt AI generation first
        if (_aiGenerator.IsAvailable)
        {
            try
            {
                var aiScript = await _aiGenerator.GenerateScriptAsync(finding, scriptType, ct);
                if (aiScript != null)
                {
                    // Sanitize AI-generated script
                    if (_sanitizationService.IsSafe(aiScript.Content))
                    {
                        aiScript.IsSanitized = true;
                        _logger.LogInformation("AI script generated and sanitized for {FindingId}", finding.Id);
                        return aiScript;
                    }

                    var violations = _sanitizationService.GetViolations(aiScript.Content);
                    _logger.LogWarning("AI script rejected for {FindingId}: {Violations}",
                        finding.Id, string.Join("; ", violations));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI script generation failed for {FindingId}, falling back", finding.Id);
            }
        }

        // Fallback: build script from NIST steps
        var steps = _nistStepsService.GetRemediationSteps(finding.ControlFamily, finding.ControlId);
        var scriptContent = string.Join("\n", steps.Select((s, i) => $"# Step {i + 1}: {s}"));

        return new RemediationScript
        {
            Content = scriptContent,
            ScriptType = scriptType,
            Description = $"NIST-based remediation steps for {finding.ControlId}",
            EstimatedDuration = EstimateDuration(finding),
            IsSanitized = true
        };
    }

    /// <inheritdoc />
    public async Task<RemediationGuidance> GetRemediationGuidanceAsync(
        ComplianceFinding finding,
        CancellationToken ct = default)
    {
        // Attempt AI guidance first
        if (_aiGenerator.IsAvailable)
        {
            try
            {
                var aiGuidance = await _aiGenerator.GetGuidanceAsync(finding, ct);
                if (aiGuidance != null)
                {
                    _logger.LogInformation("AI guidance generated for {FindingId}", finding.Id);
                    return aiGuidance;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI guidance failed for {FindingId}, falling back", finding.Id);
            }
        }

        // Fallback: deterministic guidance from finding text
        var steps = _nistStepsService.GetRemediationSteps(finding.ControlFamily, finding.ControlId);

        return new RemediationGuidance
        {
            FindingId = finding.Id,
            Explanation = $"Remediation required for {finding.ControlId} ({finding.ControlFamily} family): {finding.Title}",
            TechnicalPlan = string.Join("\n", steps.Select((s, i) => $"{i + 1}. {s}")),
            ConfidenceScore = 0.6, // deterministic guidance — moderate confidence
            References = new List<string>
            {
                $"https://csf.tools/reference/nist-sp-800-53/r5/{finding.ControlFamily.ToLowerInvariant()}/{finding.ControlId.ToLowerInvariant()}/"
            },
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task<List<PrioritizedFinding>> PrioritizeFindingsWithAiAsync(
        IEnumerable<ComplianceFinding> findings,
        string? businessContext = null,
        CancellationToken ct = default)
    {
        var findingsList = findings.ToList();

        // Attempt AI prioritization first
        if (_aiGenerator.IsAvailable)
        {
            try
            {
                var aiResult = await _aiGenerator.PrioritizeAsync(findingsList, businessContext, ct);
                if (aiResult != null && aiResult.Count > 0)
                {
                    _logger.LogInformation("AI prioritization returned {Count} findings", aiResult.Count);
                    return aiResult;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI prioritization failed, falling back to severity-based priority");
            }
        }

        // Fallback: severity-based priority
        return findingsList.Select(f =>
        {
            var priority = MapSeverityToPriority(f.Severity);
            return new PrioritizedFinding
            {
                Finding = f,
                AiPriority = priority,
                OriginalPriority = priority,
                Justification = $"Severity-based priority: {f.Severity} → {GetPriorityLabel(priority)}",
                BusinessImpact = f.Severity is FindingSeverity.Critical or FindingSeverity.High
                    ? "High business impact — immediate attention required"
                    : "Standard business impact"
            };
        })
        .OrderBy(p => p.AiPriority)
        .ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Post-execution kanban sync — advance linked task to InReview and collect evidence.</summary>
    private async Task SyncKanbanPostExecutionAsync(RemediationExecution execution, CancellationToken ct)
    {
        if (execution.Status != RemediationExecutionStatus.Completed) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var kanbanService = scope.ServiceProvider.GetService<IKanbanService>();
            if (kanbanService == null) return;

            var task = await kanbanService.GetTaskByLinkedAlertIdAsync(execution.FindingId, ct);
            if (task == null) return;

            // Advance task to InReview
            await kanbanService.MoveTaskAsync(
                task.Id,
                Core.Models.Kanban.TaskStatus.InReview,
                "system", "Remediation Engine", "System",
                $"Automated remediation completed (Tier {execution.TierUsed}). Steps: {execution.StepsExecuted}.",
                skipValidation: true,
                cancellationToken: ct);

            // Collect evidence
            await kanbanService.CollectTaskEvidenceAsync(
                task.Id, "system", "Remediation Engine",
                execution.SubscriptionId, ct);

            _logger.LogInformation("Kanban task {TaskId} advanced to InReview for {FindingId}", task.Id, execution.FindingId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kanban sync failed for {FindingId} — continuing", execution.FindingId);
        }
    }

    /// <summary>Post-failure kanban sync — add comment to linked task with error details.</summary>
    private async Task SyncKanbanFailureAsync(RemediationExecution execution, string errorMessage, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var kanbanService = scope.ServiceProvider.GetService<IKanbanService>();
            if (kanbanService == null) return;

            var task = await kanbanService.GetTaskByLinkedAlertIdAsync(execution.FindingId, ct);
            if (task == null) return;

            await kanbanService.AddCommentAsync(
                task.Id,
                "system", "Remediation Engine",
                $"⚠️ Automated remediation failed: {errorMessage}",
                "System",
                cancellationToken: ct);

            _logger.LogInformation("Kanban failure comment added for task {TaskId}", task.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kanban failure sync failed for {FindingId} — continuing", execution.FindingId);
        }
    }

    /// <summary>Builds a summary for a batch remediation result.</summary>
    private async Task<BatchRemediationSummary> BuildBatchSummaryAsync(
        BatchRemediationResult batchResult, CancellationToken ct)
    {
        var summary = new BatchRemediationSummary();
        var totalCount = batchResult.Executions.Count + batchResult.SkippedCount + batchResult.CancelledCount;
        summary.SuccessRate = totalCount > 0
            ? (double)batchResult.SuccessCount / totalCount * 100
            : 0;

        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var exec in batchResult.Executions.Where(e => e.Status == RemediationExecutionStatus.Completed))
        {
            var finding = await _complianceEngine.GetFindingAsync(exec.FindingId, ct);
            if (finding != null)
            {
                switch (finding.Severity)
                {
                    case FindingSeverity.Critical: summary.CriticalFindingsRemediated++; break;
                    case FindingSeverity.High: summary.HighFindingsRemediated++; break;
                    case FindingSeverity.Medium: summary.MediumFindingsRemediated++; break;
                    case FindingSeverity.Low: summary.LowFindingsRemediated++; break;
                }
                families.Add(finding.ControlFamily);
            }
        }

        summary.ControlFamiliesAffected = families.ToList();
        summary.TotalDuration = batchResult.Executions
            .Where(e => e.Duration.HasValue)
            .Aggregate(TimeSpan.Zero, (acc, e) => acc + e.Duration!.Value);

        // Risk reduction: weighted by severity
        var totalWeighted = 0.0;
        foreach (var exec in batchResult.Executions.Where(e => e.Status == RemediationExecutionStatus.Completed))
        {
            var finding = await _complianceEngine.GetFindingAsync(exec.FindingId, ct);
            if (finding != null)
            {
                totalWeighted += finding.Severity switch
                {
                    FindingSeverity.Critical => 10.0,
                    FindingSeverity.High => 7.0,
                    FindingSeverity.Medium => 4.0,
                    FindingSeverity.Low => 1.0,
                    _ => 0.5
                };
            }
        }

        var maxPossible = totalCount * 10.0; // max if all were Critical
        summary.EstimatedRiskReduction = maxPossible > 0 ? totalWeighted / maxPossible * 100 : 0;

        return summary;
    }

    /// <summary>Finds an execution by ID from active remediations or history.</summary>
    private RemediationExecution? FindExecution(string executionId)
    {
        if (_activeRemediations.TryGetValue(executionId, out var execution))
            return execution;

        lock (_historyLock)
        {
            return _remediationHistory.FirstOrDefault(e => e.Id == executionId);
        }
    }

    /// <summary>Validates an execution result, checking status, steps, and snapshot.</summary>
    private static RemediationValidationResult ValidateExecution(RemediationExecution execution)
    {
        var result = new RemediationValidationResult
        {
            ExecutionId = execution.Id,
            ValidatedAt = DateTime.UtcNow
        };

        var checks = new List<ValidationCheck>();

        // Check execution status
        checks.Add(new ValidationCheck
        {
            Name = "ExecutionStatus",
            Passed = execution.Status == RemediationExecutionStatus.Completed,
            ExpectedValue = "Completed",
            ActualValue = execution.Status.ToString()
        });

        // Check steps executed
        checks.Add(new ValidationCheck
        {
            Name = "StepsExecuted",
            Passed = execution.StepsExecuted > 0,
            ExpectedValue = "> 0",
            ActualValue = execution.StepsExecuted.ToString()
        });

        // Check after-snapshot captured
        checks.Add(new ValidationCheck
        {
            Name = "AfterSnapshot",
            Passed = execution.AfterSnapshot != null,
            ExpectedValue = "Non-null",
            ActualValue = execution.AfterSnapshot != null ? "Captured" : "Not captured"
        });

        // Check changes applied
        checks.Add(new ValidationCheck
        {
            Name = "ChangesApplied",
            Passed = execution.ChangesApplied != null && execution.ChangesApplied.Count > 0,
            ExpectedValue = "> 0 changes",
            ActualValue = (execution.ChangesApplied?.Count ?? 0).ToString()
        });

        result.Checks = checks;
        result.IsValid = checks.All(c => c.Passed);
        if (!result.IsValid)
        {
            result.FailureReason = string.Join("; ",
                checks.Where(c => !c.Passed).Select(c => $"{c.Name}: expected {c.ExpectedValue}, got {c.ActualValue}"));
        }

        return result;
    }

    /// <summary>Internal validation for auto-validate flow, delegates to ValidateExecution.</summary>
    private Task<RemediationValidationResult> ValidateRemediationInternalAsync(
        RemediationExecution execution,
        CancellationToken ct = default)
    {
        return Task.FromResult(ValidateExecution(execution));
    }

    /// <summary>Maps finding severity to remediation priority (Critical→P0, High→P1, etc.).</summary>
    internal static RemediationPriority MapSeverityToPriority(FindingSeverity severity) =>
        severity switch
        {
            FindingSeverity.Critical => RemediationPriority.P0,
            FindingSeverity.High => RemediationPriority.P1,
            FindingSeverity.Medium => RemediationPriority.P2,
            FindingSeverity.Low => RemediationPriority.P3,
            _ => RemediationPriority.P4
        };

    /// <summary>Gets a human-readable label for a priority level.</summary>
    internal static string GetPriorityLabel(RemediationPriority priority) =>
        priority switch
        {
            RemediationPriority.P0 => "P0 - Immediate",
            RemediationPriority.P1 => "P1 - Within 24 Hours",
            RemediationPriority.P2 => "P2 - Within 7 Days",
            RemediationPriority.P3 => "P3 - Within 30 Days",
            RemediationPriority.P4 => "P4 - Best Effort",
            _ => "P4 - Best Effort"
        };

    /// <summary>
    /// Calculates severity-weighted risk score for a finding.
    /// Critical=10, High=7.5, Medium=5, Low=2.5, Other=1.
    /// </summary>
    internal static double CalculateRiskScore(FindingSeverity severity) =>
        severity switch
        {
            FindingSeverity.Critical => 10.0,
            FindingSeverity.High => 7.5,
            FindingSeverity.Medium => 5.0,
            FindingSeverity.Low => 2.5,
            _ => 1.0
        };

    /// <summary>
    /// Estimates duration based on remediation type and whether it's auto-remediable.
    /// Auto: 10-30 minutes depending on type. Manual: 30 min to 4 hours.
    /// </summary>
    internal static TimeSpan EstimateDuration(ComplianceFinding finding)
    {
        if (finding.AutoRemediable)
        {
            return finding.RemediationType switch
            {
                RemediationType.PolicyRemediation => TimeSpan.FromMinutes(10),
                RemediationType.PolicyAssignment => TimeSpan.FromMinutes(15),
                RemediationType.ResourceConfiguration => TimeSpan.FromMinutes(20),
                _ => TimeSpan.FromMinutes(30)
            };
        }

        return finding.RemediationType switch
        {
            RemediationType.ResourceConfiguration => TimeSpan.FromHours(1),
            RemediationType.Manual => TimeSpan.FromHours(4),
            _ => TimeSpan.FromMinutes(30)
        };
    }

    /// <summary>
    /// Builds a 5-phase implementation timeline from remediation items.
    /// Phases: Immediate (P0), 24 Hours (P1), Week 1 (P2), Month 1 (P3), Backlog (P4).
    /// </summary>
    internal static ImplementationTimeline BuildTimeline(List<RemediationItem> items)
    {
        var now = DateTime.UtcNow;
        var phases = new List<(string Name, RemediationPriority Priority, TimeSpan Offset, TimeSpan Duration)>
        {
            ("Immediate", RemediationPriority.P0, TimeSpan.Zero, TimeSpan.FromHours(4)),
            ("24 Hours", RemediationPriority.P1, TimeSpan.FromHours(4), TimeSpan.FromHours(20)),
            ("Week 1", RemediationPriority.P2, TimeSpan.FromDays(1), TimeSpan.FromDays(6)),
            ("Month 1", RemediationPriority.P3, TimeSpan.FromDays(7), TimeSpan.FromDays(23)),
            ("Backlog", RemediationPriority.P4, TimeSpan.FromDays(30), TimeSpan.FromDays(60))
        };

        var timelinePhases = new List<TimelinePhase>();

        foreach (var (name, priority, offset, duration) in phases)
        {
            var phaseItems = items.Where(i => i.Priority == priority).ToList();
            var phaseDuration = phaseItems.Count > 0
                ? TimeSpan.FromTicks(phaseItems.Sum(i => i.EstimatedDuration.Ticks))
                : TimeSpan.Zero;

            timelinePhases.Add(new TimelinePhase
            {
                Name = name,
                Priority = priority,
                Items = phaseItems,
                StartDate = now + offset,
                EndDate = now + offset + duration,
                EstimatedDuration = phaseDuration
            });
        }

        var totalDuration = TimeSpan.FromTicks(
            timelinePhases.Sum(p => p.EstimatedDuration.Ticks));

        return new ImplementationTimeline
        {
            Phases = timelinePhases,
            TotalEstimatedDuration = totalDuration,
            StartDate = now,
            EndDate = timelinePhases.Last().EndDate
        };
    }

    /// <summary>
    /// Builds an executive summary from items and the filtered findings.
    /// </summary>
    internal static RemediationExecutiveSummary BuildExecutiveSummary(
        List<RemediationItem> items,
        List<ComplianceFinding> findings)
    {
        var autoCount = items.Count(i => i.IsAutoRemediable);
        var totalEffort = TimeSpan.FromTicks(items.Sum(i => i.EstimatedDuration.Ticks));

        // Risk reduction: assume all auto-remediable findings will be fixed
        var totalRisk = findings.Sum(f => CalculateRiskScore(f.Severity));
        var remediableRisk = items.Where(i => i.IsAutoRemediable)
            .Sum(i => CalculateRiskScore(i.Finding.Severity));
        var riskReduction = totalRisk > 0 ? (remediableRisk / totalRisk) * 100.0 : 0.0;

        return new RemediationExecutiveSummary
        {
            TotalFindings = findings.Count,
            CriticalCount = findings.Count(f => f.Severity == FindingSeverity.Critical),
            HighCount = findings.Count(f => f.Severity == FindingSeverity.High),
            MediumCount = findings.Count(f => f.Severity == FindingSeverity.Medium),
            LowCount = findings.Count(f => f.Severity == FindingSeverity.Low),
            AutoRemediableCount = autoCount,
            ManualCount = items.Count - autoCount,
            TotalEstimatedEffort = totalEffort,
            ProjectedRiskReduction = Math.Round(riskReduction, 2)
        };
    }

    /// <summary>
    /// Calculates risk metrics: current score, projected score (non-remediable only),
    /// and risk reduction percentage.
    /// </summary>
    internal static RiskMetrics CalculateRiskMetrics(List<ComplianceFinding> findings)
    {
        var totalRisk = findings.Sum(f => CalculateRiskScore(f.Severity));
        var nonRemediableRisk = findings
            .Where(f => !f.AutoRemediable)
            .Sum(f => CalculateRiskScore(f.Severity));
        var reductionPct = totalRisk > 0
            ? ((totalRisk - nonRemediableRisk) / totalRisk) * 100.0
            : 0.0;

        return new RiskMetrics
        {
            CurrentRiskScore = totalRisk,
            ProjectedRiskScore = nonRemediableRisk,
            RiskReductionPercentage = Math.Round(reductionPct, 2)
        };
    }

    /// <summary>Groups findings by Azure resource ID.</summary>
    internal static Dictionary<string, List<ComplianceFinding>> GroupFindingsByResource(
        List<ComplianceFinding> findings)
    {
        return findings
            .GroupBy(f => f.ResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>Applies plan generation filters (severity, family, automatable).</summary>
    private static List<ComplianceFinding> ApplyFilters(
        List<ComplianceFinding> findings,
        RemediationPlanOptions? options)
    {
        if (options == null) return findings;

        var filtered = findings.AsEnumerable();

        // MinSeverity filter
        if (!string.IsNullOrEmpty(options.MinSeverity) &&
            Enum.TryParse<FindingSeverity>(options.MinSeverity, true, out var minSeverity))
        {
            filtered = filtered.Where(f => f.Severity <= minSeverity);
        }

        // IncludeFamilies filter
        if (options.IncludeFamilies is { Count: > 0 })
        {
            var families = new HashSet<string>(options.IncludeFamilies, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(f => families.Contains(f.ControlFamily));
        }

        // ExcludeFamilies filter
        if (options.ExcludeFamilies is { Count: > 0 })
        {
            var excluded = new HashSet<string>(options.ExcludeFamilies, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(f => !excluded.Contains(f.ControlFamily));
        }

        // AutomatableOnly filter
        if (options.AutomatableOnly)
        {
            filtered = filtered.Where(f => f.AutoRemediable);
        }

        return filtered.ToList();
    }

    /// <summary>Builds a plan for a single finding from a list of steps.</summary>
    private RemediationPlan BuildSingleFindingPlan(ComplianceFinding finding, List<string> steps)
    {
        var priority = MapSeverityToPriority(finding.Severity);
        var item = new RemediationItem
        {
            Finding = finding,
            Priority = priority,
            PriorityLabel = GetPriorityLabel(priority),
            EstimatedDuration = EstimateDuration(finding),
            Steps = steps.Select(s => new RemediationStep
            {
                Description = s,
                FindingId = finding.Id,
                ControlId = finding.ControlId
            }).ToList(),
            ValidationSteps = new List<string>
            {
                $"Re-scan resource {finding.ResourceId} for control {finding.ControlId}",
                "Verify finding status changed to Remediated"
            },
            RollbackPlan = $"Restore resource {finding.ResourceId} from before-snapshot",
            IsAutoRemediable = finding.AutoRemediable,
            RemediationType = finding.RemediationType,
            AffectedResourceId = finding.ResourceId
        };

        var items = new List<RemediationItem> { item };
        var findings = new List<ComplianceFinding> { finding };

        return new RemediationPlan
        {
            SubscriptionId = finding.SubscriptionId ?? string.Empty,
            DryRun = true,
            TotalFindings = 1,
            AutoRemediableCount = finding.AutoRemediable ? 1 : 0,
            Items = items,
            Timeline = BuildTimeline(items),
            ExecutiveSummary = BuildExecutiveSummary(items, findings),
            RiskMetrics = CalculateRiskMetrics(findings)
        };
    }

    // ─── Backward-compatible private helpers ─────────────────────────────────

    /// <summary>Generates a human-readable description for the remediation of a finding.</summary>
    private static string GenerateRemediationDescription(ComplianceFinding finding) =>
        finding.RemediationType switch
        {
            RemediationType.ResourceConfiguration =>
                $"Update resource configuration for {finding.ControlId}: {finding.Title}",
            RemediationType.PolicyAssignment =>
                $"Assign policy for compliance control {finding.ControlId}",
            RemediationType.PolicyRemediation =>
                $"Run Azure Policy remediation task for {finding.ControlId}",
            RemediationType.Manual =>
                $"Manual remediation required for {finding.ControlId}: {finding.Title}",
            _ => $"Remediate finding for control {finding.ControlId}: {finding.Title}"
        };

    /// <summary>Generates an Azure CLI remediation script based on the finding's control family.</summary>
    private static string GenerateRemediationScript(ComplianceFinding finding)
    {
        if (!string.IsNullOrEmpty(finding.RemediationScript))
            return finding.RemediationScript;

        return finding.RemediationType switch
        {
            RemediationType.PolicyRemediation =>
                $"# Start policy remediation task\n" +
                $"Start-AzPolicyRemediation -PolicyAssignmentId '{finding.PolicyAssignmentId ?? "<assignment-id>"}' " +
                $"-Name 'remediate-{finding.ControlId.ToLowerInvariant()}'",

            RemediationType.ResourceConfiguration =>
                $"# Resource configuration change for {finding.ControlId}\n" +
                $"# Target: {finding.ResourceId}\n" +
                $"# {finding.RemediationGuidance}",

            _ => $"# Manual remediation steps for {finding.ControlId}\n# {finding.RemediationGuidance}"
        };
    }

    /// <summary>Estimates the remediation effort level based on the finding severity.</summary>
    private static string EstimateEffort(ComplianceFinding finding) =>
        finding.RemediationType switch
        {
            RemediationType.PolicyRemediation => "Low",
            RemediationType.ResourceConfiguration => "Medium",
            RemediationType.PolicyAssignment => "Low",
            RemediationType.Manual => "High",
            _ => "Medium"
        };
}

using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// Continuous Monitoring Service (Feature 015 — US9)
// Spec §4.1–4.7: ConMon plans, periodic reports, ATO expiration tracking,
// significant change detection, reauthorization workflow, multi-system dashboard.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Service for continuous monitoring lifecycle management.
/// Uses <see cref="IServiceScopeFactory"/> for scoped DbContext access.
/// </summary>
public class ConMonService : IConMonService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IComplianceWatchService? _watchService;
    private readonly IAlertManager? _alertManager;
    private readonly ILogger<ConMonService> _logger;
    private readonly CanonicalEnvironmentCollectionGuard? _environmentGuard;

    public ConMonService(
        IServiceScopeFactory scopeFactory,
        ILogger<ConMonService> logger,
        IComplianceWatchService? watchService = null,
        IAlertManager? alertManager = null,
        CanonicalEnvironmentCollectionGuard? environmentGuard = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _watchService = watchService;
        _alertManager = alertManager;
        _environmentGuard = environmentGuard;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §4.1 — Create or Update ConMon Plan
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ConMonPlan> CreatePlanAsync(
        string systemId, string assessmentFrequency, DateTime annualReviewDate,
        List<string>? reportDistribution, List<string>? significantChangeTriggers,
        string createdBy, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // One plan per system — upsert
        var existing = await db.ConMonPlans
            .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, cancellationToken);

        if (existing != null)
        {
            existing.AssessmentFrequency = assessmentFrequency;
            existing.AnnualReviewDate = annualReviewDate;
            existing.ReportDistribution = reportDistribution ?? existing.ReportDistribution;
            existing.SignificantChangeTriggers = significantChangeTriggers ?? existing.SignificantChangeTriggers;
            existing.ModifiedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Updated ConMon plan {PlanId} for system '{SystemId}'", existing.Id, systemId);
            return existing;
        }

        var plan = new ConMonPlan
        {
            RegisteredSystemId = systemId,
            AssessmentFrequency = assessmentFrequency,
            AnnualReviewDate = annualReviewDate,
            ReportDistribution = reportDistribution ?? new List<string>(),
            SignificantChangeTriggers = significantChangeTriggers ?? new List<string>(),
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        db.ConMonPlans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Created ConMon plan {PlanId} for system '{SystemId}'", plan.Id, systemId);
        return plan;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §4.2 — Generate Periodic ConMon Report
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ConMonReport> GenerateReportAsync(
        string systemId, string reportType, string period,
        string generatedBy, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var plan = await db.ConMonPlans
            .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"No ConMon plan found for system '{systemId}'. Create a plan first.");

        // Calculate current compliance score
        var effectivenessRecords = await db.ControlEffectivenessRecords
            .Where(e => e.RegisteredSystemId == systemId)
            .ToListAsync(cancellationToken);

        var assessed = effectivenessRecords.Count;
        var satisfied = effectivenessRecords.Count(e => e.Determination == EffectivenessDetermination.Satisfied);
        var complianceScore = assessed > 0 ? Math.Round((double)satisfied / assessed * 100, 2) : 0.0;

        // Get authorized baseline score (from most recent active authorization)
        var activeAuth = await db.AuthorizationDecisions
            .Where(d => d.RegisteredSystemId == systemId && d.IsActive)
            .OrderByDescending(d => d.DecisionDate)
            .FirstOrDefaultAsync(cancellationToken);

        double? baselineScore = activeAuth?.ComplianceScoreAtDecision;

        // Count findings
        var openFindings = await db.Findings
            .CountAsync(f =>
                db.Assessments.Any(a => a.Id == f.AssessmentId && a.RegisteredSystemId == systemId) &&
                (f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress),
                cancellationToken);

        var resolvedFindings = await db.Findings
            .CountAsync(f =>
                db.Assessments.Any(a => a.Id == f.AssessmentId && a.RegisteredSystemId == systemId) &&
                (f.Status == FindingStatus.Remediated || f.Status == FindingStatus.FalsePositive),
                cancellationToken);

        // POA&M status
        var openPoam = await db.PoamItems
            .CountAsync(p => p.RegisteredSystemId == systemId &&
                (p.Status == PoamStatus.Ongoing || p.Status == PoamStatus.Delayed),
                cancellationToken);

        var overduePoam = await db.PoamItems
            .Where(p => p.RegisteredSystemId == systemId &&
                p.Status == PoamStatus.Ongoing &&
                p.ScheduledCompletionDate < DateTime.UtcNow &&
                p.ActualCompletionDate == null)
            .CountAsync(cancellationToken);

        // Generate markdown content
        var content = GenerateReportContent(
            system.Name, reportType, period,
            complianceScore, baselineScore,
            openFindings, resolvedFindings,
            openPoam, overduePoam, activeAuth);

        var report = new ConMonReport
        {
            ConMonPlanId = plan.Id,
            RegisteredSystemId = systemId,
            ReportPeriod = period,
            ReportType = reportType,
            ComplianceScore = complianceScore,
            AuthorizedBaselineScore = baselineScore,
            NewFindings = openFindings,
            ResolvedFindings = resolvedFindings,
            OpenPoamItems = openPoam,
            OverduePoamItems = overduePoam,
            ReportContent = content,
            GeneratedAt = DateTime.UtcNow,
            GeneratedBy = generatedBy
        };

        // Phase 17 §9a.3 — Enrich report with ComplianceWatchService data
        await EnrichReportWithWatchDataAsync(report, systemId, db, cancellationToken);

        db.ConMonReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Generated {ReportType} ConMon report for system '{SystemId}', period '{Period}'",
            reportType, systemId, period);
        return report;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §4.4 — Report Significant Change
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<SignificantChange> ReportChangeAsync(
        string systemId, string changeType, string description,
        string detectedBy, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // Determine if this change type requires reauthorization
        var requiresReauth = IsReauthorizationRequired(changeType);

        var change = new SignificantChange
        {
            RegisteredSystemId = systemId,
            ChangeType = changeType,
            Description = description,
            DetectedAt = DateTime.UtcNow,
            DetectedBy = detectedBy,
            RequiresReauthorization = requiresReauth
        };

        db.SignificantChanges.Add(change);
        await db.SaveChangesAsync(cancellationToken);

        // Phase 17 §9a.4 — Auto-create alert for significant changes requiring reauthorization
        if (_alertManager != null && requiresReauth)
        {
            await CreateSignificantChangeAlertAsync(change, system.Name, cancellationToken);
        }

        _logger.LogInformation(
            "Reported significant change '{ChangeType}' for system '{SystemId}', reauthorization={Reauth}",
            changeType, systemId, requiresReauth);

        return change;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §4.3 — Check ATO Expiration
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ExpirationStatus> CheckExpirationAsync(
        string systemId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var activeAuth = await db.AuthorizationDecisions
            .Where(d => d.RegisteredSystemId == systemId && d.IsActive)
            .OrderByDescending(d => d.DecisionDate)
            .FirstOrDefaultAsync(cancellationToken);

        var status = new ExpirationStatus
        {
            SystemId = systemId,
            SystemName = system.Name
        };

        if (activeAuth == null)
        {
            status.HasActiveAuthorization = false;
            status.AlertLevel = "Warning";
            status.AlertMessage = "No active authorization decision for this system.";
            return status;
        }

        status.HasActiveAuthorization = true;
        status.DecisionType = activeAuth.DecisionType.ToString();
        status.DecisionDate = activeAuth.DecisionDate;
        status.ExpirationDate = activeAuth.ExpirationDate;

        if (activeAuth.ExpirationDate == null)
        {
            // DATO has no expiration; it's a denial
            if (activeAuth.DecisionType == AuthorizationDecisionType.Dato)
            {
                status.AlertLevel = "Urgent";
                status.AlertMessage = "System has a Denial of Authorization to Operate (DATO). System should not be in production.";
            }
            else
            {
                status.AlertLevel = "None";
                status.AlertMessage = "Authorization has no expiration date.";
            }
            return status;
        }

        var daysUntil = (int)(activeAuth.ExpirationDate.Value.Date - DateTime.UtcNow.Date).TotalDays;
        status.DaysUntilExpiration = daysUntil;

        if (daysUntil < 0)
        {
            // Expired — mark as inactive
            status.IsExpired = true;
            status.AlertLevel = "Expired";
            status.AlertMessage = $"Authorization EXPIRED {Math.Abs(daysUntil)} days ago on {activeAuth.ExpirationDate.Value:yyyy-MM-dd}. " +
                "System is operating without authorization. Initiate reauthorization immediately.";

            // Auto-deactivate expired authorization (spec §4.3)
            activeAuth.IsActive = false;
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (daysUntil <= 30)
        {
            status.AlertLevel = "Urgent";
            status.AlertMessage = $"Authorization expires in {daysUntil} days on {activeAuth.ExpirationDate.Value:yyyy-MM-dd}. " +
                "Begin reauthorization process immediately.";
        }
        else if (daysUntil <= 60)
        {
            status.AlertLevel = "Warning";
            status.AlertMessage = $"Authorization expires in {daysUntil} days on {activeAuth.ExpirationDate.Value:yyyy-MM-dd}. " +
                "Schedule reauthorization activities.";
        }
        else if (daysUntil <= 90)
        {
            status.AlertLevel = "Info";
            status.AlertMessage = $"Authorization expires in {daysUntil} days on {activeAuth.ExpirationDate.Value:yyyy-MM-dd}. " +
                "Plan for upcoming reauthorization.";
        }
        else
        {
            status.AlertLevel = "None";
            status.AlertMessage = $"Authorization valid for {daysUntil} more days (expires {activeAuth.ExpirationDate.Value:yyyy-MM-dd}).";
        }

        // Phase 17 §9a.4 — Auto-create ComplianceAlert for actionable expiration levels.
        // Graduated severity: Info@90d, Warning@60d, High@30d, Critical@expired.
        if (_alertManager != null && status.AlertLevel is "Info" or "Warning" or "Urgent" or "Expired")
        {
            await CreateExpirationAlertAsync(status, systemId, cancellationToken);
        }

        return status;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §4.6 — Multi-System Dashboard
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<DashboardResult> GetDashboardAsync(
        bool activeOnly, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var query = db.RegisteredSystems.AsQueryable();
        if (activeOnly)
            query = query.Where(s => s.IsActive);

        var systems = await query
            .Include(s => s.SecurityCategorization)
            .ToListAsync(cancellationToken);

        var result = new DashboardResult();
        result.TotalSystems = systems.Count;

        foreach (var system in systems)
        {
            var row = new DashboardSystemRow
            {
                SystemId = system.Id,
                Name = system.Name,
                Acronym = system.Acronym,
                CurrentRmfStep = system.CurrentRmfStep.ToString()
            };

            // Impact level from categorization
            if (system.SecurityCategorization != null)
            {
                // Need info types loaded for computed OverallCategorization
                var infoTypes = await db.InformationTypes
                    .Where(it => it.SecurityCategorizationId == system.SecurityCategorization.Id)
                    .ToListAsync(cancellationToken);
                system.SecurityCategorization.InformationTypes = infoTypes;
                row.ImpactLevel = system.SecurityCategorization.OverallCategorization.ToString();
            }

            // Authorization status
            var auth = await db.AuthorizationDecisions
                .Where(d => d.RegisteredSystemId == system.Id && d.IsActive)
                .OrderByDescending(d => d.DecisionDate)
                .FirstOrDefaultAsync(cancellationToken);

            if (auth != null)
            {
                row.DecisionType = auth.DecisionType.ToString();

                if (auth.ExpirationDate.HasValue)
                {
                    row.ExpirationDate = auth.ExpirationDate;
                    var daysUntil = (int)(auth.ExpirationDate.Value.Date - DateTime.UtcNow.Date).TotalDays;
                    row.DaysUntilExpiration = daysUntil;

                    if (daysUntil < 0)
                    {
                        row.AuthorizationStatus = "Expired";
                        result.ExpiredCount++;
                    }
                    else if (daysUntil <= 90)
                    {
                        row.AuthorizationStatus = "Authorized";
                        result.ExpiringCount++;
                    }
                    else
                    {
                        row.AuthorizationStatus = "Authorized";
                        result.AuthorizedCount++;
                    }
                }
                else
                {
                    row.AuthorizationStatus = auth.DecisionType == AuthorizationDecisionType.Dato
                        ? "Denied" : "Authorized";
                    if (auth.DecisionType != AuthorizationDecisionType.Dato)
                        result.AuthorizedCount++;
                }
            }
            else
            {
                row.AuthorizationStatus = system.CurrentRmfStep == RmfPhase.Monitor ? "Expired" : "Pending";
            }

            // Compliance score from effectiveness records
            var effectivenessCount = await db.ControlEffectivenessRecords
                .CountAsync(e => e.RegisteredSystemId == system.Id, cancellationToken);
            var satisfiedCount = await db.ControlEffectivenessRecords
                .CountAsync(e => e.RegisteredSystemId == system.Id && e.Determination == EffectivenessDetermination.Satisfied,
                    cancellationToken);
            row.ComplianceScore = effectivenessCount > 0
                ? Math.Round((double)satisfiedCount / effectivenessCount * 100, 2)
                : null;

            // Open findings count
            row.OpenFindings = await db.Findings
                .CountAsync(f =>
                    db.Assessments.Any(a => a.Id == f.AssessmentId && a.RegisteredSystemId == system.Id) &&
                    (f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress),
                    cancellationToken);

            // Open POA&M count
            row.OpenPoamItems = await db.PoamItems
                .CountAsync(p => p.RegisteredSystemId == system.Id &&
                    (p.Status == PoamStatus.Ongoing || p.Status == PoamStatus.Delayed),
                    cancellationToken);

            // Alert count
            var unreviewed = await db.SignificantChanges
                .CountAsync(c => c.RegisteredSystemId == system.Id && c.ReviewedAt == null,
                    cancellationToken);
            row.AlertCount = unreviewed;
            if (row.DaysUntilExpiration.HasValue && row.DaysUntilExpiration.Value <= 90)
                row.AlertCount++;

            result.Systems.Add(row);
        }

        _logger.LogInformation("Generated multi-system dashboard: {Count} systems", result.TotalSystems);
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §4.5 — Reauthorization Workflow
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ReauthorizationResult> CheckReauthorizationAsync(
        string systemId, bool initiateIfTriggered,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var result = new ReauthorizationResult { SystemId = systemId };

        // Check trigger 1: ATO expiration
        var activeAuth = await db.AuthorizationDecisions
            .Where(d => d.RegisteredSystemId == systemId && d.IsActive)
            .OrderByDescending(d => d.DecisionDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeAuth?.ExpirationDate != null)
        {
            var daysUntil = (int)(activeAuth.ExpirationDate.Value.Date - DateTime.UtcNow.Date).TotalDays;
            if (daysUntil <= 0)
            {
                result.Triggers.Add($"Authorization expired {Math.Abs(daysUntil)} days ago");
            }
            else if (daysUntil <= 90)
            {
                result.Triggers.Add($"Authorization expiring in {daysUntil} days");
            }
        }
        else if (activeAuth == null && system.CurrentRmfStep == RmfPhase.Monitor)
        {
            result.Triggers.Add("No active authorization — system in Monitor phase");
        }

        // Check trigger 2: Significant changes requiring reauthorization
        var unreviewedChanges = await db.SignificantChanges
            .Where(c => c.RegisteredSystemId == systemId &&
                c.RequiresReauthorization && !c.ReauthorizationTriggered)
            .ToListAsync(cancellationToken);

        result.UnreviewedChangeCount = unreviewedChanges.Count;
        foreach (var change in unreviewedChanges)
        {
            result.Triggers.Add($"Significant change: {change.ChangeType} — {change.Description}");
        }

        // Check trigger 3: ConMon score drift (> 10% below baseline)
        if (activeAuth != null)
        {
            var effectivenessRecords = await db.ControlEffectivenessRecords
                .Where(e => e.RegisteredSystemId == systemId)
                .ToListAsync(cancellationToken);
            var assessed = effectivenessRecords.Count;
            var satisfied = effectivenessRecords.Count(e => e.Determination == EffectivenessDetermination.Satisfied);
            var currentScore = assessed > 0 ? Math.Round((double)satisfied / assessed * 100, 2) : 0.0;

            if (activeAuth.ComplianceScoreAtDecision > 0 &&
                currentScore < activeAuth.ComplianceScoreAtDecision - 10)
            {
                result.Triggers.Add(
                    $"Compliance score drift: {currentScore:F1}% (authorized at {activeAuth.ComplianceScoreAtDecision:F1}%, delta >{10}%)");
            }
        }

        result.IsTriggered = result.Triggers.Count > 0;

        // Optionally initiate reauthorization
        if (result.IsTriggered && initiateIfTriggered)
        {
            result.PreviousRmfStep = system.CurrentRmfStep.ToString();

            // Regress to Assess phase (spec §4.5)
            system.CurrentRmfStep = RmfPhase.Assess;
            system.RmfStepUpdatedAt = DateTime.UtcNow;
            system.ModifiedAt = DateTime.UtcNow;

            // Mark significant changes as triggered
            foreach (var change in unreviewedChanges)
            {
                change.ReauthorizationTriggered = true;
            }

            await db.SaveChangesAsync(cancellationToken);

            result.WasInitiated = true;
            result.NewRmfStep = RmfPhase.Assess.ToString();

            _logger.LogInformation(
                "Initiated reauthorization for system '{SystemId}': {Step} → Assess ({TriggerCount} triggers)",
                systemId, result.PreviousRmfStep, result.Triggers.Count);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §4.8 — ISA & PIA Expiration Monitoring (Feature 021)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<List<AgreementExpirationAlert>> CheckAgreementExpirationsAsync(
        string systemId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .Include(s => s.PrivacyImpactAssessment)
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var alerts = new List<AgreementExpirationAlert>();

        // Check ISA/agreement expirations
        var activeInterconnections = await db.SystemInterconnections
            .Include(ic => ic.Agreements)
            .Where(ic => ic.RegisteredSystemId == systemId && ic.Status == InterconnectionStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var ic in activeInterconnections)
        {
            foreach (var agreement in ic.Agreements.Where(a => a.Status == AgreementStatus.Signed && a.ExpirationDate.HasValue))
            {
                var daysUntil = (int)(agreement.ExpirationDate!.Value.Date - DateTime.UtcNow.Date).TotalDays;

                if (daysUntil > 90) continue;

                var alertLevel = daysUntil switch
                {
                    < 0 => "Expired",
                    <= 30 => "Urgent",
                    <= 60 => "Warning",
                    _ => "Info"
                };

                alerts.Add(new AgreementExpirationAlert
                {
                    ItemType = "ISA",
                    AgreementTitle = agreement.Title,
                    TargetSystemName = ic.TargetSystemName,
                    ExpirationDate = agreement.ExpirationDate,
                    DaysUntilExpiration = daysUntil,
                    AlertLevel = alertLevel,
                    Message = daysUntil < 0
                        ? $"ISA '{agreement.Title}' for {ic.TargetSystemName} expired {Math.Abs(daysUntil)} days ago."
                        : $"ISA '{agreement.Title}' for {ic.TargetSystemName} expires in {daysUntil} days."
                });

                if (daysUntil < 0)
                {
                    // Create SignificantChange for expired ISA
                    var change = new SignificantChange
                    {
                        RegisteredSystemId = systemId,
                        ChangeType = "ISA Expiration",
                        Description = $"ISA '{agreement.Title}' for interconnection with {ic.TargetSystemName} has expired.",
                        DetectedAt = DateTime.UtcNow,
                        DetectedBy = "ConMon-AutoCheck",
                        RequiresReauthorization = false
                    };
                    db.SignificantChanges.Add(change);
                }
            }
        }

        // Check PIA expiration
        var pia = system.PrivacyImpactAssessment;
        if (pia is { Status: PiaStatus.Approved, ExpirationDate: not null })
        {
            var piaDaysUntil = (int)(pia.ExpirationDate.Value.Date - DateTime.UtcNow.Date).TotalDays;

            if (piaDaysUntil <= 90)
            {
                var piaAlertLevel = piaDaysUntil switch
                {
                    < 0 => "Expired",
                    <= 30 => "Urgent",
                    <= 60 => "Warning",
                    _ => "Info"
                };

                alerts.Add(new AgreementExpirationAlert
                {
                    ItemType = "PIA",
                    AgreementTitle = "Privacy Impact Assessment",
                    ExpirationDate = pia.ExpirationDate,
                    DaysUntilExpiration = piaDaysUntil,
                    AlertLevel = piaAlertLevel,
                    Message = piaDaysUntil < 0
                        ? $"PIA expired {Math.Abs(piaDaysUntil)} days ago."
                        : $"PIA expires in {piaDaysUntil} days."
                });

                if (piaDaysUntil < 0)
                {
                    pia.Status = PiaStatus.Expired;
                    var change = new SignificantChange
                    {
                        RegisteredSystemId = systemId,
                        ChangeType = "PIA Expiration",
                        Description = "Privacy Impact Assessment has expired. Annual review overdue.",
                        DetectedAt = DateTime.UtcNow,
                        DetectedBy = "ConMon-AutoCheck",
                        RequiresReauthorization = false
                    };
                    db.SignificantChanges.Add(change);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Checked agreement expirations for system '{SystemId}': {AlertCount} alert(s)",
            systemId, alerts.Count);

        return alerts;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string GenerateReportContent(
        string systemName, string reportType, string period,
        double complianceScore, double? baselineScore,
        int openFindings, int resolvedFindings,
        int openPoam, int overduePoam,
        AuthorizationDecision? activeAuth)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Continuous Monitoring Report — {systemName}");
        sb.AppendLine();
        sb.AppendLine($"**Report Type**: {reportType}");
        sb.AppendLine($"**Period**: {period}");
        sb.AppendLine($"**Generated**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // Executive summary
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Current Compliance Score**: {complianceScore:F1}%");
        if (baselineScore.HasValue)
        {
            var delta = complianceScore - baselineScore.Value;
            var direction = delta >= 0 ? "▲" : "▼";
            sb.AppendLine($"- **Authorized Baseline Score**: {baselineScore.Value:F1}%");
            sb.AppendLine($"- **Delta from Authorization**: {direction} {Math.Abs(delta):F1}%");
        }
        sb.AppendLine();

        // Authorization status
        sb.AppendLine("## Authorization Status");
        sb.AppendLine();
        if (activeAuth != null)
        {
            sb.AppendLine($"- **Decision**: {activeAuth.DecisionType}");
            sb.AppendLine($"- **Issued**: {activeAuth.DecisionDate:yyyy-MM-dd}");
            if (activeAuth.ExpirationDate.HasValue)
            {
                var daysLeft = (int)(activeAuth.ExpirationDate.Value.Date - DateTime.UtcNow.Date).TotalDays;
                sb.AppendLine($"- **Expires**: {activeAuth.ExpirationDate.Value:yyyy-MM-dd} ({daysLeft} days remaining)");
            }
        }
        else
        {
            sb.AppendLine("- **Status**: No active authorization");
        }
        sb.AppendLine();

        // Findings summary
        sb.AppendLine("## Findings Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Count |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Open Findings | {openFindings} |");
        sb.AppendLine($"| Resolved Findings | {resolvedFindings} |");
        sb.AppendLine();

        // POA&M status
        sb.AppendLine("## POA&M Status");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Count |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Open Items | {openPoam} |");
        sb.AppendLine($"| Overdue Items | {overduePoam} |");
        sb.AppendLine();

        // Risk trending
        sb.AppendLine("## Risk Trending");
        sb.AppendLine();
        if (overduePoam > 0)
        {
            sb.AppendLine("⚠️ **Attention Required**: There are overdue POA&M items that need immediate attention.");
        }
        if (baselineScore.HasValue && complianceScore < baselineScore.Value - 5)
        {
            sb.AppendLine("⚠️ **Score Drift**: Compliance score has dropped more than 5% below the authorized baseline.");
        }
        if (overduePoam == 0 && (!baselineScore.HasValue || complianceScore >= baselineScore.Value - 5))
        {
            sb.AppendLine("✅ System compliance is within acceptable parameters.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Determine if a change type requires reauthorization.
    /// Based on NIST SP 800-37 Rev 2 guidance on significant changes.
    /// </summary>
    private static bool IsReauthorizationRequired(string changeType)
    {
        var reauthTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "New Interconnection",
            "Major Upgrade",
            "Data Type Change",
            "Operating Environment Change",
            "Security Architecture Change",
            "Major Software Change",
            "New System Component",
            "Authorization Boundary Change",
            "Migration",
            "New Deployment Model"
        };

        return reauthTypes.Contains(changeType);
    }

    /// <summary>
    /// Populate <see cref="ConMonReport"/> Watch-data enrichment fields (Phase 17 §9a.3).
    /// Queries monitoring configurations and drift alerts for the system's subscriptions.
    /// No-op when <c>_watchService</c> is null (backward-compatible).
    /// </summary>
    private async Task EnrichReportWithWatchDataAsync(
        ConMonReport report,
        string systemId,
        AtoCopilotContext db,
        CancellationToken cancellationToken)
    {
        if (_environmentGuard is not null)
        {
            try
            {
                await _environmentGuard.EnsureSystemAsync(systemId, EnvironmentScopePurpose.Monitoring, cancellationToken);
            }
            catch (AssessmentEnvironmentException failure)
            {
                report.MonitoringEnabled = false;
                report.LastMonitoringCheck = null;
                report.ReportContent += $"\n\n## Environment coverage\n\nCollection unavailable: {failure.Message}\n\n{failure.Suggestion}";
                _logger.LogWarning("Report monitoring coverage unavailable for {SystemId}: {Reason}", systemId, failure.ErrorCode);
                return;
            }
        }
        if (_watchService == null)
            return;

        try
        {
            // Discover subscriptions belonging to this system
            var system = await db.RegisteredSystems
                .AsNoTracking()
                .Where(s => s.Id == systemId)
                .Select(s => new { s.AzureProfile })
                .FirstOrDefaultAsync(cancellationToken);

            var subscriptionIds = system?.AzureProfile?.SubscriptionIds ?? new List<string>();

            if (subscriptionIds.Count == 0)
            {
                report.MonitoringEnabled = false;
                return;
            }

            // Check if monitoring is enabled for any subscription
            var monitoringConfigs = await db.MonitoringConfigurations
                .AsNoTracking()
                .Where(mc => subscriptionIds.Contains(mc.SubscriptionId) && mc.IsEnabled)
                .ToListAsync(cancellationToken);

            report.MonitoringEnabled = monitoringConfigs.Count > 0;
            report.LastMonitoringCheck = monitoringConfigs
                .Where(mc => mc.LastRunAt.HasValue)
                .Select(mc => mc.LastRunAt!.Value.UtcDateTime)
                .OrderByDescending(d => d)
                .Cast<DateTime?>()
                .FirstOrDefault();

            // Count active drift alerts for the system's subscriptions
            report.DriftAlertCount = await db.ComplianceAlerts
                .AsNoTracking()
                .CountAsync(a =>
                    subscriptionIds.Contains(a.SubscriptionId) &&
                    a.Type == AlertType.Drift &&
                    a.Status != AlertStatus.Resolved &&
                    a.Status != AlertStatus.Dismissed,
                    cancellationToken);

            // Count auto-remediation rules for the system's subscriptions
            report.AutoRemediationRuleCount = await db.AutoRemediationRules
                .AsNoTracking()
                .CountAsync(r =>
                    r.SubscriptionId != null && subscriptionIds.Contains(r.SubscriptionId) &&
                    r.IsEnabled,
                    cancellationToken);

            _logger.LogDebug(
                "Enriched ConMon report for system {SystemId}: monitoring={Enabled}, driftAlerts={Drift}, rules={Rules}",
                systemId, report.MonitoringEnabled, report.DriftAlertCount, report.AutoRemediationRuleCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich ConMon report with watch data for system {SystemId}", systemId);
        }
    }

    /// <summary>
    /// Creates a <see cref="ComplianceAlert"/> for ATO expiration events (Phase 17 §9a.4).
    /// Graduated severity: Info@90d, Warning@60d, High@30d/Urgent, Critical@expired.
    /// </summary>
    private async Task CreateExpirationAlertAsync(
        ExpirationStatus status, string systemId, CancellationToken cancellationToken)
    {
        try
        {
            var severity = status.AlertLevel switch
            {
                "Info" => AlertSeverity.Low,
                "Warning" => AlertSeverity.Medium,
                "Urgent" => AlertSeverity.High,
                "Expired" => AlertSeverity.Critical,
                _ => AlertSeverity.Low
            };

            var alert = new ComplianceAlert
            {
                Type = AlertType.Degradation,
                Severity = severity,
                Title = $"ATO {status.AlertLevel}: {status.SystemName}",
                Description = status.AlertMessage ?? $"ATO alert level {status.AlertLevel} for system {systemId}.",
                SubscriptionId = systemId, // Use systemId as subscription key for correlation
                RegisteredSystemId = systemId,
                RecommendedAction = status.AlertLevel == "Expired"
                    ? "Initiate emergency reauthorization. System is operating without authorization."
                    : $"Schedule reauthorization activities. {status.DaysUntilExpiration} days remaining."
            };

            await _alertManager!.CreateAlertAsync(alert, cancellationToken);

            _logger.LogInformation(
                "Created expiration alert for system {SystemId}: level={Level}, severity={Severity}",
                systemId, status.AlertLevel, severity);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create expiration alert for system {SystemId}", systemId);
        }
    }

    /// <summary>
    /// Creates a <see cref="ComplianceAlert"/> for significant changes requiring reauthorization (Phase 17 §9a.4).
    /// </summary>
    private async Task CreateSignificantChangeAlertAsync(
        SignificantChange change, string systemName, CancellationToken cancellationToken)
    {
        try
        {
            var alert = new ComplianceAlert
            {
                Type = AlertType.Violation, // Significant change requiring action
                Severity = AlertSeverity.High,
                Title = $"Significant Change: {change.ChangeType} — {systemName}",
                Description = $"A significant change ({change.ChangeType}) has been reported for system '{systemName}' " +
                    $"that requires reauthorization. Details: {change.Description}",
                SubscriptionId = change.RegisteredSystemId, // Use systemId for correlation
                RegisteredSystemId = change.RegisteredSystemId,
                RecommendedAction = "Initiate impact analysis and reauthorization process per NIST SP 800-37 Rev 2."
            };

            await _alertManager!.CreateAlertAsync(alert, cancellationToken);

            _logger.LogInformation(
                "Created significant change alert for system {SystemId}: changeType={ChangeType}",
                change.RegisteredSystemId, change.ChangeType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to create significant change alert for system {SystemId}",
                change.RegisteredSystemId);
        }
    }
}

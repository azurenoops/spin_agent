using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Document.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Services;
using System.Text.RegularExpressions;

using KanbanTaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Mcp.Endpoints;

// ─── #648 Decomposition: ConMon domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapConMonRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
        group.MapGet("/systems/{systemId}/conmon", async (
                string systemId,
                AtoCopilotContext context,
                IConMonService conMonService,
                CancellationToken ct) =>
            {
                var system = await context.RegisteredSystems
                    .AsNoTracking()
                    .Where(s => s.Id == systemId)
                    .Select(s => new
                    {
                        s.Id,
                        s.Name,
                        s.CurrentRmfStep,
                        s.AzureProfile,
                    })
                    .FirstOrDefaultAsync(ct);

                if (system is null)
                    return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "NOT_FOUND" });

                var plan = await context.ConMonPlans
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, ct);

                var activeAuth = await context.AuthorizationDecisions
                    .AsNoTracking()
                    .Where(d => d.RegisteredSystemId == systemId && d.IsActive)
                    .OrderByDescending(d => d.DecisionDate)
                    .FirstOrDefaultAsync(ct);

                var effectivenessCount = await context.ControlEffectivenessRecords
                    .CountAsync(e => e.RegisteredSystemId == systemId, ct);
                var satisfiedCount = await context.ControlEffectivenessRecords
                    .CountAsync(e => e.RegisteredSystemId == systemId && e.Determination == EffectivenessDetermination.Satisfied, ct);
                var currentComplianceScore = effectivenessCount > 0
                    ? Math.Round((double)satisfiedCount / effectivenessCount * 100, 2)
                    : 0.0;

                var baselineScore = activeAuth?.ComplianceScoreAtDecision;
                var scoreDelta = baselineScore.HasValue
                    ? Math.Round(currentComplianceScore - baselineScore.Value, 2)
                    : (double?)null;

                var openFindings = await context.Findings
                    .CountAsync(f =>
                        context.Assessments.Any(a => a.Id == f.AssessmentId && a.RegisteredSystemId == systemId) &&
                        (f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress),
                        ct);

                var resolvedFindings = await context.Findings
                    .CountAsync(f =>
                        context.Assessments.Any(a => a.Id == f.AssessmentId && a.RegisteredSystemId == systemId) &&
                        (f.Status == FindingStatus.Remediated || f.Status == FindingStatus.FalsePositive),
                        ct);

                var openPoamItems = await context.PoamItems
                    .CountAsync(p => p.RegisteredSystemId == systemId &&
                        (p.Status == PoamStatus.Ongoing || p.Status == PoamStatus.Delayed),
                        ct);

                var overduePoamItems = await context.PoamItems
                    .CountAsync(p => p.RegisteredSystemId == systemId &&
                        p.Status == PoamStatus.Ongoing &&
                        p.ScheduledCompletionDate < DateTime.UtcNow &&
                        p.ActualCompletionDate == null,
                        ct);

                var subscriptionIds = system.AzureProfile?.SubscriptionIds ?? new List<string>();
                var monitoringConfigs = subscriptionIds.Count == 0
                    ? new List<MonitoringConfiguration>()
                    : await context.MonitoringConfigurations
                        .AsNoTracking()
                        .Where(mc => subscriptionIds.Contains(mc.SubscriptionId) && mc.IsEnabled)
                        .ToListAsync(ct);

                var monitoringEnabled = monitoringConfigs.Count > 0;
                var lastMonitoringCheck = monitoringConfigs
                    .Where(mc => mc.LastRunAt.HasValue)
                    .Select(mc => mc.LastRunAt!.Value.UtcDateTime)
                    .OrderByDescending(d => d)
                    .Cast<DateTime?>()
                    .FirstOrDefault();

                var driftAlertCount = subscriptionIds.Count == 0
                    ? 0
                    : await context.ComplianceAlerts
                        .AsNoTracking()
                        .CountAsync(a =>
                            subscriptionIds.Contains(a.SubscriptionId) &&
                            a.Type == AlertType.Drift &&
                            a.Status != AlertStatus.Resolved &&
                            a.Status != AlertStatus.Dismissed,
                            ct);

                var autoRemediationRuleCount = subscriptionIds.Count == 0
                    ? 0
                    : await context.AutoRemediationRules
                        .AsNoTracking()
                        .CountAsync(r => r.SubscriptionId != null && subscriptionIds.Contains(r.SubscriptionId) && r.IsEnabled, ct);

                var now = DateTime.UtcNow;
                var expiration = activeAuth switch
                {
                    null => new ConMonExpirationInfo
                    {
                        HasActiveAuthorization = false,
                        AlertLevel = "Warning",
                        AlertMessage = "No active authorization decision for this system.",
                    },
                    _ when activeAuth.ExpirationDate == null && activeAuth.DecisionType == AuthorizationDecisionType.Dato => new ConMonExpirationInfo
                    {
                        HasActiveAuthorization = true,
                        DecisionType = activeAuth.DecisionType.ToString(),
                        DecisionDate = activeAuth.DecisionDate,
                        AlertLevel = "Urgent",
                        AlertMessage = "System has a Denial of Authorization to Operate (DATO). System should not be in production.",
                    },
                    _ when activeAuth.ExpirationDate == null => new ConMonExpirationInfo
                    {
                        HasActiveAuthorization = true,
                        DecisionType = activeAuth.DecisionType.ToString(),
                        DecisionDate = activeAuth.DecisionDate,
                        AlertLevel = "None",
                        AlertMessage = "Authorization has no expiration date.",
                    },
                    _ => BuildConMonExpirationInfo(activeAuth, now),
                };

                var agreementAlerts = new List<AgreementExpirationInfo>();

                var activeInterconnections = await context.SystemInterconnections
                    .Include(ic => ic.Agreements)
                    .Where(ic => ic.RegisteredSystemId == systemId && ic.Status == InterconnectionStatus.Active)
                    .AsNoTracking()
                    .ToListAsync(ct);

                foreach (var interconnection in activeInterconnections)
                {
                    foreach (var agreement in interconnection.Agreements.Where(a => a.Status == AgreementStatus.Signed && a.ExpirationDate.HasValue))
                    {
                        var daysUntilExpiration = (int)(agreement.ExpirationDate!.Value.Date - now.Date).TotalDays;
                        if (daysUntilExpiration > 90)
                            continue;

                        var alertLevel = daysUntilExpiration switch
                        {
                            < 0 => "Expired",
                            <= 30 => "Urgent",
                            <= 60 => "Warning",
                            _ => "Info"
                        };

                        agreementAlerts.Add(new AgreementExpirationInfo
                        {
                            ItemType = "ISA",
                            AgreementTitle = agreement.Title,
                            TargetSystemName = interconnection.TargetSystemName,
                            ExpirationDate = agreement.ExpirationDate,
                            DaysUntilExpiration = daysUntilExpiration,
                            AlertLevel = alertLevel,
                            Message = daysUntilExpiration < 0
                                ? $"ISA '{agreement.Title}' for {interconnection.TargetSystemName} expired {Math.Abs(daysUntilExpiration)} days ago."
                                : $"ISA '{agreement.Title}' for {interconnection.TargetSystemName} expires in {daysUntilExpiration} days."
                        });
                    }
                }

                var pia = await context.PrivacyImpactAssessments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, ct);

                if (pia?.ExpirationDate is DateTime piaExpirationDate)
                {
                    var daysUntilExpiration = (int)(piaExpirationDate.Date - now.Date).TotalDays;
                    if (daysUntilExpiration <= 90)
                    {
                        var alertLevel = daysUntilExpiration switch
                        {
                            < 0 => "Expired",
                            <= 30 => "Urgent",
                            <= 60 => "Warning",
                            _ => "Info"
                        };

                        agreementAlerts.Add(new AgreementExpirationInfo
                        {
                            ItemType = "PIA",
                            AgreementTitle = $"PIA v{pia.Version}",
                            ExpirationDate = pia.ExpirationDate,
                            DaysUntilExpiration = daysUntilExpiration,
                            AlertLevel = alertLevel,
                            Message = daysUntilExpiration < 0
                                ? $"PIA expired {Math.Abs(daysUntilExpiration)} days ago."
                                : $"PIA expires in {daysUntilExpiration} days."
                        });
                    }
                }

                var significantChanges = await context.SignificantChanges
                    .AsNoTracking()
                    .Where(c => c.RegisteredSystemId == systemId)
                    .OrderByDescending(c => c.DetectedAt)
                    .Take(20)
                    .Select(c => new SignificantChangeItemInfo
                    {
                        Id = c.Id,
                        ChangeType = c.ChangeType,
                        Description = c.Description,
                        DetectedAt = c.DetectedAt,
                        DetectedBy = c.DetectedBy,
                        RequiresReauthorization = c.RequiresReauthorization,
                        ReauthorizationTriggered = c.ReauthorizationTriggered,
                        ReviewedBy = c.ReviewedBy,
                        ReviewedAt = c.ReviewedAt,
                        Disposition = c.Disposition,
                    })
                    .ToListAsync(ct);

                var reports = await context.ConMonReports
                    .AsNoTracking()
                    .Where(r => r.RegisteredSystemId == systemId)
                    .OrderByDescending(r => r.GeneratedAt)
                    .Take(12)
                    .ToListAsync(ct);

                var reauthorization = await conMonService.CheckReauthorizationAsync(systemId, initiateIfTriggered: false, ct);

                return Results.Ok(new ConMonOverviewResponse
                {
                    SystemId = systemId,
                    SystemName = system.Name,
                    CurrentPhase = system.CurrentRmfStep.ToString(),
                    Plan = plan is null ? null : new ConMonPlanDetailInfo
                    {
                        PlanId = plan.Id,
                        AssessmentFrequency = plan.AssessmentFrequency,
                        AnnualReviewDate = plan.AnnualReviewDate,
                        ReportDistribution = plan.ReportDistribution,
                        SignificantChangeTriggers = plan.SignificantChangeTriggers,
                        CreatedAt = plan.CreatedAt,
                        ModifiedAt = plan.ModifiedAt,
                    },
                    Status = new ConMonStatusInfo
                    {
                        CurrentComplianceScore = currentComplianceScore,
                        AuthorizedBaselineScore = baselineScore,
                        ScoreDelta = scoreDelta,
                        OpenFindings = openFindings,
                        ResolvedFindings = resolvedFindings,
                        OpenPoamItems = openPoamItems,
                        OverduePoamItems = overduePoamItems,
                        MonitoringEnabled = monitoringEnabled,
                        DriftAlertCount = driftAlertCount,
                        AutoRemediationRuleCount = autoRemediationRuleCount,
                        LastMonitoringCheck = lastMonitoringCheck,
                    },
                    Expiration = expiration,
                    Reauthorization = new ConMonReauthorizationInfo
                    {
                        IsTriggered = reauthorization.IsTriggered,
                        Triggers = reauthorization.Triggers,
                        UnreviewedChangeCount = reauthorization.UnreviewedChangeCount,
                    },
                    AgreementAlerts = agreementAlerts,
                    SignificantChanges = significantChanges,
                    Reports = reports.Select(r => new ConMonReportSummaryInfo
                    {
                        ReportId = r.Id,
                        ReportType = r.ReportType,
                        Period = r.ReportPeriod,
                        ComplianceScore = r.ComplianceScore,
                        AuthorizedBaselineScore = r.AuthorizedBaselineScore,
                        ScoreDelta = r.AuthorizedBaselineScore.HasValue
                            ? Math.Round(r.ComplianceScore - r.AuthorizedBaselineScore.Value, 2)
                            : null,
                        NewFindings = r.NewFindings,
                        ResolvedFindings = r.ResolvedFindings,
                        OpenPoamItems = r.OpenPoamItems,
                        OverduePoamItems = r.OverduePoamItems,
                        GeneratedAt = r.GeneratedAt,
                        GeneratedBy = r.GeneratedBy,
                    }).ToList(),
                });
            })
            .WithName("GetSystemConMonOverview");

        // ─── Get ConMon Report Detail ────────────────────────────────────────
        group.MapGet("/systems/{systemId}/conmon/reports/{reportId}", async (
                string systemId,
                string reportId,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var report = await context.ConMonReports
                    .Where(r => r.Id == reportId)
                    .Include(r => r.ConMonPlan)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);

                if (report is null)
                    return Results.NotFound(new ErrorResponse { Error = "Report not found.", ErrorCode = "NOT_FOUND" });

                if (report.ConMonPlan?.RegisteredSystemId != systemId)
                    return Results.NotFound(new ErrorResponse { Error = "Report does not belong to this system.", ErrorCode = "NOT_FOUND" });

                return Results.Ok(new
                {
                    reportId = report.Id,
                    reportType = report.ReportType,
                    period = report.ReportPeriod,
                    complianceScore = report.ComplianceScore,
                    authorizedBaselineScore = report.AuthorizedBaselineScore,
                    scoreDelta = report.AuthorizedBaselineScore.HasValue
                        ? report.ComplianceScore - report.AuthorizedBaselineScore.Value
                        : (double?)null,
                    newFindings = report.NewFindings,
                    resolvedFindings = report.ResolvedFindings,
                    openPoamItems = report.OpenPoamItems,
                    overduePoamItems = report.OverduePoamItems,
                    generatedAt = report.GeneratedAt,
                    generatedBy = report.GeneratedBy,
                    reportContent = report.ReportContent,
                });
            })
            .WithName("GetConMonReportDetail");

        // ───────────── Assessments ────────────────────────────────────────────
    }
}

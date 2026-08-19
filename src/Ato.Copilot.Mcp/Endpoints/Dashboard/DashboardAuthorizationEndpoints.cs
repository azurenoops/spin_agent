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

// ─── #648 Decomposition: Authorization domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapAuthorizationRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dashboard/systems/{systemId}/authorization", async (
            string systemId,
            IssueAuthorizationRequest body,
            IAuthorizationService authorizationService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            try
            {
                var decision = await authorizationService.IssueAuthorizationAsync(
                    systemId,
                    body.DecisionType,
                    body.ExpirationDate,
                    body.ResidualRiskLevel ?? "Medium",
                    body.TermsAndConditions,
                    body.ResidualRiskJustification,
                    body.RiskAcceptances,
                    body.IssuedBy ?? "dashboard-user",
                    body.IssuedByName ?? "Dashboard User",
                    ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "AuthorizationIssued",
                    Actor = body.IssuedBy ?? "dashboard-user",
                    Summary = $"Authorization decision issued: {decision.DecisionType} (expires {decision.ExpirationDate:yyyy-MM-dd})",
                    RelatedEntityType = "AuthorizationDecision",
                    RelatedEntityId = decision.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/systems/{systemId}/authorization/{decision.Id}", new
                {
                    id = decision.Id,
                    decisionType = decision.DecisionType.ToString(),
                    expirationDate = decision.ExpirationDate,
                    residualRiskLevel = decision.ResidualRiskLevel.ToString(),
                    issuedBy = decision.IssuedBy,
                    issuedAt = decision.DecisionDate,
                    riskAcceptanceCount = decision.RiskAcceptances.Count,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
            }
        })
        .WithName("IssueAuthorization");

        // ─── AO Pending Decisions ─────────────────────────────────────────────
        // GET /api/dashboard/ao/pending-decisions
        // Returns authorization decisions expiring within 30 days or already expired.
        app.MapGet("/api/dashboard/ao/pending-decisions", async (
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var threshold = now.AddDays(30);

            var decisions = await context.AuthorizationDecisions
                .Where(d => d.ExpirationDate != null &&
                            (d.ExpirationDate <= threshold || d.ExpirationDate < now))
                .OrderBy(d => d.ExpirationDate)
                .Select(d => new
                {
                    d.RegisteredSystemId,
                    d.DecisionType,
                    d.ExpirationDate,
                })
                .ToListAsync(ct);

            var systemIds = decisions.Select(d => d.RegisteredSystemId).Distinct().ToList();
            var systems = await context.RegisteredSystems
                .Where(s => systemIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(ct);

            var nameMap = systems.ToDictionary(s => s.Id, s => s.Name);

            var result = decisions.Select(d =>
            {
                var daysUntil = d.ExpirationDate.HasValue
                    ? (int)(d.ExpirationDate.Value.Date - now.Date).TotalDays
                    : 0;
                return new
                {
                    systemId = d.RegisteredSystemId,
                    systemName = nameMap.TryGetValue(d.RegisteredSystemId, out var n) ? n : d.RegisteredSystemId,
                    decisionType = d.DecisionType.ToString(),
                    expirationDate = d.ExpirationDate,
                    daysUntilExpiration = daysUntil,
                    isOverdue = daysUntil < 0,
                };
            }).ToList();

            return Results.Ok(result);
        })
        .WithName("GetAoPendingDecisions");

        // ─── Accept Risk ─────────────────────────────────────────────────────
        app.MapPost("/api/dashboard/systems/{systemId}/risk-acceptances", async (
            string systemId,
            AcceptRiskRequest body,
            IAuthorizationService authorizationService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            try
            {
                var risk = await authorizationService.AcceptRiskAsync(
                    systemId,
                    body.FindingId,
                    body.ControlId,
                    body.CatSeverity,
                    body.Justification,
                    body.ExpirationDate,
                    body.CompensatingControl,
                    body.AcceptedBy ?? "dashboard-user",
                    ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "RiskAccepted",
                    Actor = body.AcceptedBy ?? "dashboard-user",
                    Summary = $"Risk accepted for {body.ControlId} ({body.CatSeverity}) — expires {body.ExpirationDate:yyyy-MM-dd}",
                    RelatedEntityType = "RiskAcceptance",
                    RelatedEntityId = risk.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/systems/{systemId}/risk-acceptances/{risk.Id}", new
                {
                    id = risk.Id,
                    controlId = risk.ControlId,
                    catSeverity = risk.CatSeverity.ToString(),
                    expirationDate = risk.ExpirationDate,
                    isActive = risk.IsActive,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
            }
        })
        .WithName("AcceptRisk");

        // ─── Create ConMon Plan ──────────────────────────────────────────────
        app.MapPost("/api/dashboard/systems/{systemId}/conmon-plan", async (
            string systemId,
            CreateConMonPlanRequest body,
            IConMonService conMonService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            try
            {
                var plan = await conMonService.CreatePlanAsync(
                    systemId,
                    body.AssessmentFrequency ?? "Monthly",
                    body.AnnualReviewDate ?? DateTime.UtcNow.AddYears(1),
                    body.ReportDistribution,
                    body.SignificantChangeTriggers,
                    "dashboard-user",
                    ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "ConMonPlanCreated",
                    Actor = "dashboard-user",
                    Summary = $"Continuous monitoring plan created (frequency: {body.AssessmentFrequency ?? "Monthly"})",
                    RelatedEntityType = "ConMonPlan",
                    RelatedEntityId = plan.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/systems/{systemId}/conmon-plan/{plan.Id}", new
                {
                    id = plan.Id,
                    assessmentFrequency = plan.AssessmentFrequency,
                    annualReviewDate = plan.AnnualReviewDate,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
            }
        })
        .WithName("CreateConMonPlan");

        // ─── Generate ConMon Report ──────────────────────────────────────────
        app.MapPost("/api/dashboard/systems/{systemId}/conmon-report", async (
            string systemId,
            GenerateConMonReportRequest body,
            IConMonService conMonService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            try
            {
                var report = await conMonService.GenerateReportAsync(
                    systemId,
                    body.ReportType ?? "Monthly",
                    body.Period ?? DateTime.UtcNow.ToString("yyyy-MM"),
                    "dashboard-user",
                    ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "ConMonReportGenerated",
                    Actor = "dashboard-user",
                    Summary = $"ConMon report generated ({body.ReportType ?? "Monthly"} — {body.Period ?? DateTime.UtcNow.ToString("yyyy-MM")})",
                    RelatedEntityType = "ConMonReport",
                    RelatedEntityId = report.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/systems/{systemId}/conmon-report/{report.Id}", new
                {
                    id = report.Id,
                    reportType = report.ReportType,
                    period = report.ReportPeriod,
                    complianceScore = report.ComplianceScore,
                    scoreDelta = report.ComplianceScore - (report.AuthorizedBaselineScore ?? report.ComplianceScore),
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
            }
        })
        .WithName("GenerateConMonReport");

        // ─── Report Significant Change ───────────────────────────────────────
        app.MapPost("/api/dashboard/systems/{systemId}/conmon/significant-change", async (
            string systemId,
            ReportSignificantChangeRequest body,
            IConMonService conMonService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            try
            {
                var change = await conMonService.ReportChangeAsync(
                    systemId,
                    body.ChangeType ?? "Hardware",
                    body.Description ?? string.Empty,
                    body.DetectedBy ?? "dashboard-user",
                    ct);

                var desc = body.Description ?? string.Empty;
                var truncatedDesc = desc.Length > 80 ? desc.Substring(0, 80) + "\u2026" : desc;
                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "SignificantChangeReported",
                    Actor = body.DetectedBy ?? "dashboard-user",
                    Summary = $"Significant change reported: {body.ChangeType ?? "Hardware"} \u2014 {truncatedDesc}",
                    RelatedEntityType = "SignificantChange",
                    RelatedEntityId = change.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/systems/{systemId}/conmon/significant-change/{change.Id}", new
                {
                    id = change.Id,
                    changeType = change.ChangeType,
                    requiresReauthorization = change.RequiresReauthorization,
                    reauthorizationTriggered = change.ReauthorizationTriggered,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
            }
        })
        .WithName("ReportSignificantChange");

        // ─── Reauthorization Check ───────────────────────────────────────────
        app.MapPost("/api/dashboard/systems/{systemId}/conmon/reauthorization-check", async (
            string systemId,
            ReauthorizationCheckRequest body,
            IConMonService conMonService,
            CancellationToken ct) =>
        {
            try
            {
                var result = await conMonService.CheckReauthorizationAsync(
                    systemId,
                    body.InitiateIfTriggered,
                    ct);

                return Results.Ok(new
                {
                    isTriggered = result.IsTriggered,
                    triggers = result.Triggers,
                    unreviewedChangeCount = result.UnreviewedChangeCount,
                    initiated = body.InitiateIfTriggered && result.IsTriggered,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
            }
        })
        .WithName("CheckReauthorization");

        // ─── Remediation Summary (cross-system) ─────────────────────────────
        app.MapGet("/api/dashboard/remediation/summary", async (
            string? systemId,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            // POA&M items — optionally filtered by system
            var poamQuery = context.PoamItems.AsNoTracking();
            if (!string.IsNullOrEmpty(systemId))
                poamQuery = poamQuery.Where(p => p.RegisteredSystemId == systemId);

            var poams = await poamQuery
                .Include(p => p.Milestones)
                .Include(p => p.RegisteredSystem)
                .OrderByDescending(p => p.CatSeverity)
                .ThenBy(p => p.ScheduledCompletionDate)
                .ToListAsync(ct);

            var now = DateTime.UtcNow;

            var openPoams = poams.Where(p => p.Status != PoamStatus.Completed && p.Status != PoamStatus.RiskAccepted).ToList();
            var overduePoams = openPoams.Where(p => p.ScheduledCompletionDate < now).ToList();
            var catI = openPoams.Count(p => p.CatSeverity == CatSeverity.CatI);
            var catII = openPoams.Count(p => p.CatSeverity == CatSeverity.CatII);
            var catIII = openPoams.Count(p => p.CatSeverity == CatSeverity.CatIII);

            // Avg days to close — completed items only
            var completedPoams = poams.Where(p => p.Status == PoamStatus.Completed && p.ActualCompletionDate != null).ToList();
            var avgDaysToClose = completedPoams.Count > 0
                ? Math.Round(completedPoams.Average(p => (p.ActualCompletionDate!.Value - p.CreatedAt).TotalDays), 1)
                : 0.0;

            // Aging buckets (open only)
            var aging = new
            {
                days0To30 = openPoams.Count(p => (now - p.CreatedAt).TotalDays <= 30),
                days31To60 = openPoams.Count(p => { var d = (now - p.CreatedAt).TotalDays; return d > 30 && d <= 60; }),
                days61To90 = openPoams.Count(p => { var d = (now - p.CreatedAt).TotalDays; return d > 60 && d <= 90; }),
                days90Plus = openPoams.Count(p => (now - p.CreatedAt).TotalDays > 90),
            };

            // By-system breakdown
            var bySystem = poams
                .Where(p => p.Status != PoamStatus.Completed && p.Status != PoamStatus.RiskAccepted)
                .GroupBy(p => new { p.RegisteredSystemId, SystemName = p.RegisteredSystem?.Name ?? p.RegisteredSystemId })
                .Select(g => new
                {
                    systemId = g.Key.RegisteredSystemId,
                    systemName = g.Key.SystemName,
                    open = g.Count(),
                    overdue = g.Count(p => p.ScheduledCompletionDate < now),
                    catI = g.Count(p => p.CatSeverity == CatSeverity.CatI),
                })
                .OrderByDescending(s => s.overdue)
                .ThenByDescending(s => s.catI)
                .ToList();

            // Remediation tasks across all boards (or filtered by system via board's subscription)
            var taskQuery = context.RemediationTasks.AsNoTracking();
            if (!string.IsNullOrEmpty(systemId))
            {
                var boardIds = await context.RemediationBoards
                    .Where(b => b.SubscriptionId == systemId)
                    .Select(b => b.Id)
                    .ToListAsync(ct);
                // Also include tasks linked to POA&M items for this system
                var poamTaskIds = poams.Where(p => p.RemediationTaskId != null).Select(p => p.RemediationTaskId!).ToHashSet();
                taskQuery = taskQuery.Where(t => boardIds.Contains(t.BoardId) || poamTaskIds.Contains(t.Id));
            }

            var tasks = await taskQuery.ToListAsync(ct);

            var tasksByStatus = new
            {
                backlog = tasks.Count(t => t.Status == KanbanTaskStatus.Backlog),
                todo = tasks.Count(t => t.Status == KanbanTaskStatus.ToDo),
                inProgress = tasks.Count(t => t.Status == KanbanTaskStatus.InProgress),
                inReview = tasks.Count(t => t.Status == KanbanTaskStatus.InReview),
                blocked = tasks.Count(t => t.Status == KanbanTaskStatus.Blocked),
                done = tasks.Count(t => t.Status == KanbanTaskStatus.Done),
            };

            // Severity heatbar for open POA&Ms
            var totalOpen = openPoams.Count;
            var severityBreakdown = new
            {
                catI,
                catII,
                catIII,
                catIPercent = totalOpen > 0 ? Math.Round(100.0 * catI / totalOpen, 1) : 0,
                catIIPercent = totalOpen > 0 ? Math.Round(100.0 * catII / totalOpen, 1) : 0,
                catIIIPercent = totalOpen > 0 ? Math.Round(100.0 * catIII / totalOpen, 1) : 0,
            };

            return Results.Ok(new
            {
                totalPoams = poams.Count,
                openCount = openPoams.Count,
                overdueCount = overduePoams.Count,
                completedCount = poams.Count(p => p.Status == PoamStatus.Completed),
                riskAcceptedCount = poams.Count(p => p.Status == PoamStatus.RiskAccepted),
                delayedCount = poams.Count(p => p.Status == PoamStatus.Delayed),
                avgDaysToClose,
                severityBreakdown,
                aging,
                bySystem,
                tasksByStatus,
                totalTasks = tasks.Count,
                poams = poams.Select(p => new
                {
                    p.Id,
                    p.RegisteredSystemId,
                    systemName = p.RegisteredSystem?.Name,
                    p.Weakness,
                    p.WeaknessSource,
                    controlId = p.SecurityControlNumber,
                    catSeverity = p.CatSeverity.ToString(),
                    p.PointOfContact,
                    p.PocEmail,
                    p.ResourcesRequired,
                    p.CostEstimate,
                    p.ScheduledCompletionDate,
                    p.ActualCompletionDate,
                    status = p.Status.ToString(),
                    p.Comments,
                    p.FindingId,
                    p.RemediationTaskId,
                    p.CreatedAt,
                    isOverdue = p.Status != PoamStatus.Completed &&
                                p.Status != PoamStatus.RiskAccepted &&
                                p.ScheduledCompletionDate < now,
                    daysRemaining = p.Status == PoamStatus.Completed ? (int?)null
                        : (int)Math.Ceiling((p.ScheduledCompletionDate - now).TotalDays),
                    milestones = p.Milestones.OrderBy(m => m.Sequence).Select(m => new
                    {
                        m.Id,
                        m.Description,
                        m.TargetDate,
                        m.CompletedDate,
                        m.Sequence,
                        m.IsOverdue,
                    }),
                    milestoneProgress = new
                    {
                        total = p.Milestones.Count,
                        completed = p.Milestones.Count(m => m.CompletedDate != null),
                    },
                }),
            });
        })
        .WithName("GetRemediationSummary");

        // ─── Remediation Tasks (cross-board) ─────────────────────────────────
        app.MapGet("/api/dashboard/remediation/tasks", async (
            string? systemId,
            string? status,
            string? severity,
            bool? overdueOnly,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var taskQuery = context.RemediationTasks
                .Include(t => t.Board)
                .AsNoTracking();

            if (!string.IsNullOrEmpty(systemId))
            {
                var boardIds = await context.RemediationBoards
                    .Where(b => b.SubscriptionId == systemId)
                    .Select(b => b.Id)
                    .ToListAsync(ct);
                // Also include tasks linked to POA&M items for this system
                var poamTaskIds = await context.PoamItems
                    .Where(p => p.RegisteredSystemId == systemId && p.RemediationTaskId != null)
                    .Select(p => p.RemediationTaskId!)
                    .Distinct()
                    .ToListAsync(ct);
                taskQuery = taskQuery.Where(t => boardIds.Contains(t.BoardId) || poamTaskIds.Contains(t.Id));
            }

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<KanbanTaskStatus>(status, true, out var ts))
                taskQuery = taskQuery.Where(t => t.Status == ts);

            if (!string.IsNullOrEmpty(severity) && Enum.TryParse<FindingSeverity>(severity, true, out var sv))
                taskQuery = taskQuery.Where(t => t.Severity == sv);

            if (overdueOnly == true)
                taskQuery = taskQuery.Where(t => t.DueDate < DateTime.UtcNow && t.Status != KanbanTaskStatus.Done);

            var tasks = await taskQuery
                .OrderByDescending(t => t.Severity)
                .ThenBy(t => t.DueDate)
                .ToListAsync(ct);

            // Look up CAT severity from linked POA&M items
            var poamItemIds = tasks
                .Where(t => t.PoamItemId != null)
                .Select(t => t.PoamItemId!)
                .Distinct()
                .ToList();
            var poamCatMap = poamItemIds.Count > 0
                ? await context.PoamItems
                    .Where(p => poamItemIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.CatSeverity })
                    .AsNoTracking()
                    .ToDictionaryAsync(p => p.Id, p => p.CatSeverity.ToString(), ct)
                : new Dictionary<string, string>();

            // Look up component names for findings linked to components (Feature 040 US6)
            var findingIds = tasks
                .Where(t => t.FindingId != null)
                .Select(t => t.FindingId!)
                .Distinct()
                .ToList();
            var findingComponentMap = new Dictionary<string, (string componentId, string componentName)>();
            if (findingIds.Count > 0)
            {
                var linkedFindings = await context.Findings
                    .Where(f => findingIds.Contains(f.Id) && f.ComponentId != null)
                    .Select(f => new { f.Id, f.ComponentId })
                    .AsNoTracking()
                    .ToListAsync(ct);
                if (linkedFindings.Count > 0)
                {
                    var compIds = linkedFindings.Select(f => f.ComponentId!).Distinct().ToList();
                    var compNames = await context.SystemComponents
                        .Where(c => compIds.Contains(c.Id))
                        .Select(c => new { c.Id, c.Name })
                        .AsNoTracking()
                        .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
                    foreach (var lf in linkedFindings)
                    {
                        if (compNames.TryGetValue(lf.ComponentId!, out var name))
                            findingComponentMap[lf.Id] = (lf.ComponentId!, name);
                    }
                }
            }

            // Map task severity → DoD CAT (per DoDI 8510.01):
            // Critical/High → CAT I, Medium → CAT II, Low/Informational → CAT III.
            static string SeverityToCat(FindingSeverity s) => s switch
            {
                FindingSeverity.Critical or FindingSeverity.High => nameof(CatSeverity.CatI),
                FindingSeverity.Medium => nameof(CatSeverity.CatII),
                _ => nameof(CatSeverity.CatIII),
            };

            return Results.Ok(new
            {
                items = tasks.Select(t =>
                {
                    findingComponentMap.TryGetValue(t.FindingId ?? "", out var comp);
                    return new
                    {
                        t.Id,
                        t.TaskNumber,
                        t.BoardId,
                        boardName = t.Board?.Name,
                        t.Title,
                        t.Description,
                        t.ControlId,
                        t.ControlFamily,
                        severity = t.Severity.ToString(),
                        catSeverity = t.PoamItemId != null && poamCatMap.TryGetValue(t.PoamItemId, out var cat)
                            ? cat
                            : SeverityToCat(t.Severity),
                        status = t.Status.ToString(),
                        t.AssigneeId,
                        t.AssigneeName,
                        t.DueDate,
                        t.CreatedAt,
                        t.UpdatedAt,
                        t.FindingId,
                        t.PoamItemId,
                        t.RemediationScript,
                        t.RemediationScriptType,
                        t.ValidationCriteria,
                        isOverdue = t.DueDate < DateTime.UtcNow && t.Status != KanbanTaskStatus.Done,
                        affectedResourceCount = t.AffectedResources.Count,
                        componentId = comp.componentId,
                        componentName = comp.componentName,
                    };
                }),
                totalCount = tasks.Count,
            });
        })
        .WithName("GetRemediationTasks");

        // Fix #554: POST /api/dashboard/remediation/tasks — create a remediation task
        // Looks up or creates the default board for the system, then creates a task.
        app.MapPost("/api/dashboard/remediation/tasks", async (
            CreateRemediationTaskRequest body,
            AtoCopilotContext context,
            IKanbanService kanbanService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.SystemId))
                return Results.BadRequest(new ErrorResponse { Error = "systemId is required", ErrorCode = "INVALID_INPUT" });
            if (string.IsNullOrWhiteSpace(body.Title))
                return Results.BadRequest(new ErrorResponse { Error = "title is required", ErrorCode = "INVALID_INPUT" });

            // Resolve or create the default board for this system
            var board = await context.RemediationBoards
                .Where(b => b.SubscriptionId == body.SystemId)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (board == null)
            {
                board = await kanbanService.CreateBoardAsync(
                    $"Remediation — {body.SystemId[..Math.Min(8, body.SystemId.Length)]}",
                    body.SystemId, "dashboard-user", ct);
            }

            // Default controlId to "AC-1" if not provided (required by KanbanService)
            var controlId = !string.IsNullOrWhiteSpace(body.ControlId) ? body.ControlId : "AC-1";

            FindingSeverity? severity = null;
            if (!string.IsNullOrWhiteSpace(body.Severity) &&
                Enum.TryParse<FindingSeverity>(body.Severity, true, out var sv))
                severity = sv;

            DateTime? dueDate = null;
            if (!string.IsNullOrWhiteSpace(body.DueDate) &&
                DateTime.TryParse(body.DueDate, out var dd))
                dueDate = DateTime.SpecifyKind(dd, DateTimeKind.Utc);

            try
            {
                var task = await kanbanService.CreateTaskAsync(
                    board.Id, body.Title, controlId, "dashboard-user",
                    description: body.Description,
                    severity: severity,
                    dueDate: dueDate,
                    cancellationToken: ct);

                return Results.Ok(new
                {
                    id = task.Id,
                    taskNumber = task.TaskNumber,
                    title = task.Title,
                    description = task.Description,
                    controlId = task.ControlId,
                    severity = task.Severity.ToString(),
                    status = task.Status.ToString(),
                    boardId = task.BoardId,
                    dueDate = task.DueDate.ToString("O"),
                    createdAt = task.CreatedAt.ToString("O"),
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "OPERATION_FAILED" });
            }
        })
        .WithName("CreateRemediationTask");

        // ─── Move Remediation Task (Kanban column change) ────────────────────
        app.MapPut("/api/dashboard/remediation/tasks/{taskId}/move", async (
            string taskId,
            MoveTaskRequest body,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<KanbanTaskStatus>(body.Status, true, out var newStatus))
                return Results.BadRequest(new ErrorResponse { Error = $"Invalid status: {body.Status}", ErrorCode = "INVALID_INPUT" });

            var task = await context.RemediationTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
            if (task == null)
                return Results.NotFound(new ErrorResponse { Error = "Task not found", ErrorCode = "NOT_FOUND" });

            var oldStatus = task.Status;
            task.Status = newStatus;
            task.UpdatedAt = DateTime.UtcNow;

            task.History.Add(new TaskHistoryEntry
            {
                TaskId = taskId,
                EventType = HistoryEventType.StatusChanged,
                OldValue = oldStatus.ToString(),
                NewValue = newStatus.ToString(),
                ActingUserId = "dashboard-user",
                ActingUserName = "Dashboard User",
                Timestamp = DateTime.UtcNow,
            });

            await context.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                id = task.Id,
                taskNumber = task.TaskNumber,
                previousStatus = oldStatus.ToString(),
                newStatus = newStatus.ToString(),
                updatedAt = task.UpdatedAt,
            });
        })
        .WithName("MoveRemediationTask");

        // ─── Deviation CRUD (Feature 035) ────────────────────────────────────
    }
}

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

// ─── #648 Decomposition: Roadmap domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapRoadmapRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
        group.MapGet("/systems/{systemId}/trends", async (
                string systemId,
                [AsParameters] TrendQuery query,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetTrendsAsync(systemId, query, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
            })
            .WithName("GetTrends");

        // ─── Implementation Roadmap (Feature 031) ────────────────────────────
        group.MapGet("/systems/{systemId}/roadmap", async (
                string systemId,
                bool? includeItems,
                Ato.Copilot.Core.Interfaces.Roadmap.IRoadmapService roadmapService,
                CancellationToken ct) =>
            {
                var roadmap = await roadmapService.GetRoadmapAsync(
                    systemId, includeItems ?? true, ct);

                if (roadmap is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = $"No active roadmap found for system {systemId}",
                        ErrorCode = "ROADMAP_NOT_FOUND",
                        Suggestion = "Generate a roadmap first using the compliance_generate_roadmap tool",
                    });

                var allItems = roadmap.Phases.SelectMany(p => p.Items).ToList();
                var completedItems = allItems.Count(i => i.Status == Ato.Copilot.Core.Models.Roadmap.ItemStatus.Complete);
                var overallCompletion = allItems.Count > 0 ? (double)completedItems / allItems.Count * 100 : 0;

                var dto = new RoadmapDto
                {
                    RoadmapId = roadmap.Id,
                    SystemId = roadmap.SystemId,
                    SystemName = roadmap.Name,
                    Status = roadmap.Status.ToString(),
                    BaselineLevel = roadmap.BaselineLevel,
                    TotalGaps = roadmap.TotalGaps,
                    TotalEstimatedEffortDays = roadmap.TotalEstimatedEffort,
                    TotalRiskPoints = roadmap.TotalRiskPoints,
                    OverallCompletionPercent = Math.Round(overallCompletion, 1),
                    Phases = roadmap.Phases.OrderBy(p => p.DisplayOrder).Select(p => new RoadmapPhaseDto
                    {
                        PhaseId = p.Id,
                        Name = p.Name,
                        DisplayOrder = p.DisplayOrder,
                        EstimatedEffortDays = p.EstimatedEffort,
                        RiskPoints = p.RiskPoints,
                        RiskReductionPercent = Math.Round(p.RiskReductionPercent, 1),
                        TargetStartWeek = p.TargetStartWeek,
                        TargetEndWeek = p.TargetEndWeek,
                        Status = p.Status.ToString(),
                        CompletedItemCount = p.CompletedItemCount,
                        TotalItemCount = p.TotalItemCount,
                        Items = (includeItems ?? true)
                            ? p.Items.OrderBy(i => i.DisplayOrder).Select(i => new RoadmapItemDto
                            {
                                ItemId = i.Id,
                                ControlId = i.ControlId,
                                ControlTitle = i.ControlTitle,
                                ControlFamily = i.ControlFamily,
                                GapType = i.GapType.ToString(),
                                Severity = i.Severity.ToString(),
                                RiskPoints = i.RiskPoints,
                                EstimatedEffortDays = i.EstimatedEffortDays,
                                AssignedRole = i.AssignedRole,
                                DependsOn = string.IsNullOrEmpty(i.DependsOn) ? null : i.DependsOn.Split(',', StringSplitOptions.TrimEntries).ToList(),
                                Status = i.Status.ToString(),
                                LinkedTaskId = i.LinkedTaskId
                            }).ToList()
                            : null
                    }).ToList(),
                    CreatedAt = roadmap.CreatedAt,
                    UpdatedAt = roadmap.UpdatedAt
                };

                return Results.Ok(dto);
            })
            .WithName("GetRoadmap");

        group.MapGet("/systems/{systemId}/roadmap/progress", async (
                string systemId,
                Ato.Copilot.Core.Interfaces.Roadmap.IRoadmapService roadmapService,
                CancellationToken ct) =>
            {
                var progress = await roadmapService.GetRoadmapProgressAsync(systemId, ct);
                if (progress is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = $"No active roadmap found for system {systemId}",
                        ErrorCode = "ROADMAP_NOT_FOUND",
                    });

                var dto = new RoadmapProgressDto
                {
                    RoadmapId = progress.RoadmapId,
                    SystemName = progress.SystemName,
                    OverallCompletionPercent = progress.OverallCompletionPercent,
                    ItemsCompleted = progress.ItemsCompleted,
                    ItemsTotal = progress.ItemsTotal,
                    RiskCurve = progress.RiskCurve.Select(p => new RiskCurvePointDto
                    {
                        Week = p.Week,
                        RiskPoints = p.RiskPoints,
                        RiskReductionPercent = p.RiskReductionPercent
                    }).ToList(),
                    PhaseProgress = progress.PhaseProgress.Select(p => new PhaseProgressDto
                    {
                        Name = p.Name,
                        DisplayOrder = p.DisplayOrder,
                        CompletionPercent = p.CompletionPercent,
                        Status = p.Status,
                        ActualRiskReductionPercent = p.ActualRiskReductionPercent,
                        IsOverdue = p.IsOverdue,
                        DaysOverdue = p.DaysOverdue
                    }).ToList()
                };

                return Results.Ok(dto);
            })
            .WithName("GetRoadmapProgress");

        group.MapGet("/systems/{systemId}/roadmap/export", async (
                string systemId,
                Ato.Copilot.Core.Interfaces.Roadmap.IRoadmapService roadmapService,
                CancellationToken ct) =>
            {
                try
                {
                    var pdfBytes = await roadmapService.ExportRoadmapPdfAsync(systemId, ct);
                    var fileName = $"Implementation_Roadmap_{DateTime.UtcNow:yyyy-MM-dd}.pdf";
                    return Results.File(pdfBytes, "application/pdf", fileName);
                }
                catch (NotImplementedException)
                {
                    return Results.StatusCode(501);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "ROADMAP_NOT_FOUND",
                    });
                }
            })
            .WithName("ExportRoadmapPdf");

        // ─── Todo List ───────────────────────────────────────────────────────
        group.MapGet("/systems/{systemId}/todos", async (
                string systemId,
                TodoService todoService,
                CancellationToken ct) =>
            {
                var result = await todoService.GetTodoListAsync(systemId, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
            })
            .WithName("GetTodoList");

        // ─── System Profile (Feature 046) ──────────────────────────────────
    }
}

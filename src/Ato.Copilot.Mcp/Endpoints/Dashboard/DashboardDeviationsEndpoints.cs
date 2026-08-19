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

// ─── #648 Decomposition: Deviations domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapDeviationRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
        group.MapGet("/systems/{systemId}/deviations", async (
                string systemId,
                string? type,
                string? status,
                string? severity,
                string? search,
                int? expiringWithinDays,
                int? page,
                int? pageSize,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                var result = await deviationService.ListDeviationsAsync(
                    systemId, type, status, severity, search, expiringWithinDays,
                    page ?? 1, pageSize ?? 50, ct);
                return Results.Ok(result);
            })
            .WithName("ListDeviations");

        group.MapGet("/systems/{systemId}/deviations/summary", async (
                string systemId,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                var result = await deviationService.GetDeviationSummaryAsync(systemId, ct);
                return Results.Ok(result);
            })
            .WithName("GetDeviationSummary");

        group.MapGet("/deviations/{deviationId}", async (
                string deviationId,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                var detail = await deviationService.GetDeviationDetailAsync(deviationId, ct);
                if (detail is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Deviation not found",
                        ErrorCode = "DEVIATION_NOT_FOUND",
                        Suggestion = "Check the deviation ID and try again",
                    });
                return Results.Ok(detail);
            })
            .WithName("GetDeviationDetail");

        group.MapPost("/systems/{systemId}/deviations", async (
                string systemId,
                CreateDeviationRequest request,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                try
                {
                    var deviation = await deviationService.CreateDeviationAsync(
                        systemId, request, "dashboard-user", ct);
                    return Results.Created($"/api/dashboard/deviations/{deviation.Id}", deviation);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("DUPLICATE_DEVIATION"))
                {
                    return Results.Conflict(new ErrorResponse
                    {
                        Error = "Duplicate active deviation",
                        ErrorCode = "DUPLICATE_DEVIATION",
                        Details = ex.Message,
                        Suggestion = "Revoke or wait for the existing deviation to expire before creating a new one",
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Invalid request",
                        ErrorCode = "VALIDATION_ERROR",
                        Details = ex.Message,
                    });
                }
            })
            .WithName("CreateDeviation");

        // ─── Deviation Workflow (Feature 035) ────────────────────────────────

        group.MapPut("/deviations/{deviationId}/review", async (
                string deviationId,
                ReviewDeviationRequest request,
                string? reviewerRole,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await deviationService.ReviewDeviationAsync(
                        deviationId, request, "dashboard-user", reviewerRole ?? "ISSM", ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("NOT_PENDING"))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Deviation is not pending",
                        ErrorCode = "NOT_PENDING",
                        Details = ex.Message,
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Deviation not found",
                        ErrorCode = "DEVIATION_NOT_FOUND",
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Invalid decision",
                        ErrorCode = "INVALID_DECISION",
                        Details = ex.Message,
                    });
                }
            })
            .WithName("ReviewDeviation");

        group.MapPut("/deviations/{deviationId}/revoke", async (
                string deviationId,
                RevokeDeviationRequest request,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await deviationService.RevokeDeviationAsync(
                        deviationId, request, "dashboard-user", ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("NOT_APPROVED"))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Deviation is not approved",
                        ErrorCode = "NOT_APPROVED",
                        Details = ex.Message,
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Deviation not found",
                        ErrorCode = "DEVIATION_NOT_FOUND",
                    });
                }
            })
            .WithName("RevokeDeviation");

        group.MapPut("/deviations/{deviationId}/extend", async (
                string deviationId,
                ExtendDeviationRequest request,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await deviationService.ExtendDeviationAsync(
                        deviationId, request, "dashboard-user", ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("NOT_APPROVED"))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Deviation must be approved to extend",
                        ErrorCode = "NOT_APPROVED",
                        Details = ex.Message,
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Deviation not found",
                        ErrorCode = "DEVIATION_NOT_FOUND",
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Invalid extension request",
                        ErrorCode = "VALIDATION_ERROR",
                        Details = ex.Message,
                    });
                }
            })
            .WithName("ExtendDeviation");

        // ─── SSP Export (Feature 037) ─────────────────────────────────────────

        // T010: POST /systems/{systemId}/exports — enqueue SSP export
    }
}

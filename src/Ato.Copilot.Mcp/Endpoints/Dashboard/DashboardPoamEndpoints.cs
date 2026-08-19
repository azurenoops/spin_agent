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

// ─── #648 Decomposition: Poam domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapPoamRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
        group.MapGet("/systems/{systemId}/poam", async (
            string systemId,
            int? page, int? pageSize, string? sortBy, string? sortDirection,
            string? status, string? catSeverity, bool? overdue,
            string? componentId, string? search,
            PoamService poamService,
            CancellationToken ct) =>
        {
            PoamStatus? statusFilter = null;
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<PoamStatus>(status, ignoreCase: true, out var s))
                statusFilter = s;

            CatSeverity? sevFilter = null;
            if (!string.IsNullOrEmpty(catSeverity))
                sevFilter = catSeverity.ToUpperInvariant() switch
                {
                    "I" or "CATI" => CatSeverity.CatI,
                    "II" or "CATII" => CatSeverity.CatII,
                    "III" or "CATIII" => CatSeverity.CatIII,
                    _ => null
                };

            var (items, totalCount) = await poamService.ListAsync(
                systemId, page ?? 1, pageSize ?? 25, sortBy ?? "scheduledCompletionDate",
                sortDirection ?? "asc", statusFilter, sevFilter, overdue, componentId, search, ct);

            var effectivePageSize = Math.Clamp(pageSize ?? 25, 1, 100);
            return Results.Ok(new
            {
                items = items.Select(MapToListItem),
                totalCount,
                page = page ?? 1,
                pageSize = effectivePageSize,
                totalPages = effectivePageSize > 0 ? (int)Math.Ceiling((double)totalCount / effectivePageSize) : 0
            });
        }).WithName("ListPoamItemsV2");

        // ── GET /poam — cross-system POA&M list
        group.MapGet("/poam", async (
            int? page, int? pageSize, string? sortBy, string? sortDirection,
            string? status, string? catSeverity, bool? overdue,
            string? componentId, string? search, string? systemId,
            PoamService poamService,
            CancellationToken ct) =>
        {
            PoamStatus? statusFilter = null;
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<PoamStatus>(status, ignoreCase: true, out var s))
                statusFilter = s;

            CatSeverity? sevFilter = null;
            if (!string.IsNullOrEmpty(catSeverity))
                sevFilter = catSeverity.ToUpperInvariant() switch
                {
                    "I" or "CATI" => CatSeverity.CatI,
                    "II" or "CATII" => CatSeverity.CatII,
                    "III" or "CATIII" => CatSeverity.CatIII,
                    _ => null
                };

            var (items, totalCount) = await poamService.ListAsync(
                systemId, page ?? 1, pageSize ?? 25, sortBy ?? "scheduledCompletionDate",
                sortDirection ?? "asc", statusFilter, sevFilter, overdue, componentId, search, ct);

            var effectivePageSize = Math.Clamp(pageSize ?? 25, 1, 100);
            return Results.Ok(new
            {
                items = items.Select(MapToListItem),
                totalCount,
                page = page ?? 1,
                pageSize = effectivePageSize,
                totalPages = effectivePageSize > 0 ? (int)Math.Ceiling((double)totalCount / effectivePageSize) : 0
            });
        }).WithName("ListPoamItemsCrossSystem");

        // ── GET /poam/{poamId} — detail
        group.MapGet("/poam/{poamId}", async (
            string poamId, PoamService poamService, CancellationToken ct) =>
        {
            var poam = await poamService.GetByIdAsync(poamId, includeHistory: true, ct);
            if (poam == null) return Results.NotFound(new ErrorResponse
            {
                Error = "POA&M item not found.",
                ErrorCode = "POAM_NOT_FOUND"
            });

            return Results.Ok(MapToDetail(poam));
        }).WithName("GetPoamDetail");

        // ── POST /systems/{systemId}/poam — create
        group.MapPost("/systems/{systemId}/poam", async (
            string systemId, Feature039CreatePoamRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Weakness))
                return Results.BadRequest(new ErrorResponse { Error = "Weakness is required.", ErrorCode = "INVALID_INPUT" });
            if (string.IsNullOrWhiteSpace(req.ControlId))
                return Results.BadRequest(new ErrorResponse { Error = "ControlId is required.", ErrorCode = "INVALID_INPUT" });
            if (string.IsNullOrWhiteSpace(req.Poc))
                return Results.BadRequest(new ErrorResponse { Error = "POC is required.", ErrorCode = "INVALID_INPUT" });

            if (!Enum.TryParse<CatSeverity>("Cat" + req.CatSeverity, ignoreCase: true, out var sevEnum))
                return Results.BadRequest(new ErrorResponse { Error = $"Invalid CatSeverity: {req.CatSeverity}.", ErrorCode = "INVALID_INPUT" });

            var milestones = req.Milestones?.Select(m => (m.Description, m.TargetDate));

            var poam = await poamService.CreateAsync(
                systemId, req.Weakness, req.WeaknessSource ?? "Manual", req.ControlId,
                sevEnum, req.Poc, req.ScheduledCompletionDate,
                req.PocEmail, req.ResourcesRequired, req.CostEstimate, req.Comments,
                req.FindingId, "mcp-user", req.ComponentIds, milestones, ct);

            return Results.Created($"/api/dashboard/poam/{poam.Id}", MapToDetail(poam));
        }).WithName("CreatePoamItemV2");

        // ── PUT /poam/{poamId} — update
        group.MapPut("/poam/{poamId}", async (
            string poamId, Feature039UpdatePoamRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (!Guid.TryParse(req.RowVersion, out var rv))
                return Results.BadRequest(new ErrorResponse { Error = "Valid rowVersion is required.", ErrorCode = "INVALID_INPUT" });

            try
            {
                var updated = await poamService.UpdateAsync(poamId, rv, poam =>
                {
                    if (req.Weakness != null) poam.Weakness = req.Weakness;
                    if (req.ControlId != null) poam.SecurityControlNumber = req.ControlId;
                    if (req.Poc != null) poam.PointOfContact = req.Poc;
                    if (req.PocEmail != null) poam.PocEmail = req.PocEmail;
                    if (req.Comments != null) poam.Comments = req.Comments;
                    if (req.ResourcesRequired != null) poam.ResourcesRequired = req.ResourcesRequired;
                    if (req.ScheduledCompletionDate.HasValue) poam.ScheduledCompletionDate = req.ScheduledCompletionDate.Value;
                    if (req.CostEstimate.HasValue) poam.CostEstimate = req.CostEstimate;
                }, ct: ct);

                return Results.Ok(MapToDetail(updated));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("CONCURRENCY"))
            {
                return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_CONCURRENCY_CONFLICT" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_NOT_FOUND" });
            }
        }).WithName("UpdatePoamItem");

        // ── GET /systems/{systemId}/poam/metrics
        group.MapGet("/systems/{systemId}/poam/metrics", async (
            string systemId, PoamService poamService, CancellationToken ct) =>
        {
            var metrics = await poamService.GetMetricsAsync(systemId, ct);
            return Results.Ok(metrics);
        }).WithName("GetPoamMetrics");

        // ── GET /poam/metrics — cross-system
        group.MapGet("/poam/metrics", async (
            PoamService poamService, CancellationToken ct) =>
        {
            var metrics = await poamService.GetMetricsAsync(null, ct);
            return Results.Ok(metrics);
        }).WithName("GetPoamMetricsCrossSystem");

        // ── POST /systems/{systemId}/poam/bulk-create — bulk create from findings
        group.MapPost("/systems/{systemId}/poam/bulk-create", async (
            string systemId, Feature039BulkCreateRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (req.FindingIds == null || req.FindingIds.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one findingId is required.", ErrorCode = "INVALID_INPUT" });

            var result = await poamService.BulkCreateFromFindingsAsync(
                systemId, req.FindingIds, req.ComponentIds, req.LinkRemediationTasks, "dashboard-user", ct);

            return Results.Ok(new
            {
                created = result.Created,
                skippedDuplicates = result.SkippedDuplicates,
                results = result.Results.Select(r => new
                {
                    findingId = r.FindingId,
                    poamId = r.PoamId,
                    status = r.Status
                })
            });
        }).WithName("BulkCreatePoamFromFindings");

        // ── PUT /poam/{poamId}/status — lifecycle status change
        group.MapPut("/poam/{poamId}/status", async (
            string poamId, Feature039StatusUpdateRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (!Guid.TryParse(req.RowVersion, out var rv))
                return Results.BadRequest(new ErrorResponse { Error = "Valid rowVersion is required.", ErrorCode = "INVALID_INPUT" });

            if (!Enum.TryParse<PoamStatus>(req.Status, ignoreCase: true, out var newStatus))
                return Results.BadRequest(new ErrorResponse { Error = $"Invalid status: {req.Status}.", ErrorCode = "INVALID_INPUT" });

            try
            {
                var updated = await poamService.UpdateStatusAsync(
                    poamId, newStatus, rv, "dashboard-user",
                    req.DelayReason, req.RevisedDate.HasValue ? req.RevisedDate.Value : null,
                    req.DeviationId, req.Comments, req.CascadeToTask, ct);

                return Results.Ok(new { poam = MapToDetail(updated) });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("CONCURRENCY"))
            {
                return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_CONCURRENCY_CONFLICT" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("INVALID_TRANSITION") ||
                                                       ex.Message.Contains("REQUIRED") ||
                                                       ex.Message.Contains("DEVIATION"))
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_LIFECYCLE_ERROR" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_NOT_FOUND" });
            }
        }).WithName("UpdatePoamStatusV2");

        // ── PUT /systems/{systemId}/poam/{poamId}/status — T005 #144 ──────────
        group.MapPut("/systems/{systemId}/poam/{poamId}/status", async (
            string systemId,
            string poamId,
            Feature039StatusUpdateRequest req,
            PoamService poamService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var owned = await context.PoamItems.AnyAsync(p => p.Id == poamId && p.RegisteredSystemId == systemId, ct);
            if (!owned)
                return Results.NotFound(new ErrorResponse { Error = "POAM not found for this system.", ErrorCode = "POAM_NOT_FOUND" });

            if (!Guid.TryParse(req.RowVersion, out var rv))
                return Results.BadRequest(new ErrorResponse { Error = "Valid rowVersion is required.", ErrorCode = "INVALID_INPUT" });

            if (!Enum.TryParse<PoamStatus>(req.Status, ignoreCase: true, out var newStatus))
                return Results.BadRequest(new ErrorResponse { Error = $"Invalid status: {req.Status}.", ErrorCode = "INVALID_INPUT" });

            try
            {
                var updated = await poamService.UpdateStatusAsync(
                    poamId, newStatus, rv, "dashboard-user",
                    req.DelayReason, req.RevisedDate.HasValue ? req.RevisedDate.Value : null,
                    req.DeviationId, req.Comments, req.CascadeToTask, ct);

                return Results.Ok(new { poam = MapToDetail(updated) });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                return Results.Conflict(new ErrorResponse { Error = "Concurrency conflict — reload and retry.", ErrorCode = "CONCURRENCY_CONFLICT" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("CONCURRENCY"))
            {
                return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "CONCURRENCY_CONFLICT" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("INVALID_TRANSITION") ||
                                                       ex.Message.Contains("REQUIRED") ||
                                                       ex.Message.Contains("DEVIATION"))
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_LIFECYCLE_ERROR" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_NOT_FOUND" });
            }
        }).WithName("UpdatePoamStatusSystemScoped");

        // ── POST /poam/bulk-status — bulk status updates
        group.MapPost("/poam/bulk-status", async (
            Feature039BulkStatusRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (req.PoamIds == null || req.PoamIds.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one poamId is required.", ErrorCode = "INVALID_INPUT" });

            if (!Enum.TryParse<PoamStatus>(req.Status, ignoreCase: true, out var newStatus))
                return Results.BadRequest(new ErrorResponse { Error = $"Invalid status: {req.Status}.", ErrorCode = "INVALID_INPUT" });

            var results = await poamService.BulkUpdateStatusAsync(
                req.PoamIds, newStatus, "dashboard-user", req.DelayReason, req.RevisedDate, req.Comments, ct);

            return Results.Ok(new
            {
                succeeded = results.Count(r => r.Success),
                failed = results.Count(r => !r.Success),
                results = results.Select(r => new { poamId = r.PoamId, success = r.Success, error = r.Error })
            });
        }).WithName("BulkUpdatePoamStatusV2");

        // ── PUT /remediation/poam/bulk-status — T004 #143 alias ───────────────
        group.MapPut("/remediation/poam/bulk-status", async (
            Feature039BulkStatusRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (req.PoamIds == null || req.PoamIds.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one poamId is required.", ErrorCode = "INVALID_INPUT" });

            if (!Enum.TryParse<PoamStatus>(req.Status, ignoreCase: true, out var newStatus))
                return Results.BadRequest(new ErrorResponse { Error = $"Invalid status: {req.Status}.", ErrorCode = "INVALID_INPUT" });

            var results = await poamService.BulkUpdateStatusAsync(
                req.PoamIds, newStatus, "dashboard-user", req.DelayReason, req.RevisedDate, req.Comments, ct);

            return Results.Ok(new
            {
                succeeded = results.Count(r => r.Success),
                failed = results.Count(r => !r.Success),
                results = results.Select(r => new { poamId = r.PoamId, success = r.Success, error = r.Error })
            });
        }).WithName("BulkUpdatePoamStatusRemediation");

        // ── POST /poam/{poamId}/components — link components
        group.MapPost("/poam/{poamId}/components", async (
            string poamId, Feature039LinkComponentsRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (req.ComponentIds == null || req.ComponentIds.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one componentId is required.", ErrorCode = "INVALID_INPUT" });

            try
            {
                await poamService.LinkComponentsAsync(poamId, req.ComponentIds, "dashboard-user", ct);
                return Results.Ok(new { linked = req.ComponentIds.Count });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_NOT_FOUND" });
            }
        }).WithName("LinkPoamComponents");

        // ── DELETE /poam/{poamId}/components — unlink components
        group.MapDelete("/poam/{poamId}/components", async (
            string poamId, [FromBody] Feature039UnlinkComponentsRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (req.ComponentIds == null || req.ComponentIds.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one componentId is required.", ErrorCode = "INVALID_INPUT" });

            try
            {
                await poamService.UnlinkComponentsAsync(poamId, req.ComponentIds, "dashboard-user", ct);
                return Results.Ok(new { unlinked = req.ComponentIds.Count });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_NOT_FOUND" });
            }
        }).WithName("UnlinkPoamComponents");

        // ── GET /components/{componentId}/poam — POA&Ms by component with risk summary
        group.MapGet("/components/{componentId}/poam", async (
            string componentId, PoamService poamService, CancellationToken ct) =>
        {
            var summary = await poamService.GetPoamsByComponentAsync(componentId, ct);
            return Results.Ok(new
            {
                componentId = summary.ComponentId,
                totalPoams = summary.TotalPoams,
                openCount = summary.OpenCount,
                overdueCount = summary.OverdueCount,
                highestSeverity = summary.HighestSeverity?.ToString().Replace("Cat", ""),
                items = summary.Items.Select(MapToListItem)
            });
        }).WithName("GetPoamsByComponent");

        // ── POST /poam/{poamId}/task — create remediation task from POA&M
        group.MapPost("/poam/{poamId}/task", async (
            string poamId, Feature039CreateTaskRequest req, PoamSyncService syncService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.BoardId))
                return Results.BadRequest(new ErrorResponse { Error = "boardId is required.", ErrorCode = "INVALID_INPUT" });

            try
            {
                var task = await syncService.CreateTaskFromPoamAsync(poamId, req.BoardId, "dashboard-user", ct);
                return Results.Ok(new { taskId = task.Id, poamId, linked = true });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "NOT_FOUND" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
            }
        }).WithName("CreateTaskFromPoam");

        // ── POST /poam/{poamId}/link-task — link existing task to POA&M
        group.MapPost("/poam/{poamId}/link-task", async (
            string poamId, Feature039LinkTaskRequest req, PoamSyncService syncService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.TaskId))
                return Results.BadRequest(new ErrorResponse { Error = "taskId is required.", ErrorCode = "INVALID_INPUT" });

            try
            {
                await syncService.LinkAsync(poamId, req.TaskId, "dashboard-user", ct);
                return Results.Ok(new { poamId, taskId = req.TaskId, linked = true });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "NOT_FOUND" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
            }
        }).WithName("LinkPoamTask");

        // ── DELETE /poam/{poamId}/unlink-task — unlink task from POA&M
        group.MapDelete("/poam/{poamId}/unlink-task", async (
            string poamId, PoamSyncService syncService, CancellationToken ct) =>
        {
            try
            {
                await syncService.UnlinkAsync(poamId, "dashboard-user", ct);
                return Results.Ok(new { poamId, unlinked = true });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "NOT_FOUND" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
            }
        }).WithName("UnlinkPoamTask");

        // ── GET /systems/{systemId}/poam/trend — trend analysis
        group.MapGet("/systems/{systemId}/poam/trend", async (
            string systemId,
            string? period,
            DateTime? startDate,
            DateTime? endDate,
            PoamService poamService,
            CancellationToken ct) =>
        {
            var trend = await poamService.GetTrendDataAsync(
                systemId, period ?? "monthly", startDate, endDate, ct);
            return Results.Ok(trend);
        }).WithName("GetPoamTrend");

        // ── GET /systems/{systemId}/poam/trend/export — PDF export
        group.MapGet("/systems/{systemId}/poam/trend/export", async (
            string systemId,
            string? period,
            DateTime? startDate,
            DateTime? endDate,
            PoamService poamService,
            CancellationToken ct) =>
        {
            var pdf = await poamService.ExportTrendReportPdfAsync(
                systemId, period ?? "monthly", startDate, endDate, ct);
            return Results.File(pdf, "application/pdf", $"poam-trend-{systemId}-{DateTime.UtcNow:yyyyMMdd}.pdf");
        }).WithName("ExportPoamTrendPdf");

        // ── GET /systems/{systemId}/ticketing — get config
        group.MapGet("/systems/{systemId}/ticketing", async (
            string systemId, TicketingService ticketingService, CancellationToken ct) =>
        {
            var config = await ticketingService.GetConfigAsync(systemId, ct);
            if (config == null) return Results.Ok(new { configured = false });
            return Results.Ok(new
            {
                configured = true,
                provider = config.Provider.ToString(),
                baseUrl = config.BaseUrl,
                projectKey = config.ProjectKeyOrTableName,
                syncEnabled = config.SyncEnabled,
            });
        }).WithName("GetTicketingConfig");

        // ── POST /systems/{systemId}/ticketing — configure
        group.MapPost("/systems/{systemId}/ticketing", async (
            string systemId, ConfigureTicketingRequest req, TicketingService ticketingService, CancellationToken ct) =>
        {
            try
            {
                if (!Enum.TryParse<Ato.Copilot.Core.Models.Poam.TicketingProvider>(req.Provider, true, out var provider))
                    return Results.BadRequest(new ErrorResponse { Error = $"Invalid provider: {req.Provider}", ErrorCode = "INVALID_INPUT" });

                var config = await ticketingService.ConfigureAsync(
                    systemId, provider, req.BaseUrl, req.ProjectKey, req.ApiKeySecretName, req.SyncEnabled, ct);
                return Results.Ok(new { configured = true, provider = config.Provider.ToString() });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
            }
        }).WithName("ConfigureTicketing");

        // ── POST /poam/{poamId}/sync-ticket — single sync
        group.MapPost("/poam/{poamId}/sync-ticket", async (
            string poamId, SyncTicketRequest? req, TicketingService ticketingService, CancellationToken ct) =>
        {
            try
            {
                var result = await ticketingService.SyncTicketAsync(poamId, req?.Direction ?? "push", ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "NOT_FOUND" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
            }
        }).WithName("SyncPoamTicket");

        // ── POST /systems/{systemId}/poam/bulk-sync — bulk sync
        group.MapPost("/systems/{systemId}/poam/bulk-sync", async (
            string systemId, SyncTicketRequest? req, TicketingService ticketingService, CancellationToken ct) =>
        {
            try
            {
                var results = await ticketingService.BulkSyncAsync(systemId, req?.Direction ?? "push", ct);
                return Results.Ok(new
                {
                    total = results.Count,
                    succeeded = results.Count(r => r.Success),
                    failed = results.Count(r => !r.Success),
                    results,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
            }
        }).WithName("BulkSyncTickets");

        // ── GET /systems/{systemId}/poam/export — export POA&M data
        group.MapGet("/systems/{systemId}/poam/export", async (
            string systemId,
            string format,
            string? status,
            string? catSeverity,
            bool? includeAll,
            PoamService poamService,
            CancellationToken ct) =>
        {
            try
            {
                byte[] data;
                string contentType;
                string fileName;

                switch (format.ToLowerInvariant())
                {
                    case "emass_excel":
                        data = await poamService.ExportEmassExcelAsync(systemId, status, catSeverity, includeAll ?? false, ct);
                        contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                        fileName = $"poam-{systemId}-{DateTime.UtcNow:yyyy-MM-dd}.xlsx";
                        break;
                    case "oscal_json":
                        data = await poamService.ExportOscalJsonAsync(systemId, status, catSeverity, includeAll ?? false, ct);
                        contentType = "application/json";
                        fileName = $"poam-{systemId}-{DateTime.UtcNow:yyyy-MM-dd}.oscal.json";
                        break;
                    case "csv":
                        data = await poamService.ExportCsvAsync(systemId, status, catSeverity, includeAll ?? false, ct);
                        contentType = "text/csv";
                        fileName = $"poam-{systemId}-{DateTime.UtcNow:yyyy-MM-dd}.csv";
                        break;
                    default:
                        return Results.BadRequest(new ErrorResponse { Error = $"Unsupported export format: {format}", ErrorCode = "EXPORT_FORMAT_INVALID" });
                }

                return Results.File(data, contentType, fileName);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        }).WithName("ExportPoam");

        // ─── Component-Centric Boundary: Azure Discovery (Feature 040) ───────
    }
}

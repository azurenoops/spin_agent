using System.Security.Claims;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ato.Copilot.Mcp.Endpoints;

public static class EmassWorkflowEndpoints
{
    private const long MaxUploadBytes = 50L * 1024 * 1024;
    private static readonly AuthorizeAttribute WorkflowWriter = new()
    {
        Roles = $"{ComplianceRoles.Auditor},{ComplianceRoles.SecurityLead},{ComplianceRoles.Administrator}",
    };

    public static IEndpointRouteBuilder MapEmassWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        MapRoutes(app.MapGroup("/api/systems/{systemId}/emass"));
        MapRoutes(app.MapGroup("/api/dashboard/systems/{systemId}/emass"));
        return app;
    }

    private static void MapRoutes(RouteGroupBuilder group)
    {
        group.WithTags("eMASS Workflow").DisableAntiforgery();

        group.MapGet("/status", async (
            string systemId,
            IEmassWorkflowStatusService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await service.GetStatusAsync(systemId, cancellationToken);
                return Results.Ok(Envelope(status, new { checkedAt = DateTimeOffset.UtcNow }));
            }
            catch (InvalidOperationException exception)
            {
                return NotFound("SYSTEM_NOT_FOUND", exception.Message);
            }
        }).RequireAuthorization(Policies.ComplianceReader);

        group.MapGet("/readiness", async (
            string systemId,
            IEmassExportReadinessService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var readiness = await service.CheckReadinessAsync(systemId, cancellationToken);
                return Results.Ok(Envelope(readiness));
            }
            catch (InvalidOperationException exception)
            {
                return NotFound("SYSTEM_NOT_FOUND", exception.Message);
            }
        }).RequireAuthorization(Policies.ComplianceReader);

        group.MapPost("/sync", async (
            string systemId,
            HttpRequest request,
            IEmassRoundTripSyncService service,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
                return BadRequest("INVALID_EXCEL_FORMAT", "A multipart Excel upload is required.");

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0 || file.Length > MaxUploadBytes)
                return BadRequest("INVALID_EXCEL_FORMAT", "The Excel file must be between 1 byte and 50 MB.");

            var acknowledge = bool.TryParse(request.Query["acknowledgeUnresolved"], out var parsed) && parsed;
            try
            {
                await using var stream = file.OpenReadStream();
                var result = await service.StartSyncAsync(
                    systemId, stream, acknowledge, cancellationToken);
                return Results.Ok(Envelope(result));
            }
            catch (UnresolvedEmassConflictsException exception)
            {
                return Results.Conflict(ErrorEnvelope(
                    "UNRESOLVED_CONFLICTS",
                    exception.Message,
                    new { unresolvedConflictCount = exception.Count }));
            }
            catch (EmassSystemIdMismatchException exception)
            {
                return BadRequest("SYSTEM_ID_MISMATCH", exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return BadRequest("INVALID_EXCEL_FORMAT", exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return NotFound("SYSTEM_NOT_FOUND", exception.Message);
            }
        }).RequireAuthorization(WorkflowWriter);

        group.MapGet("/conflicts", async (
            string systemId,
            string? status,
            string? batchId,
            int? limit,
            int? offset,
            IEmassRoundTripSyncService service,
            CancellationToken cancellationToken) =>
        {
            ConflictStatus? parsedStatus = ConflictStatus.Unresolved;
            if (string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
                parsedStatus = null;
            else if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<ConflictStatus>(status, true, out var statusValue))
                    return BadRequest("INVALID_STATUS", $"Unsupported conflict status '{status}'.");
                parsedStatus = statusValue;
            }

            var conflicts = await service.GetConflictsAsync(
                systemId, parsedStatus, batchId, limit ?? 50, offset ?? 0, cancellationToken);
            return Results.Ok(Envelope(conflicts, new
            {
                total = conflicts.Count,
                limit = limit ?? 50,
                offset = offset ?? 0,
            }));
        }).RequireAuthorization(Policies.ComplianceReader);

        group.MapPut("/conflicts/{conflictId}", async (
            string systemId,
            string conflictId,
            ResolveConflictRequest request,
            ClaimsPrincipal user,
            IEmassRoundTripSyncService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var conflict = await service.ResolveConflictAsync(
                    systemId,
                    conflictId,
                    request,
                    user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "dashboard-user",
                    cancellationToken);
                return Results.Ok(Envelope(conflict));
            }
            catch (EmassConflictAlreadyResolvedException exception)
            {
                return Results.Conflict(ErrorEnvelope("CONFLICT_ALREADY_RESOLVED", exception.Message));
            }
            catch (ArgumentException exception)
            {
                return BadRequest("INVALID_RESOLUTION", exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return NotFound("CONFLICT_NOT_FOUND", exception.Message);
            }
        }).RequireAuthorization(WorkflowWriter);
    }

    private static object Envelope<T>(T data, object? meta = null) => new
    {
        data,
        meta = meta ?? new { },
        errors = Array.Empty<object>(),
    };

    private static object ErrorEnvelope(string code, string message, object? data = null) => new
    {
        data,
        meta = new { },
        errors = new[] { new { code, message } },
    };

    private static IResult BadRequest(string code, string message) =>
        Results.BadRequest(ErrorEnvelope(code, message));

    private static IResult NotFound(string code, string message) =>
        Results.NotFound(ErrorEnvelope(code, message));
}
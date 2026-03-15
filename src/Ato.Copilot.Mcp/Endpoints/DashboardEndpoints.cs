using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ato.Copilot.Mcp.Dtos.Dashboard;
using Ato.Copilot.Mcp.Services;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// Maps all /api/dashboard/* REST endpoints for the Visual Compliance Dashboard.
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>
    /// Registers dashboard route group and all dashboard API endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard");

        // ─── Portfolio (US1) ─────────────────────────────────────────────────
        group.MapGet("/portfolio", async (
                [AsParameters] PortfolioQuery query,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetPortfolioAsync(query, ct);
                return Results.Ok(result);
            })
            .WithName("GetPortfolio");

        // ─── System Detail (US2) ─────────────────────────────────────────────
        group.MapGet("/systems/{systemId}", async (
                string systemId,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetSystemDetailAsync(systemId, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
            })
            .WithName("GetSystemDetail");

        group.MapGet("/systems/{systemId}/heatmap", async (
                string systemId,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetHeatmapAsync(systemId, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System or baseline not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Ensure the system has a control baseline configured",
                    });
            })
            .WithName("GetHeatmap");

        group.MapGet("/systems/{systemId}/heatmap/{familyCode}/controls", async (
                string systemId,
                string familyCode,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetHeatmapControlsAsync(systemId, familyCode, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System, baseline, or family not found",
                        ErrorCode = "FAMILY_NOT_FOUND",
                        Suggestion = "Verify the family code is part of this system's baseline",
                    });
            })
            .WithName("GetHeatmapControls");

        // ─── Gap Analysis (US4) ──────────────────────────────────────────────
        group.MapGet("/systems/{systemId}/gaps", async (
                string systemId,
                CapabilityService capService,
                CancellationToken ct) =>
            {
                var result = await capService.GetGapAnalysisAsync(systemId, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System or baseline not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Ensure the system has a control baseline configured",
                    });
            })
            .WithName("GetGapAnalysis");

        // ─── Components (US5) ────────────────────────────────────────────────
        group.MapGet("/systems/{systemId}/components", async (
                string systemId,
                [AsParameters] ComponentQuery query,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.GetComponentsAsync(systemId, query, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
            })
            .WithName("GetComponents");

        group.MapPost("/systems/{systemId}/components", async (
                string systemId,
                CreateComponentRequest request,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.CreateComponentAsync(systemId, request, "system", ct);
                return result is not null
                    ? Results.Created($"/api/dashboard/components/{result.Id}", result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
            })
            .WithName("CreateComponent");

        group.MapPut("/components/{id}", async (
                string id,
                CreateComponentRequest request,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.UpdateComponentAsync(id, request, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Component not found",
                        ErrorCode = "COMPONENT_NOT_FOUND",
                        Suggestion = "Check the component ID and try again",
                    });
            })
            .WithName("UpdateComponent");

        group.MapDelete("/components/{id}", async (
                string id,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.DeleteComponentAsync(id, "system", ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Component not found",
                        ErrorCode = "COMPONENT_NOT_FOUND",
                        Suggestion = "Check the component ID and try again",
                    });
            })
            .WithName("DeleteComponent");

        // ─── Capabilities (US3) ──────────────────────────────────────────────
        group.MapGet("/capabilities", async (
                [AsParameters] CapabilityQuery query,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.GetCapabilitiesAsync(query, ct);
                return Results.Ok(result);
            })
            .WithName("GetCapabilities");

        group.MapPost("/capabilities", async (
                CreateCapabilityRequest request,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.CreateCapabilityAsync(request, "system", ct);
                return result is not null
                    ? Results.Created($"/api/dashboard/capabilities/{result.Id}", result)
                    : Results.Conflict(new ErrorResponse
                    {
                        Error = "A capability with this name already exists",
                        ErrorCode = "CAPABILITY_NAME_DUPLICATE",
                        Suggestion = "Use a unique name or update the existing capability",
                    });
            })
            .WithName("CreateCapability");

        group.MapPut("/capabilities/{id}", async (
                string id,
                CreateCapabilityRequest request,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var (result, nameConflict) = await service.UpdateCapabilityAsync(id, request, "system", ct);
                if (nameConflict)
                    return Results.Conflict(new ErrorResponse
                    {
                        Error = "A capability with this name already exists",
                        ErrorCode = "CAPABILITY_NAME_DUPLICATE",
                        Suggestion = "Use a unique name or update the existing capability",
                    });
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Capability not found",
                        ErrorCode = "CAPABILITY_NOT_FOUND",
                        Suggestion = "Check the capability ID and try again",
                    });
            })
            .WithName("UpdateCapability");

        group.MapDelete("/capabilities/{id}", async (
                string id,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.DeleteCapabilityAsync(id, "system", ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Capability not found",
                        ErrorCode = "CAPABILITY_NOT_FOUND",
                        Suggestion = "Check the capability ID and try again",
                    });
            })
            .WithName("DeleteCapability");

        group.MapGet("/capabilities/{id}/mappings", async (
                string id,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.GetMappingsAsync(id, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Capability not found",
                        ErrorCode = "CAPABILITY_NOT_FOUND",
                        Suggestion = "Check the capability ID and try again",
                    });
            })
            .WithName("GetCapabilityMappings");

        group.MapPost("/capabilities/{id}/mappings", async (
                string id,
                CreateMappingsRequest request,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.CreateMappingsAsync(id, request, "system", ct);
                return result is not null
                    ? Results.Created($"/api/dashboard/capabilities/{id}/mappings", result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Capability not found",
                        ErrorCode = "CAPABILITY_NOT_FOUND",
                        Suggestion = "Check the capability ID and try again",
                    });
            })
            .WithName("CreateCapabilityMappings");

        // ─── Trends (US6) ───────────────────────────────────────────────────
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

        return app;
    }
}

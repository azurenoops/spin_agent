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

// ─── #648 Decomposition: Capabilities domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapCapabilityRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
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
                if (result is null)
                    return Results.Conflict(new ErrorResponse
                    {
                        Error = "A capability with this name already exists",
                        ErrorCode = "CAPABILITY_NAME_DUPLICATE",
                        Suggestion = "Use a unique name or update the existing capability",
                    });

                return Results.Created($"/api/dashboard/capabilities/{result.Id}", result);
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

        group.MapGet("/capabilities/{id}/impact-preview", async (
                string id,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.GetCapabilityImpactPreviewAsync(id, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Capability not found",
                        ErrorCode = "CAPABILITY_NOT_FOUND",
                        Suggestion = "Check the capability ID and try again",
                    });
            })
            .WithName("GetCapabilityImpactPreview");

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
                try
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
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "INVALID_MAPPING_REQUEST",
                        Suggestion = "Adjust the mapping request and try again",
                    });
                }
                catch (DbUpdateException)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "One or more mappings already exist for this capability and scope",
                        ErrorCode = "DUPLICATE_MAPPING",
                        Suggestion = "Remove duplicates from the request and try again",
                    });
                }
            })
            .WithName("CreateCapabilityMappings");

        group.MapPut("/capabilities/{id}/mappings/{mappingId}", async (
                string id,
                string mappingId,
                UpdateMappingRequest request,
                CapabilityService service,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await service.UpdateMappingAsync(id, mappingId, request, "system", ct);
                    return result is not null
                        ? Results.Ok(result)
                        : Results.NotFound(new ErrorResponse
                        {
                            Error = "Capability or mapping not found",
                            ErrorCode = "MAPPING_NOT_FOUND",
                            Suggestion = "Check capability and mapping IDs, then try again",
                        });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "INVALID_MAPPING_UPDATE",
                    });
                }
            })
            .WithName("UpdateCapabilityMapping");

        group.MapDelete("/capabilities/{id}/mappings/{mappingId}", async (
                string id,
                string mappingId,
                CapabilityService service,
                CancellationToken ct) =>
            {
                try
                {
                    var deleted = await service.DeleteMappingAsync(id, mappingId, "system", ct);
                    return deleted
                        ? Results.NoContent()
                        : Results.NotFound(new ErrorResponse
                        {
                            Error = "Capability or mapping not found",
                            ErrorCode = "MAPPING_NOT_FOUND",
                            Suggestion = "Check capability and mapping IDs, then try again",
                        });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "MAPPING_DELETE_CONFLICT",
                        Suggestion = "Remove dependent references first, then retry.",
                    });
                }
                catch (Exception ex)
                {
                    return Results.Problem(
                        title: "Failed to delete capability mapping",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status500InternalServerError);
                }
            })
            .WithName("DeleteCapabilityMapping");

        // ─── Feature 045: Capabilities Hub Import/Coverage Endpoints ────────
        group.MapPost("/capabilities/import/csp-profile", async (
                CspProfileImportRequest request,
                CapabilityImportService importService,
                CancellationToken ct) =>
            {
                try
                {
                    if (request.DryRun == true)
                    {
                        var preview = await importService.ImportCspProfilePreviewAsync(
                            request.ProfileId, request.ConflictResolution ?? "skip", ct);
                        return Results.Ok(preview);
                    }

                    var result = await importService.ImportCspProfileAsync(
                        request.ProfileId, request.ConflictResolution ?? "skip", ct);
                    return Results.Ok(result);
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = $"CSP profile '{request.ProfileId}' not found",
                        ErrorCode = "PROFILE_NOT_FOUND",
                        Suggestion = "Check the profile ID and try again",
                    });
                }
            })
            .WithName("ImportCspProfile");

        group.MapGet("/capabilities/coverage", async (
                bool? includePerSystem,
                bool? includePerFamily,
                CapabilityImportService importService,
                CancellationToken ct) =>
            {
                var result = await importService.ComputeCoverageAsync(
                    includePerSystem ?? false, includePerFamily ?? true, ct);
                return Results.Ok(result);
            })
            .WithName("GetOrgWideCoverage");

        group.MapGet("/capabilities/csp-profiles", (
                Ato.Copilot.Mcp.Services.CspProfileService cspProfileService) =>
            {
                var profiles = cspProfileService.GetProfiles();
                return Results.Ok(new
                {
                    profiles = profiles.Select(p => new
                    {
                        profileId = p.ProfileId,
                        name = p.Name,
                        provider = p.Provider,
                        baselineLevel = p.BaselineLevel,
                        description = p.Description,
                        controlCount = p.Controls.Count,
                        serviceCount = p.Services?.Count ?? 0,
                        version = p.Version
                    })
                });
            })
            .WithName("ListCapabilityCspProfiles");

        group.MapPost("/capabilities/import/crm", async (
                HttpRequest httpRequest,
                CapabilityImportService importService,
                CrmExportService crmExportService,
                CancellationToken ct) =>
            {
                var form = await httpRequest.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");
                if (file is null || file.Length == 0)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "No file uploaded",
                        ErrorCode = "FILE_REQUIRED",
                        Suggestion = "Upload a CSV or Excel file",
                    });

                var columnMappingJson = form["columnMapping"].ToString();
                var conflictResolution = form["conflictResolution"].ToString();
                var dryRunStr = form["dryRun"].ToString();
                var dryRun = dryRunStr.Equals("true", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(conflictResolution)) conflictResolution = "skip";

                // Parse file
                CrmExportService.ImportParseResult parsed;
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                using var stream = file.OpenReadStream();
                if (ext is ".xlsx" or ".xls")
                    parsed = crmExportService.ParseExcel(stream);
                else
                    parsed = crmExportService.ParseCsv(stream);

                // Determine column mapping
                Dictionary<string, string> mapping;
                if (!string.IsNullOrEmpty(columnMappingJson))
                {
                    mapping = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(columnMappingJson)
                        ?? new Dictionary<string, string>();
                }
                else
                {
                    // Auto-detect: use exact column names
                    mapping = new Dictionary<string, string>
                    {
                        ["controlId"] = parsed.Columns.FirstOrDefault(c => c.Contains("control", StringComparison.OrdinalIgnoreCase)) ?? "controlId",
                        ["inheritanceType"] = parsed.Columns.FirstOrDefault(c => c.Contains("inheritance", StringComparison.OrdinalIgnoreCase)) ?? "inheritanceType",
                        ["provider"] = parsed.Columns.FirstOrDefault(c => c.Contains("provider", StringComparison.OrdinalIgnoreCase)) ?? "provider",
                        ["customerResponsibility"] = parsed.Columns.FirstOrDefault(c => c.Contains("responsibility", StringComparison.OrdinalIgnoreCase)) ?? "customerResponsibility",
                    };
                }

                // Map parsed rows to CrmImportRow using column mapping
                var rows = parsed.Rows.Select(row => new CrmImportRow
                {
                    ControlId = mapping.TryGetValue("controlId", out var cidCol) && row.TryGetValue(cidCol, out var cid) ? cid : "",
                    InheritanceType = mapping.TryGetValue("inheritanceType", out var itCol) && row.TryGetValue(itCol, out var it) ? it : "",
                    Provider = mapping.TryGetValue("provider", out var pCol) && row.TryGetValue(pCol, out var p) ? p : null,
                    CustomerResponsibility = mapping.TryGetValue("customerResponsibility", out var crCol) && row.TryGetValue(crCol, out var cr) ? cr : null,
                }).Where(r => !string.IsNullOrWhiteSpace(r.ControlId)).ToList();

                if (dryRun)
                {
                    var preview = await importService.ImportCrmPreviewAsync(
                        file.FileName, rows, conflictResolution, ct);
                    preview.DetectedColumns = parsed.Columns;
                    preview.SampleRows = parsed.SampleRows;
                    return Results.Ok(preview);
                }

                var result = await importService.ImportCrmAsync(
                    file.FileName, rows, conflictResolution, ct);
                return Results.Ok(result);
            })
            .DisableAntiforgery()
            .WithName("ImportCrm");

        // ─── Trends (US6) ───────────────────────────────────────────────────
    }
}

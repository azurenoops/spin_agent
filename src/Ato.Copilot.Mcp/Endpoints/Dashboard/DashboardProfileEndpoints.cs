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

// ─── #648 Decomposition: Profile domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapProfileRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
        group.MapGet("/systems/{systemId}/profile", async (
                string systemId,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await profileService.GetProfileOverviewAsync(systemId, ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("SYSTEM_NOT_FOUND"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
                }
            })
            .WithName("GetSystemProfile");

        group.MapGet("/systems/{systemId}/profile/{sectionType}", async (
                string systemId,
                string sectionType,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                if (!Enum.TryParse<ProfileSectionType>(sectionType, true, out var parsedType))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = $"Invalid section type '{sectionType}'",
                        ErrorCode = "INVALID_INPUT",
                    });

                var result = await profileService.GetSectionDetailAsync(systemId, parsedType, ct);
                if (result is null)
                {
                    // Return a synthesized "not started" section matching what the frontend expects
                    return Results.Ok(new
                    {
                        id = (string?)null,
                        sectionType = parsedType.ToString(),
                        governanceStatus = "NotStarted",
                        draftContent = (string?)null,
                        approvedContent = (string?)null,
                        completionPercentage = 0,
                        lastEditedBy = (string?)null,
                        lastEditedAt = (string?)null,
                        submittedBy = (string?)null,
                        submittedAt = (string?)null,
                        reviewedBy = (string?)null,
                        reviewedAt = (string?)null,
                        reviewerComments = (string?)null,
                        userCategories = Array.Empty<object>(),
                        dataTypeEntries = Array.Empty<object>(),
                        ppsEntries = Array.Empty<object>(),
                        leveragedAuthorizations = Array.Empty<object>(),
                    });
                }

                return Results.Ok(new
                {
                    id = result.Id,
                    sectionType = result.SectionType.ToString(),
                    governanceStatus = result.GovernanceStatus.ToString(),
                    draftContent = result.DraftContent,
                    approvedContent = result.ApprovedContent,
                    completionPercentage = result.CompletionPercentage,
                    lastEditedBy = result.LastEditedBy,
                    lastEditedAt = result.LastEditedAt?.ToString("O"),
                    submittedBy = result.SubmittedBy,
                    submittedAt = result.SubmittedAt?.ToString("O"),
                    reviewedBy = result.ReviewedBy,
                    reviewedAt = result.ReviewedAt?.ToString("O"),
                    reviewerComments = result.ReviewerComments,
                    userCategories = result.UserCategories.OrderBy(c => c.SortOrder).Select(c => new
                    {
                        c.Id, categoryName = c.CategoryName, description = c.Description,
                        approximateCount = c.ApproximateCount, accessMethod = c.AccessMethod,
                        dataSensitivityLevel = c.DataSensitivityLevel, sortOrder = c.SortOrder,
                    }),
                    dataTypeEntries = result.DataTypeEntries.OrderBy(d => d.SortOrder).Select(d => new
                    {
                        d.Id, dataTypeName = d.DataTypeName, description = d.Description,
                        sensitivityClassification = d.SensitivityClassification,
                        source = d.Source, destination = d.Destination,
                        applicableRegulations = d.ApplicableRegulations, sortOrder = d.SortOrder,
                    }),
                    ppsEntries = result.PpsEntries.OrderBy(p => p.SortOrder).Select(p => new
                    {
                        p.Id, portOrRange = p.PortOrRange, protocol = p.Protocol,
                        serviceName = p.ServiceName, direction = p.Direction,
                        justification = p.Justification, sortOrder = p.SortOrder,
                    }),
                    leveragedAuthorizations = result.LeveragedAuthorizations.OrderBy(l => l.SortOrder).Select(l => new
                    {
                        l.Id, providerName = l.ProviderName, authorizationType = l.AuthorizationType,
                        authorizationDate = l.AuthorizationDate, coveredControlFamilies = l.CoveredControlFamilies,
                        sortOrder = l.SortOrder,
                    }),
                });
            })
            .WithName("GetProfileSection");

        group.MapPut("/systems/{systemId}/profile/{sectionType}", async (
                string systemId,
                string sectionType,
                SaveProfileSectionBody body,
                HttpContext httpContext,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                if (!Enum.TryParse<ProfileSectionType>(sectionType, true, out var parsedType))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = $"Invalid section type '{sectionType}'",
                        ErrorCode = "INVALID_INPUT",
                    });

                var userId = ResolveDashboardUserId(httpContext);
                var simulatedRole = ResolveSimulatedRmfRole(httpContext);
                try
                {
                    var result = await profileService.SaveDraftAsync(
                        systemId, parsedType, body.Content, userId, simulatedRole, ct);

                    return Results.Ok(new
                    {
                        id = result.Id,
                        sectionType = result.SectionType.ToString(),
                        governanceStatus = result.GovernanceStatus.ToString(),
                        draftContent = result.DraftContent,
                        approvedContent = result.ApprovedContent,
                        completionPercentage = result.CompletionPercentage,
                        lastEditedBy = result.LastEditedBy,
                        lastEditedAt = result.LastEditedAt?.ToString("O"),
                        submittedBy = result.SubmittedBy,
                        submittedAt = result.SubmittedAt?.ToString("O"),
                        reviewedBy = result.ReviewedBy,
                        reviewedAt = result.ReviewedAt?.ToString("O"),
                        reviewerComments = result.ReviewerComments,
                        userCategories = Array.Empty<object>(),
                        dataTypeEntries = Array.Empty<object>(),
                        ppsEntries = Array.Empty<object>(),
                        leveragedAuthorizations = Array.Empty<object>(),
                    });
                }
                catch (InvalidOperationException ex)
                {
                    var code = ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
                    var statusCode = code switch
                    {
                        "UNAUTHORIZED" => StatusCodes.Status403Forbidden,
                        "SYSTEM_NOT_FOUND" => StatusCodes.Status404NotFound,
                        _ => StatusCodes.Status400BadRequest,
                    };
                    return Results.Json(new ErrorResponse { Error = ex.Message, ErrorCode = code }, statusCode: statusCode);
                }
            })
            .WithName("SaveProfileSection");

        group.MapPost("/systems/{systemId}/profile/submit", async (
                string systemId,
                SubmitSectionsBody body,
                HttpContext httpContext,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                var userId = ResolveDashboardUserId(httpContext);
                var simulatedRole = ResolveSimulatedRmfRole(httpContext);
                try
                {
                    var sectionTypes = body.SectionTypes?.Select(s =>
                    {
                        Enum.TryParse<ProfileSectionType>(s, true, out var t);
                        return t;
                    }).ToList();

                    if (string.Equals(body.Action, "withdraw", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = await profileService.WithdrawSectionAsync(systemId, sectionTypes, userId, simulatedRole, ct);
                        return Results.Ok(new
                        {
                            withdrawnSections = result.WithdrawnSections.Select(s => s.ToString()),
                            skippedSections = result.SkippedSections.Select(s => new { sectionType = s.SectionType.ToString(), s.Reason }),
                            withdrawnBy = result.WithdrawnBy,
                            withdrawnAt = result.WithdrawnAt.ToString("O"),
                        });
                    }
                    else
                    {
                        var result = await profileService.SubmitForReviewAsync(systemId, sectionTypes, userId, simulatedRole, ct);
                        return Results.Ok(new
                        {
                            submittedSections = result.SubmittedSections.Select(s => s.ToString()),
                            skippedSections = result.SkippedSections.Select(s => new { sectionType = s.SectionType.ToString(), s.Reason }),
                            submittedBy = result.SubmittedBy,
                            submittedAt = result.SubmittedAt.ToString("O"),
                        });
                    }
                }
                catch (InvalidOperationException ex)
                {
                    var code = ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
                    var statusCode = code switch
                    {
                        "UNAUTHORIZED" => StatusCodes.Status403Forbidden,
                        _ => StatusCodes.Status400BadRequest,
                    };
                    return Results.Json(new ErrorResponse { Error = ex.Message, ErrorCode = code }, statusCode: statusCode);
                }
            })
            .WithName("SubmitProfileSections");

        group.MapPost("/systems/{systemId}/profile/{sectionType}/review", async (
                string systemId,
                string sectionType,
                ReviewSectionBody body,
                HttpContext httpContext,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                if (!Enum.TryParse<ProfileSectionType>(sectionType, true, out var parsedType))
                    return Results.BadRequest(new ErrorResponse { Error = $"Invalid section type", ErrorCode = "INVALID_INPUT" });

                var userId = ResolveDashboardUserId(httpContext);
                var simulatedRole = ResolveSimulatedRmfRole(httpContext);
                var decision = body.Decision.Equals("approve", StringComparison.OrdinalIgnoreCase)
                    ? ReviewDecision.Approve
                    : ReviewDecision.RequestRevision;

                try
                {
                    var result = await profileService.ReviewSectionAsync(
                        systemId, parsedType, decision, userId, body.Comments, simulatedRole, ct);
                    return Results.Ok(new
                    {
                        sectionType = result.SectionType.ToString(),
                        newStatus = result.GovernanceStatus.ToString(),
                        reviewedBy = result.ReviewedBy,
                        reviewedAt = result.ReviewedAt?.ToString("O"),
                    });
                }
                catch (InvalidOperationException ex)
                {
                    var code = ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
                    var statusCode = code switch
                    {
                        "UNAUTHORIZED" => StatusCodes.Status403Forbidden,
                        "COMMENTS_REQUIRED" => StatusCodes.Status400BadRequest,
                        _ => StatusCodes.Status400BadRequest,
                    };
                    return Results.Json(new ErrorResponse { Error = ex.Message, ErrorCode = code }, statusCode: statusCode);
                }
            })
            .WithName("ReviewProfileSection");

        group.MapPost("/systems/{systemId}/profile/batch-approve", async (
                string systemId,
                HttpContext httpContext,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                var userId = ResolveDashboardUserId(httpContext);
                var simulatedRole = ResolveSimulatedRmfRole(httpContext);
                try
                {
                    var result = await profileService.BatchApproveSectionsAsync(systemId, userId, simulatedRole, ct);
                    return Results.Ok(new
                    {
                        approvedSections = result.ApprovedSections.Select(s => s.ToString()),
                        approvedCount = result.ApprovedCount,
                        reviewedBy = result.ReviewedBy,
                        reviewedAt = result.ReviewedAt.ToString("O"),
                    });
                }
                catch (InvalidOperationException ex)
                {
                    var code = ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
                    return Results.Json(new ErrorResponse { Error = ex.Message, ErrorCode = code }, statusCode: StatusCodes.Status403Forbidden);
                }
            })
            .WithName("BatchApproveProfile");

        group.MapGet("/systems/{systemId}/profile/completeness", async (
                string systemId,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await profileService.GetCompletenessAsync(systemId, ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("SYSTEM_NOT_FOUND"))
                {
                    return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "SYSTEM_NOT_FOUND" });
                }
            })
            .WithName("GetProfileCompleteness");

        group.MapGet("/systems/{systemId}/profile/todos", async (
                string systemId,
                HttpContext httpContext,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                var userId = ResolveDashboardUserId(httpContext);
                var result = await profileService.GetProfileTodosAsync(systemId, userId, ct);
                return Results.Ok(result);
            })
            .WithName("GetProfileTodos");


static string ResolveDashboardUserId(HttpContext httpContext)
{
    var userId = httpContext.User?.Identity?.Name;
    return string.IsNullOrWhiteSpace(userId)
        || string.Equals(userId, "anonymous", StringComparison.OrdinalIgnoreCase)
        ? "dashboard-user"
        : userId;
}

static RmfRole? ResolveSimulatedRmfRole(HttpContext httpContext)
{
    if (httpContext.User?.Identity?.IsAuthenticated == true)
        return null;

    if (!httpContext.Request.Headers.TryGetValue("X-Simulated-Role", out var rawRole))
        return null;

    return rawRole.ToString() switch
    {
        "MissionOwner" => RmfRole.MissionOwner,
        "ISSM" => RmfRole.Issm,
        "Engineer" => RmfRole.SystemOwner,
        "SystemOwner" => RmfRole.SystemOwner,
        _ => null,
    };
}
        // ─── Boundary Definitions (Feature 033) ─────────────────────────────
    }
}

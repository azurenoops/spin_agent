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
    /// <summary>Mission Owner content kept separate from canonical control narratives.</summary>
    private sealed record BusinessContextResponse(
        string Id, string ControlId, string Content, string GovernanceStatus,
        string AuthoredBy, DateTimeOffset AuthoredAt, string? ReviewerComments);

    /// <summary>Owner-authored business context, limited to the persisted content size.</summary>
    private sealed record SaveBusinessContextBody(string? Content);

    /// <summary>ISSM request to flag or unflag a control for owner input.</summary>
    private sealed record SetBusinessContextFlagBody(string? ControlId, bool? IsFlagged);

    private static async Task<IResult?> ValidateBusinessContextTargetAsync(
        AtoCopilotContext db, string systemId, string? controlId, CancellationToken ct)
    {
        if (!await db.RegisteredSystems.AnyAsync(system => system.Id == systemId && system.IsActive, ct))
            return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "SYSTEM_NOT_FOUND" });
        if (controlId is not null && !await db.ControlImplementations.AnyAsync(control =>
                control.RegisteredSystemId == systemId && control.ControlId == controlId, ct))
            return Results.NotFound(new ErrorResponse { Error = "Control not found", ErrorCode = "CONTROL_NOT_FOUND" });
        return null;
    }

    private static BusinessContextResponse ToBusinessContextResponse(BusinessContextDraft draft, string controlId) =>
        new(draft.Id, controlId, draft.Content, draft.GovernanceStatus.ToString(),
            draft.AuthoredBy, draft.AuthoredAt, draft.ReviewerComments);

    private static int? BusinessContextErrorStatus(InvalidOperationException exception) =>
        exception.Message.Split(':', 2)[0] switch
        {
            "UNAUTHORIZED" => StatusCodes.Status403Forbidden,
            "SYSTEM_NOT_FOUND" or "CONTROL_NOT_FOUND" => StatusCodes.Status404NotFound,
            "CONCURRENCY_CONFLICT" => StatusCodes.Status409Conflict,
            _ => null
        };

    private static void MapProfileRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app, ICurrentUserService currentUser)
    {
        group.MapGet("/systems/{systemId}/business-context/flagged-controls", async (
                string systemId, AtoCopilotContext db, ISystemProfileService profileService, CancellationToken ct) =>
            {
                var error = await ValidateBusinessContextTargetAsync(db, systemId, null, ct);
                if (error is not null) return error;
                return Results.Ok(await profileService.GetFlaggedControlsAsync(systemId, ct));
            })
            .WithName("GetBusinessContextFlaggedControls")
            .Produces<List<FlaggedControlItem>>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/systems/{systemId}/business-context/{controlId}", async (
                string systemId, string controlId, AtoCopilotContext db,
                ISystemProfileService profileService, CancellationToken ct) =>
            {
                var error = await ValidateBusinessContextTargetAsync(db, systemId, controlId, ct);
                if (error is not null) return error;

                var draft = await profileService.GetBusinessContextAsync(systemId, controlId, ct);
                if (draft is null)
                    return Results.Json(System.Text.Json.JsonSerializer.SerializeToElement<object?>(null));
                return Results.Json(ToBusinessContextResponse(draft, controlId));
            })
            .WithName("GetBusinessContext")
            .Produces<BusinessContextResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPut("/systems/{systemId}/business-context/{controlId}", async (
                string systemId, string controlId, SaveBusinessContextBody body,
                AtoCopilotContext db, ISystemProfileService profileService, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.Content) || body.Content.Length > 8000)
                    return Results.BadRequest(new ErrorResponse { Error = "Content must contain 1 to 8000 characters", ErrorCode = "INVALID_INPUT" });
                var error = await ValidateBusinessContextTargetAsync(db, systemId, controlId, ct);
                if (error is not null) return error;
                try
                {
                    var draft = await profileService.SaveBusinessContextAsync(systemId, controlId, body.Content, currentUser.CurrentUserId, ct);
                    return Results.Ok(ToBusinessContextResponse(draft, controlId));
                }
                catch (InvalidOperationException ex) when (BusinessContextErrorStatus(ex).HasValue)
                {
                    return Results.Json(new ErrorResponse { Error = ex.Message, ErrorCode = ex.Message.Split(':', 2)[0] },
                        statusCode: BusinessContextErrorStatus(ex));
                }
            })
            .WithName("SaveBusinessContext")
            .WithMetadata(new Ato.Copilot.Mcp.Authorization.WorkspaceAuthorizedEndpoint())
            .Produces<BusinessContextResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapPost("/systems/{systemId}/business-context/flags", async (
                string systemId, SetBusinessContextFlagBody body,
                AtoCopilotContext db, ISystemProfileService profileService, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.ControlId) || !body.IsFlagged.HasValue)
                    return Results.BadRequest(new ErrorResponse { Error = "Control ID and isFlagged are required", ErrorCode = "INVALID_INPUT" });
                var error = await ValidateBusinessContextTargetAsync(db, systemId, body.ControlId, ct);
                if (error is not null) return error;
                try
                {
                    await profileService.SetControlFlagAsync(systemId, body.ControlId, body.IsFlagged.Value, currentUser.CurrentUserId, ct);
                    return Results.NoContent();
                }
                catch (InvalidOperationException ex) when (BusinessContextErrorStatus(ex).HasValue)
                {
                    return Results.Json(new ErrorResponse { Error = ex.Message, ErrorCode = ex.Message.Split(':', 2)[0] },
                        statusCode: BusinessContextErrorStatus(ex));
                }
            })
            .WithName("SetBusinessContextFlag")
            .WithMetadata(new Ato.Copilot.Mcp.Authorization.WorkspaceAuthorizedEndpoint())
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

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

                var canEditProfile = await profileService.CanEditProfileAsync(
                    systemId, currentUser.CurrentUserId, ResolveSimulatedRmfRole(httpContext), ct);
                var result = await profileService.GetSectionDetailAsync(systemId, parsedType, ct);
                if (result is null)
                {
                    // Return a synthesized "not started" section matching what the frontend expects
                    return Results.Ok(new
                    {
                        id = (string?)null,
                        canEditProfile,
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
                    canEditProfile,
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

                var userId = currentUser.CurrentUserId;
                var simulatedRole = ResolveSimulatedRmfRole(httpContext);
                try
                {
                    var result = await profileService.SaveDraftAsync(
                        systemId, parsedType, body.Content, userId, simulatedRole, ct);

                    return Results.Ok(new
                    {
                        id = result.Id,
                        canEditProfile = await profileService.CanEditProfileAsync(systemId, userId, simulatedRole, ct),
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
            .WithName("SaveProfileSection")
            .WithMetadata(new Ato.Copilot.Mcp.Authorization.WorkspaceAuthorizedEndpoint());

        group.MapPost("/systems/{systemId}/profile/submit", async (
                string systemId,
                SubmitSectionsBody body,
                HttpContext httpContext,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                var userId = currentUser.CurrentUserId;
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
            .WithName("SubmitProfileSections")
            .WithMetadata(new Ato.Copilot.Mcp.Authorization.WorkspaceAuthorizedEndpoint());

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

                var userId = currentUser.CurrentUserId;
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
            .WithName("ReviewProfileSection")
            .WithMetadata(new Ato.Copilot.Mcp.Authorization.WorkspaceAuthorizedEndpoint());

        group.MapPost("/systems/{systemId}/profile/batch-approve", async (
                string systemId,
                HttpContext httpContext,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                var userId = currentUser.CurrentUserId;
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
            .WithName("BatchApproveProfile")
            .WithMetadata(new Ato.Copilot.Mcp.Authorization.WorkspaceAuthorizedEndpoint());

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
                var userId = currentUser.CurrentUserId;
                var result = await profileService.GetProfileTodosAsync(systemId, userId, ct);
                return Results.Ok(result);
            })
            .WithName("GetProfileTodos");

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

using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>HTTP routes for policy and technical narrative authoring.</summary>
public static class NarrativeDualEndpoints
{
    public static IEndpointRouteBuilder MapNarrativeDualEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .WithTags("Narrative")
            .RequireAuthorization();

        group.MapGet("/systems/{systemId}/controls/{controlId}/narrative", GetDualNarrativeAsync)
            .WithName("GetDualNarrative");
        group.MapPatch("/systems/{systemId}/controls/{controlId}/narrative", PatchDualNarrativeAsync)
            .WithName("PatchDualNarrative").RequireWorkspaceOperation(SystemWorkspaceOperation.AuthorNarratives);
        group.MapPatch("/evidence/{artifactId}/classify", ClassifyEvidenceAsync)
            .WithName("ClassifyNarrativeEvidence").RequireWorkspaceOperation(SystemWorkspaceOperation.ManageEvidence);

        return app;
    }

    public static async Task<IResult> GetDualNarrativeAsync(
        string systemId,
        string controlId,
        IDualNarrativeService service,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await service.GetAsync(systemId, controlId, cancellationToken);
            return Success(response, stopwatch);
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("NARRATIVE_NOT_FOUND:"))
        {
            return Failure(StatusCodes.Status404NotFound, "NOT_FOUND", exception.Message[21..], stopwatch);
        }
    }

    public static async Task<IResult> PatchDualNarrativeAsync(
        string systemId,
        string controlId,
        HttpRequest request,
        IDualNarrativeService service,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var updatePolicy = TryReadNullableString(root, "policyNarrative", out var policyNarrative);
            var updateTechnical = TryReadNullableString(root, "technicalNarrative", out var technicalNarrative);
            int? expectedVersion = null;
            if (root.TryGetProperty("expectedVersion", out var version) && version.ValueKind != JsonValueKind.Null)
            {
                if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
                    throw new ArgumentException("expectedVersion must be an integer or null.");
                expectedVersion = number;
            }
            var response = await service.UpdateAsync(
                systemId,
                controlId,
                policyNarrative,
                updatePolicy,
                technicalNarrative,
                updateTechnical,
                userContext.Role,
                userContext.UserId,
                cancellationToken,
                expectedVersion);
            return Success(response, stopwatch);
        }
        catch (JsonException exception)
        {
            return Failure(StatusCodes.Status400BadRequest, "VALIDATION_ERROR", exception.Message, stopwatch);
        }
        catch (ArgumentException exception)
        {
            return Failure(StatusCodes.Status400BadRequest, "VALIDATION_ERROR", exception.Message, stopwatch);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(StatusCodes.Status403Forbidden, "FORBIDDEN", exception.Message, stopwatch);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.StartsWith("UNDER_REVIEW:") || exception.Message.StartsWith("CONCURRENCY_CONFLICT:"))
        {
            return Failure(StatusCodes.Status409Conflict, exception.Message.Split(':', 2)[0], exception.Message, stopwatch);
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("NARRATIVE_NOT_FOUND:"))
        {
            return Failure(StatusCodes.Status404NotFound, "NOT_FOUND", exception.Message[21..], stopwatch);
        }
    }

    public static async Task<IResult> ClassifyEvidenceAsync(
        string artifactId,
        EvidenceClassificationRequest request,
        IDualNarrativeService service,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!Enum.IsDefined(request.NarrativeType))
        {
            return Failure(
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "narrativeType is not a recognized value.",
                stopwatch);
        }

        try
        {
            var response = await service.ClassifyEvidenceAsync(
                artifactId,
                request.NarrativeType,
                request.Rationale,
                userContext.UserId,
                cancellationToken);
            return Success(response, stopwatch);
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("EVIDENCE_NOT_FOUND:"))
        {
            return Failure(StatusCodes.Status404NotFound, "NOT_FOUND", exception.Message[20..], stopwatch);
        }
    }

    private static bool TryReadNullableString(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }
        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (property.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"{propertyName} must be a string or null.");
        }

        value = property.GetString();
        return true;
    }

    private static IResult Success<T>(T data, Stopwatch stopwatch) => Results.Ok(new
    {
        status = "success",
        data,
        metadata = new
        {
            executionTimeMs = stopwatch.ElapsedMilliseconds,
            timestamp = DateTime.UtcNow,
            tool = (string?)null
        },
        error = (object?)null
    });

    private static IResult Failure(
        int statusCode,
        string errorCode,
        string message,
        Stopwatch stopwatch) => Results.Json(new
    {
        status = "error",
        data = (object?)null,
        metadata = new
        {
            executionTimeMs = stopwatch.ElapsedMilliseconds,
            timestamp = DateTime.UtcNow,
            tool = (string?)null
        },
        error = new { errorCode, message, suggestion = "Verify the request and your access, then try again." }
    }, statusCode: statusCode);
}

/// <summary>Manual evidence classification request.</summary>
public sealed record EvidenceClassificationRequest(
    EvidenceNarrativeType NarrativeType,
    string? Rationale);
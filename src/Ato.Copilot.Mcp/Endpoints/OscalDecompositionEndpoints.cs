using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// OSCAL control decomposition endpoints (Feature 076 — T013).
/// AI-assisted decomposition of a control narrative into OSCAL statement-level fragments,
/// with a human-approval gate before fragments are committed to the SSP.
///
/// Routes are nested under /api/systems/{systemId}/controls/{controlId}/oscal
/// to align with the MCP tool contract in specs/076-oscal-full-compliance/contracts/mcp-tools.md.
/// </summary>
public static class OscalDecompositionEndpoints
{
    public static IEndpointRouteBuilder MapOscalDecompositionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/systems/{systemId}/controls/{controlId}/oscal")
            .WithTags("OSCAL")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName)
            .DisableAntiforgery();

        // ── POST /decompose ───────────────────────────────────────────────────
        // Trigger AI decomposition of a control narrative.
        // If 'narrative' is not provided in the body the service will look it up from DB.
        group.MapPost("/decompose", async (
                string systemId,
                string controlId,
                DecomposeRequest? body,
                HttpContext http,
                IOscalDecompositionService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();

                // Narrative is optional in the contract; the service handles null by DB look-up.
                // We forward it as-is — an empty/whitespace string is treated the same as null
                // (the service will fall back to the stored ControlImplementation narrative).
                var narrative = body?.Narrative;
                if (string.IsNullOrWhiteSpace(narrative))
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = "NARRATIVE_REQUIRED",
                        message = "A non-empty 'narrative' is required to trigger decomposition.",
                        suggestion = "Provide the narrative text in the request body or ensure the control has a saved narrative in the database.",
                    }, statusCode: StatusCodes.Status400BadRequest);
                }

                try
                {
                    var draft = await service.DecomposeAsync(
                        tenantId.ToString(),
                        systemId,
                        controlId,
                        narrative,
                        actorId.ToString(),
                        ct);

                    return Results.Json(new { ok = true, data = draft },
                        statusCode: StatusCodes.Status202Accepted);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = "DECOMPOSITION_FAILED",
                        message = ex.Message,
                    }, statusCode: StatusCodes.Status500InternalServerError);
                }
            })
            .WithName("DecomposeControl");

        // ── GET /decomposition/draft ──────────────────────────────────────────
        // Retrieve the current pending decomposition draft for a control.
        group.MapGet("/decomposition/draft", async (
                string systemId,
                string controlId,
                HttpContext http,
                IOscalDecompositionService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();

                var draft = await service.GetDraftAsync(
                    tenantId.ToString(),
                    systemId,
                    controlId,
                    ct);

                if (draft is null)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = "DRAFT_NOT_FOUND",
                        message = $"No pending decomposition draft found for control '{controlId}' on system '{systemId}'.",
                        suggestion = "Trigger a new decomposition via POST /decompose first.",
                    }, statusCode: StatusCodes.Status404NotFound);
                }

                return Results.Ok(new { ok = true, data = draft });
            })
            .WithName("GetDecompositionDraft");

        // ── PUT /decomposition/approve ────────────────────────────────────────
        // Approve the pending draft — commits fragment descriptions to the ControlImplementation.
        group.MapPut("/decomposition/approve", async (
                string systemId,
                string controlId,
                HttpContext http,
                IOscalDecompositionService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();

                try
                {
                    var result = await service.ApproveAsync(
                        tenantId.ToString(),
                        systemId,
                        controlId,
                        actorId.ToString(),
                        ct);

                    return Results.Ok(new { ok = true, data = result });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = "DRAFT_NOT_FOUND",
                        message = ex.Message,
                        suggestion = "Trigger a new decomposition via POST /decompose first.",
                    }, statusCode: StatusCodes.Status404NotFound);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = "APPROVAL_FAILED",
                        message = ex.Message,
                    }, statusCode: StatusCodes.Status500InternalServerError);
                }
            })
            .WithName("ApproveDecomposition");

        // ── DELETE /decomposition/draft ───────────────────────────────────────
        // Discard the current pending draft without applying it.
        group.MapDelete("/decomposition/draft", async (
                string systemId,
                string controlId,
                HttpContext http,
                IOscalDecompositionService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();

                try
                {
                    await service.DiscardAsync(
                        tenantId.ToString(),
                        systemId,
                        controlId,
                        ct);

                    return Results.NoContent();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = "DRAFT_NOT_FOUND",
                        message = ex.Message,
                        suggestion = "There is no pending draft to discard for this control.",
                    }, statusCode: StatusCodes.Status404NotFound);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = "DISCARD_FAILED",
                        message = ex.Message,
                    }, statusCode: StatusCodes.Status500InternalServerError);
                }
            })
            .WithName("DiscardDecomposition");

        return app;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var raw = user.FindFirstValue("tid")
            ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
        return Guid.TryParse(raw, out tenantId);
    }

    private static bool TryGetSubject(ClaimsPrincipal user, out Guid subjectId)
    {
        var raw = user.FindFirstValue("oid") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out subjectId);
    }

    private static IResult Forbidden() => Results.Json(new
    {
        ok = false,
        errorCode = "AUTH_FORBIDDEN",
        message = "You do not have permission to perform OSCAL decomposition operations.",
        suggestion = "Sign in with an account that holds the Administrator role for your tenant.",
    }, statusCode: StatusCodes.Status403Forbidden);
}

/// <summary>HTTP request body for <c>POST /api/systems/{systemId}/controls/{controlId}/oscal/decompose</c>.</summary>
public sealed class DecomposeRequest
{
    /// <summary>
    /// The control implementation narrative to decompose.
    /// If omitted, the service will look up the narrative from the control's
    /// <c>ControlImplementation</c> record in the database.
    /// </summary>
    public string? Narrative { get; set; }
}

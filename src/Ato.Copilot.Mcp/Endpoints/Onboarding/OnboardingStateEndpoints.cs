using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints.Onboarding;

/// <summary>
/// Foundational onboarding-state endpoints (FR-006/FR-007/FR-008/FR-009).
/// Returns Constitution-VII envelopes of the form
/// <c>{ "ok": true, "data": &lt;…&gt; }</c> /
/// <c>{ "ok": false, "errorCode": "…", "message": "…", "suggestion": "…" }</c>.
/// </summary>
public static class OnboardingStateEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingStateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding")
            .WithTags("Onboarding")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName);

        group.MapGet("/state", async (
                HttpContext http,
                IOnboardingStateService stateService,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId))
                    return Forbidden();
                var state = await stateService.GetAsync(tenantId, ct);
                return Results.Ok(new { ok = true, data = ProjectState(state) });
            })
            .WithName("GetOnboardingState");

        group.MapPost("/start", async (
                HttpContext http,
                IOnboardingStateService stateService,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId))
                    return Forbidden();
                if (!TryGetSubject(http.User, out var actorId))
                    return Forbidden();

                var displayName = http.User.FindFirstValue("name")
                    ?? http.User.FindFirstValue(ClaimTypes.Name);
                var email = http.User.FindFirstValue("preferred_username")
                    ?? http.User.FindFirstValue(ClaimTypes.Email);

                var correlation = Guid.NewGuid();
                try
                {
                    var state = await stateService.StartAsync(
                        tenantId, actorId, displayName, email, correlation, ct);
                    return Results.Ok(new { ok = true, data = ProjectState(state) });
                }
                catch (InvalidOperationException ex)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, ex.Message);
                }
            })
            .WithName("StartOnboarding");

        group.MapPost("/steps/{stepName}/skip", async (
                string stepName,
                HttpContext http,
                IOnboardingStateService stateService,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId))
                    return Forbidden();
                if (!TryGetSubject(http.User, out var actorId))
                    return Forbidden();

                try
                {
                    await stateService.MarkStepSkippedAsync(
                        tenantId, stepName, durationMs: 0, actorId, Guid.NewGuid(), ct);
                    return Results.Ok(new { ok = true });
                }
                catch (ArgumentException ex)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, ex.Message);
                }
            })
            .WithName("SkipOnboardingStep");

        group.MapPost("/complete", async (
                HttpContext http,
                IOnboardingStateService stateService,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId))
                    return Forbidden();
                if (!TryGetSubject(http.User, out var actorId))
                    return Forbidden();

                try
                {
                    await stateService.CompleteOnboardingAsync(
                        tenantId, actorId, Guid.NewGuid(), ct);
                    var state = await stateService.GetAsync(tenantId, ct);
                    return Results.Ok(new { ok = true, data = ProjectState(state) });
                }
                catch (InvalidOperationException ex)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, ex.Message);
                }
            })
            .WithName("CompleteOnboarding");

        return app;
    }

    private static object ProjectState(TenantOnboardingState state) => new
    {
        tenantId = state.TenantId,
        status = state.Status.ToString(),
        lastStep = state.LastStep,
        startedAt = state.OnboardingStartedAt,
        completedAt = state.OnboardingCompletedAt,
        lastReRunAt = state.LastReRunAt,
        steps = state.StepCompletions
            .Select(s => new
            {
                step = s.StepName,
                status = s.Status.ToString(),
                completedAt = s.CompletedAt,
                durationMs = s.DurationMs,
            }),
    };

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

    private static IResult Forbidden() => Envelope.Failure(
        WizardErrorCodes.AuthForbidden,
        "You do not have permission to use the onboarding wizard.",
        suggestion: "Sign in with an account that holds the Administrator role for your tenant.");
}

/// <summary>
/// Constitution VII envelope helpers for wizard endpoints.
/// </summary>
public static class Envelope
{
    public static IResult Failure(string errorCode, string message, string? suggestion = null)
        => Results.Json(new
        {
            ok = false,
            errorCode,
            message,
            suggestion,
        }, statusCode: errorCode == WizardErrorCodes.AuthForbidden ? 403 : 400);
}

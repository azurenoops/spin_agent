using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints.Onboarding;

/// <summary>
/// Polling-fallback endpoint for wizard background jobs (FR-066, contracts/progress-events.md).
/// Returns the persisted <c>WizardJobStatus</c> row so the dashboard can survive
/// SignalR disconnects without losing job state.
/// </summary>
public static class WizardJobsEndpoints
{
    public static IEndpointRouteBuilder MapWizardJobsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding/jobs")
            .WithTags("Onboarding")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName);

        group.MapGet("/{jobId:guid}", async (
                Guid jobId,
                HttpContext http,
                IDbContextFactory<AtoCopilotContext> contextFactory,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId))
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = WizardErrorCodes.AuthForbidden,
                        message = "Tenant claim missing.",
                    }, statusCode: 403);

                await using var db = await contextFactory.CreateDbContextAsync(ct);
                var status = await db.WizardJobStatuses
                    .FirstOrDefaultAsync(s => s.Id == jobId && s.TenantId == tenantId, ct);
                if (status is null)
                    return Results.NotFound(new { ok = false, message = "Job not found." });

                return Results.Ok(new
                {
                    ok = true,
                    data = new
                    {
                        jobId = status.Id,
                        tenantId = status.TenantId,
                        jobType = status.JobType.ToString(),
                        status = status.Status.ToString(),
                        percent = status.Percent,
                        message = status.Message,
                        errorCode = status.ErrorCode,
                        suggestion = status.Suggestion,
                        enqueuedAt = status.EnqueuedAt,
                        startedAt = status.StartedAt,
                        finishedAt = status.FinishedAt,
                        result = status.Result,
                    },
                });
            })
            .WithName("GetWizardJob");

        return app;
    }

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var raw = user.FindFirstValue("tid")
            ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
        return Guid.TryParse(raw, out tenantId);
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints.Onboarding;

/// <summary>
/// Onboarding wizard Step 5 — Azure subscription scope endpoints
/// (FR-070..FR-077). Implements <c>contracts/azure-subscriptions-api.yaml</c>.
/// </summary>
public static class AzureSubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapAzureSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding/azure/subscriptions")
            .WithTags("Onboarding")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName)
            .DisableAntiforgery();

        // GET /api/onboarding/azure/subscriptions → enumerate via delegated ARM token
        group.MapGet("", async (
                HttpContext http,
                IAzureSubscriptionEnumerationService enumerator,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();

                var result = await enumerator.EnumerateAsync(tenantId, actorId, ct);
                if (result.IsSuccess)
                {
                    return Results.Ok(new { ok = true, data = result.Subscriptions });
                }

                return result.ErrorCode switch
                {
                    WizardErrorCodes.ArmConsentRequired => InsufficientClaims(http, result),
                    WizardErrorCodes.ArmTokenExpired => Results.Json(new
                    {
                        ok = false,
                        errorCode = result.ErrorCode,
                        message = result.Message,
                        suggestion = result.Suggestion,
                    }, statusCode: StatusCodes.Status401Unauthorized),
                    _ => Results.Json(new
                    {
                        ok = false,
                        errorCode = result.ErrorCode,
                        message = result.Message,
                        suggestion = result.Suggestion,
                    }, statusCode: StatusCodes.Status503ServiceUnavailable),
                };
            })
            .WithName("EnumerateAzureSubscriptions");

        // GET /registrations → current tenant registrations
        group.MapGet("/registrations", async (
                HttpContext http,
                IAzureSubscriptionRegistrationService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                var rows = await service.ListAsync(tenantId, ct);
                return Results.Ok(new { ok = true, data = rows });
            })
            .WithName("ListAzureSubscriptionRegistrations");

        // PUT /registrations → replace selected set
        group.MapPut("/registrations", async (
                HttpContext http,
                ReplaceRegistrationsRequest? body,
                IAzureSubscriptionEnumerationService enumerator,
                IAzureSubscriptionRegistrationService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();

                var ids = body?.SubscriptionIds ?? new List<Guid>();
                if (ids.Count == 0)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed,
                        "subscriptionIds is required.",
                        "Submit a non-empty array of subscription GUIDs the user has selected.");
                }

                var enumeration = await enumerator.EnumerateAsync(tenantId, actorId, ct);
                if (!enumeration.IsSuccess)
                {
                    return Envelope.Failure(enumeration.ErrorCode!, enumeration.Message ?? "ARM enumeration failed.", enumeration.Suggestion);
                }

                try
                {
                    var rows = await service.ReplaceAsync(
                        tenantId, ids, enumeration.Subscriptions!, actorId, ct);
                    return Results.Ok(new { ok = true, data = rows });
                }
                catch (InvalidOperationException ex)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, ex.Message);
                }
            })
            .WithName("ReplaceAzureSubscriptionRegistrations");

        // DELETE /registrations/{id}
        group.MapDelete("/registrations/{id:guid}", async (
                Guid id,
                HttpContext http,
                IAzureSubscriptionRegistrationService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                await service.RemoveAsync(tenantId, id, actorId, ct);
                return Results.NoContent();
            })
            .WithName("RemoveAzureSubscriptionRegistration");

        return app;
    }

    private static IResult InsufficientClaims(HttpContext http, AzureSubscriptionEnumerationResult result)
    {
        http.Response.Headers["WWW-Authenticate"] =
            "Bearer error=\"insufficient_claims\", claims=\"eyJhY2Nlc3NfdG9rZW4iOnsieG1zX2NjIjp7InZhbHVlcyI6WyJjcDEiXX19fQ==\"";
        return Results.Json(new
        {
            ok = false,
            errorCode = result.ErrorCode,
            message = result.Message,
            suggestion = result.Suggestion,
        }, statusCode: StatusCodes.Status403Forbidden);
    }

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

public sealed class ReplaceRegistrationsRequest
{
    public List<Guid> SubscriptionIds { get; set; } = new();
}

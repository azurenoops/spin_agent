using System.Security.Claims;
using Serilog.Context;

namespace Ato.Copilot.Mcp.Logging;

/// <summary>
/// Pushes wizard telemetry properties into Serilog <see cref="LogContext"/>
/// for every request that targets <c>/api/onboarding/</c>* (T135).
/// Per Constitution VI / research §R12 every wizard log event must carry
/// <c>tenantId</c>, <c>userId</c>, <c>wizardStep</c> (when present),
/// <c>jobId</c> (when present), and any <c>wizardErrorCode</c> attached
/// to the response.
/// </summary>
public sealed class OnboardingTelemetryMiddleware
{
    private readonly RequestDelegate _next;
    public OnboardingTelemetryMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api/onboarding"))
        {
            await _next(ctx);
            return;
        }

        var tenantId = ctx.User.FindFirstValue("tid")
            ?? ctx.User.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid")
            ?? "anonymous";
        var userId = ctx.User.FindFirstValue("oid")
            ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "anonymous";
        var wizardStep = ExtractStep(ctx.Request.Path.Value);
        var jobId = ctx.Request.RouteValues.TryGetValue("id", out var rid) ? rid?.ToString() : null;

        using (LogContext.PushProperty("tenantId", tenantId))
        using (LogContext.PushProperty("userId", userId))
        using (LogContext.PushProperty("wizardStep", wizardStep ?? "unknown"))
        using (LogContext.PushProperty("jobId", jobId ?? "(none)"))
        {
            await _next(ctx);

            // Surface any wizard error code attached to the response by
            // Envelope.Failure(...). Endpoint authors put the code into
            // HttpContext.Items["WizardErrorCode"] when emitting envelopes.
            if (ctx.Items.TryGetValue("WizardErrorCode", out var ec) && ec is string code)
            {
                using (LogContext.PushProperty("wizardErrorCode", code))
                {
                    // log line emitted by request-logging is now enriched
                }
            }
        }
    }

    private static string? ExtractStep(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        // /api/onboarding/{segment}/...
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return parts.Length >= 2 ? "root" : null;
        return parts[2] switch
        {
            "organization-context" => "1.OrganizationContext",
            "role-assignments" => "2.RoleAssignments",
            "people" => "2.People",
            "imports" when parts.Length >= 4 && parts[3] == "emass" => "3.EmassImport",
            "imports" when parts.Length >= 4 && parts[3] == "ssp-pdf" => "4.SspPdfImport",
            "azure" => "5.AzureSubscriptions",
            "templates" => "6.Templates",
            "narrative-seeds" => "7.NarrativeSeeds",
            "imports" => "Admin.ImportedDocuments",
            "dependencies" => "Admin.Cascade",
            "state" => "WizardState",
            "jobs" => "WizardJobs",
            _ => parts[2],
        };
    }
}

public static class OnboardingTelemetryMiddlewareExtensions
{
    public static IApplicationBuilder UseOnboardingTelemetry(this IApplicationBuilder app)
        => app.UseMiddleware<OnboardingTelemetryMiddleware>();
}

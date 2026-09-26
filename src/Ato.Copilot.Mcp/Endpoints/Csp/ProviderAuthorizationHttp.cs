using System.Diagnostics;
using System.Data.Common;
using System.Text.Json;
using System.Security.Claims;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

/// <summary>Standard provider API envelope and explicit domain failure mapping.</summary>
public static class ProviderAuthorizationHttp
{
    public static async Task<IResult> ExecuteProjectionAsync<T>(HttpContext http, Func<Task<T>> action, int status = 200)
    {
        try { return await ExecuteAsync(http, action, status); }
        catch (ResponsibilityReviewConflictException)
        {
            return Failure(http, 409, "RESPONSIBILITY_CONTEXT_STALE", "Reload and review the current canonical responsibility state.");
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or DbException or DbUpdateException)
        {
            return Failure(http, 503, "PROVIDER_PROJECTION_UNAVAILABLE",
                "The retained provider context could not be read or persisted. No completeness or successful mutation is implied.");
        }
    }

    public static string Actor(HttpContext http) =>
        http.User.FindFirstValue("oid") ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("Authenticated actor identity is required.");

    public static string Key(HttpContext http)
    {
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new ArgumentException("Idempotency-Key must contain1-100 characters.");
        return key;
    }

    public static async Task<IResult> ExecuteAsync<T>(HttpContext http, Func<Task<T>> action, int status = 200)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var data = await action();
            return Results.Json(new
            {
                status = "success", data,
                metadata = new { executionTimeMs = timer.ElapsedMilliseconds, timestamp = DateTimeOffset.UtcNow }
            }, statusCode: status);
        }
        catch (UnauthorizedAccessException ex) { return Failure(http, 403, "PROVIDER_ACCESS_DENIED", ex.Message); }
        catch (KeyNotFoundException ex) { return Failure(http, 404, "PROVIDER_RECORD_NOT_FOUND", ex.Message); }
        catch (ProviderPublicationConflictException ex) { return Failure(http, 409, ex.ErrorCode, ex.Message); }
        catch (DbUpdateConcurrencyException ex) { return Failure(http, 409, "AUTHORIZATION_CONTEXT_STALE", ex.Message); }
        catch (ArgumentException ex) { return Failure(http, 400, "INVALID_PROVIDER_REQUEST", ex.Message); }
    }

    public static IResult Failure(HttpContext http, int status, string code, string message)
    {
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ProviderAuthorizationApi")
            .LogWarning("Provider request rejected with {ErrorCode}, status {Status}", code, status);
        return Results.Json(new
        {
            status = "error",
            error = new { errorCode = code, message, suggestion = status == 403
                ? "Use an authorized ordinary workspace." : "Reload the current record and review the request before retrying." },
            metadata = new { timestamp = DateTimeOffset.UtcNow }
        }, statusCode: status);
    }
}

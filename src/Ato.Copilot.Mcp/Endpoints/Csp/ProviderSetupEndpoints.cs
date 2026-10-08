using System.Security.Claims;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

public static class ProviderSetupEndpoints
{
    public static IServiceCollection AddProviderSetup(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProviderHandlingOptions>(configuration.GetSection("Deployment:DataHandling"));
        services.AddScoped<ProviderSetupService>();
        return services;
    }

    public static IEndpointRouteBuilder MapProviderSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var setup = app.MapGroup("/api/csp/onboarding").RequireAuthorization()
            .WithMetadata(new WorkspaceAuthorizedEndpoint(), new ProviderOnboardingPreparation());
        setup.MapGet("/setup", (HttpContext http, ProviderSetupService service, CancellationToken ct) =>
            Execute(http, service, () => service.StateAsync(Actor(http), ct)));
        setup.MapGet("/handling-policy", (HttpContext http, ProviderSetupService service) =>
            Execute(http, service, () => Task.FromResult(service.HandlingPolicy())));
        setup.MapPut("/setup/draft", (SaveProviderSetupDraft body, HttpContext http, ProviderSetupService service, CancellationToken ct) =>
            Execute(http, service, async () => await service.SaveAsync(body, Key(http), Actor(http), ct)));
        setup.MapPost("/setup/commits", (CommitProviderSetup body, HttpContext http, ProviderSetupService service, CancellationToken ct) =>
            Execute(http, service, async () => await service.CommitAsync(body, Key(http), Actor(http), ct)));
        setup.MapPost("/setup/completion", (CompleteProviderSetup body, HttpContext http, ProviderSetupService service, CancellationToken ct) =>
            Execute(http, service, async () => await service.CompleteAsync(body, Key(http), Actor(http), ct)));
        setup.MapPost("/setup/commands/reconcile", (ReconcileProviderSetup body, HttpContext http, ProviderSetupService service, CancellationToken ct) =>
            Execute(http, service, async () => await service.ReconcileAsync(body, Actor(http), ct)));
        setup.MapGet("/setup/actions", (HttpContext http, ProviderSetupService service, int? page, int? pageSize, CancellationToken ct) =>
            Execute(http, service, () => service.ActionsAsync(page ?? 1, pageSize ?? 25, ct)));
        var imports = app.MapGroup("/api/csp/package-imports").RequireAuthorization()
            .WithMetadata(new WorkspaceAuthorizedEndpoint(), new ProviderOnboardingPreparation());
        imports.MapPost("/upload-intents", (PrepareProviderUpload body, HttpContext http, ProviderSetupService service, CancellationToken ct) =>
            Execute(http, service, () => service.PrepareUploadAsync(body, Key(http), Actor(http), ct), 201));
        imports.MapGet("/upload-intents", (HttpContext http, ProviderSetupService service, string? entryPoint,
            int? page, int? pageSize, Guid? offeringHintId, CancellationToken ct) =>
            Execute(http, service, () => service.ListUploadIntentsAsync(entryPoint ?? "ActivePortal", page ?? 1, pageSize ?? 25, offeringHintId, ct)));
        imports.MapGet("/upload-intents/{id:guid}", (Guid id, HttpContext http, ProviderSetupService service, CancellationToken ct) =>
            Execute(http, service, () => service.UploadIntentAsync(id, ct)));
        imports.MapPost("/receipt-reconciliation", (ReconcileProviderReceipt body, HttpContext http, ProviderSetupService service, CancellationToken ct) =>
            Execute(http, service, () => service.ReconcileReceiptAsync(body, ct)));
        return app;
    }

    private static ProviderSetupActor Actor(HttpContext http) => new(
        http.User.FindFirstValue("tid") ?? "",
        http.User.FindFirstValue("oid") ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Authenticated actor identity is required."),
        http.User.FindFirstValue("name") ?? http.User.Identity?.Name ?? "Current provider administrator");
    private static string Key(HttpContext http) => http.Request.Headers["Idempotency-Key"].ToString();
    internal static string ErrorCode(Exception error, string fallback)
    {
        var prefix = error.Message.Split(':', 2)[0];
        return prefix is "SETUP_INTENT_CONFLICT" or "SETUP_REVISION_CONFLICT" or "UPLOAD_INTENT_MISMATCH"
            or "HANDLING_POLICY_CHANGED" or "HANDLING_POLICY_UNKNOWN" or "HANDLING_DECLARATION_REQUIRED"
            or "HANDLING_NOT_PERMITTED" ? prefix : fallback;
    }

    private static async Task<IResult> Execute(HttpContext http, ProviderSetupService service, Func<Task<object>> action, int status = 200)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        try
        {
            await service.AuthorizeAsync(Actor(http), http.RequestAborted);
            if (http.RequestServices.GetRequiredService<IOptions<DeploymentOptions>>().Value.Mode == DeploymentMode.SingleTenant)
                return ProviderAuthorizationHttp.Failure(http, 404, "SINGLE_TENANT_MODE", "Provider setup is not applicable.");
            if (http.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
            service.Authorize();
            return Results.Json(new { status = "success", data = await action(), metadata = new { timestamp = DateTimeOffset.UtcNow } }, statusCode: status);
        }
        catch (UnauthorizedAccessException error) { return ProviderAuthorizationHttp.Failure(http, 403, "PROVIDER_ACCESS_DENIED", error.Message); }
        catch (KeyNotFoundException error) { return ProviderAuthorizationHttp.Failure(http, 404, "PROVIDER_RECORD_NOT_FOUND", error.Message); }
        catch (CspAlreadyOnboardedException) { return ProviderAuthorizationHttp.Failure(http, 409, "CSP_ALREADY_ONBOARDED", "Provider identity is already finalized."); }
        catch (DbUpdateConcurrencyException error) { return ProviderAuthorizationHttp.Failure(http, 409, ErrorCode(error, "SETUP_REVISION_CONFLICT"), error.Message); }
        catch (ArgumentException error) { return ProviderAuthorizationHttp.Failure(http, 422, ErrorCode(error, "VALIDATION_FAILED"), error.Message); }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or System.Data.Common.DbException or DbUpdateException)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ProviderSetup")
                .LogError(error, "ProviderSetup.PersistenceUnavailable");
            return ProviderAuthorizationHttp.Failure(http, 503, "SETUP_UNAVAILABLE", "Provider setup state is unavailable. Retain the same request key and reconcile.");
        }
    }
}

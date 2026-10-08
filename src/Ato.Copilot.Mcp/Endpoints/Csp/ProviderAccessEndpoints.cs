using System.Security.Claims;
using System.Text.Json;
using Ato.Copilot.Core.Authorization;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Authorization;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

public static class ProviderAccessEndpoints
{
    public static IEndpointRouteBuilder MapProviderAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var invitations = app.MapGroup("/api/csp/invitations").RequireAuthorization()
            .WithTags("Provider access").WithMetadata(new ProviderOnboardingPreparation());
        invitations.MapGet("", ListInvitationsAsync);
        invitations.MapPost("", CreateInvitationAsync);
        invitations.MapGet("/{id:guid}", GetInvitationAsync);
        invitations.MapPost("/{id:guid}/accept", AcceptInvitationAsync);
        invitations.MapPost("/{id:guid}/revoke", RevokeInvitationAsync);

        var accessRequests = app.MapGroup("/api/csp/access-requests").RequireAuthorization()
            .WithTags("Provider access").WithMetadata(new ProviderOnboardingPreparation());
        accessRequests.MapGet("/current", GetCurrentAccessRequestAsync);
        accessRequests.MapPost("", CreateAccessRequestAsync);
        accessRequests.MapPost("/{id:guid}/decision", DecideAccessRequestAsync);

        var memberships = app.MapGroup("/api/csp/memberships").RequireAuthorization()
            .WithTags("Provider access").WithMetadata(new ProviderOnboardingPreparation());
        memberships.MapGet("", ListMembershipsAsync);
        memberships.MapPost("", GrantMembershipAsync);
        memberships.MapDelete("/{id:guid}", RevokeMembershipAsync);
        return app;
    }

    private static async Task<IResult> CreateInvitationAsync(
        CreateProviderInvitationRequest body, HttpContext http,
        ProviderAccessService service, CancellationToken ct) =>
        await ExecuteAsync(async () =>
        {
            var created = await service.CreateInvitationAsync(Subject(http), body, ct);
            return Results.Created($"/api/csp/invitations/{created.Invitation.Id}",
                Envelope(new
                {
                    invitation = await InvitationProjection(created.Invitation, http.RequestServices, ct),
                    token = created.Token,
                    deliveryState = "NotDelivered"
                }));
        });

    private static async Task<IResult> ListInvitationsAsync(
        Guid providerId, HttpContext http, ProviderAccessService service, CancellationToken ct) =>
        await ExecuteAsync(async () =>
        {
            var rows = await service.ListInvitationsAsync(Subject(http), providerId, ct);
            var projected = new List<object>();
            foreach (var row in rows)
                projected.Add(await InvitationProjection(row, http.RequestServices, ct));
            return Results.Ok(Envelope(new { items = projected, total = projected.Count }));
        });

    private static async Task<IResult> GetInvitationAsync(
        Guid id, string? token, HttpContext http,
        ProviderAccessService service, CancellationToken ct) =>
        await ExecuteAsync(async () =>
        {
            var invitation = await service.GetInvitationAsync(Subject(http), id, token, ct);
            return Results.Ok(Envelope(await InvitationProjection(invitation, http.RequestServices, ct)));
        });

    private static async Task<IResult> AcceptInvitationAsync(
        Guid id, AcceptProviderInvitationRequest? body, string? token, HttpContext http,
        ProviderAccessService service, CancellationToken ct) =>
        await ExecuteAsync(async () =>
        {
            var suppliedToken = body?.Token ?? token ?? http.Request.Headers["Invitation-Token"].ToString();
            var accepted = await service.AcceptInvitationAsync(id, suppliedToken, Subject(http), ct);
            return Results.Ok(Envelope(new { accepted.MembershipId, accepted.Destination }));
        });

    private static async Task<IResult> RevokeInvitationAsync(
        Guid id, HttpContext http, ProviderAccessService service, CancellationToken ct) =>
        await ExecuteAsync(async () =>
        {
            await service.RevokeInvitationAsync(Subject(http), id, ct);
            return Results.NoContent();
        });

    private static async Task<IResult> GetCurrentAccessRequestAsync(
        HttpContext http, ProviderAccessService service, CancellationToken ct) =>
        await ExecuteAsync(async () =>
            Results.Ok(Envelope(await service.GetCurrentAccessRequestAsync(Subject(http), ct))));

    private static async Task<IResult> CreateAccessRequestAsync(
        CreateProviderAccessRequest body, HttpContext http,
        ProviderAccessService service, CancellationToken ct) =>
        await ExecuteAsync(async () =>
        {
            var created = await service.CreateAccessRequestAsync(Subject(http), body, ct);
            return Results.Created($"/api/csp/access-requests/{created.Id}", Envelope(created));
        });

    private static async Task<IResult> DecideAccessRequestAsync(
        Guid id, DecideProviderAccessRequest body, HttpContext http,
        ProviderAccessService service, CancellationToken ct) =>
        await ExecuteAsync(async () =>
            Results.Ok(Envelope(await service.DecideAccessRequestAsync(Subject(http), id, body, ct))));

    private static async Task<IResult> ListMembershipsAsync(
        Guid providerId, HttpContext http, ProviderAccessService service, CancellationToken ct) =>
        await ExecuteAsync(async () =>
        {
            var rows = await service.ListMembershipsAsync(Subject(http), providerId, ct);
            return Results.Ok(Envelope(new { items = rows, total = rows.Count }));
        });

    private static async Task<IResult> GrantMembershipAsync(
        GrantProviderMembershipRequest body, HttpContext http,
        ProviderAccessService service, CancellationToken ct) =>
        await ExecuteAsync(async () =>
        {
            var grant = await service.GrantMembershipAsync(Subject(http), body, ct);
            return grant.Created
                ? Results.Created($"/api/csp/memberships/{grant.Membership.Id}", Envelope(grant.Membership))
                : Results.Ok(Envelope(grant.Membership));
        });

    private static async Task<IResult> RevokeMembershipAsync(
        Guid id, string? reason, HttpContext http,
        ProviderAccessService service, CancellationToken ct) =>
        await ExecuteAsync(async () =>
        {
            await service.RevokeMembershipAsync(Subject(http), id, reason, ct);
            return Results.NoContent();
        });

    private static async Task<object> InvitationProjection(
        ProviderInvitation invitation, IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var provider = await db.CspProfiles.IgnoreQueryFilters().AsNoTracking().SingleAsync(
            x => x.Id == invitation.ProviderId, ct);
        var offering = invitation.OfferingId.HasValue
            ? await db.Set<ProviderOffering>().AsNoTracking().Where(x =>
                x.ProviderId == invitation.ProviderId && x.Id == invitation.OfferingId)
                .Select(x => new { offeringId = x.Id, x.Name }).SingleOrDefaultAsync(ct)
            : null;
        return new
        {
            invitationId = invitation.Id,
            identity = new
            {
                email = invitation.TargetEmail,
                directoryTenantId = invitation.TargetDirectoryTenantId,
                objectId = invitation.TargetObjectId
            },
            provider = new { providerId = provider.Id, provider.DisplayName },
            offering,
            requestedRoles = JsonSerializer.Deserialize<ProviderRole[]>(
                invitation.RequestedRolesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [],
            status = invitation.Status,
            invitation.ExpiresAt
        };
    }

    private static ProviderAccessSubject Subject(HttpContext http)
    {
        if (http.User.Identity?.IsAuthenticated != true
            || !Guid.TryParse(http.User.FindFirst("tid")?.Value, out var directoryTenantId)
            || !Guid.TryParse(http.User.FindFirst("oid")?.Value
                ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var objectId))
            throw new UnauthorizedAccessException("Authenticated directory identity is required.");
        return new(directoryTenantId, objectId,
            http.User.FindFirstValue("name") ?? http.User.Identity.Name ?? "Provider user",
            http.User.FindFirstValue("preferred_username") ?? http.User.FindFirstValue(ClaimTypes.Email),
            http.User.IsInRole("CSP.Admin"));
    }

    private static object Envelope(object? data) => new
    {
        status = "success",
        data,
        metadata = new { timestamp = DateTimeOffset.UtcNow }
    };

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> operation)
    {
        try { return await operation(); }
        catch (UnauthorizedAccessException error) { return Error(403, "PROVIDER_ACCESS_DENIED", error.Message); }
        catch (KeyNotFoundException error) { return Error(404, "PROVIDER_RECORD_NOT_FOUND", error.Message); }
        catch (InvalidOperationException error) { return Error(409, "PROVIDER_ACCESS_CONFLICT", error.Message); }
        catch (DbUpdateConcurrencyException error) { return Error(409, "PROVIDER_ACCESS_CONFLICT", error.Message); }
        catch (ArgumentException error) { return Error(422, "VALIDATION_FAILED", error.Message); }
    }

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new
        {
            status = "error",
            error = new { errorCode = code, message },
            metadata = new { timestamp = DateTimeOffset.UtcNow }
        }, statusCode: status);
}

public sealed record AcceptProviderInvitationRequest(string Token);

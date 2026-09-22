using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Services.Tenancy;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>Supported, audited membership provisioning and repair; no database editing required.</summary>
public static class OrganizationMembershipEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationMembershipEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants/{tenantId:guid}").WithTags("Organization membership")
            .WithMetadata(new Ato.Copilot.Mcp.Authorization.WorkspaceAuthorizedEndpoint());
        group.MapPost("/administrator-assignments", async (Guid tenantId, EnrollOrganizationAdministratorRequest request,
            HttpContext http, IOrganizationMembershipService service, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var role = await service.EnrollAdministratorAsync(http, tenantId, request.PersonId, ct);
                return Results.Created($"/api/tenants/{tenantId}/administrator-assignments/{role.Id}", new { status = "success", data = role });
            }))
            .WithName("EnrollInitialOrganizationAdministrator")
            .WithSummary("Explicitly enroll an organization's first Administrator using CSP authority");
        group.MapGet("/administrator-assignments/{assignmentId:guid}", async (Guid tenantId, Guid assignmentId,
            HttpContext http, IOrganizationMembershipService service, CancellationToken ct) =>
            await ExecuteAsync(async () => Success(await service.GetAdministratorAsync(http, tenantId, assignmentId, ct))))
            .WithName("GetOrganizationAdministratorAssignment")
            .WithSummary("Read an explicitly enrolled organization Administrator assignment");
        group.MapGet("/memberships", async (Guid tenantId, int? page, int? pageSize,
            HttpContext http, IOrganizationMembershipService service, CancellationToken ct) =>
            await ExecuteAsync(async () => Success(await service.ListAsync(http, tenantId, page ?? 1, pageSize ?? 50, ct))))
            .WithName("ListOrganizationMemberships").WithSummary("List explicit organization access grants");
        group.MapGet("/memberships/{membershipId:guid}", async (Guid tenantId, Guid membershipId,
            HttpContext http, IOrganizationMembershipService service, CancellationToken ct) =>
            await ExecuteAsync(async () => Success(await service.GetAsync(http, tenantId, membershipId, ct))))
            .WithName("GetOrganizationMembership").WithSummary("Read an organization access grant and its revocation state");
        group.MapPost("/memberships", async (Guid tenantId, GrantOrganizationMembershipRequest body,
            HttpContext http, IOrganizationMembershipService service, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                var result = await service.GrantAsync(http, tenantId, body, ct);
                var envelope = new { status = "success", data = result.Membership };
                return result.Created
                    ? Results.Created($"/api/tenants/{tenantId}/memberships/{result.Membership.Id}", envelope)
                    : Results.Ok(envelope);
            }))
            .WithName("GrantOrganizationMembership").WithSummary("Grant access to an existing organization-local Person");
        group.MapDelete("/memberships/{membershipId:guid}", async (Guid tenantId, Guid membershipId,
            HttpContext http, IOrganizationMembershipService service, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await service.RevokeAsync(http, tenantId, membershipId, ct);
                return Results.NoContent();
            }))
            .WithName("RevokeOrganizationMembership").WithSummary("Revoke organization access while retaining role and audit history");
        group.MapGet("/membership-persons", async (Guid tenantId, string? query, HttpContext http,
            IOrganizationMembershipService memberships, IPersonService persons, ITenantContextAccessor accessor, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await memberships.AuthorizeAdministrationAsync(http.User, tenantId, ct);
                using var scope = accessor.Push(new TenantContext(tenantId));
                var items = await persons.SearchLocalAsync(tenantId, query ?? string.Empty, ct);
                return Success(items.Select(p => new MembershipPersonResponse(p.Id, p.DisplayName, p.Email)));
            }))
            .WithName("ListMembershipPersons").WithSummary("Find organization-local Persons to associate with directory identities");
        group.MapPost("/membership-persons", async (Guid tenantId, CreateMembershipPersonRequest body, HttpContext http,
            IOrganizationMembershipService memberships, IPersonService persons, ITenantContextAccessor accessor, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await memberships.AuthorizeAdministrationAsync(http.User, tenantId, ct);
                if (string.IsNullOrWhiteSpace(body.DisplayName) || body.DisplayName.Length > 256
                    || string.IsNullOrWhiteSpace(body.Email) || body.Email.Length > 320)
                    throw new WorkspaceException(400, "INVALID_PERSON", "A display name (up to 256 characters) and contact email (up to 320 characters) are required.");
                using var scope = accessor.Push(new TenantContext(tenantId));
                try
                {
                    var person = await persons.CreateLocalAsync(tenantId, body.DisplayName, body.Email, null,
                        WorkspaceService.Identity(http.User).ObjectId, Guid.NewGuid(), ct);
                    return Results.Created($"/api/tenants/{tenantId}/membership-persons?query={Uri.EscapeDataString(person.Email)}",
                        new { status = "success", data = new MembershipPersonResponse(person.Id, person.DisplayName, person.Email) });
                }
                catch (InvalidOperationException)
                {
                    throw new WorkspaceException(409, "PERSON_CONFLICT", "A Person with this contact email already exists; select the existing Person.");
                }
            }))
            .WithName("CreateMembershipPerson").WithSummary("Create a local contact without granting sign-in access or roles");
        app.MapGet("/api/auth/workspaces", async (HttpContext http, int? page, int? pageSize,
            IWorkspaceService workspace, CancellationToken ct) =>
            await ExecuteAsync(async () => Success(await workspace.ListAsync(http.User, page ?? 1, pageSize ?? 50, ct))))
            .WithTags("Auth").WithName("ListAuthorizedWorkspaces").WithSummary("Discover ordinary authorized workspaces");
        return app;
    }

    private static IResult Success(object data) => Results.Ok(new { status = "success", data });

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> operation)
    {
        try { return await operation(); }
        catch (WorkspaceException ex)
        {
            return Results.Json(new { status = "error", error = new { errorCode = ex.Code, message = ex.Message } },
                statusCode: ex.StatusCode);
        }
    }
}

/// <summary>Existing local contact information, not an identity or authorization grant.</summary>
public sealed record MembershipPersonResponse(Guid Id, string DisplayName, string Email);

/// <summary>Local contact to be explicitly associated in a separate grant operation.</summary>
public sealed record CreateMembershipPersonRequest(string DisplayName, string Email);

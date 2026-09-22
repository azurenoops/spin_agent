using System.Diagnostics;
using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Observability;
using Ato.Copilot.Core.Services.Roles;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>POST /api/roles/system/{systemId} body.</summary>
/// <param name="Role">Role name (one of the 7).</param>
/// <param name="PersonId">Person ID to assign.</param>
public sealed record AssignSystemRoleBody(string Role, Guid PersonId);

/// <summary>POST /api/roles/organization body.</summary>
/// <param name="Role">Role name (one of the 7).</param>
/// <param name="PersonId">Person ID to assign.</param>
/// <param name="IsPrimary">When <c>true</c> and the row succeeds, marks the
/// assignment primary for the role; defaults to <c>true</c>.</param>
/// <param name="Bootstrap">Wizard bootstrap flag. The server ONLY honors
/// this when the tenant has zero active OrganizationRoleAssignments —
/// otherwise it falls through to the FR-027 matrix.</param>
public sealed record AssignOrgRoleBody(
    string Role,
    Guid PersonId,
    bool IsPrimary = true,
    bool Bootstrap = false);

/// <summary>
/// HTTP surface for the <c>/api/roles</c> endpoint group (Feature 049 — Unified
/// RMF Role Assignments). Mirrors
/// <c>specs/049-unified-rmf-role-assignments/contracts/http-api.md</c>.
///
/// <para>
/// Every handler is exposed as <c>internal static</c> so the integration tests
/// can invoke it directly with synthetic dependencies — there is no
/// <c>WebApplicationFactory</c> overhead and authentication/tenant resolution
/// are decoupled from the business logic. The thin Minimal-API wrappers
/// registered in <see cref="MapSystemRolesEndpoints"/> pull <c>tenantId</c>
/// from <see cref="ITenantContext"/> and <c>actorPersonId</c> from the
/// caller's claims, then delegate to the handlers below.
/// </para>
///
/// <para>
/// Drives FR-008, FR-010, FR-011, FR-012, FR-026, FR-027; SC-001, SC-002,
/// SC-007, SC-009.
/// </para>
/// </summary>
public static class SystemRolesEndpoints
{
    // ─── Constants ──────────────────────────────────────────────────────────

    private static readonly string[] AllSevenRoleNames =
    {
        "AuthorizingOfficial",
        "Issm",
        "Isso",
        "Sca",
        "SystemOwner",
        "MissionOwner",
        "Administrator",
    };

    private const string AllRolesSuggestionText =
        "Valid roles: AuthorizingOfficial, Issm, Isso, Sca, SystemOwner, MissionOwner, Administrator.";


    // ─── Routing registration ──────────────────────────────────────────────

    /// <summary>
    /// Registers the <c>/api/roles</c> route group. Each Minimal-API wrapper
    /// extracts <c>tenantId</c> from the request-scoped <see cref="ITenantContext"/>
    /// and <c>actorPersonId</c> from the caller's claims before delegating to
    /// the <c>internal static</c> handlers below.
    /// </summary>
    public static IEndpointRouteBuilder MapSystemRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles").WithTags("Roles");

        group.MapGet("/system/{systemId}", async (
            string systemId,
            ITenantContext tenant,
            IUnifiedRoleReader reader,
            IDbContextFactory<AtoCopilotContext> dbFactory,
            CancellationToken ct) =>
        {
            var tid = RequireTenant(tenant);
            return await GetSystemRolesAsync(systemId, tid, reader, dbFactory, ct);
        }).WithName("GetSystemRoles");

        group.MapPost("/system/{systemId}", async (
            string systemId,
            [FromBody] AssignSystemRoleBody body,
            HttpContext http,
            ITenantContext tenant,
            IDbContextFactory<AtoCopilotContext> dbFactory,
            ICallerEffectiveRoleResolver resolver,
            IRoleAuthorizationService authz,
            ISoDConflictDetector sod,
            RoleMetrics metrics,
            CancellationToken ct) =>
        {
            var tid = RequireTenant(tenant);
            var actor = ResolveActorPersonId(http);
            return await AssignSystemRoleAsync(
                systemId, body, tid, actor, dbFactory, resolver, authz, sod, metrics, ct);
        }).WithName("AssignSystemRole").RequireWorkspaceOperation(SystemWorkspaceOperation.AssignSystemRole);

        group.MapDelete("/system/{systemId}/{role}/{personId:guid}", async (
            string systemId,
            string role,
            Guid personId,
            HttpContext http,
            ITenantContext tenant,
            IDbContextFactory<AtoCopilotContext> dbFactory,
            ICallerEffectiveRoleResolver resolver,
            IRoleAuthorizationService authz,
            RoleMetrics metrics,
            CancellationToken ct) =>
        {
            var tid = RequireTenant(tenant);
            var actor = ResolveActorPersonId(http);
            return await RemoveSystemRoleAsync(
                systemId, role, personId, tid, actor, dbFactory, resolver, authz, metrics, ct);
        }).WithName("RemoveSystemRole").RequireWorkspaceOperation(SystemWorkspaceOperation.AssignSystemRole);

        group.MapPost("/organization", async (
            [FromBody] AssignOrgRoleBody body,
            HttpContext http,
            ITenantContext tenant,
            IDbContextFactory<AtoCopilotContext> dbFactory,
            ICallerEffectiveRoleResolver resolver,
            IRoleAuthorizationService authz,
            ISoDConflictDetector sod,
            IOrganizationRoleAssignmentService orgService,
            CancellationToken ct) =>
        {
            var tid = RequireTenant(tenant);
            var actor = ResolveActorPersonId(http);
            return await AssignOrgRoleAsync(
                body, tid, actor, dbFactory, resolver, authz, sod, orgService, ct);
        }).WithName("AssignOrgRole");

        group.MapDelete("/organization/{role}/{personId:guid}", async (
            string role,
            Guid personId,
            HttpContext http,
            ITenantContext tenant,
            IDbContextFactory<AtoCopilotContext> dbFactory,
            ICallerEffectiveRoleResolver resolver,
            IRoleAuthorizationService authz,
            IOrganizationRoleAssignmentService orgService,
            CancellationToken ct) =>
        {
            var tid = RequireTenant(tenant);
            var actor = ResolveActorPersonId(http);
            return await RemoveOrgRoleAsync(
                role, personId, tid, actor, dbFactory, resolver, authz, orgService, ct);
        }).WithName("RemoveOrgRole");

        group.MapGet("/effective", async (
            HttpContext http,
            ITenantContext tenant,
            ICallerEffectiveRoleResolver resolver,
            CancellationToken ct) =>
        {
            var tid = RequireTenant(tenant);
            var actor = ResolveActorPersonId(http);
            return await GetCallerEffectiveRoleAsync(tid, actor, resolver, ct);
        }).WithName("GetCallerEffectiveRole");

        return app;
    }

    // ─── Handler: GET /api/roles/system/{systemId} ─────────────────────────

    /// <summary>
    /// Read the 7-role snapshot for a system. Composes the 7th Administrator row
    /// from <c>OrganizationRoleAssignments</c> on top of the 6 RmfRole rows
    /// surfaced by <see cref="IUnifiedRoleReader"/> (whose enum is frozen at 6
    /// per FR-020).
    /// </summary>
    public static async Task<IResult> GetSystemRolesAsync(
        string systemId,
        Guid tenantId,
        IUnifiedRoleReader reader,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var snapshot = await reader.GetSystemRolesAsync(tenantId, systemId, ct);
        var rmfRows = snapshot.Roles.Select(ProjectRow).ToList();

        // Compose the 7th (Administrator) row from the OrganizationRoleAssignments
        // table — Admin is Org-scope-only and has no per-system inheritance.
        Guid? adminPersonId = null;
        string? adminDisplayName = null;
        Guid? adminAssignmentId = null;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var adminRow = await db.OrganizationRoleAssignments
                .AsNoTracking()
                .Where(r => r.TenantId == tenantId
                         && r.Role == OrganizationRole.Administrator
                         && r.RemovedAt == null)
                .OrderByDescending(r => r.IsPrimary)
                .ThenByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (adminRow is not null)
            {
                adminAssignmentId = adminRow.Id;
                adminPersonId = adminRow.PersonId;
                var person = await db.Persons
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == adminRow.PersonId && p.TenantId == tenantId, ct);
                adminDisplayName = person?.DisplayName;
            }
        }

        var adminProjection = new
        {
            role = "Administrator",
            person = adminPersonId is null
                ? null
                : new { id = adminPersonId.Value.ToString(), displayName = adminDisplayName ?? string.Empty },
            source = adminPersonId is null ? "not-assigned" : "override",
            orgRoleId = adminAssignmentId?.ToString(),
        };
        rmfRows.Add(adminProjection);

        var data = new
        {
            systemId,
            roles = rmfRows,
        };
        return Success(sw, data);
    }

    // ─── Handler: POST /api/roles/system/{systemId} ────────────────────────

    /// <summary>
    /// Write a per-system role override (FR-010). Always produces a
    /// <see cref="SystemRoleAssignment"/> with <c>IsInherited=false</c>.
    /// </summary>
    public static async Task<IResult> AssignSystemRoleAsync(
        string systemId,
        AssignSystemRoleBody body,
        Guid tenantId,
        Guid actorPersonId,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ICallerEffectiveRoleResolver resolver,
        IRoleAuthorizationService authz,
        ISoDConflictDetector sod,
        RoleMetrics metrics,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (body is null || string.IsNullOrWhiteSpace(body.Role))
        {
            return InvalidRole(sw, body?.Role ?? "(null)");
        }

        if (!TryParseRole(body.Role, out var orgRole, out var rmfRole))
        {
            return InvalidRole(sw, body.Role);
        }

        // Administrator is Org-scope only — reject on the per-system path.
        if (rmfRole is null)
        {
            return InvalidRole(sw, body.Role,
                "Role 'Administrator' is Org-scope only; use POST /api/roles/organization. " + AllRolesSuggestionText);
        }

        // FR-027: RBAC matrix authorization.
        var caller = await resolver.ResolveAsync(tenantId, actorPersonId, ct);
        var decision = authz.Authorize(caller, rmfRole.Value, isBootstrapSession: false);
        if (!decision.Allowed)
        {
            return RbacDenied(sw, caller, rmfRole.Value);
        }

        // SoD detection (read-only, pre-write).
        var sodWarnings = await sod.DetectAsync(tenantId, body.PersonId, rmfRole.Value, ct);

        // Persist the override row.
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.SystemRoleAssignments
            .FirstOrDefaultAsync(s => s.TenantId == tenantId
                                   && s.RegisteredSystemId == systemId
                                   && s.Role == orgRole
                                   && s.PersonId == body.PersonId
                                   && s.RemovedAt == null, ct);
        SystemRoleAssignment row;
        if (existing is not null)
        {
            // Already exists — re-affirm as override (idempotent).
            existing.IsInherited = false;
            existing.SourceOrganizationRoleAssignmentId = null;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = actorPersonId;
            row = existing;
        }
        else
        {
            row = new SystemRoleAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RegisteredSystemId = systemId,
                Role = orgRole,
                PersonId = body.PersonId,
                IsInherited = false,
                SourceOrganizationRoleAssignmentId = null,
                CreatedBy = actorPersonId,
                UpdatedBy = actorPersonId,
            };
            db.SystemRoleAssignments.Add(row);
        }
        await db.SaveChangesAsync(ct);

        // Resolve display name for the response data payload.
        var personName = await db.Persons
            .AsNoTracking()
            .Where(p => p.Id == body.PersonId && p.TenantId == tenantId)
            .Select(p => p.DisplayName)
            .FirstOrDefaultAsync(ct);

        return SuccessWithOptionalWarnings(
            sw,
            data: new
            {
                role = body.Role,
                person = new { id = body.PersonId.ToString(), displayName = personName ?? string.Empty },
                source = "override",
            },
            warnings: ProjectWarnings(sodWarnings));
    }

    // ─── Handler: DELETE /api/roles/system/{systemId}/{role}/{personId} ────

    /// <summary>
    /// Remove a per-system role row. Inherited rows are rejected with 409
    /// <c>ROLE_INHERITED_NOT_REMOVABLE</c> (FR-011) — the caller must instead
    /// remove the Org-level row that fans out to this system.
    /// </summary>
    public static async Task<IResult> RemoveSystemRoleAsync(
        string systemId,
        string role,
        Guid personId,
        Guid tenantId,
        Guid actorPersonId,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ICallerEffectiveRoleResolver resolver,
        IRoleAuthorizationService authz,
        RoleMetrics metrics,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (!TryParseRole(role, out var orgRole, out var rmfRole))
        {
            return InvalidRole(sw, role);
        }
        if (rmfRole is null)
        {
            return InvalidRole(sw, role,
                "Role 'Administrator' is Org-scope only; use DELETE /api/roles/organization. " + AllRolesSuggestionText);
        }

        // FR-027: RBAC matrix authorization (remove = assign for matrix purposes).
        var caller = await resolver.ResolveAsync(tenantId, actorPersonId, ct);
        var decision = authz.Authorize(caller, rmfRole.Value, isBootstrapSession: false);
        if (!decision.Allowed)
        {
            return RbacDenied(sw, caller, rmfRole.Value);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.SystemRoleAssignments
            .FirstOrDefaultAsync(s => s.TenantId == tenantId
                                   && s.RegisteredSystemId == systemId
                                   && s.Role == orgRole
                                   && s.PersonId == personId
                                   && s.RemovedAt == null, ct);
        if (row is null)
        {
            return Error(sw, StatusCodes.Status404NotFound, "NOT_FOUND",
                $"No active SystemRoleAssignment for {role}/{personId} on system {systemId}.");
        }

        if (row.IsInherited)
        {
            // FR-011: inherited rows cannot be removed via the per-system path.
            return Error(sw, StatusCodes.Status409Conflict, "ROLE_INHERITED_NOT_REMOVABLE",
                "Inherited per-system role rows cannot be removed directly; remove the originating Organization-level assignment.",
                extras: new Dictionary<string, object?>
                {
                    ["orgRoleId"] = row.SourceOrganizationRoleAssignmentId?.ToString(),
                });
        }

        row.RemovedAt = DateTimeOffset.UtcNow;
        row.UpdatedAt = row.RemovedAt.Value;
        row.UpdatedBy = actorPersonId;
        await db.SaveChangesAsync(ct);

        return Success(sw, new
        {
            role,
            person = new { id = personId.ToString() },
            source = "not-assigned",
        });
    }

    // ─── Handler: POST /api/roles/organization ─────────────────────────────

    /// <summary>
    /// Write an Organization-scope role. The
    /// <see cref="IOrganizationRoleAssignmentService"/> handles audit + fan-out
    /// enqueue (FR-028) on success. SoD warnings are surfaced in the envelope's
    /// <c>warnings</c> array (FR-026, structured; the array is OMITTED when empty).
    /// </summary>
    public static async Task<IResult> AssignOrgRoleAsync(
        AssignOrgRoleBody body,
        Guid tenantId,
        Guid actorPersonId,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ICallerEffectiveRoleResolver resolver,
        IRoleAuthorizationService authz,
        ISoDConflictDetector sod,
        IOrganizationRoleAssignmentService orgService,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (body is null || string.IsNullOrWhiteSpace(body.Role))
        {
            return InvalidRole(sw, body?.Role ?? "(null)");
        }
        if (!TryParseRole(body.Role, out var orgRole, out var rmfRole))
        {
            return InvalidRole(sw, body.Role);
        }

        // Bootstrap security guard: ignore the flag when ≥1 active Org row exists.
        var effectiveBootstrap = body.Bootstrap;
        if (effectiveBootstrap)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var anyExisting = await db.OrganizationRoleAssignments
                .AsNoTracking()
                .AnyAsync(r => r.TenantId == tenantId && r.RemovedAt == null, ct);
            if (anyExisting)
            {
                effectiveBootstrap = false;
            }
        }

        // FR-027: RBAC matrix authorization.
        var caller = await resolver.ResolveAsync(tenantId, actorPersonId, ct);
        AuthorizationResult decision;
        if (orgRole == OrganizationRole.Administrator)
        {
            // Administrator is Org-scope-only and not in the 6×6 matrix.
            // Allow when bootstrap, or caller is already an Administrator, or
            // caller is ISSM (per FR-027 spec table extension).
            if (effectiveBootstrap
                || caller.IsTenantAdministrator
                || caller.RmfRole == RmfRole.Issm)
            {
                decision = new AuthorizationResult(true, null);
            }
            else
            {
                decision = new AuthorizationResult(false,
                    "Only Administrator or ISSM may assign the Administrator role.");
            }
        }
        else
        {
            decision = authz.Authorize(caller, rmfRole!.Value, effectiveBootstrap);
        }

        if (!decision.Allowed)
        {
            // RmfRole is non-null here except in the Administrator branch where we
            // surface "Administrator" as the target name regardless.
            return RbacDenied(sw, caller, rmfRole, orgRoleName: body.Role);
        }

        // Structured SoD detection BEFORE persist (the OrgService also records
        // string-flattened warnings via its own SoD call; we surface the
        // structured form to the envelope here for the dashboard).
        IReadOnlyList<SoDWarning> sodWarnings = Array.Empty<SoDWarning>();
        if (rmfRole is not null)
        {
            sodWarnings = await sod.DetectAsync(tenantId, body.PersonId, rmfRole.Value, ct);
        }

        // Persist via the service (audit + fanout enqueue).
        try
        {
            var result = await orgService.AddAsync(
                tenantId, orgRole, body.PersonId, actorPersonId,
                correlationId: Guid.NewGuid(), ct);

            return SuccessWithOptionalWarnings(
                sw,
                data: new
                {
                    role = body.Role,
                    person = new
                    {
                        id = body.PersonId.ToString(),
                        displayName = result.Assignment.Person?.DisplayName ?? string.Empty,
                    },
                    source = "override",
                    orgRoleId = result.Assignment.Id.ToString(),
                },
                warnings: ProjectWarnings(sodWarnings));
        }
        catch (InvalidOperationException ex)
        {
            return Error(sw, StatusCodes.Status400BadRequest, "INVALID_REQUEST", ex.Message);
        }
    }

    // ─── Handler: DELETE /api/roles/organization/{role}/{personId} ─────────

    /// <summary>
    /// Soft-remove an Organization-scope role. Cascades to inherited per-system
    /// rows via <see cref="IOrganizationRoleAssignmentService.RemoveAsync"/>
    /// (FR-007).
    /// </summary>
    public static async Task<IResult> RemoveOrgRoleAsync(
        string role,
        Guid personId,
        Guid tenantId,
        Guid actorPersonId,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ICallerEffectiveRoleResolver resolver,
        IRoleAuthorizationService authz,
        IOrganizationRoleAssignmentService orgService,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (!TryParseRole(role, out var orgRole, out var rmfRole))
        {
            return InvalidRole(sw, role);
        }

        // RBAC: identical assign matrix doubles as the remove matrix (FR-027).
        var caller = await resolver.ResolveAsync(tenantId, actorPersonId, ct);
        AuthorizationResult decision;
        if (orgRole == OrganizationRole.Administrator)
        {
            // Only Administrator or ISSM may remove an Administrator row.
            decision = (caller.IsTenantAdministrator || caller.RmfRole == RmfRole.Issm)
                ? new AuthorizationResult(true, null)
                : new AuthorizationResult(false, "Only Administrator or ISSM may remove the Administrator role.");
        }
        else
        {
            decision = authz.Authorize(caller, rmfRole!.Value, isBootstrapSession: false);
        }
        if (!decision.Allowed)
        {
            return RbacDenied(sw, caller, rmfRole, orgRoleName: role);
        }

        // Find the assignment.
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var assignment = await db.OrganizationRoleAssignments
            .FirstOrDefaultAsync(r => r.TenantId == tenantId
                                   && r.Role == orgRole
                                   && r.PersonId == personId
                                   && r.RemovedAt == null, ct);
        if (assignment is null)
        {
            return Error(sw, StatusCodes.Status404NotFound, "NOT_FOUND",
                $"No active OrganizationRoleAssignment for {role}/{personId}.");
        }

        try
        {
            await orgService.RemoveAsync(
                tenantId, assignment.Id, actorPersonId,
                correlationId: Guid.NewGuid(), ct);
            return Success(sw, new
            {
                role,
                person = new { id = personId.ToString() },
                source = "not-assigned",
            });
        }
        catch (InvalidOperationException ex)
        {
            // Bubble structured error codes (e.g. WIZARD_LAST_ADMIN_PROTECTED) verbatim.
            return Error(sw, StatusCodes.Status409Conflict, "ROLE_REMOVE_REJECTED", ex.Message);
        }
    }

    // ─── Handler: GET /api/roles/effective ─────────────────────────────────

    /// <summary>
    /// Returns the caller's resolved <see cref="CallerEffectiveRole"/> so the
    /// dashboard can hide affordances the server would deny anyway. The server
    /// remains the sole RBAC enforcement point.
    /// </summary>
    public static async Task<IResult> GetCallerEffectiveRoleAsync(
        Guid tenantId,
        Guid actorPersonId,
        ICallerEffectiveRoleResolver resolver,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var caller = await resolver.ResolveAsync(tenantId, actorPersonId, ct);
        var data = new
        {
            effectiveRole = caller.RmfRole?.ToString(),
            isTenantAdministrator = caller.IsTenantAdministrator,
        };
        return Success(sw, data);
    }

    // ─── Helpers: role-name parsing ────────────────────────────────────────

    /// <summary>
    /// Parses a dashboard role string into the underlying <see cref="OrganizationRole"/>
    /// (the storage type) and its <see cref="RmfRole"/> image (the matrix lookup
    /// key). The <c>"Sca"</c> input is special: it maps to
    /// <see cref="OrganizationRole.Assessor"/> (cross-enum naming convention)
    /// AND to <see cref="RmfRole.Sca"/>. <c>"Administrator"</c> parses to
    /// <see cref="OrganizationRole.Administrator"/> with a <c>null</c> RmfRole
    /// image (Administrator is Org-scope only per FR-020).
    /// </summary>
    private static bool TryParseRole(string role, out OrganizationRole orgRole, out RmfRole? rmfRole)
    {
        switch (role)
        {
            case "AuthorizingOfficial":
                orgRole = OrganizationRole.AuthorizingOfficial;
                rmfRole = RmfRole.AuthorizingOfficial;
                return true;
            case "Issm":
                orgRole = OrganizationRole.Issm;
                rmfRole = RmfRole.Issm;
                return true;
            case "Isso":
                orgRole = OrganizationRole.Isso;
                rmfRole = RmfRole.Isso;
                return true;
            case "Sca":
                orgRole = OrganizationRole.Assessor;
                rmfRole = RmfRole.Sca;
                return true;
            case "SystemOwner":
                orgRole = OrganizationRole.SystemOwner;
                rmfRole = RmfRole.SystemOwner;
                return true;
            case "MissionOwner":
                orgRole = OrganizationRole.MissionOwner;
                rmfRole = RmfRole.MissionOwner;
                return true;
            case "Administrator":
                orgRole = OrganizationRole.Administrator;
                rmfRole = null;
                return true;
            default:
                orgRole = default;
                rmfRole = null;
                return false;
        }
    }

    /// <summary>
    /// Projects a <see cref="ResolvedRoleAssignment"/> from the unified reader
    /// into the dashboard envelope's row shape. The
    /// <see cref="RoleAssignmentSource"/> enum is rendered as kebab-case to
    /// match the TypeScript contract.
    /// </summary>
    private static object ProjectRow(ResolvedRoleAssignment r) => new
    {
        role = r.Role.ToString(),
        person = r.PersonId is null
            ? null
            : new { id = r.PersonId.Value.ToString(), displayName = r.PersonDisplayName ?? string.Empty },
        source = SourceToKebab(r.Source),
        orgRoleId = r.OrgRoleId?.ToString(),
    };

    /// <summary>
    /// Renders <see cref="RoleAssignmentSource"/> in the kebab-case form the
    /// dashboard contract pins (<c>not-assigned</c>, <c>org-fallback</c>, etc.).
    /// </summary>
    private static string SourceToKebab(RoleAssignmentSource s) => s switch
    {
        RoleAssignmentSource.NotAssigned => "not-assigned",
        RoleAssignmentSource.Override => "override",
        RoleAssignmentSource.Inherited => "inherited",
        RoleAssignmentSource.OrgFallback => "org-fallback",
        RoleAssignmentSource.Legacy => "legacy",
        _ => "not-assigned",
    };    /// <summary>
    /// Materializes the closed set of <see cref="SoDWarning"/> records into the
    /// envelope's <c>warnings</c> array. Returns <c>null</c> when the input is
    /// empty so the envelope omits the property entirely (FR-026 contract).
    /// </summary>
    private static IReadOnlyList<object>? ProjectWarnings(IReadOnlyList<SoDWarning> warnings)
    {
        if (warnings.Count == 0) return null;
        return warnings.Select(w => (object)new
        {
            code = w.Code,
            message = w.Message,
            roleConflict = new[] { w.RoleConflict.Existing.ToString(), w.RoleConflict.Target.ToString() },
            dodiReference = w.DodiReference,
            suggestedAction = w.SuggestedAction,
        }).ToList();
    }

    // ─── Helpers: envelope building ────────────────────────────────────────

    /// <summary>
    /// Required header read for tenant context. <see cref="ITenantContext.TenantId"/>
    /// is the source of truth (set by <c>TenantResolutionMiddleware</c> per
    /// Feature 048).
    /// </summary>
    private static Guid RequireTenant(ITenantContext tenant) => tenant.EffectiveTenantId;

    /// <summary>
    /// Best-effort lookup of the calling principal's Person ID. Falls back to
    /// <see cref="Guid.Empty"/> when no claim is present — handlers downstream
    /// resolve to <c>CallerEffectiveRole.None</c> in that case, which fails the
    /// FR-027 matrix unless bootstrap is permissible.
    /// </summary>
    private static Guid ResolveActorPersonId(HttpContext http)
    {
        var tenant = http.RequestServices.GetService<ITenantContext>();
        if (tenant?.IsWorkspaceRequest == true)
            return tenant.PersonId ?? Guid.Empty;
        var claim = http.User?.FindFirst("oid")?.Value
                    ?? http.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// 200 envelope without warnings property.
    /// </summary>
    private static IResult Success(Stopwatch sw, object data) =>
        Results.Json(BuildEnvelope(sw, data, warnings: null),
            statusCode: StatusCodes.Status200OK);

    /// <summary>
    /// 200 envelope; when <paramref name="warnings"/> is non-null and non-empty
    /// it is rendered; when null/empty the property is omitted entirely
    /// (FR-026 contract).
    /// </summary>
    private static IResult SuccessWithOptionalWarnings(
        Stopwatch sw, object data, IReadOnlyList<object>? warnings) =>
        Results.Json(BuildEnvelope(sw, data, warnings),
            statusCode: StatusCodes.Status200OK);

    private static object BuildEnvelope(Stopwatch sw, object data, IReadOnlyList<object>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return new
            {
                status = "success",
                data,
                metadata = new
                {
                    executionTimeMs = sw.ElapsedMilliseconds,
                    timestamp = DateTimeOffset.UtcNow,
                },
            };
        }
        return new
        {
            status = "success",
            data,
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            warnings,
        };
    }

    /// <summary>
    /// 400 envelope for the FR-012 <c>INVALID_ROLE</c> contract. The
    /// <c>suggestion</c> string MUST list all 7 role names verbatim (SC-007).
    /// </summary>
    private static IResult InvalidRole(Stopwatch sw, string submittedRole, string? customSuggestion = null) =>
        Error(sw, StatusCodes.Status400BadRequest, "INVALID_ROLE",
            $"Unknown role '{submittedRole}'.",
            extras: new Dictionary<string, object?>
            {
                ["suggestion"] = customSuggestion ?? AllRolesSuggestionText,
            });

    /// <summary>
    /// 403 envelope for the FR-027 <c>RBAC_ROLE_ASSIGN_DENIED</c> contract.
    /// </summary>
    private static IResult RbacDenied(
        Stopwatch sw,
        CallerEffectiveRole caller,
        RmfRole? targetRole,
        string? orgRoleName = null)
    {
        var targetName = orgRoleName
            ?? targetRole?.ToString()
            ?? "(unknown)";
        return Error(sw, StatusCodes.Status403Forbidden, "RBAC_ROLE_ASSIGN_DENIED",
            $"Caller may not assign role '{targetName}' per FR-027 matrix.",
            extras: new Dictionary<string, object?>
            {
                ["callerEffectiveRole"] = caller.RmfRole?.ToString(),
                ["targetRole"] = targetName,
            });
    }

    /// <summary>
    /// Error envelope writer. <paramref name="extras"/> are merged into the
    /// <c>error</c> object (used for <c>suggestion</c>, <c>orgRoleId</c>,
    /// <c>callerEffectiveRole</c>, <c>targetRole</c>).
    /// </summary>
    private static IResult Error(
        Stopwatch sw,
        int statusCode,
        string code,
        string message,
        IDictionary<string, object?>? extras = null)
    {
        var error = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["message"] = message,
        };
        if (extras is not null)
        {
            foreach (var (k, v) in extras)
            {
                if (v is null) continue;
                error[k] = v;
            }
        }
        var envelope = new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            error,
        };
        return Results.Json(envelope, statusCode: statusCode);
    }
}

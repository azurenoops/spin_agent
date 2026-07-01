// =============================================================================
//  SystemsEndpoints.cs
//  Ato.Copilot.Mcp — Endpoints
//  Issue #588 — DELETE /api/systems/{id} — stop orphan accumulation
//
//  DELETE /api/systems/{id}             → soft-delete (IsActive = false), 204
//  DELETE /api/systems/{id}?permanent=true → hard-delete (remove row),   204
//
//  404 is returned when the GUID is not found in the current tenant's scope.
//  The handler mirrors the logic already in DeleteSystemTool
//  (compliance_delete_system) so behaviour is identical whether the caller
//  uses the MCP tool or the REST endpoint.
// =============================================================================

#nullable enable

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// Maps DELETE /api/systems/{id} — soft-delete (default) and hard-delete
/// (<c>?permanent=true</c>) for registered information systems.
///
/// <para>
/// <b>Soft-delete</b> (default): sets <c>IsActive = false</c> and
/// <c>ModifiedAt = UtcNow</c>.  All child rows are preserved; the system is
/// hidden from every active-only query.  Cascade-safe — no FK violations.
/// </para>
/// <para>
/// <b>Hard-delete</b> (<c>?permanent=true</c>): removes the
/// <see cref="Ato.Copilot.Core.Models.Compliance.RegisteredSystem"/> row
/// entirely.  Database-level <c>ON DELETE CASCADE</c> constraints handle all
/// child tables (boundaries, roles, assessments, POA&amp;Ms, findings).
/// Use only for QA / orphaned test systems.
/// </para>
/// </summary>
public static class SystemsEndpoints
{
    /// <summary>
    /// Registers <c>DELETE /api/systems/{id}</c> on the supplied route builder.
    /// Called from <c>Program.cs</c> in HTTP mode alongside the other
    /// <c>Map*Endpoints</c> extension methods.
    /// </summary>
    public static IEndpointRouteBuilder MapSystemsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/systems")
            .RequireAuthorization()
            .WithTags("Systems");

        // ─── DELETE /api/systems/{id} ────────────────────────────────────────
        group.MapDelete("/{id}", DeleteSystemAsync)
            .WithName("DeleteSystem")
            .WithSummary("Delete a registered system")
            .WithDescription(
                "Soft-deletes (IsActive=false) the specified system by default. " +
                "Pass ?permanent=true to permanently remove the row and all child data. " +
                "Returns 204 on success, 404 if the system is not found.");

        return app;
    }

    // -------------------------------------------------------------------------
    //  Handler
    // -------------------------------------------------------------------------

    /// <summary>
    /// Handles <c>DELETE /api/systems/{id}</c>.
    /// </summary>
    /// <param name="id">System GUID (string, 36 chars).</param>
    /// <param name="permanent">
    ///   <c>true</c> = hard-delete (remove row + cascade children);
    ///   <c>false</c> (default) = soft-delete (IsActive=false).
    /// </param>
    /// <param name="dbFactory">EF Core DbContext factory (injected from DI).</param>
    /// <param name="logger">Logger (injected from DI).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///   204 No Content on success;<br/>
    ///   404 Not Found when the GUID is not in the database;<br/>
    ///   400 Bad Request when <paramref name="id"/> is not a valid GUID.
    /// </returns>
    private static async Task<IResult> DeleteSystemAsync(
        string id,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILogger<LogMarker> logger,
        bool permanent = false,
        CancellationToken ct = default)
    {
        // ── Validate the id is a well-formed GUID ──────────────────────────
        if (!Guid.TryParse(id, out _))
        {
            return Results.Json(
                new { ok = false, errorCode = "INVALID_ID", message = $"'{id}' is not a valid GUID." },
                statusCode: StatusCodes.Status400BadRequest);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // ── Locate the system ──────────────────────────────────────────────
        // AtoCopilotContext applies the tenant query-filter automatically, so
        // this FirstOrDefaultAsync is already scoped to the effective tenant.
        var system = await db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (system is null)
        {
            logger.LogWarning("DELETE /api/systems/{Id}: not found (permanent={Permanent})", id, permanent);

            return Results.Json(
                new
                {
                    ok = false,
                    errorCode = "NOT_FOUND",
                    message = $"System '{id}' was not found."
                },
                statusCode: StatusCodes.Status404NotFound);
        }

        var systemName = system.Name;

        if (permanent)
        {
            // ── Hard-delete: remove row; cascade handles child tables ──────
            db.RegisteredSystems.Remove(system);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Permanently deleted system {Id} ({Name})", id, systemName);

            return Results.Json(
                new
                {
                    ok = true,
                    data = new { systemId = id, deleted = true, permanent = true }
                },
                statusCode: StatusCodes.Status204NoContent);
        }

        // ── Soft-delete: flip IsActive flag ─────────────────────────────
        system.IsActive = false;
        system.ModifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Soft-deleted system {Id} ({Name})", id, systemName);

        return Results.Json(
            new
            {
                ok = true,
                data = new { systemId = id, deleted = true, permanent = false }
            },
            statusCode: StatusCodes.Status204NoContent);
    }

    /// <summary>Non-static marker class for <see cref="ILogger{T}"/> category.</summary>
    public sealed class LogMarker { }
}

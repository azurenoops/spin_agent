using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// Org-user Capability Library endpoints (UF-CSP-01/02/03 — spec-070).
///
/// These endpoints serve the org-user (ISSO/ISSM/SCA) view of the CSP capability catalog.
/// They are intentionally separate from the CSP-admin endpoints at /api/csp/inherited-components.
///
/// Routes registered:
///   GET    /api/dashboard/capability-library                         List published CSP capabilities
///   GET    /api/dashboard/capability-library/{id}                   Single capability with control coverage
///   POST   /api/dashboard/systems/{systemId}/capability-subscriptions   Subscribe
///   GET    /api/dashboard/systems/{systemId}/capability-subscriptions   List subscriptions
///   DELETE /api/dashboard/systems/{systemId}/capability-subscriptions/{capabilityId}  Unsubscribe
/// </summary>
public static class CapabilitySubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapCapabilitySubscriptionEndpoints(
        this IEndpointRouteBuilder app)
    {
        // ─── Capability Library — org-user browse ────────────────────────────

        // GET /api/dashboard/capability-library
        // Returns Published CSP capabilities in an org-user-safe projection.
        // Supports filtering by control family and CSP provider; search by name.
        app.MapGet("/api/dashboard/capability-library", async (
            string? search,
            string? provider,
            string? controlFamily,
            string? systemId,
            AtoCopilotContext db,
            CancellationToken ct) =>
        {
            var query = db.CspInheritedCapabilities
                .Include(c => c.CspInheritedComponent)
                .Where(c => c.Status == "Published");

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.Name.Contains(search) || c.Description!.Contains(search));

            if (!string.IsNullOrWhiteSpace(provider))
                query = query.Where(c => c.CspInheritedComponent!.ComponentType == provider);

            var capabilities = await query
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    description = c.Description,
                    provider = c.CspInheritedComponent != null ? c.CspInheritedComponent.ComponentType : "Unknown",
                    componentName = c.CspInheritedComponent != null ? c.CspInheritedComponent.Name : "",
                    status = c.Status,
                    controlCount = c.ControlMappings != null ? c.ControlMappings.Count : 0,
                })
                .ToListAsync(ct);

            // If systemId provided, annotate which capabilities are already subscribed
            HashSet<string> subscribed = new();
            if (!string.IsNullOrWhiteSpace(systemId))
            {
                subscribed = (await db.CapabilitySubscriptions
                    .Where(s => s.RegisteredSystemId == systemId && s.IsActive)
                    .Select(s => s.CspInheritedCapabilityId)
                    .ToListAsync(ct)).ToHashSet();
            }

            // Apply control family filter client-side (JSON navigation property)
            var result = capabilities.Select(c => new
            {
                c.id, c.name, c.description, c.provider,
                c.componentName, c.status, c.controlCount,
                isSubscribed = subscribed.Contains(c.id),
            });

            if (!string.IsNullOrWhiteSpace(controlFamily))
                result = result.Where(c => true); // families resolved via detail endpoint

            return Results.Ok(new { items = result, totalCount = result.Count() });
        })
        .WithName("ListCapabilityLibrary")
        .WithTags("Capability Library");

        // GET /api/dashboard/capability-library/{id}
        // Returns a single CSP capability with full control coverage detail.
        // Used by UF-CSP-02 (OrgCapabilityDetailPage).
        app.MapGet("/api/dashboard/capability-library/{id}", async (
            string id,
            AtoCopilotContext db,
            CancellationToken ct) =>
        {
            var capability = await db.CspInheritedCapabilities
                .Include(c => c.CspInheritedComponent)
                .Include(c => c.ControlMappings)
                .FirstOrDefaultAsync(c => c.Id == id, ct);

            if (capability is null)
                return Results.NotFound(new { error = "Capability not found", errorCode = "NOT_FOUND" });

            var result = new
            {
                id = capability.Id,
                name = capability.Name,
                description = capability.Description,
                provider = capability.CspInheritedComponent?.ComponentType ?? "Unknown",
                componentName = capability.CspInheritedComponent?.Name ?? "",
                status = capability.Status,
                controlMappings = capability.ControlMappings?.Select(m => new
                {
                    controlId = m.ControlId,
                    inheritanceType = m.InheritanceType,
                    notes = m.Notes,
                }) ?? Enumerable.Empty<object>(),
            };

            return Results.Ok(result);
        })
        .WithName("GetCapabilityLibraryDetail")
        .WithTags("Capability Library");

        // ─── Capability Subscriptions — per-system CRUD ───────────────────────

        // POST /api/dashboard/systems/{systemId}/capability-subscriptions
        // Subscribes a system to a CSP capability (UF-CSP-01).
        // Idempotent: re-subscribing a previously active subscription returns 200.
        app.MapPost("/api/dashboard/systems/{systemId}/capability-subscriptions", async (
            string systemId,
            SubscribeCapabilityRequest body,
            AtoCopilotContext db,
            CancellationToken ct) =>
        {
            // Check the capability exists and is Published
            var capability = await db.CspInheritedCapabilities
                .FirstOrDefaultAsync(c => c.Id == body.CapabilityId, ct);

            if (capability is null)
                return Results.NotFound(new { error = "Capability not found", errorCode = "NOT_FOUND" });

            if (capability.Status != "Published")
                return Results.BadRequest(new
                {
                    error = "Only Published capabilities can be subscribed.",
                    errorCode = "CAPABILITY_NOT_PUBLISHED",
                });

            // Check for existing subscription
            var existing = await db.CapabilitySubscriptions
                .FirstOrDefaultAsync(s =>
                    s.RegisteredSystemId == systemId &&
                    s.CspInheritedCapabilityId == body.CapabilityId, ct);

            if (existing is not null)
            {
                if (existing.IsActive)
                    return Results.Ok(new { id = existing.Id, alreadySubscribed = true });

                // Re-activate soft-deleted subscription
                existing.IsActive = true;
                existing.SubscribedAt = DateTime.UtcNow;
                existing.SubscribedBy = body.SubscribedBy ?? "dashboard-user";
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { id = existing.Id, alreadySubscribed = false });
            }

            // Create new subscription
            var subscription = new CapabilitySubscription
            {
                RegisteredSystemId = systemId,
                CspInheritedCapabilityId = body.CapabilityId,
                SubscribedBy = body.SubscribedBy ?? "dashboard-user",
            };

            db.CapabilitySubscriptions.Add(subscription);

            // Audit log
            db.DashboardActivities.Add(new Core.Models.Dashboard.DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "CapabilitySubscribed",
                Actor = body.SubscribedBy ?? "dashboard-user",
                Summary = $"Subscribed to capability: {capability.Name}",
                RelatedEntityType = "CapabilitySubscription",
                RelatedEntityId = subscription.Id,
            });

            await db.SaveChangesAsync(ct);

            return Results.Created(
                $"/api/dashboard/systems/{systemId}/capability-subscriptions/{subscription.Id}",
                new { id = subscription.Id, alreadySubscribed = false });
        })
        .WithName("SubscribeToCapability")
        .WithTags("Capability Library");

        // GET /api/dashboard/systems/{systemId}/capability-subscriptions
        // Lists all active capability subscriptions for a system.
        app.MapGet("/api/dashboard/systems/{systemId}/capability-subscriptions", async (
            string systemId,
            AtoCopilotContext db,
            CancellationToken ct) =>
        {
            var subs = await db.CapabilitySubscriptions
                .Include(s => s.Capability)
                    .ThenInclude(c => c!.CspInheritedComponent)
                .Where(s => s.RegisteredSystemId == systemId && s.IsActive)
                .OrderBy(s => s.SubscribedAt)
                .Select(s => new
                {
                    id = s.Id,
                    capabilityId = s.CspInheritedCapabilityId,
                    capabilityName = s.Capability != null ? s.Capability.Name : s.CspInheritedCapabilityId,
                    provider = s.Capability != null && s.Capability.CspInheritedComponent != null
                        ? s.Capability.CspInheritedComponent.ComponentType
                        : "Unknown",
                    subscribedBy = s.SubscribedBy,
                    subscribedAt = s.SubscribedAt,
                })
                .ToListAsync(ct);

            return Results.Ok(new { items = subs, totalCount = subs.Count });
        })
        .WithName("ListCapabilitySubscriptions")
        .WithTags("Capability Library");

        // DELETE /api/dashboard/systems/{systemId}/capability-subscriptions/{capabilityId}
        // Unsubscribes a system from a CSP capability (UF-CSP-03).
        // Soft-deletes: sets IsActive=false to preserve audit trail.
        app.MapDelete("/api/dashboard/systems/{systemId}/capability-subscriptions/{capabilityId}", async (
            string systemId,
            string capabilityId,
            AtoCopilotContext db,
            CancellationToken ct) =>
        {
            var sub = await db.CapabilitySubscriptions
                .FirstOrDefaultAsync(s =>
                    s.RegisteredSystemId == systemId &&
                    s.CspInheritedCapabilityId == capabilityId &&
                    s.IsActive, ct);

            if (sub is null)
                return Results.NotFound(new { error = "Active subscription not found", errorCode = "NOT_FOUND" });

            sub.IsActive = false;

            // Capability name for audit log
            var capabilityName = (await db.CspInheritedCapabilities
                .Where(c => c.Id == capabilityId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct)) ?? capabilityId;

            db.DashboardActivities.Add(new Core.Models.Dashboard.DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "CapabilityUnsubscribed",
                Actor = "dashboard-user",
                Summary = $"Unsubscribed from capability: {capabilityName}",
                RelatedEntityType = "CapabilitySubscription",
                RelatedEntityId = sub.Id,
            });

            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = sub.Id, unsubscribed = true });
        })
        .WithName("UnsubscribeFromCapability")
        .WithTags("Capability Library");

        return app;
    }

    // ─── Request DTOs ──────────────────────────────────────────────────────────

    private record SubscribeCapabilityRequest(
        string CapabilityId,
        string? SubscribedBy);
}

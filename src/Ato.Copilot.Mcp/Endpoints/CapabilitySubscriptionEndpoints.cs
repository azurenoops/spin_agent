using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// Org-user Capability Library endpoints (UF-CSP-01/02/03 — spec-070).
///
/// These endpoints serve the org-user (ISSO/ISSM/SCA) view of the CSP capability
/// catalog. Only CspInheritedCapabilities with Status == Mapped are returned —
/// NeedsReview and Archived capabilities are filtered out at the query level.
///
/// Separate from CSP-admin endpoints at /api/csp/inherited-components.
///
/// Routes:
///   GET    /api/dashboard/capability-library
///   GET    /api/dashboard/capability-library/{id}
///   POST   /api/dashboard/systems/{systemId}/capability-subscriptions
///   GET    /api/dashboard/systems/{systemId}/capability-subscriptions
///   DELETE /api/dashboard/systems/{systemId}/capability-subscriptions/{capabilityId}
/// </summary>
public static class CapabilitySubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapCapabilitySubscriptionEndpoints(
        this IEndpointRouteBuilder app)
    {
        // ─── Capability Library — org-user browse ────────────────────────────

        // GET /api/dashboard/capability-library
        // Returns CSP capabilities with Status == Mapped, in org-user projection.
        // Optional query params: search (name/description), provider (ComponentType enum),
        // systemId (annotates isSubscribed per capability).
        app.MapGet("/api/dashboard/capability-library", async (
            string? search,
            string? provider,
            string? systemId,
            AtoCopilotContext db,
            CancellationToken ct) =>
        {
            // Base query: only Mapped (consumable by org tenants)
            var query = db.CspInheritedCapabilities
                .Include(c => c.CspInheritedComponent)
                .Where(c => c.Status == CspInheritedCapabilityStatus.Mapped)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c =>
                    c.Name.Contains(search) || c.Description.Contains(search));

            if (!string.IsNullOrWhiteSpace(provider) &&
                Enum.TryParse<CspComponentType>(provider, ignoreCase: true, out var providerEnum))
                query = query.Where(c => c.CspInheritedComponent.ComponentType == providerEnum);

            var capabilities = await query
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    id = c.Id.ToString(),
                    name = c.Name,
                    description = c.Description,
                    provider = c.CspInheritedComponent.ComponentType.ToString(),
                    componentName = c.CspInheritedComponent.Name,
                    controlCount = c.MappedNistControlIds.Count,
                    mappedControls = c.MappedNistControlIds,
                })
                .ToListAsync(ct);

            // Annotate subscription status if caller provided their systemId
            HashSet<string> subscribed = new(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(systemId))
            {
                subscribed = (await db.CapabilitySubscriptions
                    .Where(s => s.RegisteredSystemId == systemId && s.IsActive)
                    .Select(s => s.CspInheritedCapabilityId)
                    .ToListAsync(ct))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            var result = capabilities.Select(c => new
            {
                c.id, c.name, c.description, c.provider,
                c.componentName, c.controlCount, c.mappedControls,
                isSubscribed = subscribed.Contains(c.id),
            });

            return Results.Ok(new { items = result, totalCount = result.Count() });
        })
        .WithName("ListCapabilityLibrary")
        .WithTags("Capability Library");

        // ─────────────────────────────────────────────────────────────────────

        // GET /api/dashboard/capability-library/{id}
        // Returns a single Mapped capability with full control detail.
        // Used by UF-CSP-02 (OrgCapabilityDetailPage).
        app.MapGet("/api/dashboard/capability-library/{id:guid}", async (
            Guid id,
            string? systemId,
            AtoCopilotContext db,
            CancellationToken ct) =>
        {
            var capability = await db.CspInheritedCapabilities
                .Include(c => c.CspInheritedComponent)
                .FirstOrDefaultAsync(c =>
                    c.Id == id && c.Status == CspInheritedCapabilityStatus.Mapped, ct);

            if (capability is null)
                return Results.NotFound(new { error = "Capability not found", errorCode = "NOT_FOUND" });

            // Check subscription if systemId provided
            bool isSubscribed = false;
            if (!string.IsNullOrWhiteSpace(systemId))
            {
                isSubscribed = await db.CapabilitySubscriptions.AnyAsync(s =>
                    s.RegisteredSystemId == systemId &&
                    s.CspInheritedCapabilityId == id.ToString() &&
                    s.IsActive, ct);
            }

            return Results.Ok(new
            {
                id = capability.Id.ToString(),
                name = capability.Name,
                description = capability.Description,
                provider = capability.CspInheritedComponent.ComponentType.ToString(),
                componentName = capability.CspInheritedComponent.Name,
                mappedControls = capability.MappedNistControlIds,
                mappingConfidence = capability.MappingConfidence,
                reviewerNote = capability.ReviewerNote,
                reviewedBy = capability.ReviewedBy,
                reviewedAt = capability.ReviewedAt,
                isSubscribed,
            });
        })
        .WithName("GetCapabilityLibraryDetail")
        .WithTags("Capability Library");

        // ─── Capability Subscriptions — per-system ────────────────────────────

        // POST /api/dashboard/systems/{systemId}/capability-subscriptions
        // Subscribe a system to a Mapped CSP capability (UF-CSP-01).
        // Idempotent: re-subscribe after unsubscribe reactivates the record.
        app.MapPost("/api/dashboard/systems/{systemId}/capability-subscriptions", async (
            string systemId,
            SubscribeCapabilityRequest body,
            AtoCopilotContext db,
            CancellationToken ct) =>
        {
            // Validate capability exists and is Mapped
            if (!Guid.TryParse(body.CapabilityId, out var capGuid))
                return Results.BadRequest(new { error = "Invalid capabilityId", errorCode = "INVALID_INPUT" });

            var capability = await db.CspInheritedCapabilities
                .FirstOrDefaultAsync(c => c.Id == capGuid, ct);

            if (capability is null)
                return Results.NotFound(new { error = "Capability not found", errorCode = "NOT_FOUND" });

            if (capability.Status != CspInheritedCapabilityStatus.Mapped)
                return Results.BadRequest(new
                {
                    error = "Only Mapped capabilities can be subscribed to.",
                    errorCode = "CAPABILITY_NOT_MAPPED",
                });

            // Idempotency: re-activate if previously unsubscribed
            var existing = await db.CapabilitySubscriptions.FirstOrDefaultAsync(s =>
                s.RegisteredSystemId == systemId &&
                s.CspInheritedCapabilityId == body.CapabilityId, ct);

            if (existing is not null)
            {
                if (existing.IsActive)
                    return Results.Ok(new { id = existing.Id, alreadySubscribed = true });

                existing.IsActive = true;
                existing.SubscribedAt = DateTime.UtcNow;
                existing.SubscribedBy = body.SubscribedBy ?? "dashboard-user";
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { id = existing.Id, alreadySubscribed = false });
            }

            var subscription = new CapabilitySubscription
            {
                RegisteredSystemId = systemId,
                CspInheritedCapabilityId = body.CapabilityId,
                SubscribedBy = body.SubscribedBy ?? "dashboard-user",
            };

            db.CapabilitySubscriptions.Add(subscription);

            db.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "CapabilitySubscribed",
                Actor = body.SubscribedBy ?? "dashboard-user",
                Summary = $"Subscribed to CSP capability: {capability.Name}",
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

        // ─────────────────────────────────────────────────────────────────────

        // GET /api/dashboard/systems/{systemId}/capability-subscriptions
        // Lists active capability subscriptions for a system.
        app.MapGet("/api/dashboard/systems/{systemId}/capability-subscriptions", async (
            string systemId,
            AtoCopilotContext db,
            CancellationToken ct) =>
        {
            // Fetch subscriptions + enrich with capability names from CspInheritedCapabilities
            var subs = await db.CapabilitySubscriptions
                .Where(s => s.RegisteredSystemId == systemId && s.IsActive)
                .OrderBy(s => s.SubscribedAt)
                .Select(s => new
                {
                    id = s.Id,
                    capabilityId = s.CspInheritedCapabilityId,
                    subscribedBy = s.SubscribedBy,
                    subscribedAt = s.SubscribedAt,
                })
                .ToListAsync(ct);

            // Resolve capability names in one secondary query
            var capIds = subs.Select(s => s.capabilityId).Distinct().ToList();
            var capGuids = capIds
                .Select(id => Guid.TryParse(id, out var g) ? (Guid?)g : null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value)
                .ToList();

            var capNames = await db.CspInheritedCapabilities
                .Where(c => capGuids.Contains(c.Id))
                .Select(c => new { id = c.Id.ToString(), c.Name })
                .ToListAsync(ct);

            var nameMap = capNames.ToDictionary(c => c.id, c => c.Name);

            var result = subs.Select(s => new
            {
                s.id, s.capabilityId,
                capabilityName = nameMap.TryGetValue(s.capabilityId, out var n) ? n : s.capabilityId,
                s.subscribedBy, s.subscribedAt,
            });

            return Results.Ok(new { items = result, totalCount = result.Count() });
        })
        .WithName("ListCapabilitySubscriptions")
        .WithTags("Capability Library");

        // ─────────────────────────────────────────────────────────────────────

        // DELETE /api/dashboard/systems/{systemId}/capability-subscriptions/{capabilityId}
        // Unsubscribes a system from a CSP capability (UF-CSP-03).
        // Soft-delete: sets IsActive=false to preserve audit trail.
        app.MapDelete(
            "/api/dashboard/systems/{systemId}/capability-subscriptions/{capabilityId}",
            async (
                string systemId,
                string capabilityId,
                AtoCopilotContext db,
                CancellationToken ct) =>
        {
            var sub = await db.CapabilitySubscriptions.FirstOrDefaultAsync(s =>
                s.RegisteredSystemId == systemId &&
                s.CspInheritedCapabilityId == capabilityId &&
                s.IsActive, ct);

            if (sub is null)
                return Results.NotFound(new { error = "Active subscription not found", errorCode = "NOT_FOUND" });

            sub.IsActive = false;

            // Capability name for audit log
            Guid.TryParse(capabilityId, out var capGuid);
            var capabilityName = (await db.CspInheritedCapabilities
                .Where(c => c.Id == capGuid)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct)) ?? capabilityId;

            db.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "CapabilityUnsubscribed",
                Actor = "dashboard-user",
                Summary = $"Unsubscribed from CSP capability: {capabilityName}",
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

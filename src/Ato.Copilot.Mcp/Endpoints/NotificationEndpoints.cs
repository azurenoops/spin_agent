using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// Maps /api/dashboard/notifications/* REST endpoints for the notification center.
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard/notifications")
            .WithTags("Notifications");

        // ─── List notifications for the current user ─────────────────────────
        group.MapGet("/", async (
                string? userId,
                bool? unreadOnly,
                int? limit,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var resolvedUserId = userId ?? "dashboard-user";
                var take = Math.Clamp(limit ?? 50, 1, 200);

                var query = db.AlertNotifications
                    .Include(n => n.Alert)
                    .Where(n => n.UserId == resolvedUserId)
                    .AsQueryable();

                if (unreadOnly == true)
                    query = query.Where(n => !n.IsRead);

                var notifications = await query
                    .OrderByDescending(n => n.SentAt)
                    .Take(take)
                    .Select(n => new NotificationDto
                    {
                        Id = n.Id,
                        AlertId = n.AlertId,
                        Channel = n.Channel.ToString(),
                        Subject = n.Subject,
                        Body = n.Body,
                        IsRead = n.IsRead,
                        ReadAt = n.ReadAt,
                        SentAt = n.SentAt,
                        AlertTitle = n.Alert.Title,
                        AlertSeverity = n.Alert.Severity.ToString(),
                    })
                    .ToListAsync(ct);

                return Results.Ok(new { items = notifications, totalCount = notifications.Count });
            })
            .WithName("ListNotifications");

        // ─── Unread count (badge) ────────────────────────────────────────────
        group.MapGet("/summary", async (
                string? userId,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var resolvedUserId = userId ?? "dashboard-user";

                var unreadCount = await db.AlertNotifications
                    .CountAsync(n => n.UserId == resolvedUserId && !n.IsRead, ct);

                var totalCount = await db.AlertNotifications
                    .CountAsync(n => n.UserId == resolvedUserId, ct);

                return Results.Ok(new NotificationSummaryDto
                {
                    UnreadCount = unreadCount,
                    TotalCount = totalCount,
                });
            })
            .WithName("GetNotificationSummary");

        // ─── Mark notifications as read ──────────────────────────────────────
        group.MapPost("/mark-read", async (
                MarkNotificationsReadRequest body,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                if (body.NotificationIds.Count == 0)
                    return Results.BadRequest(new { error = "NotificationIds is required", errorCode = "INVALID_INPUT" });

                var now = DateTimeOffset.UtcNow;
                var notifications = await db.AlertNotifications
                    .Where(n => body.NotificationIds.Contains(n.Id) && !n.IsRead)
                    .ToListAsync(ct);

                foreach (var n in notifications)
                {
                    n.IsRead = true;
                    n.ReadAt = now;
                }

                await db.SaveChangesAsync(ct);

                return Results.Ok(new { markedCount = notifications.Count });
            })
            .WithName("MarkNotificationsRead");

        // ─── Mark all as read ────────────────────────────────────────────────
        group.MapPost("/mark-all-read", async (
                string? userId,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var resolvedUserId = userId ?? "dashboard-user";
                var now = DateTimeOffset.UtcNow;

                var unread = await db.AlertNotifications
                    .Where(n => n.UserId == resolvedUserId && !n.IsRead)
                    .ToListAsync(ct);

                foreach (var n in unread)
                {
                    n.IsRead = true;
                    n.ReadAt = now;
                }

                await db.SaveChangesAsync(ct);

                return Results.Ok(new { markedCount = unread.Count });
            })
            .WithName("MarkAllNotificationsRead");

        // ─── Notification preferences ────────────────────────────────────────
        group.MapGet("/preferences", async (
                string? userId,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var resolvedUserId = userId ?? "dashboard-user";

                var prefs = await db.NotificationPreferences
                    .FirstOrDefaultAsync(p => p.UserId == resolvedUserId, ct);

                if (prefs is null)
                    return Results.Ok(new NotificationPreferencesDto());

                return Results.Ok(new NotificationPreferencesDto
                {
                    PoamOverdueAlerts = prefs.PoamOverdueAlerts,
                    AtoExpirationAlerts = prefs.AtoExpirationAlerts,
                    ComplianceDriftAlerts = prefs.ComplianceDriftAlerts,
                    AlertDaysBefore = prefs.AlertDaysBefore,
                });
            })
            .WithName("GetNotificationPreferences");

        group.MapPut("/preferences", async (
                string? userId,
                NotificationPreferencesDto body,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var resolvedUserId = userId ?? "dashboard-user";

                var prefs = await db.NotificationPreferences
                    .FirstOrDefaultAsync(p => p.UserId == resolvedUserId, ct);

                if (prefs is null)
                {
                    prefs = new Core.Models.Compliance.NotificationPreferences
                    {
                        Id = Guid.NewGuid(),
                        UserId = resolvedUserId,
                        CreatedAt = DateTimeOffset.UtcNow,
                    };
                    db.NotificationPreferences.Add(prefs);
                }

                prefs.PoamOverdueAlerts = body.PoamOverdueAlerts;
                prefs.AtoExpirationAlerts = body.AtoExpirationAlerts;
                prefs.ComplianceDriftAlerts = body.ComplianceDriftAlerts;
                prefs.AlertDaysBefore = body.AlertDaysBefore;
                prefs.UpdatedAt = DateTimeOffset.UtcNow;

                await db.SaveChangesAsync(ct);

                return Results.Ok(new NotificationPreferencesDto
                {
                    PoamOverdueAlerts = prefs.PoamOverdueAlerts,
                    AtoExpirationAlerts = prefs.AtoExpirationAlerts,
                    ComplianceDriftAlerts = prefs.ComplianceDriftAlerts,
                    AlertDaysBefore = prefs.AlertDaysBefore,
                });
            })
            .WithName("UpdateNotificationPreferences");

        return app;
    }
}

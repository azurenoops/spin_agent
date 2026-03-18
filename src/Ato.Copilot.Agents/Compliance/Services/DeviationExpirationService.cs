using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Background service that runs daily at 06:00 UTC to:
/// 1. Expire approved deviations past their expiration date
/// 2. Fire 30-day and 7-day expiration warning notifications
/// </summary>
public class DeviationExpirationService : BackgroundService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly INotificationBroadcaster? _broadcaster;
    private readonly ILogger<DeviationExpirationService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private const int ExpirationHourUtc = 6; // Run at 06:00 UTC

    private DateOnly _lastRunDate = DateOnly.MinValue;

    public DeviationExpirationService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILogger<DeviationExpirationService> logger,
        INotificationBroadcaster? broadcaster = null)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _broadcaster = broadcaster;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeviationExpirationService started — runs at {Hour}:00 UTC", ExpirationHourUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var today = DateOnly.FromDateTime(now.UtcDateTime);

                if (now.Hour >= ExpirationHourUtc && _lastRunDate < today)
                {
                    await ProcessExpirationsAsync(stoppingToken);
                    await ProcessExpirationWarningsAsync(stoppingToken);
                    _lastRunDate = today;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during deviation expiration processing");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task ProcessExpirationsAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var expiredDeviations = await db.Deviations
            .Include(d => d.Finding)
            .Include(d => d.PoamEntry)
            .Where(d => d.Status == DeviationStatus.Approved && d.ExpirationDate < DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var deviation in expiredDeviations)
        {
            deviation.Status = DeviationStatus.Expired;
            deviation.ModifiedAt = DateTime.UtcNow;

            // Revert linked finding
            if (deviation.Finding != null)
            {
                deviation.Finding.Status = FindingStatus.Open;
                deviation.Finding.DeviationId = null;
            }

            // Revert linked POA&M
            if (deviation.PoamEntry != null)
            {
                deviation.PoamEntry.Status = PoamStatus.Ongoing;
                deviation.PoamEntry.DeviationId = null;
            }

            db.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = deviation.RegisteredSystemId,
                EventType = "DeviationExpired",
                Actor = "system",
                Summary = $"Deviation for {deviation.ControlId} expired; linked finding/POA&M reverted",
                RelatedEntityType = "Deviation",
                RelatedEntityId = deviation.Id,
            });

            // Broadcast expiration notification
            await BroadcastAsync(db, deviation.RequestedBy,
                $"Deviation Expired: {deviation.ControlId}",
                $"Your {deviation.DeviationType} deviation for {deviation.ControlId} has expired. " +
                "Any linked findings and POA&M items have been reverted to their original status.",
                ct);
        }

        if (expiredDeviations.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Expired {Count} deviations", expiredDeviations.Count);
        }
    }

    private async Task ProcessExpirationWarningsAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var in30Days = now.AddDays(30);
        var in7Days = now.AddDays(7);

        // 30-day warnings
        var thirtyDayDeviations = await db.Deviations
            .Where(d => d.Status == DeviationStatus.Approved
                && d.ExpirationDate >= in7Days
                && d.ExpirationDate <= in30Days)
            .ToListAsync(ct);

        foreach (var d in thirtyDayDeviations)
        {
            var daysLeft = (int)(d.ExpirationDate - now).TotalDays;
            await BroadcastAsync(db, d.RequestedBy,
                $"Deviation Expiring: {d.ControlId}",
                $"Your {d.DeviationType} deviation for {d.ControlId} expires in {daysLeft} days. " +
                "Consider requesting an extension.",
                ct);
        }

        // 7-day warnings
        var sevenDayDeviations = await db.Deviations
            .Where(d => d.Status == DeviationStatus.Approved
                && d.ExpirationDate >= now
                && d.ExpirationDate < in7Days)
            .ToListAsync(ct);

        foreach (var d in sevenDayDeviations)
        {
            var daysLeft = (int)(d.ExpirationDate - now).TotalDays;
            await BroadcastAsync(db, d.RequestedBy,
                $"Deviation Expiring Soon: {d.ControlId}",
                $"URGENT: Your {d.DeviationType} deviation for {d.ControlId} expires in {daysLeft} days. " +
                "Request an extension immediately to avoid automatic expiration.",
                ct);
        }

        if (thirtyDayDeviations.Count > 0 || sevenDayDeviations.Count > 0)
        {
            _logger.LogInformation(
                "Sent expiration warnings: {ThirtyDay} at 30d, {SevenDay} at 7d",
                thirtyDayDeviations.Count, sevenDayDeviations.Count);
        }
    }

    private async Task BroadcastAsync(
        AtoCopilotContext db, string userId, string subject, string body, CancellationToken ct)
    {
        if (_broadcaster is null || string.IsNullOrEmpty(userId)) return;

        var notification = new AlertNotification
        {
            Id = Guid.NewGuid(),
            AlertId = Guid.Empty,
            Channel = NotificationChannel.Chat,
            Recipient = userId,
            Subject = subject,
            Body = body,
            IsDelivered = true,
            SentAt = DateTimeOffset.UtcNow,
            DeliveredAt = DateTimeOffset.UtcNow,
            UserId = userId,
        };

        db.AlertNotifications.Add(notification);
        await db.SaveChangesAsync(ct);

        try
        {
            await _broadcaster.BroadcastToUserAsync(userId, notification, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to broadcast expiration notification — client may not be connected");
        }
    }
}

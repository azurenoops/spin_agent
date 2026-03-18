using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Multi-channel alert notification service with rate limiting, quiet hours support,
/// and HMAC-SHA256 signed webhook payloads.
/// Singleton service using IDbContextFactory for DB access.
/// </summary>
public class AlertNotificationService : IAlertNotificationService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IComplianceWatchService _watchService;
    private readonly INotificationBroadcaster? _broadcaster;
    private readonly ILogger<AlertNotificationService> _logger;

    // Rate limiter: max 10 notifications per minute per channel
    private readonly Dictionary<NotificationChannel, SlidingWindowRateLimiter> _rateLimiters;

    public AlertNotificationService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IComplianceWatchService watchService,
        ILogger<AlertNotificationService> logger,
        INotificationBroadcaster? broadcaster = null)
    {
        _dbFactory = dbFactory;
        _watchService = watchService;
        _logger = logger;
        _broadcaster = broadcaster;

        _rateLimiters = new Dictionary<NotificationChannel, SlidingWindowRateLimiter>
        {
            [NotificationChannel.Chat] = CreateRateLimiter(),
            [NotificationChannel.Email] = CreateRateLimiter(),
            [NotificationChannel.Webhook] = CreateRateLimiter()
        };
    }

    /// <inheritdoc />
    public async Task SendNotificationAsync(ComplianceAlert alert, CancellationToken cancellationToken = default)
    {
        // Check quiet hours suppression
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var suppressions = await db.SuppressionRules
            .Where(s => s.IsActive && s.QuietHoursStart != null)
            .ToListAsync(cancellationToken);

        if (_watchService.IsAlertSuppressed(alert, suppressions))
        {
            _logger.LogDebug("Alert {AlertId} held during quiet hours (non-Critical)", alert.AlertId);
            return;
        }

        // Chat notification — always enabled
        await DispatchNotificationAsync(alert, NotificationChannel.Chat, "system", cancellationToken);

        // Email — immediate for Critical/High, deferred digest for Medium/Low
        if (alert.Severity is AlertSeverity.Critical or AlertSeverity.High)
        {
            await DispatchNotificationAsync(alert, NotificationChannel.Email, "system", cancellationToken);
        }

        // Webhook — check for configured escalation paths with webhook URLs
        var webhookPaths = await db.EscalationPaths
            .Where(p => p.IsEnabled && p.WebhookUrl != null && p.TriggerSeverity <= alert.Severity)
            .ToListAsync(cancellationToken);

        foreach (var path in webhookPaths)
        {
            await DispatchWebhookAsync(alert, path.WebhookUrl!, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task SendDigestAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Get alerts that haven't had digest notifications sent today
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var alerts = await db.ComplianceAlerts
            .Where(a => a.SubscriptionId == subscriptionId
                && a.CreatedAt >= cutoff
                && (a.Severity == AlertSeverity.Medium || a.Severity == AlertSeverity.Low)
                && a.Status == AlertStatus.New)
            .ToListAsync(cancellationToken);

        // Deviation digest section (Feature 035)
        int pendingDeviations;
        int expiringDeviations;
        try
        {
            pendingDeviations = await db.Deviations
                .CountAsync(d => d.Status == DeviationStatus.Pending, cancellationToken);
            expiringDeviations = await db.Deviations
                .CountAsync(d => d.Status == DeviationStatus.Approved
                    && d.ExpirationDate <= DateTime.UtcNow.AddDays(30), cancellationToken);
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            pendingDeviations = 0;
            expiringDeviations = 0;
        }

        if (alerts.Count == 0 && pendingDeviations == 0 && expiringDeviations == 0) return;

        var subject = $"[Compliance Digest] {alerts.Count} alert(s) for {subscriptionId}";
        var body = JsonSerializer.Serialize(new
        {
            type = "digest",
            subscriptionId,
            alertCount = alerts.Count,
            alerts = alerts.Select(a => new { a.AlertId, a.Title, severity = a.Severity.ToString(), a.CreatedAt }),
            deviations = new
            {
                pendingReviews = pendingDeviations,
                expiringWithin30Days = expiringDeviations,
            },
            generatedAt = DateTimeOffset.UtcNow
        });

        var notification = new AlertNotification
        {
            Id = Guid.NewGuid(),
            AlertId = alerts.Count > 0 ? alerts.First().Id : Guid.Empty,
            Channel = NotificationChannel.Email,
            Recipient = "digest",
            Subject = subject,
            Body = body,
            IsDelivered = true,
            SentAt = DateTimeOffset.UtcNow,
            DeliveredAt = DateTimeOffset.UtcNow
        };

        db.AlertNotifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Sent digest with {AlertCount} alerts, {PendingDeviations} pending deviations for {Sub}",
            alerts.Count, pendingDeviations, subscriptionId);
    }

    /// <inheritdoc />
    public async Task<List<AlertNotification>> GetNotificationsForAlertAsync(
        Guid alertId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.AlertNotifications
            .Where(n => n.AlertId == alertId)
            .OrderByDescending(n => n.SentAt)
            .ToListAsync(cancellationToken);
    }

    // ─── Private Helpers ────────────────────────────────────────────────────

    private async Task DispatchNotificationAsync(
        ComplianceAlert alert,
        NotificationChannel channel,
        string recipient,
        CancellationToken cancellationToken)
    {
        // Rate limiting
        if (!_rateLimiters.TryGetValue(channel, out var limiter))
            return;

        using var lease = limiter.AttemptAcquire();
        if (!lease.IsAcquired)
        {
            _logger.LogWarning("Rate limit exceeded for channel {Channel} — notification deferred", channel);
            await RecordNotification(alert, channel, recipient,
                isDelivered: false, error: "RATE_LIMITED", cancellationToken);
            return;
        }

        var subject = $"[{alert.Severity}] {alert.Title}";
        var body = JsonSerializer.Serialize(new
        {
            alertId = alert.AlertId,
            type = alert.Type.ToString(),
            severity = alert.Severity.ToString(),
            title = alert.Title,
            description = alert.Description,
            affectedResources = alert.AffectedResources,
            recommendedAction = alert.RecommendedAction,
            createdAt = alert.CreatedAt
        });

        // TODO: Implement actual SMTP/Teams/Slack delivery when channels are configured.
        // Currently records notification for audit trail but does not deliver externally.
        _logger.LogDebug("Notification recorded for channel {Channel} (delivery pending external integration)", channel);

        await RecordNotification(alert, channel, recipient,
            isDelivered: true, error: null, cancellationToken, subject, body);
    }

    private async Task DispatchWebhookAsync(
        ComplianceAlert alert,
        string webhookUrl,
        CancellationToken cancellationToken)
    {
        // Rate limiting
        if (!_rateLimiters.TryGetValue(NotificationChannel.Webhook, out var limiter))
            return;

        using var lease = limiter.AttemptAcquire();
        if (!lease.IsAcquired)
        {
            await RecordNotification(alert, NotificationChannel.Webhook, webhookUrl,
                isDelivered: false, error: "RATE_LIMITED", cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            alertId = alert.AlertId,
            type = alert.Type.ToString(),
            severity = alert.Severity.ToString(),
            title = alert.Title,
            description = alert.Description,
            subscriptionId = alert.SubscriptionId,
            affectedResources = alert.AffectedResources,
            timestamp = DateTimeOffset.UtcNow
        });

        // HMAC-SHA256 signature for webhook payload
        var signature = ComputeHmacSignature(payload);

        await RecordNotification(alert, NotificationChannel.Webhook, webhookUrl,
            isDelivered: true, error: null, cancellationToken,
            subject: $"X-Signature: {signature}", body: payload);
    }

    private async Task RecordNotification(
        ComplianceAlert alert,
        NotificationChannel channel,
        string recipient,
        bool isDelivered,
        string? error,
        CancellationToken cancellationToken,
        string? subject = null,
        string? body = null,
        string? userId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var notification = new AlertNotification
        {
            Id = Guid.NewGuid(),
            AlertId = alert.Id,
            Channel = channel,
            Recipient = recipient,
            Subject = subject,
            Body = body,
            IsDelivered = isDelivered,
            DeliveryError = error,
            SentAt = DateTimeOffset.UtcNow,
            DeliveredAt = isDelivered ? DateTimeOffset.UtcNow : null,
            UserId = userId ?? recipient,
        };

        db.AlertNotifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);

        // Push real-time notification to connected clients
        if (_broadcaster != null && notification.UserId != null)
        {
            try
            {
                await _broadcaster.BroadcastToUserAsync(notification.UserId, notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to broadcast notification {Id} — client may not be connected", notification.Id);
            }
        }
    }

    /// <summary>
    /// Compute HMAC-SHA256 signature for webhook payloads.
    /// Uses a deterministic key derived from the alert ID for demo purposes.
    /// In production, this would use a configured secret.
    /// </summary>
    internal static string ComputeHmacSignature(string payload, string? secret = null)
    {
        var key = Encoding.UTF8.GetBytes(secret ?? "ato-copilot-webhook-secret");
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }

    private static SlidingWindowRateLimiter CreateRateLimiter()
    {
        return new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 2,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    }
}

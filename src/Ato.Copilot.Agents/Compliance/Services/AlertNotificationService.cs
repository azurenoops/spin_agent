using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
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
    private readonly NotificationOptions _notificationOptions;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<AlertNotificationService> _logger;

    // Rate limiter: max 10 notifications per minute per channel
    private readonly Dictionary<NotificationChannel, SlidingWindowRateLimiter> _rateLimiters;

    public AlertNotificationService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IComplianceWatchService watchService,
        ILogger<AlertNotificationService> logger,
        INotificationBroadcaster? broadcaster = null,
        IOptions<NotificationOptions>? notificationOptions = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        _dbFactory = dbFactory;
        _watchService = watchService;
        _logger = logger;
        _broadcaster = broadcaster;
        _notificationOptions = notificationOptions?.Value ?? new NotificationOptions();
        _httpClientFactory = httpClientFactory;

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

        // Chat notification — delivered through the in-app broadcaster when configured
        await DispatchNotificationAsync(alert, NotificationChannel.Chat, "system", cancellationToken);

        // Email — immediate for Critical/High, deferred digest for Medium/Low
        if (alert.Severity is AlertSeverity.Critical or AlertSeverity.High)
        {
            var recipients = _notificationOptions.Email.Recipients
                .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (recipients.Count == 0)
            {
                recipients.Add("system");
            }

            foreach (var recipient in recipients)
            {
                await DispatchNotificationAsync(alert, NotificationChannel.Email, recipient, cancellationToken);
            }
        }

        // Webhook — check for configured escalation paths with webhook URLs
        var webhookPaths = await db.EscalationPaths
            .Where(p => p.IsEnabled
                && p.Channel == NotificationChannel.Webhook
                && p.WebhookUrl != null
                && p.TriggerSeverity <= alert.Severity)
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

        var recipients = _notificationOptions.Email.Recipients
            .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (recipients.Count == 0)
        {
            recipients.Add("digest");
        }

        foreach (var recipient in recipients)
        {
            var outcome = await DeliverEmailAsync(recipient, subject, body, cancellationToken);
            db.AlertNotifications.Add(new AlertNotification
            {
                Id = Guid.NewGuid(),
                AlertId = alerts.Count > 0 ? alerts.First().Id : Guid.Empty,
                Channel = NotificationChannel.Email,
                Recipient = recipient,
                Subject = subject,
                Body = body,
                IsDelivered = outcome.IsDelivered,
                DeliveryError = outcome.Error,
                SentAt = DateTimeOffset.UtcNow,
                DeliveredAt = outcome.IsDelivered ? DateTimeOffset.UtcNow : null
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Processed digest with {AlertCount} alerts, {PendingDeviations} pending deviations for {Sub}",
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

        if (channel == NotificationChannel.Chat && _broadcaster != null)
        {
            await RecordNotification(alert, channel, recipient,
                isDelivered: false, error: null, cancellationToken, subject, body,
                dispatchToBroadcaster: true);
            return;
        }

        if (channel == NotificationChannel.Email)
        {
            var outcome = await DeliverEmailAsync(recipient, subject, body, cancellationToken);
            await RecordNotification(alert, channel, recipient,
                outcome.IsDelivered, outcome.Error, cancellationToken, subject, body);
            return;
        }

        _logger.LogWarning("Notification channel {Channel} is not configured; delivery was not attempted", channel);
        await RecordNotification(alert, channel, recipient,
            isDelivered: false, error: "CHANNEL_NOT_CONFIGURED", cancellationToken, subject, body);
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

        var webhookOptions = _notificationOptions.Webhook;
        if (!webhookOptions.Enabled
            || string.IsNullOrWhiteSpace(webhookOptions.Secret)
            || _httpClientFactory == null)
        {
            _logger.LogWarning("Webhook delivery is not configured for alert {AlertId}; delivery was not attempted", alert.AlertId);
            await RecordNotification(alert, NotificationChannel.Webhook, webhookUrl,
                isDelivered: false, error: "CHANNEL_NOT_CONFIGURED", cancellationToken,
                body: payload);
            return;
        }

        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var webhookUri)
            || webhookUri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogWarning("Webhook URL for alert {AlertId} is invalid or does not use HTTPS", alert.AlertId);
            await RecordNotification(alert, NotificationChannel.Webhook, webhookUrl,
                isDelivered: false, error: "INVALID_WEBHOOK_URL", cancellationToken,
                body: payload);
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, webhookOptions.TimeoutSeconds)));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, webhookUri)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Signature", ComputeHmacSignature(payload, webhookOptions.Secret));

            var client = _httpClientFactory.CreateClient(nameof(AlertNotificationService));
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
            var isDelivered = response.IsSuccessStatusCode;
            var error = isDelivered ? null : $"HTTP_{(int)response.StatusCode}";

            await RecordNotification(alert, NotificationChannel.Webhook, webhookUrl,
                isDelivered, error, cancellationToken, body: payload);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Webhook delivery timed out for alert {AlertId}", alert.AlertId);
            await RecordNotification(alert, NotificationChannel.Webhook, webhookUrl,
                isDelivered: false, error: "DELIVERY_TIMEOUT", cancellationToken,
                body: payload);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook delivery failed for alert {AlertId}", alert.AlertId);
            await RecordNotification(alert, NotificationChannel.Webhook, webhookUrl,
                isDelivered: false, error: "DELIVERY_FAILED", cancellationToken,
                body: payload);
        }
    }

    private async Task<(bool IsDelivered, string? Error)> DeliverEmailAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var emailOptions = _notificationOptions.Email;
        if (!emailOptions.Enabled
            || string.IsNullOrWhiteSpace(emailOptions.SmtpHost)
            || recipient is "system" or "digest")
        {
            _logger.LogWarning("Email delivery is not configured; delivery was not attempted");
            return (false, "CHANNEL_NOT_CONFIGURED");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, emailOptions.TimeoutSeconds)));

        try
        {
            using var client = new SmtpClient(emailOptions.SmtpHost, emailOptions.SmtpPort)
            {
                EnableSsl = emailOptions.UseSsl
            };
            if (!string.IsNullOrWhiteSpace(emailOptions.Username))
            {
                client.Credentials = new NetworkCredential(emailOptions.Username, emailOptions.Password);
            }

            using var message = new MailMessage(emailOptions.FromAddress, recipient)
            {
                Subject = subject,
                Body = body
            };
            await client.SendMailAsync(message, timeoutCts.Token);
            return (true, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Email delivery timed out");
            return (false, "DELIVERY_TIMEOUT");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email delivery failed");
            return (false, "DELIVERY_FAILED");
        }
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
        string? userId = null,
        bool dispatchToBroadcaster = false)
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

        if (dispatchToBroadcaster)
        {
            try
            {
                notification.IsDelivered = true;
                notification.DeliveryError = null;
                notification.DeliveredAt = DateTimeOffset.UtcNow;
                await _broadcaster!.BroadcastToUserAsync(
                    notification.UserId!, notification, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                notification.IsDelivered = false;
                notification.DeliveryError = "DELIVERY_FAILED";
                notification.DeliveredAt = null;
                _logger.LogWarning(ex, "Chat delivery failed for notification {Id}", notification.Id);
            }
        }

        db.AlertNotifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Compute an HMAC-SHA256 signature for a webhook payload using an explicit secret.
    /// </summary>
    internal static string ComputeHmacSignature(string payload, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var key = Encoding.UTF8.GetBytes(secret);
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

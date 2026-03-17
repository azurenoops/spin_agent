using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Background service that sends daily digest emails for Medium/Low severity alerts.
/// Runs once per hour and sends the digest at the configured hour (default: 08:00 UTC).
/// </summary>
public class DigestSchedulerHostedService : BackgroundService
{
    private readonly IAlertNotificationService _notificationService;
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly ILogger<DigestSchedulerHostedService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private const int DigestHourUtc = 8; // Send digest at 08:00 UTC

    private DateOnly _lastDigestDate = DateOnly.MinValue;

    public DigestSchedulerHostedService(
        IAlertNotificationService notificationService,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILogger<DigestSchedulerHostedService> logger)
    {
        _notificationService = notificationService;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DigestSchedulerHostedService started — digest at {Hour}:00 UTC", DigestHourUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var today = DateOnly.FromDateTime(now.UtcDateTime);

                if (now.Hour >= DigestHourUtc && _lastDigestDate < today)
                {
                    await SendDigestsForAllSubscriptionsAsync(stoppingToken);
                    _lastDigestDate = today;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during digest scheduling");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task SendDigestsForAllSubscriptionsAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var subscriptionIds = await db.MonitoringConfigurations
            .Where(m => m.IsEnabled)
            .Select(m => m.SubscriptionId)
            .Distinct()
            .ToListAsync(ct);

        _logger.LogInformation("Sending daily digest for {Count} subscription(s)", subscriptionIds.Count);

        foreach (var subId in subscriptionIds)
        {
            try
            {
                await _notificationService.SendDigestAsync(subId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send digest for subscription {SubscriptionId}", subId);
            }
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Background service that runs on a 1-hour polling interval with a daily
/// 06:00 UTC execution guard. On each qualifying run it:
/// 1. Queries all active <see cref="Ato.Copilot.Core.Models.Compliance.AuthorizationDecision"/>
///    records that carry an expiration date.
/// 2. Calls <see cref="IConMonService.CheckExpirationAsync"/> for the owning
///    system so ConMonService can apply graduated alerting (Info@90d,
///    Warning@60d, Urgent@30d, Expired) and auto-deactivate past-due ATOs.
///
/// Epic #210 — ConMon BackgroundService (Issue #292).
/// </summary>
public class AtoExpiryMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AtoExpiryMonitorService> _logger;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private const int RunHourUtc = 6; // Execute daily at 06:00 UTC

    private DateOnly _lastRunDate = DateOnly.MinValue;

    public AtoExpiryMonitorService(
        IServiceScopeFactory scopeFactory,
        ILogger<AtoExpiryMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AtoExpiryMonitorService started — runs daily at {Hour}:00 UTC on a {Interval}-hour polling interval",
            RunHourUtc, (int)CheckInterval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var today = DateOnly.FromDateTime(now.UtcDateTime);

                if (now.Hour >= RunHourUtc && _lastRunDate < today)
                {
                    await ScanExpiryAsync(stoppingToken);
                    _lastRunDate = today;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error during ATO expiry scan — will retry on next interval");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core scan logic — resolves scoped services per invocation so the
    // background service (singleton-lifetime) never holds a live DbContext.
    // ─────────────────────────────────────────────────────────────────────────

    private async Task ScanExpiryAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var conMonService = scope.ServiceProvider.GetRequiredService<IConMonService>();

        // Collect all active authorization decisions that have an expiration date.
        // We project to just the RegisteredSystemId so we don't pull the whole
        // entity into memory, then deduplicate in case a system has multiple
        // active decisions (unusual but possible during reauthorization transitions).
        var systemIds = await db.AuthorizationDecisions
            .Where(a => a.IsActive && a.ExpirationDate != null)
            .Select(a => a.RegisteredSystemId)
            .Distinct()
            .ToListAsync(ct);

        if (systemIds.Count == 0)
        {
            _logger.LogDebug("AtoExpiryMonitorService — no active authorizations with expiration dates found; skipping scan");
            return;
        }

        _logger.LogInformation(
            "AtoExpiryMonitorService — scanning {Count} system(s) for ATO expiry",
            systemIds.Count);

        int warnings = 0, expired = 0, errors = 0;

        foreach (var systemId in systemIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var status = await conMonService.CheckExpirationAsync(systemId, ct);

                if (status.IsExpired)
                    expired++;
                else if (status.AlertLevel is "Urgent" or "Warning" or "Info")
                    warnings++;

                _logger.LogDebug(
                    "AtoExpiryMonitorService — system '{SystemId}' alert={AlertLevel} daysLeft={Days}",
                    systemId, status.AlertLevel, status.DaysUntilExpiration);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors++;
                _logger.LogWarning(ex,
                    "AtoExpiryMonitorService — CheckExpirationAsync failed for system '{SystemId}'",
                    systemId);
            }
        }

        _logger.LogInformation(
            "AtoExpiryMonitorService — scan complete: {Expired} expired, {Warnings} warning(s), {Errors} error(s) across {Total} system(s)",
            expired, warnings, errors, systemIds.Count);
    }
}

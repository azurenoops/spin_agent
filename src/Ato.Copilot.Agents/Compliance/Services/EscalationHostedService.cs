using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Background service that periodically checks for alerts exceeding their SLA and
/// triggers escalation notifications via configured escalation paths.
/// Also implements IEscalationService for managing escalation path configuration.
/// </summary>
public class EscalationHostedService : BackgroundService, IEscalationService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IAlertNotificationService _notificationService;
    private readonly IAlertManager _alertManager;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ILogger<EscalationHostedService> _logger;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    public EscalationHostedService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IAlertNotificationService notificationService,
        IAlertManager alertManager,
        ITenantContextAccessor tenantAccessor,
        ILogger<EscalationHostedService> logger)
    {
        _dbFactory = dbFactory;
        _notificationService = notificationService;
        _alertManager = alertManager;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EscalationHostedService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckEscalationsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during escalation check");
            }

            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    /// <inheritdoc />
    public async Task CheckEscalationsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var activeAlerts = await db.ComplianceAlerts
            .IgnoreQueryFilters()
            .Where(a => a.Status == AlertStatus.New || a.Status == AlertStatus.Acknowledged)
            .ToListAsync(cancellationToken);
        var overdueAlerts = activeAlerts.Where(a => a.SlaDeadline <= now).ToList();

        if (overdueAlerts.Count == 0) return;

        var paths = await db.EscalationPaths
            .IgnoreQueryFilters()
            .Where(p => p.IsEnabled)
            .ToListAsync(cancellationToken);

        if (paths.Count == 0) return;

        foreach (var alert in overdueAlerts)
        {
            using var tenantScope = _tenantAccessor.Push(new TenantContext(alert.TenantId));
            var matchingPaths = paths
                .Where(p => p.TenantId == alert.TenantId && p.TriggerSeverity == alert.Severity)
                .ToList();

            if (matchingPaths.Count == 0) continue;

            // Check how many escalation notifications already sent for this alert
            var existingEscalations = await db.AlertNotifications
                .IgnoreQueryFilters()
                .CountAsync(n => n.TenantId == alert.TenantId && n.AlertId == alert.Id
                    && n.Subject != null && n.Subject.Contains("[ESCALATION]"), cancellationToken);

            foreach (var path in matchingPaths)
            {
                // Check MaxEscalations cap
                if (existingEscalations >= path.MaxEscalations)
                    continue;

                // Check RepeatIntervalMinutes
                var lastEscalation = await db.AlertNotifications
                    .IgnoreQueryFilters()
                    .Where(n => n.TenantId == alert.TenantId && n.AlertId == alert.Id
                        && n.Subject != null && n.Subject.Contains("[ESCALATION]"))
                    .OrderByDescending(n => n.SentAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (lastEscalation != null
                    && lastEscalation.SentAt.AddMinutes(path.RepeatIntervalMinutes) > now)
                    continue;

                // Check escalation delay
                if (alert.SlaDeadline.AddMinutes(path.EscalationDelayMinutes) > now)
                    continue;

                // Transition alert to Escalated if not already
                if (alert.Status != AlertStatus.Escalated)
                {
                    try
                    {
                        await _alertManager.TransitionAlertAsync(
                            alert.Id, AlertStatus.Escalated, "system", "EscalationService", cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to transition alert {AlertId} to Escalated", alert.AlertId);
                    }
                }

                // Send escalation notification to all recipients
                await _notificationService.SendNotificationAsync(alert, cancellationToken);

                _logger.LogWarning("Escalated alert {AlertId} — SLA exceeded by {Minutes}m",
                    alert.AlertId, (int)(now - alert.SlaDeadline).TotalMinutes);
            }
        }
    }

    /// <inheritdoc />
    public async Task<EscalationPath> ConfigureEscalationPathAsync(
        EscalationPath path, CancellationToken cancellationToken = default)
    {
        if (path.Recipients.Count == 0)
            throw new InvalidOperationException("VALIDATION_ERROR: Recipients must have at least one entry.");
        if (path.EscalationDelayMinutes <= 0)
            throw new InvalidOperationException("VALIDATION_ERROR: EscalationDelayMinutes must be > 0.");
        if (path.RepeatIntervalMinutes < 5)
            throw new InvalidOperationException("VALIDATION_ERROR: RepeatIntervalMinutes must be >= 5.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        if (path.Id == Guid.Empty)
        {
            path.Id = Guid.NewGuid();
            path.CreatedAt = DateTimeOffset.UtcNow;
            path.UpdatedAt = DateTimeOffset.UtcNow;
            db.EscalationPaths.Add(path);
        }
        else
        {
            var existing = await db.EscalationPaths.FindAsync(new object[] { path.Id }, cancellationToken)
                ?? throw new KeyNotFoundException($"ESCALATION_NOT_FOUND: Path '{path.Id}' not found.");

            existing.Name = path.Name;
            existing.TriggerSeverity = path.TriggerSeverity;
            existing.EscalationDelayMinutes = path.EscalationDelayMinutes;
            existing.Recipients = path.Recipients;
            existing.Channel = path.Channel;
            existing.RepeatIntervalMinutes = path.RepeatIntervalMinutes;
            existing.MaxEscalations = path.MaxEscalations;
            existing.WebhookUrl = path.WebhookUrl;
            existing.IsEnabled = path.IsEnabled;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            path = existing;
        }

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Escalation path '{Name}' configured for {Severity}",
            path.Name, path.TriggerSeverity);
        return path;
    }

    /// <inheritdoc />
    public async Task<List<EscalationPath>> GetEscalationPathsAsync(
        AlertSeverity? severity = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.EscalationPaths.AsQueryable();
        if (severity.HasValue)
            query = query.Where(p => p.TriggerSeverity == severity.Value);

        return await query.OrderBy(p => p.TriggerSeverity).ThenBy(p => p.Name).ToListAsync(cancellationToken);
    }
}

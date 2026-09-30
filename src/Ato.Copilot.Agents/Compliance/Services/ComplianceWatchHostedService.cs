using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Background service that periodically runs compliance monitoring checks.
/// Queries MonitoringConfigurations for due checks, runs drift detection,
/// and advances NextRunAt. Uses PeriodicTimer with configurable tick interval.
/// </summary>
public class ComplianceWatchHostedService : BackgroundService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IComplianceWatchService _watchService;
    private readonly IAlertManager _alertManager;
    private readonly IComplianceEventSource _eventSource;
    private readonly IOptions<MonitoringOptions> _monitoringOptions;
    private readonly ILogger<ComplianceWatchHostedService> _logger;
    private int _consecutiveFailures;
    private const int MaxConsecutiveFailuresBeforeMetaAlert = 3;
    private DateOnly _lastSnapshotDate;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ITenantContextAccessor? _tenantAccessor;
    private readonly CanonicalEnvironmentCollectionGuard? _environmentGuard;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplianceWatchHostedService"/> class.
    /// </summary>
    public ComplianceWatchHostedService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IComplianceWatchService watchService,
        IAlertManager alertManager,
        IComplianceEventSource eventSource,
        IOptions<MonitoringOptions> monitoringOptions,
        ILogger<ComplianceWatchHostedService> logger,
        IServiceScopeFactory? scopeFactory = null,
        ITenantContextAccessor? tenantAccessor = null,
        CanonicalEnvironmentCollectionGuard? environmentGuard = null)
    {
        _dbFactory = dbFactory;
        _watchService = watchService;
        _alertManager = alertManager;
        _eventSource = eventSource;
        _monitoringOptions = monitoringOptions;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _tenantAccessor = tenantAccessor;
        _environmentGuard = environmentGuard;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tickInterval = TimeSpan.FromSeconds(_monitoringOptions.Value.TickIntervalSeconds);
        _logger.LogInformation("ComplianceWatchHostedService started with tick interval {Interval}", tickInterval);

        using var timer = new PeriodicTimer(tickInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunTenantChecksAsync(stoppingToken);
                _consecutiveFailures = 0;
            }

            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                var backoffMs = Math.Min(1000 * Math.Pow(2, _consecutiveFailures), 300_000); // Max 5 min

                _logger.LogError(ex,
                    "ComplianceWatchHostedService tick failed (consecutive: {Failures}). Backing off {Backoff}ms",
                    _consecutiveFailures, backoffMs);

                // Create meta-alert if repeated failures (FR-042)
                if (_consecutiveFailures >= MaxConsecutiveFailuresBeforeMetaAlert)
                {
                    await CreateMetaAlertAsync(ex, stoppingToken);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(backoffMs), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("ComplianceWatchHostedService stopped");
    }

    internal async Task RunTenantChecksAsync(CancellationToken ct)
    {
        if (_scopeFactory == null || _tenantAccessor == null)
            throw new InvalidOperationException("Monitoring requires an explicit tenant scope factory and accessor.");
        await using var routing = await _dbFactory.CreateDbContextAsync(ct);
        // Internal routing-only query, never used by an HTTP request.
        var tenants = await routing.MonitoringConfigurations.IgnoreQueryFilters().Where(x => x.IsEnabled)
            .Select(x => x.TenantId).Union(routing.AlertRules.IgnoreQueryFilters()
                .Where(x => x.IsEnabled && x.RegisteredSystemId != null).Select(x => x.TenantId))
            .Union(routing.ComplianceAlerts.IgnoreQueryFilters().Select(x => x.TenantId))
            .Distinct().ToListAsync(ct);
        var failures = new List<Exception>();
        foreach (var tenantId in tenants.Where(x => x != Guid.Empty))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>() as TenantContext
                ?? throw new InvalidOperationException("Monitoring requires the production TenantContext.");
            tenant.TenantId = tenantId;
            using var pushed = _tenantAccessor.Push(tenant);
            try
            {
                await RunScheduledChecksAsync(ct);
                await RunEventDrivenChecksAsync(ct);
                await scope.ServiceProvider.GetRequiredService<ScopedMonitoringService>().EvaluateDueAsync(ct);
                _lastSnapshotDate = default;
                await CaptureSnapshotsIfDueAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(ex);
                _logger.LogError(ex, "Monitoring failed for tenant {TenantId}; other tenant checks continue", tenantId);
            }
        }
        await using (var providerScope = _scopeFactory.CreateAsyncScope())
        {
            var providerMonitoring = providerScope.ServiceProvider.GetService<Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderMonitoringService>();
            if (providerMonitoring is not null)
            {
                // Provider maintenance is a separate internal context, never a mission actor.
                var providerContext = new TenantContext(Guid.Empty, isCspAdmin: true);
                using var providerPush = _tenantAccessor.Push(providerContext);
                var providerStore = ActivatorUtilities.CreateInstance<Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore>(
                    providerScope.ServiceProvider, providerContext);
                try { await new Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderMonitoringService(providerStore).RunDueAsync(ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(ex);
                    _logger.LogError(ex, "Provider monitoring failed; tenant checks have already completed");
                }
            }
        }
        if (failures.Count > 0) throw new AggregateException("Monitoring checks failed.", failures);
    }

    /// <summary>
    /// Query all due monitoring configurations and run checks.
    /// </summary>
    private async Task RunScheduledChecksAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var enabledConfigs = await db.MonitoringConfigurations
            .Where(c => c.IsEnabled).ToListAsync(cancellationToken);
        var dueConfigs = enabledConfigs.Where(c => c.NextRunAt <= now).ToList();

        if (dueConfigs.Count == 0)
            return;

        _logger.LogInformation("Found {Count} due monitoring configurations", dueConfigs.Count);

        foreach (var config in dueConfigs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            config.LastAttemptAt = DateTimeOffset.UtcNow;
            try
            {
                await EnsureCurrentEnvironmentAsync(config.SubscriptionId, cancellationToken);
                if (!await db.ComplianceBaselines.AnyAsync(x => x.SubscriptionId == config.SubscriptionId && x.IsActive, cancellationToken))
                    throw new InvalidOperationException("Missing reviewed compliance baseline.");
                var alertCount = await _watchService.RunMonitoringCheckAsync(config, cancellationToken);

                // Advance NextRunAt
                config.NextRunAt = ComplianceWatchService.ComputeNextRunAt(config.Frequency);
                config.LastRunAt = DateTimeOffset.UtcNow;
                config.CollectionError = null;
                config.UpdatedAt = DateTimeOffset.UtcNow;

                sw.Stop();
                _logger.LogInformation(
                    "Monitoring check completed for {Sub}/{RG} in {Elapsed}ms: {AlertCount} alerts",
                    config.SubscriptionId, config.ResourceGroupName ?? "*",
                    sw.ElapsedMilliseconds, alertCount);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "Monitoring check failed for {Sub}/{RG} after {Elapsed}ms",
                    config.SubscriptionId, config.ResourceGroupName ?? "*",
                    sw.ElapsedMilliseconds);

                // Still advance NextRunAt to prevent stuck loops
                config.NextRunAt = ComplianceWatchService.ComputeNextRunAt(config.Frequency);
                config.LastFailureAt = DateTimeOffset.UtcNow;
                config.CollectionError = ex is AssessmentEnvironmentException admission ? admission.ErrorCode
                    : ex is InvalidOperationException ? "MissingBaselineOrInvalidCollection" : "CollectionFailed";
                config.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Poll Activity Log for scopes with Mode=EventDriven or Both,
    /// trigger targeted compliance checks on affected resources.
    /// </summary>
    internal async Task RunEventDrivenChecksAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var eventDrivenConfigs = await db.MonitoringConfigurations
            .Where(c => c.IsEnabled
                && (c.Mode == MonitoringMode.EventDriven || c.Mode == MonitoringMode.Both))
            .ToListAsync(cancellationToken);

        if (eventDrivenConfigs.Count == 0)
            return;

        foreach (var config in eventDrivenConfigs)
        {
            var since = config.LastEventCheckAt ?? config.CreatedAt;

            try
            {
                await EnsureCurrentEnvironmentAsync(config.SubscriptionId, cancellationToken);
                var events = await _eventSource.GetRecentEventsAsync(
                    config.SubscriptionId, since, config.ResourceGroupName, cancellationToken);

                if (events.Count == 0)
                    continue;

                _logger.LogInformation(
                    "Event-driven: {Count} events for {Sub}/{RG} since {Since}",
                    events.Count, config.SubscriptionId, config.ResourceGroupName ?? "*", since);

                // Check for policy drift events (FR-004)
                var policyDriftEvents = events
                    .Where(e => ActivityLogEventSource.IsPolicyDriftEvent(e))
                    .ToList();

                if (policyDriftEvents.Count > 0)
                {
                    _logger.LogWarning(
                        "Policy drift detected: {Count} policy change events for {Sub}",
                        policyDriftEvents.Count, config.SubscriptionId);
                }

                // Trigger a targeted compliance check for the affected scope
                await _watchService.RunMonitoringCheckAsync(config, cancellationToken);

                // Advance high-water mark to latest event timestamp
                var maxTimestamp = events.Max(e => e.Timestamp);
                config.LastEventCheckAt = maxTimestamp;
                config.UpdatedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Event-driven check failed for {Sub}/{RG} — falling back to scheduled",
                    config.SubscriptionId, config.ResourceGroupName ?? "*");
                config.LastFailureAt = DateTimeOffset.UtcNow;
                config.CollectionError = ex is AssessmentEnvironmentException admission ? admission.ErrorCode : "EventCollectionFailed";

                // Fallback: if Activity Log is unavailable, scheduled checks still run
                // Do not advance LastEventCheckAt so events are re-polled next tick
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Capture daily compliance snapshots at midnight UTC.
    /// On Sundays, also promote the snapshot to a weekly snapshot.
    /// </summary>
    internal async Task CaptureSnapshotsIfDueAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        if (today <= _lastSnapshotDate)
            return; // Already captured for today

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var configs = await db.MonitoringConfigurations
            .Where(c => c.IsEnabled)
            .ToListAsync(cancellationToken);

        if (configs.Count == 0)
            return;

        var isSunday = DateTimeOffset.UtcNow.DayOfWeek == DayOfWeek.Sunday;

        foreach (var config in configs)
        {
            try
            {
                await EnsureCurrentEnvironmentAsync(config.SubscriptionId, cancellationToken);
            }
            catch (AssessmentEnvironmentException failure)
            {
                config.CollectionError = failure.ErrorCode;
                config.LastFailureAt = DateTimeOffset.UtcNow;
                _logger.LogWarning("Monitoring snapshot blocked for {SubscriptionId}: {Reason}",
                    config.SubscriptionId, failure.ErrorCode);
                continue;
            }
            // Check if snapshot already exists for today
            var capturedDates = await db.ComplianceSnapshots
                .Where(s => s.SubscriptionId == config.SubscriptionId)
                .Select(s => s.CapturedAt).ToListAsync(cancellationToken);
            var existing = capturedDates.Any(date => date.UtcDateTime.Date == DateTime.UtcNow.Date);

            if (existing)
                continue;

            // Get latest assessment data
            var latestBaselines = await db.ComplianceBaselines
                .Where(b => b.SubscriptionId == config.SubscriptionId && b.IsActive)
                .ToListAsync(cancellationToken);

            // Count active alerts
            var alertCounts = await db.ComplianceAlerts
                .Where(a => a.SubscriptionId == config.SubscriptionId
                    && a.Status != AlertStatus.Resolved
                    && a.Status != AlertStatus.Dismissed)
                .GroupBy(a => a.Severity)
                .Select(g => new { Severity = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var totalActive = alertCounts.Sum(a => a.Count);
            var criticalCount = alertCounts
                .Where(a => a.Severity == AlertSeverity.Critical).Sum(a => a.Count);
            var highCount = alertCounts
                .Where(a => a.Severity == AlertSeverity.High).Sum(a => a.Count);

            // Build control family breakdown from baselines
            var familyBreakdown = latestBaselines
                .Where(b => !string.IsNullOrEmpty(b.ResourceType))
                .GroupBy(b => b.ResourceType.Split('/').FirstOrDefault() ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());

            var totalResources = latestBaselines.Count;
            var snapshot = new ComplianceSnapshot
            {
                Id = Guid.NewGuid(),
                SubscriptionId = config.SubscriptionId,
                ComplianceScore = totalResources > 0 ? (double)totalResources / Math.Max(totalResources, 1) * 100 : 0,
                TotalControls = totalResources,
                PassedControls = totalResources,
                FailedControls = 0,
                TotalResources = totalResources,
                CompliantResources = totalResources,
                NonCompliantResources = 0,
                ActiveAlertCount = totalActive,
                CriticalAlertCount = criticalCount,
                HighAlertCount = highCount,
                ControlFamilyBreakdown = familyBreakdown.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(familyBreakdown)
                    : null,
                CapturedAt = DateTimeOffset.UtcNow,
                IsWeeklySnapshot = isSunday
            };

            db.ComplianceSnapshots.Add(snapshot);
            _logger.LogInformation(
                "Snapshot captured for {Sub} | Score: {Score:F1} | Alerts: {Alerts} | Weekly: {Weekly}",
                config.SubscriptionId, snapshot.ComplianceScore, totalActive, isSunday);
        }

        await db.SaveChangesAsync(cancellationToken);
        _lastSnapshotDate = today;
    }

    /// <summary>
    /// Create a meta-alert when the monitoring system itself is experiencing persistent failures (FR-042).
    /// </summary>
    private async Task CreateMetaAlertAsync(Exception ex, CancellationToken cancellationToken)
    {
        try
        {
            var alert = new ComplianceAlert
            {
                Type = AlertType.Degradation,
                Severity = AlertSeverity.Critical,
                Title = "Compliance monitoring system experiencing persistent failures",
                Description = $"The compliance monitoring background service has failed " +
                    $"{_consecutiveFailures} consecutive times. Last error: {ex.Message}. " +
                    $"Monitoring may be degraded until the issue is resolved.",
                SubscriptionId = "system",
                RecommendedAction = "Check system logs and connectivity to Azure. " +
                    "Verify network access and service principal permissions."
            };

            await _alertManager.CreateAlertAsync(alert, cancellationToken);
            _logger.LogWarning("Meta-alert created for persistent monitoring failures");
        }

        catch (Exception metaEx)
        {
            _logger.LogError(metaEx, "Failed to create meta-alert for monitoring failures");
        }
    }

    private Task EnsureCurrentEnvironmentAsync(string subscriptionId, CancellationToken cancellationToken) =>
        _environmentGuard?.EnsureSubscriptionAsync(subscriptionId, EnvironmentScopePurpose.Monitoring,
            cancellationToken, requireAttachment: true) ?? Task.CompletedTask;
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Compliance Watch monitoring service — manages monitoring configurations, baselines,
/// drift detection, and scheduled compliance checks.
/// Singleton service using IDbContextFactory for DB access.
/// </summary>
public class ComplianceWatchService : IComplianceWatchService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IAlertManager _alertManager;
    private readonly IAtoComplianceEngine _complianceEngine;
    private readonly IRemediationEngine _remediationEngine;
    private readonly ISystemSubscriptionResolver? _subscriptionResolver;
    private readonly IServiceScopeFactory? _serviceScopeFactory;
    private readonly IOptions<MonitoringOptions> _monitoringOptions;
    private readonly IOptions<AlertOptions> _alertOptions;
    private readonly ILogger<ComplianceWatchService> _logger;
    private readonly CanonicalEnvironmentCollectionGuard? _environmentGuard;

    /// <summary>Blocked control families that always require human approval.</summary>
    private static readonly HashSet<string> BlockedFamilies = new(StringComparer.OrdinalIgnoreCase) { "AC", "IA", "SC" };

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplianceWatchService"/> class.
    /// </summary>
    public ComplianceWatchService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IAlertManager alertManager,
        IAtoComplianceEngine complianceEngine,
        IRemediationEngine remediationEngine,
        IOptions<MonitoringOptions> monitoringOptions,
        IOptions<AlertOptions> alertOptions,
        ILogger<ComplianceWatchService> logger,
        ISystemSubscriptionResolver? subscriptionResolver = null,
        IServiceScopeFactory? serviceScopeFactory = null,
        CanonicalEnvironmentCollectionGuard? environmentGuard = null)
    {
        _dbFactory = dbFactory;
        _alertManager = alertManager;
        _complianceEngine = complianceEngine;
        _remediationEngine = remediationEngine;
        _monitoringOptions = monitoringOptions;
        _alertOptions = alertOptions;
        _logger = logger;
        _subscriptionResolver = subscriptionResolver;
        _serviceScopeFactory = serviceScopeFactory;
        _environmentGuard = environmentGuard;
    }

    /// <inheritdoc />
    public async Task<MonitoringConfiguration> EnableMonitoringAsync(
        string subscriptionId,
        string? resourceGroupName = null,
        MonitoringFrequency frequency = MonitoringFrequency.Hourly,
        MonitoringMode mode = MonitoringMode.Scheduled,
        string createdBy = "system",
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentEnvironmentAsync(subscriptionId, cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.MonitoringConfigurations
            .FirstOrDefaultAsync(c => c.SubscriptionId == subscriptionId
                && c.ResourceGroupName == resourceGroupName, cancellationToken);

        if (existing != null)
        {
            if (existing.IsEnabled)
            {
                throw new InvalidOperationException(
                    "MONITORING_ALREADY_ENABLED: Monitoring is already enabled for this scope. " +
                    "Use watch_configure_monitoring to update settings.");
            }

            // Re-enable existing disabled configuration
            existing.IsEnabled = true;
            existing.Frequency = frequency;
            existing.Mode = mode;
            existing.NextRunAt = ComputeNextRunAt(frequency);
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Monitoring re-enabled for {Sub}/{RG}", subscriptionId, resourceGroupName ?? "*");
            return existing;
        }

        var config = new MonitoringConfiguration
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            ResourceGroupName = resourceGroupName,
            Mode = mode,
            Frequency = frequency,
            IsEnabled = true,
            NextRunAt = ComputeNextRunAt(frequency),
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.MonitoringConfigurations.Add(config);
        await db.SaveChangesAsync(cancellationToken);

        // Seed default alert rules for new monitoring configurations
        await SeedDefaultRulesAsync(subscriptionId, createdBy, cancellationToken);

        _logger.LogInformation("Monitoring enabled for {Sub}/{RG} with frequency {Freq} and mode {Mode}",
            subscriptionId, resourceGroupName ?? "*", frequency, mode);

        return config;
    }

    /// <inheritdoc />
    public async Task<MonitoringConfiguration> DisableMonitoringAsync(
        string subscriptionId,
        string? resourceGroupName = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var config = await db.MonitoringConfigurations
            .FirstOrDefaultAsync(c => c.SubscriptionId == subscriptionId
                && c.ResourceGroupName == resourceGroupName, cancellationToken)
            ?? throw new InvalidOperationException(
                "MONITORING_NOT_CONFIGURED: No monitoring configuration found for this scope.");

        config.IsEnabled = false;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Monitoring disabled for {Sub}/{RG}", subscriptionId, resourceGroupName ?? "*");
        return config;
    }

    /// <inheritdoc />
    public async Task<MonitoringConfiguration> ConfigureMonitoringAsync(
        string subscriptionId,
        string? resourceGroupName = null,
        MonitoringFrequency? frequency = null,
        MonitoringMode? mode = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var config = await db.MonitoringConfigurations
            .FirstOrDefaultAsync(c => c.SubscriptionId == subscriptionId
                && c.ResourceGroupName == resourceGroupName, cancellationToken)
            ?? throw new InvalidOperationException(
                "MONITORING_NOT_CONFIGURED: No monitoring configuration found for this scope.");

        if (frequency.HasValue)
        {
            config.Frequency = frequency.Value;
            config.NextRunAt = ComputeNextRunAt(frequency.Value);
        }

        if (mode.HasValue)
            config.Mode = mode.Value;

        config.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Monitoring updated for {Sub}/{RG}: Freq={Freq}, Mode={Mode}",
            subscriptionId, resourceGroupName ?? "*", config.Frequency, config.Mode);

        return config;
    }

    /// <inheritdoc />
    public async Task<List<MonitoringConfiguration>> GetMonitoringStatusAsync(
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.MonitoringConfigurations.AsQueryable();

        if (!string.IsNullOrEmpty(subscriptionId))
            query = query.Where(c => c.SubscriptionId == subscriptionId);

        return await query.OrderBy(c => c.SubscriptionId)
            .ThenBy(c => c.ResourceGroupName)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<ComplianceBaseline>> CaptureBaselineAsync(
        string subscriptionId,
        string? resourceGroupName = null,
        Guid? assessmentId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentEnvironmentAsync(subscriptionId, cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Run a compliance assessment to get the current state
        var assessment = await _complianceEngine.RunAssessmentAsync(
            subscriptionId,
            controlFamilies: null,
            resourceTypes: null,
            scanType: "full",
            includePassed: true,
            cancellationToken: cancellationToken);

        EnsureCollectionCompleted(assessment);

        var captured = new List<ComplianceBaseline>();
        var now = DateTimeOffset.UtcNow;

        // Create baselines from assessment findings covering each unique resource
        var resourceFindings = assessment.Findings
            .Where(f => !string.IsNullOrEmpty(f.ResourceId))
            .GroupBy(f => f.ResourceId)
            .ToList();

        foreach (var group in resourceFindings)
        {
            var resourceId = group.Key;
            var configSnapshot = JsonSerializer.Serialize(
                group.Select(f => new { f.ControlId, f.Status, f.ResourceType }).ToList());
            var hash = ComputeHash(configSnapshot);

            // Deactivate any existing active baseline for this resource
            var existing = await db.ComplianceBaselines
                .Where(b => b.ResourceId == resourceId && b.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var old in existing)
                old.IsActive = false;

            var baseline = new ComplianceBaseline
            {
                Id = Guid.NewGuid(),
                SubscriptionId = subscriptionId,
                ResourceId = resourceId,
                ResourceType = group.First().ResourceType ?? string.Empty,
                ConfigurationHash = hash,
                ConfigurationSnapshot = configSnapshot,
                PolicyComplianceState = JsonSerializer.Serialize(
                    group.Select(f => new { f.ControlId, f.Status }).ToList()),
                AssessmentId = assessmentId ?? (Guid.TryParse(assessment.Id, out var parsedId) ? parsedId : null),
                CapturedAt = now,
                IsActive = true
            };

            db.ComplianceBaselines.Add(baseline);
            captured.Add(baseline);
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Captured {Count} baselines for {Sub}/{RG}",
            captured.Count, subscriptionId, resourceGroupName ?? "*");

        return captured;
    }

    /// <inheritdoc />
    public async Task<int> RunMonitoringCheckAsync(
        MonitoringConfiguration config,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentEnvironmentAsync(config.SubscriptionId, cancellationToken);
        _logger.LogInformation("Running monitoring check for {Sub}/{RG}",
            config.SubscriptionId, config.ResourceGroupName ?? "*");

        var alerts = await DetectDriftAsync(
            config.SubscriptionId,
            config.ResourceGroupName,
            cancellationToken);

        // Check for secure score degradation
        try
        {
            var scoreAlerts = await DetectScoreDegradationAsync(
                config.SubscriptionId, cancellationToken);
            alerts.AddRange(scoreAlerts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check secure score degradation for {Sub}", config.SubscriptionId);
            throw;
        }

        // Auto-resolve alerts where resources have returned to baseline
        await AutoResolveAlertsAsync(config.SubscriptionId, cancellationToken);

        _logger.LogInformation("Monitoring check completed for {Sub}/{RG}: {AlertCount} new alerts",
            config.SubscriptionId, config.ResourceGroupName ?? "*", alerts.Count);

        return alerts.Count;
    }

    /// <inheritdoc />
    public async Task<List<ComplianceAlert>> DetectDriftAsync(
        string subscriptionId,
        string? resourceGroupName = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurrentEnvironmentAsync(subscriptionId, cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var baselines = await db.ComplianceBaselines
            .Where(b => b.SubscriptionId == subscriptionId && b.IsActive)
            .ToListAsync(cancellationToken);

        if (baselines.Count == 0)
        {
            _logger.LogDebug("No active baselines found for {Sub}/{RG} — skipping drift detection",
                subscriptionId, resourceGroupName ?? "*");
            return new List<ComplianceAlert>();
        }

        // Run a fresh assessment to get current state
        var assessment = await _complianceEngine.RunAssessmentAsync(
            subscriptionId,
            controlFamilies: null,
            resourceTypes: null,
            scanType: "full",
            includePassed: true,
            cancellationToken: cancellationToken);

        EnsureCollectionCompleted(assessment);
        var alerts = new List<ComplianceAlert>();

        // Load alert rules and suppressions for this subscription
        var alertRules = await GetAlertRulesAsync(subscriptionId, cancellationToken);
        var suppressions = await GetSuppressionsAsync(subscriptionId, cancellationToken);

        foreach (var baseline in baselines)
        {
            var currentFindings = assessment.Findings
                .Where(f => f.ResourceId == baseline.ResourceId)
                .ToList();

            if (currentFindings.Count == 0)
                continue;

            var currentSnapshot = JsonSerializer.Serialize(
                currentFindings.Select(f => new { f.ControlId, f.Status, f.ResourceType }).ToList());
            var currentHash = ComputeHash(currentSnapshot);

            // Compare hashes for baseline drift
            if (currentHash != baseline.ConfigurationHash)
            {
                var driftDetails = DetectChanges(baseline.ConfigurationSnapshot, currentSnapshot);

                // Determine severity based on affected control families
                var controlFamilies = currentFindings
                    .Where(f => f.Status != FindingStatus.Remediated)
                    .Select(f => f.ControlId?.Split('-').FirstOrDefault())
                    .Where(cf => cf != null)
                    .Distinct()
                    .ToList();

                var severity = DetermineDriftSeverity(controlFamilies!);

                var alert = new ComplianceAlert
                {
                    Type = AlertType.Drift,
                    Severity = severity,
                    Title = $"Configuration drift detected on {baseline.ResourceId.Split('/').LastOrDefault() ?? baseline.ResourceId}",
                    Description = $"Resource configuration has deviated from the established baseline. " +
                        $"Baseline captured at {baseline.CapturedAt:u}.",
                    SubscriptionId = subscriptionId,
                    AffectedResources = new List<string> { baseline.ResourceId },
                    ControlFamily = controlFamilies.FirstOrDefault(),
                    ControlId = currentFindings.FirstOrDefault(f => f.Status != FindingStatus.Remediated)?.ControlId,
                    ChangeDetails = driftDetails,
                    RecommendedAction = "Review the configuration changes and remediate or re-baseline."
                };

                // Apply alert rule severity override
                var matchedRule = MatchAlertRule(alert, alertRules);
                if (matchedRule?.SeverityOverride != null)
                    alert.Severity = matchedRule.SeverityOverride.Value;

                // Check suppression — skip if suppressed
                if (IsAlertSuppressed(alert, suppressions))
                {
                    _logger.LogDebug("Alert suppressed for {Resource}: matched suppression rule", baseline.ResourceId);
                    continue;
                }

                await EnrichAlertWithSystemAsync(alert, cancellationToken);
                var created = await _alertManager.CreateAlertAsync(alert, cancellationToken);
                alerts.Add(created);

                _logger.LogWarning("Drift detected on {Resource}: hash mismatch",
                    baseline.ResourceId);
            }

            // Check for compliance state drift (e.g., new violations)
            var newViolations = currentFindings
                .Where(f => f.Status == FindingStatus.Open)
                .ToList();

            if (newViolations.Any() && baseline.PolicyComplianceState != null)
            {
                var baselineStates = JsonSerializer.Deserialize<List<JsonElement>>(baseline.PolicyComplianceState);
                var compliantStatuses = new HashSet<int>
                {
                    (int)FindingStatus.Remediated,
                    (int)FindingStatus.Accepted,
                    (int)FindingStatus.FalsePositive
                };
                var baselineControlIds = baselineStates?
                    .Where(e => e.TryGetProperty("Status", out var s) &&
                        ((s.ValueKind == JsonValueKind.Number && compliantStatuses.Contains(s.GetInt32())) ||
                         (s.ValueKind == JsonValueKind.String && s.GetString() is "Remediated" or "Accepted" or "FalsePositive")))
                    .Select(e => e.GetProperty("ControlId").GetString())
                    .ToHashSet() ?? new HashSet<string?>();

                foreach (var violation in newViolations)
                {
                    if (baselineControlIds.Contains(violation.ControlId))
                    {
                        // This control was previously compliant — new violation
                        var vAlert = new ComplianceAlert
                        {
                            Type = AlertType.Violation,
                            Severity = AlertSeverity.High,
                            Title = $"New violation: {violation.ControlId} on {baseline.ResourceId.Split('/').LastOrDefault()}",
                            Description = $"Control {violation.ControlId} was previously compliant but is now in violation.",
                            SubscriptionId = subscriptionId,
                            AffectedResources = new List<string> { baseline.ResourceId },
                            ControlFamily = violation.ControlId?.Split('-').FirstOrDefault(),
                            ControlId = violation.ControlId,
                            RecommendedAction = $"Remediate {violation.ControlId} violation or accept risk with documented justification."
                        };

                        // Apply alert rule severity override
                        var matchedVRule = MatchAlertRule(vAlert, alertRules);
                        if (matchedVRule?.SeverityOverride != null)
                            vAlert.Severity = matchedVRule.SeverityOverride.Value;

                        // Check suppression
                        if (IsAlertSuppressed(vAlert, suppressions))
                        {
                            _logger.LogDebug("Violation alert suppressed for {Control}", violation.ControlId);
                            continue;
                        }

                        await EnrichAlertWithSystemAsync(vAlert, cancellationToken);
                        var created = await _alertManager.CreateAlertAsync(vAlert, cancellationToken);
                        alerts.Add(created);
                    }
                }
            }
        }

        // Phase 17 §9a.5 — If drift count exceeds threshold for a system, create significant change
        if (alerts.Count >= _monitoringOptions.Value.SignificantDriftThreshold
            && _monitoringOptions.Value.AutoCreateSignificantChanges
            && _serviceScopeFactory != null)
        {
            foreach (var attributed in alerts.Where(x => x.RegisteredSystemId != null).GroupBy(x => x.RegisteredSystemId!))
                if (attributed.Count() >= _monitoringOptions.Value.SignificantDriftThreshold)
                    await AutoCreateDriftSignificantChangeAsync(attributed.Key, subscriptionId, attributed.Count(), cancellationToken);
        }

        return alerts;
    }

    // ─── Private Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Detect secure score degradation below the configured threshold.
    /// </summary>
    private async Task<List<ComplianceAlert>> DetectScoreDegradationAsync(
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        await EnsureCurrentEnvironmentAsync(subscriptionId, cancellationToken);
        var alerts = new List<ComplianceAlert>();
        var threshold = _alertOptions.Value.SecureScoreThreshold;

        // Try to get the current secure score
        var scoreAssessment = await _complianceEngine.RunAssessmentAsync(
            subscriptionId,
            scanType: "quick",
            cancellationToken: cancellationToken);
        EnsureCollectionCompleted(scoreAssessment);

        // Use the assessment's control counts
        var totalControls = scoreAssessment.TotalControls;
        var passedControls = scoreAssessment.PassedControls;

        if (totalControls > 0)
        {
            var score = (double)passedControls / totalControls * 100.0;

            if (score < threshold)
            {
                var alert = new ComplianceAlert
                {
                    Type = AlertType.Degradation,
                    Severity = AlertSeverity.High,
                    Title = $"Compliance score degraded to {score:F1}% (threshold: {threshold:F1}%)",
                    Description = $"The compliance score for subscription {subscriptionId} has dropped " +
                        $"below the configured threshold of {threshold:F1}%. " +
                        $"Current: {passedControls}/{totalControls} controls passing.",
                    SubscriptionId = subscriptionId,
                    RecommendedAction = "Review recent changes and address failing controls to restore compliance score."
                };

                await EnrichAlertWithSystemAsync(alert, cancellationToken);
                var created = await _alertManager.CreateAlertAsync(alert, cancellationToken);
                alerts.Add(created);
            }
        }

        return alerts;
    }

    private static void EnsureCollectionCompleted(ComplianceAssessment assessment)
    {
        if (assessment.Status != AssessmentStatus.Completed || assessment.ScanPillarResults.Any(x => !x.Value))
            throw new InvalidOperationException("Monitoring collection did not complete successfully.");
    }

    /// <summary>
    /// Auto-resolve alerts where resources have returned to their baseline state.
    /// </summary>
    private async Task AutoResolveAlertsAsync(
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        await EnsureCurrentEnvironmentAsync(subscriptionId, cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Get active alerts (New, Acknowledged, InProgress) for this subscription
        var activeAlerts = await db.ComplianceAlerts
            .Where(a => a.SubscriptionId == subscriptionId
                && (a.Status == AlertStatus.New || a.Status == AlertStatus.Acknowledged || a.Status == AlertStatus.InProgress)
                && (a.Type == AlertType.Drift || a.Type == AlertType.Violation))
            .ToListAsync(cancellationToken);

        foreach (var alert in activeAlerts)
        {
            await EnsureCurrentEnvironmentAsync(subscriptionId, cancellationToken);
            if (alert.AffectedResources.Count == 0)
                continue;

            var resourceId = alert.AffectedResources.First();

            var baseline = await db.ComplianceBaselines
                .FirstOrDefaultAsync(b => b.ResourceId == resourceId && b.IsActive, cancellationToken);

            if (baseline == null)
                continue;

            // Run a quick check to see if resource has returned to baseline
            try
            {
                var assessment = await _complianceEngine.RunAssessmentAsync(
                    subscriptionId,
                    scanType: "quick",
                    cancellationToken: cancellationToken);

                var currentFindings = assessment.Findings
                    .Where(f => f.ResourceId == resourceId)
                    .ToList();

                var currentSnapshot = JsonSerializer.Serialize(
                    currentFindings.Select(f => new { f.ControlId, f.Status, f.ResourceType }).ToList());
                var currentHash = ComputeHash(currentSnapshot);

                if (currentHash == baseline.ConfigurationHash)
                {
                    // Resource has returned to baseline — auto-resolve
                    await _alertManager.TransitionAlertAsync(
                        alert.Id, AlertStatus.Resolved, "system", "System",
                        justification: "Auto-resolved: resource returned to baseline state.",
                        cancellationToken: cancellationToken);

                    _logger.LogInformation("Auto-resolved alert {AlertId} — resource {Resource} returned to baseline",
                        alert.AlertId, resourceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-resolve check for alert {AlertId}", alert.AlertId);
            }
        }
    }

    /// <summary>
    /// Compute the next scheduled run time based on frequency.
    /// </summary>
    internal static DateTimeOffset ComputeNextRunAt(MonitoringFrequency frequency)
    {
        var now = DateTimeOffset.UtcNow;
        return frequency switch
        {
            MonitoringFrequency.FifteenMinutes => now.AddMinutes(15),
            MonitoringFrequency.Hourly => now.AddHours(1),
            MonitoringFrequency.Daily => now.AddDays(1),
            MonitoringFrequency.Weekly => now.AddDays(7),
            _ => now.AddHours(1)
        };
    }

    /// <summary>
    /// Compute SHA-256 hash of a configuration snapshot string.
    /// </summary>
    internal static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Determine alert severity based on affected control families.
    /// AC, IA, SC families are Critical/High; others default to Medium.
    /// </summary>
    private static AlertSeverity DetermineDriftSeverity(List<string> controlFamilies)
    {
        var highRiskFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AC", "IA", "SC", "AU" };

        if (controlFamilies.Any(cf => highRiskFamilies.Contains(cf)))
            return AlertSeverity.High;

        return AlertSeverity.Medium;
    }

    /// <summary>
    /// Detect differences between baseline and current configuration snapshots.
    /// Returns a JSON change details string.
    /// </summary>
    private static string DetectChanges(string baselineSnapshot, string currentSnapshot)
    {
        try
        {
            var changes = new { baseline = baselineSnapshot, current = currentSnapshot };
            return JsonSerializer.Serialize(changes);
        }
        catch
        {
            return JsonSerializer.Serialize(new { error = "Unable to compute detailed changes" });
        }
    }

    // ─── Alert Rule Management (US3) ─────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AlertRule> CreateAlertRuleAsync(AlertRule rule, CancellationToken cancellationToken = default)
    {
        rule.Id = Guid.NewGuid();
        rule.CreatedAt = DateTimeOffset.UtcNow;
        rule.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Alert rule '{Name}' created for {Sub}", rule.Name, rule.SubscriptionId ?? "*");
        return rule;
    }

    /// <inheritdoc />
    public async Task<List<AlertRule>> GetAlertRulesAsync(string? subscriptionId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.AlertRules.AsQueryable();
        if (subscriptionId != null)
            query = query.Where(r => r.SubscriptionId == subscriptionId || r.SubscriptionId == null);

        return await query.OrderBy(r => r.Name).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SuppressionRule> CreateSuppressionAsync(SuppressionRule rule, CancellationToken cancellationToken = default)
    {
        // Validation
        if (rule.Type == SuppressionType.Permanent && string.IsNullOrWhiteSpace(rule.Justification))
            throw new InvalidOperationException("JUSTIFICATION_REQUIRED: Permanent suppression requires a justification.");

        if (rule.Type == SuppressionType.Temporary && (rule.ExpiresAt == null || rule.ExpiresAt <= DateTimeOffset.UtcNow))
            throw new InvalidOperationException("INVALID_EXPIRATION: Temporary suppression requires ExpiresAt in the future.");

        if ((rule.QuietHoursStart.HasValue) != (rule.QuietHoursEnd.HasValue))
            throw new InvalidOperationException("INVALID_QUIET_HOURS: Both QuietHoursStart and QuietHoursEnd must be set, or neither.");

        rule.Id = Guid.NewGuid();
        rule.IsActive = true;
        rule.CreatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.SuppressionRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Suppression rule created for {Sub} type={Type}", rule.SubscriptionId ?? "*", rule.Type);
        return rule;
    }

    /// <inheritdoc />
    public async Task<List<SuppressionRule>> GetSuppressionsAsync(string? subscriptionId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.SuppressionRules.Where(s => s.IsActive);
        if (subscriptionId != null)
            query = query.Where(s => s.SubscriptionId == subscriptionId || s.SubscriptionId == null);

        return await query.OrderByDescending(s => s.CreatedAt).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SuppressionRule> ConfigureQuietHoursAsync(
        string subscriptionId,
        TimeOnly start,
        TimeOnly end,
        string createdBy = "system",
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Find existing quiet-hours suppression for this sub, or create one
        var existing = await db.SuppressionRules
            .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId
                && s.IsActive && s.QuietHoursStart != null, cancellationToken);

        if (existing != null)
        {
            existing.QuietHoursStart = start;
            existing.QuietHoursEnd = end;
            await db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var rule = new SuppressionRule
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            Type = SuppressionType.Permanent,
            Justification = "Quiet hours configuration",
            QuietHoursStart = start,
            QuietHoursEnd = end,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.SuppressionRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Quiet hours configured for {Sub}: {Start}-{End}", subscriptionId, start, end);
        return rule;
    }

    /// <inheritdoc />
    public bool IsAlertSuppressed(ComplianceAlert alert, IReadOnlyList<SuppressionRule> activeSuppressions)
    {
        foreach (var suppression in activeSuppressions)
        {
            // Check scope match
            if (suppression.SubscriptionId != null && !string.Equals(suppression.SubscriptionId, alert.SubscriptionId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (suppression.ResourceId != null && !alert.AffectedResources.Any(r => r.Contains(suppression.ResourceId, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (suppression.ControlFamily != null && !string.Equals(suppression.ControlFamily, alert.ControlFamily, StringComparison.OrdinalIgnoreCase))
                continue;

            if (suppression.ControlId != null && !string.Equals(suppression.ControlId, alert.ControlId, StringComparison.OrdinalIgnoreCase))
                continue;

            // Check temporary expiration
            if (suppression.Type == SuppressionType.Temporary && suppression.ExpiresAt.HasValue && suppression.ExpiresAt.Value <= DateTimeOffset.UtcNow)
                continue; // Expired

            // Check quiet hours — Critical alerts bypass quiet hours (FR-019)
            if (suppression.QuietHoursStart.HasValue && suppression.QuietHoursEnd.HasValue)
            {
                if (alert.Severity == AlertSeverity.Critical)
                    continue; // Critical alerts are never suppressed by quiet hours

                var now = TimeOnly.FromDateTime(DateTime.UtcNow);
                if (IsInQuietHours(now, suppression.QuietHoursStart.Value, suppression.QuietHoursEnd.Value))
                    return true; // Suppressed during quiet hours
                continue; // Outside quiet hours — not suppressed by this rule
            }

            // Full match — alert is suppressed
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public AlertRule? MatchAlertRule(ComplianceAlert alert, IReadOnlyList<AlertRule> rules)
    {
        AlertRule? bestMatch = null;
        var bestSpecificity = -1;

        foreach (var rule in rules)
        {
            if (!rule.IsEnabled) continue;
            // Scoped rules create independent review work after boundary attribution.
            if (rule.RegisteredSystemId != null) continue;
            if (!MonitoringConditionEvaluator.Matches(rule.TriggerCondition, alert)) continue;

            // Scope matching
            if (rule.SubscriptionId != null && !string.Equals(rule.SubscriptionId, alert.SubscriptionId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (rule.ResourceId != null && !alert.AffectedResources.Any(r => string.Equals(r.TrimEnd('/'), rule.ResourceId.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                continue;

            if (rule.ResourceGroupName != null && !alert.AffectedResources.Any(r =>
                r.Contains($"/resourceGroups/{rule.ResourceGroupName}/", StringComparison.OrdinalIgnoreCase))) continue;
            if (rule.ResourceType != null && !alert.AffectedResources.Any(r =>
                r.Contains($"/providers/{rule.ResourceType}/", StringComparison.OrdinalIgnoreCase))) continue;

            if (rule.ControlFamily != null && !string.Equals(rule.ControlFamily, alert.ControlFamily, StringComparison.OrdinalIgnoreCase))
                continue;

            if (rule.ControlId != null && !string.Equals(rule.ControlId, alert.ControlId, StringComparison.OrdinalIgnoreCase))
                continue;

            // Compute specificity score (more specific rules win)
            var specificity = 0;
            if (rule.SubscriptionId != null) specificity++;
            if (rule.ResourceGroupName != null) specificity++;
            if (rule.ResourceId != null) specificity++;
            if (rule.ControlFamily != null) specificity++;
            if (rule.ControlId != null) specificity++;

            if (specificity > bestSpecificity)
            {
                bestSpecificity = specificity;
                bestMatch = rule;
            }
        }

        return bestMatch;
    }

    /// <inheritdoc />
    public async Task SeedDefaultRulesAsync(string subscriptionId, string createdBy = "system", CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Check if defaults already exist for this subscription
        var hasDefaults = await db.AlertRules
            .AnyAsync(r => r.SubscriptionId == subscriptionId && r.IsDefault, cancellationToken);

        if (hasDefaults) return;

        var defaultRules = new List<AlertRule>
        {
            new()
            {
                Id = Guid.NewGuid(), Name = "Default: Access Control Changes",
                Description = "Alert on any access control (AC) changes", SubscriptionId = subscriptionId,
                ControlFamily = "AC", SeverityOverride = AlertSeverity.High, IsDefault = true,
                IsEnabled = true, CreatedBy = createdBy, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(), Name = "Default: Encryption Changes",
                Description = "Critical alert when encryption settings change", SubscriptionId = subscriptionId,
                ControlFamily = "SC", ControlId = "SC-28", SeverityOverride = AlertSeverity.Critical, IsDefault = true,
                IsEnabled = true, CreatedBy = createdBy, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(), Name = "Default: Audit Logging Changes",
                Description = "Critical alert when audit logging is disabled", SubscriptionId = subscriptionId,
                ControlFamily = "AU", SeverityOverride = AlertSeverity.Critical, IsDefault = true,
                IsEnabled = true, CreatedBy = createdBy, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(), Name = "Default: MFA Changes",
                Description = "Critical alert when MFA configuration changes", SubscriptionId = subscriptionId,
                ControlFamily = "IA", SeverityOverride = AlertSeverity.Critical, IsDefault = true,
                IsEnabled = true, CreatedBy = createdBy, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        db.AlertRules.AddRange(defaultRules);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded {Count} default alert rules for {Sub}", defaultRules.Count, subscriptionId);
    }

    /// <summary>
    /// Check if the current time falls within quiet hours window.
    /// Handles overnight windows (e.g., 22:00-06:00).
    /// </summary>
    private static bool IsInQuietHours(TimeOnly now, TimeOnly start, TimeOnly end)
    {
        if (start <= end)
            return now >= start && now < end;
        else
            return now >= start || now < end; // Overnight window
    }

    // ─── Auto-Remediation Engine (US9) ──────────────────────────────────────

    /// <inheritdoc />
    public async Task<AutoRemediationRule> CreateAutoRemediationRuleAsync(
        AutoRemediationRule rule, CancellationToken cancellationToken = default)
    {
        // Validate blocked families
        if (!string.IsNullOrEmpty(rule.ControlFamily) && BlockedFamilies.Contains(rule.ControlFamily))
            throw new ArgumentException(
                $"BLOCKED_FAMILY: Control family '{rule.ControlFamily}' cannot be auto-remediated — AC, IA, and SC always require human approval.");

        // Validate approval mode
        if (rule.ApprovalMode is not ("auto" or "require-approval"))
            throw new ArgumentException("INVALID_APPROVAL_MODE: ApprovalMode must be 'auto' or 'require-approval'.");

        // Validate required fields
        if (string.IsNullOrWhiteSpace(rule.Name))
            throw new ArgumentException("INVALID_NAME: Rule name is required.");
        if (string.IsNullOrWhiteSpace(rule.Action))
            throw new ArgumentException("INVALID_ACTION: Action is required.");

        rule.Id = Guid.NewGuid();
        rule.CreatedAt = DateTimeOffset.UtcNow;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        rule.ExecutionCount = 0;

        await using var db = _dbFactory.CreateDbContext();
        db.AutoRemediationRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created auto-remediation rule {Name} ({Id}) for family {Family}",
            rule.Name, rule.Id, rule.ControlFamily ?? "all");

        return rule;
    }

    /// <inheritdoc />
    public async Task<List<AutoRemediationRule>> GetAutoRemediationRulesAsync(
        string? subscriptionId = null, bool? isEnabled = null, CancellationToken cancellationToken = default)
    {
        await using var db = _dbFactory.CreateDbContext();
        var query = db.AutoRemediationRules.AsQueryable();

        if (subscriptionId is not null)
            query = query.Where(r => r.SubscriptionId == subscriptionId || r.SubscriptionId == null);
        if (isEnabled.HasValue)
            query = query.Where(r => r.IsEnabled == isEnabled.Value);

        return await query.OrderBy(r => r.Name).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AutoRemediationResult> TryAutoRemediateAsync(
        ComplianceAlert alert, CancellationToken cancellationToken = default)
    {
        await EnsureCurrentEnvironmentAsync(alert.SubscriptionId, cancellationToken);
        // Never auto-remediate blocked families
        if (!string.IsNullOrEmpty(alert.ControlFamily) && BlockedFamilies.Contains(alert.ControlFamily))
        {
            return new AutoRemediationResult
            {
                Attempted = false,
                Success = false,
                Message = $"Control family '{alert.ControlFamily}' is blocked from auto-remediation."
            };
        }

        // Find matching enabled rules
        await using var db = _dbFactory.CreateDbContext();
        var rules = await db.AutoRemediationRules
            .Where(r => r.IsEnabled)
            .Where(r => r.SubscriptionId == null || r.SubscriptionId == alert.SubscriptionId)
            .Where(r => r.ControlFamily == null || r.ControlFamily == alert.ControlFamily)
            .Where(r => r.ControlId == null || r.ControlId == alert.ControlId)
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
        {
            return new AutoRemediationResult
            {
                Attempted = false,
                Success = false,
                Message = "No matching auto-remediation rules found."
            };
        }

        // Use the most specific rule (prefer ControlId match > ControlFamily match > global)
        var bestRule = rules
            .OrderByDescending(r => r.ControlId != null ? 3 : 0)
            .ThenByDescending(r => r.ControlFamily != null ? 2 : 0)
            .ThenByDescending(r => r.SubscriptionId != null ? 1 : 0)
            .First();

        // Require-approval mode — don't execute, just flag
        if (bestRule.ApprovalMode == "require-approval")
        {
            return new AutoRemediationResult
            {
                Attempted = false,
                Success = false,
                MatchedRule = bestRule,
                Message = $"Rule '{bestRule.Name}' matched but requires approval before execution."
            };
        }

        // Auto mode — dry-run first, then execute
        try
        {
            _logger.LogInformation("Auto-remediating alert {AlertId} with rule {RuleName} ({RuleId})",
                alert.AlertId, bestRule.Name, bestRule.Id);

            // Step 1: Dry-run
            var dryRunResult = await _remediationEngine.BatchRemediateAsync(
                alert.SubscriptionId, alert.Severity.ToString(), alert.ControlFamily,
                dryRun: true, cancellationToken: cancellationToken);

            _logger.LogInformation("Dry-run completed for alert {AlertId}: {Result}", alert.AlertId, dryRunResult);

            // Step 2: Execute remediation
            var executeResult = await _remediationEngine.BatchRemediateAsync(
                alert.SubscriptionId, alert.Severity.ToString(), alert.ControlFamily,
                dryRun: false, cancellationToken: cancellationToken);

            // Step 3: Create RESOLUTION alert
            var resolutionAlert = new ComplianceAlert
            {
                Type = AlertType.Drift, // Resolution sub-type
                Severity = AlertSeverity.Low,
                Status = AlertStatus.Resolved,
                Title = $"[RESOLUTION] Auto-remediated: {alert.Title}",
                Description = $"Auto-remediation applied by rule '{bestRule.Name}'. Original alert: {alert.AlertId}. Action: {bestRule.Action}",
                SubscriptionId = alert.SubscriptionId,
                ControlId = alert.ControlId,
                ControlFamily = alert.ControlFamily,
                AffectedResources = alert.AffectedResources,
                ResolvedAt = DateTimeOffset.UtcNow
            };
            await EnrichAlertWithSystemAsync(resolutionAlert, cancellationToken);
            await _alertManager.CreateAlertAsync(resolutionAlert, cancellationToken);

            // Step 4: Update rule execution stats
            bestRule.ExecutionCount++;
            bestRule.LastExecutedAt = DateTimeOffset.UtcNow;
            bestRule.UpdatedAt = DateTimeOffset.UtcNow;
            var ruleDb = _dbFactory.CreateDbContext();
            ruleDb.AutoRemediationRules.Update(bestRule);
            await ruleDb.SaveChangesAsync(cancellationToken);
            await ruleDb.DisposeAsync();

            // Step 5: Create audit log entry
            var auditDb = _dbFactory.CreateDbContext();
            auditDb.AuditLogs.Add(new AuditLogEntry
            {
                Id = Guid.NewGuid().ToString(),
                Action = "AutoRemediation",
                UserId = "system:auto-remediation",
                UserRole = "System",
                Details = JsonSerializer.Serialize(new
                {
                    alertId = alert.AlertId,
                    ruleId = bestRule.Id,
                    ruleName = bestRule.Name,
                    action = bestRule.Action,
                    affectedResources = alert.AffectedResources,
                    outcome = "Success"
                }),
                Timestamp = DateTime.UtcNow
            });
            await auditDb.SaveChangesAsync(cancellationToken);
            await auditDb.DisposeAsync();

            _logger.LogInformation("Auto-remediation succeeded for alert {AlertId} with rule {RuleName}",
                alert.AlertId, bestRule.Name);

            return new AutoRemediationResult
            {
                Attempted = true,
                Success = true,
                MatchedRule = bestRule,
                Message = $"Auto-remediation applied successfully using rule '{bestRule.Name}'."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-remediation failed for alert {AlertId} with rule {RuleName}",
                alert.AlertId, bestRule.Name);

            // Move alert back to New on failure
            try
            {
                await _alertManager.TransitionAlertAsync(
                    alert.Id, AlertStatus.New, "system:auto-remediation", "System",
                    $"Auto-remediation failed: {ex.Message}", cancellationToken);
            }
            catch (Exception transitionEx)
            {
                _logger.LogWarning(transitionEx, "Failed to transition alert {AlertId} back to New after remediation failure",
                    alert.AlertId);
            }

            // Create audit log for failure
            try
            {
                var auditDb = _dbFactory.CreateDbContext();
                auditDb.AuditLogs.Add(new AuditLogEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    Action = "AutoRemediation",
                    UserId = "system:auto-remediation",
                    UserRole = "System",
                    Details = JsonSerializer.Serialize(new
                    {
                        alertId = alert.AlertId,
                        ruleId = bestRule.Id,
                        ruleName = bestRule.Name,
                        action = bestRule.Action,
                        outcome = "Failed",
                        error = ex.Message
                    }),
                    Timestamp = DateTime.UtcNow
                });
                await auditDb.SaveChangesAsync(cancellationToken);
                await auditDb.DisposeAsync();
            }
            catch (Exception auditEx)
            {
                _logger.LogWarning(auditEx, "Failed to create audit log for remediation failure");
            }

            return new AutoRemediationResult
            {
                Attempted = true,
                Success = false,
                MatchedRule = bestRule,
                Message = $"Auto-remediation failed for rule '{bestRule.Name}'.",
                FailureReason = ex.Message
            };
        }
    }

    /// <summary>
    /// Populates the legacy single-system FK only when exact in-scope component attribution is unambiguous.
    /// </summary>
    private async Task EnrichAlertWithSystemAsync(ComplianceAlert alert, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var assignments = await db.BoundaryComponentAssignments.AsNoTracking()
            .Where(x => x.IsInScope && x.SystemComponent != null && x.SystemComponent.AzureResourceId != null)
            .Select(x => new { x.SystemComponent!.AzureResourceId, x.AuthorizationBoundaryDefinition.RegisteredSystemId })
            .ToListAsync(cancellationToken);
        var systems = assignments.Where(x => alert.AffectedResources.Any(r =>
                string.Equals(r.TrimEnd('/'), x.AzureResourceId!.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.RegisteredSystemId).Distinct().ToList();
        alert.RegisteredSystemId = systems.Count == 1 ? systems[0] : null;
    }

    private Task EnsureCurrentEnvironmentAsync(string subscriptionId, CancellationToken cancellationToken) =>
        _environmentGuard?.EnsureSubscriptionAsync(subscriptionId, EnvironmentScopePurpose.Monitoring,
            cancellationToken, requireAttachment: true) ?? Task.CompletedTask;

    /// <summary>
    /// When drift count exceeds <see cref="MonitoringOptions.SignificantDriftThreshold"/>,
    /// resolve <see cref="IConMonService"/> from a new scope and call <c>ReportChangeAsync</c>
    /// with <c>changeType = "configuration_drift"</c>. Phase 17 §9a.5.
    /// </summary>
    private async Task AutoCreateDriftSignificantChangeAsync(
        string systemId, string subscriptionId, int driftCount, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceScopeFactory!.CreateScope();
            var conMonService = scope.ServiceProvider.GetService<IConMonService>();
            if (conMonService == null)
            {
                _logger.LogDebug("IConMonService not available — skipping drift significant change creation");
                return;
            }

            await conMonService.ReportChangeAsync(
                systemId,
                "configuration_drift",
                $"Configuration drift detected across {driftCount} resources in subscription {subscriptionId}, " +
                $"exceeding threshold of {_monitoringOptions.Value.SignificantDriftThreshold}. " +
                $"Automated review recommended.",
                "ComplianceWatchService",
                cancellationToken);

            _logger.LogInformation(
                "Auto-created significant change (configuration_drift) for system {SystemId}: " +
                "{DriftCount} drifted resources exceeded threshold {Threshold}",
                systemId, driftCount, _monitoringOptions.Value.SignificantDriftThreshold);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to auto-create significant change for subscription {SubscriptionId} " +
                "after {DriftCount} drift detections",
                subscriptionId, driftCount);
        }
    }
}

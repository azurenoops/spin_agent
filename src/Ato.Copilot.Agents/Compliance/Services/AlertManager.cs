using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using TaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Alert lifecycle manager — CRUD, state machine transitions, ID generation, role-based access.
/// Singleton service using IDbContextFactory for database access.
/// </summary>
public class AlertManager : IAlertManager
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IAlertCorrelationService? _correlationService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AlertManager> _logger;
    private readonly AlertOptions _alertOptions;

    // Lazy-resolved to break circular dependency:
    // AlertManager → IAlertNotificationService → AlertNotificationService → IComplianceWatchService
    //   → ComplianceWatchService → IAlertManager → AlertManager
    private IAlertNotificationService? _notificationService;
    private bool _notificationServiceResolved;

    /// <summary>
    /// Valid alert status transitions. Key = current status, Value = allowed next statuses.
    /// </summary>
    private static readonly Dictionary<AlertStatus, HashSet<AlertStatus>> ValidTransitions = new()
    {
        [AlertStatus.New] = new() { AlertStatus.Acknowledged, AlertStatus.Escalated, AlertStatus.Resolved },
        [AlertStatus.Acknowledged] = new() { AlertStatus.InProgress, AlertStatus.Dismissed, AlertStatus.Escalated, AlertStatus.Resolved },
        [AlertStatus.InProgress] = new() { AlertStatus.Resolved, AlertStatus.Escalated },
        [AlertStatus.Escalated] = new() { AlertStatus.Acknowledged, AlertStatus.InProgress, AlertStatus.Resolved },
        [AlertStatus.Resolved] = new() { AlertStatus.New },
        [AlertStatus.Dismissed] = new()
    };

    /// <summary>
    /// Roles that are considered Compliance Officers (full access).
    /// </summary>
    private static readonly HashSet<string> ComplianceOfficerRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ComplianceRoles.Administrator,
        "ComplianceOfficer",
        "compliance_officer"
    };

    /// <summary>
    /// Role considered read-only (Auditor).
    /// </summary>
    private static readonly HashSet<string> AuditorRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ComplianceRoles.Auditor,
        "Auditor"
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="AlertManager"/> class.
    /// </summary>
    public AlertManager(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IOptions<AlertOptions> alertOptions,
        ILogger<AlertManager> logger,
        IServiceProvider serviceProvider,
        IAlertCorrelationService? correlationService = null)
    {
        _dbFactory = dbFactory;
        _alertOptions = alertOptions.Value;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _correlationService = correlationService;
    }

    /// <summary>
    /// Lazily resolves IAlertNotificationService to break the singleton circular dependency.
    /// </summary>
    private IAlertNotificationService? EnsureNotificationServiceResolved()
    {
        if (!_notificationServiceResolved)
        {
            _notificationService = _serviceProvider.GetService<IAlertNotificationService>();
            _notificationServiceResolved = true;
        }
        return _notificationService;
    }

    /// <inheritdoc />
    public async Task<ComplianceAlert> CreateAlertAsync(
        ComplianceAlert alert,
        CancellationToken cancellationToken = default)
    {
        alert.Id = Guid.NewGuid();
        alert.AlertId = await GenerateAlertIdAsync(cancellationToken);
        alert.Status = AlertStatus.New;
        alert.CreatedAt = DateTimeOffset.UtcNow;
        alert.UpdatedAt = DateTimeOffset.UtcNow;
        alert.SlaDeadline = ComputeSlaDeadline(alert.Severity, alert.CreatedAt);

        // Attempt correlation — merge into existing grouped alert if possible
        if (_correlationService != null)
        {
            var result = await _correlationService.CorrelateAlertAsync(alert, cancellationToken);
            if (result.WasMerged)
            {
                // Alert was merged into an existing group — update parent in DB
                await using var mergeDb = await _dbFactory.CreateDbContextAsync(cancellationToken);
                var parent = await mergeDb.ComplianceAlerts.FindAsync(
                    new object[] { result.Alert.Id }, cancellationToken);
                if (parent != null)
                {
                    parent.ChildAlertCount = result.Alert.ChildAlertCount;
                    parent.UpdatedAt = DateTimeOffset.UtcNow;
                    await mergeDb.SaveChangesAsync(cancellationToken);
                }

                // Still persist the child alert with GroupedAlertId reference
                alert.GroupedAlertId = result.Alert.Id;

                _logger.LogInformation(
                    "Alert {AlertId} merged into group {ParentAlertId} (key: {Key}, count: {Count})",
                    alert.AlertId, result.Alert.AlertId, result.CorrelationKey, result.Alert.ChildAlertCount);
            }
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.ComplianceAlerts.Add(alert);
        await db.SaveChangesAsync(cancellationToken);

        // Phase 17 §9a.2 — fire notification after successful persistence.
        // Optional dependency: null → silent (backward-compatible).
        // Lazily resolved to break circular dependency chain.
        var notificationService = EnsureNotificationServiceResolved();
        if (notificationService != null)
        {
            try
            {
                await notificationService.SendNotificationAsync(alert, cancellationToken);
            }
            catch (Exception ex)
            {
                // Notification failure must NOT fail alert creation — log and continue.
                _logger.LogWarning(ex,
                    "Failed to send notification for alert {AlertId}; alert was persisted successfully",
                    alert.AlertId);
            }
        }

        _logger.LogInformation(
            "Alert created | AlertId: {AlertId} | Type: {Type} | Severity: {Severity} | Sub: {Sub}",
            alert.AlertId, alert.Type, alert.Severity, alert.SubscriptionId);

        return alert;
    }

    /// <inheritdoc />
    public async Task<ComplianceAlert> TransitionAlertAsync(
        Guid alertId,
        AlertStatus newStatus,
        string userId,
        string userRole,
        string? justification = null,
        CancellationToken cancellationToken = default)
    {
        // Auditor role is read-only — deny all transitions
        if (AuditorRoles.Contains(userRole))
        {
            throw new InvalidOperationException(
                "INSUFFICIENT_PERMISSIONS: Auditor role has read-only access. Cannot transition alerts.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var alert = await db.ComplianceAlerts.FindAsync(new object[] { alertId }, cancellationToken)
            ?? throw new KeyNotFoundException($"ALERT_NOT_FOUND: Alert {alertId} not found.");

        // Validate transition
        if (!ValidTransitions.TryGetValue(alert.Status, out var allowed) || !allowed.Contains(newStatus))
        {
            throw new InvalidOperationException(
                $"INVALID_TRANSITION: Cannot transition from {alert.Status} to {newStatus}.");
        }

        // Role-based constraints
        if (newStatus == AlertStatus.Dismissed)
        {
            if (!ComplianceOfficerRoles.Contains(userRole))
            {
                throw new InvalidOperationException(
                    "INSUFFICIENT_PERMISSIONS: Only Compliance Officers can dismiss alerts.");
            }
            if (string.IsNullOrWhiteSpace(justification))
            {
                throw new InvalidOperationException(
                    "JUSTIFICATION_REQUIRED: Dismissal requires a justification.");
            }
            alert.DismissalJustification = justification;
            alert.DismissedBy = userId;
        }

        // Update status and timestamps
        var previousStatus = alert.Status;
        alert.Status = newStatus;
        alert.UpdatedAt = DateTimeOffset.UtcNow;

        switch (newStatus)
        {
            case AlertStatus.Acknowledged:
                alert.AcknowledgedAt = DateTimeOffset.UtcNow;
                alert.AcknowledgedBy = userId;
                break;
            case AlertStatus.Resolved:
                alert.ResolvedAt = DateTimeOffset.UtcNow;
                break;
            case AlertStatus.Escalated:
                alert.EscalatedAt = DateTimeOffset.UtcNow;
                break;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Auto-close linked Kanban task when alert is resolved
        if (newStatus == AlertStatus.Resolved)
        {
            var linkedTask = await db.RemediationTasks
                .FirstOrDefaultAsync(t => t.LinkedAlertId == alert.AlertId && t.Status != TaskStatus.Done,
                    cancellationToken);
            if (linkedTask != null)
            {
                linkedTask.Status = TaskStatus.Done;
                linkedTask.UpdatedAt = DateTime.UtcNow;
                linkedTask.History.Add(new TaskHistoryEntry
                {
                    TaskId = linkedTask.Id,
                    EventType = HistoryEventType.StatusChanged,
                    OldValue = linkedTask.Status.ToString(),
                    NewValue = TaskStatus.Done.ToString(),
                    ActingUserId = userId,
                    ActingUserName = userId,
                    Details = $"Auto-closed: linked alert {alert.AlertId} resolved."
                });
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Auto-closed task {TaskNumber} for resolved alert {AlertId}",
                    linkedTask.TaskNumber, alert.AlertId);
            }
        }

        // Capture state change as AuditLogEntry
        db.AuditLogs.Add(new AuditLogEntry
        {
            UserId = userId,
            UserRole = userRole,
            Action = "AlertTransition",
            Timestamp = DateTime.UtcNow,
            SubscriptionId = alert.SubscriptionId,
            Outcome = AuditOutcome.Success,
            Details = $"Alert {alert.AlertId} transitioned from {previousStatus} to {newStatus}." +
                      (justification != null ? $" Justification: {justification}" : "")
        });
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Alert transitioned | AlertId: {AlertId} | {From} → {To} | By: {User}",
            alert.AlertId, previousStatus, newStatus, userId);

        return alert;
    }

    /// <inheritdoc />
    public async Task<ComplianceAlert?> GetAlertAsync(
        Guid alertId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ComplianceAlerts
            .Include(a => a.Notifications)
            .Include(a => a.ChildAlerts)
            .FirstOrDefaultAsync(a => a.Id == alertId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ComplianceAlert?> GetAlertByAlertIdAsync(
        string alertId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ComplianceAlerts
            .Include(a => a.Notifications)
            .Include(a => a.ChildAlerts)
            .FirstOrDefaultAsync(a => a.AlertId == alertId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(List<ComplianceAlert> Alerts, int TotalCount)> GetAlertsAsync(
        string? subscriptionId = null,
        AlertSeverity? severity = null,
        AlertStatus? status = null,
        string? controlFamily = null,
        int? days = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, _alertOptions.MaxPageSize);
        page = Math.Max(1, page);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.ComplianceAlerts.AsQueryable();

        if (!string.IsNullOrWhiteSpace(subscriptionId))
            query = query.Where(a => a.SubscriptionId == subscriptionId);

        if (severity.HasValue)
            query = query.Where(a => a.Severity == severity.Value);

        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(controlFamily))
            query = query.Where(a => a.ControlFamily == controlFamily);

        if (days.HasValue)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-days.Value);
            query = query.Where(a => a.CreatedAt >= cutoff);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var alerts = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (alerts, totalCount);
    }

    /// <inheritdoc />
    public async Task<string> GenerateAlertIdAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Use a transaction for atomic increment. SqlServerRetryingExecutionStrategy
        // requires user-initiated transactions to be wrapped in CreateExecutionStrategy().
        string alertId = null!;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var counter = await db.AlertIdCounters.FindAsync(new object[] { today }, cancellationToken);

                if (counter == null)
                {
                    counter = new AlertIdCounter { Date = today, LastSequence = 1 };
                    db.AlertIdCounters.Add(counter);
                }
                else
                {
                    counter.LastSequence++;
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // Format: ALT-YYYYMMDDNNNNN
                alertId = $"ALT-{today:yyyyMMdd}{counter.LastSequence:D5}";
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
        return alertId;
    }

    /// <inheritdoc />
    public async Task<ComplianceAlert> DismissAlertAsync(
        Guid alertId,
        string justification,
        string userId,
        string userRole,
        CancellationToken cancellationToken = default)
    {
        return await TransitionAlertAsync(
            alertId,
            AlertStatus.Dismissed,
            userId,
            userRole,
            justification,
            cancellationToken);
    }

    /// <summary>
    /// Compute SLA deadline based on alert severity and configured options.
    /// </summary>
    private DateTimeOffset ComputeSlaDeadline(AlertSeverity severity, DateTimeOffset createdAt)
    {
        var minutes = severity switch
        {
            AlertSeverity.Critical => _alertOptions.CriticalSlaMinutes,
            AlertSeverity.High => _alertOptions.HighSlaMinutes,
            AlertSeverity.Medium => _alertOptions.MediumSlaMinutes,
            AlertSeverity.Low => _alertOptions.LowSlaMinutes,
            _ => _alertOptions.MediumSlaMinutes
        };

        return createdAt.AddMinutes(minutes);
    }
}

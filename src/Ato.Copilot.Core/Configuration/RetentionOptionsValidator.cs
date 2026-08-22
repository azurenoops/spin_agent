using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Configuration;

/// <summary>
/// Validates <see cref="RetentionPolicyOptions"/> at startup to enforce federal retention floors.
/// Prevents misconfigured deployments from purging audit logs before the legally-required period.
///
/// Floors (issue #730 / FR-043):
/// - AuditLogRetentionDays >= 2555 (7 years — NARA GRS 3.2, FedRAMP AU-11, DoDI 8500.01)
/// - AssessmentRetentionDays >= 365 (1 year — FR-042 minimum)
/// - AlertRetentionDays >= 90 (3 months — operational minimum)
/// - WeeklySnapshotRetentionDays >= 90 (3 months — operational minimum)
/// </summary>
public sealed class RetentionOptionsValidator : IValidateOptions<RetentionPolicyOptions>
{
    /// <summary>
    /// Federal minimum for audit log retention: 7 years (2555 days).
    /// Reference: NARA GRS 3.2, FedRAMP AU-11 control, DoDI 8500.01.
    /// </summary>
    public const int AuditLogFloorDays = 2555;

    /// <summary>Minimum assessment retention floor: 1 year (365 days) per FR-042.</summary>
    public const int AssessmentFloorDays = 365;

    /// <summary>Operational minimum for alert and snapshot retention.</summary>
    public const int OperationalFloorDays = 90;

    public ValidateOptionsResult Validate(string? name, RetentionPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        // Hard floor: audit logs must meet the 7-year federal minimum (FR-043 / NARA GRS 3.2).
        if (options.AuditLogRetentionDays < AuditLogFloorDays)
        {
            errors.Add(
                $"Retention:AuditLogRetentionDays is {options.AuditLogRetentionDays} days, " +
                $"which is below the federal minimum of {AuditLogFloorDays} days (7 years). " +
                "Federal systems must retain audit logs for a minimum of 7 years per " +
                "NARA GRS 3.2, FedRAMP AU-11, and DoDI 8500.01. " +
                $"Set Retention:AuditLogRetentionDays to at least {AuditLogFloorDays}.");
        }

        // Soft floor: assessment data (1 year minimum per FR-042).
        if (options.AssessmentRetentionDays < AssessmentFloorDays)
        {
            errors.Add(
                $"Retention:AssessmentRetentionDays is {options.AssessmentRetentionDays} days, " +
                $"which is below the minimum of {AssessmentFloorDays} days (1 year) per FR-042. " +
                $"Set Retention:AssessmentRetentionDays to at least {AssessmentFloorDays}.");
        }

        // Operational floor: alert and snapshot retention.
        if (options.AlertRetentionDays < OperationalFloorDays)
        {
            errors.Add(
                $"Retention:AlertRetentionDays is {options.AlertRetentionDays} days, " +
                $"which is below the operational minimum of {OperationalFloorDays} days. " +
                $"Set Retention:AlertRetentionDays to at least {OperationalFloorDays}.");
        }

        if (options.WeeklySnapshotRetentionDays < OperationalFloorDays)
        {
            errors.Add(
                $"Retention:WeeklySnapshotRetentionDays is {options.WeeklySnapshotRetentionDays} days, " +
                $"which is below the operational minimum of {OperationalFloorDays} days. " +
                $"Set Retention:WeeklySnapshotRetentionDays to at least {OperationalFloorDays}.");
        }

        // CleanupInterval sanity check.
        if (options.CleanupIntervalHours < 1)
        {
            errors.Add("Retention:CleanupIntervalHours must be at least 1 hour.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

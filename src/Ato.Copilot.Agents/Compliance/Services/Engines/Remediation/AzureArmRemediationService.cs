using System.Text.Json;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services.Engines.Remediation;

/// <summary>
/// Azure ARM resource operations for remediation (Tier 3).
/// Plans legacy ARM operations in dry-run mode and captures resource snapshots.
/// Live operations fail explicitly until resource-specific implementations exist.
/// </summary>
public class AzureArmRemediationService : IAzureArmRemediationService
{
    private const string LiveRemediationNotImplementedMessage =
        "Live ARM remediation is not implemented for operation '{0}'. Use dry-run mode to preview the planned operation.";
    private const string SnapshotRestoreNotImplementedMessage =
        "ARM snapshot restoration is not implemented. No Azure resource changes were applied.";

    private readonly ArmClient _armClient;
    private readonly ILogger<AzureArmRemediationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureArmRemediationService"/> class.
    /// </summary>
    public AzureArmRemediationService(
        ArmClient armClient,
        ILogger<AzureArmRemediationService> logger)
    {
        _armClient = armClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> CaptureResourceSnapshotAsync(
        string resourceId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            _logger.LogWarning("Empty resource ID — cannot capture snapshot");
            return null;
        }

        try
        {
            _logger.LogInformation("Capturing resource snapshot for {ResourceId}", resourceId);

            var resourceIdentifier = new Azure.Core.ResourceIdentifier(resourceId);
            var resource = _armClient.GetGenericResource(resourceIdentifier);
            var response = await resource.GetAsync(ct);

            if (response?.Value?.Data != null)
            {
                var snapshot = JsonSerializer.Serialize(new
                {
                    resourceId,
                    capturedAt = DateTime.UtcNow,
                    properties = response.Value.Data.Properties?.ToString(),
                    location = response.Value.Data.Location.Name,
                    tags = response.Value.Data.Tags
                }, JsonOptions);

                _logger.LogInformation("Snapshot captured for {ResourceId} ({Bytes} bytes)",
                    resourceId, snapshot.Length);

                return snapshot;
            }

            _logger.LogWarning("Resource {ResourceId} returned null data", resourceId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture snapshot for {ResourceId}", resourceId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<RemediationExecution> ExecuteArmRemediationAsync(
        ComplianceFinding finding,
        RemediationExecutionOptions options,
        CancellationToken ct = default)
    {
        var execution = new RemediationExecution
        {
            FindingId = finding.Id,
            SubscriptionId = finding.SubscriptionId,
            Status = RemediationExecutionStatus.InProgress,
            StartedAt = DateTime.UtcNow,
            DryRun = options.DryRun,
            Options = options,
            TierUsed = 3 // ARM is Tier 3
        };

        try
        {
            _logger.LogInformation(
                "Executing ARM remediation for {FindingId} ({ControlId}, {RemType})",
                finding.Id, finding.ControlId, finding.RemediationType);

            // Determine ARM operation from control family and remediation type
            var operation = DetermineArmOperation(finding);

            if (options.DryRun)
            {
                execution.Status = RemediationExecutionStatus.Completed;
                execution.ChangesApplied = new List<string>
                {
                    $"[DRY RUN] Would execute ARM operation: {operation}"
                };
                execution.StepsExecuted = 1;
                execution.CompletedAt = DateTime.UtcNow;
                execution.Duration = execution.CompletedAt - execution.StartedAt;
                return execution;
            }

            throw new NotSupportedException(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                LiveRemediationNotImplementedMessage,
                operation));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ARM remediation failed for {FindingId}", finding.Id);
            execution.Status = RemediationExecutionStatus.Failed;
            execution.Error = ex.Message;
            execution.CompletedAt = DateTime.UtcNow;
            execution.Duration = execution.CompletedAt - execution.StartedAt;
        }

        return execution;
    }

    /// <inheritdoc />
    public async Task<RemediationRollbackResult> RestoreFromSnapshotAsync(
        string resourceId,
        string snapshotJson,
        CancellationToken ct = default)
    {
        var result = new RemediationRollbackResult();

        try
        {
            if (string.IsNullOrWhiteSpace(snapshotJson))
            {
                result.Success = false;
                result.Error = "No snapshot data provided for rollback";
                return result;
            }

            _logger.LogInformation("Restoring resource {ResourceId} from snapshot", resourceId);

            _ = new Azure.Core.ResourceIdentifier(resourceId);
            using var doc = JsonDocument.Parse(snapshotJson);

            result.Success = false;
            result.Error = SnapshotRestoreNotImplementedMessage;
            _logger.LogWarning(
                "Snapshot restore is not implemented for {ResourceId}; no Azure changes were applied",
                resourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed for {ResourceId}", resourceId);
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>Determines the ARM operation based on finding characteristics.</summary>
    private static string DetermineArmOperation(ComplianceFinding finding)
    {
        var family = finding.ControlFamily?.ToUpperInvariant() ?? "";
        var title = finding.Title?.ToLowerInvariant() ?? "";

        return (family, finding.RemediationType) switch
        {
            (_, RemediationType.PolicyAssignment) => "PolicyAssignment",
            (_, RemediationType.PolicyRemediation) => "PolicyRemediation",
            ("SC", _) when title.Contains("tls") => "TlsVersionUpdate",
            ("SC", _) when title.Contains("encrypt") => "Encryption",
            ("SC", _) when title.Contains("https") => "HttpsEnforcement",
            ("SC", _) when title.Contains("nsg") || title.Contains("network") => "NsgConfiguration",
            ("AU", _) when title.Contains("diagnostic") || title.Contains("log") => "DiagnosticSettings",
            ("AU", _) when title.Contains("retention") => "LogRetention",
            ("AU", _) when title.Contains("alert") => "AlertRules",
            _ => "GenericResourceConfiguration"
        };
    }

}

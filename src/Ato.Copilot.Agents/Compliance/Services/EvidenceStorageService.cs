using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Evidence storage and collection service. Collects compliance evidence from
/// Azure (config exports, policy snapshots, resource inventories, activity logs,
/// Defender recommendations), computes SHA-256 content hash, persists via EF Core.
/// </summary>
public class EvidenceStorageService : IEvidenceStorageService
{
    private readonly IAzurePolicyComplianceService _policyService;
    private readonly IDefenderForCloudService _defenderService;
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly ILogger<EvidenceStorageService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EvidenceStorageService"/> class.
    /// </summary>
    public EvidenceStorageService(
        IAzurePolicyComplianceService policyService,
        IDefenderForCloudService defenderService,
        IDbContextFactory<AtoCopilotContext> dbFactory,
        ILogger<EvidenceStorageService> logger)
    {
        _policyService = policyService;
        _defenderService = defenderService;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ComplianceEvidence> CollectEvidenceAsync(
        string controlId,
        string subscriptionId,
        string? resourceGroup = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Collecting evidence for control {ControlId} in subscription {SubId}",
            controlId, subscriptionId);

        var family = controlId.Split('-')[0].ToUpperInvariant();

        // Collect evidence based on control family
        var content = await CollectEvidenceContentAsync(
            controlId, family, subscriptionId, cancellationToken);

        var evidence = new ComplianceEvidence
        {
            ControlId = controlId,
            SubscriptionId = subscriptionId,
            EvidenceType = DetermineEvidenceType(family),
            Description = $"Automated evidence collection for {controlId}",
            Content = content,
            CollectedBy = "Security Posture Intelligence Navigator (automated)",
            EvidenceCategory = DetermineCategory(family),
            ContentHash = ComputeHash(content)
        };

        // Persist
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.Evidence.Add(evidence);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Evidence {Id} collected for {ControlId} | Type: {Type} | Hash: {Hash}",
            evidence.Id, controlId, evidence.EvidenceType, evidence.ContentHash[..12] + "...");

        return evidence;
    }

    /// <inheritdoc />
    public async Task<List<ComplianceEvidence>> GetEvidenceAsync(
        string controlId,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Evidence.Where(e => e.ControlId == controlId);

        if (!string.IsNullOrEmpty(subscriptionId))
            query = query.Where(e => e.SubscriptionId == subscriptionId);

        return await query
            .OrderByDescending(e => e.CollectedAt)
            .ToListAsync(cancellationToken);
    }

    // ─── Private Methods ────────────────────────────────────────────────────────

    /// <summary>Collects evidence content from Azure services based on the control family.</summary>
    private async Task<string> CollectEvidenceContentAsync(
        string controlId,
        string family,
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return family switch
            {
                // Access Control — policy compliance snapshot
                "AC" or "IA" => await CollectPolicyEvidenceAsync(subscriptionId, controlId, cancellationToken),

                // Audit & Accountability — audit log data
                "AU" => await CollectAuditEvidenceAsync(subscriptionId, controlId, cancellationToken),

                // System & Communications Protection — Defender assessments
                "SC" or "SI" => await CollectDefenderEvidenceAsync(subscriptionId, controlId, cancellationToken),

                // Configuration Management — policy state
                "CM" => await CollectPolicyEvidenceAsync(subscriptionId, controlId, cancellationToken),

                // Contingency Planning — backup/recovery evidence
                "CP" => await CollectDefenderEvidenceAsync(subscriptionId, controlId, cancellationToken),

                // Default — policy compliance snapshot
                _ => await CollectPolicyEvidenceAsync(subscriptionId, controlId, cancellationToken)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Evidence collection failed for {ControlId}, using error snapshot", controlId);
            return JsonSerializer.Serialize(new
            {
                controlId,
                subscriptionId,
                error = ex.Message,
                collectedAt = DateTime.UtcNow,
                note = "Evidence collection encountered an error. Manual evidence collection may be required."
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>Collects policy compliance evidence from Azure Policy for the subscription.</summary>
    private async Task<string> CollectPolicyEvidenceAsync(
        string subscriptionId,
        string controlId,
        CancellationToken cancellationToken)
    {
        var policyData = await _policyService.GetComplianceSummaryAsync(subscriptionId, cancellationToken);

        var evidence = new
        {
            controlId,
            subscriptionId,
            evidenceType = "PolicyComplianceSnapshot",
            collectedAt = DateTime.UtcNow,
            policyComplianceSummary = JsonDocument.Parse(policyData).RootElement.Clone(),
            note = $"Azure Policy compliance snapshot for control {controlId}"
        };

        return JsonSerializer.Serialize(evidence, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>Collects audit and monitoring evidence from Azure Policy states.</summary>
    private async Task<string> CollectAuditEvidenceAsync(
        string subscriptionId,
        string controlId,
        CancellationToken cancellationToken)
    {
        // Collect audit-related evidence from policy states
        var policyData = await _policyService.GetPolicyStatesAsync(subscriptionId, null, cancellationToken);

        var evidence = new
        {
            controlId,
            subscriptionId,
            evidenceType = "AuditLogSnapshot",
            collectedAt = DateTime.UtcNow,
            auditPolicyStates = JsonDocument.Parse(policyData).RootElement.Clone(),
            note = $"Audit policy compliance states for control {controlId}"
        };

        return JsonSerializer.Serialize(evidence, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>Collects security assessment evidence from Microsoft Defender for Cloud.</summary>
    private async Task<string> CollectDefenderEvidenceAsync(
        string subscriptionId,
        string controlId,
        CancellationToken cancellationToken)
    {
        var assessmentData = await _defenderService.GetAssessmentsAsync(subscriptionId, cancellationToken);
        var scoreData = await _defenderService.GetSecureScoreAsync(subscriptionId, cancellationToken);

        var evidence = new
        {
            controlId,
            subscriptionId,
            evidenceType = "SecurityAssessmentSnapshot",
            collectedAt = DateTime.UtcNow,
            secureScore = JsonDocument.Parse(scoreData).RootElement.Clone(),
            securityAssessments = JsonDocument.Parse(assessmentData).RootElement.Clone(),
            note = $"Defender for Cloud security assessment snapshot for control {controlId}"
        };

        return JsonSerializer.Serialize(evidence, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Computes SHA-256 hash of content for integrity verification.
    /// </summary>
    public static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Maps a control family to an evidence type string (e.g., "policy", "configuration").</summary>
    private static string DetermineEvidenceType(string family) =>
        family switch
        {
            "AC" or "IA" => "PolicyComplianceSnapshot",
            "AU" => "AuditLogSnapshot",
            "SC" or "SI" => "SecurityAssessmentSnapshot",
            "CM" => "ConfigurationSnapshot",
            "CP" => "RecoverySnapshot",
            _ => "ComplianceSnapshot"
        };

    /// <summary>Maps a control family to its evidence category (Access, Data, Network, System).</summary>
    private static EvidenceCategory DetermineCategory(string family) =>
        family switch
        {
            "AC" or "IA" or "CM" => EvidenceCategory.PolicyCompliance,
            "AU" => EvidenceCategory.ActivityLog,
            "SC" or "SI" => EvidenceCategory.SecurityAssessment,
            "CP" => EvidenceCategory.ResourceCompliance,
            _ => EvidenceCategory.Configuration
        };
}

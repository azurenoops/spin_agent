using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Links multiple Azure SDK evidence sources to NIST controls.
/// Supports multi-evidence chain visualization (§418 AC-2).
/// Persists ControlEvidenceMapping records and writes EvidenceAuditEvents on every link.
/// </summary>
public class EvidenceCorrelationEngine : IEvidenceCorrelationEngine
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IEvidenceAuditService _auditService;
    private readonly ILogger<EvidenceCorrelationEngine> _logger;

    /// <summary>Base correlation score for direct Azure SDK evidence (same family).</summary>
    private const double DirectCorrelationScore = 1.0;
    /// <summary>Reduced score for indirect/policy-inferred evidence.</summary>
    private const double IndirectCorrelationScore = 0.6;

    public EvidenceCorrelationEngine(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IEvidenceAuditService auditService,
        ILogger<EvidenceCorrelationEngine> logger)
    {
        _dbFactory = dbFactory;
        _auditService = auditService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ControlEvidenceMapping> CorrelateEvidenceAsync(
        string controlId,
        string subscriptionId,
        string evidenceReferenceId,
        EvidenceSourceType sourceType,
        string mappedBy,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var score = ComputeCorrelationScore(controlId, sourceType);

        var mapping = new ControlEvidenceMapping
        {
            ControlId = controlId,
            SubscriptionId = subscriptionId,
            EvidenceReferenceId = evidenceReferenceId,
            EvidenceSourceType = sourceType,
            MappedBy = mappedBy,
            MappingNote = note,
            CorrelationScore = score,
            MappedAt = DateTime.UtcNow
        };

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.Set<ControlEvidenceMapping>().Add(mapping);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Correlated evidence {RefId} → control {ControlId} (score: {Score:F2}, source: {Source})",
            evidenceReferenceId, controlId, score, sourceType);

        var meta = JsonSerializer.Serialize(new
        {
            evidenceReferenceId,
            sourceType = sourceType.ToString(),
            correlationScore = score
        });

        await _auditService.RecordEventAsync(
            EvidenceAuditEventType.Mapped,
            controlId,
            mappedBy,
            $"Evidence {evidenceReferenceId} mapped to control {controlId} (score {score:F2})",
            subscriptionId,
            meta,
            cancellationToken);

        return mapping;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ControlEvidenceMapping>> GetMappingsForControlAsync(
        string controlId,
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.Set<ControlEvidenceMapping>()
            .Where(m => m.ControlId == controlId && m.SubscriptionId == subscriptionId)
            .OrderByDescending(m => m.CorrelationScore)
            .ThenByDescending(m => m.MappedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> AutoCorrelatePackageAsync(
        EvidencePackage package,
        string subscriptionId,
        string actorId = "system",
        CancellationToken cancellationToken = default)
    {
        if (package.EvidenceItems == null || package.EvidenceItems.Count == 0)
            return 0;

        var created = 0;

        foreach (var item in package.EvidenceItems)
        {
            try
            {
                // Derive control ID from family code (maps to the primary representative control)
                var controlId = $"{package.FamilyCode}-1";

                await CorrelateEvidenceAsync(
                    controlId,
                    subscriptionId,
                    item.ContentHash ?? item.Title,
                    EvidenceSourceType.AutomatedAzure,
                    actorId,
                    $"Auto-correlated from {package.FamilyCode} evidence package",
                    cancellationToken);

                created++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to auto-correlate evidence item {Title} for family {Family}",
                    item.Title, package.FamilyCode);
            }
        }

        _logger.LogInformation(
            "Auto-correlated {Count}/{Total} items from {Family} evidence package",
            created, package.EvidenceItems.Count, package.FamilyCode);

        return created;
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────

    private static double ComputeCorrelationScore(string controlId, EvidenceSourceType sourceType)
    {
        // Manual uploads receive a slight reduction — requires human review
        return sourceType switch
        {
            EvidenceSourceType.AutomatedAzure => DirectCorrelationScore,
            EvidenceSourceType.ExternalScan => DirectCorrelationScore,
            EvidenceSourceType.ManualUpload => IndirectCorrelationScore,
            _ => IndirectCorrelationScore
        };
    }
}

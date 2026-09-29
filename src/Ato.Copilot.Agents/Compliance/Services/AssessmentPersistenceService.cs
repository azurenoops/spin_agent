using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Database persistence service for assessments and findings.
/// Uses <see cref="IDbContextFactory{TContext}"/> for thread-safe context creation
/// with upsert semantics and 24-hour cache for latest assessment.
/// </summary>
public class AssessmentPersistenceService : IAssessmentPersistenceService
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AssessmentPersistenceService> _logger;

    private static readonly TimeSpan LatestAssessmentCacheTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Initializes a new instance of the <see cref="AssessmentPersistenceService"/> class.
    /// </summary>
    /// <param name="dbFactory">EF Core context factory.</param>
    /// <param name="cache">Memory cache for latest assessment.</param>
    /// <param name="logger">Logger instance.</param>
    public AssessmentPersistenceService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IMemoryCache cache,
        ILogger<AssessmentPersistenceService> logger)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveAssessmentAsync(
        ComplianceAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);

            // Normalize finding ControlIds: set to null if the referenced NistControl doesn't exist.
            // The Findings→NistControls FK is optional (IsRequired=false) but a non-null ControlId
            // that doesn't match any NistControl.Id violates the FK constraint.
            var nistControlIds = await context.NistControls
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);
            var nistControlSet = new HashSet<string>(nistControlIds, StringComparer.OrdinalIgnoreCase);

            foreach (var finding in assessment.Findings)
            {
                if (!string.IsNullOrEmpty(finding.ControlId))
                {
                    // Try lowercase match first (NistControls use lowercase IDs)
                    var normalizedId = finding.ControlId.ToLowerInvariant();
                    if (nistControlSet.Contains(normalizedId))
                    {
                        finding.ControlId = normalizedId;
                    }
                    else if (!nistControlSet.Contains(finding.ControlId))
                    {
                        // No matching NistControl exists — null out to avoid FK violation
                        _logger.LogDebug("ControlId '{ControlId}' not found in NistControls, setting to null",
                            finding.ControlId);
                        finding.ControlId = null!;
                    }
                }
            }

            var existing = await context.Assessments
                .FirstOrDefaultAsync(a => a.Id == assessment.Id, cancellationToken);

            if (existing is not null)
            {
                // Upsert: update existing assessment
                var incomingExecution = AssessmentResultProvenance.Read(assessment.ResultProvenanceJson).ExecutionToken;
                if (incomingExecution is not null && incomingExecution != AssessmentResultProvenance.Read(existing.ResultProvenanceJson).ExecutionToken)
                    throw new DbUpdateConcurrencyException("Assessment execution was superseded by a retained retry.");
                context.Entry(existing).CurrentValues.SetValues(assessment);

                // Preserve finding identities and their downstream remediation/provenance links.
                var existingFindings = await context.Findings
                    .Where(f => f.AssessmentId == assessment.Id)
                    .ToListAsync(cancellationToken);
                foreach (var finding in assessment.Findings)
                {
                    var saved = existingFindings.FirstOrDefault(x => x.Id == finding.Id);
                    if (saved is null) context.Findings.Add(finding);
                    else context.Entry(saved).CurrentValues.SetValues(finding);
                }

                _logger.LogDebug("Updated existing assessment {Id}", assessment.Id);
            }
            else
            {
                // Insert new assessment
                context.Assessments.Add(assessment);
                _logger.LogDebug("Inserted new assessment {Id}", assessment.Id);
            }

            await context.SaveChangesAsync(cancellationToken);

            // Invalidate latest assessment cache for this subscription
            var cacheKey = $"latest-assessment:{assessment.SubscriptionId}";
            _cache.Set(cacheKey, assessment, LatestAssessmentCacheTtl);

            _logger.LogInformation("Saved assessment {Id} for Sub={Sub} (Score: {Score}%)",
                assessment.Id, assessment.SubscriptionId, assessment.ComplianceScore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save assessment {Id}", assessment.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ComplianceAssessment?> GetAssessmentAsync(
        string assessmentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
            return await context.Assessments
                .Include(a => a.Findings)
                .FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get assessment {Id}", assessmentId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ComplianceAssessment?> GetLatestAssessmentAsync(
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"latest-assessment:{subscriptionId}";

        if (_cache.TryGetValue<ComplianceAssessment>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var latest = await context.Assessments
                .Include(a => a.Findings)
                .Where(a => a.SubscriptionId == subscriptionId)
                .OrderByDescending(a => a.AssessedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (latest is not null)
            {
                _cache.Set(cacheKey, latest, LatestAssessmentCacheTtl);
            }

            return latest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest assessment for Sub={Sub}", subscriptionId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<List<ComplianceAssessment>> GetAssessmentHistoryAsync(
        string subscriptionId,
        int days,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var cutoff = DateTime.UtcNow.AddDays(-days);

            return await context.Assessments
                .Include(a => a.Findings)
                .Where(a => a.SubscriptionId == subscriptionId && a.AssessedAt >= cutoff)
                .OrderByDescending(a => a.AssessedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get assessment history for Sub={Sub}", subscriptionId);
            return new List<ComplianceAssessment>();
        }
    }

    /// <inheritdoc />
    public async Task<ComplianceFinding?> GetFindingAsync(
        string findingId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
            return await context.Findings
                .FirstOrDefaultAsync(f => f.Id == findingId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get finding {Id}", findingId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateFindingStatusAsync(
        string findingId,
        FindingStatus status,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var finding = await context.Findings
                .FirstOrDefaultAsync(f => f.Id == findingId, cancellationToken);

            if (finding is null)
            {
                _logger.LogWarning("Finding {Id} not found for status update", findingId);
                return false;
            }

            finding.Status = status;

            if (status == FindingStatus.Remediated)
            {
                finding.RemediationTrackingStatus = RemediationTrackingStatus.Completed;
                finding.RemediatedAt = DateTime.UtcNow;
            }
            else if (status == FindingStatus.InProgress)
            {
                finding.RemediationTrackingStatus = RemediationTrackingStatus.InProgress;
            }

            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Updated finding {Id} status to {Status}", findingId, status);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update finding {Id} status", findingId);
            return false;
        }
    }
}

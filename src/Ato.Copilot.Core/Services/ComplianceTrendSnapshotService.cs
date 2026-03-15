using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Background service that captures daily compliance trend snapshots for all active systems.
/// Also exposes <see cref="CaptureSnapshotAsync"/> for on-demand capture after assessments.
/// </summary>
public class ComplianceTrendSnapshotService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ComplianceTrendSnapshotService> _logger;

    public ComplianceTrendSnapshotService(
        IServiceScopeFactory scopeFactory,
        ILogger<ComplianceTrendSnapshotService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Runs the daily snapshot loop. Fires at midnight UTC, then sleeps until the next day.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ComplianceTrendSnapshotService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextMidnight = now.Date.AddDays(1);
            var delay = nextMidnight - now;

            _logger.LogDebug("Next trend snapshot capture in {Delay}", delay);
            await Task.Delay(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
                await CaptureAllSnapshotsAsync(db, "Scheduled", stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to capture scheduled trend snapshots");
            }
        }
    }

    /// <summary>
    /// Captures a single on-demand snapshot for the specified system (e.g., after an assessment completes).
    /// </summary>
    public async Task CaptureSnapshotAsync(string systemId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, cancellationToken);

        if (system is null)
        {
            _logger.LogWarning("Snapshot requested for unknown or inactive system {SystemId}", systemId);
            return;
        }

        var snapshot = await BuildSnapshotAsync(db, systemId, "Assessment", cancellationToken);
        db.ComplianceTrendSnapshots.Add(snapshot);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("On-demand trend snapshot captured for system {SystemId}, score={Score}",
            systemId, snapshot.ComplianceScore);
    }

    private async Task CaptureAllSnapshotsAsync(AtoCopilotContext db, string source, CancellationToken ct)
    {
        var systemIds = await db.RegisteredSystems
            .Where(s => s.IsActive)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (systemIds.Count == 0)
        {
            _logger.LogInformation("No active systems found for trend snapshot capture");
            return;
        }

        _logger.LogInformation("Capturing trend snapshots for {Count} active systems", systemIds.Count);

        foreach (var systemId in systemIds)
        {
            try
            {
                var snapshot = await BuildSnapshotAsync(db, systemId, source, ct);
                db.ComplianceTrendSnapshots.Add(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to capture snapshot for system {SystemId}", systemId);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Saved {Count} trend snapshots", systemIds.Count);
    }

    private static async Task<ComplianceTrendSnapshot> BuildSnapshotAsync(
        AtoCopilotContext db, string systemId, string source, CancellationToken ct)
    {
        // Latest completed assessment score
        var latestAssessment = await db.Assessments
            .Where(a => a.RegisteredSystemId == systemId && a.Status == AssessmentStatus.Completed)
            .OrderByDescending(a => a.AssessedAt)
            .FirstOrDefaultAsync(ct);

        var complianceScore = latestAssessment?.ComplianceScore ?? 0;

        // Finding counts by CatSeverity from latest assessment
        int catI = 0, catII = 0, catIII = 0;
        if (latestAssessment is not null)
        {
            var findings = await db.Findings
                .Where(f => f.AssessmentId == latestAssessment.Id && f.Status == FindingStatus.Open && f.CatSeverity != null)
                .GroupBy(f => f.CatSeverity)
                .Select(g => new { Severity = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            catI = findings.FirstOrDefault(f => f.Severity == CatSeverity.CatI)?.Count ?? 0;
            catII = findings.FirstOrDefault(f => f.Severity == CatSeverity.CatII)?.Count ?? 0;
            catIII = findings.FirstOrDefault(f => f.Severity == CatSeverity.CatIII)?.Count ?? 0;
        }

        // POA&M counts
        var openPoam = await db.PoamItems
            .CountAsync(p => p.RegisteredSystemId == systemId && p.Status == PoamStatus.Ongoing, ct);
        var overduePoam = await db.PoamItems
            .CountAsync(p => p.RegisteredSystemId == systemId && p.Status == PoamStatus.Ongoing
                         && p.ScheduledCompletionDate < DateTime.UtcNow, ct);

        // Narrative coverage: controls with non-empty narratives / total baseline controls
        var baseline = await db.ControlBaselines
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, ct);

        double narrativeCoverage = 0;
        if (baseline is not null && baseline.TotalControls > 0)
        {
            var withNarrative = await db.ControlImplementations
                .CountAsync(ci => ci.RegisteredSystemId == systemId
                            && ci.Narrative != null && ci.Narrative != "", ct);
            narrativeCoverage = Math.Round((double)withNarrative / baseline.TotalControls * 100, 1);
        }

        return new ComplianceTrendSnapshot
        {
            RegisteredSystemId = systemId,
            ComplianceScore = Math.Round(complianceScore, 1),
            CatICount = catI,
            CatIICount = catII,
            CatIIICount = catIII,
            OpenPoamCount = openPoam,
            OverduePoamCount = overduePoam,
            NarrativeCoverage = narrativeCoverage,
            Source = source,
        };
    }
}

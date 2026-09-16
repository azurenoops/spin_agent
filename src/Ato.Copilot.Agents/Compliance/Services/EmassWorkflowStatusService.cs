using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed class EmassWorkflowStatusService(
    IDbContextFactory<AtoCopilotContext> contextFactory,
    IEmassExportReadinessService readinessService) : IEmassWorkflowStatusService
{
    public async Task<EmassWorkflowStatus> GetStatusAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var system = await context.RegisteredSystems.AsNoTracking().FirstOrDefaultAsync(
            candidate => candidate.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"RegisteredSystem '{systemId}' not found.");

        var latestPackage = await context.AuthorizationPackages
            .AsNoTracking()
            .Where(package => package.RegisteredSystemId == systemId &&
                package.Status == PackageStatus.Completed && package.CompletedAt != null)
            .OrderByDescending(package => package.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var lastExportedAt = latestPackage?.CompletedAt;

        var conflicts = context.EmassConflicts.AsNoTracking()
            .Where(conflict => conflict.RegisteredSystemId == systemId);
        var unresolvedConflictCount = await conflicts.CountAsync(
            conflict => conflict.ConflictStatus == ConflictStatus.Unresolved,
            cancellationToken);
        var lastSyncedAt = await conflicts.MaxAsync(
            conflict => (DateTimeOffset?)conflict.DetectedAt,
            cancellationToken);

        var controls = await context.ControlImplementations
            .AsNoTracking()
            .Where(implementation => implementation.RegisteredSystemId == systemId)
            .Select(implementation => new { implementation.AuthoredAt, implementation.ModifiedAt })
            .ToListAsync(cancellationToken);
        var poamItems = await context.PoamItems
            .AsNoTracking()
            .Where(item => item.RegisteredSystemId == systemId)
            .Select(item => new { item.CreatedAt, item.ModifiedAt })
            .ToListAsync(cancellationToken);

        var exportTime = lastExportedAt?.UtcDateTime;
        var pendingControls = exportTime.HasValue
            ? controls.Count(item => (item.ModifiedAt ?? item.AuthoredAt) > exportTime.Value)
            : controls.Count;
        var pendingPoamItems = exportTime.HasValue
            ? poamItems.Count(item => (item.ModifiedAt ?? item.CreatedAt) > exportTime.Value)
            : poamItems.Count;
        var systemTimestamp = system.ModifiedAt ?? system.CreatedAt;
        var pendingSystemInfo = exportTime.HasValue && systemTimestamp > exportTime.Value ? 1 : 0;

        var summaries = new List<EmassExportCategorySummary>
        {
            new("Controls", lastExportedAt.HasValue ? controls.Count - pendingControls : 0,
                pendingControls, lastExportedAt),
            new("PoamItems", lastExportedAt.HasValue ? poamItems.Count - pendingPoamItems : 0,
                pendingPoamItems, lastExportedAt),
            new("Artifacts", latestPackage?.TotalArtifactCount ?? 0, 0, lastExportedAt),
            new("SystemInfo", lastExportedAt.HasValue ? 1 - pendingSystemInfo : 0,
                pendingSystemInfo, lastExportedAt),
        };

        var readiness = await readinessService.CheckReadinessAsync(systemId, cancellationToken);
        var readinessSummary = new EmassReadinessSummary(
            readiness.IsReady,
            readiness.Gaps.Count(gap => gap.Severity == ReadinessGapSeverity.Blocking),
            readiness.Gaps.Count(gap => gap.Severity == ReadinessGapSeverity.Advisory));

        var pendingCount = summaries.Sum(summary => summary.PendingCount);
        var overallStatus = unresolvedConflictCount > 0
            ? EmassWorkflowOverallStatus.HasConflicts
            : !lastExportedAt.HasValue
                ? EmassWorkflowOverallStatus.NeverExported
                : pendingCount > 0
                    ? EmassWorkflowOverallStatus.PendingExport
                    : EmassWorkflowOverallStatus.UpToDate;

        return new EmassWorkflowStatus(
            systemId,
            overallStatus,
            lastExportedAt,
            lastSyncedAt,
            unresolvedConflictCount,
            summaries,
            readinessSummary);
    }
}
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed class EmassExportReadinessService(
    IDbContextFactory<AtoCopilotContext> contextFactory) : IEmassExportReadinessService
{
    public async Task<EmassExportReadinessResult> CheckReadinessAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var system = await context.RegisteredSystems
            .AsNoTracking()
            .Include(candidate => candidate.SecurityCategorization!)
                .ThenInclude(categorization => categorization.InformationTypes)
            .FirstOrDefaultAsync(candidate => candidate.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"RegisteredSystem '{systemId}' not found.");

        var gaps = new List<ReadinessGap>();
        AddRequiredIdentifierGap(gaps, systemId, "DitprId", system.DitprId,
            "DITPR System ID is required by eMASS to match the import to the correct system record.");
        AddRequiredIdentifierGap(gaps, systemId, "EmassId", system.EmassId,
            "eMASS System ID is required to match the export to the existing eMASS system record.");

        if (system.SecurityCategorization is null ||
            system.SecurityCategorization.InformationTypes.Count == 0)
        {
            gaps.Add(new ReadinessGap(
                "SecurityCategorization",
                "At least one categorized information type is required to establish CIA impact values.",
                ReadinessGapSeverity.Blocking,
                $"/systems/{systemId}/categorization"));
        }

        var hasApprovedSection = await context.SspSections
            .AsNoTracking()
            .AnyAsync(section => section.RegisteredSystemId == systemId &&
                section.Status == SspSectionStatus.Approved, cancellationToken);
        if (!hasApprovedSection)
        {
            gaps.Add(new ReadinessGap(
                "Ssp.ApprovedSections",
                "At least one SSP section should be approved before export.",
                ReadinessGapSeverity.Advisory,
                $"/systems/{systemId}/ssp"));
        }

            var unscheduledPoamCount = await context.PoamItems
                .AsNoTracking()
                .CountAsync(item => item.RegisteredSystemId == systemId &&
                item.ScheduledCompletionDate == default, cancellationToken);
            if (unscheduledPoamCount > 0)
            {
                gaps.Add(new ReadinessGap(
                "Poam.ScheduledCompletionDate",
                $"{unscheduledPoamCount} POA&M item(s) should have a scheduled completion date before export.",
                ReadinessGapSeverity.Advisory,
                $"/systems/{systemId}/poam"));
            }

        return new EmassExportReadinessResult(
            systemId,
            gaps.All(gap => gap.Severity != ReadinessGapSeverity.Blocking),
            gaps,
            DateTimeOffset.UtcNow);
    }

    private static void AddRequiredIdentifierGap(
        ICollection<ReadinessGap> gaps,
        string systemId,
        string fieldName,
        string? value,
        string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            gaps.Add(new ReadinessGap(
                fieldName,
                description,
                ReadinessGapSeverity.Blocking,
                $"/systems/{systemId}/settings#identifiers"));
        }
    }
}
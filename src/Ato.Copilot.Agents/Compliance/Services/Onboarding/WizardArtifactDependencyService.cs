using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding;

/// <summary>
/// EF-backed wizard artifact-dependency service (research §R6). Provides the cascade
/// machinery so when an admin replaces a template / import / narrative seed, downstream
/// systems / exports / suggestions are visibly flagged stale and one-click rerunable.
/// </summary>
public class WizardArtifactDependencyService : IWizardArtifactDependencyService
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IWizardJobRunner _jobRunner;
    private readonly ILogger<WizardArtifactDependencyService> _logger;

    public WizardArtifactDependencyService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IWizardJobRunner jobRunner,
        ILogger<WizardArtifactDependencyService> logger)
    {
        _contextFactory = contextFactory;
        _jobRunner = jobRunner;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WizardArtifactDependency> LinkAsync(
        Guid tenantId,
        ArtifactSourceKind sourceKind,
        Guid sourceArtifactId,
        string sourceVersionTag,
        ArtifactDependentKind dependentKind,
        Guid dependentId,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await db.WizardArtifactDependencies
            .FirstOrDefaultAsync(
                d => d.TenantId == tenantId
                  && d.SourceArtifactType == sourceKind
                  && d.SourceArtifactId == sourceArtifactId
                  && d.DependentType == dependentKind
                  && d.DependentId == dependentId,
                ct);
        if (existing is not null)
        {
            existing.SourceVersionTag = sourceVersionTag;
            existing.IsStale = false;
            existing.StaleSince = null;
            existing.StaleReason = null;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var dep = new WizardArtifactDependency
        {
            TenantId = tenantId,
            SourceArtifactType = sourceKind,
            SourceArtifactId = sourceArtifactId,
            SourceVersionTag = sourceVersionTag,
            DependentType = dependentKind,
            DependentId = dependentId,
        };
        db.WizardArtifactDependencies.Add(dep);
        await db.SaveChangesAsync(ct);
        return dep;
    }

    /// <inheritdoc />
    public async Task<int> FlagDependentsStaleAsync(
        Guid tenantId,
        ArtifactSourceKind sourceKind,
        Guid sourceArtifactId,
        string staleReason,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var rows = await db.WizardArtifactDependencies
            .Where(d => d.TenantId == tenantId
                     && d.SourceArtifactType == sourceKind
                     && d.SourceArtifactId == sourceArtifactId
                     && !d.IsStale)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            row.IsStale = true;
            row.StaleSince = now;
            row.StaleReason = staleReason;
        }
        if (rows.Count > 0)
            await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "wizard.dependency_cascade flagged {Count} dependents stale (tenant {TenantId}, source {SourceKind}/{SourceId})",
            rows.Count, tenantId, sourceKind, sourceArtifactId);

        return rows.Count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WizardArtifactDependency>> ListBySourceAsync(
        Guid tenantId,
        ArtifactSourceKind sourceKind,
        Guid sourceArtifactId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 500) pageSize = 500;

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.WizardArtifactDependencies
            .Where(d => d.TenantId == tenantId
                     && d.SourceArtifactType == sourceKind
                     && d.SourceArtifactId == sourceArtifactId)
            .OrderByDescending(d => d.IsStale)
            .ThenByDescending(d => d.DerivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<Guid?> RerunAsync(
        Guid tenantId,
        Guid dependencyId,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var dep = await db.WizardArtifactDependencies
            .FirstOrDefaultAsync(d => d.Id == dependencyId && d.TenantId == tenantId, ct);
        if (dep is null)
            return null;

        // Pick the right job type per source kind.
        var jobType = dep.SourceArtifactType switch
        {
            ArtifactSourceKind.Template => WizardJobType.ExportRerender,
            ArtifactSourceKind.EmassImportSession => WizardJobType.ImportRerender,
            ArtifactSourceKind.SspPdfImportSession => WizardJobType.ImportRerender,
            ArtifactSourceKind.NarrativeSeedDocument => WizardJobType.NarrativeSeedIndex,
            _ => WizardJobType.ImportRerender,
        };

        var job = await _jobRunner.EnqueueAsync(
            jobType,
            tenantId,
            actorUserId,
            new
            {
                dependencyId = dep.Id,
                sourceKind = dep.SourceArtifactType.ToString(),
                sourceId = dep.SourceArtifactId,
                dependentKind = dep.DependentType.ToString(),
                dependentId = dep.DependentId,
            },
            ct);

        dep.LastReRunJobId = job.Id;
        await db.SaveChangesAsync(ct);
        return job.Id;
    }
}

using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding;

/// <summary>
/// Cross-kind inventory aggregator for the <c>/admin/imported-documents</c>
/// view (T129 / SC-013). Pulls from each of the four wizard source tables,
/// projects to a unified <see cref="WizardArtifactInventoryRow"/>, and joins
/// <see cref="WizardArtifactDependency"/> aggregates for the dependents
/// rollup. Pagination caps page size at 200 (FR-093).
/// </summary>
public sealed class WizardArtifactInventoryService : IWizardArtifactInventoryService
{
    private const int MaxPageSize = 200;
    private sealed record DepCount(int Total, int Stale);

    private readonly IDbContextFactory<AtoCopilotContext> _factory;

    public WizardArtifactInventoryService(IDbContextFactory<AtoCopilotContext> factory)
    {
        _factory = factory;
    }

    public async Task<WizardArtifactInventoryPage> ListAsync(
        Guid tenantId,
        ArtifactSourceKind? filter = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        await using var db = await _factory.CreateDbContextAsync(ct);

        // Pre-aggregate dependents counts for every source. Sliced once and
        // intersected with each kind in memory, since dependency rows are
        // typically small relative to the unioned source set.
        var depAggList = await db.WizardArtifactDependencies
            .Where(d => d.TenantId == tenantId)
            .GroupBy(d => new { d.SourceArtifactType, d.SourceArtifactId })
            .Select(g => new
            {
                SourceKind = g.Key.SourceArtifactType,
                g.Key.SourceArtifactId,
                Total = g.Count(),
                Stale = g.Count(x => x.IsStale),
            })
            .ToListAsync(ct);
        var depMap = depAggList.ToDictionary(
            d => (d.SourceKind, d.SourceArtifactId),
            d => new DepCount(d.Total, d.Stale));

        var rows = new List<WizardArtifactInventoryRow>(256);

        if (filter is null or ArtifactSourceKind.Template)
        {
            var t = await db.OrganizationDocumentTemplates
                .Where(x => x.TenantId == tenantId && x.DeletedAt == null)
                .ToListAsync(ct);
            rows.AddRange(t.Select(x => Build(x, depMap)));
        }
        if (filter is null or ArtifactSourceKind.EmassImportSession)
        {
            var e = await db.EmassImportSessions
                .Where(x => x.TenantId == tenantId)
                .ToListAsync(ct);
            rows.AddRange(e.Select(x => Build(x, depMap)));
        }
        if (filter is null or ArtifactSourceKind.SspPdfImportSession)
        {
            var s = await db.SspPdfImportSessions
                .Where(x => x.TenantId == tenantId)
                .ToListAsync(ct);
            rows.AddRange(s.Select(x => Build(x, depMap)));
        }
        if (filter is null or ArtifactSourceKind.NarrativeSeedDocument)
        {
            var n = await db.NarrativeSeedDocuments
                .Where(x => x.TenantId == tenantId && x.Status == NarrativeSeedStatus.Active)
                .ToListAsync(ct);
            rows.AddRange(n.Select(x => Build(x, depMap)));
        }

        var ordered = rows.OrderByDescending(r => r.UpdatedAt).ToList();
        var total = ordered.Count;
        var paged = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new WizardArtifactInventoryPage(paged, total, page, pageSize);
    }

    private static WizardArtifactInventoryRow Build(
        OrganizationDocumentTemplate t,
        IReadOnlyDictionary<(ArtifactSourceKind, Guid), DepCount> depMap)
    {
        var (total, stale) = LookupDeps(depMap, ArtifactSourceKind.Template, t.Id);
        return new WizardArtifactInventoryRow(
            t.Id, t.TenantId, ArtifactSourceKind.Template,
            t.Label, t.Version, t.CreatedAt, t.UpdatedAt,
            total, stale, t.IsDefault ? "Default" : t.Status.ToString());
    }

    private static WizardArtifactInventoryRow Build(
        EmassImportSession e,
        IReadOnlyDictionary<(ArtifactSourceKind, Guid), DepCount> depMap)
    {
        var (total, stale) = LookupDeps(depMap, ArtifactSourceKind.EmassImportSession, e.Id);
        return new WizardArtifactInventoryRow(
            e.Id, e.TenantId, ArtifactSourceKind.EmassImportSession,
            e.OriginalFileName, null, e.CreatedAt, e.UpdatedAt,
            total, stale, e.Status.ToString());
    }

    private static WizardArtifactInventoryRow Build(
        SspPdfImportSession s,
        IReadOnlyDictionary<(ArtifactSourceKind, Guid), DepCount> depMap)
    {
        var (total, stale) = LookupDeps(depMap, ArtifactSourceKind.SspPdfImportSession, s.Id);
        return new WizardArtifactInventoryRow(
            s.Id, s.TenantId, ArtifactSourceKind.SspPdfImportSession,
            s.OriginalFileName, null, s.CreatedAt, s.UpdatedAt,
            total, stale, s.Status.ToString());
    }

    private static WizardArtifactInventoryRow Build(
        NarrativeSeedDocument n,
        IReadOnlyDictionary<(ArtifactSourceKind, Guid), DepCount> depMap)
    {
        var (total, stale) = LookupDeps(depMap, ArtifactSourceKind.NarrativeSeedDocument, n.Id);
        return new WizardArtifactInventoryRow(
            n.Id, n.TenantId, ArtifactSourceKind.NarrativeSeedDocument,
            n.Label, null, n.CreatedAt, n.UpdatedAt,
            total, stale, n.IndexingStatus.ToString());
    }

    private static (int total, int stale) LookupDeps(
        IReadOnlyDictionary<(ArtifactSourceKind, Guid), DepCount> map,
        ArtifactSourceKind kind, Guid id)
    {
        if (map.TryGetValue((kind, id), out var hit))
            return (hit.Total, hit.Stale);
        return (0, 0);
    }
}

using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Services.PackageImports;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderAuthorizationService
{
    public async Task<ProviderPackageReceiptResponse> ReceiveAsync(Guid id, ReceiveProviderPackageRequest request,
        IReadOnlyList<PackageUpload> files, string key, string actor, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, id, ct);
        var status = await packages.ReceiveForOfferingAsync(offering.ProviderId, key, request.Name, files,
            new(id, request.ExpectedOfferingRevision, request.BoundaryRevisionId, request.SeriesId, request.PreviousVersionId), actor, ct);
        var version = await db.Set<ProviderPackageVersion>().AsNoTracking().SingleAsync(x =>
            x.PackageId == status.PackageId && x.ProviderId == offering.ProviderId && x.OfferingId == id, ct);
        return new(status, PackageVersion(version));
    }

    public Task<ProviderPackageReceiptResponse> AssociateAsync(Guid packageId, AssociateProviderPackageRequest request,
        string key, string actor, CancellationToken ct) =>
        store.WriteAsync(request.OfferingId, $"PackageAssociated:{packageId:D}", key, request, actor, async (db, provider, _) =>
        {
            var package = await db.CspPackages.SingleOrDefaultAsync(x => x.Id == packageId && x.ProviderId == provider, ct)
                ?? throw new KeyNotFoundException("Package was not found in this provider.");
            if (package.Revision != request.ExpectedPackageRevision || package.ProcessingState == "Processing")
                throw new DbUpdateConcurrencyException("The package changed or is processing; reload before association.");
            var version = await CspPackageService.AssociateOfferingAsync(db, package,
                new(request.OfferingId, request.ExpectedOfferingRevision, request.BoundaryRevisionId,
                    request.SeriesId, request.PreviousVersionId), actor, ct);
            package.Revision++;
            package.Version++;
            package.UpdatedAt = DateTimeOffset.UtcNow;
            foreach (var approval in await db.CspPackageApprovals.Where(x => x.PackageId == packageId && x.State != "Published").ToListAsync(ct))
                approval.State = "Invalidated";
            foreach (var candidate in await db.CspPackageCandidates.Where(x => x.PackageId == packageId && x.ReviewState == "Approved").ToListAsync(ct))
                candidate.ReviewState = "Reviewed";
            return new ProviderPackageReceiptResponse(await CspPackageService.StatusAsync(db, package, ct), PackageVersion(version));
        }, ct);

    public async Task<PagedResult<ProviderPackageVersionResponse>> PackagesAsync(Guid id, int page, int pageSize,
        Guid? seriesId, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, id, ct);
        var query = db.Set<ProviderPackageVersion>().AsNoTracking().Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == id);
        if (seriesId.HasValue) query = query.Where(x => x.SeriesId == seriesId);
        return await PageAsync(query.OrderBy(x => x.SeriesId).ThenByDescending(x => x.Version), page, pageSize, PackageVersion, ct);
    }

    private static ProviderPackageVersionResponse PackageVersion(ProviderPackageVersion row) =>
        new(row.Id, row.OfferingId, row.SeriesId, row.Version, row.PackageId, row.BoundaryRevisionId,
            row.PreviousVersionId, row.ManifestHash, row.CreatedAt);
}

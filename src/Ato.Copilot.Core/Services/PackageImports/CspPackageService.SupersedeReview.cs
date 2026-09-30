using System.Data;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.PackageImports;

public sealed partial class CspPackageService
{
    public async Task<PackageStatus> SupersedeReviewAsync(Guid id, SupersedePackageReviewRequest request, string actor, CancellationToken ct)
    {
        Authorize();
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (actor.Length > 254 || request.ExpectedRevision < 1 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 2000)
            throw new ArgumentException("Supply an actor, positive expected revision and a reason of 1-2000 characters.");
        var reason = request.Reason.Trim();
        await using var strategy = await factory.CreateDbContextAsync(ct);
        return await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var package = await LoadAsync(db, id, ct);
            if (package.SupersededAt.HasValue)
            {
                if (package.Revision != request.ExpectedRevision + 1 || package.SupersededBy != actor
                    || package.SupersedeReason != reason || package.SupersededByPackageId != request.ReplacementPackageId)
                    throw new DbUpdateConcurrencyException("Review work was already superseded by a different decision.");
                return await StatusAsync(db, package, ct);
            }
            var now = DateTimeOffset.UtcNow;
            if (package.Revision != request.ExpectedRevision || package.ArchivedAt.HasValue || package.PublicationState != "Unpublished"
                || package.ProcessingState is "Received" or "Processing" || package.LeaseExpiresTicks > now.UtcTicks)
                throw new DbUpdateConcurrencyException("Supersession requires the current revision of an unpublished inactive source.");
            if (request.ReplacementPackageId == id || !package.OfferingId.HasValue)
                throw new DbUpdateConcurrencyException("Choose a different published replacement in the same offering.");
            var replacement = await db.CspPackages.SingleOrDefaultAsync(x => x.Id == request.ReplacementPackageId
                && x.ProviderId == package.ProviderId && x.OfferingId == package.OfferingId, ct);
            if (replacement is null || replacement.SupersededAt.HasValue || replacement.PublicationState != "Published"
                || replacement.ProcessingState is "Received" or "Processing" || replacement.LeaseExpiresTicks > now.UtcTicks)
                throw new DbUpdateConcurrencyException("The exact replacement must be active, published and in the same provider/offering.");
            var version = await db.Set<ProviderPackageVersion>().SingleOrDefaultAsync(x => x.Id == replacement.PackageVersionId
                && x.PackageId == replacement.Id && x.ProviderId == package.ProviderId && x.OfferingId == package.OfferingId
                && x.BoundaryRevisionId == replacement.BoundaryRevisionId, ct);
            if (version is null || await db.Set<ProviderPackageVersion>().AnyAsync(x => x.ProviderId == package.ProviderId
                && x.OfferingId == package.OfferingId && x.SeriesId == version.SeriesId && x.Version > version.Version, ct))
                throw new DbUpdateConcurrencyException("The replacement must be the exact latest retained version of its package series.");
            if (await db.CspPackageApprovals.AnyAsync(x => x.PackageId == id && (x.State == "Published" || x.PublicationJson != null), ct)
                || await db.CspPackageCandidates.AnyAsync(x => x.PackageId == id && x.ReviewState == "Published", ct))
                throw new DbUpdateConcurrencyException("Published candidate or approval history cannot be retired as unpublished review work.");

            package.SupersededByPackageId = replacement.Id;
            package.SupersededAt = now;
            package.SupersededBy = actor;
            package.SupersedeReason = reason;
            Touch(package, true);
            Audit(db, package, "ReviewSuperseded", actor, Json(new
            {
                request.ExpectedRevision, request.ReplacementPackageId, ReplacementRevision = replacement.Revision,
                ReplacementPackageVersionId = version.Id, Reason = reason
            }));
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return await StatusAsync(db, package, ct);
        });
    }
}

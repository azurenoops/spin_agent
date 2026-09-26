using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.PackageImports;

public sealed partial class CspPackageService
{
    internal static async Task<ProviderPackageVersion> AssociateOfferingAsync(AtoCopilotContext db, CspPackage package,
        PackageOfferingContext context, string actor, CancellationToken ct)
    {
        var offering = await db.Set<ProviderOffering>().SingleOrDefaultAsync(x => x.Id == context.OfferingId
            && x.ProviderId == package.ProviderId, ct) ?? throw new KeyNotFoundException("Offering was not found in this provider.");
        ProviderAuthorizationStore.Expected(offering, context.ExpectedOfferingRevision);
        await ProviderAuthorizationService.RequireBoundaryAsync(db, offering, context.BoundaryRevisionId, ct);
        if (package.PackageVersionId.HasValue)
            throw new DbUpdateConcurrencyException("A receipt already associated with a version cannot be silently reassigned.");
        ProviderPackageVersion? previous = null;
        if (context.PreviousVersionId.HasValue)
        {
            previous = await db.Set<ProviderPackageVersion>().SingleOrDefaultAsync(x =>
                x.Id == context.PreviousVersionId.Value && x.OfferingId == offering.Id && x.ProviderId == offering.ProviderId, ct)
                ?? throw new KeyNotFoundException("Previous package version was not found in this offering.");
            if (context.SeriesId.HasValue && context.SeriesId != previous.SeriesId)
                throw new ArgumentException("The predecessor belongs to a different package series.");
        }
        var seriesId = previous?.SeriesId ?? context.SeriesId ?? Guid.NewGuid();
        var latest = await db.Set<ProviderPackageVersion>().Where(x => x.ProviderId == offering.ProviderId
            && x.OfferingId == offering.Id && x.SeriesId == seriesId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        if (latest?.Id != previous?.Id)
            throw new DbUpdateConcurrencyException("Select the latest immutable package version as predecessor.");
        var originals = db.CspPackageEntries.Local.Where(x => x.PackageId == package.Id && x.IsOriginal).ToArray();
        if (originals.Length == 0)
            originals = await db.CspPackageEntries.AsNoTracking().Where(x => x.PackageId == package.Id && x.IsOriginal).ToArrayAsync(ct);
        if (originals.Length == 0) throw new InvalidDataException("A package association requires its retained original manifest.");
        var manifestHash = Hash(Json(new
        {
            package.Name,
            Files = originals.OrderBy(x => x.StableKey, StringComparer.Ordinal)
                .Select(x => new { x.FileName, x.MediaType, x.ByteLength, x.Sha256 })
        }));
        var version = new ProviderPackageVersion
        {
            ProviderId = offering.ProviderId, OfferingId = offering.Id, PackageId = package.Id,
            SeriesId = seriesId, Version = (latest?.Version ?? 0) + 1, BoundaryRevisionId = context.BoundaryRevisionId,
            PreviousVersionId = previous?.Id, ManifestHash = manifestHash, CreatedBy = actor
        };
        db.Set<ProviderPackageVersion>().Add(version);
        package.OfferingId = offering.Id;
        package.PackageVersionId = version.Id;
        package.BoundaryRevisionId = context.BoundaryRevisionId;
        offering.Revision++;
        await ProviderAuthorizationService.InvalidateAsync(db, offering, actor, "A source package version was associated", ct);
        ProviderAuthorizationStore.Audit(db, offering, package.Id, "PackageAssociated", actor, new { version.Id });
        return version;
    }
}

using System.Globalization;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;
using ProviderAuthorizationRecord = Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderAuthorizationRecord;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderImpactService
{
    public async Task<PagedResult<ProviderImpactOption>> OptionsAsync(Guid offeringId, string kind,
        int page, int pageSize, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        ValidateOptionKind(kind);
        ValidatePage(page, pageSize);
        var ids = await OptionIdsAsync(db, offering, kind, ct);
        var items = new List<ProviderImpactOption>();
        foreach (var id in ids.Skip((page - 1) * pageSize).Take(pageSize))
            items.Add(await ProjectOptionAsync(db, offering, kind, id, ct));
        return new(items, page, pageSize, ids.Count);
    }

    public async Task<ProviderImpactOption> OptionAsync(Guid offeringId, string kind, Guid id, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        ValidateOptionKind(kind);
        if (!(await OptionIdsAsync(db, offering, kind, ct)).Contains(id))
            throw new KeyNotFoundException("The selected context record is not linked to this offering.");
        return await ProjectOptionAsync(db, offering, kind, id, ct);
    }

    private static void ValidateOptionKind(string kind)
    {
        if (kind is not ("Boundary" or "HostingScope" or "Authorization" or "Package" or "Component" or "Capability"))
            throw new ArgumentException("Use Boundary, HostingScope, Authorization, Package, Component or Capability.");
    }

    private static void ValidatePage(int page, int size)
    {
        if (page < 1 || size is < 1 or > 100 || page > int.MaxValue / size)
            throw new ArgumentException("Use pages >=1 and pageSize between1 and100.");
    }

    private static async Task<IReadOnlyList<Guid>> OptionIdsAsync(AtoCopilotContext db, ProviderOffering offering, string kind, CancellationToken ct)
    {
        if (kind == "Boundary")
            return await db.Set<ProviderBoundaryRevision>().AsNoTracking()
                .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id)
                .OrderByDescending(x => x.Revision).ThenBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        if (kind == "HostingScope")
            return await db.Set<ProviderHostingScopeRevision>().AsNoTracking()
                .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id)
                .OrderByDescending(x => x.Revision).ThenBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        if (kind == "Authorization")
            return await db.Set<ProviderAuthorizationRevision>().AsNoTracking()
                .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id)
                .OrderByDescending(x => x.Revision).ThenBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        if (kind == "Package")
            return await db.Set<ProviderPackageVersion>().AsNoTracking()
                .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id)
                .OrderByDescending(x => x.Version).ThenBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);

        var linked = await db.CspPackageCandidates.AsNoTracking().Where(x => x.Type == kind
            && db.CspPackages.Any(p => p.Id == x.PackageId && p.ProviderId == offering.ProviderId
                && (p.OfferingId == offering.Id || db.Set<ProviderPackageVersion>().Any(v =>
                    v.ProviderId == offering.ProviderId && v.OfferingId == offering.Id && v.PackageId == p.Id))))
            .Select(x => x.Id).ToListAsync(ct);
        var canonical = kind == "Component"
            ? await db.CspInheritedComponents.AsNoTracking().Where(x => x.CspProfileId == offering.ProviderId).Select(x => x.Id).ToListAsync(ct)
            : await db.CspInheritedCapabilities.AsNoTracking().Where(x => x.CspInheritedComponent.CspProfileId == offering.ProviderId).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in canonical)
            if ((await ProviderPublicationGuard.GraphAsync(db, offering.ProviderId, [id], ct)).Offerings.Contains(offering.Id))
                linked.Add(id);
        return linked.Distinct().Order().ToArray();
    }

    private static async Task<ProviderImpactOption> ProjectOptionAsync(AtoCopilotContext db, ProviderOffering offering,
        string kind, Guid id, CancellationToken ct)
    {
        var name = await ImpactNameAsync(db, offering, kind, id, ct)
            ?? throw new InvalidDataException("The linked option has no accessible name.");
        long revision;
        string summary;
        ProviderImpactChange? change = null;
        if (kind == "Authorization")
        {
            var row = await db.Set<ProviderAuthorizationRevision>().AsNoTracking().SingleAsync(x =>
                x.Id == id && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct);
            revision = row.Revision;
            var source = Read<CreateProviderDecisionRequest>(row.SnapshotJson);
            var current = await db.Set<ProviderAuthorizationRecord>().AnyAsync(x => x.Id == row.RecordId
                && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id && x.CurrentRevisionId == id, ct);
            summary = $"{source.RecordKind}; {row.MetadataReviewState}; {(current ? "current" : "historical")} revision. Context only; canonical eligibility is checked during preview.";
        }
        else if (kind == "Package")
        {
            var row = await db.Set<ProviderPackageVersion>().AsNoTracking().SingleAsync(x =>
                x.Id == id && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct);
            revision = row.Version;
            summary = "Retained package version. Context only; select reviewed component or capability candidates to describe a change.";
        }
        else
        {
            var candidate = await db.CspPackageCandidates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Type == kind
                && db.CspPackages.Any(p => p.Id == x.PackageId && p.ProviderId == offering.ProviderId), ct);
            if (candidate is not null && (candidate.ReviewState == "Rejected"
                || Read<PackageCandidateResponse>(candidate.PayloadJson).PublishedRecordId.HasValue))
                return new(id, name, $"Revision {candidate.Revision}", $"{candidate.ReviewState} source candidate; context only. Select the canonical record for subsequent changes.", null);
            if (kind == "Capability" && candidate is null && !await db.ProviderCapabilityWorkingRevisions.AnyAsync(x => x.CapabilityId == id, ct))
                return new(id, name, "No working revision", "Context only; create an explicit canonical working revision before reviewing a change.", null);
            change = await ChangeAsync(db, offering.ProviderId, kind, id, ct);
            revision = change.ExpectedRevision;
            summary = candidate is not null ? $"{candidate.ReviewState} source candidate; review is separate from publication."
                : kind is "Boundary" or "HostingScope"
                    ? $"{(id == (kind == "Boundary" ? offering.CurrentBoundaryRevisionId : offering.CurrentHostingScopeRevisionId) ? "Current" : "Historical")} immutable scope revision; no coverage change is inferred."
                    : "Current canonical working material; dependency analysis does not establish coverage.";
        }
        return new(id, name, $"Revision {revision.ToString(CultureInfo.InvariantCulture)}", summary,
            change is null ? null : PresentChange(change));
    }

    private static ProviderImpactChangePresentation PresentChange(ProviderImpactChange change) =>
        new(change.Kind, change.RecordId, change.ExpectedRevision.ToString(CultureInfo.InvariantCulture), change.ProposedSnapshotHash);

    private static async Task<string?> ImpactNameAsync(AtoCopilotContext db, ProviderOffering offering, string kind, Guid id, CancellationToken ct)
    {
        if (kind is "Component" or "Capability")
        {
            var payload = await db.CspPackageCandidates.AsNoTracking().Where(x => x.Id == id && x.Type == kind
                && db.CspPackages.Any(p => p.Id == x.PackageId && p.ProviderId == offering.ProviderId))
                .Select(x => x.PayloadJson).SingleOrDefaultAsync(ct);
            if (payload is not null) return Read<PackageCandidateResponse>(payload).Name;
            return kind == "Component"
                ? await db.CspInheritedComponents.AsNoTracking().Where(x => x.Id == id && x.CspProfileId == offering.ProviderId)
                    .Select(x => x.Name).SingleOrDefaultAsync(ct)
                : await db.CspInheritedCapabilities.AsNoTracking().Where(x => x.Id == id && x.CspInheritedComponent.CspProfileId == offering.ProviderId)
                    .Select(x => x.Name).SingleOrDefaultAsync(ct);
        }
        if (kind == "Package")
            return await (from version in db.Set<ProviderPackageVersion>().AsNoTracking()
                          join package in db.CspPackages.AsNoTracking() on version.PackageId equals package.Id
                          where version.Id == id && version.OfferingId == offering.Id && version.ProviderId == offering.ProviderId
                              && package.ProviderId == offering.ProviderId
                          select package.Name).SingleOrDefaultAsync(ct);
        if (kind == "Boundary")
        {
            var json = await db.Set<ProviderBoundaryRevision>().AsNoTracking().Where(x =>
                x.Id == id && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id).Select(x => x.SnapshotJson).SingleOrDefaultAsync(ct);
            return json is null ? null : Read<CreateProviderBoundaryRequest>(json).Name;
        }
        if (kind == "HostingScope")
        {
            var json = await db.Set<ProviderHostingScopeRevision>().AsNoTracking().Where(x =>
                x.Id == id && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id).Select(x => x.SnapshotJson).SingleOrDefaultAsync(ct);
            return json is null ? null : Read<CreateProviderHostingScopeRequest>(json).Name;
        }
        if (kind == "Authorization")
        {
            var json = await db.Set<ProviderAuthorizationRevision>().AsNoTracking().Where(x =>
                x.Id == id && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id).Select(x => x.SnapshotJson).SingleOrDefaultAsync(ct);
            return json is null ? null : Read<CreateProviderDecisionRequest>(json).Reference;
        }
        return null;
    }
}

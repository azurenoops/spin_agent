using System.Data;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.PackageImports;

public sealed partial class CspPackageService
{
    public async Task<PackageStatus> ArchiveAsync(Guid id, ArchivePackageRequest request, string actor, CancellationToken ct)
    {
        Authorize();
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (actor.Length > 254 || request.ExpectedRevision < 1
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 2000)
            throw new ArgumentException("Archive requires an actor, a positive expected revision and a reason of 1-2000 characters.");
        var reason = request.Reason.Trim();
        await using var strategy = await factory.CreateDbContextAsync(ct);
        return await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var package = await LoadAsync(db, id, ct);
            if (package.SupersededAt.HasValue)
                throw new DbUpdateConcurrencyException("Superseded review work remains retained canonical source history.");
            if (package.ArchivedAt.HasValue)
            {
                if (package.Revision != request.ExpectedRevision + 1 || package.ArchivedBy != actor || package.ArchiveReason != reason)
                    throw new DbUpdateConcurrencyException("Package was already archived by a different decision.");
                return await StatusAsync(db, package, ct);
            }
            if (package.Revision != request.ExpectedRevision)
                throw new DbUpdateConcurrencyException("Package revision is stale; reload before archival.");
            if (package.PublicationState != "Unpublished" || package.ProcessingState is "Received" or "Processing"
                || package.LeaseExpiresTicks > DateTimeOffset.UtcNow.UtcTicks)
                throw new DbUpdateConcurrencyException("Only unpublished inactive packages without an active worker lease can be archived.");
            await RequireUnreferencedAsync(db, package, ct);
            package.ArchivedAt = DateTimeOffset.UtcNow;
            package.ArchivedBy = actor;
            package.ArchiveReason = reason;
            Touch(package, true);
            Audit(db, package, "Archived", actor, Json(new { request.ExpectedRevision, Reason = reason }));
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return await StatusAsync(db, package, ct);
        });
    }

    public async Task<PackageHistoryResponse> HistoryAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var package = await LoadAsync(db, id, ct);
        var audits = await db.CspPackageAudits.AsNoTracking().Where(x => x.PackageId == id).ToListAsync(ct);
        return new(await StatusAsync(db, package, ct), audits.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToArray());
    }

    private static async Task RequireUnreferencedAsync(AtoCopilotContext db, CspPackage package, CancellationToken ct)
    {
        var id = package.Id;
        var candidateIds = db.CspPackageCandidates.Where(x => x.PackageId == id).Select(x => x.Id);
        var approvalIds = db.CspPackageApprovals.Where(x => x.PackageId == id).Select(x => x.Id);
        var sourcePrefix = $"package:{id:D}/";
        if (await db.CspPackageCandidates.AnyAsync(x => x.PackageId == id && x.ReviewState == "Published", ct)
            || await db.CspPackageApprovals.AnyAsync(x => x.PackageId == id && (x.State == "Published" || x.PublicationJson != null), ct)
            || await db.CspInheritedComponents.AnyAsync(x => x.CspProfileId == package.ProviderId
                && (candidateIds.Contains(x.Id) || x.SourceArtifactReference != null && x.SourceArtifactReference.StartsWith(sourcePrefix)), ct)
            || await db.CspInheritedCapabilities.AnyAsync(x => candidateIds.Contains(x.Id), ct)
            || await db.Set<ProviderCatalogContextSnapshot>().AnyAsync(x => x.ProviderId == package.ProviderId
                && x.PackageApprovalId != null && approvalIds.Contains(x.PackageApprovalId.Value), ct))
            throw new DbUpdateConcurrencyException("Package has canonical publication references and must remain active.");

        // Retained canonical context and source citations must not be hidden by an archive,
        // even if a legacy receipt's publication flag was never advanced.
        var snapshots = await db.Set<ProviderCatalogContextSnapshot>().Where(x => x.ProviderId == package.ProviderId)
            .Select(x => x.SnapshotJson).ToListAsync(ct);
        snapshots.AddRange(await db.Set<ProviderBoundaryRevision>().Where(x => x.ProviderId == package.ProviderId)
            .Select(x => x.SnapshotJson).ToListAsync(ct));
        snapshots.AddRange(await db.Set<ProviderHostingScopeRevision>().Where(x => x.ProviderId == package.ProviderId)
            .Select(x => x.SnapshotJson).ToListAsync(ct));
        snapshots.AddRange(await db.Set<ProviderAuthorizationRevision>().Where(x => x.ProviderId == package.ProviderId)
            .Select(x => x.SnapshotJson).ToListAsync(ct));
        snapshots.AddRange(await db.Set<ProviderAuthorizationLifecycleEvent>().Where(x => x.ProviderId == package.ProviderId)
            .Select(x => x.CitationsJson).ToListAsync(ct));
        foreach (var snapshot in snapshots)
        {
            using var json = JsonDocument.Parse(snapshot);
            if (ReferencesPackage(json.RootElement, id))
                throw new DbUpdateConcurrencyException("Package is referenced by retained canonical authorization context.");
        }
    }

    private static bool ReferencesPackage(JsonElement value, Guid id) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Any(property =>
            property.Name.Equals("PackageId", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String && property.Value.TryGetGuid(out var packageId) && packageId == id
            || ReferencesPackage(property.Value, id)),
        JsonValueKind.Array => value.EnumerateArray().Any(item => ReferencesPackage(item, id)),
        _ => false
    };
}

using System.Data;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.PackageImports;

public sealed partial class CspPackageService
{
    public async Task<PackageReviewStateResponse> ReviewStateAsync(Guid id, CancellationToken ct)
    {
        Authorize();
        await using var strategy = await factory.CreateDbContextAsync(ct);
        return await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var package = await LoadAsync(db, id, ct);
            var approvals = db.CspPackageApprovals.Where(x => x.PackageId == id);
            var latest = await approvals.OrderByDescending(x => x.CreatedVersion).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            var published = approvals.Where(x => x.State == "Published" && x.PublicationJson != null);
            var publication = await published.OrderByDescending(x => x.PublishedVersion).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (latest is { CreatedVersion: 0 } && await approvals.CountAsync(ct) > 1)
                throw new PackageStateException("Saved previews predate durable ordering. Generate a new preview; previous decisions remain retained.");
            if (publication is { PublishedVersion: null } && await published.CountAsync(ct) > 1)
                throw new PackageStateException("Saved publications predate durable ordering. Contact the administrator to reconcile the retained outcomes.");
            PackagePreviewResponse? preview = null;
            var stale = false;
            if (latest is not null)
            {
                var snapshot = Read<PreviewSnapshot>(latest.SnapshotJson);
                preview = Preview(latest, snapshot.Candidates);
                if (latest.State != "Published")
                {
                    var material = await ValidateSelectionAsync(db, package, Read<PackageSelection[]>(latest.SelectionJson), ct);
                    stale = latest.State is not ("Preview" or "Approved") || latest.Revision != package.Revision
                        || latest.ExpiresAt <= DateTimeOffset.UtcNow || material.Hash != latest.PreviewHash
                        || !material.Blockers.SequenceEqual(preview.Blockers);
                }
            }
            var result = new PackageReviewStateResponse(id, package.Revision, preview, stale,
                publication?.PublicationJson is { } json ? Read<PackagePublicationResponse>(json) : null);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        });
    }

    private sealed record PreviewSnapshot(IReadOnlyList<PackageCandidateResponse> Candidates);
}

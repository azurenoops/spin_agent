using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderAuthorizationService
{
    public async Task<ScopeFitResponse> RecordScopeFitAsync(Guid id, Guid assignmentId,
        RecordScopeFitRequest request, string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = ScopeFitFacts.Require(request.Result);
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, id, ct);
        var assignment = await db.Set<ProviderHostingAssignment>()
            .FirstOrDefaultAsync(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id
                && x.Id == assignmentId, ct)
            ?? throw new KeyNotFoundException("Hosting assignment was not found on this offering.");
        var row = await db.Set<ProviderScopeFitRecord>()
            .FirstOrDefaultAsync(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id
                && x.AssignmentId == assignment.Id, ct);
        if (row is null)
        {
            row = new ProviderScopeFitRecord
            {
                ProviderId = offering.ProviderId,
                OfferingId = offering.Id,
                AssignmentId = assignment.Id,
                CreatedBy = actor
            };
            db.Set<ProviderScopeFitRecord>().Add(row);
        }
        row.Result = result;
        row.DeclaredUse = Text(request.DeclaredUse, "declared use", 2000, result != ScopeFitFacts.Unevaluated);
        row.CitedOfferingVersion = Text(request.CitedOfferingVersion, "cited offering version", 256, false);
        row.CitedAdoptedRelease = Text(request.CitedAdoptedRelease, "cited adopted release", 256, false);
        row.Rationale = Text(request.Rationale, "rationale", 2000, false);
        row.Evaluator = actor;
        row.EvaluatedAt = DateTimeOffset.UtcNow;
        row.Revision++;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    internal static ScopeFitResponse Map(ProviderScopeFitRecord row) =>
        new(row.Result, row.DeclaredUse, row.CitedOfferingVersion, row.CitedAdoptedRelease,
            row.Evaluator, row.EvaluatedAt, row.Rationale);
}

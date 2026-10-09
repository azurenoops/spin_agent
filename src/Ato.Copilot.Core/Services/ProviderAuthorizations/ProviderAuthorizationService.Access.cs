using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderAuthorizationService
{
    public async Task<PagedResult<OfferingAccessResponse>> AccessRequestsAsync(
        Guid id, int page, int pageSize, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, id, ct);
        var rows = await PageAsync(db.Set<ProviderOfferingAccessRequest>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id)
            .OrderByDescending(x => x.CreatedAt), page, pageSize, x => x, ct);
        var names = await db.RegisteredSystems.IgnoreQueryFilters().AsNoTracking()
            .Where(x => rows.Items.Select(r => r.SystemId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return new(rows.Items.Select(row => MapAccess(row, offering.Name, names.GetValueOrDefault(row.SystemId))).ToArray(),
            page, pageSize, rows.Total);
    }

    public async Task<ScopeFitResponse> EstimateAccessAsync(Guid id, Guid requestId, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, id, ct);
        var row = await RequireAccessAsync(db, offering, requestId, ct);
        return OfferingAccessFacts.Estimate(row.AuthPath, row.EnvironmentLane, row.ImpactLevel, row.DataTypes, row.Interconnect);
    }

    public Task<OfferingAccessResponse> DecideAccessAsync(Guid id, Guid requestId, string decision, string actor, CancellationToken ct) =>
        store.WriteAsync(id, $"AccessDecided:{requestId:D}", null, new { requestId, decision }, actor, async (db, _, offering) =>
        {
            var row = await RequireAccessAsync(db, offering!, requestId, ct);
            if (row.Status != "Requested")
                throw new InvalidOperationException("Only a requested access record can be approved or refused.");
            var status = decision.Equals("Approved", StringComparison.OrdinalIgnoreCase) ? "Approved"
                : decision.Equals("Refused", StringComparison.OrdinalIgnoreCase) ? "Refused"
                : throw new ArgumentException("Decision must be Approved or Refused. Approve is not allocate, associate, or inherit.");
            var estimate = OfferingAccessFacts.Estimate(row.AuthPath, row.EnvironmentLane, row.ImpactLevel, row.DataTypes, row.Interconnect);
            row.Status = status;
            row.EstimateResult = estimate.Result;
            row.EstimateRationale = estimate.Rationale;
            row.DecidedBy = actor;
            row.DecidedAt = DateTimeOffset.UtcNow;
            row.Revision++;
            return MapAccess(row, offering!.Name, null);
        }, ct);

    private static async Task<ProviderOfferingAccessRequest> RequireAccessAsync(
        Ato.Copilot.Core.Data.Context.AtoCopilotContext db, ProviderOffering offering, Guid requestId, CancellationToken ct) =>
        await db.Set<ProviderOfferingAccessRequest>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == requestId && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct)
        ?? throw new KeyNotFoundException("Access request was not found on this offering.");

    private static OfferingAccessResponse MapAccess(ProviderOfferingAccessRequest row, string offeringName, string? systemName) =>
        new(row.Id, row.OfferingId, offeringName, row.TargetTenantId, row.SystemId, systemName, row.Status, row.AuthPath,
            row.EnvironmentLane, row.ImpactLevel, row.DataTypes, row.Interconnect, row.EstimateResult, row.EstimateRationale);
}

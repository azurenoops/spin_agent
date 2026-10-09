using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderMissionService
{
    public async Task<PagedResult<OfferingAccessResponse>> AccessRequestsAsync(
        string systemId, int page, int pageSize, CancellationToken ct)
    {
        await AccessAsync(systemId, ct);
        var rows = await PageAsync(db.Set<ProviderOfferingAccessRequest>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TargetTenantId == TenantId && x.SystemId == systemId)
            .OrderByDescending(x => x.CreatedAt), page, pageSize, x => x, ct);
        var names = await db.Set<ProviderOffering>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => rows.Items.Select(r => r.OfferingId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return new(rows.Items.Select(row => new OfferingAccessResponse(row.Id, row.OfferingId,
            names.GetValueOrDefault(row.OfferingId) ?? "", row.TargetTenantId, row.SystemId, null, row.Status,
            row.AuthPath, row.EnvironmentLane, row.ImpactLevel, row.DataTypes, row.Interconnect,
            row.EstimateResult, row.EstimateRationale)).ToArray(), page, pageSize, rows.Total);
    }

    public async Task<OfferingAccessResponse> RequestAccessAsync(string systemId, RequestOfferingAccessRequest request,
        string actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await AuthorizeAsync(systemId, true, false, ct);
        var offering = await db.Set<ProviderOffering>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == request.OfferingId && x.Lifecycle != "Retired", ct)
            ?? throw new KeyNotFoundException("Offering not found.");
        var row = await db.Set<ProviderOfferingAccessRequest>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id
                && x.TargetTenantId == TenantId && x.SystemId == systemId, ct);
        if (row is null)
        {
            row = new ProviderOfferingAccessRequest
            {
                ProviderId = offering.ProviderId,
                OfferingId = offering.Id,
                TargetTenantId = TenantId,
                SystemId = systemId,
                CreatedBy = actor
            };
            db.Add(row);
        }
        var estimate = OfferingAccessFacts.Estimate(request.AuthPath, request.EnvironmentLane,
            request.ImpactLevel, request.DataTypes, request.Interconnect);
        row.Status = "Requested";
        row.AuthPath = Text(request.AuthPath, "authorization path", 16);
        row.EnvironmentLane = Text(request.EnvironmentLane, "environment lane", 16);
        row.ImpactLevel = Text(request.ImpactLevel, "impact level", 16);
        row.DataTypes = Text(request.DataTypes, "data types", 256, false);
        row.Interconnect = Text(request.Interconnect, "interconnect", 256, false);
        row.EstimateResult = estimate.Result;
        row.EstimateRationale = estimate.Rationale;
        row.DecidedBy = null;
        row.DecidedAt = null;
        row.Revision++;
        await db.SaveChangesAsync(ct);
        return await MapAccessAsync(row, ct);
    }

    public async Task<IReadOnlyList<RequestableOfferingResponse>> RequestableOfferingsAsync(string systemId, CancellationToken ct)
    {
        await AccessAsync(systemId, ct);
        return await db.Set<ProviderOffering>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Lifecycle == "Active")
            .OrderBy(x => x.Name)
            .Select(x => new RequestableOfferingResponse(x.Id, x.Name))
            .ToListAsync(ct);
    }

    private async Task<OfferingAccessResponse> MapAccessAsync(ProviderOfferingAccessRequest row, CancellationToken ct)
    {
        var name = await db.Set<ProviderOffering>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == row.OfferingId).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "";
        return new(row.Id, row.OfferingId, name, row.TargetTenantId, row.SystemId, null, row.Status, row.AuthPath,
            row.EnvironmentLane, row.ImpactLevel, row.DataTypes, row.Interconnect, row.EstimateResult, row.EstimateRationale);
    }

    private OfferingAccessResponse MapAccess(ProviderOfferingAccessRequest row) =>
        new(row.Id, row.OfferingId, "", row.TargetTenantId, row.SystemId, null, row.Status, row.AuthPath,
            row.EnvironmentLane, row.ImpactLevel, row.DataTypes, row.Interconnect, row.EstimateResult, row.EstimateRationale);
}

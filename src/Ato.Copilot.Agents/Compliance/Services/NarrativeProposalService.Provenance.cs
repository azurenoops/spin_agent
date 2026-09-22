using System.Text.Json;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Immutable source delivery provenance; RecordedAt is delivery time, not original source-change time.</summary>
public sealed record NarrativeImpactReceiptResponse(
    string Id, string ImpactId, DateTimeOffset RecordedAt, string? SourceKind, string? SourceId,
    string? SourceActor, NarrativeChangeSourceContext? SourceContext);

/// <summary>Bounded receipt history for one authorized system proposal.</summary>
public sealed record NarrativeImpactReceiptPage(
    IReadOnlyList<NarrativeImpactReceiptResponse> Items, int TotalCount, int Page, int PageSize);

public sealed partial class NarrativeProposalService
{
    public async Task<NarrativeImpactReceiptPage> GetImpactReceiptsAsync(
        string systemId, Guid proposalId, string actor, int page = 1, int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        await library.RequireSystemAsync(systemId, actor, false, cancellationToken);
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("Provide a positive page and page size between 1 and 100.");
        if (!await db.NarrativeProposals.AnyAsync(item => item.TenantId == TenantId &&
            item.RegisteredSystemId == systemId && item.Id == proposalId, cancellationToken))
            throw new KeyNotFoundException("Proposal not found.");
        var query = db.Set<NarrativeImpactReceipt>().AsNoTracking().Where(item => item.TenantId == TenantId &&
            item.RegisteredSystemId == systemId && item.NarrativeProposalId == proposalId);
        var count = await query.CountAsync(cancellationToken);
        var receipts = await query.OrderByDescending(item => item.RecordedAt).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = receipts.Select(item => new NarrativeImpactReceiptResponse(
            item.Id, item.ImpactId, new DateTimeOffset(DateTime.SpecifyKind(item.RecordedAt, DateTimeKind.Utc)),
            item.SourceKind, item.SourceId, item.SourceActor,
            item.SourceContextJson is null ? null :
                JsonSerializer.Deserialize<NarrativeChangeSourceContext>(item.SourceContextJson)
                    ?? throw new InvalidDataException("Recorded source context is invalid."))).ToArray();
        return new(items, count, page, pageSize);
    }
}

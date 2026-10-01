using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Onboarding;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services;

public static class SystemSourceReadProjection
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static SystemSourceReceipt Receipt(ISystemSourceSession source, string kind)
    {
        var analysis = source.ReviewProposalJson is null ? null :
            JsonSerializer.Deserialize<SystemSourceAnalysis>(source.ReviewProposalJson, Json);
        var applied = source.ApplyReceiptJson is not null;
        return new(source.Id, kind, source.TargetSystemId!, source.OriginalFileName, source.ContentChecksumSha256,
            source.ReviewRevision, analysis is null ? "receiving" : "confirmed", analysis?.State ?? "notAnalyzed",
            applied ? "applied" : analysis?.State == "parsed" ? "pending" : "blocked",
            applied ? "applied" : analysis?.State == "parsed" ? "reviewRequired" : "analysisIncomplete",
            analysis?.Fields ?? [], analysis?.Error);
    }

    public static async Task<IReadOnlyList<SystemSourceReceipt>> ListAsync(AtoCopilotContext db, Guid tenantId, string systemId, CancellationToken ct)
    {
        var emass = await db.Set<EmassImportSession>().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.TargetSystemId == systemId).ToListAsync(ct);
        var pdf = await db.Set<SspPdfImportSession>().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.TargetSystemId == systemId).ToListAsync(ct);
        return emass.Select(x => Receipt(x, "emass")).Concat(pdf.Select(x => Receipt(x, "ssp-pdf")))
            .OrderBy(x => x.FileName).ThenBy(x => x.SessionId).ToArray();
    }

    public static async Task<string?> ReviewedProvenanceAsync(AtoCopilotContext db, Guid tenantId, string systemId, CancellationToken ct)
    {
        var emass = await db.Set<EmassImportSession>().AsNoTracking().Where(x =>
            x.TenantId == tenantId && x.TargetSystemId == systemId && x.ApplyReceiptJson != null)
            .Select(x => x.ApplyReceiptJson!).ToListAsync(ct);
        var pdf = await db.Set<SspPdfImportSession>().AsNoTracking().Where(x =>
            x.TenantId == tenantId && x.TargetSystemId == systemId && x.ApplyReceiptJson != null)
            .Select(x => x.ApplyReceiptJson!).ToListAsync(ct);
        var receipts = emass.Concat(pdf).Select(json => JsonSerializer.Deserialize<SystemSourceApplyReceipt>(json, Json)
            ?? throw new InvalidOperationException("Source review provenance is unreadable.")).ToArray();
        return receipts.Length == 0 ? null : string.Join("\n", receipts.Select(r =>
            $"Reviewed source SHA-256: {r.SourceHash}; source revision {r.SourceRevision}; reviewed {r.ReviewedAt:O}. " +
            $"Identity retained/applied: {r.AfterName} ({r.AfterAcronym ?? "no acronym"}). " +
            "This records source-field review, not document approval or an authorization decision."));
    }
}

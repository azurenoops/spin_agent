using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.NarrativeSeeds;

/// <summary>
/// Step 7 service. Persists raw bytes via <see cref="IFileStorageProvider"/>
/// under <c>wizard/narrative-seeds/{tenantId}/{documentId}/{filename}</c>,
/// records onboarding metadata (<see cref="NarrativeSeedDocument"/>), and
/// emits an audit row + a stub indexing job that downstream feature 014 can
/// consume to surface citation-aware suggestions (FR-051..FR-054).
/// </summary>
public sealed class NarrativeSeedDocumentService : INarrativeSeedDocumentService
{
    private readonly IDbContextFactory<AtoCopilotContext> _factory;
    private readonly IFileStorageProvider _storage;
    private readonly IWizardAuditService _audit;
    private readonly IOptions<OnboardingOptions> _options;
    private readonly ILogger<NarrativeSeedDocumentService> _log;

    public NarrativeSeedDocumentService(
        IDbContextFactory<AtoCopilotContext> factory,
        IFileStorageProvider storage,
        IWizardAuditService audit,
        IOptions<OnboardingOptions> options,
        ILogger<NarrativeSeedDocumentService> log)
    {
        _factory = factory;
        _storage = storage;
        _audit = audit;
        _options = options;
        _log = log;
    }

    private static string KeyFor(Guid tenantId, Guid documentId, string filename)
        => $"wizard/narrative-seeds/{tenantId:D}/{documentId:D}/{filename}";

    public async Task<IReadOnlyList<NarrativeSeedDocument>> ListAsync(
        Guid tenantId, bool includeDeleted = false, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.NarrativeSeedDocuments.AsQueryable()
            .Where(d => d.TenantId == tenantId);
        if (!includeDeleted) q = q.Where(d => d.Status == NarrativeSeedStatus.Active);
        return await q.OrderByDescending(d => d.UpdatedAt).ToListAsync(ct);
    }

    public async Task<NarrativeSeedDocument?> GetAsync(
        Guid tenantId, Guid documentId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.NarrativeSeedDocuments
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, ct);
    }

    public async Task<NarrativeSeedUploadResult> UploadAsync(
        Guid tenantId, Guid actorUserId, string label,
        IReadOnlyList<string> tags, string originalFileName,
        string contentType, Stream content, long lengthBytes,
        CancellationToken ct = default)
    {
        var maxBytes = _options.Value.Limits.MaxNarrativeSeedBytes;
        if (lengthBytes > maxBytes)
            throw new InvalidOperationException(WizardErrorCodes.SspPdfUnreadable);

        var doc = new NarrativeSeedDocument
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Label = label,
            Tags = JsonSerializer.Serialize(tags),
            EvidenceArtifactId = Guid.Empty, // wired later via downstream evidence pipeline
            IndexingStatus = NarrativeSeedIndexingStatus.Pending,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };

        var key = KeyFor(tenantId, doc.Id, originalFileName);
        await _storage.SaveAsync(key, content, contentType, ct);

        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            db.NarrativeSeedDocuments.Add(doc);
            await db.SaveChangesAsync(ct);
        }

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.NarrativeSeedUploaded,
            $"NarrativeSeed/{doc.Id:D}", null, null,
            JsonSerializer.Serialize(new { doc.Id, doc.Label, originalFileName, lengthBytes }),
            null, Guid.NewGuid(), ct);

        _log.LogInformation(
            "NarrativeSeed uploaded TenantId={Tenant} DocId={Doc} StorageKey={Key} Bytes={Bytes}",
            tenantId, doc.Id, key, lengthBytes);

        // Indexing job is stubbed for v1 — the document is marked Pending and
        // a downstream worker (Feature 014 NarrativeSuggestions) consumes
        // active rows and updates IndexingStatus.
        return new NarrativeSeedUploadResult(doc, IndexJobId: null);
    }

    public async Task DeleteAsync(
        Guid tenantId, Guid documentId, Guid actorUserId,
        bool confirmCitations, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var doc = await db.NarrativeSeedDocuments
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, ct)
            ?? throw new KeyNotFoundException("Narrative seed not found.");

        // Citation tracking is downstream (Feature 014 — NarrativeSuggestion).
        // For v1 we treat *any* indexed document as citable; an indexed doc
        // requires the admin to acknowledge citation cascade by passing
        // confirmCitations=true. Pending or Failed indexing is safe to delete.
        if (doc.IndexingStatus == NarrativeSeedIndexingStatus.Indexed && !confirmCitations)
        {
            throw new InvalidOperationException("WIZARD_NARRATIVE_SEED_HAS_CITATIONS");
        }

        doc.Status = NarrativeSeedStatus.Deleted;
        doc.DeletedAt = DateTimeOffset.UtcNow;
        doc.UpdatedAt = doc.DeletedAt.Value;
        doc.UpdatedBy = actorUserId;
        await db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.NarrativeSeedDeleted,
            $"NarrativeSeed/{doc.Id:D}", null, null,
            JsonSerializer.Serialize(new { doc.Label, confirmCitations }),
            null, Guid.NewGuid(), ct);
    }
}

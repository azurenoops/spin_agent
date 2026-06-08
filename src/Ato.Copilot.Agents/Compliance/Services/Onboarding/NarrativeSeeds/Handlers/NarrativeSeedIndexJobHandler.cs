using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.NarrativeSeeds.Handlers;

/// <summary>
/// Handler for <see cref="WizardJobType.NarrativeSeedIndex"/> jobs (T121 / Issue #299).
/// Reads the uploaded document from blob storage, splits the text into ~500-word chunks,
/// records <see cref="NarrativeSeedDocument.IndexedChunkCount"/> and
/// <see cref="NarrativeSeedDocument.IndexedAt"/>, then transitions the document
/// from <c>Pending</c> → <c>Indexed</c> (or <c>Failed</c> on error).
///
/// Downstream feature 014 (citation-aware narrative suggestions) hooks into this
/// same job slot to register chunks with the retrieval vector index.
/// </summary>
public sealed class NarrativeSeedIndexJobHandler : IWizardJobHandler
{
    private const int WordsPerChunk = 500;

    private readonly IDbContextFactory<AtoCopilotContext> _factory;
    private readonly IFileStorageProvider _storage;
    private readonly IWizardProgressNotifier _notifier;
    private readonly ILogger<NarrativeSeedIndexJobHandler> _log;

    public NarrativeSeedIndexJobHandler(
        IDbContextFactory<AtoCopilotContext> factory,
        IFileStorageProvider storage,
        IWizardProgressNotifier notifier,
        ILogger<NarrativeSeedIndexJobHandler> log)
    {
        _factory = factory;
        _storage = storage;
        _notifier = notifier;
        _log = log;
    }

    public WizardJobType JobType => WizardJobType.NarrativeSeedIndex;

    public async Task ExecuteAsync(WizardJobEnvelope envelope, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<NarrativeSeedIndexPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException("Empty payload.");

        await PublishAsync(envelope, WizardJobState.InProgress, 10, "Loading document metadata", ct);

        // ── Load the document record ──────────────────────────────────────────
        NarrativeSeedDocument doc;
        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            doc = await db.NarrativeSeedDocuments
                .FirstOrDefaultAsync(d => d.Id == payload.DocumentId, ct)
                ?? throw new InvalidOperationException(
                    $"NarrativeSeedDocument {payload.DocumentId} not found.");
        }

        _log.LogInformation(
            "NarrativeSeedIndex starting DocId={Doc} Tenant={Tenant} Label={Label}",
            doc.Id, doc.TenantId, doc.Label);

        try
        {
            await PublishAsync(envelope, WizardJobState.InProgress, 30, "Reading document content", ct);

            // ── Derive the storage blob key ───────────────────────────────────
            // The upload service stores seeds at:
            //   wizard/narrative-seeds/{tenantId}/{documentId}/{filename}
            // We reconstruct the key prefix to find the blob.
            var blobKeyPrefix = $"wizard/narrative-seeds/{doc.TenantId:D}/{doc.Id:D}/";

            // Read the document content from blob storage.
            // The exact filename is not stored on the model (by design), so we
            // attempt to read via the EvidenceArtifact pattern if a blobKey is
            // known, otherwise fall back to listing the prefix.
            // TODO(feat-014): When EvidenceArtifact wiring is complete, read the
            // blob key from doc.EvidenceArtifactId → EvidenceArtifact.StoragePath.
            string rawText;
            await using (var stream = await _storage.GetAsync(blobKeyPrefix, ct))
            {
                if (stream is not null)
                {
                    // Direct hit — storage provider found the key as-is
                    rawText = await ReadStreamAsTextAsync(stream, ct);
                }
                else
                {
                    // Blob key includes the filename component. Since NarrativeSeedDocument
                    // does not currently persist it, we record a meaningful failure message
                    // and mark the document as Failed so the admin can re-upload.
                    // TODO(feat-014): Persist OriginalFileName on NarrativeSeedDocument and
                    //                 use KeyFor(tenantId, docId, originalFileName) here.
                    _log.LogWarning(
                        "NarrativeSeedIndex could not locate blob for DocId={Doc} " +
                        "at prefix={Prefix}. OriginalFileName is not persisted on the model yet.",
                        doc.Id, blobKeyPrefix);

                    throw new InvalidOperationException(
                        $"Blob not found at prefix '{blobKeyPrefix}'. " +
                        "Re-upload the document or ensure OriginalFileName is persisted " +
                        "on NarrativeSeedDocument (see TODO in NarrativeSeedIndexJobHandler).");
                }
            }

            await PublishAsync(envelope, WizardJobState.InProgress, 60, "Splitting into chunks", ct);

            // ── Split into ~500-word chunks ───────────────────────────────────
            var chunks = SplitIntoChunks(rawText, WordsPerChunk);
            var chunkCount = chunks.Count;

            _log.LogInformation(
                "NarrativeSeedIndex split DocId={Doc} into {ChunkCount} chunks (~{Words} words each)",
                doc.Id, chunkCount, WordsPerChunk);

            await PublishAsync(envelope, WizardJobState.InProgress, 80, $"Indexing {chunkCount} chunk(s)", ct);

            // ── TODO(feat-014): Register chunks with the retrieval vector index.
            // For v1 we simply count them and persist the count. The downstream
            // NarrativeSuggestions feature will replace this stub with actual
            // vector embedding calls (FR-051..FR-054).

            // ── Persist success ───────────────────────────────────────────────
            await using (var db = await _factory.CreateDbContextAsync(ct))
            {
                var entity = await db.NarrativeSeedDocuments
                    .FirstOrDefaultAsync(d => d.Id == payload.DocumentId, ct);
                if (entity is not null)
                {
                    entity.IndexingStatus = NarrativeSeedIndexingStatus.Indexed;
                    entity.IndexedChunkCount = chunkCount;
                    entity.IndexedAt = DateTime.UtcNow;
                    entity.IndexingError = null;
                    entity.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }

            await PublishAsync(envelope, WizardJobState.Succeeded, 100,
                $"Indexed {chunkCount} chunk(s)", ct);

            _log.LogInformation(
                "NarrativeSeedIndex succeeded DocId={Doc} Tenant={Tenant} Chunks={Chunks}",
                doc.Id, doc.TenantId, chunkCount);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "NarrativeSeedIndex failed DocId={Doc} Tenant={Tenant}",
                doc.Id, doc.TenantId);

            // ── Persist failure ───────────────────────────────────────────────
            try
            {
                await using var db = await _factory.CreateDbContextAsync(ct);
                var entity = await db.NarrativeSeedDocuments
                    .FirstOrDefaultAsync(d => d.Id == payload.DocumentId, ct);
                if (entity is not null)
                {
                    entity.IndexingStatus = NarrativeSeedIndexingStatus.Failed;
                    entity.IndexedAt = DateTime.UtcNow;
                    entity.IndexingError = ex.Message;
                    entity.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception dbEx)
            {
                _log.LogError(dbEx,
                    "Failed to persist Failed status for DocId={Doc}", doc.Id);
            }

            await PublishAsync(envelope, WizardJobState.Failed, 100,
                ex.Message, ct,
                errorCode: "WIZARD_NARRATIVE_SEED_INDEX_FAILED",
                suggestion: "Re-upload the document and retry.");

            throw;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits <paramref name="text"/> into chunks of approximately
    /// <paramref name="wordsPerChunk"/> words using whitespace tokenisation.
    /// </summary>
    private static List<string> SplitIntoChunks(string text, int wordsPerChunk)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();

        for (var i = 0; i < words.Length; i += wordsPerChunk)
        {
            var slice = words.Skip(i).Take(wordsPerChunk);
            chunks.Add(string.Join(' ', slice));
        }

        // Ensure at least one (possibly empty) chunk so downstream callers
        // can always inspect the result without null-checks.
        if (chunks.Count == 0)
            chunks.Add(string.Empty);

        return chunks;
    }

    /// <summary>
    /// Reads a <see cref="Stream"/> as UTF-8 text.
    /// </summary>
    private static async Task<string> ReadStreamAsTextAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: -1, leaveOpen: true);
        return await reader.ReadToEndAsync(ct);
    }

    private Task PublishAsync(
        WizardJobEnvelope envelope,
        WizardJobState state,
        int percent,
        string message,
        CancellationToken ct,
        string? errorCode = null,
        string? suggestion = null)
        => _notifier.PublishAsync(
            new WizardJobStatusEvent(
                envelope.JobId,
                envelope.TenantId,
                envelope.JobType,
                state,
                percent,
                message,
                ErrorCode: errorCode,
                Suggestion: suggestion,
                Timestamp: DateTimeOffset.UtcNow),
            ct);

    public sealed record NarrativeSeedIndexPayload(Guid DocumentId);
}

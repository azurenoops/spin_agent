using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.PackageImports.CspPackageService;

namespace Ato.Copilot.Core.Services.PackageImports;

public sealed partial class CspPackageProcessor
{
    private async Task<byte[]> RetainedBytesAsync(CspPackageEntry entry, CancellationToken ct)
    {
        if (entry.StorageKey is null || entry.ByteLength < 0 || entry.ByteLength > MaxBytes)
            throw new IOException("Retained source metadata is missing or exceeds the per-entry limit.");
        await using var stream = await storage.GetAsync(entry.StorageKey, ct)
            ?? throw new IOException("Retained package bytes are missing.");
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (memory.Length + read > entry.ByteLength)
                throw new IOException("Retained artifact length exceeds its immutable manifest.");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var bytes = memory.ToArray();
        if (bytes.LongLength != entry.ByteLength || !Hash(bytes).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Retained artifact integrity check failed.");
        return bytes;
    }

    private async Task<CspPackageAnalysisResumeRequest?> ResumeRequestAsync(Guid id,
        IReadOnlyList<CspPackageAnalysisInput> originals, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var package = await db.CspPackages.SingleAsync(x => x.Id == id, ct);
        var retained = await db.CspPackageEntries.Where(x => x.PackageId == id).ToDictionaryAsync(x => x.Id, ct);
        if (package.AnalysisCheckpointJson is null)
        {
            if (retained.Values.Any(x => x.Status != "Pending"))
                throw new InvalidDataException("This package lacks a resumable analysis checkpoint. Retain its sources and submit a new package with a new idempotency key.");
            return null;
        }
        var checkpoint = Read<CspPackageAnalysisCheckpoint>(package.AnalysisCheckpointJson);
        var hydrated = new List<CspPackageAnalyzedEntry>();
        var retry = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = 0;
        var limits = new CspPackageAnalysisLimits();
        foreach (var entry in checkpoint.Entries)
        {
            var artifactId = entry.ParentKey is null && Guid.TryParse(entry.ArtifactId, out var root)
                ? root : StableId(id, $"entry/{entry.Key}");
            if (!retained.TryGetValue(artifactId, out var stored) || !checkpoint.Progress.TryGetValue(entry.Key, out var progress))
                throw new InvalidDataException("Analyzer checkpoint is missing its retained artifact or progress.");
            byte[]? bytes = null;
            if (progress.ContentSha256 is not null)
            {
                totalBytes = checked(totalBytes + stored.ByteLength);
                if (totalBytes > limits.MaxUploadedBytes + limits.MaxExpandedBytes)
                    throw new IOException("Retained checkpoint exceeds the package hydration budget.");
                bytes = stored.IsOriginal
                    ? originals.Single(x => x.ArtifactId == stored.Id.ToString("D")).Content
                    : await RetainedBytesAsync(stored, ct);
                if (!Hash(bytes).Equals(progress.ContentSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Checkpoint content does not match the immutable retained artifact.");
            }
            var hydratedEntry = entry with { Content = bytes };
            if (stored.Status == "Excluded")
            {
                if (string.IsNullOrWhiteSpace(stored.ExclusionReason))
                    throw new InvalidDataException("Retained exclusion is missing its rationale.");
                hydratedEntry = hydratedEntry with
                {
                    Status = CspPackageEntryStatus.Excluded, AnalysisComplete = true,
                    ReasonCode = "RETAINED_EXCLUSION", Reason = stored.ExclusionReason
                };
            }
            else if (entry.Status == CspPackageEntryStatus.Processed && entry.MediaType == "application/pdf")
            {
                // PDF page semantics remain independent of successfully enumerated attachment children.
                if (!progress.PdfAttachmentsEnumerated || !entry.AnalysisComplete && checkpoint.Segments.Any(segment =>
                    segment.EntryKey == entry.Key && !progress.PdfAnalysisViews.ContainsKey(segment.Key)
                        && !progress.SemanticallyAnalyzedSegmentKeys.Contains(segment.Key)))
                    retry.Add(entry.Key);
            }
            else if (stored.Status != "Processed" && entry.Status != CspPackageEntryStatus.Excluded
                && (entry.Status != CspPackageEntryStatus.Processed || !entry.AnalysisComplete))
                retry.Add(entry.Key);
            hydrated.Add(hydratedEntry);
        }
        if (checkpoint.AutomaticContinuationEntryKeys.Count > 0)
            retry.IntersectWith(checkpoint.AutomaticContinuationEntryKeys);
        return new(originals, checkpoint with { Entries = hydrated }, retry);
    }
}

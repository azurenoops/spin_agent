using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.PackageImports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.PackageImports;

public sealed partial class CspPackageService(
    IDbContextFactory<AtoCopilotContext> factory, IFileStorageProvider storage,
    ICspPackageAnalyzer analyzer, ITenantContext tenant, ILogger<CspPackageService> logger) : ICspPackageService
{
    internal const long MaxBytes = 50L * 1024 * 1024;
    internal static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    internal static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    internal static Guid StableId(Guid package, string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{package:D}/{key}"))[..16]);
    internal static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException("Persisted package data is invalid.");
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value);

    private void Authorize()
    {
        if (!tenant.IsCspAdmin || tenant.ImpersonatedTenantId is not null)
        {
            logger.LogWarning("CspPackage.AccessDenied in non-provider or impersonated context");
            throw new UnauthorizedAccessException("Ordinary CSP administrator access is required; leave support or organization context.");
        }
    }

    private async Task<Guid> ProviderAsync(AtoCopilotContext db, CancellationToken ct)
    {
        Authorize();
        return await db.CspProfiles.Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Provider profile was not found.");
    }

    private async Task<CspPackage> LoadAsync(AtoCopilotContext db, Guid id, CancellationToken ct)
    {
        var provider = await ProviderAsync(db, ct);
        return await db.CspPackages.SingleOrDefaultAsync(x => x.Id == id && x.ProviderId == provider, ct)
            ?? throw new KeyNotFoundException("Package was not found in the current provider.");
    }

    internal static void Audit(AtoCopilotContext db, CspPackage package, string action, string actor, string detail = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (actor.Length > 254) throw new ArgumentException("Actor is too long.");
        db.CspPackageAudits.Add(new() { PackageId = package.Id, Action = action, Actor = actor, Revision = package.Revision, Detail = detail });
    }

    internal static void Touch(CspPackage package, bool revise)
    {
        package.Version++;
        if (revise) package.Revision++;
        package.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<T> ChangeAsync<T>(Guid id, string action, string actor,
        Func<AtoCopilotContext, CspPackage, Task<T>> change, CancellationToken ct)
    {
        Authorize();
        await using var strategy = await factory.CreateDbContextAsync(ct);
        return await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var package = await LoadAsync(db, id, ct);
            var value = await change(db, package);
            Touch(package, false);
            Audit(db, package, action, actor);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            logger.LogInformation("CspPackage.{Action} package={PackageId} revision={Revision} actor={Actor}",
                action, id, package.Revision, actor);
            return value;
        });
    }

    public Task<PackageStatus> ReceiveAsync(Guid providerId, string? key, string name,
        IReadOnlyList<PackageUpload> files, string actor, CancellationToken ct) =>
        ReceiveCoreAsync(providerId, key, name, files, null, actor, ct);

    public Task<PackageStatus> ReceiveForOfferingAsync(Guid providerId, string key, string name,
        IReadOnlyList<PackageUpload> files, PackageOfferingContext context, string actor, CancellationToken ct) =>
        ReceiveCoreAsync(providerId, key, name, files, context, actor, ct);

    private async Task<PackageStatus> ReceiveCoreAsync(Guid providerId, string? key, string name,
        IReadOnlyList<PackageUpload> files, PackageOfferingContext? context, string actor, CancellationToken ct)
    {
        Authorize();
        if (files.Count is < 1 or > 1000) throw new ArgumentException("Supply 1-1000 source files.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256) throw new ArgumentException("Package name must contain 1-256 characters.");
        var buffers = new List<byte[]>();
        long total = 0;
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.FileName) || file.FileName.Length > 512 || file.MediaType.Length > 256)
                throw new ArgumentException("Source name or media type is invalid.");
            using var memory = new MemoryStream();
            var chunk = new byte[81920];
            int count;
            while ((count = await file.Content.ReadAsync(chunk, ct)) > 0)
            {
                total += count;
                if (total > MaxBytes) throw new PackageLimitException("Uploads exceed the total 50 MiB package limit.");
                await memory.WriteAsync(chunk.AsMemory(0, count), ct);
            }
            buffers.Add(memory.ToArray());
        }
        var fingerprint = Hash(Json(new { name, Files = files.Select((f, i) => new { f.FileName, f.MediaType, Hash = Hash(buffers[i]) }) }));
        if (context is not null) fingerprint = Hash(Json(new { Content = fingerprint, Context = context }));
        key = string.IsNullOrWhiteSpace(key) ? $"content-{fingerprint}" : key;
        ValidateKey(key);
        await using var strategy = await factory.CreateDbContextAsync(ct);
        return await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            if (await ProviderAsync(db, ct) != providerId) throw new UnauthorizedAccessException("Provider does not match the current scope.");
            await using var transaction = context is not null && db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var existing = await db.CspPackages.SingleOrDefaultAsync(x => x.ProviderId == providerId && x.IdempotencyKey == key, ct);
            if (existing is not null)
            {
                if (existing.ContentHash != fingerprint) throw new DbUpdateConcurrencyException("Idempotency key was already used for different package content.");
                Audit(db, existing, "ReceiptReplayed", actor);
                await db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
                return await StatusAsync(db, existing, ct);
            }
            var package = new CspPackage { ProviderId = providerId, Name = name, IdempotencyKey = key, ContentHash = fingerprint, CreatedBy = actor };
            var entries = files.Select((file, index) => new CspPackageEntry
            {
                PackageId = package.Id, StableKey = Hash($"original/{index}"), IsOriginal = true, FileName = file.FileName,
                ArchivePath = file.FileName, MediaType = file.MediaType, ByteLength = buffers[index].LongLength, Sha256 = Hash(buffers[index])
            }).ToArray();
            db.CspPackages.Add(package);
            db.CspPackageEntries.AddRange(entries);
            if (context is not null)
                await AssociateOfferingAsync(db, package, context, actor, ct);
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                entry.StorageKey = $"csp-packages/{providerId:N}/{package.Id:N}/{entry.Id:N}";
                using var content = new MemoryStream(buffers[index], false);
                await storage.SaveAsync(entry.StorageKey, content, entry.MediaType, ct);
            }
            Audit(db, package, "Received", actor);
            try
            {
                await db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException error)
            {
                if (transaction is not null) await transaction.RollbackAsync(ct);
                await using var winnerDb = await factory.CreateDbContextAsync(ct);
                existing = await winnerDb.CspPackages.SingleOrDefaultAsync(x => x.ProviderId == providerId && x.IdempotencyKey == key, ct);
                if (existing is null) throw;
                if (existing.Id != package.Id)
                    foreach (var entry in entries)
                        await storage.DeleteAsync(entry.StorageKey!, ct);
                if (existing.ContentHash != fingerprint) throw new DbUpdateConcurrencyException("Idempotency key was already used for different package content.");
                logger.LogWarning(error, "CspPackage.ConcurrentReceiptRecovered package={PackageId}", existing.Id);
                return await StatusAsync(winnerDb, existing, ct);
            }
            return await StatusAsync(db, package, ct);
        });
    }

    internal static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new ArgumentException("A stable idempotency key of 1-100 characters is required.");
    }

    private static (int Page, int Size, int Skip) PageBounds(int page, int size)
    {
        if (page < 1 || page > 1000000 || size < 1 || size > 100)
            throw new ArgumentException("page must be 1-1000000 and pageSize 1-100.");
        return (page, size, checked((page - 1) * size));
    }

    public async Task<PagedResult<PackageStatus>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        Authorize();
        var bounds = PageBounds(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var provider = await ProviderAsync(db, ct);
        var query = db.CspPackages.Where(x => x.ProviderId == provider);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Id).Skip(bounds.Skip).Take(bounds.Size).ToListAsync(ct);
        var results = new List<PackageStatus>();
        foreach (var row in rows) results.Add(await StatusAsync(db, row, ct));
        return new(results, page, pageSize, total);
    }

    public async Task<PackageStatus> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await StatusAsync(db, await LoadAsync(db, id, ct), ct);
    }

    internal static async Task<PackageStatus> StatusAsync(AtoCopilotContext db, CspPackage row, CancellationToken ct)
    {
        var states = await db.CspPackageEntries.Where(x => x.PackageId == row.Id).Select(x => x.Status).ToListAsync(ct);
        return Status(row, states);
    }

    internal static PackageStatus Status(CspPackage row, IReadOnlyList<string> states)
    {
        int Count(string state) => states.Count(x => x == state);
        CspPackageAnalysisProgress? progress = null;
        if (row.AnalysisCheckpointJson is not null)
        {
            using var checkpoint = JsonDocument.Parse(row.AnalysisCheckpointJson);
            if (checkpoint.RootElement.TryGetProperty(nameof(CspPackageAnalysisCheckpoint.AnalysisProgress), out var saved))
                progress = saved.Deserialize<CspPackageAnalysisProgress>();
            if (progress is not null)
                progress = progress with { ContinuingAutomatically = progress.ContinuingAutomatically
                    && row.ProcessingState is "Received" or "Processing" };
        }
        return new(row.Id, row.Id, row.Name, row.Revision, row.ProcessingState, row.PublicationState,
            new(states.Count, Count("Pending"), Count("Processed"), Count("Unsupported"), Count("Unreadable"), Count("Failed"), Count("Excluded")),
            row.LastError, row.CreatedAt, row.UpdatedAt,
            row.OfferingId.HasValue && row.PackageVersionId.HasValue && row.BoundaryRevisionId.HasValue
                ? new(row.OfferingId.Value, row.PackageVersionId.Value, row.BoundaryRevisionId.Value) : null, progress);
    }

    internal static PackageCandidateResponse Candidate(CspPackageCandidate row) =>
        Read<PackageCandidateResponse>(row.PayloadJson) with { CandidateId = row.Id, Revision = row.Revision, ReviewState = row.ReviewState };
    private static PackageEntryResponse Entry(CspPackageEntry entry, int count) =>
        new(entry.Id, entry.Id, entry.FileName, entry.ArchivePath, entry.MediaType, entry.ByteLength, entry.Sha256,
            entry.Status, entry.Reason, count, entry.ExclusionReason, entry.Revision);

    public async Task<PagedResult<PackageEntryResponse>> EntriesAsync(Guid id, int page, int pageSize, CancellationToken ct)
    {
        Authorize();
        var bounds = PageBounds(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        await LoadAsync(db, id, ct);
        var query = db.CspPackageEntries.Where(x => x.PackageId == id);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Id).Skip(bounds.Skip).Take(bounds.Size).ToListAsync(ct);
        var candidates = (await db.CspPackageCandidates.Where(x => x.PackageId == id).ToListAsync(ct)).Select(Candidate).ToArray();
        return new(rows.Select(x => Entry(x, candidates.Count(c => c.Citations.Any(s => s.ArtifactId == x.Id)))).ToArray(), page, pageSize, total);
    }

    public async Task<PagedResult<PackageCandidateResponse>> CandidatesAsync(Guid id, int page, int pageSize,
        string? type, string? reviewState, CancellationToken ct)
    {
        Authorize();
        var bounds = PageBounds(page, pageSize);
        if (!string.IsNullOrWhiteSpace(type) && !Enum.GetNames<CspPackageCandidateKind>().Contains(type, StringComparer.Ordinal))
            throw new ArgumentException("Candidate type filter is invalid.");
        if (!string.IsNullOrWhiteSpace(reviewState) && reviewState is not ("NeedsReview" or "Reviewed" or "Rejected" or "Approved" or "Published"))
            throw new ArgumentException("Candidate review filter is invalid.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var package = await LoadAsync(db, id, ct);
        var query = db.CspPackageCandidates.Where(x => x.PackageId == id);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(x => x.Type == type);
        if (!string.IsNullOrWhiteSpace(reviewState)) query = query.Where(x => x.ReviewState == reviewState);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Id).Skip(bounds.Skip).Take(bounds.Size).ToListAsync(ct);
        return new(await CandidatesWithRetainedClaimsAsync(db, package, rows, ct), page, pageSize, total);
    }

    internal static Dictionary<string, Guid> ArtifactIds(Guid packageId, IReadOnlyList<CspPackageAnalyzedEntry> entries, ISet<Guid> roots) =>
        entries.ToDictionary(entry => entry.Key, entry =>
            entry.ParentKey is null && Guid.TryParse(entry.ArtifactId, out var root) && roots.Contains(root)
                ? root : StableId(packageId, $"entry/{entry.Key}"), StringComparer.Ordinal);

    private async Task<PackageCandidateResponse[]> CandidatesWithRetainedClaimsAsync(
        AtoCopilotContext db, CspPackage package, IReadOnlyList<CspPackageCandidate> rows, CancellationToken ct)
    {
        var candidates = rows.Select(Candidate).ToArray();
        if (package.AnalysisCheckpointJson is null || candidates.Length == 0)
            return candidates;

        // Legacy payloads omit claims/profile provenance; recover exact retained evidence without writing on read.
        var checkpoint = Read<CspPackageAnalysisCheckpoint>(package.AnalysisCheckpointJson);
        var roots = (await db.CspPackageEntries.Where(x => x.PackageId == package.Id && x.IsOriginal).Select(x => x.Id).ToListAsync(ct)).ToHashSet();
        var artifacts = ArtifactIds(package.Id, checkpoint.Entries, roots);
        var entries = checkpoint.Entries.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var drafts = checkpoint.Candidates.ToDictionary(x => Hash(x.Key));
        for (var i = 0; i < candidates.Length; i++)
        {
            var row = rows[i];
            var candidate = candidates[i];
            if (!drafts.TryGetValue(row.StableKey, out var draft))
                continue;
            if (StableId(package.Id, $"candidate/{draft.Key}") != row.Id || draft.Kind.ToString() != row.Type
                || candidate.Type != row.Type
                || !candidate.Citations.Select(x => ((Guid?)x.ArtifactId, x.ArchivePath, x.Locator, x.Quote))
                    .SequenceEqual(draft.Citations.Select(x => (
                        artifacts.TryGetValue(x.EntryKey, out var artifact) ? artifact : (Guid?)null, x.ArchivePath, x.Locator, x.Quote))))
            {
                logger.LogWarning("CspPackage.ClaimRecoverySkipped candidate={CandidateId}: retained identity, kind or citations do not match", row.Id);
                continue;
            }
            var profile = candidate.AnalysisProfileVersion;
            if (profile is null && draft.Citations.Count > 0 && draft.Citations.All(x => entries.ContainsKey(x.EntryKey)))
                profile = draft.Citations.Select(x => entries[x.EntryKey].AnalysisProfileVersion)
                    .Append(checkpoint.AnalysisProfileVersion).Min();
            candidates[i] = candidate with { Claim = candidate.Claim ?? draft.Claim, AnalysisProfileVersion = profile };
        }
        return candidates;
    }

    public async Task<PackageContent> ContentAsync(Guid id, Guid artifactId, string actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var package = await LoadAsync(db, id, ct);
        var entry = await db.CspPackageEntries.SingleOrDefaultAsync(x => x.PackageId == id && x.Id == artifactId, ct)
            ?? throw new KeyNotFoundException("Artifact was not found.");
        if (entry.StorageKey is null) throw new KeyNotFoundException("Artifact bytes were not readable; inspect coverage details.");
        Audit(db, package, "SourceRead", actor, artifactId.ToString());
        await db.SaveChangesAsync(ct);
        var stream = await storage.GetAsync(entry.StorageKey, ct)
            ?? throw new KeyNotFoundException("Stored artifact is unavailable; contact the provider administrator.");
        return new(entry.FileName, "application/octet-stream", stream);
    }
}

public sealed class PackageLimitException(string message) : Exception(message);
public sealed class PackageStateException(string message) : InvalidOperationException(message);
public sealed class PackageAnalysisException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

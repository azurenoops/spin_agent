using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static Ato.Copilot.Core.Services.PackageImports.CspPackageService;

namespace Ato.Copilot.Core.Services.PackageImports;

/// <summary>Trusted background-only durable queue consumer; never registered as an HTTP service.</summary>
public sealed partial class CspPackageProcessor(IDbContextFactory<AtoCopilotContext> factory,
    IFileStorageProvider storage, ICspPackageAnalyzer analyzer, ILogger logger)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(15);

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow.UtcTicks;
        var id = await db.CspPackages.Where(x => x.ProcessingState == "Received"
            || (x.ProcessingState == "Processing" && x.LeaseExpiresTicks < now))
            .OrderBy(x => x.LeaseExpiresTicks).ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        return id.HasValue && await ProcessAsync(id.Value, ct);
    }

    public async Task<bool> ProcessAsync(Guid id, CancellationToken ct)
    {
        var lease = Guid.NewGuid();
        if (!await ClaimAsync(id, lease, ct)) return false;
        try
        {
            using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeat = RenewLeaseAsync(id, lease, heartbeatStop.Token);
            CspPackageAnalysisResult output;
            try
            {
                var inputs = await InputsAsync(id, ct);
                var resume = await ResumeRequestAsync(id, inputs, ct);
                output = resume is null
                    ? await analyzer.AnalyzeAsync(inputs, ct)
                    : await analyzer.ResumeAsync(resume, ct);
            }
            finally
            {
                await heartbeatStop.CancelAsync();
                try { await heartbeat; }
                catch (OperationCanceledException) when (heartbeatStop.IsCancellationRequested) { }
            }
            await CheckpointAsync(id, lease, output, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("CspPackage.ProcessingInterrupted package={PackageId}; lease permits restart recovery", id);
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning("CspPackage.LeaseLost package={PackageId}; stale worker output discarded", id);
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "CspPackage.ProcessingFailed package={PackageId}", id);
            var message = exception switch
            {
                InvalidDataException => exception.Message,
                IOException => "Source storage failed; retry after restoring access.",
                _ => "Package analysis failed; inspect retained sources and retry."
            };
            await FailAsync(id, lease, message, ct);
            return true;
        }
    }

    private async Task RenewLeaseAsync(Guid id, Guid lease, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            await using var strategy = await factory.CreateDbContextAsync(ct);
            await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                var row = await FencedAsync(db, id, lease, ct);
                row.LeaseExpiresTicks = DateTimeOffset.UtcNow.Add(LeaseDuration).UtcTicks;
                Touch(row, false);
                await db.SaveChangesAsync(ct);
            });
        }
    }

    private async Task<bool> ClaimAsync(Guid id, Guid lease, CancellationToken ct)
    {
        await using var strategy = await factory.CreateDbContextAsync(ct);
        return await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var row = await db.CspPackages.SingleAsync(x => x.Id == id, ct);
            if (row.ProcessingState != "Received"
                && !(row.ProcessingState == "Processing" && row.LeaseExpiresTicks < DateTimeOffset.UtcNow.UtcTicks))
                return false;
            row.ProcessingState = "Processing";
            row.LeaseId = lease;
            row.LeaseExpiresTicks = DateTimeOffset.UtcNow.Add(LeaseDuration).UtcTicks;
            Touch(row, false);
            Audit(db, row, "ProcessingClaimed", "package-worker");
            try { await db.SaveChangesAsync(ct); return true; }
            catch (DbUpdateConcurrencyException) { return false; }
        });
    }

    private async Task<IReadOnlyList<CspPackageAnalysisInput>> InputsAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var roots = await db.CspPackageEntries.Where(x => x.PackageId == id && x.IsOriginal)
            .OrderBy(x => x.StableKey).ToListAsync(ct);
        var inputs = new List<CspPackageAnalysisInput>();
        foreach (var root in roots)
        {
            var bytes = await RetainedBytesAsync(root, ct);
            inputs.Add(new(root.Id.ToString("D"), root.FileName, root.MediaType, bytes));
        }
        return inputs;
    }

    private async Task CheckpointAsync(Guid id, Guid lease, CspPackageAnalysisResult output, CancellationToken ct)
    {
        var rootIds = new HashSet<Guid>();
        await using (var observed = await factory.CreateDbContextAsync(ct))
            rootIds.UnionWith(await observed.CspPackageEntries.Where(x => x.PackageId == id && x.IsOriginal).Select(x => x.Id).ToListAsync(ct));
        var artifacts = ArtifactIds(id, output.Entries, rootIds);
        foreach (var entry in output.Entries.Where(x => x.Content is not null && !rootIds.Contains(artifacts[x.Key])))
        {
            using var bytes = new MemoryStream(entry.Content!, false);
            await storage.SaveAsync($"csp-package-entries/{id:N}/{artifacts[entry.Key]:N}/{Hash(entry.Content!)}", bytes, entry.MediaType, ct);
        }
        await using var strategy = await factory.CreateDbContextAsync(ct);
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var row = await FencedAsync(db, id, lease, ct);
            var priorEntries = await db.CspPackageEntries.Where(x => x.PackageId == id).ToDictionaryAsync(x => x.Id, ct);
            var needsAttention = output.NeedsAttention;
            foreach (var entry in output.Entries)
            {
                var artifact = artifacts[entry.Key];
                var exists = priorEntries.TryGetValue(artifact, out var stored);
                if (exists && stored!.Status is "Processed" or "Excluded")
                {
                    if (stored.Status == "Processed" && entry.Status == CspPackageEntryStatus.Processed
                        && output.Entries.Any(child => child.ParentKey == entry.Key) && stored.Reason != entry.Reason)
                    {
                        stored.Reason = entry.Reason;
                        stored.Revision++;
                    }
                    continue;
                }
                stored ??= new CspPackageEntry { Id = artifact, PackageId = id, StableKey = Hash(entry.Key) };
                if (!exists) db.CspPackageEntries.Add(stored);
                stored.ArchivePath = entry.ArchivePath;
                stored.OriginalEntryId = entry.ParentKey is null ? null : artifacts[entry.ParentKey];
                stored.FileName = stored.IsOriginal ? stored.FileName : Path.GetFileName(entry.ArchivePath);
                if (!stored.IsOriginal) stored.MediaType = entry.MediaType;
                stored.ByteLength = entry.ExpandedBytes;
                stored.Status = entry.Status.ToString();
                stored.Reason = entry.Reason;
                if (entry.Status == CspPackageEntryStatus.Excluded)
                {
                    if (!entry.AnalysisComplete)
                    {
                        stored.Status = "Unsupported";
                        needsAttention = true;
                    }
                    else if (string.IsNullOrWhiteSpace(entry.Reason))
                    {
                        stored.Status = "Failed";
                        stored.Reason = "Analysis excluded this entry without a reason; explicit coverage review is required.";
                        needsAttention = true;
                    }
                    else stored.ExclusionReason = entry.Reason;
                }
                if (!entry.AnalysisComplete && stored.Status == "Processed"
                    && !output.Entries.Any(child => child.ParentKey == entry.Key))
                {
                    stored.Status = "Unsupported";
                    stored.Reason = entry.Reason ?? "Semantic analysis is incomplete; review coverage and explicitly exclude if appropriate.";
                    needsAttention = true;
                }
                if (!stored.IsOriginal && entry.Content is { } content)
                {
                    stored.Sha256 = Hash(content);
                    stored.StorageKey = $"csp-package-entries/{id:N}/{artifact:N}/{stored.Sha256}";
                }
                stored.SegmentsJson = Json(output.Segments.Where(x => x.EntryKey == entry.Key).ToArray());
                stored.Revision++;
            }
            var existing = await db.CspPackageCandidates.Where(x => x.PackageId == id).Select(x => x.StableKey).ToListAsync(ct);
            var ids = output.Candidates.ToDictionary(x => x.Key, x => StableId(id, $"candidate/{x.Key}"));
            var publishedComponents = await db.CspInheritedComponents.Where(x => x.CspProfileId == row.ProviderId
                && x.Status == CspInheritedComponentStatus.Published).ToListAsync(ct);
            var publishedCapabilities = await db.CspInheritedCapabilities.Where(x => x.CspInheritedComponent.CspProfileId == row.ProviderId
                && x.CspInheritedComponent.Status == CspInheritedComponentStatus.Published
                && db.ProviderCapabilityReleases.Any(r => r.CapabilityId == x.Id)).ToListAsync(ct);
            foreach (var draft in output.Candidates.Where(x => !existing.Contains(Hash(x.Key))))
            {
                var candidateId = ids[draft.Key];
                var citations = draft.Citations.Select(c => new PackageCitation(artifacts[c.EntryKey], c.ArchivePath, c.Locator, c.Quote)).ToArray();
                var publishedDuplicates = draft.Kind == CspPackageCandidateKind.Component
                    ? publishedComponents.Where(x => x.Name.Equals(draft.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(x => new PackageDuplicate(x.Id, x.Name, "Component", true)).ToArray()
                    : draft.Kind == CspPackageCandidateKind.Capability
                        ? publishedCapabilities.Where(x => x.Name.Equals(draft.Name, StringComparison.OrdinalIgnoreCase))
                            .Select(x => new PackageDuplicate(x.Id, x.Name, "Capability", true)).ToArray()
                        : [];
                var duplicates = publishedDuplicates.Concat(output.Candidates
                    .Where(x => x.Key != draft.Key && x.Kind == draft.Kind && x.Name.Equals(draft.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(x => new PackageDuplicate(ids[x.Key], x.Name, x.Kind.ToString(), false))).ToArray();
                var duties = new Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(draft.ControlId) && draft.Responsibility is "Provider" or "Shared" or "Customer")
                    duties.Add(draft.ControlId, draft.Responsibility);
                var componentType = Enum.GetNames<CspComponentType>()
                    .FirstOrDefault(value => value.Equals(draft.ComponentType, StringComparison.OrdinalIgnoreCase))
                    ?? draft.ComponentType ?? "Service";
                var payload = new PackageCandidateResponse(candidateId, draft.Kind.ToString(), draft.Name, draft.Description,
                    componentType, "", "", duties, draft.DependencyKeys.Where(ids.ContainsKey).Select(x => ids[x]).ToArray(),
                    citations, duplicates, null, null, "NeedsReview", 1, null, null, draft.UnresolvedDependencies,
                    draft.AuthorizationReference is { } reference
                        ? new(reference.Reference, reference.Issuer, reference.IssuedAt, reference.ExpiresAt) : null,
                    draft.Claim);
                db.CspPackageCandidates.Add(new CspPackageCandidate
                {
                    Id = candidateId, PackageId = id, StableKey = Hash(draft.Key), Type = draft.Kind.ToString(),
                    PayloadJson = Json(payload), UnresolvedDependenciesJson = Json(draft.UnresolvedDependencies)
                });
            }
            var continuing = false;
            if (output.Checkpoint is { } checkpoint)
            {
                var priorCalls = row.AnalysisCheckpointJson is null ? 0
                    : Read<CspPackageAnalysisCheckpoint>(row.AnalysisCheckpointJson).Progress.Values.Sum(value => value.SemanticCallsCharged);
                var calls = checkpoint.Progress.Values.Sum(value => value.SemanticCallsCharged);
                var retryKeys = checkpoint.Entries.Where(entry => !entry.AnalysisComplete
                    && entry.ReasonCode == "MODEL_TIME_LIMIT").Select(entry => entry.Key).ToArray();
                continuing = retryKeys.Length > 0 && calls > priorCalls && calls < checkpoint.SemanticCallLimit;
                var byKey = checkpoint.Entries.ToDictionary(entry => entry.Key);
                var included = checkpoint.Segments.Where(segment =>
                    byKey[segment.EntryKey].Status != CspPackageEntryStatus.Excluded
                    && !checkpoint.Progress[segment.EntryKey].PdfAnalysisViews.ContainsKey(segment.Key)).ToArray();
                var completed = included.Count(segment =>
                    byKey[segment.EntryKey].AnalysisComplete
                    || checkpoint.Progress[segment.EntryKey].SemanticallyAnalyzedSegmentKeys.Contains(segment.Key)
                    && (byKey[segment.EntryKey].AnalysisProfileVersion < 2
                        || Enum.GetValues<CspPackageClaimFamily>().All(family =>
                            checkpoint.Progress[segment.EntryKey].FamilyAnalyzedSegmentKeys.TryGetValue(family, out var keys)
                            && keys.Contains(segment.Key))));
                row.AnalysisCheckpointJson = Json(checkpoint with
                {
                    Entries = checkpoint.Entries.Select(entry => entry with { Content = null }).ToArray(),
                    AutomaticContinuationEntryKeys = continuing ? retryKeys : [],
                    AnalysisProgress = new(completed, included.Length, calls, checkpoint.SemanticCallLimit, continuing)
                });
                if (continuing)
                    logger.LogInformation("CspPackage.ContinuationQueued package={PackageId} segments={Completed}/{Total} calls={Calls}/{Limit}",
                        id, completed, included.Length, calls, checkpoint.SemanticCallLimit);
                else if (retryKeys.Length > 0)
                    logger.LogWarning("CspPackage.ContinuationStopped package={PackageId} calls={Calls}/{Limit}; budget exhausted or no progress",
                        id, calls, checkpoint.SemanticCallLimit);
            }
            else if (row.AnalysisCheckpointJson is not null)
                throw new InvalidDataException("Resumed analysis must produce a durable checkpoint.");
            row.ProcessingState = continuing ? "Received" : needsAttention ? "NeedsAttention" : "ReadyForReview";
            row.LastError = !continuing && needsAttention ? "Coverage or dependency exceptions require explicit review." : null;
            row.LeaseId = null;
            row.LeaseExpiresTicks = 0;
            Touch(row, true);
            var approvals = await db.CspPackageApprovals.Where(x => x.PackageId == id && x.State != "Published").ToListAsync(ct);
            foreach (var approval in approvals) approval.State = "Invalidated";
            Audit(db, row, "AnalysisCheckpointed", "package-worker", row.ProcessingState);
            await db.SaveChangesAsync(ct);
        });
    }

    private static async Task<CspPackage> FencedAsync(AtoCopilotContext db, Guid id, Guid lease, CancellationToken ct)
    {
        var row = await db.CspPackages.SingleAsync(x => x.Id == id, ct);
        if (row.LeaseId != lease || row.LeaseExpiresTicks <= DateTimeOffset.UtcNow.UtcTicks || row.ProcessingState != "Processing")
            throw new DbUpdateConcurrencyException("The processing lease is no longer current.");
        return row;
    }

    private async Task FailAsync(Guid id, Guid lease, string message, CancellationToken ct)
    {
        await using var strategy = await factory.CreateDbContextAsync(ct);
        await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var row = await db.CspPackages.SingleAsync(x => x.Id == id, ct);
            if (row.LeaseId != lease) return;
            row.ProcessingState = "Failed";
            row.LastError = message;
            row.LeaseId = null;
            row.LeaseExpiresTicks = 0;
            Touch(row, false);
            Audit(db, row, "ProcessingFailed", "package-worker", message);
            await db.SaveChangesAsync(ct);
        });
    }
}

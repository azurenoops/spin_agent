using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private const int CheckpointVersion = 1;

    /// <inheritdoc />
    public async Task<CspPackageAnalysisResult> ResumeAsync(
        CspPackageAnalysisResumeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCheckpoint(request, cancellationToken);
        var session = RestoreSession(request, cancellationToken);
        ValidateRetainedBudgets(session);
        foreach (var entry in session.Entries.Where(entry => request.RetryEntryKeys.Contains(entry.Key))
            .OrderBy(entry => entry.Depth).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Status == CspPackageEntryStatus.Processed && entry.MediaType == "application/pdf"
                && !entry.PdfAttachmentsEnumerated)
                await EnsurePdfAttachmentsAsync(session, entry).ConfigureAwait(false);
            if (entry.Status != CspPackageEntryStatus.Pending) continue;
            if (entry.Content is null)
                await RecoverEntryContentAsync(session, entry).ConfigureAwait(false);
            if (entry.Status != CspPackageEntryStatus.Pending || !session.CanRead(entry)) continue;
            if (entry.Content is null)
                throw new ArgumentException("Resume requires retained bytes for the selected entry.", nameof(request));
            session.ExpandedBytes += entry.ExpandedBytes - entry.ExpandedBytesCharged;
            entry.ExpandedBytesCharged = entry.ExpandedBytes;
            await ExtractResumedEntryAsync(session, entry).ConfigureAwait(false);
        }
        var semanticKeys = request.RetryEntryKeys.Concat(session.Entries
            .Where(entry => !request.Checkpoint.Progress.ContainsKey(entry.Key)).Select(entry => entry.Key))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var entry in session.Entries.Where(entry => semanticKeys.Contains(entry.Key)).ToArray())
            if (!UpgradePdfAnalysisViews(session, entry)) semanticKeys.Remove(entry.Key);
        EnrichClaims(session, semanticKeys);
        await AnalyzeSemanticsAsync(session, semanticKeys).ConfigureAwait(false);
        CompleteFamilyCoverage(session, semanticKeys);
        RefreshContainerCoverage(session);
        ResolveDependencies(session);
        ResolveClaimRelationships(session);
        MarkMissingDependencies(session);
        var result = session.Result();
        _logger.LogInformation("Resumed {RetryCount} package entries; retained {EntryCount} total entries; NeedsAttention={NeedsAttention}",
            request.RetryEntryKeys.Count, result.Entries.Count, result.NeedsAttention);
        return result;
    }

    private static CspPackageOriginalFingerprint Fingerprint(CspPackageAnalysisInput input) =>
        new(input.ArtifactId, input.FileName, input.MediaType, ContentHash(input.Content)!, input.Content.LongLength);

    private static string? ContentHash(byte[]? content) => content is null
        ? null : Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private void ValidateCheckpoint(CspPackageAnalysisResumeRequest request, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Originals);
        ArgumentNullException.ThrowIfNull(request.Checkpoint);
        ArgumentNullException.ThrowIfNull(request.RetryEntryKeys);
        var checkpoint = request.Checkpoint;
        if (checkpoint.AnalysisProfileVersion is < 1 or > 2
            || request.TargetAnalysisProfileVersion is < 1 or > 2
            || request.TargetAnalysisProfileVersion < checkpoint.AnalysisProfileVersion)
            throw new ArgumentException("Unsupported analysis profile transition.", nameof(request));
        if (checkpoint.Version != CheckpointVersion)
            throw new ArgumentException("Unsupported analyzer checkpoint version; a new analysis revision is required.", nameof(request));
        if (request.Originals.Count == 0 || request.Originals.Count != checkpoint.Originals.Count)
            throw new ArgumentException("Resume must include exactly the checkpoint's original uploads.", nameof(request));
        long uploaded = 0;
        for (var index = 0; index < request.Originals.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var original = request.Originals[index];
            ArgumentNullException.ThrowIfNull(original);
            ArgumentNullException.ThrowIfNull(original.Content);
            if (Fingerprint(original) != checkpoint.Originals[index])
                throw new ArgumentException("Original source bytes, identity, metadata or order changed; start a new analysis revision.", nameof(request));
            uploaded = checked(uploaded + original.Content.LongLength);
        }
        if (uploaded > _limits.MaxUploadedBytes || checkpoint.Entries.Count > _limits.MaxEntries)
            throw new ArgumentException("Retained uploads or manifest exceed the configured resume budget.", nameof(request));
        var entries = UniqueEntries(checkpoint);
        ValidateCheckpointEntries(checkpoint, entries, cancellation);
        ValidateCheckpointSources(checkpoint, entries, cancellation);
        foreach (var key in request.RetryEntryKeys)
        {
            if (!entries.TryGetValue(key, out var entry) || !(CanRetry(entry, checkpoint.Progress[key], checkpoint.Segments)
                || request.TargetAnalysisProfileVersion == 2 && entry.AnalysisProfileVersion < 2
                    && entry.Status == CspPackageEntryStatus.Processed))
                throw new ArgumentException("Retry keys must identify unfinished entries, not completed sources or exclusions.", nameof(request));
        }
    }

    private static Dictionary<string, CspPackageAnalyzedEntry> UniqueEntries(CspPackageAnalysisCheckpoint checkpoint)
    {
        var entries = new Dictionary<string, CspPackageAnalyzedEntry>(StringComparer.Ordinal);
        foreach (var entry in checkpoint.Entries)
            if (!entries.TryAdd(entry.Key, entry))
                throw new ArgumentException("Checkpoint contains duplicate entry keys.", nameof(checkpoint));
        if (entries.Count != checkpoint.Progress.Count || entries.Keys.Any(key => !checkpoint.Progress.ContainsKey(key)))
            throw new ArgumentException("Checkpoint must include progress for every manifest entry.", nameof(checkpoint));
        return entries;
    }

    private static void ValidateCheckpointEntries(CspPackageAnalysisCheckpoint checkpoint,
        IReadOnlyDictionary<string, CspPackageAnalyzedEntry> entries, CancellationToken cancellation)
    {
        var roots = entries.Values.Where(entry => entry.ParentKey is null).ToArray();
        if (roots.Length != checkpoint.Originals.Count)
            throw new ArgumentException("Checkpoint original manifest is incomplete.", nameof(checkpoint));
        foreach (var original in checkpoint.Originals)
            if (!entries.TryGetValue(StableKey(original.ArtifactId), out var root) || root.ParentKey is not null
                || root.ArtifactId != original.ArtifactId || root.ArchivePath != original.FileName
                || root.ExpandedBytes != original.ByteLength || ContentHash(root.Content) != original.Sha256
                || root.MediaType != MediaType(original.FileName, original.MediaType))
                throw new ArgumentException("Checkpoint original entry does not match its fingerprint.", nameof(checkpoint));
        foreach (var entry in entries.Values)
        {
            cancellation.ThrowIfCancellationRequested();
            if (entry.AnalysisProfileVersion is < 1 or > 2
                || entry.FamilyCoverage is null
                || entry.AnalysisProfileVersion == 2 && (entry.FamilyCoverage.Count != 5
                    || entry.FamilyCoverage.Select(value => value.Family).Distinct().Count() != 5
                    || entry.FamilyCoverage.Any(value => !Enum.IsDefined(value.Family) || !Enum.IsDefined(value.Status)
                        || !FamilyComplete(value) && string.IsNullOrWhiteSpace(value.Reason))
                    || entry.AnalysisComplete && entry.Status != CspPackageEntryStatus.Excluded
                        && entry.FamilyCoverage.Any(value => !FamilyComplete(value))
                    || !checkpoint.FamilyCoverage.TryGetValue(entry.Key, out var retainedFamilies)
                    || !entry.FamilyCoverage.SequenceEqual(retainedFamilies)))
                throw new ArgumentException("Checkpoint family coverage is incomplete or inconsistent.", nameof(checkpoint));
            var progress = checkpoint.Progress[entry.Key];
            if (entry.ExpandedBytes < 0 || progress.ExpandedBytesCharged < 0
                || progress.ExpandedBytesCharged > entry.ExpandedBytes || progress.PdfPagesCharged < 0
                || progress.Depth < 0 || progress.Depth > entries.Count
                || progress.ContentSha256 != ContentHash(entry.Content)
                || entry.Content is not null && entry.Content.LongLength != entry.ExpandedBytes
                || entry.Content is not null && progress.ExpandedBytesCharged != 0 && progress.ExpandedBytesCharged != entry.ExpandedBytes
                || entry.MediaType != "application/pdf" && (progress.PdfPagesCharged != 0 || progress.PdfAttachmentsEnumerated)
                || progress.SemanticCallsCharged < 0 || progress.SemanticallyAnalyzedSegmentKeys is null)
                throw new ArgumentException("Checkpoint entry bytes or budget charges are invalid or were not hydrated.", nameof(checkpoint));
            if (entry.ParentKey is null)
            {
                if (progress.Depth != 0) throw new ArgumentException("Checkpoint root depth must be zero.", nameof(checkpoint));
            }
            else if (!entries.TryGetValue(entry.ParentKey, out var parent)
                || progress.Depth != checkpoint.Progress[parent.Key].Depth + 1
                || !entry.ArchivePath.StartsWith(parent.ArchivePath + "!/", StringComparison.Ordinal))
                throw new ArgumentException("Checkpoint containment tree is invalid.", nameof(checkpoint));
        }
    }

    private static void ValidateCheckpointSources(CspPackageAnalysisCheckpoint checkpoint,
        IReadOnlyDictionary<string, CspPackageAnalyzedEntry> entries, CancellationToken cancellation)
    {
        var segments = new Dictionary<string, CspPackageSourceSegment>(StringComparer.Ordinal);
        foreach (var segment in checkpoint.Segments)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!segments.TryAdd(segment.Key, segment) || !entries.TryGetValue(segment.EntryKey, out var entry)
                || segment.Key != StableKey(segment.EntryKey, segment.Locator)
                || segment.ArtifactId != entry.ArtifactId || segment.ArchivePath != entry.ArchivePath)
                throw new ArgumentException("Checkpoint segment identity is invalid.", nameof(checkpoint));
        }
        var candidateKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in checkpoint.Candidates)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!candidateKeys.Add(candidate.Key) || candidate.Citations.Count == 0)
                throw new ArgumentException("Checkpoint candidate identity or provenance is missing.", nameof(checkpoint));
            foreach (var citation in candidate.Citations)
                if (!segments.TryGetValue(citation.SegmentKey, out var segment) || citation.Quote.Length == 0
                    || !segment.Text.Contains(citation.Quote, StringComparison.Ordinal)
                    || citation.EntryKey != segment.EntryKey || citation.ArtifactId != segment.ArtifactId
                    || citation.ArchivePath != segment.ArchivePath || citation.Locator != segment.Locator)
                    throw new ArgumentException("Checkpoint citation is not supported by its retained source.", nameof(checkpoint));
            if (IsClaimKind(candidate.Kind) != (candidate.Claim is not null))
                throw new ArgumentException("Checkpoint claim kind and typed payload must agree.", nameof(checkpoint));
            if (candidate.Claim is not null)
                ValidateClaim(candidate.Kind, candidate.Claim, candidate.Citations);
        }
        if (checkpoint.CandidateSourceReferences.Keys.Any(key => !candidateKeys.Contains(key)))
            throw new ArgumentException("Checkpoint dependency references identify an absent candidate.", nameof(checkpoint));
        foreach (var (key, progress) in checkpoint.Progress)
        {
            if (progress.PdfTextLayoutVersion is < 0 or > PdfTextLayoutVersion || progress.PdfAnalysisViews is null
                || progress.PdfAnalysisViews.Values.Distinct(StringComparer.Ordinal).Count() != progress.PdfAnalysisViews.Count
                || progress.PdfAnalysisViews.Any(pair =>
                    !segments.TryGetValue(pair.Key, out var original) || !segments.TryGetValue(pair.Value, out var view)
                    || entries[key].MediaType != "application/pdf" || original.EntryKey != key || view.EntryKey != key
                    || !original.Locator.StartsWith("page:", StringComparison.Ordinal)
                    || original.Locator.Contains("/layout:", StringComparison.Ordinal)
                    || view.Locator != $"{original.Locator}/layout:{PdfTextLayoutVersion}"))
                throw new ArgumentException("Checkpoint PDF analysis views do not match their retained page evidence.", nameof(checkpoint));
            if (progress.SemanticallyAnalyzedSegmentKeys.Count > 0 && progress.SemanticCallsCharged == 0
                || progress.SemanticallyAnalyzedSegmentKeys.Count != progress.SemanticallyAnalyzedSegmentKeys.Distinct(StringComparer.Ordinal).Count()
                || progress.SemanticallyAnalyzedSegmentKeys.Any(segmentKey =>
                    !segments.TryGetValue(segmentKey, out var segment) || segment.EntryKey != key))
                throw new ArgumentException("Checkpoint semantic progress does not match its retained source.", nameof(checkpoint));
            if (progress.FamilyAnalyzedSegmentKeys is null || progress.FamilyAnalyzedSegmentKeys.Any(pair =>
                !Enum.IsDefined(pair.Key) || pair.Value is null || pair.Value.Count != pair.Value.Distinct().Count()
                || pair.Value.Any(segmentKey => !progress.SemanticallyAnalyzedSegmentKeys.Contains(segmentKey))))
                throw new ArgumentException("Checkpoint family progress does not match its analyzed source.", nameof(checkpoint));
        }
    }

    private static bool CanRetry(CspPackageAnalyzedEntry entry, CspPackageEntryProgress progress,
        IReadOnlyList<CspPackageSourceSegment> segments)
    {
        if (entry.Status == CspPackageEntryStatus.Excluded) return false;
        if (entry.Status != CspPackageEntryStatus.Processed) return true;
        if (entry.MediaType == "application/pdf")
            return !progress.PdfAttachmentsEnumerated || !entry.AnalysisComplete && segments.Any(segment =>
                segment.EntryKey == entry.Key && !progress.PdfAnalysisViews.ContainsKey(segment.Key)
                    && (!progress.SemanticallyAnalyzedSegmentKeys.Contains(segment.Key)
                    || entry.AnalysisProfileVersion >= 2 && Enum.GetValues<Ato.Copilot.Core.Models.PackageImports.CspPackageClaimFamily>()
                        .Any(family => !progress.FamilyAnalyzedSegmentKeys.TryGetValue(family, out var keys)
                            || !keys.Contains(segment.Key))));
        return entry.MediaType is not ("application/zip" or DocxMime or XlsxMime)
            ? !entry.AnalysisComplete : !progress.EnumerationComplete;
    }

    private AnalysisSession RestoreSession(CspPackageAnalysisResumeRequest request, CancellationToken cancellation)
    {
        var checkpoint = request.Checkpoint;
        var session = new AnalysisSession(_limits, cancellation)
        {
            Originals = checkpoint.Originals,
            AnalysisProfileVersion = request.TargetAnalysisProfileVersion ?? checkpoint.AnalysisProfileVersion
        };
        var extractionKeys = checkpoint.Entries.Where(entry => request.RetryEntryKeys.Contains(entry.Key)
                && entry.Status != CspPackageEntryStatus.Processed)
            .Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var source in checkpoint.Entries)
        {
            var progress = checkpoint.Progress[source.Key];
            var entry = session.AddEntry(source.Key, source.ParentKey, source.ArtifactId,
                source.ArchivePath, source.MediaType, source.ExpandedBytes, source.Content);
            entry.Set(source.Status, source.ReasonCode, source.Reason, source.AnalysisComplete);
            entry.Depth = progress.Depth;
            entry.EnumerationComplete = progress.EnumerationComplete;
            entry.ExpandedBytesCharged = progress.ExpandedBytesCharged;
            entry.PdfPagesCharged = progress.PdfPagesCharged;
            entry.PdfAttachmentsEnumerated = progress.PdfAttachmentsEnumerated;
            entry.SemanticCallsCharged = progress.SemanticCallsCharged;
            entry.SemanticBatchSize = Math.Clamp(progress.SemanticBatchSize, 0, _limits.MaxSemanticSegmentsPerCall);
            entry.PdfTextLayoutVersion = progress.PdfTextLayoutVersion;
            foreach (var pair in progress.PdfAnalysisViews) entry.PdfAnalysisViews.Add(pair.Key, pair.Value);
            entry.AnalysisProfileVersion = source.AnalysisProfileVersion;
            entry.HasRetainedProfile = true;
            entry.FamilyCoverage = source.FamilyCoverage;
            if (!extractionKeys.Contains(entry.Key))
            {
                entry.SemanticallyAnalyzedSegmentKeys.UnionWith(progress.SemanticallyAnalyzedSegmentKeys);
                foreach (var (family, keys) in progress.FamilyAnalyzedSegmentKeys)
                    entry.FamilyAnalyzedSegmentKeys[family] = keys.ToHashSet(StringComparer.Ordinal);
            }
            if (extractionKeys.Contains(entry.Key))
            {
                entry.Set(CspPackageEntryStatus.Pending, null, null);
                entry.PdfPagesCharged = 0;
                entry.PdfTextLayoutVersion = 0;
                entry.PdfAnalysisViews.Clear();
                if (entry.Content is null) entry.ExpandedBytesCharged = 0;
            }
        }
        session.Segments.AddRange(checkpoint.Segments.Where(segment => !extractionKeys.Contains(segment.EntryKey)));
        session.Candidates.AddRange(checkpoint.Candidates.Where(candidate =>
            !extractionKeys.Contains(candidate.Citations[0].EntryKey)));
        foreach (var candidate in session.Candidates)
        {
            if (checkpoint.CandidateSourceReferences.TryGetValue(candidate.Key, out var references))
                session.SourceDependencies[candidate.Key] = references;
            var identity = StableKey(candidate.Citations[0].SegmentKey, candidate.Kind.ToString(), candidate.Name);
            session.CandidateOccurrences[identity] = session.CandidateOccurrences.GetValueOrDefault(identity) + 1;
        }
        session.ExpandedBytes = session.Entries.Sum(entry => entry.ExpandedBytesCharged);
        session.PdfPages = session.Entries.Sum(entry => entry.PdfPagesCharged);
        session.Characters = checked((int)session.Segments.Sum(segment => (long)segment.Text.Length));
        return session;
    }

    private void ValidateRetainedBudgets(AnalysisSession session)
    {
        if (session.ExpandedBytes > _limits.MaxExpandedBytes || session.PdfPages > _limits.MaxPdfPages
            || session.Characters > _limits.MaxExtractedCharacters || session.Segments.Count > _limits.MaxSourceSegments
            || session.Candidates.Count > _limits.MaxCandidates
            || session.Entries.Sum(entry => (long)entry.SemanticCallsCharged) > _limits.MaxSemanticCalls)
            throw new ArgumentException("Completed source work already exceeds the configured resume limits.");
    }

    private async Task RecoverEntryContentAsync(AnalysisSession session, Entry entry)
    {
        if (!session.CanRead(entry)) return;
        var parent = session.Entries.SingleOrDefault(candidate => candidate.Key == entry.ParentKey);
        if (parent?.Content is null)
            throw new ArgumentException("Hydrate the retained containing archive before retrying its unread child.");
        if (parent.MediaType == "application/pdf")
        {
            await RecoverPdfAttachmentContentAsync(session, parent, entry).ConfigureAwait(false);
            return;
        }
        ValidateArchiveDirectory(session, parent);
        using var input = new MemoryStream(parent.Content, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        for (var index = 0; index < archive.Entries.Count; index++)
        {
            session.Cancellation.ThrowIfCancellationRequested();
            var source = archive.Entries[index];
            if (StableKey(parent.Key, index.ToString(CultureInfo.InvariantCulture), source.FullName) != entry.Key) continue;
            if (!SafeArchivePath(source.FullName))
                throw new ArgumentException("Unsafe archive entries cannot be resumed.");
            await ReadChildAsync(session, source, entry).ConfigureAwait(false);
            return;
        }
        throw new ArgumentException("Retained archive no longer contains the checkpoint entry.");
    }

    private async Task ExtractResumedEntryAsync(AnalysisSession session, Entry entry)
    {
        var parent = session.Entries.SingleOrDefault(candidate => candidate.Key == entry.ParentKey);
        if (parent?.MediaType == DocxMime)
        {
            var name = PartName(parent, entry);
            if (name.StartsWith("word/", StringComparison.Ordinal) && name.EndsWith(".xml", StringComparison.Ordinal)
                && !WordMetadata(name))
            {
                _logger.LogDebug("Extracting package entry {EntryKey}", entry.Key);
                ExtractPart(session, entry, () => ExtractWordXml(session, entry));
                return;
            }
        }
        if (parent?.MediaType == XlsxMime && PartName(parent, entry).StartsWith("xl/worksheets/", StringComparison.Ordinal))
        {
            _logger.LogDebug("Extracting package entry {EntryKey}", entry.Key);
            ExtractPart(session, entry, () => ResumeWorksheet(session, parent, entry));
            return;
        }
        if (parent?.MediaType is DocxMime or XlsxMime && IsContainerMetadata(PartName(parent, entry)))
        {
            _logger.LogDebug("Extracting package entry {EntryKey}", entry.Key);
            ExtractPart(session, entry, () => ExcludeMetadata(session, entry));
            return;
        }
        await ExtractAsync(session, entry, entry.Depth).ConfigureAwait(false);
    }

    private void ResumeWorksheet(AnalysisSession session, Entry parent, Entry entry)
    {
        var parts = session.Entries.Where(candidate => candidate.ParentKey == parent.Key)
            .ToDictionary(candidate => PartName(parent, candidate), StringComparer.Ordinal);
        var strings = new List<string>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        if (parts.TryGetValue("xl/sharedStrings.xml", out var shared) && shared.Content is not null)
            strings.AddRange(ReadXml(session, shared).Descendants(spreadsheet + "si").Select(element =>
                string.Concat(element.Descendants(spreadsheet + "t").Select(text => text.Value))));
        if (parts.TryGetValue("xl/workbook.xml", out var workbook) && workbook.Content is not null
            && parts.TryGetValue("xl/_rels/workbook.xml.rels", out var relations) && relations.Content is not null)
            MapWorksheets(ReadXml(session, workbook), ReadXml(session, relations), names);
        ExtractWorksheet(session, entry, strings, names.GetValueOrDefault(PartName(parent, entry), PartName(parent, entry)));
    }

    private static string PartName(Entry parent, Entry child) => child.ArchivePath[(parent.ArchivePath.Length + 2)..];

    private static void RefreshContainerCoverage(AnalysisSession session)
    {
        foreach (var entry in session.Entries.OrderByDescending(entry => entry.Depth))
        {
            if (entry.MediaType is "application/zip" or DocxMime or XlsxMime
                && entry.Status == CspPackageEntryStatus.Processed && entry.EnumerationComplete)
                entry.Process(session.Entries.Where(child => child.ParentKey == entry.Key).All(child => child.AnalysisComplete));
            else if (entry.MediaType == "application/pdf" && entry.Status == CspPackageEntryStatus.Processed)
            {
                var ownSegments = session.Segments.Where(segment => segment.EntryKey == entry.Key).ToArray();
                var ownComplete = ownSegments.Length > 0
                    && ownSegments.All(segment => entry.SemanticallyAnalyzedSegmentKeys.Contains(segment.Key))
                    && (entry.AnalysisProfileVersion < 2
                        || Enum.GetValues<Ato.Copilot.Core.Models.PackageImports.CspPackageClaimFamily>().All(family =>
                            entry.FamilyAnalyzedSegmentKeys.TryGetValue(family, out var analyzed)
                            && ownSegments.All(segment => analyzed.Contains(segment.Key))));
                if (ownComplete && entry.PdfAttachmentsEnumerated)
                {
                    if (session.Entries.Where(child => child.ParentKey == entry.Key).All(child => child.AnalysisComplete))
                        entry.Process(true);
                    else
                        entry.Set(CspPackageEntryStatus.Processed, "CONTAINED_COVERAGE_INCOMPLETE",
                            "PDF text was analyzed, but embedded attachments still have explicit coverage exceptions.");
                }
            }
        }
    }

    private static void MarkMissingDependencies(AnalysisSession session)
    {
        var keys = session.Candidates.Select(candidate => candidate.Key).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < session.Candidates.Count; index++)
        {
            var candidate = session.Candidates[index];
            var missing = candidate.DependencyKeys.Where(key => !keys.Contains(key)).ToArray();
            if (missing.Length > 0)
                session.Candidates[index] = candidate with
                {
                    UnresolvedDependencies = candidate.UnresolvedDependencies.Concat(missing).Distinct(StringComparer.Ordinal).ToArray()
                };
        }
    }
}

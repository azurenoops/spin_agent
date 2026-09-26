using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;

namespace Ato.Copilot.Agents.Services.PackageImports;

/// <summary>
/// Bounded, deterministic package extraction. No source instruction is executed and no draft is approved.
/// Optional configured semantic analysis emits only source-validated proposals, never approvals.
/// </summary>
public sealed partial class CspPackageAnalyzer : ICspPackageAnalyzer
{
    private readonly ILogger<CspPackageAnalyzer> _logger;
    private readonly CspPackageAnalysisLimits _limits;
    private readonly IChatClient? _chatClient;

    public CspPackageAnalyzer(ILogger<CspPackageAnalyzer> logger, CspPackageAnalysisLimits? limits = null,
        IChatClient? chatClient = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _limits = limits ?? new();
        _chatClient = chatClient;
        if (_limits.MaxUploadedBytes <= 0 || _limits.MaxExpandedBytes <= 0 || _limits.MaxEntryBytes <= 0
            || _limits.MaxEntries <= 0 || _limits.MaxArchiveDepth < 0 || _limits.MaxPdfPages <= 0
            || _limits.MaxPdfLayoutWordsPerPage <= 0 || _limits.MaxPdfLayoutBlocksPerPage <= 0
            || _limits.MaxExtractedCharacters <= 0 || _limits.MaxStructuredDepth is < 1 or > 256
            || _limits.MaxSourceSegments <= 0 || _limits.MaxCandidates <= 0
            || _limits.MaxPdfAttachmentNodes <= 0 || _limits.MaxPdfAttachmentDepth is < 1 or > 256
            || _limits.MaxSemanticCalls <= 0 || _limits.MaxSemanticSegmentsPerCall <= 0
            || _limits.MaxSemanticInputCharactersPerCall <= 0 || _limits.MaxSemanticResponseCharacters <= 0
            || _limits.MaxSemanticOutputTokens <= 0 || _limits.SemanticCallTimeout <= TimeSpan.Zero
            || _limits.SemanticTotalTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(limits), "Analysis budgets must be positive; depth must be bounded.");
    }

    /// <inheritdoc />
    public async Task<CspPackageAnalysisResult> AnalyzeAsync(
        IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0 || inputs.Count > _limits.MaxEntries)
            throw new ArgumentException("Supply at least one upload and no more than MaxEntries.", nameof(inputs));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long uploaded = 0;
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(input.Content);
            ArgumentException.ThrowIfNullOrWhiteSpace(input.ArtifactId);
            ArgumentException.ThrowIfNullOrWhiteSpace(input.FileName);
            if (!ids.Add(input.ArtifactId))
                throw new ArgumentException("ArtifactId must uniquely identify each original upload.", nameof(inputs));
            uploaded = checked(uploaded + input.Content.LongLength);
        }
        var session = new AnalysisSession(_limits, cancellationToken)
        {
            Originals = inputs.Select(Fingerprint).ToArray()
        };
        // Reserve originals before children so a large first archive cannot hide subsequent uploads.
        var roots = inputs.Select(input => session.AddEntry(
            StableKey(input.ArtifactId), null, input.ArtifactId, input.FileName,
            MediaType(input.FileName, input.MediaType), input.Content.LongLength, input.Content)).ToArray();
        for (var index = 0; index < roots.Length; index++)
        {
            var root = roots[index];
            if (uploaded > _limits.MaxUploadedBytes)
                root.Fail("UPLOAD_SIZE_LIMIT", "Uploaded package exceeds its byte budget. Split the package and retry.");
            else if (session.CanRead(root))
            {
                session.ExpandedBytes += root.ExpandedBytes;
                root.ExpandedBytesCharged = root.ExpandedBytes;
                await ExtractAsync(session, root, 0).ConfigureAwait(false);
            }
        }
        EnrichClaims(session);
        await AnalyzeSemanticsAsync(session).ConfigureAwait(false);
        CompleteFamilyCoverage(session);
        RefreshContainerCoverage(session);
        ResolveDependencies(session);
        ResolveClaimRelationships(session);
        var result = session.Result();
        _logger.LogInformation(
            "Package analysis returned {EntryCount} entries, {CandidateCount} drafts, NeedsAttention={NeedsAttention}",
            result.Entries.Count, result.Candidates.Count, result.NeedsAttention);
        return result;
    }

    private async Task ExtractAsync(AnalysisSession session, Entry entry, int depth)
    {
        session.Cancellation.ThrowIfCancellationRequested();
        entry.Depth = depth;
        _logger.LogDebug("Extracting package entry {EntryKey}", entry.Key);
        try
        {
            switch (entry.MediaType)
            {
                case "application/zip":
                case DocxMime:
                case XlsxMime:
                    await ExtractArchiveAsync(session, entry, depth).ConfigureAwait(false);
                    break;
                case "application/pdf": await ExtractPdfAsync(session, entry).ConfigureAwait(false); break;
                case "application/json": ExtractJson(session, entry); break;
                case "application/xml": ExtractXml(session, entry); break;
                case "text/csv": ExtractCsv(session, entry); break;
                case "text/plain":
                case "text/markdown": ExtractText(session, entry); break;
                default:
                    entry.Set(CspPackageEntryStatus.Unsupported, "UNSUPPORTED_FORMAT",
                        "No safe decoder is available for this binary attachment or format. Supply a supported text-bearing export.");
                    break;
            }
        }
        catch (BudgetExceededException exception)
        {
            entry.Fail(exception.Code, exception.Message);
        }
        catch (UnsupportedContentException exception)
        {
            entry.Set(CspPackageEntryStatus.Unsupported, exception.Code, exception.Message);
        }
        catch (PdfDocumentEncryptedException)
        {
            entry.Set(CspPackageEntryStatus.Unreadable, "ENCRYPTED_CONTENT",
                "Encrypted PDF cannot be read. Supply an unrestricted export.");
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            entry.Fail("CLAIM_ANALYSIS_TIMEOUT", "Bounded claim matching timed out. Split the source or supply explicit structured declarations.");
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or XmlException
            or DecoderFallbackException or FormatException or PdfDocumentFormatException)
        {
            entry.Set(CspPackageEntryStatus.Unreadable, "MALFORMED_CONTENT",
                "Content is malformed, encrypted or uses unsupported encoding. Re-export the source and retry.");
            _logger.LogWarning("Package entry {EntryKey} could not be read ({ErrorType})", entry.Key, exception.GetType().Name);
        }
        catch (IOException exception)
        {
            entry.Fail("CONTENT_READ_FAILED", "The entry could not be read. Retain the source and retry.");
            _logger.LogWarning("Package entry {EntryKey} read failed ({ErrorType})", entry.Key, exception.GetType().Name);
        }
        session.Cancellation.ThrowIfCancellationRequested();
    }

    private async Task ExtractArchiveAsync(AnalysisSession session, Entry container, int depth)
    {
        if (depth > _limits.MaxArchiveDepth)
        {
            session.EnumerationComplete = false;
            container.Fail("ARCHIVE_DEPTH_LIMIT", "Nested archive depth exceeds the budget. Upload the nested package separately.");
            return;
        }
        ValidateArchiveDirectory(session, container);
        using var input = new MemoryStream(container.Content!, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var children = new List<(string Name, Entry Entry)>();
        for (var index = 0; index < archive.Entries.Count; index++)
        {
            session.Cancellation.ThrowIfCancellationRequested();
            var source = archive.Entries[index];
            var key = StableKey(container.Key, index.ToString(CultureInfo.InvariantCulture), source.FullName);
            var existing = session.Entries.FirstOrDefault(entry => entry.Key == key);
            if (existing is null && session.Entries.Count >= _limits.MaxEntries)
            {
                session.EnumerationComplete = false;
                container.Fail("ENTRY_COUNT_LIMIT",
                    $"Entry limit reached; {archive.Entries.Count - index} direct entries were not enumerated. Split this archive.");
                break;
            }
            var child = existing ?? session.AddEntry(key, container.Key, key, $"{container.ArchivePath}!/{source.FullName}",
                MediaType(source.FullName, null), source.Length, null);
            child.Depth = depth + 1;
            children.Add((source.FullName, child));
            if (existing is not null && (child.Status != CspPackageEntryStatus.Pending || child.Content is not null))
                continue;
            if (!SafeArchivePath(source.FullName))
                child.Set(CspPackageEntryStatus.Excluded, "UNSAFE_ARCHIVE_PATH",
                    "Archive entry uses an absolute, traversing or ambiguous path. Repackage with safe relative names.");
            else if (source.FullName.EndsWith('/') || source.FullName.EndsWith('\\'))
            {
                child.Content = [];
                child.Set(CspPackageEntryStatus.Excluded, "DIRECTORY_ENTRY", "Directory record; not a document.", complete: true);
            }
            else if (session.CanRead(child))
                await ReadChildAsync(session, source, child).ConfigureAwait(false);
        }
        container.EnumerationComplete = children.Count == archive.Entries.Count;
        if (container.MediaType == DocxMime)
            await ExtractWordPartsAsync(session, container, children, depth).ConfigureAwait(false);
        else if (container.MediaType == XlsxMime)
            await ExtractWorkbookPartsAsync(session, container, children, depth).ConfigureAwait(false);
        else
            foreach (var (_, child) in children.Where(pair => pair.Entry.Status == CspPackageEntryStatus.Pending))
                await ExtractAsync(session, child, depth + 1).ConfigureAwait(false);
        if (container.Status == CspPackageEntryStatus.Pending)
            container.Process(complete: children.All(pair => pair.Entry.AnalysisComplete));
    }

    private async Task ReadChildAsync(AnalysisSession session, ZipArchiveEntry source, Entry child)
    {
        try
        {
            await using var stream = source.Open();
            using var content = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var count = await stream.ReadAsync(buffer, session.Cancellation).ConfigureAwait(false);
                if (count == 0) break;
                if (content.Length + count > _limits.MaxEntryBytes || content.Length + count > child.ExpandedBytes)
                    throw new BudgetExceededException("ENTRY_SIZE_LIMIT", "Expanded entry exceeds its declared size or byte budget.");
                if (session.ExpandedBytes + count > _limits.MaxExpandedBytes)
                    throw new BudgetExceededException("EXPANDED_SIZE_LIMIT", "Expanded package exceeds the byte budget. Split the package.");
                content.Write(buffer, 0, count);
                session.ExpandedBytes += count;
                child.ExpandedBytesCharged += count;
            }
            if (content.Length != child.ExpandedBytes)
                throw new InvalidDataException("Archive entry length does not match the declared length.");
            child.Content = content.ToArray();
        }
        catch (BudgetExceededException exception) { child.Fail(exception.Code, exception.Message); }
        catch (InvalidDataException)
        {
            child.Set(CspPackageEntryStatus.Unreadable, "MALFORMED_CONTENT", "Archive entry is damaged or encrypted. Re-export it.");
        }
        catch (IOException)
        {
            child.Fail("CONTENT_READ_FAILED", "Archive entry could not be read. Re-export it.");
        }
    }

    private static bool SafeArchivePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return !normalized.StartsWith('/') && !normalized.Contains(':') && !normalized.Contains('\0')
            && !normalized.Split('/').Any(part => part is ".." or ".");
    }

    private const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static string MediaType(string name, string? supplied) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".zip" => "application/zip",
        ".docx" => DocxMime,
        ".xlsx" => XlsxMime,
        ".pdf" => "application/pdf",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".csv" => "text/csv",
        ".txt" => "text/plain",
        ".md" => "text/markdown",
        _ => supplied?.Split(';', 2)[0].Trim().ToLowerInvariant() ?? "application/octet-stream"
    };

    private static string StableKey(params string[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Concat(parts.Select(part => $"{part.Length}:{part}"))))).ToLowerInvariant();

    private sealed class BudgetExceededException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    private sealed class UnsupportedContentException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    private sealed class Entry(string key, string? parentKey, string artifactId, string path, string mediaType,
        long size, byte[]? content)
    {
        public string Key { get; } = key;
        public string? ParentKey { get; } = parentKey;
        public string ArtifactId { get; } = artifactId;
        public string ArchivePath { get; } = path;
        public string MediaType { get; } = mediaType;
        public long ExpandedBytes { get; set; } = size;
        public byte[]? Content { get; set; } = content;
        public int Depth { get; set; }
        public bool EnumerationComplete { get; set; } = mediaType is not ("application/zip" or DocxMime or XlsxMime);
        public long ExpandedBytesCharged { get; set; }
        public int PdfPagesCharged { get; set; }
        public int SemanticCallsCharged { get; set; }
        public int SemanticBatchSize { get; set; }
        public int PdfTextLayoutVersion { get; set; }
        public Dictionary<string, string> PdfAnalysisViews { get; } = new(StringComparer.Ordinal);
        public HashSet<string> SemanticallyAnalyzedSegmentKeys { get; } = new(StringComparer.Ordinal);
        public bool PdfAttachmentsEnumerated { get; set; }
        public int AnalysisProfileVersion { get; set; } = 1;
        public bool HasRetainedProfile { get; set; }
        public IReadOnlyList<CspPackageFamilyCoverage> FamilyCoverage { get; set; } = [];
        public Dictionary<CspPackageClaimFamily, HashSet<string>> FamilyAnalyzedSegmentKeys { get; } = [];
        public CspPackageEntryStatus Status { get; private set; }
        public string? ReasonCode { get; private set; }
        public string? Reason { get; private set; }
        public bool AnalysisComplete { get; private set; }
        public void Set(CspPackageEntryStatus status, string? code, string? reason, bool complete = false)
        {
            Status = status;
            ReasonCode = code;
            Reason = reason;
            AnalysisComplete = complete;
        }
        public void Fail(string code, string reason) => Set(CspPackageEntryStatus.Failed, code, reason);
        public void Process(bool complete)
        {
            if (ReasonCode == "UNSUPPORTED_DECLARATION_KIND") return;
            Set(CspPackageEntryStatus.Processed, complete ? null : "MODEL_ANALYSIS_UNAVAILABLE",
                complete ? null : "Text was extracted, but complete semantic analysis is unavailable. Review this source explicitly.", complete);
        }
        public CspPackageAnalyzedEntry Result() =>
            new(Key, ParentKey, ArtifactId, ArchivePath, MediaType, ExpandedBytes, Content, Status, ReasonCode, Reason, AnalysisComplete)
            { AnalysisProfileVersion = AnalysisProfileVersion, FamilyCoverage = FamilyCoverage };
    }

    private sealed class AnalysisSession(CspPackageAnalysisLimits limits, CancellationToken cancellation)
    {
        public CancellationToken Cancellation { get; } = cancellation;
        public IReadOnlyList<CspPackageOriginalFingerprint> Originals { get; set; } = [];
        public List<Entry> Entries { get; } = [];
        public List<CspPackageSourceSegment> Segments { get; } = [];
        public List<CspPackageCandidateDraft> Candidates { get; } = [];
        public Dictionary<string, IReadOnlyList<string>> SourceDependencies { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> CandidateOccurrences { get; } = new(StringComparer.Ordinal);
        public int MaxCandidates { get; } = limits.MaxCandidates;
        public long ExpandedBytes { get; set; }
        public int Characters { get; set; }
        public int PdfPages { get; set; }
        public bool EnumerationComplete { get; set; } = true;
        public int AnalysisProfileVersion { get; set; } = 2;
        public Entry AddEntry(string key, string? parent, string artifact, string path, string media, long size, byte[]? content)
        {
            var entry = new Entry(key, parent, artifact, path, media, size, content);
            Entries.Add(entry);
            return entry;
        }
        public bool CanRead(Entry entry)
        {
            if (entry.ExpandedBytes > limits.MaxEntryBytes)
                entry.Fail("ENTRY_SIZE_LIMIT", "Expanded entry exceeds the per-entry byte budget. Split or re-export the entry.");
            else if (entry.ExpandedBytes - entry.ExpandedBytesCharged > limits.MaxExpandedBytes - ExpandedBytes)
                entry.Fail("EXPANDED_SIZE_LIMIT", "Expanded package exceeds the byte budget. Split the package.");
            return entry.Status == CspPackageEntryStatus.Pending;
        }
        public CspPackageSourceSegment Segment(Entry entry, string locator, string text)
        {
            Cancellation.ThrowIfCancellationRequested();
            if (Segments.Count >= limits.MaxSourceSegments)
                throw new BudgetExceededException("SEGMENT_COUNT_LIMIT",
                    "Source segment count exceeds the package budget. Remaining content was not analyzed; split the source.");
            if (text.Length > limits.MaxExtractedCharacters - Characters)
                throw new BudgetExceededException("EXTRACTED_CHARACTER_LIMIT",
                    "Extracted text exceeds the package character budget. Remaining content was not analyzed; split the package.");
            Characters += text.Length;
            var segment = new CspPackageSourceSegment(StableKey(entry.Key, locator), entry.Key,
                entry.ArtifactId, entry.ArchivePath, locator, text);
            Segments.Add(segment);
            return segment;
        }
        public CspPackageAnalysisResult Result()
        {
            int Count(CspPackageEntryStatus state) => Entries.Count(entry => entry.Status == state);
            var entries = Entries.Select(entry => entry.Result()).ToArray();
            var segments = Segments.ToArray();
            var candidates = Candidates.ToArray();
            return new(entries, segments, candidates,
                new(Entries.Count, Count(CspPackageEntryStatus.Processed), Count(CspPackageEntryStatus.Unsupported),
                    Count(CspPackageEntryStatus.Unreadable), Count(CspPackageEntryStatus.Failed), Count(CspPackageEntryStatus.Excluded),
                    ExpandedBytes, Characters, EnumerationComplete && Entries.All(entry =>
                        entry.Status == CspPackageEntryStatus.Excluded && entry.AnalysisComplete
                        || (entry.MediaType == "application/pdf" ? entry.PdfAttachmentsEnumerated
                            : entry.MediaType is not ("application/zip" or DocxMime or XlsxMime)
                                || entry.Status == CspPackageEntryStatus.Processed)),
                    Entries.All(entry => entry.AnalysisComplete)))
            {
                AnalysisProfileVersion = entries.Min(entry => entry.AnalysisProfileVersion),
                FamilyCoverage = entries.ToDictionary(entry => entry.Key, entry => entry.FamilyCoverage),
                Checkpoint = new(CheckpointVersion, Originals, entries, segments, candidates,
                    SourceDependencies.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                    Entries.ToDictionary(entry => entry.Key, entry => new CspPackageEntryProgress(
                        entry.Depth, entry.EnumerationComplete, entry.ExpandedBytesCharged,
                        entry.PdfPagesCharged, ContentHash(entry.Content))
                    {
                        SemanticCallsCharged = entry.SemanticCallsCharged,
                        SemanticBatchSize = entry.SemanticBatchSize,
                        PdfTextLayoutVersion = entry.PdfTextLayoutVersion,
                        PdfAnalysisViews = new Dictionary<string, string>(entry.PdfAnalysisViews, StringComparer.Ordinal),
                        SemanticallyAnalyzedSegmentKeys = entry.SemanticallyAnalyzedSegmentKeys.Order(StringComparer.Ordinal).ToArray(),
                        PdfAttachmentsEnumerated = entry.PdfAttachmentsEnumerated,
                        FamilyAnalyzedSegmentKeys = entry.FamilyAnalyzedSegmentKeys.ToDictionary(pair => pair.Key,
                            pair => (IReadOnlyList<string>)pair.Value.Order(StringComparer.Ordinal).ToArray())
                    }, StringComparer.Ordinal))
                {
                    SemanticCallLimit = limits.MaxSemanticCalls,
                    AnalysisProfileVersion = entries.Min(entry => entry.AnalysisProfileVersion),
                    FamilyCoverage = entries.ToDictionary(entry => entry.Key, entry => entry.FamilyCoverage)
                }
            };
        }
    }
}

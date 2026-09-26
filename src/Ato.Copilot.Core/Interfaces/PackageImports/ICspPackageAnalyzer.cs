using Ato.Copilot.Core.Models.PackageImports;

namespace Ato.Copilot.Core.Interfaces.PackageImports;

/// <summary>Analyzes untrusted package bytes without persistence or publication side effects.</summary>
public interface ICspPackageAnalyzer
{
    /// <summary>Returns bounded manifest coverage, retained originals and source-supported drafts.</summary>
    Task<CspPackageAnalysisResult> AnalyzeAsync(
        IReadOnlyList<CspPackageAnalysisInput> inputs,
        CancellationToken cancellationToken = default);

    /// <summary>Retries only selected unfinished entries using an immutable, server-owned checkpoint.</summary>
    Task<CspPackageAnalysisResult> ResumeAsync(
        CspPackageAnalysisResumeRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>An original upload; ArtifactId is the caller's durable, package-local source identity.</summary>
public sealed record CspPackageAnalysisInput(
    string ArtifactId, string FileName, string MediaType, byte[] Content);

/// <summary>Limits apply to the whole call, including container parts and nested attachments.</summary>
public sealed record CspPackageAnalysisLimits
{
    public long MaxUploadedBytes { get; init; } = 50L * 1024 * 1024;
    public long MaxExpandedBytes { get; init; } = 200L * 1024 * 1024;
    public long MaxEntryBytes { get; init; } = 50L * 1024 * 1024;
    public int MaxEntries { get; init; } = 1_000;
    public int MaxArchiveDepth { get; init; } = 3;
    public int MaxPdfPages { get; init; } = 1_000;
    public int MaxPdfLayoutWordsPerPage { get; init; } = 5_000;
    public int MaxPdfLayoutBlocksPerPage { get; init; } = 512;
    public int MaxExtractedCharacters { get; init; } = 2_000_000;
    public int MaxStructuredDepth { get; init; } = 64;
    public int MaxSourceSegments { get; init; } = 20_000;
    public int MaxCandidates { get; init; } = 10_000;
    public int MaxPdfAttachmentNodes { get; init; } = 4_096;
    public int MaxPdfAttachmentDepth { get; init; } = 32;
    public int MaxSemanticCalls { get; init; } = 64;
    public int MaxSemanticSegmentsPerCall { get; init; } = 32;
    public int MaxSemanticInputCharactersPerCall { get; init; } = 24_000;
    public int MaxSemanticResponseCharacters { get; init; } = 64_000;
    public int MaxSemanticOutputTokens { get; init; } = 8_192;
    public TimeSpan SemanticCallTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan SemanticTotalTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>Extraction state is separate from semantic-analysis completeness and human review.</summary>
public enum CspPackageEntryStatus { Pending, Processed, Unsupported, Unreadable, Failed, Excluded }

/// <summary>Draft semantics are asserted by the source, never inferred from a filename.</summary>
public enum CspPackageCandidateKind
{
    Component, Capability, ControlMapping, Responsibility, AuthorizationReference,
    AuthorizationDecisionClaim, BoundaryClaim, AssessmentFinding, PoamItem
}

/// <summary>
/// A distinct archive occurrence. Key is stable for identical input/order, including duplicate names.
/// Content is null only when reading was unsafe, failed, or exceeded a limit; roots retain their bytes.
/// ArchivePath is display-only and must never be used as a filesystem/storage key.
/// </summary>
public sealed record CspPackageAnalyzedEntry(
    string Key, string? ParentKey, string ArtifactId, string ArchivePath, string MediaType,
    long ExpandedBytes, byte[]? Content, CspPackageEntryStatus Status,
    string? ReasonCode, string? Reason, bool AnalysisComplete)
{
    public int AnalysisProfileVersion { get; init; } = 1;
    public IReadOnlyList<CspPackageFamilyCoverage> FamilyCoverage { get; init; } = [];
}

/// <summary>A verbatim extracted content unit with stable source identity and a precise locator.</summary>
public sealed record CspPackageSourceSegment(
    string Key, string EntryKey, string ArtifactId, string ArchivePath, string Locator, string Text);

/// <summary>A source quote validated against the referenced segment before a draft is emitted.</summary>
public sealed record CspPackageSourceCitation(
    string SegmentKey, string EntryKey, string ArtifactId, string ArchivePath, string Locator, string Quote);

/// <summary>
/// An unpublished, unapproved source-backed proposal. Unknown classifications remain null.
/// DependencyKeys refer to drafts; unresolved source IDs must block approval.
/// </summary>
public sealed record CspPackageCandidateDraft(
    string Key, CspPackageCandidateKind Kind, string Name, string Description,
    string? SourceId, string? ComponentType, string? ControlId, string? Responsibility,
    IReadOnlyList<string> DependencyKeys, IReadOnlyList<string> UnresolvedDependencies,
    IReadOnlyList<CspPackageSourceCitation> Citations)
{
    /// <summary>Stated source metadata only; this is not an authorization verification or ATO decision.</summary>
    public CspPackageAuthorizationReferenceDraft? AuthorizationReference { get; init; }
    public CspPackageClaim? Claim { get; init; }
}

/// <summary>A private, unverified source reference. Calendar-only dates use midnight UTC.</summary>
public sealed record CspPackageAuthorizationReferenceDraft(
    string Reference, string? Issuer, DateTimeOffset? IssuedAt, DateTimeOffset? ExpiresAt);

/// <summary>Coverage never equates successful text extraction with complete semantic analysis.</summary>
public sealed record CspPackageAnalysisCoverage(
    int TotalEntries, int Processed, int Unsupported, int Unreadable, int Failed, int Excluded,
    long ExpandedBytes, int ExtractedCharacters, bool EnumerationComplete, bool AnalysisComplete);

/// <summary>Storage-ready output with stable source identities; all candidates still require portal review.</summary>
public sealed record CspPackageAnalysisResult(
    IReadOnlyList<CspPackageAnalyzedEntry> Entries,
    IReadOnlyList<CspPackageSourceSegment> Segments,
    IReadOnlyList<CspPackageCandidateDraft> Candidates,
    CspPackageAnalysisCoverage Coverage)
{
    public int AnalysisProfileVersion { get; init; } = 1;
    public IReadOnlyDictionary<string, IReadOnlyList<CspPackageFamilyCoverage>> FamilyCoverage { get; init; }
        = new Dictionary<string, IReadOnlyList<CspPackageFamilyCoverage>>();
    /// <summary>Generated extraction state, not human-edited review state; preserve bytes in protected storage.</summary>
    public CspPackageAnalysisCheckpoint? Checkpoint { get; init; }

    /// <summary>True for missing coverage, unread content, incomplete analysis or unresolved links.</summary>
    public bool NeedsAttention => !Coverage.EnumerationComplete || !Coverage.AnalysisComplete
        || Coverage.Unsupported + Coverage.Unreadable + Coverage.Failed > 0
        || Candidates.Any(candidate => candidate.UnresolvedDependencies.Count > 0
            || candidate.Claim?.Relationships.Any(relation => relation.Resolution != "Resolved") == true)
        || FamilyCoverage.Values.SelectMany(value => value).Any(value =>
            value.Status is not (CspPackageFamilyCoverageStatus.Analyzed or CspPackageFamilyCoverageStatus.NoDeclarations));
}

/// <summary>All original identities remain bound; only explicitly selected unfinished entries may be extracted.</summary>
public sealed record CspPackageAnalysisResumeRequest(
    IReadOnlyList<CspPackageAnalysisInput> Originals,
    CspPackageAnalysisCheckpoint Checkpoint,
    IReadOnlySet<string> RetryEntryKeys)
{
    /// <summary>Explicit profile enrichment; null preserves the retained profile.</summary>
    public int? TargetAnalysisProfileVersion { get; init; }
}

/// <summary>Versioned server-generated checkpoint; never accept this object from an untrusted client.</summary>
public sealed record CspPackageAnalysisCheckpoint(
    int Version,
    IReadOnlyList<CspPackageOriginalFingerprint> Originals,
    IReadOnlyList<CspPackageAnalyzedEntry> Entries,
    IReadOnlyList<CspPackageSourceSegment> Segments,
    IReadOnlyList<CspPackageCandidateDraft> Candidates,
    IReadOnlyDictionary<string, IReadOnlyList<string>> CandidateSourceReferences,
    IReadOnlyDictionary<string, CspPackageEntryProgress> Progress)
{
    public int SemanticCallLimit { get; init; } = 64;
    public IReadOnlyList<string> AutomaticContinuationEntryKeys { get; init; } = [];
    public CspPackageAnalysisProgress? AnalysisProgress { get; init; }
    public int AnalysisProfileVersion { get; init; } = 1;
    public IReadOnlyDictionary<string, IReadOnlyList<CspPackageFamilyCoverage>> FamilyCoverage { get; init; }
        = new Dictionary<string, IReadOnlyList<CspPackageFamilyCoverage>>();
}

/// <summary>Binds a checkpoint to the exact immutable upload and its metadata.</summary>
public sealed record CspPackageOriginalFingerprint(
    string ArtifactId, string FileName, string MediaType, string Sha256, long ByteLength);

/// <summary>Per-entry logical charges allow replacement of partial work without resetting completed-source budgets.</summary>
public sealed record CspPackageEntryProgress(
    int Depth, bool EnumerationComplete, long ExpandedBytesCharged, int PdfPagesCharged, string? ContentSha256)
{
    public int SemanticCallsCharged { get; init; }
    public int SemanticBatchSize { get; init; }
    public int PdfTextLayoutVersion { get; init; }
    /// <summary>Retained old segment keys mapped to their active layout views; old evidence is never rewritten.</summary>
    public IReadOnlyDictionary<string, string> PdfAnalysisViews { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> SemanticallyAnalyzedSegmentKeys { get; init; } = [];
    public bool PdfAttachmentsEnumerated { get; init; }
    public IReadOnlyDictionary<CspPackageClaimFamily, IReadOnlyList<string>> FamilyAnalyzedSegmentKeys { get; init; }
        = new Dictionary<CspPackageClaimFamily, IReadOnlyList<string>>();
}

/// <summary>Saved analysis progress, separate from human review and publication.</summary>
public sealed record CspPackageAnalysisProgress(int CompletedSegments, int TotalSegments,
    int ModelCalls, int ModelCallLimit, bool ContinuingAutomatically);

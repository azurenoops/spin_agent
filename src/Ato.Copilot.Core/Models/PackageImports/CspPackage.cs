using System.ComponentModel.DataAnnotations;

namespace Ato.Copilot.Core.Models.PackageImports;

/// <summary>Provider-private durable queue aggregate. Version fences every writer; Revision fences human decisions.</summary>
public sealed class CspPackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public Guid? OfferingId { get; set; }
    public Guid? PackageVersionId { get; set; }
    public Guid? BoundaryRevisionId { get; set; }
    [MaxLength(100)] public string IdempotencyKey { get; set; } = "";
    [MaxLength(64)] public string ContentHash { get; set; } = "";
    [MaxLength(256)] public string Name { get; set; } = "";
    [MaxLength(32)] public string ProcessingState { get; set; } = "Received";
    [MaxLength(32)] public string PublicationState { get; set; } = "Unpublished";
    public long Revision { get; set; } = 1;
    [ConcurrencyCheck] public long Version { get; set; } = 1;
    public Guid? LeaseId { get; set; }
    public long LeaseExpiresTicks { get; set; }
    public string? LastError { get; set; }
    public string RetryKeysJson { get; set; } = "[]";
    public string? AnalysisCheckpointJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string CreatedBy { get; set; } = "";
}

/// <summary>Original or expanded entry retained outside the public provider catalog.</summary>
public sealed class CspPackageEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    [MaxLength(64)] public string StableKey { get; set; } = "";
    public bool IsOriginal { get; set; }
    public Guid? OriginalEntryId { get; set; }
    [MaxLength(512)] public string FileName { get; set; } = "";
    public string ArchivePath { get; set; } = "";
    [MaxLength(256)] public string MediaType { get; set; } = "";
    public string? StorageKey { get; set; }
    public long ByteLength { get; set; }
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    [MaxLength(32)] public string Status { get; set; } = "Pending";
    public string? Reason { get; set; }
    public string? ExclusionReason { get; set; }
    public long Revision { get; set; } = 1;
    public string SegmentsJson { get; set; } = "[]";
}

/// <summary>Immutable extracted evidence plus revisioned human edits, not a globally readable catalog row.</summary>
public sealed class CspPackageCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    [MaxLength(64)] public string StableKey { get; set; } = "";
    [MaxLength(32)] public string Type { get; set; } = "";
    [MaxLength(32)] public string ReviewState { get; set; } = "NeedsReview";
    public long Revision { get; set; } = 1;
    public string PayloadJson { get; set; } = "{}";
    public string UnresolvedDependenciesJson { get; set; } = "[]";
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

/// <summary>Approval binds the complete selection and impact hash; canonical publication results share its transaction.</summary>
public sealed class CspPackageApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public long Revision { get; set; }
    public long CreatedVersion { get; set; }
    public long? PublishedVersion { get; set; }
    [MaxLength(64)] public string PreviewHash { get; set; } = "";
    public string SelectionJson { get; set; } = "[]";
    public string SnapshotJson { get; set; } = "{}";
    public string BlockersJson { get; set; } = "[]";
    [MaxLength(32)] public string State { get; set; } = "Preview";
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(30);
    [MaxLength(100)] public string? PublicationKey { get; set; }
    public string? PublicationJson { get; set; }
}

/// <summary>Append-only provider audit events, private with the package.</summary>
public sealed class CspPackageAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    [MaxLength(100)] public string Action { get; set; } = "";
    [MaxLength(254)] public string Actor { get; set; } = "";
    public long Revision { get; set; }
    public string Detail { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

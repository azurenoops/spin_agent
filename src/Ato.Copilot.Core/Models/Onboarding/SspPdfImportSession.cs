using Ato.Copilot.Core.Models.Tenancy.Attributes;
namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Per-PDF record for an SSP PDF ingestion session (Step 4). Multiple sessions can share a
/// <see cref="BatchId"/> when an admin uploads several PDFs at once.
/// </summary>
[TenantScoped]
public class SspPdfImportSession
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Batch grouping id; one per "Step 4 upload" admin action.</summary>
    public Guid BatchId { get; set; }

    /// <summary>Original client filename.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Storage key under <c>wizard/imports/ssp-pdf/{tenantId}/{Id}/{filename}</c>.</summary>
    public string StorageBlobKey { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>SHA-256 checksum (hex).</summary>
    public string ContentChecksumSha256 { get; set; } = string.Empty;

    /// <summary>Lifecycle status.</summary>
    public SspPdfStatus Status { get; set; } = SspPdfStatus.Uploaded;

    /// <summary>FK → extraction <see cref="WizardJobStatus.Id"/>.</summary>
    public Guid? ExtractJobId { get; set; }

    /// <summary>JSON: extracted field map with per-field confidence band.</summary>
    public string? ExtractionResult { get; set; }

    /// <summary>JSON: snapshot of analyst overrides applied to the extracted result.</summary>
    public string? UserCorrections { get; set; }

    /// <summary>Set when <see cref="Status"/> = <c>Rejected</c>.</summary>
    public SspPdfRejectReason? RejectReason { get; set; }

    /// <summary>FK → newly created <c>RegisteredSystem.Id</c> (existing entity) on import.</summary>
    public Guid? CreatedSystemId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UpdatedBy { get; set; }
}

/// <summary>Lifecycle status of an <see cref="SspPdfImportSession"/>.</summary>
public enum SspPdfStatus
{
    Uploaded,
    Extracting,
    Extracted,
    Imported,
    Rejected,
    Failed,
}

/// <summary>Reason an SSP PDF could not be ingested.</summary>
public enum SspPdfRejectReason
{
    Encrypted,
    PasswordProtected,
    ImageOnly,
    Unreadable,
    UnknownFramework,
}

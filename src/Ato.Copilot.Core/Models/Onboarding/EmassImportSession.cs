using Ato.Copilot.Core.Models.Tenancy.Attributes;
namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Per-upload record for an eMASS bulk import (Step 3). Each session represents a single
/// admin upload, parsed asynchronously through a <see cref="WizardJobStatus"/> chain
/// (`ParseJobId` → preview → `CommitJobId` → committed entities).
/// </summary>
[TenantScoped]
public class EmassImportSession
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Original client filename (preserved for replay per FR-065 / FR-095).</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Storage key under <c>wizard/imports/emass/{tenantId}/{Id}/{filename}</c>.</summary>
    public string StorageBlobKey { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>SHA-256 checksum (hex). Doubles as <c>SourceVersionTag</c> in dependencies.</summary>
    public string ContentChecksumSha256 { get; set; } = string.Empty;

    /// <summary>Detected file format.</summary>
    public EmassImportFormat Format { get; set; } = EmassImportFormat.Xlsx;

    /// <summary>Lifecycle status.</summary>
    public EmassImportStatus Status { get; set; } = EmassImportStatus.Uploaded;

    /// <summary>FK → parse <see cref="WizardJobStatus.Id"/>.</summary>
    public Guid? ParseJobId { get; set; }

    /// <summary>FK → commit <see cref="WizardJobStatus.Id"/>.</summary>
    public Guid? CommitJobId { get; set; }

    /// <summary>JSON preview: per-system / per-control / per-POA&amp;M counts and conflict resolutions.</summary>
    public string? Preview { get; set; }

    /// <summary>Last error message (truncated to 1024 chars).</summary>
    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UpdatedBy { get; set; }
}

/// <summary>Detected eMASS upload format.</summary>
public enum EmassImportFormat
{
    Xlsx,
    PackageZip,
}

/// <summary>Lifecycle status of an <see cref="EmassImportSession"/>.</summary>
public enum EmassImportStatus
{
    Uploaded,
    Parsing,
    Parsed,
    Importing,
    Imported,
    Failed,
}

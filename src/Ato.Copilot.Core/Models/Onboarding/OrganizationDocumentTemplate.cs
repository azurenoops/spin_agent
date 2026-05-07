namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Tenant-scoped custom document template (Step 6). At most one row per
/// <c>(TenantId, TemplateType)</c> may have <see cref="IsDefault"/> = <c>true</c>; this
/// invariant is enforced both by a filtered unique index and by service transactions
/// (data-model.md §"Default-template 'exactly one' invariant").
/// </summary>
public class OrganizationDocumentTemplate
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Type of artifact this template generates.</summary>
    public TemplateType TemplateType { get; set; }

    /// <summary>Admin-supplied display label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Free-text version string (e.g., <c>v1.2</c>, <c>2026-Q2</c>).</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Original client filename.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Storage key under <c>wizard/templates/{tenantId}/{Id}/{filename}</c>.</summary>
    public string StorageBlobKey { get; set; } = string.Empty;

    /// <summary>File format.</summary>
    public TemplateFileFormat FileFormat { get; set; } = TemplateFileFormat.Docx;

    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>SHA-256 checksum (hex).</summary>
    public string ContentChecksumSha256 { get; set; } = string.Empty;

    /// <summary>Whether this is the default template for its <see cref="TemplateType"/>.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Validation status from <c>OrganizationTemplateValidator</c>.</summary>
    public TemplateValidationStatus ValidationStatus { get; set; } = TemplateValidationStatus.Pending;

    /// <summary>JSON: list of placeholder / column warnings emitted by validation.</summary>
    public string? ValidationWarnings { get; set; }

    /// <summary>Lifecycle status.</summary>
    public TemplateStatus Status { get; set; } = TemplateStatus.Active;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Type of artifact a template generates.</summary>
public enum TemplateType
{
    Ssp,
    Sar,
    Sap,
    /// <summary>Control Responsibility Matrix.</summary>
    Crm,
    /// <summary>Hardware/Software Inventory.</summary>
    HwSwInventory,
}

/// <summary>Template file format.</summary>
public enum TemplateFileFormat
{
    Docx,
    Xlsx,
}

/// <summary>Template-validation lifecycle status.</summary>
public enum TemplateValidationStatus
{
    Pending,
    Compliant,
    FlaggedNonCompliant,
}

/// <summary>Lifecycle status of a template (separate from validation).</summary>
public enum TemplateStatus
{
    Active,
    Superseded,
    Deleted,
}

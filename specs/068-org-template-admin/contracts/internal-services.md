# Internal Services Contract — Spec 068: Org Templates & Narrative Seed Admin

**Epic:** #222 — Org Templates & Narrative Seed Admin UI
**Layer:** Application / Domain Services (C# interfaces + implementation outline)

---

## Table of Contents

1. [Result Types](#1-result-types)
2. [IOrganizationTemplateService](#2-iorganizationtemplateservice)
   - [Interface Definition](#21-interface-definition)
   - [Method Implementation Outlines](#22-method-implementation-outlines)
3. [INarrativeSeedDocumentService](#3-inarrativeseeддocumentservice)
   - [Interface Definition](#31-interface-definition)
   - [Method Implementation Outlines](#32-method-implementation-outlines)
4. [Shared Exceptions & Error Codes](#4-shared-exceptions--error-codes)
5. [Architecture Notes](#5-architecture-notes)

---

## 1. Result Types

These result DTOs are returned by service methods where a simple entity is not sufficient.

```csharp
namespace Org.Onboarding.Templates;

/// <summary>
/// Result returned by <see cref="IOrganizationTemplateService.UploadAsync"/>.
/// </summary>
public sealed record UploadTemplateResult
{
    /// <summary>The persisted template entity after upload.</summary>
    public required OrganizationDocumentTemplate Template { get; init; }

    /// <summary>
    /// Human-readable validation warnings discovered during content inspection.
    /// Empty if no warnings were raised.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Result returned by <see cref="IOrganizationTemplateService.ReplaceFileAsync"/>.
/// </summary>
public sealed record ReplaceTemplateResult
{
    /// <summary>The updated template entity after file replacement.</summary>
    public required OrganizationDocumentTemplate Template { get; init; }

    /// <summary>
    /// Number of dependent draft artifacts (e.g., SSPs) flagged for review
    /// because their bound template file changed.
    /// </summary>
    public required int DependentsFlagged { get; init; }
}

/// <summary>
/// Result returned by <see cref="INarrativeSeedDocumentService.UploadAsync"/>.
/// </summary>
public sealed record UploadSeedResult
{
    /// <summary>The persisted seed document entity.</summary>
    public required NarrativeSeedDocument Document { get; init; }

    /// <summary>
    /// The async indexing job ID, or <c>null</c> if no indexing job was queued
    /// (e.g., feature-flagged off or synchronous path taken).
    /// </summary>
    public Guid? JobId { get; init; }
}
```

---

## 2. IOrganizationTemplateService

### 2.1 Interface Definition

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Org.Onboarding.Templates;

/// <summary>
/// Manages the lifecycle of organization document templates (SSP, SAR, SAP, CRM,
/// HW/SW Inventory). All operations are scoped to a tenant; callers must supply
/// the resolved <paramref name="tenantId"/> from the authenticated HTTP context.
/// </summary>
public interface IOrganizationTemplateService
{
    /// <summary>
    /// Returns all document templates for a tenant, optionally filtered by type
    /// and/or including soft-deleted records.
    /// </summary>
    /// <param name="tenantId">Resolved tenant ID from auth context.</param>
    /// <param name="type">
    ///     When non-null, restricts results to templates of this type.
    ///     Pass <c>null</c> to return all types.
    /// </param>
    /// <param name="includeDeleted">
    ///     When <c>true</c>, includes templates with
    ///     <see cref="TemplateStatus.Deleted"/>. Defaults to <c>false</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only ordered list of matching templates.</returns>
    Task<IReadOnlyList<OrganizationDocumentTemplate>> ListAsync(
        Guid tenantId,
        TemplateType? type,
        bool includeDeleted,
        CancellationToken ct);

    /// <summary>
    /// Retrieves a single template by ID, scoped to the tenant.
    /// </summary>
    /// <param name="tenantId">Resolved tenant ID from auth context.</param>
    /// <param name="id">Template GUID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     The matching template, or <c>null</c> if not found or belonging to
    ///     a different tenant.
    /// </returns>
    Task<OrganizationDocumentTemplate?> GetAsync(
        Guid tenantId,
        Guid id,
        CancellationToken ct);

    /// <summary>
    /// Validates, stores, and registers a new document template.
    /// </summary>
    /// <param name="tenantId">Resolved tenant ID from auth context.</param>
    /// <param name="actorId">ID of the authenticated user performing the action.</param>
    /// <param name="type">Template type (<see cref="TemplateType"/>).</param>
    /// <param name="label">Human-readable label.</param>
    /// <param name="version">Version string (e.g., "2024-Q4").</param>
    /// <param name="fileName">Original client-supplied file name, stored for download.</param>
    /// <param name="content">Seekable stream of the file contents.</param>
    /// <param name="sizeBytes">File size in bytes; used for pre-validation before streaming.</param>
    /// <param name="isDefault">
    ///     If <c>true</c>, this template is marked as default for its type;
    ///     any prior default of the same type is demoted atomically.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="UploadTemplateResult"/> containing the saved entity and warnings.</returns>
    /// <exception cref="TemplateFileTooLargeException">
    ///     Thrown when <paramref name="sizeBytes"/> exceeds the configured maximum.
    /// </exception>
    /// <exception cref="TemplateWrongFormatException">
    ///     Thrown when <paramref name="fileName"/> extension is not .docx or .xlsx,
    ///     or when content inspection fails.
    /// </exception>
    Task<UploadTemplateResult> UploadAsync(
        Guid tenantId,
        Guid actorId,
        TemplateType type,
        string label,
        string version,
        string fileName,
        Stream content,
        long sizeBytes,
        bool isDefault,
        CancellationToken ct);

    /// <summary>
    /// Updates the metadata fields (<paramref name="label"/> and/or
    /// <paramref name="version"/>) of an existing template.
    /// File content and <c>templateType</c> cannot be changed via this method.
    /// </summary>
    /// <param name="tenantId">Resolved tenant ID from auth context.</param>
    /// <param name="id">Template GUID.</param>
    /// <param name="actorId">ID of the authenticated user performing the action.</param>
    /// <param name="label">New label, or <c>null</c> to leave unchanged.</param>
    /// <param name="version">New version string, or <c>null</c> to leave unchanged.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated template entity.</returns>
    /// <exception cref="TemplateNotFoundException">
    ///     Thrown when no template with <paramref name="id"/> exists for the tenant.
    /// </exception>
    Task<OrganizationDocumentTemplate> PatchMetadataAsync(
        Guid tenantId,
        Guid id,
        Guid actorId,
        string? label,
        string? version,
        CancellationToken ct);

    /// <summary>
    /// Soft-deletes a template by setting its status to
    /// <see cref="TemplateStatus.Deleted"/> and recording <c>deletedAt</c>.
    /// </summary>
    /// <param name="tenantId">Resolved tenant ID from auth context.</param>
    /// <param name="id">Template GUID.</param>
    /// <param name="actorId">ID of the authenticated user performing the action.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TemplateNotFoundException">
    ///     Thrown when no template with <paramref name="id"/> exists for the tenant.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown with error code <c>TEMPLATE_DEFAULT_PROTECTED</c> when the template
    ///     is currently marked as default. Call
    ///     <see cref="ClearDefaultAsync"/> first.
    /// </exception>
    Task DeleteAsync(
        Guid tenantId,
        Guid id,
        Guid actorId,
        CancellationToken ct);

    /// <summary>
    /// Opens a readable stream for the raw template file from blob storage.
    /// </summary>
    /// <param name="tenantId">Resolved tenant ID from auth context.</param>
    /// <param name="id">Template GUID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     A readable <see cref="Stream"/> positioned at offset 0, or <c>null</c>
    ///     if the template does not exist for this tenant.
    /// </returns>
    /// <remarks>
    ///     Caller is responsible for disposing the returned stream.
    ///     The blob key is resolved internally; it is never surfaced to callers.
    /// </remarks>
    Task<Stream?> DownloadAsync(
        Guid tenantId,
        Guid id,
        CancellationToken ct);

    /// <summary>
    /// Replaces the binary content of an existing template with a new file,
    /// then flags all dependent draft artifacts for re-generation review.
    /// </summary>
    /// <param name="tenantId">Resolved tenant ID from auth context.</param>
    /// <param name="id">Template GUID.</param>
    /// <param name="actorId">ID of the authenticated user performing the action.</param>
    /// <param name="fileName">Original client-supplied file name for the replacement.</param>
    /// <param name="content">Seekable stream of the replacement file contents.</param>
    /// <param name="sizeBytes">File size in bytes.</param>
    /// <param name="version">
    ///     New version string, or <c>null</c> to retain the existing version.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="ReplaceTemplateResult"/> with updated entity and dependent count.</returns>
    /// <exception cref="TemplateFileTooLargeException">File exceeds maximum size.</exception>
    /// <exception cref="TemplateNotFoundException">Template not found for tenant.</exception>
    Task<ReplaceTemplateResult> ReplaceFileAsync(
        Guid tenantId,
        Guid id,
        Guid actorId,
        string fileName,
        Stream content,
        long sizeBytes,
        string? version,
        CancellationToken ct);

    /// <summary>
    /// Marks a template as the default for its <see cref="TemplateType"/>.
    /// Any previously default template of the same type is atomically demoted
    /// (<c>isDefault = false</c>) within the same transaction.
    /// </summary>
    /// <param name="tenantId">Resolved tenant ID from auth context.</param>
    /// <param name="id">Template GUID.</param>
    /// <param name="actorId">ID of the authenticated user performing the action.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated template entity with <c>IsDefault = true</c>.</returns>
    /// <exception cref="TemplateNotFoundException">Template not found for tenant.</exception>
    Task<OrganizationDocumentTemplate> MarkDefaultAsync(
        Guid tenantId,
        Guid id,
        Guid actorId,
        CancellationToken ct);

    /// <summary>
    /// Removes the default designation from a template.
    /// After this call, <c>isDefault</c> is <c>false</c>. No other template
    /// is automatically promoted to default.
    /// </summary>
    /// <param name="tenantId">Resolved tenant ID from auth context.</param>
    /// <param name="id">Template GUID.</param>
    /// <param name="actorId">ID of the authenticated user performing the action.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TemplateNotFoundException">Template not found for tenant.</exception>
    Task ClearDefaultAsync(
        Guid tenantId,
        Guid id,
        Guid actorId,
        CancellationToken ct);
}
```

---

### 2.2 Method Implementation Outlines

#### `ListAsync`
1. Query the `OrganizationDocumentTemplates` table filtered by `TenantId = tenantId`.
2. If `type != null`, add `WHERE TemplateType = @type`.
3. If `includeDeleted == false`, add `WHERE Status != 'Deleted'`.
4. Order by `CreatedAt DESC` (most recent first).
5. Map to domain entities and return.

#### `GetAsync`
1. Query by `Id = id AND TenantId = tenantId`.
2. Return `null` on no match (caller maps to 404).

#### `UploadAsync`
1. **Pre-validate size:** if `sizeBytes > configuredMaxBytes`, throw `TemplateFileTooLargeException`.
2. **Validate extension:** derive `FileFormat` from `fileName` extension (`.docx` → `Docx`, `.xlsx` → `Xlsx`); throw `TemplateWrongFormatException` for any other extension.
3. **Content inspection:** open stream, run format-specific content check (e.g., valid OOXML structure); collect `warnings`.
4. **Blob storage:** write stream to blob store; record returned `storageBlobKey` (internal only — never returned to callers).
5. **Compute checksum:** SHA-256 over stream content.
6. **Persist entity:** insert `OrganizationDocumentTemplate` row with `ValidationStatus = Pending`, `Status = Active`.
7. **Mark default (optional):** within the same DB transaction, if `isDefault == true`, set `IsDefault = false` on any existing default of same type, then set `IsDefault = true` on new template.
8. Return `UploadTemplateResult { Template, Warnings }`.

#### `PatchMetadataAsync`
1. Load entity by `Id + TenantId`; throw `TemplateNotFoundException` on miss.
2. Apply non-null fields: `label`, `version`.
3. Update `UpdatedAt = now`, `UpdatedBy = actorId`.
4. Persist and return updated entity.

#### `DeleteAsync`
1. Load entity by `Id + TenantId`; throw `TemplateNotFoundException` on miss.
2. If `entity.IsDefault == true`, throw `InvalidOperationException("TEMPLATE_DEFAULT_PROTECTED")`.
3. Set `Status = Deleted`, `DeletedAt = now`, `UpdatedAt = now`, `UpdatedBy = actorId`.
4. Persist.
5. **Do NOT remove blob** — blob is retained for audit / recovery purposes.

#### `DownloadAsync`
1. Load entity by `Id + TenantId`; return `null` on miss.
2. Resolve blob using `entity.StorageBlobKey` (internal field; never passed through public API).
3. Open and return blob read stream; caller is responsible for disposal and streaming to HTTP response with `Content-Disposition: attachment; filename="<entity.OriginalFileName>"`.

#### `ReplaceFileAsync`
1. Load entity; throw `TemplateNotFoundException` on miss.
2. Validate size (throw `TemplateFileTooLargeException` if exceeded).
3. Write new blob to storage; record new `StorageBlobKey`.
4. Compute new SHA-256 checksum.
5. Update entity: `StorageBlobKey`, `OriginalFileName`, `FileSizeBytes`, `ContentChecksumSha256`, `FileFormat`, optionally `Version`, reset `ValidationStatus = Pending`, `UpdatedAt`, `UpdatedBy`.
6. **Flag dependents:** query all draft artifacts bound to this template ID; set a `TemplateStale = true` flag on each. Return count as `DependentsFlagged`.
7. Persist all changes in a single transaction.
8. Return `ReplaceTemplateResult`.

#### `MarkDefaultAsync`
1. Load entity; throw `TemplateNotFoundException` on miss.
2. In a single DB transaction:
   a. `UPDATE ... SET IsDefault = false WHERE TenantId = @tenantId AND TemplateType = @type AND IsDefault = true AND Id != @id`
   b. `UPDATE ... SET IsDefault = true WHERE Id = @id`
3. Update `UpdatedAt`, `UpdatedBy`.
4. Return refreshed entity.

#### `ClearDefaultAsync`
1. Load entity; throw `TemplateNotFoundException` on miss.
2. Set `IsDefault = false`, `UpdatedAt = now`, `UpdatedBy = actorId`.
3. Persist.

---

## 3. INarrativeSeedDocumentService

### 3.1 Interface Definition

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Org.Onboarding.NarrativeSeeds;

/// <summary>
/// Manages narrative seed PDF documents used to prime AI-assisted wizard narratives.
/// All operations are scoped to a tenant.
/// </summary>
public interface INarrativeSeedDocumentService
{
    /// <summary>
    /// Returns all narrative seed documents for the tenant.
    /// </summary>
    /// <param name="tenantId">Resolved tenant ID from auth context.</param>
    /// <param name="includeDeleted">
    ///     When <c>true</c>, includes seeds with <see cref="SeedStatus.Deleted"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of seed documents.</returns>
    Task<IReadOnlyList<NarrativeSeedDocument>> ListAsync(
        Guid tenantId,
        bool includeDeleted,
        CancellationToken ct);

    /// <summary>
    /// Uploads a PDF seed document, stores it, and enqueues an async indexing job.
    /// </summary>
    /// <param name="tenantId">Resolved tenant ID from auth context.</param>
    /// <param name="actorId">ID of the authenticated user performing the action.</param>
    /// <param name="label">Human-readable label for this seed.</param>
    /// <param name="tags">
    ///     Zero or more classification tags (e.g., "policy", "ac").
    ///     Stored internally as a JSON string.
    /// </param>
    /// <param name="fileName">Original client-supplied file name.</param>
    /// <param name="contentType">MIME type of the uploaded file.</param>
    /// <param name="content">Readable stream of PDF file bytes.</param>
    /// <param name="sizeBytes">File size; used for pre-validation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     <see cref="UploadSeedResult"/> containing the persisted document and
    ///     optional async <c>JobId</c>.
    /// </returns>
    /// <exception cref="SeedPdfUnreadableException">
    ///     Thrown when the file is too large or cannot be parsed as a PDF.
    ///     Maps to HTTP 413 with error code <c>SspPdfUnreadable</c>.
    /// </exception>
    Task<UploadSeedResult> UploadAsync(
        Guid tenantId,
        Guid actorId,
        string label,
        IList<string> tags,
        string fileName,
        string contentType,
        Stream content,
        long sizeBytes,
        CancellationToken ct);

    /// <summary>
    /// Soft-deletes a narrative seed document.
    /// </summary>
    /// <param name="tenantId">Resolved tenant ID from auth context.</param>
    /// <param name="id">Seed document GUID.</param>
    /// <param name="actorId">ID of the authenticated user performing the action.</param>
    /// <param name="confirmCitations">
    ///     When <c>false</c> (default), deletion is blocked if active citations
    ///     reference this seed and <see cref="SeedHasCitationsException"/> is thrown.
    ///     When <c>true</c>, citations are force-removed and deletion proceeds.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="SeedNotFoundException">
    ///     Thrown when no seed with <paramref name="id"/> exists for the tenant.
    /// </exception>
    /// <exception cref="SeedHasCitationsException">
    ///     Thrown when the seed has active wizard citations and
    ///     <paramref name="confirmCitations"/> is <c>false</c>.
    ///     Maps to HTTP 409 with error code <c>WIZARD_NARRATIVE_SEED_HAS_CITATIONS</c>.
    /// </exception>
    Task DeleteAsync(
        Guid tenantId,
        Guid id,
        Guid actorId,
        bool confirmCitations,
        CancellationToken ct);
}
```

---

### 3.2 Method Implementation Outlines

#### `ListAsync`
1. Query `NarrativeSeedDocuments` filtered by `TenantId = tenantId`.
2. If `includeDeleted == false`, add `WHERE Status != 'Deleted'`.
3. Order by `CreatedAt DESC`.
4. Return mapped domain list.

#### `UploadAsync`
1. **Pre-validate size:** if `sizeBytes > configuredMaxBytes`, throw `SeedPdfUnreadableException`.
2. **Validate PDF:** attempt to open stream as a PDF (e.g., using a PDF library); throw `SeedPdfUnreadableException` if parsing fails.
3. **Persist to blob storage:** write bytes; record blob key internally.
4. **Create EvidenceArtifact:** insert a linked evidence artifact record; capture `evidenceArtifactId`.
5. **Serialize tags:** `JsonSerializer.Serialize(tags)` → store as JSON string.
6. **Persist `NarrativeSeedDocument`:** insert row with `IndexingStatus = Pending`, `Status = Active`.
7. **Enqueue indexing job:** dispatch background job (e.g., via message bus). Capture `jobId` if returned; may be `null` when feature-flagged off.
8. Update `IndexJobId` field on the persisted entity with `jobId` (if non-null).
9. Return `UploadSeedResult { Document, JobId }`.

#### `DeleteAsync`
1. Load entity by `Id + TenantId`; throw `SeedNotFoundException` on miss.
2. If `confirmCitations == false`:
   a. Count active citations referencing this seed.
   b. If count > 0, throw `SeedHasCitationsException`.
3. If `confirmCitations == true` and citations exist:
   a. Remove or nullify citation references in a cascading update.
4. Set `Status = Deleted`, `DeletedAt = now`, `UpdatedAt = now`, `UpdatedBy = actorId`.
5. Persist (single transaction covering citation cleanup + status update).

---

## 4. Shared Exceptions & Error Codes

```csharp
namespace Org.Onboarding.Exceptions;

/// <summary>Base class for domain exceptions that map to specific HTTP error codes.</summary>
public abstract class OnboardingDomainException : Exception
{
    public string ErrorCode { get; }
    protected OnboardingDomainException(string errorCode, string message)
        : base(message) => ErrorCode = errorCode;
}

/// <summary>
/// Thrown when a template file exceeds the maximum allowed size.
/// Maps to HTTP 413 / <c>TEMPLATE_TOO_LARGE</c>.
/// </summary>
public sealed class TemplateFileTooLargeException : OnboardingDomainException
{
    public TemplateFileTooLargeException()
        : base("TEMPLATE_TOO_LARGE", "The uploaded template file exceeds the maximum allowed size.") { }
}

/// <summary>
/// Thrown when a template file has an unsupported format or fails content inspection.
/// Maps to HTTP 415 / <c>TEMPLATE_WRONG_FORMAT</c>.
/// </summary>
public sealed class TemplateWrongFormatException : OnboardingDomainException
{
    public TemplateWrongFormatException(string detail)
        : base("TEMPLATE_WRONG_FORMAT", $"The template file format is not supported: {detail}") { }
}

/// <summary>
/// Thrown when a template cannot be found for the given tenant.
/// Maps to HTTP 404.
/// </summary>
public sealed class TemplateNotFoundException : OnboardingDomainException
{
    public TemplateNotFoundException(Guid id)
        : base("TEMPLATE_NOT_FOUND", $"Template '{id}' was not found.") { }
}

/// <summary>
/// Thrown when attempting to delete a template currently marked as default.
/// Maps to HTTP 409 / <c>TEMPLATE_DEFAULT_PROTECTED</c>.
/// </summary>
public sealed class TemplateDefaultProtectedException : OnboardingDomainException
{
    public TemplateDefaultProtectedException(Guid id)
        : base("TEMPLATE_DEFAULT_PROTECTED",
               $"Template '{id}' is the default template and cannot be deleted. Clear its default status first.") { }
}

/// <summary>
/// Thrown when a seed PDF file is too large or cannot be parsed.
/// Maps to HTTP 413 / <c>SspPdfUnreadable</c>.
/// </summary>
public sealed class SeedPdfUnreadableException : OnboardingDomainException
{
    public SeedPdfUnreadableException(string detail)
        : base("SspPdfUnreadable", $"The seed PDF could not be read: {detail}") { }
}

/// <summary>
/// Thrown when a narrative seed cannot be found for the given tenant.
/// Maps to HTTP 404.
/// </summary>
public sealed class SeedNotFoundException : OnboardingDomainException
{
    public SeedNotFoundException(Guid id)
        : base("SEED_NOT_FOUND", $"Narrative seed '{id}' was not found.") { }
}

/// <summary>
/// Thrown when a seed has active citations and deletion was requested without confirmation.
/// Maps to HTTP 409 / <c>WIZARD_NARRATIVE_SEED_HAS_CITATIONS</c>.
/// </summary>
public sealed class SeedHasCitationsException : OnboardingDomainException
{
    public int CitationCount { get; }

    public SeedHasCitationsException(Guid id, int citationCount)
        : base("WIZARD_NARRATIVE_SEED_HAS_CITATIONS",
               $"Narrative seed '{id}' has {citationCount} active citation(s). Pass confirmCitations=true to force delete.")
    {
        CitationCount = citationCount;
    }
}
```

### Exception-to-HTTP Mapping

| Exception | HTTP Status | `errorCode` |
|-----------|-------------|-------------|
| `TemplateFileTooLargeException` | `413` | `TEMPLATE_TOO_LARGE` |
| `TemplateWrongFormatException` | `415` | `TEMPLATE_WRONG_FORMAT` |
| `TemplateNotFoundException` | `404` | *(no body)* |
| `TemplateDefaultProtectedException` | `409` | `TEMPLATE_DEFAULT_PROTECTED` |
| `SeedPdfUnreadableException` | `413` | `SspPdfUnreadable` |
| `SeedNotFoundException` | `404` | *(no body)* |
| `SeedHasCitationsException` | `409` | `WIZARD_NARRATIVE_SEED_HAS_CITATIONS` |

Register a global exception filter / middleware that catches `OnboardingDomainException` and writes the standard error envelope `{ ok, errorCode, message, suggestion }`.

---

## 5. Architecture Notes

### Tenant Scoping
Every service method accepts `tenantId` as an explicit parameter. Services **must never** cross tenant boundaries. The endpoint handlers resolve `tenantId` from the authenticated claims before invoking the service.

### `storageBlobKey` Isolation
`StorageBlobKey` is an infrastructure concern stored on the entity but never projected into API response DTOs. The download path in `DownloadAsync` resolves the blob internally. Ensure response projection strips this field unconditionally.

### Default Template Atomicity
The `MarkDefaultAsync` demote-then-promote pattern must execute inside a single database transaction to avoid a window where zero or two templates are simultaneously marked as default. Use `SELECT FOR UPDATE` or an equivalent optimistic-concurrency mechanism.

### Blob Retention on Delete
Soft-deletion does **not** remove blobs. Blob cleanup (if desired) should be handled by a separate background job after a configurable retention window, keyed off `deletedAt`.

### Tags Serialization
`INarrativeSeedDocumentService.UploadAsync` accepts `IList<string> tags` and the implementation serializes to JSON (`"[\"policy\",\"ac\"]"`) before persistence. The HTTP endpoint handler parses the repeated form field `tags[]` into `IList<string>` before calling the service.

### Dependency Registration (example)
```csharp
builder.Services.AddScoped<IOrganizationTemplateService, OrganizationTemplateService>();
builder.Services.AddScoped<INarrativeSeedDocumentService, NarrativeSeedDocumentService>();
```

using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Result envelope from a template upload (FR-080..FR-088). Includes any
/// validator warnings; the caller decides whether to surface them to the
/// admin (UI may show "accepted with warnings").
/// </summary>
public sealed record TemplateUploadResult(
    OrganizationDocumentTemplate Template,
    IReadOnlyList<string> Warnings);

/// <summary>Result of replacing a template's underlying file (FR-093..FR-095 cascade).</summary>
public sealed record TemplateReplaceResult(
    OrganizationDocumentTemplate Template,
    int DependentsFlagged,
    IReadOnlyList<Guid> SuggestedReRunDependencyIds);

/// <summary>
/// Step 6 service surface. Owns upload, list, replace, mark-default, and
/// delete flows plus the default-uniqueness invariant (FR-085, FR-093..FR-096).
/// </summary>
public interface IOrganizationTemplateService
{
    Task<IReadOnlyList<OrganizationDocumentTemplate>> ListAsync(
        Guid tenantId,
        TemplateType? templateType = null,
        bool includeDeleted = false,
        CancellationToken ct = default);

    Task<OrganizationDocumentTemplate?> GetAsync(
        Guid tenantId, Guid templateId, CancellationToken ct = default);

    Task<TemplateUploadResult> UploadAsync(
        Guid tenantId,
        Guid actorUserId,
        TemplateType templateType,
        string label,
        string version,
        string originalFileName,
        Stream content,
        long lengthBytes,
        bool isDefault,
        CancellationToken ct = default);

    Task<OrganizationDocumentTemplate> PatchMetadataAsync(
        Guid tenantId,
        Guid templateId,
        Guid actorUserId,
        string? label,
        string? version,
        CancellationToken ct = default);

    Task DeleteAsync(
        Guid tenantId, Guid templateId, Guid actorUserId, CancellationToken ct = default);

    Task<Stream?> DownloadAsync(
        Guid tenantId, Guid templateId, CancellationToken ct = default);

    Task<TemplateReplaceResult> ReplaceFileAsync(
        Guid tenantId,
        Guid templateId,
        Guid actorUserId,
        string originalFileName,
        Stream content,
        long lengthBytes,
        string? version,
        CancellationToken ct = default);

    Task<OrganizationDocumentTemplate> MarkDefaultAsync(
        Guid tenantId, Guid templateId, Guid actorUserId, CancellationToken ct = default);

    Task ClearDefaultAsync(
        Guid tenantId, Guid templateId, Guid actorUserId, CancellationToken ct = default);
}

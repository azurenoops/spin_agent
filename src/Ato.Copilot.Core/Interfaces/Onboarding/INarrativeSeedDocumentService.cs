using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>Result envelope for a narrative-seed upload.</summary>
public sealed record NarrativeSeedUploadResult(
    NarrativeSeedDocument Document,
    Guid? IndexJobId);

/// <summary>
/// Step 7 service: tenant-scoped narrative seed-document storage and indexing.
/// Delegates byte storage to the existing evidence pipeline (FR-051) and tracks
/// onboarding-specific metadata. Deletion is blocked when citation markers
/// already reference the seed unless caller passes <c>confirmCitations</c>.
/// </summary>
public interface INarrativeSeedDocumentService
{
    Task<IReadOnlyList<NarrativeSeedDocument>> ListAsync(
        Guid tenantId, bool includeDeleted = false, CancellationToken ct = default);

    Task<NarrativeSeedDocument?> GetAsync(
        Guid tenantId, Guid documentId, CancellationToken ct = default);

    Task<NarrativeSeedUploadResult> UploadAsync(
        Guid tenantId,
        Guid actorUserId,
        string label,
        IReadOnlyList<string> tags,
        string originalFileName,
        string contentType,
        Stream content,
        long lengthBytes,
        CancellationToken ct = default);

    Task DeleteAsync(
        Guid tenantId,
        Guid documentId,
        Guid actorUserId,
        bool confirmCitations,
        CancellationToken ct = default);
}

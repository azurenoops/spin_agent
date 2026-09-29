using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

public interface IControlValidationLinkService
{
    Task<ControlValidationLink> AddEvidenceLinkAsync(string systemId, string controlId, string evidenceId,
        string expectedHash, string addedBy, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ControlValidationLink>> GetLinksAsync(
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default);

    Task<ControlValidationLink> AddLinkAsync(
        string systemId,
        string controlId,
        ControlValidationLinkType linkType,
        string linkTarget,
        string? description,
        string addedBy,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteLinkAsync(
        string linkId,
        string deletedBy,
        CancellationToken cancellationToken = default);

    Task<bool> UpsertScanLinkAsync(
        string systemId,
        string controlId,
        string scanFindingRef,
        string? description,
        CancellationToken cancellationToken = default);
}

public sealed class ControlImplementationNotFoundException(string systemId, string controlId)
    : InvalidOperationException($"Control implementation '{controlId}' was not found for system '{systemId}'.");

public sealed class DuplicateControlValidationLinkException(string linkTarget)
    : InvalidOperationException($"Validation link target '{linkTarget}' is already attached to this control.");
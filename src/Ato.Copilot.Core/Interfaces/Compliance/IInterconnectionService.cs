using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for system interconnection registry, ISA/MOU agreement tracking, and agreement validation.
/// Implements NIST SP 800-47 / DoD Instruction 8510.01 interconnection documentation requirements.
/// </summary>
public interface IInterconnectionService
{
    /// <summary>
    /// Register a new system interconnection crossing the authorization boundary.
    /// Clears HasNoExternalInterconnections flag if set.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID (GUID string).</param>
    /// <param name="targetSystemName">Name of external system.</param>
    /// <param name="interconnectionType">Connection type per NIST SP 800-47.</param>
    /// <param name="dataFlowDirection">Direction of data flow.</param>
    /// <param name="dataClassification">Data classification level.</param>
    /// <param name="createdBy">Identity of the user registering the interconnection.</param>
    /// <param name="targetSystemOwner">Organization/POC owning target system.</param>
    /// <param name="targetSystemAcronym">Target system abbreviation.</param>
    /// <param name="dataDescription">Description of data exchanged.</param>
    /// <param name="protocolsUsed">Protocols used (e.g., "TLS 1.3").</param>
    /// <param name="portsUsed">Ports used (e.g., "443").</param>
    /// <param name="securityMeasures">Security controls applied.</param>
    /// <param name="authenticationMethod">How systems authenticate to each other.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Interconnection registration result.</returns>
    Task<InterconnectionResult> AddInterconnectionAsync(
        string systemId,
        string targetSystemName,
        InterconnectionType interconnectionType,
        DataFlowDirection dataFlowDirection,
        string dataClassification,
        string createdBy,
        string? targetSystemOwner = null,
        string? targetSystemAcronym = null,
        string? dataDescription = null,
        List<string>? protocolsUsed = null,
        List<string>? portsUsed = null,
        List<string>? securityMeasures = null,
        string? authenticationMethod = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List interconnections for a system with optional status filter.
    /// Includes agreement summary per interconnection.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID (GUID string).</param>
    /// <param name="statusFilter">Optional status filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of interconnection results.</returns>
    Task<List<InterconnectionResult>> ListInterconnectionsAsync(
        string systemId,
        InterconnectionStatus? statusFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing interconnection's details or status.
    /// Requires StatusReason for Suspended/Terminated status changes.
    /// </summary>
    /// <param name="interconnectionId">Interconnection ID (GUID string).</param>
    /// <param name="targetSystemName">Updated target system name.</param>
    /// <param name="interconnectionType">Updated connection type.</param>
    /// <param name="dataFlowDirection">Updated data flow direction.</param>
    /// <param name="dataClassification">Updated data classification.</param>
    /// <param name="dataDescription">Updated data description.</param>
    /// <param name="protocolsUsed">Updated protocols.</param>
    /// <param name="portsUsed">Updated ports.</param>
    /// <param name="securityMeasures">Updated security measures.</param>
    /// <param name="authenticationMethod">Updated authentication method.</param>
    /// <param name="status">Updated status.</param>
    /// <param name="statusReason">Reason for suspension/termination (required for Suspended/Terminated).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="targetSystemOwner">Updated owner; null leaves unchanged, empty clears.</param>
    /// <param name="targetSystemAcronym">Updated acronym; null leaves unchanged, empty clears.</param>
    /// <returns>Updated interconnection result.</returns>
    Task<InterconnectionResult> UpdateInterconnectionAsync(
        string interconnectionId,
        string? targetSystemName = null,
        InterconnectionType? interconnectionType = null,
        DataFlowDirection? dataFlowDirection = null,
        string? dataClassification = null,
        string? dataDescription = null,
        List<string>? protocolsUsed = null,
        List<string>? portsUsed = null,
        List<string>? securityMeasures = null,
        string? authenticationMethod = null,
        InterconnectionStatus? status = null,
        string? statusReason = null,
        CancellationToken cancellationToken = default,
        string? targetSystemOwner = null,
        string? targetSystemAcronym = null);

    /// <summary>
    /// Generate an ISA document using NIST 800-47 7-section structure.
    /// AI-drafts the document from interconnection and system data.
    /// </summary>
    /// <param name="interconnectionId">Interconnection ID (GUID string).</param>
    /// <param name="createdBy">Identity of the user generating the ISA.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>ISA generation result with document content.</returns>
    Task<IsaGenerationResult> GenerateIsaAsync(
        string interconnectionId,
        string createdBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Register an agreement (ISA/MOU/SLA) for an interconnection.
    /// </summary>
    /// <param name="interconnectionId">Parent interconnection ID.</param>
    /// <param name="agreementType">Agreement classification.</param>
    /// <param name="title">Agreement title.</param>
    /// <param name="createdBy">Identity of the user registering the agreement.</param>
    /// <param name="documentReference">URL or path to agreement document.</param>
    /// <param name="status">Agreement status.</param>
    /// <param name="effectiveDate">When agreement becomes effective.</param>
    /// <param name="expirationDate">When agreement expires.</param>
    /// <param name="signedByLocal">Local signatory.</param>
    /// <param name="signedByRemote">Remote signatory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created agreement.</returns>
    Task<InterconnectionAgreement> RegisterAgreementAsync(
        string interconnectionId,
        AgreementType agreementType,
        string title,
        string createdBy,
        string? documentReference = null,
        AgreementStatus status = AgreementStatus.Draft,
        DateTime? effectiveDate = null,
        DateTime? expirationDate = null,
        string? signedByLocal = null,
        string? signedByRemote = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing agreement's status, metadata, or signatories.
    /// Validates status transitions; prevents updates to Terminated agreements (except review_notes).
    /// </summary>
    /// <param name="agreementId">Agreement ID (GUID string).</param>
    /// <param name="status">Updated status.</param>
    /// <param name="effectiveDate">Updated effective date.</param>
    /// <param name="expirationDate">Updated expiration date.</param>
    /// <param name="signedByLocal">Updated local signatory.</param>
    /// <param name="signedByLocalDate">Updated local signature date.</param>
    /// <param name="signedByRemote">Updated remote signatory.</param>
    /// <param name="signedByRemoteDate">Updated remote signature date.</param>
    /// <param name="reviewNotes">Updated review notes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated agreement.</returns>
    Task<InterconnectionAgreement> UpdateAgreementAsync(
        string agreementId,
        AgreementStatus? status = null,
        DateTime? effectiveDate = null,
        DateTime? expirationDate = null,
        string? signedByLocal = null,
        DateTime? signedByLocalDate = null,
        string? signedByRemote = null,
        DateTime? signedByRemoteDate = null,
        string? reviewNotes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Set or clear HasNoExternalInterconnections certification on a system.
    /// Rejects certification if Active interconnections exist.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID (GUID string).</param>
    /// <param name="certify">True to certify no interconnections; false to clear certification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CertifyNoInterconnectionsAsync(
        string systemId,
        bool certify,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate all active interconnections have signed, current agreements.
    /// Supports HasNoExternalInterconnections bypass.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID (GUID string).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Agreement validation result with per-interconnection details.</returns>
    Task<AgreementValidationResult> ValidateAgreementsAsync(
        string systemId,
        CancellationToken cancellationToken = default);
}

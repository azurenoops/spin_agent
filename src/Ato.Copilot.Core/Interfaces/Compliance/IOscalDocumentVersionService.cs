namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Tracks UUID lifecycle for generated OSCAL documents per system (Feature 076 — T003).
/// Ensures every substantive document modification produces a new UUID per OSCAL spec.
/// </summary>
public interface IOscalDocumentVersionService
{
    /// <summary>
    /// Issue a new document UUID for <paramref name="systemId"/> and <paramref name="documentType"/>,
    /// recording lineage from any previous UUID. Called once per export operation.
    /// </summary>
    Task<OscalDocumentVersionRecord> IssueAsync(
        string systemId,
        OscalDocumentType documentType,
        bool schemaValid,
        int schematronAdvisoryViolations = 0,
        string generatedBy = "system",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Return the most recent document version record for a system+type, or null if none exists.
    /// </summary>
    Task<OscalDocumentVersionRecord?> GetLatestAsync(
        string systemId,
        OscalDocumentType documentType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Document type discriminator for OSCAL version tracking.
/// </summary>
public enum OscalDocumentType
{
    Ssp,
    Sap,
    Sar,
    Poam
}

/// <summary>
/// Immutable record of a single OSCAL document version issuance.
/// </summary>
public record OscalDocumentVersionRecord
{
    public string Id { get; init; } = string.Empty;
    public string RegisteredSystemId { get; init; } = string.Empty;
    public OscalDocumentType DocumentType { get; init; }
    public string DocumentUuid { get; init; } = string.Empty;
    public string? PreviousUuid { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public string GeneratedBy { get; init; } = string.Empty;
    public string OscalVersion { get; init; } = "1.1.2";
    public bool SchemaValid { get; init; }
    public int SchematronAdvisoryViolations { get; init; }
}

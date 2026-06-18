namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Bidirectional transform bridge between SPIN Agent OSCAL structures
/// and eMASS API v3.22 JSON format.
/// Feature 076 — T014.
/// </summary>
public interface IEmassBridgeService
{
    /// <summary>
    /// Transform SPIN Agent control implementations for <paramref name="systemId"/>
    /// into eMASS API-compatible control payloads.
    /// When <paramref name="dryRun"/> is true, no external calls are made.
    /// </summary>
    Task<EmassBridgeResult> OscalToEmassAsync(
        string systemId,
        bool dryRun = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Import eMASS control data back into SPIN Agent control implementations.
    /// Returns the number of controls upserted.
    /// </summary>
    Task<int> EmassToOscalAsync(
        string systemId,
        List<EmassControlDto> emassControls,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of an OSCAL-to-eMASS transform operation.</summary>
public record EmassBridgeResult
{
    public string SystemId { get; init; } = string.Empty;
    public bool IsDryRun { get; init; }
    public List<EmassControlPayload> Controls { get; init; } = new();
    public int TruncatedNarratives { get; init; }
}

/// <summary>Single eMASS API control payload (PUT /api/systems/{id}/controls body item).</summary>
public record EmassControlPayload
{
    public string Acronym { get; init; } = string.Empty;           // e.g. "AC-1"
    public string ImplementationStatus { get; init; } = string.Empty;
    public string ControlDesignation { get; init; } = string.Empty;
    public string ImplementationNarrative { get; init; } = string.Empty;
    public bool IsTruncated { get; init; }
}

/// <summary>Incoming eMASS control data for reverse import.</summary>
public record EmassControlDto
{
    public string Acronym { get; init; } = string.Empty;
    public string? ImplementationStatus { get; init; }
    public string? ControlDesignation { get; init; }
    public string? ImplementationNarrative { get; init; }
}
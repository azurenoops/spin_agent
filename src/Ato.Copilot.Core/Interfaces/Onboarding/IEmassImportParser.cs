namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Per-system parse result for an eMASS bulk import. Each entry corresponds to one
/// detected system in the upload (whether well-formed or malformed). Malformed
/// systems are flagged via <see cref="MalformedReason"/> so the operator can decide
/// whether to skip them at commit time (FR-031).
/// </summary>
public sealed record EmassParsedSystem(
    string SystemIdentifier,
    string SystemName,
    int ControlCount,
    int PoamCount,
    string? MalformedReason);

/// <summary>
/// Top-level parse result for a single uploaded eMASS file/package.
/// </summary>
public sealed record EmassParseResult(
    IReadOnlyList<EmassParsedSystem> Systems,
    string SourceFormat);

/// <summary>
/// Parses an uploaded eMASS file (XLSX or PackageZip) into a per-system summary.
/// Implementations MUST flag malformed systems via <see cref="EmassParsedSystem.MalformedReason"/>
/// rather than throwing — a single bad row should not fail the batch (FR-031).
/// </summary>
public interface IEmassImportParser
{
    /// <summary>
    /// Parse an eMASS upload from the storage layer.
    /// </summary>
    /// <param name="content">Stream positioned at the start of the file.</param>
    /// <param name="originalFileName">Original client filename — used to detect the format
    /// when the stream itself is ambiguous.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<EmassParseResult> ParseAsync(
        Stream content,
        string originalFileName,
        CancellationToken ct = default);
}

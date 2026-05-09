namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// CSP ATO-document parser dispatcher (Feature 048 FR-100 / FR-101). Routes a
/// single uploaded artifact (PDF SSP, OSCAL JSON, DOCX, XLSX, or eMASS ZIP)
/// to the existing reuse-first parser stack and returns a
/// <see cref="ParsedAtoDocument"/> of candidate components for downstream
/// persistence by <see cref="ICspComponentExtractionService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per the Reuse-First Audit
/// (<c>specs/048-tenant-isolation/research-reuse-audit.md</c>), the dispatcher
/// reuses existing parsers — <c>ISspPdfExtractionService</c> for PDF
/// (Feature 047), <c>DocumentFormat.OpenXml</c> for DOCX, <c>ClosedXML</c> for
/// XLSX, and a net-new minimal <c>OscalSspJsonParser</c> for OSCAL JSON
/// (Feature 022 is OSCAL export-only). The dispatcher itself owns zero
/// parsing logic — it only routes <c>contentType</c> to the correct existing
/// parser.
/// </para>
/// <para>
/// The FR-110 startup health check (<c>CspInheritanceReuseAuditHealthCheck</c>,
/// landed in T218) enforces exactly one DI registration of this interface.
/// </para>
/// </remarks>
public interface ICspAtoDocumentParser
{
    /// <summary>
    /// Parse a single CSP ATO artifact stream and project candidate components.
    /// </summary>
    /// <param name="stream">Read-only stream over the uploaded artifact bytes. The implementation MUST NOT close the stream.</param>
    /// <param name="contentType">MIME type or extension hint (e.g. <c>application/pdf</c>, <c>application/json</c>); used for parser dispatch.</param>
    /// <param name="fileName">Original filename for provenance (flows into <c>SourceFileName</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Parsed candidate components; never null. Empty <see cref="ParsedAtoDocument.Components"/> when no candidates are recoverable.</returns>
    /// <exception cref="System.NotSupportedException">Content type is not handled by any registered parser.</exception>
    /// <exception cref="System.IO.InvalidDataException">Artifact is malformed or corrupted.</exception>
    Task<ParsedAtoDocument> ParseAsync(
        Stream stream,
        string contentType,
        string fileName,
        CancellationToken ct = default);
}

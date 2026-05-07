namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Confidence band for an extracted SSP PDF field (Feature 047 / FR-041).
/// Surfaced in the dashboard so admins know which fields warrant review.
/// </summary>
public enum SspPdfFieldConfidence
{
    /// <summary>Plain-text match with high precision (e.g. "System Identifier:" tag).</summary>
    High,
    /// <summary>Heuristic / regex match — admin should review.</summary>
    Medium,
    /// <summary>Best guess — admin almost always overrides.</summary>
    Low,
}

/// <summary>
/// One extracted field from a digital SSP PDF.
/// </summary>
public record SspPdfField(string Name, string? Value, SspPdfFieldConfidence Confidence, int? PageNumber);

/// <summary>
/// Result of extracting structured data from a single SSP PDF (FR-040..FR-046).
/// Each field is reported with a confidence band; rejection categories surface
/// via <see cref="RejectReason"/> and emit the appropriate
/// <see cref="Ato.Copilot.Core.Onboarding.WizardErrorCodes"/> value.
/// </summary>
public record SspPdfExtractionResult(
    bool IsAccepted,
    Ato.Copilot.Core.Models.Onboarding.SspPdfRejectReason? RejectReason,
    string? RejectMessage,
    IReadOnlyList<SspPdfField> Fields,
    int PageCount);

/// <summary>
/// Service that extracts structured fields from a digital SSP PDF using PdfPig
/// (research §R3). Image-only / encrypted / password-protected / non-NIST PDFs
/// are rejected with the appropriate <see cref="Ato.Copilot.Core.Models.Onboarding.SspPdfRejectReason"/>.
/// </summary>
public interface ISspPdfExtractionService
{
    /// <summary>Extracts structured fields from the supplied PDF stream.</summary>
    Task<SspPdfExtractionResult> ExtractAsync(Stream pdfStream, string originalFileName, CancellationToken ct = default);
}

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.SspPdf;

/// <summary>
/// Extracts structured fields from a digital SSP PDF using PdfPig (research §R3).
/// Rejects non-digital, encrypted, password-protected, image-only, and
/// non-NIST-framework PDFs with the matching <see cref="SspPdfRejectReason"/>.
/// </summary>
public sealed class SspPdfExtractionService : ISspPdfExtractionService
{
    private static readonly Regex SystemIdRegex = new(
        @"(?:System\s+Identifier|System\s+ID|UUID)\s*[:\-]?\s*([^\s]{3,64})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SystemNameRegex = new(
        @"(?:System\s+Name|Name\s+of\s+System)\s*[:\-]?\s*(.{3,200})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FedrampImpactRegex = new(
        @"(?:Impact\s+Level|Categorization)\s*[:\-]?\s*(High|Moderate|Low)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BoundaryRegex = new(
        @"(?:Authorization\s+Boundary|System\s+Boundary)\s*[:\-]?\s*(.{20,500})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] NistMarkers =
    {
        "NIST SP 800-53",
        "NIST 800-53",
        "NIST Risk Management Framework",
        "RMF",
        "FedRAMP",
        "FISMA",
    };

    private readonly ILogger<SspPdfExtractionService> _logger;

    public SspPdfExtractionService(ILogger<SspPdfExtractionService> logger)
    {
        _logger = logger;
    }

    public async Task<SspPdfExtractionResult> ExtractAsync(
        Stream pdfStream, string originalFileName, CancellationToken ct = default)
    {
        await using var buffered = new MemoryStream();
        await pdfStream.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        try
        {
            using var doc = PdfDocument.Open(buffered);
            if (doc.IsEncrypted)
            {
                return Reject(SspPdfRejectReason.PasswordProtected,
                    "PDF is password-protected. Re-export an unrestricted copy and retry.");
            }

            var allText = string.Join("\n", doc.GetPages().Select(ExtractPageText));
            if (string.IsNullOrWhiteSpace(allText) || allText.Length < 200)
            {
                return Reject(SspPdfRejectReason.ImageOnly,
                    "PDF has no text layer (likely a scanned document). Convert to a text-bearing PDF and retry.");
            }

            if (!NistMarkers.Any(m => allText.Contains(m, StringComparison.OrdinalIgnoreCase)))
            {
                return Reject(SspPdfRejectReason.UnknownFramework,
                    "PDF does not reference NIST SP 800-53 or FedRAMP. Only NIST-framework SSPs are supported.");
            }

            var fields = new List<SspPdfField>
            {
                ExtractField("system_identifier", allText, SystemIdRegex, SspPdfFieldConfidence.High),
                ExtractField("system_name", allText, SystemNameRegex, SspPdfFieldConfidence.Medium),
                ExtractField("impact_level", allText, FedrampImpactRegex, SspPdfFieldConfidence.High),
                ExtractField("authorization_boundary", allText, BoundaryRegex, SspPdfFieldConfidence.Low),
            };

            return new SspPdfExtractionResult(
                IsAccepted: true,
                RejectReason: null,
                RejectMessage: null,
                Fields: fields,
                PageCount: doc.NumberOfPages);
        }
        catch (UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException)
        {
            return Reject(SspPdfRejectReason.PasswordProtected,
                "PDF is password-protected. Re-export an unrestricted copy and retry.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read SSP PDF {FileName}", originalFileName);
            return Reject(SspPdfRejectReason.Unreadable,
                "PDF could not be parsed. Re-export the document and retry.");
        }
    }

    private static SspPdfExtractionResult Reject(SspPdfRejectReason reason, string message) =>
        new(IsAccepted: false, RejectReason: reason, RejectMessage: message,
            Fields: Array.Empty<SspPdfField>(), PageCount: 0);

    private static SspPdfField ExtractField(string name, string text, Regex regex, SspPdfFieldConfidence band)
    {
        var m = regex.Match(text);
        var value = m.Success ? m.Groups[1].Value.Trim() : null;
        return new SspPdfField(name, value, value is null ? SspPdfFieldConfidence.Low : band, null);
    }

    /// <summary>
    /// PdfPig's <see cref="Page.Text"/> concatenates glyphs without preserving
    /// inter-word whitespace, which breaks regex extraction. Reconstruct by
    /// joining the page's words with spaces.
    /// </summary>
    private static string ExtractPageText(Page page)
    {
        var words = page.GetWords().Select(w => w.Text);
        return string.Join(' ', words);
    }
}

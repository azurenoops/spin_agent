using System.Globalization;
using System.Text;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Exceptions;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private const int PdfTextLayoutVersion = 1;

    private string PdfPageText(AnalysisSession session, Entry entry, Page page)
    {
        session.Cancellation.ThrowIfCancellationRequested();
        if (session.PdfPages >= _limits.MaxPdfPages)
            throw new BudgetExceededException("PDF_PAGE_LIMIT", "Package PDF page budget exhausted. Retained evidence is unchanged; split the PDFs for further analysis.");
        session.PdfPages++;
        entry.PdfPagesCharged++;
        var remaining = _limits.MaxExtractedCharacters - session.Characters;
        var words = new List<Word>();
        long characters = 0;
        foreach (var word in page.GetWords(NearestNeighbourWordExtractor.Instance))
        {
            session.Cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(word.Text)) continue;
            if (words.Count >= _limits.MaxPdfLayoutWordsPerPage)
                throw new BudgetExceededException("PDF_LAYOUT_WORD_LIMIT", "PDF page exceeds the bounded layout word limit. Supply a simpler text-bearing export.");
            characters += word.Text.Length + (words.Count > 0 ? 1 : 0);
            if (characters > remaining)
                throw new BudgetExceededException("EXTRACTED_CHARACTER_LIMIT", "PDF text exceeds the package character budget.");
            words.Add(word);
        }
        if (words.Count == 0) return string.Empty;
        var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        session.Cancellation.ThrowIfCancellationRequested();
        if (blocks.Count > _limits.MaxPdfLayoutBlocksPerPage)
            throw new BudgetExceededException("PDF_LAYOUT_BLOCK_LIMIT", "PDF page exceeds the bounded reading-order block limit. Supply a simpler text-bearing export.");
        var grouped = blocks.SelectMany(block => block.TextLines).SelectMany(line => line.Words).ToArray();
        if (grouped.Length != words.Count || !words.ToHashSet().SetEquals(grouped))
            throw new UnsupportedContentException("PDF_LAYOUT_INCOMPLETE", "PDF layout did not retain every extracted word. No partial page was accepted.");
        var ordered = new UnsupervisedReadingOrderDetector(5,
            UnsupervisedReadingOrderDetector.SpatialReasoningRules.RowWise, true).Get(blocks).ToArray();
        if (ordered.Length != blocks.Count || !blocks.ToHashSet().SetEquals(ordered))
            throw new UnsupportedContentException("PDF_LAYOUT_INCOMPLETE", "PDF reading order did not retain every text block. No partial page was accepted.");
        var text = new StringBuilder();
        foreach (var block in ordered)
        {
            session.Cancellation.ThrowIfCancellationRequested();
            if (text.Length > 0) text.Append("\n\n");
            var first = true;
            foreach (var line in block.TextLines)
            {
                if (!first) text.Append(' ');
                text.Append(line.Text);
                first = false;
                if (text.Length > remaining)
                    throw new BudgetExceededException("EXTRACTED_CHARACTER_LIMIT", "PDF layout text exceeds the package character budget.");
            }
        }
        return text.ToString();
    }

    private static IEnumerable<CspPackageSourceSegment> AnalysisSources(AnalysisSession session, Entry entry) =>
        session.Segments.Where(segment => segment.EntryKey == entry.Key && !entry.PdfAnalysisViews.ContainsKey(segment.Key));

    private static bool SourceAnalyzed(Entry entry, CspPackageSourceSegment source) =>
        entry.SemanticallyAnalyzedSegmentKeys.Contains(source.Key)
        && (entry.AnalysisProfileVersion < 2 || Enum.GetValues<CspPackageClaimFamily>().All(family =>
            entry.FamilyAnalyzedSegmentKeys.TryGetValue(family, out var keys) && keys.Contains(source.Key)));

    private bool UpgradePdfAnalysisViews(AnalysisSession session, Entry entry)
    {
        if (entry.MediaType != "application/pdf" || entry.Status != CspPackageEntryStatus.Processed
            || entry.AnalysisComplete || entry.PdfTextLayoutVersion >= PdfTextLayoutVersion) return true;
        try
        {
            if (entry.Content is null)
                throw new InvalidDataException("PDF layout recovery requires retained source bytes.");
            using var document = PdfDocument.Open(entry.Content);
            foreach (var source in AnalysisSources(session, entry).Where(source => !SourceAnalyzed(entry, source)).ToArray())
            {
                if (!source.Locator.StartsWith("page:", StringComparison.Ordinal)
                    || source.Locator.Contains("/layout:", StringComparison.Ordinal)) continue;
                if (!int.TryParse(source.Locator.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var page)
                    || page < 1 || page > document.NumberOfPages)
                    throw new InvalidDataException("Retained PDF page locator is invalid.");
                var text = PdfPageText(session, entry, document.GetPage(page));
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidDataException("Retained PDF page has no readable layout text.");
                var view = session.Segment(entry, $"{source.Locator}/layout:{PdfTextLayoutVersion}", text);
                entry.PdfAnalysisViews.Add(source.Key, view.Key);
            }
            entry.PdfTextLayoutVersion = PdfTextLayoutVersion;
            return true;
        }
        catch (BudgetExceededException exception)
        {
            SemanticFailure(entry, exception.Code, exception.Message);
        }
        catch (UnsupportedContentException exception)
        {
            SemanticFailure(entry, exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is InvalidDataException or PdfDocumentFormatException
            or PdfDocumentEncryptedException or IOException)
        {
            SemanticFailure(entry, "PDF_LAYOUT_UNAVAILABLE",
                "The retained PDF could not produce a complete layout view. Original evidence and prior citations remain unchanged.");
        }
        return false;
    }
}

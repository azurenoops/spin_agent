using Ato.Copilot.Core.Interfaces.PackageImports;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Tokens;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private void ExtractPdfPageText(AnalysisSession session, Entry entry, PdfDocument document)
    {
        var missingText = false;
        for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            var text = PdfPageText(session, entry, document.GetPage(pageNumber));
            session.Segment(entry, $"page:{pageNumber}", text);
            missingText |= text.Length == 0;
        }
        entry.PdfTextLayoutVersion = PdfTextLayoutVersion;
        if (missingText)
            entry.Set(CspPackageEntryStatus.Unreadable, "OCR_UNAVAILABLE",
                "One or more PDF pages have no readable text layer. OCR is unavailable; supply a text-bearing export or explicitly exclude those pages.");
        else entry.Process(complete: false);
    }

    private async Task EnsurePdfAttachmentsAsync(AnalysisSession session, Entry pdfEntry)
    {
        if (pdfEntry.PdfAttachmentsEnumerated) return;
        session.Cancellation.ThrowIfCancellationRequested();
        try
        {
            if (pdfEntry.Content is null)
                throw new ArgumentException("Hydrate the retained PDF before enumerating its attachments.");
            using var document = PdfDocument.Open(pdfEntry.Content);
            if (document.IsEncrypted) throw new PdfDocumentEncryptedException("PDF attachments are encrypted.");
            await ScanPdfAttachmentsAsync(session, pdfEntry, document).ConfigureAwait(false);
        }
        catch (PdfDocumentEncryptedException)
        {
            pdfEntry.Set(CspPackageEntryStatus.Unreadable, "ENCRYPTED_CONTENT",
                "Encrypted PDF attachments cannot be enumerated. Supply an unrestricted export.");
            session.EnumerationComplete = false;
        }
        catch (PdfDocumentFormatException)
        {
            pdfEntry.Set(CspPackageEntryStatus.Unreadable, "MALFORMED_CONTENT",
                "PDF attachment references could not be read. Re-export the PDF.");
            session.EnumerationComplete = false;
        }
    }

    private async Task RecoverPdfAttachmentContentAsync(AnalysisSession session, Entry parent, Entry child)
    {
        session.Cancellation.ThrowIfCancellationRequested();
        try
        {
            using var document = PdfDocument.Open(parent.Content!);
            if (document.IsEncrypted) throw new PdfDocumentEncryptedException("PDF attachments are encrypted.");
            var scan = new PdfAttachmentScan(this, session, parent, document, child.Key);
            await WalkPdfAttachmentsAsync(scan).ConfigureAwait(false);
            if (!scan.TargetFound && child.Status == CspPackageEntryStatus.Pending)
                child.Set(CspPackageEntryStatus.Unreadable, "PDF_ATTACHMENT_NOT_FOUND",
                    "The retained PDF attachment reference could not be recovered within the traversal limits. Re-export the attachment.");
        }
        catch (PdfDocumentEncryptedException)
        {
            child.Set(CspPackageEntryStatus.Unreadable, "PDF_ATTACHMENT_ENCRYPTED", "Supply an unrestricted attachment export.");
        }
        catch (PdfDocumentFormatException)
        {
            child.Set(CspPackageEntryStatus.Unreadable, "PDF_ATTACHMENT_MALFORMED", "The containing PDF is malformed. Re-export it.");
        }
    }

    private async Task ScanPdfAttachmentsAsync(AnalysisSession session, Entry parent, PdfDocument document)
    {
        var scan = new PdfAttachmentScan(this, session, parent, document, null);
        await WalkPdfAttachmentsAsync(scan).ConfigureAwait(false);
        parent.PdfAttachmentsEnumerated = scan.Complete;
        if (!scan.Complete) session.EnumerationComplete = false;
    }

    private async Task WalkPdfAttachmentsAsync(PdfAttachmentScan scan)
    {
        try
        {
            if (scan.Parent.Depth > _limits.MaxArchiveDepth)
                throw new BudgetExceededException("ARCHIVE_DEPTH_LIMIT", "Nested PDF attachment depth exceeds the budget.");
            var catalog = scan.Document.Structure.Catalog.CatalogDictionary;
            await PdfBranchAsync(scan, PdfValue(catalog, "Names"), "catalog/Names", 0, async (names, depth) =>
                await WalkPdfNameTreeAsync(scan, PdfValue(names, "EmbeddedFiles"),
                    "catalog/Names/EmbeddedFiles", depth + 1).ConfigureAwait(false)).ConfigureAwait(false);
            await WalkPdfAssociatedFilesAsync(scan, PdfValue(catalog, "AF"), "catalog/AF", 0).ConfigureAwait(false);
            await WalkPdfPagesAsync(scan, PdfValue(catalog, "Pages"), "pages", 0).ConfigureAwait(false);
        }
        catch (BudgetExceededException exception)
        {
            scan.Complete = false;
            if (scan.TargetKey is null) scan.Parent.Fail(exception.Code, exception.Message);
            else scan.Session.Entries.Single(entry => entry.Key == scan.TargetKey).Fail(exception.Code, exception.Message);
        }
    }

    private static IToken? PdfValue(DictionaryToken dictionary, string key) =>
        dictionary.TryGet(NameToken.Create(key), out IToken value) ? value : null;

    private static string? PdfText(IToken? token) => token switch
    {
        StringToken text => text.Data,
        HexToken text => text.Data,
        _ => null
    };

    private sealed class PdfAttachmentException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    private sealed class PdfAttachmentScan(CspPackageAnalyzer analyzer, AnalysisSession session,
        Entry parent, PdfDocument document, string? targetKey)
    {
        public AnalysisSession Session { get; } = session;
        public Entry Parent { get; } = parent;
        public PdfDocument Document { get; } = document;
        public string? TargetKey { get; } = targetKey;
        public bool TargetFound { get; set; }
        public bool Complete { get; set; } = true;
        public int PageCount { get; set; }
        public HashSet<DictionaryToken> Active { get; } = new(ReferenceEqualityComparer.Instance);
        public HashSet<IndirectReference> ActiveReferences { get; } = [];
        private int _nodes;

        public IToken? Resolve(IToken? token, int depth) => Resolve(token, ref depth);

        public IToken? Resolve(IToken? token, ref int depth)
        {
            var references = new HashSet<IndirectReference>();
            while (token is not null)
            {
                Session.Cancellation.ThrowIfCancellationRequested();
                if (++_nodes > analyzer._limits.MaxPdfAttachmentNodes)
                    throw new BudgetExceededException("PDF_ATTACHMENT_NODE_LIMIT",
                        "PDF attachment traversal node budget exhausted. Some references remain unenumerated; split the PDF.");
                if (depth++ > analyzer._limits.MaxPdfAttachmentDepth)
                    throw new PdfAttachmentException("PDF_ATTACHMENT_DEPTH_LIMIT",
                        "PDF attachment reference depth exceeds the traversal budget. Re-export a shallower package.");
                if (token is not IndirectReferenceToken reference) return token;
                if (!references.Add(reference.Data))
                    throw new PdfAttachmentException("PDF_ATTACHMENT_CYCLE", "PDF attachment references form a cycle. Re-export the PDF.");
                try
                {
                    token = Document.Structure.GetObject(reference.Data)?.Data
                        ?? throw PdfMalformed("PDF attachment refers to an absent object.");
                }
                catch (InvalidOperationException)
                {
                    throw PdfMalformed("PDF attachment object could not be resolved.");
                }
            }
            return null;
        }

        public IToken? Read(DictionaryToken dictionary, string name, int depth) =>
            Resolve(PdfValue(dictionary, name), depth);
    }
}

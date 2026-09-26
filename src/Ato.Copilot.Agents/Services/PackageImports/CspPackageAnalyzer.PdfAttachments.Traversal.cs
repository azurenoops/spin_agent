using Ato.Copilot.Core.Interfaces.PackageImports;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private async Task PdfBranchAsync(PdfAttachmentScan scan, IToken? token, string location, int depth,
        Func<DictionaryToken, int, Task> visit)
    {
        if (token is null) return;
        DictionaryToken? active = null;
        IndirectReference? activeReference = null;
        try
        {
            if (token is IndirectReferenceToken reference)
            {
                if (!scan.ActiveReferences.Add(reference.Data))
                    throw new PdfAttachmentException("PDF_ATTACHMENT_CYCLE", "PDF attachment reference tree contains a cycle. Re-export it.");
                activeReference = reference.Data;
            }
            var dictionary = scan.Resolve(token, ref depth) as DictionaryToken
                ?? throw PdfMalformed("Expected a dictionary in the PDF attachment reference tree.");
            if (!scan.Active.Add(dictionary))
                throw new PdfAttachmentException("PDF_ATTACHMENT_CYCLE", "PDF attachment reference tree contains a cycle. Re-export it.");
            active = dictionary;
            await visit(dictionary, depth + 1).ConfigureAwait(false);
        }
        catch (PdfAttachmentException exception)
        {
            RecordPdfTraversalFailure(scan, location, exception.Code, exception.Message);
        }
        catch (PdfDocumentFormatException)
        {
            RecordPdfTraversalFailure(scan, location, "PDF_ATTACHMENT_MALFORMED", "PDF reference tree is malformed. Re-export it.");
        }
        finally
        {
            if (active is not null) scan.Active.Remove(active);
            if (activeReference.HasValue) scan.ActiveReferences.Remove(activeReference.Value);
        }
    }

    private async Task PdfArrayAsync(PdfAttachmentScan scan, IToken? token, string location, int depth,
        Func<ArrayToken, int, Task> visit)
    {
        if (token is null) return;
        try
        {
            var array = scan.Resolve(token, ref depth) as ArrayToken
                ?? throw PdfMalformed("Expected an array of PDF attachment references.");
            await visit(array, depth + 1).ConfigureAwait(false);
        }
        catch (PdfAttachmentException exception)
        {
            RecordPdfTraversalFailure(scan, location, exception.Code, exception.Message);
        }
        catch (PdfDocumentFormatException)
        {
            RecordPdfTraversalFailure(scan, location, "PDF_ATTACHMENT_MALFORMED", "PDF reference array is malformed. Re-export it.");
        }
    }

    private Task WalkPdfNameTreeAsync(PdfAttachmentScan scan, IToken? token, string location, int depth) =>
        PdfBranchAsync(scan, token, location, depth, async (dictionary, nextDepth) =>
        {
            await PdfArrayAsync(scan, PdfValue(dictionary, "Names"), location + "/Names", nextDepth, async (names, arrayDepth) =>
            {
                for (var index = 0; index < names.Length; index += 2)
                {
                    scan.Session.Cancellation.ThrowIfCancellationRequested();
                    await ReadPdfNamePairAsync(scan, names, index, location + "/Names/" + index,
                        arrayDepth).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
            await PdfArrayAsync(scan, PdfValue(dictionary, "Kids"), location + "/Kids", nextDepth, async (kids, arrayDepth) =>
            {
                for (var index = 0; index < kids.Length; index++)
                    await WalkPdfNameTreeAsync(scan, kids[index], location + "/Kids/" + index,
                        arrayDepth).ConfigureAwait(false);
            }).ConfigureAwait(false);
            if (PdfValue(dictionary, "Names") is null && PdfValue(dictionary, "Kids") is null
                && dictionary.Data.Count != 0)
                throw PdfMalformed("Embedded-file name tree has no Names or Kids collection.");
        });

    private async Task ReadPdfNamePairAsync(PdfAttachmentScan scan, ArrayToken names, int index, string location, int depth)
    {
        string? name = null;
        try
        {
            name = PdfText(scan.Resolve(names[index], depth)) ?? throw PdfMalformed("Embedded-file name is not a string.");
        }
        catch (PdfAttachmentException exception)
        {
            RecordPdfTraversalFailure(scan, location + "/label", exception.Code, exception.Message);
        }
        catch (PdfDocumentFormatException)
        {
            RecordPdfTraversalFailure(scan, location + "/label", "PDF_ATTACHMENT_MALFORMED",
                "Embedded-file name cannot be read. Re-export the PDF.");
        }
        await ReadPdfFileSpecificationAsync(scan, index + 1 < names.Length ? names[index + 1] : null,
            location, name, depth).ConfigureAwait(false);
    }

    private Task WalkPdfAssociatedFilesAsync(PdfAttachmentScan scan, IToken? token, string location, int depth) =>
        PdfArrayAsync(scan, token, location, depth, async (files, arrayDepth) =>
        {
            for (var index = 0; index < files.Length; index++)
                await ReadPdfFileSpecificationAsync(scan, files[index], location + "/" + index,
                    null, arrayDepth).ConfigureAwait(false);
        });

    private Task WalkPdfPagesAsync(PdfAttachmentScan scan, IToken? token, string location, int depth) =>
        PdfBranchAsync(scan, token, location, depth, async (dictionary, nextDepth) =>
        {
            var type = (scan.Read(dictionary, "Type", nextDepth) as NameToken)?.Data;
            if (type == "Page" && ++scan.PageCount > _limits.MaxPdfPages)
                throw new BudgetExceededException("PDF_PAGE_LIMIT", "PDF page attachment discovery exceeds the page budget. Split the PDF.");
            await WalkPdfAssociatedFilesAsync(scan, PdfValue(dictionary, "AF"),
                location + "/AF", nextDepth).ConfigureAwait(false);
            await WalkPdfAnnotationsAsync(scan, PdfValue(dictionary, "Annots"),
                location + "/Annots", nextDepth).ConfigureAwait(false);
            if (type != "Page")
                await PdfArrayAsync(scan, PdfValue(dictionary, "Kids"), location + "/Kids", nextDepth, async (kids, arrayDepth) =>
                {
                    for (var index = 0; index < kids.Length; index++)
                        await WalkPdfPagesAsync(scan, kids[index], location + "/Kids/" + index,
                            arrayDepth).ConfigureAwait(false);
                }).ConfigureAwait(false);
        });

    private Task WalkPdfAnnotationsAsync(PdfAttachmentScan scan, IToken? token, string location, int depth) =>
        PdfArrayAsync(scan, token, location, depth, async (annotations, arrayDepth) =>
        {
            for (var index = 0; index < annotations.Length; index++)
            {
                var annotationLocation = location + "/" + index;
                await PdfBranchAsync(scan, annotations[index], annotationLocation, arrayDepth, async (annotation, nextDepth) =>
                {
                    if ((scan.Read(annotation, "Subtype", nextDepth) as NameToken)?.Data == "FileAttachment")
                        await ReadPdfFileSpecificationAsync(scan, PdfValue(annotation, "FS"),
                            annotationLocation + "/FS", null, nextDepth).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
        });

    private async Task ReadPdfFileSpecificationAsync(PdfAttachmentScan scan, IToken? token,
        string location, string? name, int depth)
    {
        try
        {
            var resolved = scan.Resolve(token, ref depth);
            if (resolved is StringToken or HexToken)
            {
                RecordPdfAttachmentFailure(scan, location, PdfText(resolved), "PDF_ATTACHMENT_EXTERNAL_ONLY",
                    "File specification has no embedded bytes. Upload the file separately; external references are never fetched.");
                return;
            }
            var specification = resolved as DictionaryToken ?? throw PdfMalformed("PDF file specification is missing or malformed.");
            name = ReadPdfFileName(scan, specification, "F", name, location, depth + 1);
            name = ReadPdfFileName(scan, specification, "UF", name, location, depth + 1);
            var embeddedDepth = depth + 1;
            var embedded = scan.Resolve(PdfValue(specification, "EF"), ref embeddedDepth);
            if (embedded is null)
            {
                RecordPdfAttachmentFailure(scan, location, name, "PDF_ATTACHMENT_EXTERNAL_ONLY",
                    "No embedded file stream is present. Upload the source separately; external files are never fetched.");
                return;
            }
            var streams = embedded as DictionaryToken ?? throw PdfMalformed("Embedded file references are not a dictionary.");
            if (streams.Data.Count == 0) throw PdfMalformed("Embedded file reference dictionary is empty.");
            foreach (var pair in streams.Data)
            {
                scan.Session.Cancellation.ThrowIfCancellationRequested();
                var streamName = ReadPdfFileName(scan, specification, pair.Key, name,
                    location + "/EF/" + Uri.EscapeDataString(pair.Key), embeddedDepth + 1);
                await ReadPdfAttachmentStreamAsync(scan, pair.Value,
                    location + "/EF/" + Uri.EscapeDataString(pair.Key), streamName, embeddedDepth + 1).ConfigureAwait(false);
            }
        }
        catch (PdfAttachmentException exception)
        {
            if (exception.Code is "PDF_ATTACHMENT_DEPTH_LIMIT" or "PDF_ATTACHMENT_CYCLE") scan.Complete = false;
            RecordPdfAttachmentFailure(scan, location, name, exception.Code, exception.Message);
        }
        catch (PdfDocumentFormatException)
        {
            RecordPdfAttachmentFailure(scan, location, name, "PDF_ATTACHMENT_MALFORMED",
                "PDF file specification cannot be read. Re-export the attachment.");
        }
    }

    private string? ReadPdfFileName(PdfAttachmentScan scan, DictionaryToken specification, string key,
        string? fallback, string location, int depth)
    {
        try
        {
            var token = scan.Read(specification, key, depth);
            return token is null ? fallback : PdfText(token) ?? throw PdfMalformed("PDF filename is not a string.");
        }
        catch (PdfAttachmentException exception)
        {
            RecordPdfTraversalFailure(scan, location + "/filename-" + Uri.EscapeDataString(key),
                exception.Code, exception.Message);
        }
        catch (PdfDocumentFormatException)
        {
            RecordPdfTraversalFailure(scan, location + "/filename-" + Uri.EscapeDataString(key),
                "PDF_ATTACHMENT_MALFORMED", "PDF filename cannot be read. Re-export the attachment.");
        }
        return fallback;
    }

    private Entry? PdfAttachmentEntry(PdfAttachmentScan scan, string location, string? name)
    {
        name ??= "unnamed-attachment";
        var key = StableKey(scan.Parent.Key, "pdf-attachment", location, name);
        if (scan.TargetKey is not null && scan.TargetKey != key) return null;
        scan.TargetFound |= scan.TargetKey == key;
        var existing = scan.Session.Entries.FirstOrDefault(entry => entry.Key == key);
        if (existing is not null) return existing;
        if (scan.TargetKey is not null)
            throw new ArgumentException("PDF attachment recovery target is not present in the checkpoint.");
        if (scan.Session.Entries.Count >= _limits.MaxEntries)
            throw new BudgetExceededException("ENTRY_COUNT_LIMIT",
                "PDF attachment entry budget exhausted. Some references remain unenumerated; split the PDF.");
        var child = scan.Session.AddEntry(key, scan.Parent.Key, key,
            $"{scan.Parent.ArchivePath}!/attachments/{location}/{name}", MediaType(name, null), 0, null);
        child.Depth = scan.Parent.Depth + 1;
        return child;
    }

    private void RecordPdfTraversalFailure(PdfAttachmentScan scan, string location, string code, string message)
    {
        scan.Complete = false;
        RecordPdfAttachmentFailure(scan, location, null, code, message);
    }

    private void RecordPdfAttachmentFailure(PdfAttachmentScan scan, string location, string? name, string code, string message)
    {
        var entry = PdfAttachmentEntry(scan, location, name);
        if (entry is not null && entry.Status == CspPackageEntryStatus.Pending)
            SetPdfAttachmentFailure(entry, code, message);
    }

    private static void SetPdfAttachmentFailure(Entry entry, string code, string message) =>
        entry.Set(code switch
        {
            "UNSAFE_ARCHIVE_PATH" => CspPackageEntryStatus.Excluded,
            "PDF_ATTACHMENT_FILTER_UNSUPPORTED" or "PDF_ATTACHMENT_EXTERNAL_ONLY" => CspPackageEntryStatus.Unsupported,
            _ => CspPackageEntryStatus.Unreadable
        }, code, message);

    private static PdfAttachmentException PdfMalformed(string message) =>
        new("PDF_ATTACHMENT_MALFORMED", message + " Re-export the attachment.");
}

using System.Globalization;
using System.IO.Compression;
using System.Text;
using Ato.Copilot.Core.Interfaces.PackageImports;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed partial class CspPackageAnalyzerTests
{
    [Fact]
    public async Task PdfAttachment_AllReferenceLocationsRetainDistinctIdentitiesAndPageText()
    {
        // Arrange
        var payload = Bytes("""{"components":[{"name":"Synthetic embedded component"}]}""");
        var input = Input("attachments.pdf", AttachmentPdf(
            "/Names << /EmbeddedFiles << /Names [(named.json) 6 0 R] >> >> /AF [6 0 R]",
            "/AF [6 0 R] /Annots [8 0 R]",
            PdfFileSpec("component.json"), PdfEmbeddedStream(payload),
            "<< /Type /Annot /Subtype /FileAttachment /Rect [0 0 10 10] /FS 6 0 R >>"));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);
        var replay = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var parent = result.Entries.Single(entry => entry.ParentKey is null);
        var children = result.Entries.Where(entry => entry.ParentKey == parent.Key).ToArray();
        children.Should().HaveCount(4);
        children.Select(entry => entry.Key).Should().OnlyHaveUniqueItems();
        children.Should().OnlyContain(entry => entry.Content!.SequenceEqual(payload));
        children.Should().OnlyContain(entry => entry.ArchivePath.StartsWith("attachments.pdf!/"));
        result.Entries.Select(entry => entry.Key).Should().Equal(replay.Entries.Select(entry => entry.Key));
        result.Segments.Should().Contain(segment => segment.EntryKey == parent.Key
            && segment.Locator == "page:1" && segment.Text.Contains("Synthetic PDF page"));
        result.Candidates.Should().HaveCount(4);
        result.Checkpoint!.Progress[parent.Key].PdfAttachmentsEnumerated.Should().BeTrue();
        result.Coverage.ExpandedBytes.Should().Be(input.Content.LongLength + 4 * payload.LongLength);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PdfAttachment_FlateDecodingPreservesOriginalBytes(bool declaredSize)
    {
        // Arrange
        var payload = Bytes("""{"components":[{"name":"Compressed synthetic component"}]}""");
        var input = Input("compressed.pdf", AttachmentPdf("/AF [6 0 R]", "",
            PdfFileSpec("compressed.json"), PdfEmbeddedStream(PdfDeflate(payload), "/Filter /FlateDecode",
                declaredSize ? payload.Length : null)));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var child = result.Entries.Single(entry => entry.ParentKey is not null);
        child.Content.Should().Equal(payload);
        child.ExpandedBytes.Should().Be(payload.Length);
        result.Candidates.Single().Name.Should().Be("Compressed synthetic component");
        result.Checkpoint!.Progress[child.Key].ExpandedBytesCharged.Should().Be(payload.Length);
    }

    [Theory]
    [InlineData("/Filter /LZWDecode", "PDF_ATTACHMENT_FILTER_UNSUPPORTED")]
    [InlineData("/Filter /ASCII85Decode", "PDF_ATTACHMENT_FILTER_UNSUPPORTED")]
    [InlineData("/Filter /Crypt", "PDF_ATTACHMENT_ENCRYPTED")]
    [InlineData("/Filter [/FlateDecode /ASCIIHexDecode]", "PDF_ATTACHMENT_FILTER_UNSUPPORTED")]
    [InlineData("/Filter /FlateDecode /DecodeParms << /Predictor 12 >>", "PDF_ATTACHMENT_FILTER_UNSUPPORTED")]
    [InlineData("/Filter /FlateDecode /DecodeParms << /Predictor 1.5 >>", "PDF_ATTACHMENT_FILTER_UNSUPPORTED")]
    public async Task PdfAttachment_UnsupportedDecodersRemainIndividualExceptions(string filter, string reason)
    {
        // Arrange
        var input = Input("unsupported.pdf", AttachmentPdf("/AF [6 0 R]", "",
            PdfFileSpec("source.json"), PdfEmbeddedStream(Bytes("not decoded source"), filter, 123)));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var child = result.Entries.Single(entry => entry.ParentKey is not null);
        child.ReasonCode.Should().Be(reason);
        child.Content.Should().BeNull();
        child.ExpandedBytes.Should().Be(123);
        result.Checkpoint!.Progress[child.Key].ExpandedBytesCharged.Should().Be(0);
        result.NeedsAttention.Should().BeTrue();
        result.Segments.Should().ContainSingle(segment => segment.Locator == "page:1");
    }

    [Fact]
    public async Task PdfAttachment_UnsafeExternalAndMissingReferencesDoNotHideReadableSibling()
    {
        // Arrange
        var input = Input("mixed.pdf", AttachmentPdf("/AF [6 0 R 8 0 R 99 0 R 9 0 R]", "",
            PdfFileSpec("../unsafe.json"), PdfEmbeddedStream(Bytes("{\"components\":[]}")),
            "<< /Type /Filespec /F (https://invalid.example/external.json) /FS /URL >>",
            PdfFileSpec("safe.json")));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var children = result.Entries.Where(entry => entry.ParentKey is not null).ToArray();
        children.Should().HaveCount(4);
        children.Select(entry => entry.ReasonCode).Should().Contain(
            ["UNSAFE_ARCHIVE_PATH", "PDF_ATTACHMENT_EXTERNAL_ONLY", "PDF_ATTACHMENT_MALFORMED"]);
        children.Should().ContainSingle(entry => entry.AnalysisComplete);
        result.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task PdfAttachment_IndirectNameTreeCycleIsExplicitAndSiblingStillExtracted()
    {
        // Arrange
        var input = Input("tree.pdf", AttachmentPdf("/Names 8 0 R", "",
            PdfFileSpec("safe.json"), PdfEmbeddedStream(Bytes("{\"components\":[]}")),
            "<< /EmbeddedFiles 9 0 R >>",
            "<< /Kids [10 0 R 11 0 R] >>",
            "<< /Kids [10 0 R] >>",
            "<< /Names [(safe.json) 6 0 R (safe-again.json) 6 0 R] >>"));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Entries.Should().Contain(entry => entry.ReasonCode == "PDF_ATTACHMENT_CYCLE");
        result.Entries.Count(entry => entry.ParentKey is not null && entry.AnalysisComplete).Should().Be(2);
        result.Entries.Select(entry => entry.Key).Should().OnlyHaveUniqueItems();
        result.Coverage.EnumerationComplete.Should().BeFalse();
        result.Checkpoint!.Progress[result.Entries[0].Key].PdfAttachmentsEnumerated.Should().BeFalse();
    }

    [Theory]
    [InlineData(4, 32, "PDF_ATTACHMENT_NODE_LIMIT")]
    [InlineData(4096, 2, "PDF_ATTACHMENT_DEPTH_LIMIT")]
    public async Task PdfAttachment_TraversalBudgetsNeverClaimComplete(int nodes, int depth, string reason)
    {
        // Arrange
        var input = Input("bounded.pdf", AttachmentPdf("/Names 8 0 R", "",
            PdfFileSpec("safe.json"), PdfEmbeddedStream(Bytes("{}")),
            "<< /EmbeddedFiles 9 0 R >>", "<< /Kids [10 0 R] >>",
            "<< /Names [(safe.json) 6 0 R] >>"));

        // Act
        var result = await Analyzer(new() { MaxPdfAttachmentNodes = nodes, MaxPdfAttachmentDepth = depth })
            .AnalyzeAsync([input]);

        // Assert
        result.Entries.Should().Contain(entry => entry.ReasonCode == reason);
        result.Coverage.EnumerationComplete.Should().BeFalse();
        result.Checkpoint!.Progress[result.Entries[0].Key].PdfAttachmentsEnumerated.Should().BeFalse();
    }

    [Fact]
    public async Task PdfAttachment_EntryBudgetIncludesEachReference()
    {
        // Arrange
        var input = Input("count.pdf", AttachmentPdf("/AF [6 0 R 6 0 R 6 0 R]", "",
            PdfFileSpec("safe.json"), PdfEmbeddedStream(Bytes("{}"))));

        // Act
        var result = await Analyzer(new() { MaxEntries = 3 }).AnalyzeAsync([input]);

        // Assert
        result.Entries.Should().HaveCount(3);
        result.Entries[0].ReasonCode.Should().Be("ENTRY_COUNT_LIMIT");
        result.Coverage.EnumerationComplete.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PdfAttachment_UnknownExpandedSizeIsBoundedAndCheckpointChargesRemainValid(bool entryBudget)
    {
        // Arrange
        var payload = Bytes(new string('x', 20_000));
        var input = Input("bomb.pdf", AttachmentPdf("/AF [6 0 R]", "",
            PdfFileSpec("large.txt"), PdfEmbeddedStream(PdfDeflate(payload), "/Filter /FlateDecode")));
        var limits = entryBudget ? new CspPackageAnalysisLimits { MaxEntryBytes = 4096 }
            : new CspPackageAnalysisLimits { MaxExpandedBytes = input.Content.Length + 1024 };

        // Act
        var result = await Analyzer(limits).AnalyzeAsync([input]);

        // Assert
        var child = result.Entries.Single(entry => entry.ParentKey is not null);
        child.ReasonCode.Should().Be(entryBudget ? "ENTRY_SIZE_LIMIT" : "EXPANDED_SIZE_LIMIT");
        child.Content.Should().BeNull();
        var charged = result.Checkpoint!.Progress[child.Key].ExpandedBytesCharged;
        charged.Should().BeGreaterThan(0);
        charged.Should().BeLessThanOrEqualTo(child.ExpandedBytes);
        charged.Should().BeLessThanOrEqualTo(entryBudget ? 4096 : 1024);
        result.Coverage.ExpandedBytes.Should().Be(input.Content.Length + charged);
    }

    [Fact]
    public async Task PdfAttachment_DeclaredOversizeIsRetainedWithoutDecoding()
    {
        // Arrange
        var input = Input("declared.pdf", AttachmentPdf("/AF [6 0 R]", "",
            PdfFileSpec("large.txt"), PdfEmbeddedStream(Bytes("bad zlib"), "/Filter /FlateDecode", 100_000)));

        // Act
        var result = await Analyzer(new() { MaxEntryBytes = 4096 }).AnalyzeAsync([input]);

        // Assert
        var child = result.Entries.Single(entry => entry.ParentKey is not null);
        child.ReasonCode.Should().Be("ENTRY_SIZE_LIMIT");
        child.ExpandedBytes.Should().Be(100_000);
        child.Content.Should().BeNull();
        result.Checkpoint!.Progress[child.Key].ExpandedBytesCharged.Should().Be(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task PdfAttachment_IncorrectDeclaredLengthNeverRetainsMisidentifiedOriginals(int size)
    {
        // Arrange
        var input = Input("length.pdf", AttachmentPdf("/AF [6 0 R]", "",
            PdfFileSpec("data.json"), PdfEmbeddedStream(PdfDeflate(Bytes("{\"components\":[]}")),
                "/Filter /FlateDecode", size)));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var child = result.Entries.Single(entry => entry.ParentKey is not null);
        child.ReasonCode.Should().Be("PDF_ATTACHMENT_MALFORMED");
        child.ExpandedBytes.Should().Be(size);
        child.Content.Should().BeNull();
        result.Checkpoint!.Progress[child.Key].ExpandedBytesCharged.Should().BeLessThanOrEqualTo(size);
    }

    [Theory]
    [InlineData("nested.zip")]
    [InlineData("nested.pdf")]
    public async Task PdfAttachment_NestedContainersUseGenericTraversal(string name)
    {
        // Arrange
        var payload = Bytes("""{"components":[{"name":"Deep synthetic component"}]}""");
        var nested = name.EndsWith(".zip", StringComparison.Ordinal) ? Zip(("deep.json", payload))
            : AttachmentPdf("/AF [6 0 R]", "", PdfFileSpec("deep.json"), PdfEmbeddedStream(payload));
        var input = Input("outer.pdf", AttachmentPdf("/AF [6 0 R]", "",
            PdfFileSpec(name), PdfEmbeddedStream(nested)));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);
        var limited = await Analyzer(new() { MaxArchiveDepth = 0 }).AnalyzeAsync([input]);

        // Assert
        result.Candidates.Single().Name.Should().Be("Deep synthetic component");
        result.Entries.Should().HaveCount(3);
        result.Checkpoint!.Progress.Values.Max(progress => progress.Depth).Should().Be(2);
        limited.Entries.Should().Contain(entry => entry.ReasonCode == "ARCHIVE_DEPTH_LIMIT");
        limited.Coverage.EnumerationComplete.Should().BeFalse();
    }

    [Fact]
    public async Task PdfAttachment_ResumeRecoversOnlySelectedChildAndPreservesCompletedPageText()
    {
        // Arrange
        var payload = Bytes("{\"components\":[]}");
        var input = Input("resume.pdf", AttachmentPdf("/AF [6 0 R 6 0 R]", "",
            PdfFileSpec("safe.json"), PdfEmbeddedStream(PdfDeflate(payload), "/Filter /FlateDecode", payload.Length)));
        var initial = await Analyzer(new() { MaxExpandedBytes = input.Content.Length + payload.Length })
            .AnalyzeAsync([input]);
        var failed = initial.Entries.Single(entry => entry.Status == CspPackageEntryStatus.Failed);

        // Act
        var resumed = await Analyzer().ResumeAsync(new([input], initial.Checkpoint!,
            new HashSet<string> { failed.Key }));

        // Assert
        resumed.Entries.Should().HaveCount(3);
        resumed.Entries.Single(entry => entry.Key == failed.Key).Content.Should().Equal(payload);
        resumed.Segments.Where(segment => segment.Locator == "page:1")
            .Should().Equal(initial.Segments.Where(segment => segment.Locator == "page:1"));
        resumed.Checkpoint!.Progress[initial.Entries[0].Key].PdfPagesCharged.Should().Be(1);
        resumed.Coverage.ExpandedBytes.Should().Be(input.Content.Length + 2 * payload.Length);
    }

    private static string PdfFileSpec(string name) =>
        $"<< /Type /Filespec /F ({name}) /EF << /F 7 0 R >> >>";

    [Fact]
    public async Task PdfAttachment_MalformedNameReferenceDoesNotHideLaterNamePairs()
    {
        // Arrange
        var input = Input("bad-name.pdf", AttachmentPdf(
            "/Names << /EmbeddedFiles << /Names [99 0 R 6 0 R (valid.json) 6 0 R (orphan)] >> >>", "",
            PdfFileSpec("safe.json"), PdfEmbeddedStream(Bytes("{\"components\":[]}"))));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Entries.Should().Contain(entry => entry.ParentKey != null && entry.AnalysisComplete);
        result.Entries.Count(entry => entry.ReasonCode == "PDF_ATTACHMENT_MALFORMED").Should().Be(2);
        result.NeedsAttention.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PdfAttachment_TruncatedOrBadChecksumZlibNeverBecomesOriginalContent(bool truncated)
    {
        // Arrange
        var encoded = PdfDeflate(Bytes("{\"components\":[]}"));
        if (truncated) encoded = encoded[..^3];
        else encoded[^1] ^= 0xff;
        var input = Input("damaged.pdf", AttachmentPdf("/AF [6 0 R]", "",
            PdfFileSpec("damaged.json"), PdfEmbeddedStream(encoded, "/Filter /FlateDecode")));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var child = result.Entries.Single(entry => entry.ParentKey is not null);
        child.ReasonCode.Should().Be("PDF_ATTACHMENT_MALFORMED");
        child.Content.Should().BeNull();
        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task PdfAttachment_EveryTraversalLimitBoundaryKeepsEnumerationIncomplete()
    {
        // Arrange
        var input = Input("node-boundaries.pdf", AttachmentPdf("", "/Annots [8 0 R]",
            PdfFileSpec("safe.json"), PdfEmbeddedStream(Bytes("{}")),
            "<< /Subtype /FileAttachment /FS 6 0 R >>"));

        // Act
        var results = new List<CspPackageAnalysisResult>();
        for (var nodes = 1; nodes <= 35; nodes++)
            results.Add(await Analyzer(new() { MaxPdfAttachmentNodes = nodes }).AnalyzeAsync([input]));

        // Assert
        foreach (var result in results.Where(result =>
            result.Entries.Any(entry => entry.ReasonCode == "PDF_ATTACHMENT_NODE_LIMIT")))
        {
            result.Coverage.EnumerationComplete.Should().BeFalse();
            result.Checkpoint!.Progress[result.Entries[0].Key].PdfAttachmentsEnumerated.Should().BeFalse();
        }
    }

    [Fact]
    public async Task PdfAttachment_DistinctFileSpecificationStreamsAreAllAccountedFor()
    {
        // Arrange
        var input = Input("variants.pdf", AttachmentPdf("/AF [6 0 R]", "",
            "<< /Type /Filespec /F (ascii.json) /UF (unicode.json) /EF << /F 7 0 R /UF 8 0 R >> >>",
            PdfEmbeddedStream(Bytes("{\"components\":[]}")),
            PdfEmbeddedStream(Bytes("{\"capabilities\":[]}"))));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var children = result.Entries.Where(entry => entry.ParentKey is not null).ToArray();
        children.Should().HaveCount(2);
        children.Should().OnlyContain(entry => entry.AnalysisComplete);
        children.Should().Contain(entry => entry.ArchivePath.EndsWith("/ascii.json"));
        children.Should().Contain(entry => entry.ArchivePath.EndsWith("/unicode.json"));
    }

    [Fact]
    public async Task PdfAttachment_MalformedVariantFilenameDoesNotHideOtherStreamReferences()
    {
        // Arrange
        var input = Input("variant-failure.pdf", AttachmentPdf("/AF [6 0 R]", "",
            "<< /Type /Filespec /F (safe.json) /DOS 99 0 R /EF << /DOS 7 0 R /F 7 0 R >> >>",
            PdfEmbeddedStream(Bytes("{\"components\":[]}"))));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Entries.Should().Contain(entry => entry.ReasonCode == "PDF_ATTACHMENT_MALFORMED");
        result.Entries.Should().Contain(entry => entry.ParentKey != null && entry.AnalysisComplete);
    }

    [Fact]
    public async Task PdfAttachment_CancellationIsNeverConvertedToCoverageSuccess()
    {
        // Arrange
        var input = Input("cancel.pdf", AttachmentPdf("/AF [6 0 R]", "",
            PdfFileSpec("safe.json"), PdfEmbeddedStream(Bytes("{}"))));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var action = () => Analyzer().AnalyzeAsync([input], cancellation.Token);

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PdfAttachment_IndirectHopsAccumulateAcrossTreeDepth()
    {
        // Arrange
        var input = Input("indirect-depth.pdf", AttachmentPdf("/Names 8 0 R", "",
            PdfFileSpec("safe.json"), PdfEmbeddedStream(Bytes("{}")),
            "9 0 R", "10 0 R", "11 0 R", "12 0 R", "13 0 R", "<< /EmbeddedFiles 14 0 R >>",
            "15 0 R", "16 0 R", "17 0 R", "18 0 R", "19 0 R",
            "<< /Names [(safe.json) 6 0 R] >>"));

        // Act
        var result = await Analyzer(new() { MaxPdfAttachmentDepth = 10 }).AnalyzeAsync([input]);

        // Assert
        result.Entries.Should().Contain(entry => entry.ReasonCode == "PDF_ATTACHMENT_DEPTH_LIMIT");
        result.Coverage.EnumerationComplete.Should().BeFalse();
    }

    private static string PdfEmbeddedStream(byte[] bytes, string filters = "", int? size = null) =>
        $"<< /Type /EmbeddedFile /Length {bytes.Length} {filters}"
        + (size.HasValue ? $" /Params << /Size {size.Value} >>" : "")
        + $" >>\nstream\n{Encoding.Latin1.GetString(bytes)}\nendstream";

    private static byte[] PdfDeflate(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var stream = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            stream.Write(bytes);
        return output.ToArray();
    }

    private static byte[] AttachmentPdf(string catalog, string page, params string[] additionalObjects)
    {
        const string pageText = "BT /F1 12 Tf 20 700 Td (Synthetic PDF page) Tj ET";
        var objects = new[]
        {
            $"<< /Type /Catalog /Pages 2 0 R {catalog} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R {page} >>",
            $"<< /Length {pageText.Length} >>\nstream\n{pageText}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        }.Concat(additionalObjects).ToArray();
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        pdf.Append(CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(pdf.ToString());
    }
}

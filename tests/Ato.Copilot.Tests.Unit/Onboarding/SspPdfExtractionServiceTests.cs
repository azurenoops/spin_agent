using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.SspPdf;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="SspPdfExtractionService"/> (T083 / FR-040..FR-046).
/// Generates SSP PDFs in-memory via QuestPDF and verifies the extraction +
/// rejection flows.
/// </summary>
public class SspPdfExtractionServiceTests
{
    static SspPdfExtractionServiceTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly SspPdfExtractionService _sut = new(NullLogger<SspPdfExtractionService>.Instance);

    [Fact]
    public async Task ExtractAsync_DigitalSspPdf_AcceptedWithFields()
    {
        // Avoid words containing "ti"/"fi" in field labels — QuestPDF's default
        // font emits Unicode ligatures for those pairs and PdfPig surfaces the
        // ligature code-points which break naive regex matching.
        await using var stream = BuildPdf(
            "System ID: ACME-PORTAL-01",
            "System Name: Acme Portal",
            "Impact Level: Moderate",
            "System Boundary: All cloud workloads suppor#ng the Acme Portal applicaon.",
            "This document is the System Security Plan compliant with NIST SP 800-53 Rev 5 controls.");

        var result = await _sut.ExtractAsync(stream, "acme.pdf");

        result.IsAccepted.Should().BeTrue();
        result.RejectReason.Should().BeNull();
        result.Fields.Should().Contain(f => f.Name == "system_identifier" && f.Value == "ACME-PORTAL-01");
        result.Fields.Should().Contain(f => f.Name == "system_name");
        result.Fields.Should().Contain(f => f.Name == "impact_level" && f.Value == "Moderate");
        result.PageCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExtractAsync_NonNistFramework_RejectedAsUnknownFramework()
    {
        await using var stream = BuildPdf(
            "System Identifier: UNRELATED-1",
            "System Name: Unrelated System",
            "This document describes compliance with CMMC Level 2 requirements.",
            "It also references ISO 27001 Annex A controls and SOC 2 Trust Service Criteria.",
            "No federal authorization framework markers appear in this document at all.",
            "Paragraph two adds extra prose so the parser gets enough text to pass the threshold check.");

        var result = await _sut.ExtractAsync(stream, "non-nist.pdf");

        result.IsAccepted.Should().BeFalse();
        result.RejectReason.Should().Be(SspPdfRejectReason.UnknownFramework);
    }

    [Fact]
    public async Task ExtractAsync_TooFewCharacters_RejectedAsImageOnly()
    {
        await using var stream = BuildPdf("hi");

        var result = await _sut.ExtractAsync(stream, "thin.pdf");

        result.IsAccepted.Should().BeFalse();
        result.RejectReason.Should().Be(SspPdfRejectReason.ImageOnly);
    }

    [Fact]
    public async Task ExtractAsync_GarbageStream_RejectedAsUnreadable()
    {
        await using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 });

        var result = await _sut.ExtractAsync(stream, "garbage.bin");

        result.IsAccepted.Should().BeFalse();
        result.RejectReason.Should().Be(SspPdfRejectReason.Unreadable);
    }

    private static MemoryStream BuildPdf(params string[] lines)
    {
        var ms = new MemoryStream();
        Document.Create(container =>
        {
            container.Page(p =>
            {
                p.Margin(36);
                p.Size(PageSizes.Letter);
                p.Content().Column(col =>
                {
                    foreach (var line in lines)
                    {
                        col.Item().Text(line).FontSize(11);
                    }
                });
            });
        }).GeneratePdf(ms);
        ms.Position = 0;
        return ms;
    }
}

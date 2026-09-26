using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;
using ClosedXML.Excel;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed partial class CspPackageAnalyzerTests
{
    [Fact]
    public async Task AuthorizationReference_ManualSyntheticFixturePreservesDisclaimerAndMetadata()
    {
        // Arrange
        const string fileName = "synthetic-authorization-reference.json";
        var content = ReadManualSyntheticFixture(fileName);
        var input = Input(fileName, content);
        using var source = JsonDocument.Parse(content);
        var declaration = source.RootElement.GetProperty("authorizationReferences")[0];

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var candidate = result.Candidates.Should().ContainSingle().Which;
        candidate.Kind.Should().Be(CspPackageCandidateKind.AuthorizationReference);
        candidate.AuthorizationReference.Should().Be(new CspPackageAuthorizationReferenceDraft(
            "Synthetic test-only authorization letter", "Synthetic test authority",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        candidate.DependencyKeys.Should().BeEmpty();
        candidate.UnresolvedDependencies.Should().BeEmpty();
        var citation = candidate.Citations.Should().ContainSingle().Which;
        citation.ArchivePath.Should().Be(fileName);
        citation.Locator.Should().Be("$/authorizationReferences[0]");
        citation.Quote.Should().Be(declaration.GetRawText());
        var segment = result.Segments.Single(item => item.Key == citation.SegmentKey);
        segment.EntryKey.Should().Be(citation.EntryKey);
        segment.Text.Should().Be(citation.Quote);
        result.Entries.Single().Content.Should().Equal(content);
        result.Segments.Should().Contain(item => item.Locator == "$/notice"
            && item.Text.Contains("not an authorization or an ATO decision.", StringComparison.Ordinal));
        result.Entries.Single().Status.Should().Be(CspPackageEntryStatus.Processed);
        result.Coverage.AnalysisComplete.Should().BeFalse();
        result.NeedsAttention.Should().BeTrue();
    }

    [Theory]
    [InlineData("json")]
    [InlineData("xml")]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task AuthorizationReference_ExplicitRecordsHaveSourceSupportedMetadata(string format)
    {
        // Arrange
        var input = AuthorizationReferenceInput(format);

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var candidate = result.Candidates.Should().ContainSingle().Which;
        candidate.Kind.ToString().Should().Be("AuthorizationReference");
        candidate.Name.Should().Be("Synthetic authorization memorandum");
        var metadata = JsonSerializer.SerializeToElement(candidate).GetProperty("AuthorizationReference");
        metadata.GetProperty("Reference").GetString().Should().Be("SYNTHETIC-REFERENCE-01");
        metadata.GetProperty("Issuer").GetString().Should().Be("Synthetic issuing office");
        metadata.GetProperty("IssuedAt").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        metadata.GetProperty("ExpiresAt").GetDateTimeOffset().Should().Be(new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero));
        candidate.DependencyKeys.Should().BeEmpty();
        candidate.UnresolvedDependencies.Should().BeEmpty();
        foreach (var citation in candidate.Citations)
        {
            var source = result.Segments.Single(segment => segment.Key == citation.SegmentKey);
            citation.Quote.Should().Be(source.Text).And.Contain("SYNTHETIC-REFERENCE-01");
            citation.EntryKey.Should().Be(source.EntryKey);
            citation.ArchivePath.Should().Be(source.ArchivePath);
            citation.Locator.Should().Be(source.Locator);
        }
        if (format == "json") result.NeedsAttention.Should().BeFalse();
        else result.NeedsAttention.Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"authorizationReference":{"reference":"Synthetic memorandum"}}""")]
    [InlineData("""{"kind":"AuthorizationReference","reference":"Synthetic memorandum"}""")]
    [InlineData("""{"authorizationReferences":[{"reference":"Synthetic memorandum","issuer":null,"issuedAt":null,"expiresAt":null}]}""")]
    public async Task AuthorizationReference_ExplicitJsonShapesDoNotNeedAModel(string json)
    {
        // Arrange
        var input = Input("reference.json", Bytes(json));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var candidate = result.Candidates.Should().ContainSingle().Which;
        candidate.Kind.ToString().Should().Be("AuthorizationReference");
        var metadata = JsonSerializer.SerializeToElement(candidate).GetProperty("AuthorizationReference");
        metadata.GetProperty("Reference").GetString().Should().Be("Synthetic memorandum");
        metadata.GetProperty("Issuer").ValueKind.Should().Be(JsonValueKind.Null);
        metadata.GetProperty("IssuedAt").ValueKind.Should().Be(JsonValueKind.Null);
        metadata.GetProperty("ExpiresAt").ValueKind.Should().Be(JsonValueKind.Null);
        result.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public async Task AuthorizationReference_NestedArchivePreservesStableCitationAndCheckpointIdentity()
    {
        // Arrange
        var input = Input("outer.zip", Zip(("nested.zip", Zip(("reference.json",
            Bytes("""{"authorizationReferences":[{"reference":"SYNTHETIC-NESTED"}]}"""))))));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);
        var replay = await Analyzer().AnalyzeAsync([input]);
        var checkpoint = JsonSerializer.Deserialize<CspPackageAnalysisCheckpoint>(
            JsonSerializer.Serialize(result.Checkpoint))!;
        var resumed = await Analyzer().ResumeAsync(new([input], checkpoint, new HashSet<string>()));

        // Assert
        var candidate = result.Candidates.Should().ContainSingle().Which;
        candidate.Key.Should().Be(replay.Candidates.Single().Key);
        candidate.Citations.Single().ArchivePath.Should().Be("outer.zip!/nested.zip!/reference.json");
        candidate.Citations.Single().Locator.Should().Be("$/authorizationReferences[0]");
        resumed.Candidates.Should().BeEquivalentTo(result.Candidates);
        resumed.Coverage.Should().Be(result.Coverage);
        JsonSerializer.SerializeToElement(resumed.Candidates.Single()).GetProperty("AuthorizationReference")
            .GetProperty("Reference").GetString().Should().Be("SYNTHETIC-NESTED");
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"reference":null}""")]
    [InlineData("""{"reference":42}""")]
    [InlineData("""{"reference":"   "}""")]
    [InlineData("""{"reference":"Synthetic","issuer":42}""")]
    [InlineData("""{"reference":"Synthetic","issuedAt":true}""")]
    [InlineData("""{"reference":"Synthetic","issuedAt":"01/02/2026"}""")]
    [InlineData("""{"reference":"Synthetic","issuedAt":"2026-02-30"}""")]
    [InlineData("""{"reference":"Synthetic","issuedAt":"2026-01-01T10:00:00"}""")]
    [InlineData("""{"reference":"Synthetic","issuedAt":"2026-02-01","expiresAt":"2026-01-31"}""")]
    [InlineData("""{"reference":"Synthetic","issuer":"A","Issuer":"B"}""")]
    public async Task AuthorizationReference_InvalidDeclarationsAreActionableWithoutPartialCandidates(string record)
    {
        // Arrange
        var input = Input("invalid-reference.json", Bytes("""{"authorizationReferences":[""" + record + "]}"));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().BeEmpty();
        result.Entries.Single().Status.Should().Be(CspPackageEntryStatus.Unreadable);
        result.Entries.Single().Reason.Should().NotBeNullOrWhiteSpace();
        result.NeedsAttention.Should().BeTrue();
    }

    [Theory]
    [InlineData(2000, 500, true)]
    [InlineData(2001, 500, false)]
    [InlineData(2000, 501, false)]
    public async Task AuthorizationReference_FieldLimitsAreExactAndNeverTruncated(int referenceLength, int issuerLength, bool valid)
    {
        // Arrange
        var reference = new string('R', referenceLength);
        var issuer = new string('I', issuerLength);
        var input = Input("bounded-reference.json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            authorizationReferences = new[] { new { name = "Synthetic reference", reference, issuer } }
        }));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        if (valid)
        {
            var metadata = JsonSerializer.SerializeToElement(result.Candidates.Should().ContainSingle().Which)
                .GetProperty("AuthorizationReference");
            metadata.GetProperty("Reference").GetString().Should().Be(reference);
            metadata.GetProperty("Issuer").GetString().Should().Be(issuer);
            result.NeedsAttention.Should().BeFalse();
        }
        else
        {
            result.Candidates.Should().BeEmpty();
            result.Entries.Single().Status.Should().Be(CspPackageEntryStatus.Unreadable);
            result.NeedsAttention.Should().BeTrue();
        }
    }

    [Fact]
    public async Task AuthorizationReference_CannotSatisfyComponentOrCapabilityDependencies()
    {
        // Arrange
        var input = Input("dependencies.json", Bytes("""
            {"authorizationReferences":[{"id":"provider","reference":"Synthetic reference"}],
             "capabilities":[{"id":"capability","name":"Synthetic capability","componentIds":["provider"]}]}
            """));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().Contain(candidate => candidate.Kind.ToString() == "AuthorizationReference");
        var capability = result.Candidates.Single(candidate => candidate.Kind == CspPackageCandidateKind.Capability);
        capability.DependencyKeys.Should().BeEmpty();
        capability.UnresolvedDependencies.Should().Equal("provider");
        result.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizationReference_DoesNotMakeAnExplicitComponentIdAmbiguous()
    {
        // Arrange
        var input = Input("shared-source-ids.json", Bytes("""
            {"authorizationReferences":[{"id":"provider","reference":"Synthetic reference"}],
             "components":[{"id":"provider","name":"Synthetic component","type":"service"}],
             "capabilities":[{"name":"Synthetic capability","componentIds":["provider"]}]}
            """));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().Contain(candidate => candidate.Kind.ToString() == "AuthorizationReference");
        var component = result.Candidates.Single(candidate => candidate.Kind == CspPackageCandidateKind.Component);
        var capability = result.Candidates.Single(candidate => candidate.Kind == CspPackageCandidateKind.Capability);
        capability.DependencyKeys.Should().Equal(component.Key);
        capability.UnresolvedDependencies.Should().BeEmpty();
        result.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public async Task AuthorizationReference_DoesNotInferOrVerifyAnAuthorizationFromOtherContent()
    {
        // Arrange
        var inputs = new[]
        {
            Input("SYNTHETIC-ATO-letter.txt", Bytes("Authorization reference might exist. No memorandum is supplied."), "text"),
            Input("component.json", Bytes("""{"components":[{"name":"Synthetic authorized component","type":"service","controlIds":["CA-6"]}]}"""), "component"),
            Input("claims.json", Bytes("""{"authorizationReference":{"reference":"Synthetic reference","verified":true}}"""), "claims")
        };

        // Act
        var result = await Analyzer().AnalyzeAsync(inputs);

        // Assert
        var reference = result.Candidates.Where(candidate => candidate.Kind.ToString() == "AuthorizationReference")
            .Should().ContainSingle().Which;
        reference.Citations.Should().OnlyContain(citation => citation.ArchivePath == "claims.json");
        var metadata = JsonSerializer.SerializeToElement(reference).GetProperty("AuthorizationReference");
        metadata.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("Reference", "Issuer", "IssuedAt", "ExpiresAt");
        result.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizationReference_CandidateBudgetIsNotBypassed()
    {
        // Arrange
        var input = Input("many-references.json", Bytes("""
            {"authorizationReferences":[{"reference":"Synthetic one"},{"reference":"Synthetic two"}]}
            """));

        // Act
        var result = await Analyzer(new() { MaxCandidates = 1 }).AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().ContainSingle();
        result.Entries.Single().ReasonCode.Should().Be("CANDIDATE_COUNT_LIMIT");
        result.NeedsAttention.Should().BeTrue();
    }

    [Theory]
    [InlineData("json")]
    [InlineData("xml")]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task AuthorizationReference_ConflictingDeclarationsAreNotResolvedByGuessing(string format)
    {
        // Arrange
        CspPackageAnalysisInput input;
        if (format == "json")
            input = Input("conflict.json", Bytes("""
                {"authorizationReferences":[{"kind":"Component","reference":"Synthetic memorandum"}]}
                """));
        else if (format == "xml")
            input = Input("conflict.xml", Bytes("""
                <records><authorizationReference reference="Synthetic memorandum" issuer="Synthetic A">
                <issuer>Synthetic B</issuer></authorizationReference></records>
                """));
        else if (format == "csv")
            input = Input("conflict.csv", Bytes("kind,reference,issuer,issuer\nAuthorizationReference,Synthetic memorandum,Synthetic A,Synthetic B"));
        else
        {
            using var workbook = new XLWorkbook(new MemoryStream(AuthorizationReferenceInput("xlsx").Content));
            workbook.Worksheet(1).Cell(1, 6).Value = "issuer";
            workbook.Worksheet(1).Cell(2, 6).Value = "Conflicting synthetic issuer";
            using var buffer = new MemoryStream();
            workbook.SaveAs(buffer);
            input = Input("conflict.xlsx", buffer.ToArray());
        }

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().BeEmpty();
        result.Entries.Should().Contain(entry => entry.Status == CspPackageEntryStatus.Unreadable);
        result.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizationReference_WhitespaceDoesNotBypassIssuerLengthLimit()
    {
        // Arrange
        var input = Input("long-issuer.json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            authorizationReference = new { reference = "Synthetic memorandum", issuer = new string(' ', 501) }
        }));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().BeEmpty();
        result.Entries.Single().Status.Should().Be(CspPackageEntryStatus.Unreadable);
        result.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizationReference_DatesRetainSourceOffsetAndCompareInstants()
    {
        // Arrange
        var input = Input("offsets.json", Bytes("""
            {"authorizationReference":{"reference":"Synthetic memorandum",
             "issuedAt":"2026-01-02T01:00:00.1234567-05:00","expiresAt":"2026-01-02T06:00:00.1234567Z"}}
            """));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var metadata = JsonSerializer.SerializeToElement(result.Candidates.Should().ContainSingle().Which)
            .GetProperty("AuthorizationReference");
        var issued = metadata.GetProperty("IssuedAt").GetDateTimeOffset();
        issued.Offset.Should().Be(TimeSpan.FromHours(-5));
        issued.Should().Be(metadata.GetProperty("ExpiresAt").GetDateTimeOffset());
        result.NeedsAttention.Should().BeFalse();
    }

    private static CspPackageAnalysisInput AuthorizationReferenceInput(string format)
    {
        const string json = """
            {"authorizationReferences":[{"name":"Synthetic authorization memorandum",
            "reference":"SYNTHETIC-REFERENCE-01","issuer":"Synthetic issuing office",
            "issuedAt":"2026-01-02","expiresAt":"2027-01-02T00:00:00Z"}]}
            """;
        if (format == "json") return Input("reference.json", Bytes(json));
        if (format == "xml") return Input("reference.xml", Bytes("""
            <records><authorizationReference name="Synthetic authorization memorandum"
            reference="SYNTHETIC-REFERENCE-01" issuer="Synthetic issuing office"
            issuedAt="2026-01-02" expiresAt="2027-01-02T00:00:00Z"/></records>
            """));
        string[] headers = ["kind", "name", "reference", "issuer", "issuedAt", "expiresAt"];
        string[] values = ["AuthorizationReference", "Synthetic authorization memorandum",
            "SYNTHETIC-REFERENCE-01", "Synthetic issuing office", "2026-01-02", "2027-01-02T00:00:00Z"];
        if (format == "csv") return Input("reference.csv", Bytes(string.Join(',', headers) + "\n" + string.Join(',', values)));
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic references");
        for (var index = 0; index < headers.Length; index++)
        {
            sheet.Cell(1, index + 1).Value = headers[index];
            sheet.Cell(2, index + 1).Value = values[index];
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Input("reference.xlsx", stream.ToArray());
    }
}

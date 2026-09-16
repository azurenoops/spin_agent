using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public class EvidenceNarrativeClassifierTests
{
    [Theory]
    [InlineData(ArtifactCategory.ScanResult)]
    [InlineData(ArtifactCategory.ConfigurationExport)]
    [InlineData(ArtifactCategory.AuditLog)]
    [InlineData(ArtifactCategory.TestResult)]
    public async Task ClassifyAsync_TechnicalCategory_ReturnsTechnical(ArtifactCategory category)
    {
        // Arrange
        var classifier = new EvidenceNarrativeClassifier();
        var artifact = CreateArtifact("account-policy.json", category);

        // Act
        var result = await classifier.ClassifyAsync(artifact);

        // Assert
        result.Type.Should().Be(EvidenceNarrativeType.Technical);
        result.Rationale.Should().StartWith("ArtifactCategory=");
    }

    [Theory]
    [InlineData("Account Management Policy.pdf")]
    [InlineData("incident-response-procedure.docx")]
    [InlineData("Access_Control_SOP_v2.pdf")]
    [InlineData("Contingency Plan.pdf")]
    public async Task ClassifyAsync_PolicyFilename_ReturnsPolicy(string fileName)
    {
        // Arrange
        var classifier = new EvidenceNarrativeClassifier();
        var artifact = CreateArtifact(fileName, ArtifactCategory.Other);

        // Act
        var result = await classifier.ClassifyAsync(artifact);

        // Assert
        result.Type.Should().Be(EvidenceNarrativeType.Policy);
        result.Rationale.Should().Be("Filename matches policy document pattern");
    }

    [Fact]
    public async Task ClassifyAsync_UnknownArtifact_ReturnsCombinedLowConfidence()
    {
        // Arrange
        var classifier = new EvidenceNarrativeClassifier();
        var artifact = CreateArtifact("miscellaneous.txt", ArtifactCategory.Other);

        // Act
        var result = await classifier.ClassifyAsync(artifact);

        // Assert
        result.Type.Should().Be(EvidenceNarrativeType.Combined);
        result.Rationale.Should().Be("LowConfidence");
    }

    private static EvidenceArtifact CreateArtifact(string fileName, ArtifactCategory category) => new()
    {
        FileName = fileName,
        ArtifactCategory = category
    };
}
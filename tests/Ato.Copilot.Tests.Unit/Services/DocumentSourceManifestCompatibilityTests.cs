using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Dtos.Dashboard;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class DocumentSourceManifestCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyManifest_RoundTripPreservesCanonicalBytesAndHash(bool webJson)
    {
        // Arrange
        var options = new JsonSerializerOptions(webJson ? JsonSerializerDefaults.Web : JsonSerializerDefaults.General);
        var legacy = new LegacyManifest("GeneratedOscalContent",
            [new("ApprovedProfile", "section", "approval", new string('A', 64))],
            [new("ProviderDecision", "decision", "revision", new string('B', 64))])
        {
            Narratives = [new("ApprovedNarrative", "control", "version", new string('C', 64))],
            EvidenceStatus = "EvaluatedApprovedSummariesOnly"
        };
        var original = JsonSerializer.Serialize(legacy, options);
        var originalHash = SHA256.HashData(Encoding.UTF8.GetBytes(original));

        // Act
        var restored = JsonSerializer.Deserialize<DocumentSourceManifest>(original, options)!;
        var roundTrip = JsonSerializer.Serialize(restored, options);
        var working = JsonSerializer.Serialize(restored with { PreviewOnly = true }, options);

        // Assert
        restored.PreviewOnly.Should().BeFalse();
        restored.HasWorkingProfileSources.Should().BeFalse();
        roundTrip.Should().Be(original);
        SHA256.HashData(Encoding.UTF8.GetBytes(roundTrip)).Should().Equal(originalHash);
        using var legacyJson = JsonDocument.Parse(roundTrip);
        legacyJson.RootElement.TryGetProperty(webJson ? "previewOnly" : "PreviewOnly", out _).Should().BeFalse();
        using var workingJson = JsonDocument.Parse(working);
        workingJson.RootElement.GetProperty(webJson ? "previewOnly" : "PreviewOnly").GetBoolean().Should().BeTrue();
        workingJson.RootElement.TryGetProperty(webJson ? "hasWorkingProfileSources" : "HasWorkingProfileSources", out _).Should().BeFalse();
    }

    // Exact pre-preview-mode contract: preserve its canonical property order as well as values.
    private sealed record LegacyManifest(string Scope, IReadOnlyList<DocumentSourceReference> Profiles,
        IReadOnlyList<DocumentSourceReference> ProviderSources)
    {
        public IReadOnlyList<DocumentSourceReference> Narratives { get; init; } = [];
        public string OtherSources => "CurrentWorkingDataAtGeneration";
        public IReadOnlyList<DocumentEvidenceReference> Evidence { get; init; } = [];
        public string EvidenceStatus { get; init; } = "NotEvaluated";
        public IReadOnlyList<DocumentResponsibilityReference> Responsibilities { get; init; } = [];
    }
}

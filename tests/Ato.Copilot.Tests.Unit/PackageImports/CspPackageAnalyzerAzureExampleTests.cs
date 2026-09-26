using Ato.Copilot.Core.Interfaces.PackageImports;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed partial class CspPackageAnalyzerTests
{
    [Fact]
    public async Task AzureExample_HasCompleteSourceBackedInventoryWithoutAuthorizationClaims()
    {
        // Arrange
        const string fileName = "azure-example-package.json";
        var content = ReadManualSyntheticFixture(fileName);
        var input = Input(fileName, content, "azure-example-source");

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.NeedsAttention.Should().BeFalse();
        result.Coverage.AnalysisComplete.Should().BeTrue();
        result.Entries.Should().ContainSingle().Which.Content.Should().Equal(content);
        result.Candidates.Should().HaveCount(16);
        result.Candidates.Where(candidate => candidate.Kind == CspPackageCandidateKind.Component)
            .Select(candidate => candidate.Name).Should().BeEquivalentTo(
                "Microsoft Entra ID (example)", "Azure Monitor (example)",
                "Azure Key Vault (example)", "Azure Firewall (example)");
        result.Candidates.Count(candidate => candidate.Kind == CspPackageCandidateKind.Capability).Should().Be(4);
        result.Candidates.Where(candidate => candidate.Kind == CspPackageCandidateKind.ControlMapping)
            .Select(candidate => candidate.ControlId).Should().BeEquivalentTo("IA-2", "AU-6", "SC-12", "SC-7");
        result.Candidates.Where(candidate => candidate.Kind == CspPackageCandidateKind.Responsibility)
            .Select(candidate => candidate.Responsibility).Should().BeEquivalentTo("Shared", "Shared", "Shared", "Shared");
        result.Candidates.Should().NotContain(candidate => candidate.Kind == CspPackageCandidateKind.AuthorizationReference);
        result.Candidates.Should().OnlyContain(candidate => candidate.UnresolvedDependencies.Count == 0);

        foreach (var citation in result.Candidates.SelectMany(candidate => candidate.Citations))
        {
            citation.ArtifactId.Should().Be("azure-example-source");
            citation.ArchivePath.Should().Be(fileName);
            citation.Locator.Should().StartWith("$/");
            citation.Quote.Should().Contain("Example only; not an official Microsoft ATO package or verified authorization.");
            result.Segments.Single(segment => segment.Key == citation.SegmentKey).Text.Should().Be(citation.Quote);
        }
    }

    [Fact]
    public async Task AzureExample_ResolvesContributorsAndReplaysTheSameCandidates()
    {
        // Arrange
        var input = Input("azure-example-package.json", ReadManualSyntheticFixture("azure-example-package.json"));

        // Act
        var first = await Analyzer().AnalyzeAsync([input]);
        var replay = await Analyzer().AnalyzeAsync([input]);

        // Assert
        replay.Candidates.Should().BeEquivalentTo(first.Candidates);
        var capabilities = first.Candidates.Where(candidate => candidate.Kind == CspPackageCandidateKind.Capability).ToArray();
        capabilities.Sum(candidate => candidate.DependencyKeys.Count).Should().Be(5);
        foreach (var capability in capabilities)
            capability.DependencyKeys.Should().OnlyContain(key =>
                first.Candidates.Any(candidate => candidate.Key == key && candidate.Kind == CspPackageCandidateKind.Component));
        var monitoring = capabilities.Single(candidate => candidate.SourceId == "azure-example-audit");
        first.Candidates.Where(candidate => monitoring.DependencyKeys.Contains(candidate.Key))
            .Select(candidate => candidate.SourceId).Should().BeEquivalentTo("azure-example-monitor", "azure-example-entra");
    }
}

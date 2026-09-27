using Ato.Copilot.Agents.Services.PackageImports;
using Ato.Copilot.Core.Interfaces.PackageImports;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed class ProviderDemoPackageTests
{
    [Theory]
    [InlineData("azure-il5-shared-services/release-1.2", 8, 69)]
    [InlineData("azure-il5-shared-services/release-1.3-proposed", 8, 69)]
    [InlineData("microsoft-365-collaboration/release-1.0", 5, 46)]
    public async Task DemoBaseline_IsFullyAnalyzedWithoutAi_AndAllDependenciesResolve(string edition, int capabilities, int candidates)
    {
        // Arrange
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Ato.Copilot.sln"))) root = root.Parent;
        root.Should().NotBeNull("the demo package is part of this repository");
        var source = Path.Combine(root!.FullName, "demos", "provider-offerings", edition, "review-source.zip");
        var analyzer = new CspPackageAnalyzer(NullLogger<CspPackageAnalyzer>.Instance, chatClient: null);

        // Act
        var result = await analyzer.AnalyzeAsync([
            new CspPackageAnalysisInput("demo-source", "review-source.zip", "application/zip", await File.ReadAllBytesAsync(source))
        ]);

        // Assert
        result.Coverage.TotalEntries.Should().Be(7, "one container plus six structured source documents");
        result.Coverage.AnalysisComplete.Should().BeTrue();
        result.NeedsAttention.Should().BeFalse(string.Join("; ", result.Entries.Select(e => $"{e.ArchivePath}: {e.Reason}")));
        result.Candidates.Should().HaveCount(candidates);
        result.Candidates.Count(c => c.Kind == CspPackageCandidateKind.Capability).Should().Be(capabilities);
        result.Candidates.Should().OnlyContain(c => c.Citations.Count > 0 && c.UnresolvedDependencies.Count == 0);
    }
}

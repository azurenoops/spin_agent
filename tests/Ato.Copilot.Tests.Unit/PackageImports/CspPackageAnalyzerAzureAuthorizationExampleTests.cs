using System.Runtime.CompilerServices;
using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed partial class CspPackageAnalyzerTests
{
    private const string AzureAuthorizationFixtureName = "azure-authorization-example.json";
    private const string AzureAuthorizationDisclaimer =
        "Synthetic test data only; not a valid ATO or official Microsoft authorization.";

    [Fact]
    public async Task AzureAuthorizationExample_CompletesAllFamiliesWithoutModelOrAuthorityConfirmation()
    {
        // Arrange
        var content = ReadAzureAuthorizationFixture();
        var input = Input(AzureAuthorizationFixtureName, content, "synthetic-azure-authorization");

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.AnalysisProfileVersion.Should().Be(2);
        result.NeedsAttention.Should().BeFalse();
        result.Coverage.EnumerationComplete.Should().BeTrue();
        result.Coverage.AnalysisComplete.Should().BeTrue();
        result.Entries.Should().ContainSingle().Which.Content.Should().Equal(content);
        result.Entries.Single().Status.Should().Be(CspPackageEntryStatus.Processed);
        result.FamilyCoverage.Single().Value.Should().HaveCount(5).And.OnlyContain(family =>
            family.Status == CspPackageFamilyCoverageStatus.Analyzed && family.Reason == null);
        result.Checkpoint!.Progress.Values.Should().OnlyContain(progress => progress.SemanticCallsCharged == 0);

        result.Candidates.Should().HaveCount(23);
        result.Candidates.Count(candidate => candidate.Kind == CspPackageCandidateKind.Component).Should().Be(4);
        result.Candidates.Count(candidate => candidate.Kind == CspPackageCandidateKind.Capability).Should().Be(4);
        result.Candidates.Count(candidate => candidate.Kind == CspPackageCandidateKind.ControlMapping).Should().Be(4);
        result.Candidates.Count(candidate => candidate.Kind == CspPackageCandidateKind.Responsibility).Should().Be(4);
        result.Candidates.Should().NotContain(candidate => candidate.Kind == CspPackageCandidateKind.AuthorizationReference);
        result.Candidates.Should().OnlyContain(candidate => candidate.UnresolvedDependencies.Count == 0);
        result.Candidates.Where(candidate => candidate.Kind == CspPackageCandidateKind.Capability)
            .Should().OnlyContain(candidate => candidate.DependencyKeys.Count > 0
                && candidate.DependencyKeys.All(key => result.Candidates.Any(target =>
                    target.Key == key && target.Kind == CspPackageCandidateKind.Component)));

        var decision = result.Candidates.Single(candidate => candidate.Kind == CspPackageCandidateKind.AuthorizationDecisionClaim);
        decision.Claim!.AuthorizationDecision!.SubjectKind.Should().Be("Provider");
        decision.Claim.AuthorizationDecision.Reference.Should().Be("SYN-AZ-DECISION-001");
        decision.Claim.AuthorizationDecision.Authority.Should().Contain("Fictional");
        decision.Claim.AuthorizationDecision.StatusAsStated.Should().Be("Fictional test decision; not effective");
        decision.Claim.AuthorizationDecision.DecisionDate.Should().Be("2026-09-01");
        decision.Claim.AuthorizationDecision.ExpirationDate.Should().Be("2027-08-31");
        decision.Claim.AuthorizationDecision.Conditions.Should().NotBeEmpty();
        decision.Claim.AuthorizationDecision.Exclusions.Should().Contain("Customer mission-system authorization is excluded.");
        decision.Claim.Qualifications.Should().Contain("No real authorizing official signed or issued this decision.");

        var boundaries = result.Candidates.Where(candidate => candidate.Kind == CspPackageCandidateKind.BoundaryClaim).ToArray();
        boundaries.Should().HaveCount(2);
        boundaries.Select(candidate => candidate.Claim!.Boundary!.Relationship).Should().BeEquivalentTo("Included", "Excluded");
        boundaries.Single(candidate => candidate.Claim!.Boundary!.Relationship == "Excluded")
            .Claim!.Boundary!.Scope.Should().Contain("customer application code").And.Contain("mission databases");

        var findings = result.Candidates.Where(candidate => candidate.Kind == CspPackageCandidateKind.AssessmentFinding).ToArray();
        findings.Select(candidate => candidate.SourceId).Should().BeEquivalentTo("SYN-AZ-FIND-001", "SYN-AZ-FIND-002");
        findings.Should().OnlyContain(candidate => candidate.Claim!.AssessmentFinding!.StatusAsStated == "Open");
        var poams = result.Candidates.Where(candidate => candidate.Kind == CspPackageCandidateKind.PoamItem).ToArray();
        poams.Select(candidate => candidate.SourceId).Should().BeEquivalentTo("SYN-AZ-POAM-001", "SYN-AZ-POAM-002");
        poams.Should().OnlyContain(candidate => candidate.Claim!.PoamItem!.SubmittedEvidenceReferences.Count == 0
            && candidate.Claim.PoamItem.RequiredClosureEvidence.Count > 0
            && candidate.Claim.PoamItem.Milestones.Count > 0);
        poams.SelectMany(candidate => candidate.Claim!.Relationships).Select(relation => relation.TargetSourceId)
            .Should().BeEquivalentTo("SYN-AZ-FIND-001", "SYN-AZ-FIND-002");
        result.Candidates.Where(candidate => candidate.Claim is not null).SelectMany(candidate => candidate.Claim!.Relationships)
            .Should().HaveCount(12).And.OnlyContain(relation => relation.Resolution == "Resolved");

        foreach (var candidate in result.Candidates.Where(candidate => candidate.Claim is not null))
        {
            candidate.DependencyKeys.Should().BeEmpty();
            candidate.Claim!.Qualifications.Should().Contain(AzureAuthorizationDisclaimer);
            candidate.Claim.Relationships.Should().NotContain(relation => relation.Resolution != "Resolved");
            candidate.Claim.FieldSources.Should().NotBeEmpty();
            foreach (var field in candidate.Claim.FieldSources)
                field.CitationIndexes.Should().NotBeEmpty().And.OnlyContain(index => index >= 0 && index < candidate.Citations.Count);
        }
        foreach (var citation in result.Candidates.SelectMany(candidate => candidate.Citations))
        {
            citation.ArtifactId.Should().Be(input.ArtifactId);
            citation.ArchivePath.Should().Be(AzureAuthorizationFixtureName);
            citation.Locator.Should().StartWith("$/");
            citation.Quote.Should().Contain(AzureAuthorizationDisclaimer);
            result.Segments.Single(segment => segment.Key == citation.SegmentKey).Text.Should().Contain(citation.Quote);
        }
    }

    [Fact]
    public async Task AzureAuthorizationExample_PreservesTypedClaimsAndStableIdentityAcrossCheckpointReplay()
    {
        // Arrange
        var input = Input(AzureAuthorizationFixtureName, ReadAzureAuthorizationFixture(), "synthetic-azure-authorization");
        var initial = await Analyzer().AnalyzeAsync([input]);
        var retained = JsonSerializer.Deserialize<CspPackageAnalysisCheckpoint>(JsonSerializer.Serialize(initial.Checkpoint))!;

        // Act
        var replay = await Analyzer().ResumeAsync(new([input], retained, new HashSet<string>()));
        var repeated = await Analyzer().AnalyzeAsync([input]);

        // Assert
        replay.NeedsAttention.Should().BeFalse();
        replay.Candidates.Should().BeEquivalentTo(initial.Candidates);
        replay.Segments.Should().BeEquivalentTo(initial.Segments);
        replay.FamilyCoverage.Should().BeEquivalentTo(initial.FamilyCoverage);
        replay.Entries.Should().BeEquivalentTo(initial.Entries);
        replay.Checkpoint!.Progress.Should().BeEquivalentTo(initial.Checkpoint!.Progress);
        repeated.Candidates.Should().BeEquivalentTo(initial.Candidates);
    }

    private static byte[] ReadAzureAuthorizationFixture([CallerFilePath] string source = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,
            "../../../docs/examples/package-imports", AzureAuthorizationFixtureName));
        File.Exists(path).Should().BeTrue("the additive synthetic manual input must be available for local acceptance");
        return File.ReadAllBytes(path);
    }
}

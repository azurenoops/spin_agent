using Ato.Copilot.Core.Interfaces.PackageImports;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed partial class CspPackageAnalyzerTests
{
    [Fact]
    public async Task LabeledClaims_BoundaryRetainsIncludedAndExcludedScopeWithoutInventingInventory()
    {
        // Arrange
        const string text = "Authorization boundary The provider boundary includes CMP-471 through CMP-474. " +
            "Customer application code and mission databases are outside the provider boundary. " +
            "All information in this fixture is synthetic test data. " +
            "Mission owners retain responsibility for their own authorization decisions.";

        // Act
        var result = await Analyzer().AnalyzeAsync([Input("unrelated-name.txt", Bytes(text))]);

        // Assert
        var boundaries = result.Candidates.Where(c => c.Kind == CspPackageCandidateKind.BoundaryClaim).ToArray();
        boundaries.Should().HaveCount(2);
        boundaries.Single(c => c.Claim!.Boundary!.Relationship == "Included").Claim!.Boundary!.Scope
            .Should().Be("The provider boundary includes CMP-471 through CMP-474.");
        boundaries.Single(c => c.Claim!.Boundary!.Relationship == "Excluded").Claim!.Boundary!.Scope
            .Should().Be("Customer application code and mission databases are outside the provider boundary.");
        boundaries.Should().OnlyContain(c => c.Claim!.Qualifications.Any(q => q.Contains("synthetic test data")));
        result.Candidates.Should().OnlyContain(c => c.Kind == CspPackageCandidateKind.BoundaryClaim);
        result.NeedsAttention.Should().BeTrue();
        result.Coverage.AnalysisComplete.Should().BeFalse();
        AssertLabeledClaimSources(result);
    }

    [Fact]
    public async Task LabeledClaims_FindingsAndRemediationKeepDistinctIdsFieldsAndRelationships()
    {
        // Arrange
        const string text = "Assessment summary and remediation plan Assessment scope " +
            "Synthetic review date: 2026-09-10. Assessor: Example Test Assessment Team. " +
            "No actual assessment was performed. " +
            "FIND-471 Restore test evidence Related component: CMP-473. Related capability: CAP-474. " +
            "Control: CP-9. Fictional observation: application consistency sign-off is missing. Test severity: Moderate. " +
            "POAM-471 Corrective action Owner: Example Recovery Lead. " +
            "Milestone: complete a synthetic restore by 2026-10-15. Status: Open. " +
            "Closure evidence: a reviewed restore report. " +
            "FIND-472 Incident contact validation Related component: CMP-474. Related capability: CAP-476. " +
            "Control: IR-4. Fictional observation: mission contact confirmation is overdue. Test severity: Low. " +
            "POAM-472 owner: Example Response Lead. Target: 2026-10-01. Status: Open. " +
            "Extraction boundary Findings and remediation actions must not be mistaken for completed corrective actions.";

        // Act
        var result = await Analyzer().AnalyzeAsync([Input("assessment.txt", Bytes(text))]);

        // Assert
        result.Candidates.Count(c => c.Kind == CspPackageCandidateKind.AssessmentFinding).Should().Be(2);
        result.Candidates.Count(c => c.Kind == CspPackageCandidateKind.PoamItem).Should().Be(2);
        var first = result.Candidates.Single(c => c.SourceId == "FIND-471");
        first.Claim!.AssessmentFinding!.Observation.Should().Be("application consistency sign-off is missing");
        first.Claim.AssessmentFinding.Assessor.Should().Be("Example Test Assessment Team");
        first.Claim.AssessmentFinding.AssessmentDate.Should().Be("2026-09-10");
        first.Claim.AssessmentFinding.SeverityAsStated.Should().Be("Moderate");
        first.Claim.AssessmentFinding.ControlIds.Should().Equal("CP-9");
        first.Claim.Relationships.Should().Contain(r => r.Kind == "FindingComponent" && r.TargetSourceId == "CMP-473");
        first.Claim.Relationships.Should().Contain(r => r.Kind == "FindingCapability" && r.TargetSourceId == "CAP-474");
        first.Claim.Qualifications.Should().Contain(q => q.Contains("No actual assessment was performed"));
        var plan = result.Candidates.Single(c => c.SourceId == "POAM-471").Claim!.PoamItem!;
        plan.OwnerAsStated.Should().Be("Example Recovery Lead");
        plan.StatusAsStated.Should().Be("Open");
        plan.Milestones.Should().ContainSingle().Which.DueDate.Should().Be("2026-10-15");
        plan.Milestones[0].Description.Should().Be("complete a synthetic restore by 2026-10-15");
        plan.RequiredClosureEvidence.Should().Equal("a reviewed restore report");
        plan.SubmittedEvidenceReferences.Should().BeEmpty();
        var secondPlan = result.Candidates.Single(c => c.SourceId == "POAM-472").Claim!.PoamItem!;
        secondPlan.OwnerAsStated.Should().Be("Example Response Lead");
        secondPlan.Milestones.Should().ContainSingle().Which.DueDate.Should().Be("2026-10-01");
        result.Candidates.Should().OnlyContain(c => c.DependencyKeys.Count == 0);
        result.NeedsAttention.Should().BeTrue();
        AssertLabeledClaimSources(result);
    }

    [Fact]
    public async Task LabeledClaims_FictionalDecisionPreservesConditionsExclusionsAndUnsignedDisclaimer()
    {
        // Arrange
        const string text = "Fictional authorization reference Not a real authorization " +
            "SYNTHETIC TEST RECORD - NOT A VALID ATO. It has no legal, operational or authorization effect. " +
            "Reference fields Reference ID: TEST-DECISION-471. Title: Example synthetic provider decision. " +
            "Issuing authority: Fictional Test Authorizing Official. Decision date: 2026-09-01. " +
            "Expiration date: 2027-08-31. Scope: provider components CMP-471 through CMP-474 only. " +
            "Illustrative conditions For this fictional scenario, POAM-471 remains open and requires tracking. " +
            "Customer mission applications and their authorization decisions are excluded from this reference. " +
            "Signature No signature. No seal. No actual authorizing official. " +
            "Extracted metadata must remain unconfirmed until reviewed by an authorized administrator.";

        // Act
        var result = await Analyzer().AnalyzeAsync([Input("decision.txt", Bytes(text))]);

        // Assert
        var candidate = result.Candidates.Single(c => c.Kind == CspPackageCandidateKind.AuthorizationDecisionClaim);
        var claim = candidate.Claim!;
        claim.AuthorizationDecision!.Reference.Should().Be("TEST-DECISION-471");
        claim.AuthorizationDecision.Subject.Should().Be("Example synthetic provider decision");
        claim.AuthorizationDecision.SubjectKind.Should().Be("Unspecified");
        claim.AuthorizationDecision.Authority.Should().Be("Fictional Test Authorizing Official");
        claim.AuthorizationDecision.DecisionDate.Should().Be("2026-09-01");
        claim.AuthorizationDecision.ExpirationDate.Should().Be("2027-08-31");
        claim.AuthorizationDecision.Scope.Should().Be("provider components CMP-471 through CMP-474 only");
        claim.AuthorizationDecision.StatusAsStated.Should().BeNull();
        claim.AuthorizationDecision.Conditions.Should().Contain(q => q.Contains("POAM-471 remains open"));
        claim.AuthorizationDecision.Exclusions.Should().Contain(q => q.Contains("are excluded from this reference"));
        claim.Qualifications.Should().Contain(q => q.Contains("NOT A VALID ATO"));
        claim.Qualifications.Should().Contain(q => q.Contains("No signature"));
        claim.Qualifications.Should().Contain(q => q.Contains("must remain unconfirmed"));
        result.Coverage.AnalysisComplete.Should().BeFalse();
        AssertLabeledClaimSources(result);
    }

    [Theory]
    [InlineData("02/03/2026")]
    [InlineData("2026-02-30")]
    [InlineData("45111")]
    public async Task LabeledClaims_InvalidOrAmbiguousDatesRemainQualifiedRatherThanNormalized(string rawDate)
    {
        // Arrange
        var text = $"Authorization decision Reference ID: DEMO-42. Title: Fictional decision. " +
            $"Decision date: {rawDate}. Expiration date: {rawDate}. " +
            $"POAM-42 owner: Example Lead. Target: {rawDate}. Status: Open.";

        // Act
        var result = await Analyzer().AnalyzeAsync([Input("dates.txt", Bytes(text))]);

        // Assert
        var decision = result.Candidates.Single(c => c.Kind == CspPackageCandidateKind.AuthorizationDecisionClaim).Claim!;
        decision.AuthorizationDecision!.DecisionDate.Should().BeNull();
        decision.AuthorizationDecision.ExpirationDate.Should().BeNull();
        decision.Qualifications.Should().Contain(q => q.Contains(rawDate));
        var plan = result.Candidates.Single(c => c.Kind == CspPackageCandidateKind.PoamItem).Claim!;
        plan.PoamItem!.Milestones.Should().OnlyContain(m => m.DueDate == null);
        plan.Qualifications.Should().Contain(q => q.Contains(rawDate));
        AssertLabeledClaimSources(result);
    }

    [Fact]
    public async Task LabeledClaims_TextLineDeclarationsBindFieldsToOriginalSegments()
    {
        // Arrange
        var input = Input("lines.txt", Bytes("""
            Authorization decision
            Reference ID: FICTION-719
            Title: Fictional service decision
            Issuing authority: Fictional Review Office
            Decision date: 2026-09-01
            Scope: Shared test services only
            Signature
            No signature. Not a valid authorization.
            """));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var decision = result.Candidates.Single(c => c.Kind == CspPackageCandidateKind.AuthorizationDecisionClaim);
        decision.Claim!.AuthorizationDecision!.Reference.Should().Be("FICTION-719");
        decision.Claim.AuthorizationDecision.Authority.Should().Be("Fictional Review Office");
        decision.Claim.AuthorizationDecision.Scope.Should().Be("Shared test services only");
        decision.Claim.Qualifications.Should().Contain(q => q.Contains("No signature"));
        AssertLabeledClaimSources(result);
    }

    [Fact]
    public async Task LabeledClaims_ClosedSourceStatusDoesNotInventClosureEvidenceOrInventory()
    {
        // Arrange
        var input = Input("plan.txt", Bytes("POAM-901 Corrective action: Check alert route. " +
            "Owner: Example Lead. Status: Closed. Closure evidence: reviewed alert report. " +
            "Synthetic statement only; no actual verification was performed."));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        var candidate = result.Candidates.Should().ContainSingle().Which;
        candidate.Kind.Should().Be(CspPackageCandidateKind.PoamItem);
        candidate.Claim!.PoamItem!.StatusAsStated.Should().Be("Closed");
        candidate.Claim.PoamItem.RequiredClosureEvidence.Should().Equal("reviewed alert report");
        candidate.Claim.PoamItem.SubmittedEvidenceReferences.Should().BeEmpty();
        candidate.DependencyKeys.Should().BeEmpty();
        result.NeedsAttention.Should().BeTrue();
        AssertLabeledClaimSources(result);
    }

    [Theory]
    [InlineData("Fictional documents discuss findings, corrective action, authorization and boundary scope.")]
    [InlineData("Reference ID: memo-19. Title: Meeting notes. Owner: Example Lead.")]
    [InlineData("Authorization boundary Customer requested that CMP-901 be included next year; no boundary decision was made.")]
    [InlineData("Authorization boundary If the provider boundary includes CMP-901, customer review is needed.")]
    [InlineData("Authorization boundary If mission databases are outside the provider boundary, review applicability.")]
    [InlineData("Related finding:\nFIND-901\nRelated component: CMP-901. Observation: A referenced item, not a declaration.")]
    public async Task LabeledClaims_UnsupportedNarrativeDoesNotBecomeAClaimOrCompleteCoverage(string text)
    {
        // Arrange
        var input = Input("FIND-001_POAM-001_authorization-boundary.txt", Bytes(text));

        // Act
        var result = await Analyzer().AnalyzeAsync([input]);

        // Assert
        result.Candidates.Should().BeEmpty();
        result.Coverage.AnalysisComplete.Should().BeFalse();
        result.NeedsAttention.Should().BeTrue();
    }

    private static void AssertLabeledClaimSources(CspPackageAnalysisResult result)
    {
        foreach (var candidate in result.Candidates.Where(candidate => candidate.Claim is not null))
        {
            candidate.Claim!.FieldSources.Should().NotBeEmpty();
            foreach (var binding in candidate.Claim.FieldSources)
            {
                binding.CitationIndexes.Should().NotBeEmpty();
                foreach (var index in binding.CitationIndexes)
                    index.Should().BeInRange(0, candidate.Citations.Count - 1);
            }
            foreach (var citation in candidate.Citations)
                result.Segments.Single(segment => segment.Key == citation.SegmentKey).Text.Should().Contain(citation.Quote);
            foreach (var qualification in candidate.Claim.Qualifications)
                result.Segments.Should().Contain(segment => segment.Text.Contains(qualification, StringComparison.Ordinal));
        }
    }
}

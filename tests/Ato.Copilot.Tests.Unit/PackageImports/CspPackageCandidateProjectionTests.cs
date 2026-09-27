using System.Text.Json;
using System.Text.Json.Nodes;
using Ato.Copilot.Core.Interfaces.PackageImports;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.PackageImports.CspPackageServiceTests.Fixture;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed class CspPackageCandidateProjectionTests
{
    [Theory]
    [InlineData("current", "NeedsReview", 2)]
    [InlineData("current", "Reviewed", 2)]
    [InlineData("current", "Rejected", 2)]
    [InlineData("legacy-entry", "Reviewed", 1)]
    [InlineData("legacy-checkpoint", "Reviewed", 1)]
    [InlineData("explicit-legacy", "Reviewed", 1)]
    [InlineData("missing-checkpoint", "Reviewed", null)]
    [InlineData("mismatched-key", "Reviewed", null)]
    [InlineData("mismatched-citation", "Reviewed", null)]
    public async Task EditResponse_MatchesFreshRead_WithoutPromotingOrRewritingAnalysisProvenance(
        string provenance, string action, int? expectedProfile)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                var result = CspPackageProfile2Tests.Profile2(inputs);
                var drafts = Fixture.Analysis(inputs).Candidates;
                return Task.FromResult(result with { Candidates = drafts, Checkpoint = result.Checkpoint! with { Candidates = drafts } });
            });
        var receipt = await fixture.AnalyzeAsync();
        string? checkpointBefore;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var candidate = await db.CspPackageCandidates.SingleAsync(row => row.Type == "Component");
            var payload = JsonNode.Parse(candidate.PayloadJson)!.AsObject();
            payload.Remove("AnalysisProfileVersion");
            var package = await db.CspPackages.SingleAsync();
            var checkpoint = JsonSerializer.Deserialize<CspPackageAnalysisCheckpoint>(package.AnalysisCheckpointJson!)!;
            switch (provenance)
            {
                case "legacy-entry":
                    checkpoint = checkpoint with { Entries = checkpoint.Entries.Select(entry => entry with { AnalysisProfileVersion = 1 }).ToArray() };
                    break;
                case "legacy-checkpoint": checkpoint = checkpoint with { AnalysisProfileVersion = 1 }; break;
                case "explicit-legacy": payload["AnalysisProfileVersion"] = 1; break;
                case "mismatched-key": candidate.StableKey = "unmatched-legacy-key"; break;
                case "mismatched-citation": payload["Citations"]![0]!["Quote"] = "Unmatched retained quote"; break;
            }
            candidate.PayloadJson = payload.ToJsonString();
            package.AnalysisCheckpointJson = provenance == "missing-checkpoint" ? null : JsonSerializer.Serialize(checkpoint);
            await db.SaveChangesAsync();
            checkpointBefore = package.AnalysisCheckpointJson;
        }
        var before = (await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, "Component", null, default)).Items.Single();
        before.AnalysisProfileVersion.Should().Be(expectedProfile);

        // Act
        var mutation = await fixture.Service.EditAsync(receipt.PackageId, before.CandidateId,
            Fixture.Edit(before) with { Name = "Human-reviewed retained component", ReviewAction = action,
                Rationale = action == "Rejected" ? "Explicit source review rejection" : null }, "reviewer", default);
        var fresh = (await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, "Component", action, default)).Items.Single();

        // Assert
        mutation.Should().BeEquivalentTo(fresh);
        mutation.AnalysisProfileVersion.Should().Be(expectedProfile);
        mutation.ReviewState.Should().Be(action);
        mutation.Revision.Should().Be(before.Revision + 1);
        mutation.Name.Should().Be("Human-reviewed retained component");
        await using var verify = fixture.Factory.CreateDbContext();
        var stored = await verify.CspPackageCandidates.SingleAsync(row => row.Id == before.CandidateId);
        JsonSerializer.Deserialize<PackageCandidateResponse>(stored.PayloadJson)!.AnalysisProfileVersion
            .Should().Be(provenance == "explicit-legacy" ? 1 : null, "a response projection must not stamp inferred analysis metadata into retained payloads");
        (await verify.CspPackages.SingleAsync()).AnalysisCheckpointJson.Should().Be(checkpointBefore);
    }
}

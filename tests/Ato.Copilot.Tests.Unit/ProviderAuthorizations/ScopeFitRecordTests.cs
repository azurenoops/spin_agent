using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.ProviderAuthorizations.OfferingBoundaryOverviewTests.Fixture;
using Store = Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ScopeFitRecordTests
{
    [Fact]
    public async Task MissingRecord_ProjectsUnevaluated_NotInScope()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddAssignmentAsync();

        // Act
        var result = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        result.MissionSystems.Items.Should().ContainSingle();
        result.MissionSystems.Items[0].ScopeFit.Result.Should().Be(ScopeFitFacts.Unevaluated);
        result.MissionSystems.Items[0].ScopeFit.Result.Should().NotBe(ScopeFitFacts.InScope);
    }

    [Fact]
    public async Task OutOfScope_DoesNotWithdrawRecordedAuthorization_AndStoresNoSspBody()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var assignment = await fixture.AddAssignmentAsync();
        Guid recordId;
        await using (var db = fixture.Db())
        {
            var record = new ProviderAuthorizationRecord
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering, Revision = 1
            };
            var body = new CreateProviderDecisionRequest(7, fixture.SourceBoundary, [], "ProviderDecision",
                "Recorded parent", "Synthetic issuer", "Source decision", "2020-01-01", null, null,
                "NoExpiryStated", "Source scope", [], []);
            var revision = new ProviderAuthorizationRevision
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering, RecordId = record.Id,
                BoundaryRevisionId = fixture.SourceBoundary, Revision = 1,
                SnapshotJson = Store.Json(body), SnapshotHash = "hash-scope-fit",
                MetadataReviewState = "Recorded"
            };
            record.CurrentRevisionId = revision.Id;
            recordId = record.Id;
            db.AddRange(record, revision);
            await db.SaveChangesAsync();
        }
        var before = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Act
        await using (var db = fixture.Db())
        {
            db.Add(new ProviderScopeFitRecord
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering, AssignmentId = assignment.Id,
                Result = ScopeFitFacts.OutOfScope, DeclaredUse = "IL5 declared on IL4 offering",
                CitedOfferingVersion = "v2.1", CitedAdoptedRelease = "r2",
                Evaluator = "tester", EvaluatedAt = DateTimeOffset.UtcNow,
                Rationale = "Declared use exceeds recorded IL."
            });
            await db.SaveChangesAsync();
        }
        var after = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);
        var missions = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        after.AuthorizationFacts!.Validity.Should().Be(before.AuthorizationFacts!.Validity);
        after.Authorizations.Recorded.Should().Be(before.Authorizations.Recorded);
        after.Authorizations.Items.Should().OnlyContain(x => x.CurrentStanding != "Withdrawn");
        missions.MissionSystems.Items.Single(x => x.AssignmentId == assignment.Id).ScopeFit.Result
            .Should().Be(ScopeFitFacts.OutOfScope);
        missions.MissionSystems.Items.Single(x => x.AssignmentId == assignment.Id).ScopeFit.DeclaredUse
            .Should().Be("IL5 declared on IL4 offering");
        typeof(RecordScopeFitRequest).GetProperties().Select(p => p.Name)
            .Should().NotContain(name => name.Contains("Ssp", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Narrative", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Sar", StringComparison.OrdinalIgnoreCase));
        recordId.Should().NotBe(Guid.Empty);
    }
}

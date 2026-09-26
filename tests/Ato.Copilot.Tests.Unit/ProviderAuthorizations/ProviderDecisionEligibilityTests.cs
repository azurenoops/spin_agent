using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ProviderDecisionEligibilityTests
{
    private static ProviderAuthorizationRevision Revision(string? statement = "ATO",
        string recordKind = "ProviderDecision", string? issued = null, string? effective = "2020-01-01",
        string? expires = "2099-12-31", string expiryBasis = "DateStated")
    {
        var source = new CreateProviderDecisionRequest(1, Guid.NewGuid(), [], recordKind, "Synthetic source",
            "Synthetic issuer", statement, issued, effective, expires, expiryBasis, "Synthetic scope", [],
            [new(Guid.NewGuid(), Guid.NewGuid(), "source.txt", "p1", "Synthetic statement")]);
        var json = ProviderAuthorizationStore.Json(source);
        return new()
        {
            ProviderId = Guid.NewGuid(), OfferingId = Guid.NewGuid(), RecordId = Guid.NewGuid(),
            BoundaryRevisionId = source.BoundaryRevisionId, SnapshotJson = json,
            SnapshotHash = ProviderAuthorizationStore.Hash(json), MetadataReviewState = "Recorded",
            RecordedBy = "human", RecordedAt = DateTimeOffset.UtcNow
        };
    }

    [Theory]
    [InlineData("ATO")]
    [InlineData("IATO")]
    [InlineData("Authorized")]
    [InlineData("Approved")]
    [InlineData("Authorization to Operate")]
    [InlineData("Interim Authorization to Operate")]
    [InlineData("FedRAMP Authorized")]
    [InlineData(" ato ")]
    public void ExplicitSupportedStatement_IsEligibleWithoutChangingSource(string statement)
    {
        // Arrange
        var revision = Revision(statement);
        var snapshot = revision.SnapshotJson;

        // Act
        var blocker = ProviderDecisionEligibility.Evaluate(revision, []);

        // Assert
        blocker.Should().BeNull();
        revision.SnapshotJson.Should().Be(snapshot);
        ProviderAuthorizationStore.Read<CreateProviderDecisionRequest>(snapshot).DecisionAsStated.Should().Be(statement);
    }

    [Theory]
    [InlineData("DATO")]
    [InlineData("Denied")]
    [InlineData("Not Authorized")]
    [InlineData("ATO denied")]
    [InlineData("See attached letter")]
    [InlineData("Authorization pending review")]
    [InlineData("")]
    [InlineData(null)]
    public void DeniedOrUnrecognizedFreeText_ReturnsReviewBlockerDespiteCurrentStanding(string? statement)
    {
        // Arrange
        var revision = Revision(statement);

        // Act
        var blocker = ProviderDecisionEligibility.Evaluate(revision, []);

        // Assert
        ProviderAuthorizationService.Standing(revision, []).Should().Be("CurrentAsRecorded");
        blocker.Should().NotBeNull();
        blocker!.Code.Should().Be("DECISION_NOT_ELIGIBLE");
        blocker.Message.Should().Contain("source text").And.Contain("Review");
        blocker.TargetId.Should().Be(revision.Id.ToString());
    }

    [Theory]
    [InlineData("Expired")]
    [InlineData("FutureEffective")]
    [InlineData("FutureIssued")]
    [InlineData("UnknownExpiry")]
    [InlineData("Unconfirmed")]
    [InlineData("MissingReviewer")]
    [InlineData("MissingRecordedAt")]
    [InlineData("DigestMismatch")]
    [InlineData("Withdrawn")]
    [InlineData("Superseded")]
    public void IneligibleRecordedSource_ReturnsActionableBlocker(string state)
    {
        // Arrange
        var revision = Revision(issued: state == "FutureIssued" ? "2099-01-01" : null,
            effective: state == "FutureEffective" ? "2099-01-01" : "2020-01-01",
            expires: state == "Expired" ? "2020-01-02" : state == "UnknownExpiry" ? null : "2099-12-31",
            expiryBasis: state == "UnknownExpiry" ? "NotRecorded" : "DateStated");
        if (state == "Unconfirmed") revision.MetadataReviewState = "Unconfirmed";
        if (state == "MissingReviewer") revision.RecordedBy = " ";
        if (state == "MissingRecordedAt") revision.RecordedAt = null;
        if (state == "DigestMismatch") revision.SnapshotHash = new string('0', 64);
        var events = new List<ProviderAuthorizationLifecycleEvent>();
        if (state is "Withdrawn" or "Superseded")
            events.Add(new()
            {
                ProviderId = revision.ProviderId, OfferingId = revision.OfferingId, RecordId = revision.RecordId,
                AuthorizationRevisionId = revision.Id, Kind = state, EffectiveOn = "2020-01-01"
            });

        // Act
        var blocker = ProviderDecisionEligibility.Evaluate(revision, events);

        // Assert
        blocker.Should().NotBeNull();
        blocker!.Code.Should().Be("DECISION_NOT_ELIGIBLE");
        blocker.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void InheritedReference_RequiresExplicitSeparateKind_AndNeverSubstitutesForProviderDecision()
    {
        // Arrange
        var reference = Revision(recordKind: "InheritedMicrosoftReference");
        var providerDecision = Revision();

        // Act
        var substitute = ProviderDecisionEligibility.Evaluate(reference, []);
        var supportingReference = ProviderDecisionEligibility.Evaluate(reference, [], "InheritedMicrosoftReference");
        var wrongReplacement = ProviderDecisionEligibility.Evaluate(providerDecision, [], "InheritedMicrosoftReference");

        // Assert
        substitute!.Code.Should().Be("PROVIDER_DECISION_REQUIRED");
        supportingReference.Should().BeNull();
        wrongReplacement!.Code.Should().Be("DECISION_NOT_ELIGIBLE");
    }

    [Fact]
    public void ExplicitNoExpiryStated_IsNotTreatedAsUnknownExpiry()
    {
        // Arrange
        var revision = Revision(expires: null, expiryBasis: "NoExpiryStated");

        // Act
        var blocker = ProviderDecisionEligibility.Evaluate(revision, []);

        // Assert
        blocker.Should().BeNull();
    }
}

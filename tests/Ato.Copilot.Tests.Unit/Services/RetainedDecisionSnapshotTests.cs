using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class RetainedDecisionSnapshotTests
{
    [Fact]
    public void ExternalDecision_RetainsActualSourceAndRecorder_WithoutInventingAssessmentMetrics()
    {
        // Arrange
        var decision = new AuthorizationDecision
        {
            Id = "decision-a", RegisteredSystemId = "system-a", DecisionType = AuthorizationDecisionType.Ato,
            DecisionDate = new DateTime(2026, 9, 1), IssuedBy = "recording-ao", IssuedByName = "Recording AO",
            ExternalIssuingAuthority = "DEMO external authority", SourceEvidenceId = "evidence-a",
            SourceEvidenceHash = "source-hash", BaselinePackageId = "baseline-a", BaselinePackageHash = "baseline-hash",
            RecordedBy = "recording-ao", RecordedAt = new DateTime(2026, 9, 2)
        };
        // Act
        var snapshot = AuthorizationPackageContextOptions.DecisionSnapshot(decision);
        // Assert
        snapshot.GetProperty("externalIssuingAuthority").GetString().Should().Be("DEMO external authority");
        snapshot.GetProperty("sourceEvidenceHash").GetString().Should().Be("source-hash");
        snapshot.GetProperty("baselinePackageHash").GetString().Should().Be("baseline-hash");
        snapshot.GetProperty("recordedBy").GetString().Should().Be("recording-ao");
        snapshot.GetProperty("complianceScoreAtDecision").ValueKind.Should().Be(JsonValueKind.Null);
        snapshot.GetProperty("findingsAtDecision").ValueKind.Should().Be(JsonValueKind.Null);
    }
}

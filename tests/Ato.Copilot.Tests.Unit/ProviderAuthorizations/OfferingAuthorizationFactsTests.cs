using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class OfferingAuthorizationFactsTests
{
    [Fact]
    public void NoRecordedDecision_IsNotRecorded_NotUnauthorized()
    {
        // Arrange
        // Act
        var facts = OfferingAuthorizationFacts.Project([], packageIncomplete: true, monitoringHealth: null);

        // Assert
        facts.Validity.Should().Be("NotRecorded");
        facts.Validity.Should().NotBe("Unauthorized");
        facts.PackageReadiness.Should().Be("Incomplete");
        facts.MonitoringHealth.Should().Be("Unavailable");
    }

    [Fact]
    public void EffectiveDecision_IncompletePackage_StaleMonitoring_DisagreeAndDoNotCollapse()
    {
        // Arrange
        // Act
        var facts = OfferingAuthorizationFacts.Project(
            ["CurrentAsRecorded"], packageIncomplete: true, monitoringHealth: "Stale");

        // Assert
        facts.Validity.Should().Be("Effective");
        facts.PackageReadiness.Should().Be("Incomplete");
        facts.MonitoringHealth.Should().Be("Stale");
        facts.Validity.Should().NotBe(facts.PackageReadiness);
        facts.Validity.Should().NotBe(facts.MonitoringHealth);
    }

    [Fact]
    public void StaleMonitoring_DoesNotFlipValidity()
    {
        // Arrange
        // Act
        var facts = OfferingAuthorizationFacts.Project(
            ["CurrentAsRecorded"], packageIncomplete: false, monitoringHealth: "Stale");

        // Assert
        facts.Validity.Should().Be("Effective");
        facts.PackageReadiness.Should().Be("Ready");
        facts.MonitoringHealth.Should().Be("Stale");
    }

    [Fact]
    public void WithdrawnStanding_IsWithdrawn_EvenIfPackageReadyAndMonitoringCurrent()
    {
        // Arrange
        // Act
        var facts = OfferingAuthorizationFacts.Project(
            ["Withdrawn"], packageIncomplete: false, monitoringHealth: "Current");

        // Assert
        facts.Validity.Should().Be("Withdrawn");
        facts.PackageReadiness.Should().Be("Ready");
        facts.MonitoringHealth.Should().Be("Current");
    }

    [Fact]
    public void ExpiredStanding_IsExpired()
    {
        // Arrange
        // Act
        var facts = OfferingAuthorizationFacts.Project(
            ["Expired"], packageIncomplete: false, monitoringHealth: "Current");

        // Assert
        facts.Validity.Should().Be("Expired");
    }

    [Fact]
    public void CurrentAsRecordedWithConditions_IsConditional()
    {
        // Arrange
        // Act
        var facts = OfferingAuthorizationFacts.Project(
            ["CurrentAsRecorded"], packageIncomplete: false, monitoringHealth: "Current", hasRecordedConditions: true);

        // Assert
        facts.Validity.Should().Be("Conditional");
        facts.PackageReadiness.Should().Be("Ready");
        facts.MonitoringHealth.Should().Be("Current");
    }

    [Fact]
    public void EmptyMonitoringSources_AreNotRecorded_NotHealthyZero()
    {
        // Arrange
        // Act
        var facts = OfferingAuthorizationFacts.Project(
            ["CurrentAsRecorded"], packageIncomplete: false, monitoringHealth: "NotRecorded");

        // Assert
        facts.MonitoringHealth.Should().Be("NotRecorded");
        facts.MonitoringHealth.Should().NotBe("Current");
        facts.Validity.Should().Be("Effective");
    }

    [Fact]
    public void MonitoringHealth_UsesOnlyOfferingOwnedSignals()
    {
        // Arrange
        var health = OfferingAuthorizationFacts.MonitoringFromSourceHealth(["Available", "Unreviewed"]);

        // Act
        var facts = OfferingAuthorizationFacts.Project(
            ["CurrentAsRecorded"], packageIncomplete: false, monitoringHealth: health);

        // Assert
        health.Should().Be("Stale");
        facts.MonitoringHealth.Should().Be("Stale");
        facts.Validity.Should().Be("Effective");
    }

    [Theory]
    [InlineData("Available", "Current")]
    [InlineData("Missing", "Stale")]
    [InlineData("Unreviewed", "Stale")]
    public void SingleSourceHealth_MapsWithoutInventingMoSubscriptionTelemetry(string sourceHealth, string expected)
    {
        // Arrange
        // Act
        var health = OfferingAuthorizationFacts.MonitoringFromSourceHealth([sourceHealth]);

        // Assert
        health.Should().Be(expected);
        health.Should().NotBe("MoSubscription");
    }
}

using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public class ScopedMonitoringConditionTests
{
    [Theory]
    [InlineData("{\"field\":\"Severity\",\"operator\":\"Equals\",\"value\":\"High\"}", AlertSeverity.Low, false)]
    [InlineData("{\"field\":\"Severity\",\"operator\":\"Equals\",\"value\":\"High\"}", AlertSeverity.High, true)]
    [InlineData("{\"field\":\"MadeUp\",\"operator\":\"Equals\",\"value\":\"High\"}", AlertSeverity.High, false)]
    [InlineData("not json", AlertSeverity.High, false)]
    public void Conditions_are_executable_and_fail_closed(string condition, AlertSeverity severity, bool expected)
    {
        // Arrange
        var alert = new ComplianceAlert { Severity = severity };
        // Act
        var matches = MonitoringConditionEvaluator.Matches(condition, alert);
        // Assert
        matches.Should().Be(expected);
    }

    [Fact]
    public void Changed_property_requires_the_observed_value()
    {
        // Arrange
        var alert = new ComplianceAlert { ChangeDetails = "{\"property\":\"publicAccess\",\"oldValue\":false,\"newValue\":true}" };
        // Act
        var matches = MonitoringConditionEvaluator.Matches(
            "{\"field\":\"Change.publicAccess\",\"operator\":\"Becomes\",\"value\":\"true\"}", alert);
        // Assert
        matches.Should().BeTrue();
    }

    [Fact]
    public void Existing_watch_baseline_current_payload_is_executable()
    {
        // Arrange
        var alert = new ComplianceAlert { ControlId = "SC-7",
            ChangeDetails = "{\"baseline\":\"[{\\\"ControlId\\\":\\\"SC-7\\\",\\\"Status\\\":0}]\",\"current\":\"[{\\\"ControlId\\\":\\\"SC-7\\\",\\\"Status\\\":1}]\"}" };
        // Act
        var matches = MonitoringConditionEvaluator.Matches(
            "{\"field\":\"Change.Status\",\"operator\":\"Becomes\",\"value\":\"1\"}", alert);
        // Assert
        matches.Should().BeTrue();
    }

    [Fact]
    public void Next_run_in_future_does_not_hide_stale_collection()
    {
        // Arrange
        var configuration = new MonitoringConfiguration { IsEnabled = true, Frequency = MonitoringFrequency.Hourly,
            LastRunAt = DateTimeOffset.UtcNow.AddDays(-1), NextRunAt = DateTimeOffset.UtcNow.AddHours(1) };
        // Act
        var health = ScopedMonitoringService.CollectionHealth(configuration);
        // Assert
        health.Should().Be("Stale");
    }
}

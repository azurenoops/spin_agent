using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for EmassBridgeService transform logic (Feature 076 — T014).
/// </summary>
public class EmassBridgeServiceTests
{
    [Theory]
    [InlineData("ac-1",   "AC-1")]
    [InlineData("sc-7",   "SC-7")]
    [InlineData("ac-2.1", "AC-2.1")]
    [InlineData("cm-6",   "CM-6")]
    public void ToEmassControlId_UppercasesCorrectly(string oscal, string expected)
        => EmassBridgeService.ToEmassControlId(oscal).Should().Be(expected);

    [Theory]
    [InlineData("AC-1",   "ac-1")]
    [InlineData("SC-7.3", "sc-7.3")]
    public void FromEmassControlId_LowercasesCorrectly(string emass, string expected)
        => EmassBridgeService.FromEmassControlId(emass).Should().Be(expected);

    [Theory]
    [InlineData(ImplementationStatus.Implemented,          "Implemented")]
    [InlineData(ImplementationStatus.PartiallyImplemented, "Planned")]
    [InlineData(ImplementationStatus.NotApplicable,        "Not Applicable")]
    [InlineData(ImplementationStatus.Planned,              "Planned")]
    public void MapStatusToEmass_MapsCorrectly(ImplementationStatus status, string expected)
        => EmassBridgeService.MapStatusToEmass(status).Should().Be(expected);

    [Theory]
    [InlineData("Implemented",   ImplementationStatus.Implemented)]
    [InlineData("Not Applicable",ImplementationStatus.NotApplicable)]
    [InlineData("Inherited",     ImplementationStatus.Implemented)]
    [InlineData("Planned",       ImplementationStatus.Planned)]
    [InlineData(null,            ImplementationStatus.Planned)]
    public void MapEmassStatusToOscal_MapsCorrectly(string? emass, ImplementationStatus expected)
        => EmassBridgeService.MapEmassStatusToOscal(emass).Should().Be(expected);
}

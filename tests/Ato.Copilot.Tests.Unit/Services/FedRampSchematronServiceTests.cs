using Ato.Copilot.Agents.Compliance.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for FedRampSchematronService advisory validation (Feature 076 — T007).
/// </summary>
public class FedRampSchematronServiceTests
{
    private static readonly FedRampSchematronService Sut = new(NullLogger<FedRampSchematronService>.Instance);

    [Fact]
    public async Task ValidateAsync_InvalidJson_ReturnsHighViolation()
    {
        var result = await Sut.ValidateAsync("{ bad json", "ssp");
        result.IsCompliant.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Severity == "high");
        result.AdvisoryOnly.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WrongOscalVersion_ReturnsMediumViolation()
    {
        var json = @"{""system-security-plan"":{""uuid"":""abc"",""metadata"":{""title"":""T"",""last-modified"":""2026-01-01T00:00:00Z"",""version"":""1.0"",""oscal-version"":""1.0.4""}}}";
        var result = await Sut.ValidateAsync(json, "ssp");
        result.Violations.Should().Contain(v => v.RuleId == "FEDRAMP-META-001");
    }

    [Fact]
    public async Task ValidateAsync_PoamMissingSystemId_ReturnsHighViolation()
    {
        var json = @"{""plan-of-action-and-milestones"":{""uuid"":""abc"",""metadata"":{""title"":""T"",""last-modified"":""2026-01-01T00:00:00Z"",""version"":""1.0"",""oscal-version"":""1.1.2""},""poam-items"":[]}}";
        var result = await Sut.ValidateAsync(json, "poam");
        result.Violations.Should().Contain(v => v.RuleId == "FEDRAMP-POAM-001" && v.Severity == "high");
    }

    [Fact]
    public async Task ValidateAsync_EmptyDocument_IsAdvisoryOnly()
    {
        var result = await Sut.ValidateAsync("{}", "ssp");
        result.AdvisoryOnly.Should().BeTrue();
        // Should not throw even for empty/invalid document
    }

    [Fact]
    public async Task ValidateAsync_ValidPoamWithSystemId_NoHighViolations()
    {
        var json = @"{""plan-of-action-and-milestones"":{""uuid"":""abc"",""metadata"":{""title"":""T"",""last-modified"":""2026-01-01T00:00:00Z"",""version"":""1.0"",""oscal-version"":""1.1.2""},""system-id"":{""id"":""F-001""},""poam-items"":[]}}";
        var result = await Sut.ValidateAsync(json, "poam");
        result.Violations.Should().NotContain(v => v.Severity == "high");
    }
}

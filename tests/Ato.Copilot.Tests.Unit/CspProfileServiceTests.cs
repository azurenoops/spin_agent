using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ato.Copilot.Mcp.Services;

namespace Ato.Copilot.Tests.Unit;

/// <summary>
/// Feature 043 – T039: Unit tests for CspProfileService.
/// Tests profile matching, conflict resolution, and unmatched control handling.
/// </summary>
public class CspProfileServiceTests
{
    private readonly CspProfileService _service;

    public CspProfileServiceTests()
    {
        // Point the service at the actual seed-data directory so profiles load at startup
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "src", "Ato.Copilot.Mcp"));
        _service = new CspProfileService(NullLogger<CspProfileService>.Instance, env.Object);
    }

    [Fact]
    public void GetProfiles_LoadsSeedData()
    {
        var profiles = _service.GetProfiles();
        profiles.Should().NotBeEmpty();
        profiles[0].ProfileId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetProfile_ById_ReturnsProfile()
    {
        var profiles = _service.GetProfiles();
        var profile = _service.GetProfile(profiles[0].ProfileId);
        profile.Should().NotBeNull();
        profile!.Controls.Should().NotBeEmpty();
    }

    [Fact]
    public void GetProfile_Unknown_ReturnsNull()
    {
        _service.GetProfile("doesnt-exist").Should().BeNull();
    }

    [Fact]
    public void MatchProfile_AllControlsInBaseline_FullMatch()
    {
        var profile = CreateTestProfile(["AC-1", "AC-2", "AU-1"]);
        var baselineIds = new List<string> { "AC-1", "AC-2", "AU-1", "AU-2" };
        var existing = new Dictionary<string, string>();

        var result = _service.MatchProfile(profile, baselineIds, existing, "skip");

        result.MatchedControls.Should().Be(3);
        result.UnmatchedControls.Should().Be(0);
        result.Conflicts.Should().Be(0);
        result.WillSkipExisting.Should().Be(0);
    }

    [Fact]
    public void MatchProfile_UnmatchedControls_AreCountedSilently()
    {
        var profile = CreateTestProfile(["AC-1", "ZZ-99"]);
        var baselineIds = new List<string> { "AC-1", "AC-2" };

        var result = _service.MatchProfile(profile, baselineIds, new Dictionary<string, string>(), "skip");

        result.MatchedControls.Should().Be(1);
        result.UnmatchedControls.Should().Be(1);
    }

    [Fact]
    public void MatchProfile_SkipConflicts_SkipsExisting()
    {
        var profile = CreateTestProfile(["AC-1", "AC-2"]);
        var existing = new Dictionary<string, string> { { "AC-1", "Customer" } };

        var result = _service.MatchProfile(profile, ["AC-1", "AC-2"], existing, "skip");

        result.Conflicts.Should().Be(1);
        result.WillSkipExisting.Should().Be(1);
        result.MappingsToApply.Should().HaveCount(1);
        result.MappingsToApply[0].ControlId.Should().Be("AC-2");
    }

    [Fact]
    public void MatchProfile_OverwriteConflicts_IncludesExisting()
    {
        var profile = CreateTestProfile(["AC-1", "AC-2"]);
        var existing = new Dictionary<string, string> { { "AC-1", "Customer" } };

        var result = _service.MatchProfile(profile, ["AC-1", "AC-2"], existing, "overwrite");

        result.Conflicts.Should().Be(1);
        result.WillSkipExisting.Should().Be(0);
        result.MappingsToApply.Should().HaveCount(2);
    }

    [Fact]
    public void MatchProfile_InheritanceCounts_AreCorrect()
    {
        var profile = new CspProfileService.CspProfile
        {
            ProfileId = "test", Name = "Test", Provider = "Test CSP",
            Controls =
            [
                new() { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure" },
                new() { ControlId = "AC-2", InheritanceType = "Shared", Provider = "Azure", CustomerResponsibility = "Manage" },
                new() { ControlId = "AU-1", InheritanceType = "Customer", CustomerResponsibility = "Full" },
            ]
        };

        var result = _service.MatchProfile(profile, ["AC-1", "AC-2", "AU-1"], new Dictionary<string, string>(), "skip");

        result.WillSetInherited.Should().Be(1);
        result.WillSetShared.Should().Be(1);
        result.WillSetCustomer.Should().Be(1);
    }

    private static CspProfileService.CspProfile CreateTestProfile(string[] controlIds)
    {
        return new CspProfileService.CspProfile
        {
            ProfileId = "test-profile",
            Name = "Test Profile",
            Provider = "Test CSP",
            Controls = controlIds.Select(id => new CspProfileService.ProfileControlMapping
            {
                ControlId = id,
                InheritanceType = "Inherited",
                Provider = "Test CSP"
            }).ToList()
        };
    }
}

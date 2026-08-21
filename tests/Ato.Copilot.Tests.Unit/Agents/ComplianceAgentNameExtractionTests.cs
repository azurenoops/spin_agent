// ─────────────────────────────────────────────────────────────────────────────
// fix(#617) — ComplianceAgent name extraction regression tests
// fix(#568) — GUID token scanning helper tests
// ─────────────────────────────────────────────────────────────────────────────

using System.Reflection;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Agents;

namespace Ato.Copilot.Tests.Unit.Agents;

public class ComplianceAgentNameExtractionTests
{
    // Reflectively invoke the private static ExtractSystemNameFromRegistrationMessage
    private static string? Extract(string message)
    {
        var method = typeof(ComplianceAgent).GetMethod(
            "ExtractSystemNameFromRegistrationMessage",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("reflection target must exist");
        return (string?)method!.Invoke(null, new object[] { message });
    }

    // Helper mirrors the GUID-token-scan logic from fix(#568) so we can unit-test the
    // candidate extraction independently of the DB.  The actual method requires a live
    // DbContextFactory, so we only test the pure tokenizing step here.
    private static IEnumerable<string> ExtractGuidCandidates(string message)
    {
        foreach (var token in message.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = token.Trim('.', ',', ';', ':', '"', '\'', '(', ')');
            if (Guid.TryParse(candidate, out _))
                yield return candidate;
        }
    }

    // ── fix(#617): name extraction ───────────────────────────────────────────

    [Theory]
    [InlineData("Register a new system with name Zephyr Test Platform, type MajorApplication", "Zephyr Test Platform")]
    [InlineData("register a new system named Eagle Eye, type Enclave", "Eagle Eye")]
    [InlineData("register a system called ACME Portal type MajorApplication", "ACME Portal")]
    [InlineData("Register a new system with name My Cool System, type Enclave, acronym MCS", "My Cool System")]
    [InlineData("register a new system named SkyNet", "SkyNet")]
    [InlineData("register system named Prometheus type MajorApplication", "Prometheus")]
    [InlineData("register a system named Alpha Bravo; type MajorApplication", "Alpha Bravo")]
    public void Extract_ShouldCaptureName_ForRegistrationMessages(string message, string expected)
    {
        Extract(message).Should().Be(expected);
    }

    [Theory]
    [InlineData("list systems")]
    [InlineData("get system details for Foo")]
    public void Extract_ShouldReturnNull_WhenNoNameKeywordPresent(string message)
    {
        Extract(message).Should().BeNull();
    }

    // ── fix(#568): GUID token scanning ──────────────────────────────────────

    [Theory]
    [InlineData("get status for 3fa85f64-5717-4562-b3fc-2c963f66afa6", "3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    [InlineData("show system (3fa85f64-5717-4562-b3fc-2c963f66afa6)", "3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    [InlineData("system id: 3fa85f64-5717-4562-b3fc-2c963f66afa6.", "3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    public void GuidTokenScan_ShouldExtractGuid_FromVariousDelimiters(string message, string expectedGuid)
    {
        var candidates = ExtractGuidCandidates(message).ToList();
        candidates.Should().Contain(expectedGuid);
    }

    [Fact]
    public void GuidTokenScan_ShouldReturnEmpty_WhenNoGuidPresent()
    {
        var candidates = ExtractGuidCandidates("get status for Eagle Eye system").ToList();
        candidates.Should().BeEmpty();
    }
}

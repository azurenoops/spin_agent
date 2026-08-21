// ─────────────────────────────────────────────────────────────────────────────
// fix(#617) — ComplianceAgent name extraction regression tests
// Verifies that ExtractSystemNameFromRegistrationMessage handles the real-world
// patterns reported in #617 (comma-delimited "name X, type Y" inputs).
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

    [Theory]
    // Quoted values (handled by ExtractQuotedValue, but double-check Extract doesn't break)
    [InlineData("Register a new system with name Zephyr Test Platform, type MajorApplication", "Zephyr Test Platform")]
    [InlineData("register a new system named Eagle Eye, type Enclave", "Eagle Eye")]
    [InlineData("register a system called ACME Portal type MajorApplication", "ACME Portal")]
    // "with name X" pattern (fix #617)
    [InlineData("Register a new system with name My Cool System, type Enclave, acronym MCS", "My Cool System")]
    // Trailing end-of-string (no type suffix)
    [InlineData("register a new system named SkyNet", "SkyNet")]
    // Single word names
    [InlineData("register system named Prometheus type MajorApplication", "Prometheus")]
    // Multi-word before semicolon
    [InlineData("register a system named Alpha Bravo; type MajorApplication", "Alpha Bravo")]
    public void Extract_ShouldCaptureName_ForRegistrationMessages(string message, string expected)
    {
        var result = Extract(message);
        result.Should().Be(expected);
    }

    [Theory]
    // Messages that should NOT match (no "named/name/called" keyword)
    [InlineData("list systems")]
    [InlineData("get system details for Foo")]
    public void Extract_ShouldReturnNull_WhenNoNameKeywordPresent(string message)
    {
        var result = Extract(message);
        result.Should().BeNull();
    }
}

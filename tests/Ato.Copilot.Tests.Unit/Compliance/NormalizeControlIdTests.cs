using Ato.Copilot.Agents.Compliance.Services;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

/// <summary>
/// Feature 044 — contract for
/// <see cref="FrameworkImportService.NormalizeControlId(string)"/>. Verifies the
/// OSCAL Rev 5 single-enhancement dot form maps to parenthetical display, and
/// that multi-segment NIST 800-171 Rev 3 ids are left structurally intact (no
/// unbalanced parenthesis).
/// </summary>
public class NormalizeControlIdTests
{
    [Theory]
    // OSCAL Rev 5 enhancement: single numeric segment → parenthetical.
    [InlineData("ac-2", "AC-2")]
    [InlineData("ac-2.1", "AC-2(1)")]
    // Enhancement leading zeros are stripped ("01" → "1"); the base segment is
    // left as-is (matches the established import behavior).
    [InlineData("ac-2.01", "AC-2(1)")]
    // NIST 800-171 Rev 3: multi-segment ids are NOT enhancements — leave them
    // intact rather than emitting an unbalanced paren ("SP_800_171_03(01.01").
    [InlineData("sp_800_171_03.01.01", "SP_800_171_03.01.01")]
    [InlineData("sp_800_171_03.01", "SP_800_171_03(1)")]
    public void NormalizeControlId_ProducesExpectedDisplayForm(string input, string expected)
    {
        // Act
        var result = FrameworkImportService.NormalizeControlId(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("sp_800_171_03.01.01")]
    [InlineData("sp_800_171_03.03.05")]
    public void NormalizeControlId_NeverEmitsUnbalancedParenthesis(string input)
    {
        // Act
        var result = FrameworkImportService.NormalizeControlId(input);

        // Assert
        var openCount = result.Count(c => c == '(');
        var closeCount = result.Count(c => c == ')');
        openCount.Should().Be(closeCount);
    }
}

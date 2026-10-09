using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ScopeFitFactsTests
{
    [Theory]
    [InlineData(null, "Unevaluated")]
    [InlineData("", "Unevaluated")]
    [InlineData("AcceptForPublication", "Unevaluated")]
    [InlineData("InScope", "InScope")]
    [InlineData("Exception", "Exception")]
    [InlineData("OutOfScope", "OutOfScope")]
    [InlineData("Unevaluated", "Unevaluated")]
    public void MissingOrUnknown_DoesNotDefaultToInScope(string? raw, string expected)
    {
        // Arrange
        // Act
        var result = ScopeFitFacts.Normalize(raw);

        // Assert
        result.Should().Be(expected);
        if (expected != "InScope") result.Should().NotBe("InScope");
    }

    [Fact]
    public void Require_RejectsImpactDispositions()
    {
        // Arrange
        // Act
        var error = Record.Exception(() => ScopeFitFacts.Require("AcceptForPublication"));

        // Assert
        error.Should().BeOfType<ArgumentException>();
    }
}

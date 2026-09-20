using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public sealed class ControlImplementationNarrativeTests
{
    [Fact]
    public void HasCanonicalNarrative_LegacyOnly_ReturnsFalse()
    {
        // Arrange
        var implementation = new ControlImplementation { Narrative = "Legacy-only content" };

        // Act
        var result = implementation.HasCanonicalNarrative();

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("Policy content", null)]
    [InlineData(null, "Technical content")]
    public void HasCanonicalNarrative_EitherCanonicalHalf_ReturnsTrue(
        string? policyNarrative,
        string? technicalNarrative)
    {
        // Arrange
        var implementation = new ControlImplementation
        {
            PolicyNarrative = policyNarrative,
            TechnicalNarrative = technicalNarrative,
        };

        // Act
        var result = implementation.HasCanonicalNarrative();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void SetCombinedNarrative_MirrorsTechnicalAndPreservesPolicy()
    {
        // Arrange
        var implementation = new ControlImplementation
        {
            PolicyNarrative = "Authored policy",
            MigratedFromLegacy = true,
        };

        // Act
        implementation.SetCombinedNarrative("Generated technical content");

        // Assert
        implementation.Narrative.Should().Be("Generated technical content");
        implementation.TechnicalNarrative.Should().Be("Generated technical content");
        implementation.PolicyNarrative.Should().Be("Authored policy");
        implementation.MigratedFromLegacy.Should().BeFalse();
    }
}
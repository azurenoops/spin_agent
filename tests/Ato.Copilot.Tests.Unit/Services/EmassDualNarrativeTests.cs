using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public class EmassDualNarrativeTests
{
    [Fact]
    public void BuildImplementationNarrative_WithBothHalves_ConcatenatesInCanonicalOrder()
    {
        // Arrange
        var implementation = new ControlImplementation
        {
            PolicyNarrative = "Policy narrative",
            TechnicalNarrative = "Technical narrative",
            Narrative = "Legacy narrative"
        };

        // Act
        var result = EmassExportService.BuildImplementationNarrative(implementation);

        // Assert
        result.Should().Be("Policy narrative\n\nTechnical narrative");
    }
}
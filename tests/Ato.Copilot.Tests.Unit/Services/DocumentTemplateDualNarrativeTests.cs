using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public class DocumentTemplateDualNarrativeTests
{
    [Fact]
    public void BuildControlNarratives_DoesNotSilentlyDropControlsAfterFifty()
    {
        // Arrange
        var implementations = Enumerable.Range(1, 51).Select(number => new ControlImplementation
        {
            ControlId = $"DEMO-{number:00}", PolicyNarrative = $"Recorded policy {number}",
            TechnicalNarrative = $"Recorded implementation {number}"
        });

        // Act
        var result = DocumentTemplateService.BuildControlNarratives(implementations);

        // Assert
        result.Should().Contain("Recorded implementation 51");
    }

    [Fact]
    public void BuildControlNarratives_LabelsBothHalvesInCanonicalOrder()
    {
        // Arrange
        var implementations = new[]
        {
            new ControlImplementation
            {
                ControlId = "AC-2",
                PolicyNarrative = "Annual account management policy.",
                TechnicalNarrative = null
            }
        };

        // Act
        var result = DocumentTemplateService.BuildControlNarratives(implementations);

        // Assert
        result.Should().Be(
            "AC-2\n" +
            "Implementation Statement (Policy):\nAnnual account management policy.\n\n" +
            "Implementation Statement (Technical):\n[Not Authored]");
    }
}
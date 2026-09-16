using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public class PolicyTechnicalNarrativeModelTests
{
    [Fact]
    public void Model_MapsDualNarrativesAndEvidenceClassification()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"PolicyTechnicalNarrativeModel_{Guid.NewGuid()}")
            .Options;
        using var context = new AtoCopilotContext(options);
        var controlImplementation = context.Model.FindEntityType(typeof(ControlImplementation));
        var evidenceArtifact = context.Model.FindEntityType(typeof(EvidenceArtifact));

        // Act
        var policyNarrative = controlImplementation?.FindProperty("PolicyNarrative");
        var technicalNarrative = controlImplementation?.FindProperty("TechnicalNarrative");
        var migratedFromLegacy = controlImplementation?.FindProperty("MigratedFromLegacy");
        var narrativeType = evidenceArtifact?.FindProperty("NarrativeType");
        var autoTagRationale = evidenceArtifact?.FindProperty("AutoTagRationale");
        var manuallyTaggedBy = evidenceArtifact?.FindProperty("ManuallyTaggedBy");

        // Assert
        policyNarrative.Should().NotBeNull();
        policyNarrative!.GetMaxLength().Should().Be(8000);
        policyNarrative.IsNullable.Should().BeTrue();
        technicalNarrative.Should().NotBeNull();
        technicalNarrative!.GetMaxLength().Should().Be(8000);
        technicalNarrative.IsNullable.Should().BeTrue();
        migratedFromLegacy.Should().NotBeNull();
        narrativeType.Should().NotBeNull();
        narrativeType!.ClrType.Name.Should().Be("EvidenceNarrativeType");
        autoTagRationale.Should().NotBeNull();
        autoTagRationale!.GetMaxLength().Should().Be(500);
        manuallyTaggedBy.Should().NotBeNull();
        manuallyTaggedBy!.GetMaxLength().Should().Be(200);
    }
}
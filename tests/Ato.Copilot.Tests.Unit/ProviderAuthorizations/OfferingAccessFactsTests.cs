using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class OfferingAccessFactsTests
{
    [Fact]
    public void IattDev_IsInScope_AndIsNotAnAuthorization()
    {
        // Arrange
        // Act
        var estimate = OfferingAccessFacts.Estimate("IATT", "Dev", "IL4", "CUI as published", "None");

        // Assert
        estimate.Result.Should().Be(ScopeFitFacts.InScope);
        estimate.Rationale.Should().NotContain("ATO");
    }

    [Fact]
    public void IattTest_IsException_NotCloserToAto()
    {
        // Arrange
        // Act
        var estimate = OfferingAccessFacts.Estimate("IATT", "Test", "IL4", "CUI as published", "None");

        // Assert
        estimate.Result.Should().Be(ScopeFitFacts.Exception);
        estimate.Rationale.Should().Contain("not closer to ATO");
    }

    [Fact]
    public void IattProd_IsOutOfScope()
    {
        // Arrange
        // Act
        var estimate = OfferingAccessFacts.Estimate("IATT", "Prod", "IL4", "CUI as published", "None");

        // Assert
        estimate.Result.Should().Be(ScopeFitFacts.OutOfScope);
    }

    [Fact]
    public void Il5_IsOutOfScope()
    {
        // Arrange
        // Act
        var estimate = OfferingAccessFacts.Estimate("IATT", "Dev", "IL5", "CUI as published", "None");

        // Assert
        estimate.Result.Should().Be(ScopeFitFacts.OutOfScope);
    }
}

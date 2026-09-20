using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class OscalValidationServiceOfficialSchemaTests
{
    [Theory]
    [InlineData("ssp")]
    [InlineData("poam")]
    [InlineData("assessment-results")]
    [InlineData("assessment-plan")]
    public async Task ValidateAsync_WithOfficialDraft7Schema_ResolvesInternalFragmentIdentifiers(
        string modelType)
    {
        // Arrange
        var sut = new OscalSchemaValidationService(
            Mock.Of<IEmassExportService>(),
            Mock.Of<IOscalSapExportService>(),
            Mock.Of<ILogger<OscalSchemaValidationService>>());

        // Act
        var result = await sut.ValidateAsync(
            "{}",
            modelType);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().NotBeEmpty();
    }
}

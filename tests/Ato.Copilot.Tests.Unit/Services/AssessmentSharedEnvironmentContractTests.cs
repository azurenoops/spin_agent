using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class AssessmentSharedEnvironmentContractTests
{
    [Fact]
    public void SharedSourceMutation_IsConflictRatherThanInvalidSubscriptionInput()
    {
        // Arrange
        const string code = "ASSESSMENT_SHARED_ENVIRONMENT_REQUIRED";
        // Act
        var status = AssessmentEnvironmentErrors.HttpStatusCode(code);
        // Assert
        status.Should().Be(409);
    }
}

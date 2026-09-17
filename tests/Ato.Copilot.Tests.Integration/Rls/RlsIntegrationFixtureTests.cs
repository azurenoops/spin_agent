using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Rls;

public class RlsIntegrationFixtureTests
{
    [Fact]
    public void EnsureFixtureAvailable_WhenFixtureIsRequiredAndInitializationFails_Throws()
    {
        // Arrange
        var initializationException = new InvalidOperationException("seed failed");

        // Act
        var act = () => RlsIntegrationFixture.EnsureFixtureAvailable(
            fixtureRequired: true,
            initializationException);

        // Assert
        var thrown = act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("SQL Server RLS fixture is required but failed to initialize.")
            .Which;
        thrown.InnerException.Should().BeSameAs(initializationException);
    }

    [Fact]
    public void EnsureFixtureAvailable_WhenFixtureIsOptionalAndInitializationFails_DoesNotThrow()
    {
        // Arrange
        var initializationException = new InvalidOperationException("seed failed");

        // Act
        var act = () => RlsIntegrationFixture.EnsureFixtureAvailable(
            fixtureRequired: false,
            initializationException);

        // Assert
        act.Should().NotThrow();
    }
}
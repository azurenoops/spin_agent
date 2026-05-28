using Ato.Copilot.Core.Configuration.Auth;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T023 — startup-validation contract for <see cref="AuthOptions"/>.
/// Verifies the rules pinned by <c>contracts/internal-services.md § 5.2</c>:
/// the Development-vs-non-Development cookie-key gate, the
/// <see cref="AuthOptions.IdleTimeoutMinutes"/> range, positive throttle counts,
/// and the Teams-SSO Required mode contract.
/// </summary>
public class AuthOptionsValidatorTests
{
    private static AuthOptionsValidator Validator(string environmentName)
    {
        // Pre-Feature-051-T113 tests cover the non-manifest gates. Inject a
        // valid-manifest stub so the Required-mode tests below exercise only
        // the ConnectionName gate; dedicated manifest behavior lives in
        // TeamsManifestValidatorTests.
        const string validManifest = """
        { "manifestVersion": "1.17", "webApplicationInfo": { "id": "11111111-2222-3333-4444-555555555555", "resource": "api://bot.example.com" } }
        """;
        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        envMock.SetupGet(e => e.ContentRootPath).Returns("/tmp/test-content-root");
        return new AuthOptionsValidator(envMock.Object, () => validManifest);
    }

    private static AuthOptions ValidOptions() => new()
    {
        IdleTimeoutMinutes = 30,
        RememberTenantCookieDays = 30,
        Cookie = new AuthCookieOptions { SigningKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=" },
        Throttle = new AuthThrottleOptions
        {
            Development = new ThrottleBucket { PerIpPerMinute = 100, PerIdentityPerMinute = 100 },
            Production = new ThrottleBucket { PerIpPerMinute = 20, PerIdentityPerMinute = 10 },
        },
        TeamsSso = new AuthTeamsSsoOptions
        {
            Mode = TeamsSsoMode.Optional,
            ConnectionName = string.Empty,
        },
        Archive = new AuthArchiveOptions { RunHourUtc = 2 },
    };

    [Fact]
    public void Validate_ValidOptionsInProduction_Succeeds()
    {
        // Arrange
        var validator = Validator(Environments.Production);
        var options = ValidOptions();

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingCookieSigningKeyInProduction_Fails()
    {
        // Arrange
        var validator = Validator(Environments.Production);
        var options = ValidOptions();
        options.Cookie.SigningKey = string.Empty;

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Auth:Cookie:SigningKey");
    }

    [Fact]
    public void Validate_MissingCookieSigningKeyInDevelopment_Succeeds()
    {
        // Arrange
        var validator = Validator(Environments.Development);
        var options = ValidOptions();
        options.Cookie.SigningKey = string.Empty;

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingCookieSigningKeyInTesting_Succeeds()
    {
        // Arrange — integration tests run under env=Testing and seed many
        // hosts. The validator treats Testing as Development-equivalent
        // for the SigningKey gate so .ValidateOnStart() does not fail the
        // host before any tests run.
        var validator = Validator("Testing");
        var options = ValidOptions();
        options.Cookie.SigningKey = string.Empty;

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(481)]
    [InlineData(-1)]
    public void Validate_IdleTimeoutOutsideRange_Fails(int minutes)
    {
        // Arrange
        var validator = Validator(Environments.Production);
        var options = ValidOptions();
        options.IdleTimeoutMinutes = minutes;

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("IdleTimeoutMinutes");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(480)]
    public void Validate_IdleTimeoutInRange_Succeeds(int minutes)
    {
        // Arrange
        var validator = Validator(Environments.Production);
        var options = ValidOptions();
        options.IdleTimeoutMinutes = minutes;

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_ProductionPerIpPerMinuteZero_Fails()
    {
        // Arrange
        var validator = Validator(Environments.Production);
        var options = ValidOptions();
        options.Throttle.Production.PerIpPerMinute = 0;

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Auth:Throttle:Production:PerIpPerMinute");
    }

    [Fact]
    public void Validate_ProductionPerIdentityPerMinuteNegative_Fails()
    {
        // Arrange
        var validator = Validator(Environments.Production);
        var options = ValidOptions();
        options.Throttle.Production.PerIdentityPerMinute = -1;

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Auth:Throttle:Production:PerIdentityPerMinute");
    }

    [Fact]
    public void Validate_TeamsSsoRequiredWithoutConnectionName_Fails()
    {
        // Arrange
        var validator = Validator(Environments.Production);
        var options = ValidOptions();
        options.TeamsSso.Mode = TeamsSsoMode.Required;
        options.TeamsSso.ConnectionName = string.Empty;

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Auth:TeamsSso:ConnectionName");
    }

    [Fact]
    public void Validate_TeamsSsoRequiredWithConnectionName_Succeeds()
    {
        // Arrange
        var validator = Validator(Environments.Production);
        var options = ValidOptions();
        options.TeamsSso.Mode = TeamsSsoMode.Required;
        options.TeamsSso.ConnectionName = "Entra-Government";

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_TeamsSsoOptionalWithoutConnectionName_Succeeds()
    {
        // Arrange
        var validator = Validator(Environments.Production);
        var options = ValidOptions();
        options.TeamsSso.Mode = TeamsSsoMode.Optional;
        options.TeamsSso.ConnectionName = string.Empty;

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Validate_RememberTenantCookieDaysOutsideRange_Fails(int days)
    {
        // Arrange
        var validator = Validator(Environments.Production);
        var options = ValidOptions();
        options.RememberTenantCookieDays = days;

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("RememberTenantCookieDays");
    }

    [Fact]
    public void Validate_ArchiveRunHourOutsideRange_Fails()
    {
        // Arrange
        var validator = Validator(Environments.Production);
        var options = ValidOptions();
        options.Archive.RunHourUtc = 24;

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Auth:Archive:RunHourUtc");
    }

    [Fact]
    public void Validate_StagingTreatedAsNonDevelopment_RequiresCookieKey()
    {
        // Arrange — analysis C11: non-Development uses Production block + Production gates.
        var validator = Validator("Staging");
        var options = ValidOptions();
        options.Cookie.SigningKey = string.Empty;

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Auth:Cookie:SigningKey");
    }
}

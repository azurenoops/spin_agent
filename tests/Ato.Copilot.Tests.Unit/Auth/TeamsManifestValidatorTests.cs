using Ato.Copilot.Core.Configuration.Auth;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T112 — Teams manifest startup-validation contract per
/// <c>research.md § R12</c> and <c>contracts/m365-bot.md § 2</c>.
/// </summary>
/// <remarks>
/// The validator reads the deployed Teams app manifest and, when
/// <see cref="AuthTeamsSsoOptions.Mode"/> = <see cref="TeamsSsoMode.Required"/>,
/// fails startup if <c>webApplicationInfo.id</c> is missing/empty or
/// the manifest file itself is missing. <see cref="TeamsSsoMode.Optional"/>
/// and <see cref="TeamsSsoMode.Disabled"/> are unaffected by manifest state.
/// </remarks>
public class TeamsManifestValidatorTests
{
    private static AuthOptionsValidator Validator(
        string environmentName,
        Func<string>? manifestReader)
    {
        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        envMock.SetupGet(e => e.ContentRootPath).Returns("/tmp/test-content-root");
        return new AuthOptionsValidator(envMock.Object, manifestReader);
    }

    private static AuthOptions BaseOptions(TeamsSsoMode mode, string connectionName = "Entra-Government") => new()
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
            Mode = mode,
            ConnectionName = connectionName,
        },
        Archive = new AuthArchiveOptions { RunHourUtc = 2 },
    };

    [Fact]
    public void Validate_TeamsSsoRequired_WithEmptyWebApplicationInfoId_Fails()
    {
        // Arrange
        var manifestJson = """
        {
            "manifestVersion": "1.17",
            "id": "02eb4bb5-edef-4192-be23-9fd7b942fd06",
            "webApplicationInfo": {
                "id": "",
                "resource": "api://bot.example.com"
            }
        }
        """;
        var validator = Validator(Environments.Production, () => manifestJson);
        var options = BaseOptions(TeamsSsoMode.Required);

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("webApplicationInfo.id");
        result.FailureMessage.Should().Contain("Auth:TeamsSso:Mode");
    }

    [Fact]
    public void Validate_TeamsSsoRequired_WithMissingWebApplicationInfo_Fails()
    {
        // Arrange — webApplicationInfo block absent entirely
        var manifestJson = """
        {
            "manifestVersion": "1.17",
            "id": "02eb4bb5-edef-4192-be23-9fd7b942fd06"
        }
        """;
        var validator = Validator(Environments.Production, () => manifestJson);
        var options = BaseOptions(TeamsSsoMode.Required);

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("webApplicationInfo.id");
    }

    [Fact]
    public void Validate_TeamsSsoRequired_WithPopulatedWebApplicationInfoId_Succeeds()
    {
        // Arrange
        var manifestJson = """
        {
            "manifestVersion": "1.17",
            "id": "02eb4bb5-edef-4192-be23-9fd7b942fd06",
            "webApplicationInfo": {
                "id": "11111111-2222-3333-4444-555555555555",
                "resource": "api://bot.example.com/11111111-2222-3333-4444-555555555555"
            }
        }
        """;
        var validator = Validator(Environments.Production, () => manifestJson);
        var options = BaseOptions(TeamsSsoMode.Required);

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_TeamsSsoRequired_WithMissingManifestFile_Fails()
    {
        // Arrange — the reader signals "no manifest at any candidate path"
        // by throwing FileNotFoundException. The validator surfaces a clear
        // message identifying the offending config key.
        var validator = Validator(
            Environments.Production,
            () => throw new FileNotFoundException("Teams manifest not found at any candidate path."));
        var options = BaseOptions(TeamsSsoMode.Required);

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Teams manifest");
        result.FailureMessage.Should().Contain("Auth:TeamsSso:Mode");
    }

    [Fact]
    public void Validate_TeamsSsoOptional_WithMissingManifestFile_Succeeds()
    {
        // Arrange — Optional mode does not require the manifest. The
        // reader's FileNotFoundException is swallowed.
        var validator = Validator(
            Environments.Production,
            () => throw new FileNotFoundException("Teams manifest not found at any candidate path."));
        var options = BaseOptions(TeamsSsoMode.Optional, connectionName: string.Empty);

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_TeamsSsoOptional_WithEmptyWebApplicationInfoId_Succeeds()
    {
        // Arrange
        var manifestJson = """
        {
            "manifestVersion": "1.17",
            "id": "02eb4bb5-edef-4192-be23-9fd7b942fd06",
            "webApplicationInfo": { "id": "", "resource": "" }
        }
        """;
        var validator = Validator(Environments.Production, () => manifestJson);
        var options = BaseOptions(TeamsSsoMode.Optional, connectionName: string.Empty);

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_TeamsSsoDisabled_WithMissingManifestFile_Succeeds()
    {
        // Arrange — Disabled mode is unaffected by manifest state.
        var validator = Validator(
            Environments.Production,
            () => throw new FileNotFoundException("Teams manifest not found at any candidate path."));
        var options = BaseOptions(TeamsSsoMode.Disabled, connectionName: string.Empty);

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_TeamsSsoDisabled_WithEmptyWebApplicationInfoId_Succeeds()
    {
        // Arrange
        var manifestJson = """
        { "manifestVersion": "1.17", "webApplicationInfo": { "id": "" } }
        """;
        var validator = Validator(Environments.Production, () => manifestJson);
        var options = BaseOptions(TeamsSsoMode.Disabled, connectionName: string.Empty);

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_TeamsSsoRequired_WithMalformedManifestJson_Fails()
    {
        // Arrange — invalid JSON should surface as a startup failure
        // identifying the manifest, not a generic JsonException
        // bubbling out of host startup.
        var validator = Validator(
            Environments.Production,
            () => "{ this is not valid json");
        var options = BaseOptions(TeamsSsoMode.Required);

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Teams manifest");
    }
}

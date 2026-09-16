using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Extensions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Configuration;

public class AzureAdOptionsValidatorTests
{
    private static AzureAdOptionsValidator Validator(string environmentName)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(environmentName);
        return new AzureAdOptionsValidator(environment.Object);
    }

    private static AzureAdOptions ValidOptions() => new()
    {
        RequireCac = true,
        Instance = "https://login.microsoftonline.us/",
        TenantId = "11111111-1111-1111-1111-111111111111",
        ClientId = "22222222-2222-4222-8222-222222222222"
    };

    [Theory]
    [InlineData(nameof(AzureAdOptions.Instance), "AzureAd:Instance")]
    [InlineData(nameof(AzureAdOptions.TenantId), "AzureAd:TenantId")]
    [InlineData(nameof(AzureAdOptions.ClientId), "AzureAd:ClientId")]
    public void Validate_RequiredValueMissingInProduction_Fails(
        string propertyName,
        string expectedConfigurationKey)
    {
        // Arrange
        var options = ValidOptions();
        typeof(AzureAdOptions).GetProperty(propertyName)!.SetValue(options, string.Empty);

        // Act
        var result = Validator(Environments.Production).Validate(name: null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(expectedConfigurationKey);
    }

    [Fact]
    public void Validate_RequiredValuesPresentInProduction_Succeeds()
    {
        // Arrange
        var options = ValidOptions();

        // Act
        var result = Validator(Environments.Production).Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Validate_NonProductionEnvironment_AllowsUnconfiguredAzureAd(
        string environmentName)
    {
        // Arrange
        var options = new AzureAdOptions();

        // Act
        var result = Validator(environmentName).Validate(name: null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AddAzureAdConfiguration_MissingClientIdInProduction_FailsHostStartup()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.us/",
            ["AzureAd:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["AzureAd:ClientId"] = string.Empty
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });
        builder.Services.AddAzureAdConfiguration(configuration);
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        await act.Should().ThrowAsync<OptionsValidationException>()
            .WithMessage("*AzureAd:ClientId*");
    }

    [Fact]
    public async Task AddAzureAdConfiguration_ValidProductionConfiguration_StartsHost()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.us/",
            ["AzureAd:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["AzureAd:ClientId"] = "22222222-2222-4222-8222-222222222222"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });
        builder.Services.AddAzureAdConfiguration(configuration);
        using var host = builder.Build();

        // Act
        var act = () => host.StartAsync();

        // Assert
        await act.Should().NotThrowAsync();
        await host.StopAsync();
    }
}

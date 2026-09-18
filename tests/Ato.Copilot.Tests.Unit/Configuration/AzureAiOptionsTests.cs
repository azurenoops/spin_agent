using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;

namespace Ato.Copilot.Tests.Unit.Configuration;

/// <summary>
/// Unit tests for AzureAiOptions — verifies default values,
/// configuration binding, computed properties, and AiProvider enum.
/// </summary>
public class AzureAiOptionsTests
{
    [Fact]
    public void DefaultValues_Enabled_IsFalse()
    {
        var options = new AzureAiOptions();
        options.Enabled.Should().BeFalse();
    }

    [Fact]
    public void DefaultValues_MaxToolIterations_Is10()
    {
        var options = new AzureAiOptions();
        options.MaxToolIterations.Should().Be(10);
    }

    [Fact]
    public void DefaultValues_Temperature_Is03()
    {
        var options = new AzureAiOptions();
        options.Temperature.Should().Be(0.3);
    }

    [Fact]
    public void DefaultValues_HaveExpectedDefaults()
    {
        var options = new AzureAiOptions();

        options.Endpoint.Should().BeEmpty();
        options.DeploymentName.Should().Be("gpt-4o");
        options.UseManagedIdentity.Should().BeTrue();
        options.CloudEnvironment.Should().Be("AzurePublicCloud");
        options.MaxCompletionTokens.Should().Be(4096);
        options.ConversationWindowSize.Should().Be(20);
        options.RunTimeoutSeconds.Should().Be(60);
        options.Provider.Should().Be(AiProvider.OpenAi);
        options.AllowBackendFallback.Should().BeFalse();
    }

    [Fact]
    public void Binding_FromConfigSection_BindsAllProperties()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAi:Enabled"] = "true",
                ["AzureAi:Provider"] = "Foundry",
                ["AzureAi:Endpoint"] = "https://test.openai.azure.us/",
                ["AzureAi:DeploymentName"] = "gpt-4o-custom",
                ["AzureAi:ApiKey"] = "test-key-123",
                ["AzureAi:UseManagedIdentity"] = "false",
                ["AzureAi:CloudEnvironment"] = "AzureGovernment",
                ["AzureAi:MaxToolIterations"] = "15",
                ["AzureAi:Temperature"] = "0.7",
                ["AzureAi:FoundryProjectEndpoint"] = "https://foundry.azure.us/proj",
                ["AzureAi:RunTimeoutSeconds"] = "120",
                ["AzureAi:AllowBackendFallback"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<AzureAiOptions>(config.GetSection("AzureAi"));
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AzureAiOptions>>().Value;

        options.Enabled.Should().BeTrue();
        options.Provider.Should().Be(AiProvider.Foundry);
        options.Endpoint.Should().Be("https://test.openai.azure.us/");
        options.DeploymentName.Should().Be("gpt-4o-custom");
        options.ApiKey.Should().Be("test-key-123");
        options.UseManagedIdentity.Should().BeFalse();
        options.CloudEnvironment.Should().Be("AzureGovernment");
        options.MaxToolIterations.Should().Be(15);
        options.Temperature.Should().Be(0.7);
        options.FoundryProjectEndpoint.Should().Be("https://foundry.azure.us/proj");
        options.RunTimeoutSeconds.Should().Be(120);
        options.AllowBackendFallback.Should().BeTrue();
    }

    [Fact]
    public void Binding_MissingSection_UsesDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.Configure<AzureAiOptions>(config.GetSection("AzureAi"));
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AzureAiOptions>>().Value;

        options.Enabled.Should().BeFalse();
        options.MaxToolIterations.Should().Be(10);
        options.Temperature.Should().Be(0.3);
        options.Endpoint.Should().BeEmpty();
    }

    [Fact]
    public void Binding_PartialConfig_MergesWithDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAi:Enabled"] = "true",
                ["AzureAi:Endpoint"] = "https://my-gov.openai.azure.us/"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<AzureAiOptions>(config.GetSection("AzureAi"));
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AzureAiOptions>>().Value;

        options.Enabled.Should().BeTrue();
        options.Endpoint.Should().Be("https://my-gov.openai.azure.us/");
        // Defaults preserved
        options.MaxToolIterations.Should().Be(10);
        options.Temperature.Should().Be(0.3);
        options.DeploymentName.Should().Be("gpt-4o");
    }

    [Fact]
    public void IsConfigured_WhenEnabledAndEndpointSet_ReturnsTrue()
    {
        var options = new AzureAiOptions { Enabled = true, Endpoint = "https://test.openai.azure.us/" };
        options.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_WhenDisabled_ReturnsFalse()
    {
        var options = new AzureAiOptions { Enabled = false, Endpoint = "https://test.openai.azure.us/" };
        options.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsFoundry_WhenProviderFoundryAndEndpointSet_ReturnsTrue()
    {
        var options = new AzureAiOptions
        {
            Provider = AiProvider.Foundry,
            FoundryProjectEndpoint = "https://foundry.azure.us/proj"
        };
        options.IsFoundry.Should().BeTrue();
    }

    [Fact]
    public void IsFoundry_WhenProviderOpenAi_ReturnsFalse()
    {
        var options = new AzureAiOptions
        {
            Provider = AiProvider.OpenAi,
            FoundryProjectEndpoint = "https://foundry.azure.us/proj"
        };
        options.IsFoundry.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------
    // Regression tests for #698 — silent model swap
    // ---------------------------------------------------------------------------

    /// <summary>
    /// When AzureAi:DeploymentName is absent from configuration, the IChatClient
    /// fallback silently used "gpt-4o" without any indication.  This test asserts
    /// that the resolved deployment name IS "gpt-4o" in that case so the warning
    /// log path in CoreServiceExtensions is exercised (the warning fires when
    /// configuredDeployment is null).
    /// </summary>
    [Fact]
    public void DeploymentName_WhenNotConfigured_DefaultsToGpt4o_SignallingDefaultFallback()
    {
        // Arrange — no AzureAi:DeploymentName key in config
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAi:Enabled"] = "true",
                ["AzureAi:Endpoint"] = "https://my-gov.openai.azure.us/"
                // DeploymentName intentionally absent
            })
            .Build();

        // The raw config value is null when key is absent
        var rawValue = config.GetValue<string>("AzureAi:DeploymentName");
        rawValue.Should().BeNull("the key is absent — null triggers the #698 startup warning");

        // AzureAiOptions itself falls back to "gpt-4o" via property default
        var services = new ServiceCollection();
        services.Configure<AzureAiOptions>(config.GetSection("AzureAi"));
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AzureAiOptions>>().Value;

        options.DeploymentName.Should().Be("gpt-4o",
            "default must be gpt-4o so the startup warning log is accurate");
    }

    /// <summary>
    /// When AzureAi:DeploymentName is explicitly configured, the raw config value
    /// is non-null and the startup warning path in CoreServiceExtensions is NOT triggered.
    /// </summary>
    [Fact]
    public void DeploymentName_WhenConfigured_IsNonNull_NoDefaultFallbackWarning()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAi:Enabled"] = "true",
                ["AzureAi:Endpoint"] = "https://my-gov.openai.azure.us/",
                ["AzureAi:DeploymentName"] = "my-gpt4o-deployment"
            })
            .Build();

        // When explicitly set, raw value is non-null — warning path in RegisterChatClient is skipped
        var rawValue = config.GetValue<string>("AzureAi:DeploymentName");
        rawValue.Should().NotBeNull("explicit configuration must not trigger the default-fallback warning");
        rawValue.Should().Be("my-gpt4o-deployment");
    }
}

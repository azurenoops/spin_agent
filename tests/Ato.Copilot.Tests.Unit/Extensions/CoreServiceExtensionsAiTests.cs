using Xunit;
using FluentAssertions;
using Azure.AI.Agents.Persistent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ato.Copilot.Core.Extensions;

namespace Ato.Copilot.Tests.Unit.Extensions;

/// <summary>
/// Unit tests for IChatClient factory registration in CoreServiceExtensions.
/// Verifies conditional registration based on AzureAi configuration.
/// </summary>
public class CoreServiceExtensionsAiTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void AddAtoCopilotCore_CompletionTokenAdapterRequiresExplicitOptIn(string? setting, bool expected)
    {
        // Arrange
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAi:Endpoint"] = "https://test.openai.azure.com/",
            ["AzureAi:ApiKey"] = "unit-test-only",
            ["AzureAi:DeploymentName"] = "production-reasoning",
            ["AzureAi:UseMaxCompletionTokens"] = setting
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtoCopilotCore(config);
        using var provider = services.BuildServiceProvider();

        // Act
        var client = provider.GetRequiredService<IChatClient>();

        // Assert
        (client.GetService<OpenAI.Chat.ChatClient>() is Ato.Copilot.Core.Services.AzureCompletionTokenChatClient)
            .Should().Be(expected);
        client.GetService<ChatClientMetadata>()!.DefaultModelId.Should().Be("production-reasoning");
    }

    [Fact]
    public void AddAtoCopilotCore_WhenEndpointConfigured_RegistersIChatClient()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAi:Endpoint"] = "https://test.openai.azure.us/",
                ["AzureAi:ApiKey"] = "test-api-key-12345",
                ["AzureAi:DeploymentName"] = "gpt-4o"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtoCopilotCore(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IChatClient));
        descriptor.Should().NotBeNull("IChatClient should be registered when Endpoint is configured");
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddAtoCopilotCore_WhenEndpointEmpty_DoesNotRegisterIChatClient()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAi:Endpoint"] = "",
                ["AzureAi:ApiKey"] = "test-api-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtoCopilotCore(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IChatClient));
        descriptor.Should().BeNull("IChatClient should not be registered when Endpoint is empty");
    }

    [Fact]
    public void AddAtoCopilotCore_WhenAzureOpenAISectionMissing_DoesNotRegisterIChatClient()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:Azure:TenantId"] = "some-tenant"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtoCopilotCore(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IChatClient));
        descriptor.Should().BeNull("IChatClient should not be registered when AzureAi section is missing");
    }

    [Fact]
    public void AddAtoCopilotCore_WhenUnconfigured_NoStartupErrors()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddAtoCopilotCore(config);
        act.Should().NotThrow("AddAtoCopilotCore should not throw when Azure OpenAI is unconfigured");
    }

    [Fact]
    public void AddAtoCopilotCore_WithManagedIdentity_RegistersIChatClient()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAi:Endpoint"] = "https://test.openai.azure.us/",
                ["AzureAi:UseManagedIdentity"] = "true",
                ["AzureAi:DeploymentName"] = "gpt-4o"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtoCopilotCore(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IChatClient));
        descriptor.Should().NotBeNull("IChatClient should be registered with managed identity");
    }

    [Fact]
    public void AddAtoCopilotCore_WithGovEndpoint_RegistersIChatClient()
    {
        // FR-002/SC-008: Azure Government .us endpoint validation
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAi:Endpoint"] = "https://my-service.openai.azure.us/",
                ["AzureAi:ApiKey"] = "gov-api-key",
                ["AzureAi:DeploymentName"] = "gpt-4o",
                ["AzureAi:CloudEnvironment"] = "AzureGovernment"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtoCopilotCore(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IChatClient));
        descriptor.Should().NotBeNull("IChatClient should work with Azure Government .us endpoints");
    }

    [Fact]
    public void AddAtoCopilotCore_WithFoundryProjectEndpoint_RegistersPersistentAgentsClient()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAi:FoundryProjectEndpoint"] = "https://my-foundry-project.services.ai.azure.us/",
                ["AzureAi:CloudEnvironment"] = "AzureGovernment"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtoCopilotCore(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(PersistentAgentsClient));
        descriptor.Should().NotBeNull(
            "PersistentAgentsClient should be registered when FoundryProjectEndpoint is configured");
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddAtoCopilotCore_WithFoundryEndpointEmpty_DoesNotRegisterPersistentAgentsClient()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAi:FoundryProjectEndpoint"] = ""
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtoCopilotCore(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(PersistentAgentsClient));
        descriptor.Should().BeNull(
            "PersistentAgentsClient should not be registered when FoundryProjectEndpoint is empty");
    }

    [Fact]
    public void AddAtoCopilotCore_WithGovFoundryEndpoint_RegistersWithGovAuthorityHost()
    {
        // T013a / FR-002: Azure Government authority host for Foundry client
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAi:FoundryProjectEndpoint"] = "https://my-foundry-project.services.ai.azure.us/",
                ["AzureAi:CloudEnvironment"] = "AzureGovernment"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtoCopilotCore(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(PersistentAgentsClient));
        descriptor.Should().NotBeNull(
            "PersistentAgentsClient should be registered for Azure Government Foundry endpoints");

        // Verify the factory can be invoked (exercises Gov authority host path)
        var provider = services.BuildServiceProvider();
        var act = () => provider.GetService<PersistentAgentsClient>();
        act.Should().NotThrow(
            "PersistentAgentsClient factory should construct successfully with Gov authority host");
    }
}

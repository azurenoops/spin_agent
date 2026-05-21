using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Ato.Copilot.Core.Models;

namespace Ato.Copilot.Tests.Unit.Configuration;

/// <summary>
/// Binding + defaults tests for <see cref="ResilienceOptions"/> +
/// <see cref="ResiliencePipelineConfig"/>. Locks in the contract that the
/// <c>Resilience</c> appsettings section is read so the default HTTP client
/// resilience policy can be tuned per environment without a rebuild.
/// </summary>
public class ResilienceOptionsTests
{
    [Fact]
    public void DefaultPipelineConfig_MatchesHistoricalHardcodedLiterals()
    {
        // Arrange / Act
        var pipeline = new ResiliencePipelineConfig();

        // Assert — defaults must equal the literals that were previously
        // hardcoded inside CoreServiceExtensions.AddResilienceHandler so
        // existing deployments behave identically after the wire-up.
        pipeline.Name.Should().Be("default");
        pipeline.MaxRetryAttempts.Should().Be(3);
        pipeline.BaseDelaySeconds.Should().Be(2.0);
        pipeline.UseJitter.Should().BeTrue();
        pipeline.RequestTimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void Binding_FromConfigSection_BindsDefaultPipeline()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resilience:Pipelines:0:Name"] = "default",
                ["Resilience:Pipelines:0:MaxRetryAttempts"] = "7",
                ["Resilience:Pipelines:0:BaseDelaySeconds"] = "4.5",
                ["Resilience:Pipelines:0:UseJitter"] = "false",
                ["Resilience:Pipelines:0:RequestTimeoutSeconds"] = "60"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<ResilienceOptions>(config.GetSection(ResilienceOptions.SectionName));
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<ResilienceOptions>>().Value;

        // Assert
        options.Pipelines.Should().HaveCount(1);
        var pipeline = options.Pipelines[0];
        pipeline.Name.Should().Be("default");
        pipeline.MaxRetryAttempts.Should().Be(7);
        pipeline.BaseDelaySeconds.Should().Be(4.5);
        pipeline.UseJitter.Should().BeFalse();
        pipeline.RequestTimeoutSeconds.Should().Be(60);
    }

    [Fact]
    public void Binding_MissingSection_ReturnsEmptyPipelineList()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.Configure<ResilienceOptions>(config.GetSection(ResilienceOptions.SectionName));
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<ResilienceOptions>>().Value;

        // Assert — empty list signals "no override; use defaults" to consumers
        options.Pipelines.Should().BeEmpty();
    }

    [Fact]
    public void SectionName_MatchesAppsettingsKey()
    {
        ResilienceOptions.SectionName.Should().Be("Resilience");
    }
}

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Ato.Copilot.Core.Configuration;

namespace Ato.Copilot.Tests.Unit.Configuration;

/// <summary>
/// Binding + defaults tests for <see cref="DatabaseOptions"/>.
/// Locks in the contract that the <c>Database</c> appsettings section is read,
/// so changing those JSON keys at deploy time actually steers EF Core retry,
/// command timeout, provider, and sensitive-data-logging behavior.
/// </summary>
public class DatabaseOptionsTests
{
    [Fact]
    public void DefaultValues_MatchHistoricalHardcodedLiterals()
    {
        // Arrange / Act
        var options = new DatabaseOptions();

        // Assert — defaults must equal the literals that were previously
        // hardcoded in CoreServiceExtensions.RegisterDbContext and
        // Chat/Program.cs so behavior is unchanged when the JSON omits keys.
        options.Provider.Should().Be("SQLite");
        options.EnableSensitiveDataLogging.Should().BeFalse();
        options.CommandTimeoutSeconds.Should().Be(30);
        options.MaxRetryCount.Should().Be(5);
        options.MaxRetryDelay.Should().Be(30);
    }

    [Fact]
    public void Binding_FromConfigSection_BindsAllProperties()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "SqlServer",
                ["Database:EnableSensitiveDataLogging"] = "true",
                ["Database:CommandTimeoutSeconds"] = "90",
                ["Database:MaxRetryCount"] = "7",
                ["Database:MaxRetryDelay"] = "45"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<DatabaseOptions>(config.GetSection(DatabaseOptions.SectionName));
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        // Assert
        options.Provider.Should().Be("SqlServer");
        options.EnableSensitiveDataLogging.Should().BeTrue();
        options.CommandTimeoutSeconds.Should().Be(90);
        options.MaxRetryCount.Should().Be(7);
        options.MaxRetryDelay.Should().Be(45);
    }

    [Fact]
    public void Binding_MissingSection_UsesDefaults()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.Configure<DatabaseOptions>(config.GetSection(DatabaseOptions.SectionName));
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        // Assert
        options.Provider.Should().Be("SQLite");
        options.CommandTimeoutSeconds.Should().Be(30);
        options.MaxRetryCount.Should().Be(5);
        options.MaxRetryDelay.Should().Be(30);
    }

    [Fact]
    public void SectionName_MatchesAppsettingsKey()
    {
        // Regression guard: the SectionName constant must match the literal
        // string used in services.Configure<DatabaseOptions>(...) and the
        // top-level "Database" key in appsettings.json. If anyone renames
        // either side, this test fails loudly.
        DatabaseOptions.SectionName.Should().Be("Database");
    }
}

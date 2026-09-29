using System.Text.RegularExpressions;
using System.Text;
using Ato.Copilot.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Deployment;

public class DockerPackageSourceContractTests
{
    [Fact]
    public void Mcp_runtime_retains_documents_on_the_owned_persistent_data_volume()
    {
        // Arrange
        var dockerfile = File.ReadAllText(Path.Combine(FindRepoRoot(), "Dockerfile"));
        var compose = File.ReadAllText(Path.Combine(FindRepoRoot(), "docker-compose.mcp.yml"));

        // Act
        var runtime = Regex.Match(dockerfile, @"(?ms)^FROM [^\r\n]+ AS runtime\r?\n(?<runtime>.*)$");

        // Assert
        runtime.Success.Should().BeTrue();
        runtime.Groups["runtime"].Value.Should().Contain("ENV ATO_ExportSettings__DataPath=/data");
        runtime.Groups["runtime"].Value.Should().Contain("chown -R atocopilot:atocopilot /data");
        runtime.Groups["runtime"].Value.Should().Contain("USER atocopilot");
        compose.Should().Contain("ato-data:/data");
    }

    [Fact]
    public void Mcp_runtime_storage_override_survives_the_production_json_reload()
    {
        // Arrange
        var dockerfile = File.ReadAllText(Path.Combine(FindRepoRoot(), "Dockerfile"));
        var setting = Regex.Match(dockerfile, @"(?mi)^ENV (?<key>\S*ExportSettings__DataPath)=(?<value>\S+)");
        setting.Success.Should().BeTrue();
        setting.Groups["key"].Value.Should().StartWith("ATO_");
        var prefix = "TEST_EXPORT_STORAGE_" + Guid.NewGuid().ToString("N") + "_";
        var isolatedVariable = prefix + setting.Groups["key"].Value["ATO_".Length..];
        Environment.SetEnvironmentVariable(isolatedVariable, setting.Groups["value"].Value);
        try
        {
            // Act
            using var json = new MemoryStream(Encoding.UTF8.GetBytes("""{"ExportSettings":{"DataPath":"./data"}}"""));
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ExportSettings:DataPath"] = "/host-default" })
                .AddJsonStream(json)
                .AddEnvironmentVariables(prefix)
                .Build();

            // Assert
            configuration.GetSection(ExportSettings.SectionName).Get<ExportSettings>()!.DataPath.Should().Be("/data");
        }
        finally
        {
            Environment.SetEnvironmentVariable(isolatedVariable, null);
        }
    }

    [Fact]
    public void Mcp_compose_forwards_optional_entra_credentials_without_embedding_them()
    {
        // Arrange
        var compose = File.ReadAllText(Path.Combine(FindRepoRoot(), "docker-compose.mcp.yml"));

        // Act
        var service = Regex.Match(compose, @"(?ms)^  ato-copilot:\r?\n(?<service>.*?)(?=^  [a-z]|\z)");

        // Assert
        service.Success.Should().BeTrue();
        foreach (var name in new[] { "AZURE_TENANT_ID", "AZURE_CLIENT_ID", "AZURE_CLIENT_SECRET" })
            service.Groups["service"].Value.Should().Contain($"{name}=${{{name}:-}}");
        service.Groups["service"].Value.Should().Contain(
            "ATO_AZUREAI__USEMAXCOMPLETIONTOKENS=${ATO_AZUREAI__USEMAXCOMPLETIONTOKENS:-false}");
    }

    [Theory]
    [InlineData("Dockerfile", "Ato.Copilot.Mcp")]
    [InlineData("src/Ato.Copilot.Chat/Dockerfile", "Ato.Copilot.Chat")]
    public void Backend_build_uses_selected_source_without_implicit_publish_restore(
        string relativePath, string project)
    {
        // Arrange
        var path = Path.Combine(FindRepoRoot(), relativePath);

        // Act
        var dockerfile = File.ReadAllText(path);
        var publish = Regex.Match(
            dockerfile, @"RUN dotnet publish(?:[^\r\n]*\\\r?\n)*[^\r\n]*");

        // Assert
        dockerfile.Should().Contain("FROM scratch AS nuget-packages");
        dockerfile.Should().Contain("ARG NUGET_SOURCE=https://api.nuget.org/v3/index.json");
        dockerfile.Should().MatchRegex(
            @"RUN --mount=type=bind,from=nuget-packages,target=/nuget-feed \\\r?\n\s+dotnet restore");
        dockerfile.Should().Contain(
            $"dotnet restore src/{project}/{project}.csproj --source \"$NUGET_SOURCE\"");
        publish.Success.Should().BeTrue();
        publish.Value.Should().Contain("--no-restore");
    }

    [Theory]
    [InlineData("ato-copilot")]
    [InlineData("ato-chat")]
    public void Offline_override_requires_explicit_package_context_and_local_source(string service)
    {
        // Arrange
        var path = Path.Combine(FindRepoRoot(), "docker-compose.offline.yml");
        File.Exists(path).Should().BeTrue();

        // Act
        var compose = File.ReadAllText(path);
        var section = Regex.Match(
            compose, $@"(?ms)^  {Regex.Escape(service)}:\r?\n(?<service>.*?)(?=^  [a-z]|\z)");

        // Assert
        section.Success.Should().BeTrue();
        section.Groups["service"].Value.Should().Contain("NUGET_SOURCE: /nuget-feed");
        section.Groups["service"].Value.Should().Contain("additional_contexts:");
        section.Groups["service"].Value.Should().Contain("nuget-packages: ${NUGET_OFFLINE_PACKAGES:?");
    }

    [Theory]
    [InlineData("ato-copilot")]
    [InlineData("ato-chat")]
    public void Compose_forwards_selected_source_to_each_backend_build(string service)
    {
        // Arrange
        var compose = File.ReadAllText(Path.Combine(FindRepoRoot(), "docker-compose.mcp.yml"));

        // Act
        var build = Regex.Match(
            compose, $@"(?ms)^  {Regex.Escape(service)}:\r?\n    build:\r?\n(?<build>.*?)(?=^    \S)");

        // Assert
        build.Success.Should().BeTrue();
        build.Groups["build"].Value.Should().Contain(
            "NUGET_SOURCE: ${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}");
    }

    [Fact]
    public void Chat_build_copies_channels_project_before_restore()
    {
        // Arrange
        var path = Path.Combine(FindRepoRoot(), "src", "Ato.Copilot.Chat", "Dockerfile");

        // Act
        var dockerfile = File.ReadAllText(path);
        var restore = dockerfile.IndexOf("dotnet restore", StringComparison.Ordinal);

        // Assert
        restore.Should().BeGreaterThan(0);
        dockerfile[..restore].Should().Contain(
            "COPY src/Ato.Copilot.Channels/Ato.Copilot.Channels.csproj src/Ato.Copilot.Channels/");
    }

    [Theory]
    [InlineData("src/Ato.Copilot.Chat/Dockerfile")]
    [InlineData("src/Ato.Copilot.Dashboard/Dockerfile")]
    public void Frontend_build_uses_selected_registry_without_changing_default(string relativePath)
    {
        // Arrange
        var path = Path.Combine(FindRepoRoot(), relativePath);

        // Act
        var dockerfile = File.ReadAllText(path);

        // Assert
        dockerfile.Should().Contain("ARG NPM_REGISTRY=https://registry.npmjs.org");
        dockerfile.Should().Contain("RUN npm ci --registry=\"$NPM_REGISTRY\"");
    }

    [Theory]
    [InlineData("ato-chat")]
    [InlineData("ato-dashboard")]
    public void Compose_forwards_selected_registry_to_each_frontend_build(string service)
    {
        // Arrange
        var compose = File.ReadAllText(Path.Combine(FindRepoRoot(), "docker-compose.mcp.yml"));

        // Act
        var build = Regex.Match(
            compose, $@"(?ms)^  {Regex.Escape(service)}:\r?\n    build:\r?\n(?<build>.*?)(?=^    \S)");

        // Assert
        build.Success.Should().BeTrue();
        build.Groups["build"].Value.Should().Contain(
            "NPM_REGISTRY: ${NPM_REGISTRY:-https://registry.npmjs.org}");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Ato.Copilot.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root (Ato.Copilot.sln).");
    }
}

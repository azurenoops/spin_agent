using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Deployment;

public class DashboardDockerfileTests
{
    [Fact]
    public void Runtime_defines_empty_single_tenant_override_for_nginx_substitution()
    {
        // Arrange
        var dockerfile = ReadRepositoryFile("src/Ato.Copilot.Dashboard/Dockerfile");

        // Act
        var runtimeStart = dockerfile.IndexOf("AS runtime", StringComparison.Ordinal);

        // Assert
        runtimeStart.Should().BeGreaterThan(0);
        dockerfile[runtimeStart..].Should().Contain("ENV FORCE_SINGLE_TENANT=\"\"");
    }

    [Fact]
    public void Build_environment_defaults_to_production_and_preserves_build_dependencies()
    {
        // Arrange
        var dockerfile = ReadRepositoryFile("src/Ato.Copilot.Dashboard/Dockerfile");

        // Act
        var install = dockerfile.IndexOf("RUN npm ci", StringComparison.Ordinal);
        var environment = dockerfile.IndexOf("ARG DASHBOARD_BUILD_ENV=production", StringComparison.Ordinal);

        // Assert
        install.Should().BeGreaterThan(0);
        environment.Should().BeGreaterThan(install);
        dockerfile.Should().Contain("RUN NODE_ENV=\"$DASHBOARD_BUILD_ENV\" npm run build");
        dockerfile[dockerfile.IndexOf("AS runtime", StringComparison.Ordinal)..]
            .Should().NotContain("DASHBOARD_BUILD_ENV");
    }

    [Fact]
    public void Local_compose_selects_development_dashboard_build_with_an_explicit_override()
    {
        // Arrange
        var compose = ReadRepositoryFile("docker-compose.mcp.yml");

        // Act
        var build = Regex.Match(compose,
            @"(?ms)^  ato-dashboard:\r?\n    build:\r?\n(?<build>.*?)(?=^    \S)");

        // Assert
        build.Success.Should().BeTrue();
        build.Groups["build"].Value.Should().Contain(
            "DASHBOARD_BUILD_ENV: ${DASHBOARD_BUILD_ENV:-development}");
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ato.Copilot.sln")))
            directory = directory.Parent;
        if (directory is null)
            throw new DirectoryNotFoundException("Could not locate repo root (Ato.Copilot.sln).");
        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }
}

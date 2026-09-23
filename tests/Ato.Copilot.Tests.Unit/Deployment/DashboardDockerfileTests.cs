using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Deployment;

public class DashboardDockerfileTests
{
    [Fact]
    public void Runtime_defines_empty_single_tenant_override_for_nginx_substitution()
    {
        // Arrange
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ato.Copilot.sln")))
            directory = directory.Parent;
        if (directory is null)
            throw new DirectoryNotFoundException("Could not locate repo root (Ato.Copilot.sln).");
        var dockerfile = File.ReadAllText(Path.Combine(
            directory.FullName, "src", "Ato.Copilot.Dashboard", "Dockerfile"));

        // Act
        var runtimeStart = dockerfile.IndexOf("AS runtime", StringComparison.Ordinal);

        // Assert
        runtimeStart.Should().BeGreaterThan(0);
        dockerfile[runtimeStart..].Should().Contain("ENV FORCE_SINGLE_TENANT=\"\"");
    }
}

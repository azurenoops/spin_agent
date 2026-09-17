using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.CrossCutting;

public class AuditActorSourceGuardTests
{
    [Fact]
    public void McpEndpoints_DoNotUseLegacyDashboardActor()
    {
        var repositoryRoot = FindRepositoryRoot();
        var endpointsDirectory = Path.Combine(
            repositoryRoot.FullName,
            "src",
            "Ato.Copilot.Mcp",
            "Endpoints");

        var offendingFiles = Directory
            .EnumerateFiles(endpointsDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("\"dashboard-user\"", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(repositoryRoot.FullName, file))
            .ToList();

        offendingFiles.Should().BeEmpty(
            "audit actors must be resolved from the authenticated request principal");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ato.Copilot.sln")))
            directory = directory.Parent;

        return directory ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Authentication;

public class AuthDeploymentContractTests
{
    [Fact]
    public void Local_compose_forwards_the_simulation_startup_switch()
    {
        // Arrange
        var path = Path.Combine(FindRepoRoot(), "docker-compose.mcp.yml");

        // Act
        var compose = File.ReadAllText(path);

        // Assert
        compose.Should().Contain("ATO_CACAUTH__SIMULATIONMODE=${ATO_CACAUTH__SIMULATIONMODE:-true}");
    }

    [Fact]
    public void Mcp_deployment_requires_complete_browser_and_api_auth_configuration()
    {
        // Arrange
        var workflow = ReadWorkflow();

        // Act - the reusable deployment workflow is the runtime configuration contract.

        // Assert
        workflow.Should().Contain("Missing environment variable: ATO_AUTH_MSAL_CLIENTID");
        workflow.Should().Contain("Missing environment variable: ATO_AUTH_MSAL_AUTHORITY");
        workflow.Should().Contain("Missing environment variable: ATO_AUTH_MSAL_REDIRECTURI");
        workflow.Should().Contain("Missing environment variable: ATO_AUTH_MSAL_POSTLOGOUTREDIRECTURI");
        workflow.Should().Contain("AUTH_AUDIENCE=\"${AUTH_CLIENT_ID}\"");
        workflow.Should().Contain("https://login.microsoftonline.com/) AUTH_CLOUD=\"AzurePublic\"");
        workflow.Should().Contain("https://login.microsoftonline.us/) AUTH_CLOUD=\"AzureUSGovernment\"");
        workflow.Should().Contain("ATO_AUTH__CLOUD=${AUTH_CLOUD}");
        workflow.Should().Contain("ATO_AZUREAD__INSTANCE=${AUTH_INSTANCE}");
        workflow.Should().Contain("ATO_AZUREAD__TENANTID=${AUTH_TENANT_ID}");
        workflow.Should().Contain("ATO_AZUREAD__CLIENTID=${AUTH_CLIENT_ID}");
        workflow.Should().Contain("ATO_AZUREAD__AUDIENCE=${AUTH_AUDIENCE}");
        workflow.Should().Contain("ATO_DATABASE__PROVIDER=${DATABASE_PROVIDER}");
    }

    private static string ReadWorkflow()
    {
        var path = Path.Combine(FindRepoRoot(), ".github", "workflows", "deploy-containerapp-stage.yml");
        File.Exists(path).Should().BeTrue();
        return File.ReadAllText(path);
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
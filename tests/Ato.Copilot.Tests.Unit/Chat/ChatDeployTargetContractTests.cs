using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Chat;

/// <summary>
/// Contract tests for Chat Container App targeting in CD.
/// Run 33771928221 (2026-09-03) showed deploy-dev-chat resolving
/// <c>APP=ca-ato-copilot-mcp-v2</c> because <c>AZURE_CHAT_CONTAINERAPP_NAME</c>
/// was empty and resolve-target treated chat as MCP.
/// </summary>
public class ChatDeployTargetContractTests
{
    [Fact]
    public void Resolve_target_refuses_to_deploy_chat_onto_mcp_app_name()
    {
        // Arrange
        var path = Path.Combine(FindRepoRoot(), ".github", "workflows", "deploy-containerapp-stage.yml");
        File.Exists(path).Should().BeTrue();
        var text = File.ReadAllText(path);

        // Act — contract is the workflow resolve-target script

        // Assert
        text.Should().Contain(
            @"WORKLOAD"" = ""chat""",
            "resolve-target must have an explicit chat branch");
        text.Should().Contain(
            "AZURE_CHAT_CONTAINERAPP_NAME",
            "chat target must come from AZURE_CHAT_CONTAINERAPP_NAME, not AZURE_CONTAINERAPP_NAME");
        text.Should().Contain(
            "Refusing to deploy workload=chat",
            "empty chat name must fail the job instead of updating the MCP app");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ato.Copilot.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root (Ato.Copilot.sln).");
    }
}

using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Chat;

/// <summary>
/// Contract tests for Chat Container App targeting in CD.
/// Run 33771928221 (2026-09-03) showed deploy-dev-chat resolving
/// <c>APP=ca-ato-copilot-mcp-v2</c> because <c>AZURE_CHAT_CONTAINERAPP_NAME</c>
/// was empty and resolve-target treated chat as MCP.
/// Run 33777693029 showed three further targeting bugs:
/// <list type="bullet">
/// <item>deploy-test-chat failed validation when the chat var was unset (test/prod have no env vars).</item>
/// <item>Dev <c>AZURE_CHAT_CONTAINERAPP_NAME=ca-ato-copilot-mcp-v2</c> so Chat still updated MCP.</item>
/// <item>Dev <c>AZURE_CONTAINERAPP_NAME=ca-ato-copilot-dashboard-v2</c> so MCP updated Dashboard.</item>
/// </list>
/// </summary>
public class ChatDeployTargetContractTests
{
    [Fact]
    public void Resolve_target_refuses_to_deploy_chat_onto_mcp_app_name()
    {
        // Arrange
        var text = ReadWorkflow();

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
            "empty or swapped chat name must fail the job instead of updating the MCP app");
    }

    [Fact]
    public void Resolve_target_derives_chat_name_from_mcp_like_dashboard()
    {
        // Arrange
        var text = ReadWorkflow();

        // Act — contract is the workflow resolve-target script

        // Assert — test/prod have no AZURE_CHAT_CONTAINERAPP_NAME (run 33777693029).
        // Dashboard already derives ${BASE_APP/-mcp/-dashboard}; chat must do the same
        // so an unset var creates/updates ca-ato-copilot-chat-v2 instead of failing.
        text.Should().Contain(
            "${BASE_APP/-mcp/-chat}",
            "unset AZURE_CHAT_CONTAINERAPP_NAME must derive the chat app from the MCP name");
    }

    [Fact]
    public void Resolve_target_rejects_chat_name_that_does_not_contain_chat()
    {
        // Arrange
        var text = ReadWorkflow();

        // Act — contract is the workflow resolve-target script

        // Assert — Dev env set AZURE_CHAT_CONTAINERAPP_NAME=ca-ato-copilot-mcp-v2
        // (run 33777693029 deploy-dev-chat). An explicit wrong value must fail.
        text.Should().Contain(
            "*chat*",
            "resolved chat app name must be required to contain 'chat'");
    }

    [Fact]
    public void Resolve_target_rejects_mcp_name_that_contains_dashboard()
    {
        // Arrange
        var text = ReadWorkflow();

        // Act — contract is the workflow resolve-target script

        // Assert — Dev env set AZURE_CONTAINERAPP_NAME=ca-ato-copilot-dashboard-v2
        // so deploy-dev (workload=mcp) updated the dashboard app.
        text.Should().Contain(
            "Refusing to deploy workload=mcp",
            "MCP must not deploy onto a dashboard-named Container App");
    }

    [Fact]
    public void Create_does_not_assign_acrpull_inline_via_registry_identity_system()
    {
        // Arrange
        var text = ReadWorkflow();

        // Act — contract is the Create/update container app step

        // Assert — run 33804223965: `az containerapp create --registry-identity system`
        // tried to write AcrPull and the CD OIDC principal lacks
        // Microsoft.Authorization/roleAssignments/write. Registry bind must
        // happen after identity assign; AcrPull is the later least-privilege step.
        text.Should().NotContain(
            "--registry-identity system",
            "create must not implicitly grant AcrPull; that fails the Chat first-create");
        text.Should().Contain(
            "containerapp registry set",
            "after identity assign, bind ACR with the app system identity");
    }

    private static string ReadWorkflow()
    {
        var path = Path.Combine(FindRepoRoot(), ".github", "workflows", "deploy-containerapp-stage.yml");
        File.Exists(path).Should().BeTrue();
        return File.ReadAllText(path);
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

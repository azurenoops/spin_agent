using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Chat;

/// <summary>
/// Contract tests for the Chat Container App Dockerfile frontend install.
/// CD build-image failed on 2026-09-01 (runs 33513113932, 33527240413, 33551808455)
/// with <c>Cannot find module 'ajv/dist/compile/codegen'</c> because the frontend
/// stage copied only package.json and ran <c>npm install</c>, resolving a floating
/// ajv/ajv-keywords tree that does not match CI (<c>npm ci</c> + lockfile).
/// </summary>
public class ChatDockerfileLockfileTests
{
    [Fact]
    public void Frontend_stage_installs_from_committed_lockfile_via_npm_ci()
    {
        // Arrange
        var dockerfilePath = Path.Combine(FindRepoRoot(), "src", "Ato.Copilot.Chat", "Dockerfile");
        File.Exists(dockerfilePath).Should().BeTrue("src/Ato.Copilot.Chat/Dockerfile must exist");
        var text = File.ReadAllText(dockerfilePath);

        // Act — contract is the Dockerfile install itself (no runtime execution)

        // Assert
        text.Should().Contain(
            "package-lock.json",
            "frontend stage must COPY the committed lockfile so Docker matches CI and local npm ci");
        text.Should().MatchRegex(
            @"RUN\s+npm ci\b",
            "frontend stage must use npm ci; npm install without the lockfile produced the ajv codegen CD failure");
        text.Should().NotMatchRegex(
            @"(?m)^RUN\s+npm install\b",
            "frontend stage must not use a floating npm install");
    }

    [Fact]
    public void Frontend_stage_copies_npmrc_so_legacy_peer_deps_apply_during_ci()
    {
        // Arrange
        var dockerfilePath = Path.Combine(FindRepoRoot(), "src", "Ato.Copilot.Chat", "Dockerfile");
        var text = File.ReadAllText(dockerfilePath);

        // Act — contract is the Dockerfile COPY set before npm ci

        // Assert
        text.Should().Contain(
            ".npmrc",
            "ClientApp/.npmrc sets legacy-peer-deps=true (react-scripts@5 optional peer vs TypeScript 5). npm ci must see it.");
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

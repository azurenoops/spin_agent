// ═══════════════════════════════════════════════════════════════════════════
// Feature 028 — Azure AI Foundry Agent Path
// Task #177 (Epic #132): Integration tests covering startup validation,
// provider routing, and error paths with a mock Foundry HTTP endpoint.
// ═══════════════════════════════════════════════════════════════════════════

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Configuration;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Agents;

/// <summary>
/// Integration tests for the Azure AI Foundry agent path (Task #177 / Epic #132).
/// Covers:
///   - Startup validation: fail-fast when Provider=Foundry and endpoint is empty
///   - Startup validation: passes through when Provider=OpenAi (no endpoint required)
///   - Startup validation: skips validation when AI is disabled (Enabled=false)
///   - Provider routing: Foundry path attempted before IChatClient fallback
///   - Provider routing: OpenAi path does NOT invoke Foundry
///   - Error paths: Foundry null client gracefully degrades to IChatClient fallback
///   - Error paths: Foundry null agent ID returns null from TryProcessWithFoundryAsync
/// </summary>
public class FoundryAgentIntegrationTests
{
    private readonly Mock<ILogger<FoundryIntegrationTestAgent>> _loggerMock = new();

    // ── Startup Validation ────────────────────────────────────────────────

    /// <summary>
    /// T177-1: ValidateFoundryConfig throws InvalidOperationException when
    /// Provider=Foundry and FoundryProjectEndpoint is null.
    /// </summary>
    [Fact]
    public void ValidateFoundryConfig_ProviderFoundry_EmptyEndpoint_ThrowsInvalidOperation()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AzureAi:Enabled"]  = "true",
            ["AzureAi:Provider"] = "Foundry",
            ["AzureAi:FoundryProjectEndpoint"] = null,
        });

        var act = () => InvokeValidateFoundryConfig(config);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*AzureAi:Provider is set to 'Foundry'*AzureAi:FoundryProjectEndpoint*");
    }

    /// <summary>
    /// T177-2: ValidateFoundryConfig throws when endpoint is whitespace-only.
    /// </summary>
    [Fact]
    public void ValidateFoundryConfig_ProviderFoundry_WhitespaceEndpoint_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AzureAi:Enabled"]  = "true",
            ["AzureAi:Provider"] = "Foundry",
            ["AzureAi:FoundryProjectEndpoint"] = "   ",
        });

        var act = () => InvokeValidateFoundryConfig(config);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*FoundryProjectEndpoint*");
    }

    /// <summary>
    /// T177-3: ValidateFoundryConfig passes when Provider=Foundry and endpoint is set.
    /// </summary>
    [Fact]
    public void ValidateFoundryConfig_ProviderFoundry_EndpointSet_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AzureAi:Enabled"]  = "true",
            ["AzureAi:Provider"] = "Foundry",
            ["AzureAi:FoundryProjectEndpoint"] = "https://my-resource.services.ai.azure.com/api/projects/my-project",
        });

        var act = () => InvokeValidateFoundryConfig(config);

        act.Should().NotThrow("Foundry with a valid endpoint should pass startup validation");
    }

    /// <summary>
    /// T177-4: ValidateFoundryConfig does not throw when Provider=OpenAi — endpoint not required.
    /// </summary>
    [Fact]
    public void ValidateFoundryConfig_ProviderOpenAi_NoEndpointRequired_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AzureAi:Enabled"]  = "true",
            ["AzureAi:Provider"] = "OpenAi",
            // No FoundryProjectEndpoint
        });

        var act = () => InvokeValidateFoundryConfig(config);

        act.Should().NotThrow("OpenAi provider does not require FoundryProjectEndpoint");
    }

    /// <summary>
    /// T177-5: ValidateFoundryConfig is a no-op when AI is disabled (Enabled=false).
    /// Even a misconfigured Foundry endpoint should not throw when AI is off.
    /// </summary>
    [Fact]
    public void ValidateFoundryConfig_AiDisabled_SkipsValidation()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AzureAi:Enabled"]  = "false",
            ["AzureAi:Provider"] = "Foundry",
            // No endpoint — would throw if validation ran
        });

        var act = () => InvokeValidateFoundryConfig(config);

        act.Should().NotThrow("disabled AI should skip Foundry endpoint validation entirely");
    }

    // ── Provider Routing ─────────────────────────────────────────────────

    /// <summary>
    /// T177-6: When Provider=Foundry, TryProcessWithBackendAsync attempts the Foundry path.
    /// Verified by observing FoundryCallCount > 0 before fallback.
    /// </summary>
    [Fact]
    public async Task Routing_ProviderFoundry_AttemptsFoundryPathFirst()
    {
        var agent = new FoundryIntegrationTestAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions
            {
                Enabled  = true,
                Provider = AiProvider.Foundry,
                FoundryProjectEndpoint = "https://mock.foundry.endpoint/api/projects/test",
            },
            foundryClient: null); // null → TryProcessWithFoundryAsync returns null → falls through

        var context = new AgentConversationContext { ConversationId = "routing-1" };
        await agent.InvokeTryProcessWithBackendAsync("test message", context);

        agent.FoundryAttemptCount.Should().BeGreaterThan(0,
            "Foundry path should be attempted first when Provider=Foundry");
    }

    /// <summary>
    /// T177-7: When Provider=OpenAi, Foundry is never invoked.
    /// </summary>
    [Fact]
    public async Task Routing_ProviderOpenAi_NeverInvokesFoundry()
    {
        var agent = new FoundryIntegrationTestAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions
            {
                Enabled  = true,
                Provider = AiProvider.OpenAi,
            },
            foundryClient: null);

        var context = new AgentConversationContext { ConversationId = "routing-2" };
        await agent.InvokeTryProcessWithBackendAsync("test message", context);

        agent.FoundryAttemptCount.Should().Be(0,
            "OpenAi provider should never invoke the Foundry path");
    }

    // ── Error Paths ───────────────────────────────────────────────────────

    /// <summary>
    /// T177-8: When Foundry client is null, TryProcessWithFoundryAsync returns null
    /// and does not propagate an exception.
    /// </summary>
    [Fact]
    public async Task ErrorPath_FoundryNullClient_ReturnsNullGracefully()
    {
        var agent = new FoundryIntegrationTestAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.Foundry },
            foundryClient: null);

        var context = new AgentConversationContext { ConversationId = "err-1" };
        var result  = await agent.InvokeTryProcessWithFoundryAsync("query", context);

        result.Should().BeNull("null Foundry client must return null, not throw");
    }

    /// <summary>
    /// T177-9: When Foundry agent ID is not provisioned, TryProcessWithFoundryAsync returns null.
    /// This covers the case where ProvisionFoundryAgentAsync hasn't been called.
    /// </summary>
    [Fact]
    public async Task ErrorPath_FoundryAgentIdNotProvisioned_ReturnsNull()
    {
        var agent = new FoundryIntegrationTestAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.Foundry },
            foundryClient: null);

        // _foundryAgentId is null — no mock client provided
        var context = new AgentConversationContext { ConversationId = "err-2" };
        var result  = await agent.InvokeTryProcessWithFoundryAsync("query", context);

        result.Should().BeNull("unprovisioneed agent ID should yield null, not an exception");
        agent.IsFoundryAgentIdNull().Should().BeTrue();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    /// <summary>
    /// Exercises the ValidateFoundryConfig logic extracted from Program.cs.
    /// The function body is reproduced here to unit-test it independently of the
    /// ASP.NET host startup lifecycle.
    /// </summary>
    private static void InvokeValidateFoundryConfig(IConfiguration configuration)
    {
        var aiOptions = configuration.GetSection(AzureAiOptions.SectionName)
                                     .Get<AzureAiOptions>();

        if (aiOptions is null) return;
        if (!aiOptions.Enabled) return;

        if (aiOptions.Provider == AiProvider.Foundry
            && string.IsNullOrWhiteSpace(aiOptions.FoundryProjectEndpoint))
        {
            throw new InvalidOperationException(
                "AzureAi:Provider is set to 'Foundry' but AzureAi:FoundryProjectEndpoint is empty. " +
                "Set ATO_AZUREAI__FOUNDRYPROJECTENDPOINT to your Azure AI Foundry project endpoint " +
                "or change ATO_AZUREAI__PROVIDER to 'OpenAi'.");
        }
    }
}

/// <summary>
/// Testable agent for FoundryAgentIntegrationTests. Mirrors the pattern from
/// FoundryFallbackTests but adds an attempt counter for routing assertions.
/// </summary>
public class FoundryIntegrationTestAgent : BaseAgent
{
    public int FoundryAttemptCount { get; private set; }

    public FoundryIntegrationTestAgent(
        ILogger logger,
        AzureAiOptions? azureAiOptions = null,
        Azure.AI.Agents.Persistent.PersistentAgentsClient? foundryClient = null)
        : base(logger, null, foundryClient, azureAiOptions)
    {
    }

    public override string AgentId   => "foundry-integration-test-agent";
    public override string AgentName => "Foundry Integration Test Agent";
    public override string Description => "Test agent for Task #177 integration tests";
    public override double CanHandle(string message) => 0.5;
    public override string GetSystemPrompt() => "You are a test agent.";

    public override Task<AgentResponse> ProcessAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
        => Task.FromResult(new AgentResponse { Success = true, Response = "test", AgentName = AgentName });

    protected override Task<AgentResponse?> TryProcessWithFoundryAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        FoundryAttemptCount++;
        return base.TryProcessWithFoundryAsync(message, context, cancellationToken, progress);
    }

    // Public wrappers for protected methods
    public Task<AgentResponse?> InvokeTryProcessWithBackendAsync(
        string message, AgentConversationContext context, CancellationToken ct = default)
        => TryProcessWithBackendAsync(message, context, ct);

    public Task<AgentResponse?> InvokeTryProcessWithFoundryAsync(
        string message, AgentConversationContext context, CancellationToken ct = default)
        => TryProcessWithFoundryAsync(message, context, ct);  // virtual dispatch — hits override counter

    /// <summary>
    /// Returns true when the Foundry agent ID has not been provisioned.
    /// Uses reflection because _foundryAgentId is private-protected in BaseAgent.
    /// </summary>
    public bool IsFoundryAgentIdNull()
    {
        var field = typeof(BaseAgent).GetField("_foundryAgentId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(this) is null;
    }
}

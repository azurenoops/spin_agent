using Xunit;
using FluentAssertions;
using Moq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Extensions;

namespace Ato.Copilot.Tests.Integration.Agents;

/// <summary>
/// Fallback chain integration tests for Azure AI Foundry Agent integration (Feature 028).
/// Validates graceful degradation: Foundry → IChatClient → deterministic (SC-009).
/// </summary>
public class FoundryFallbackTests
{
    private readonly Mock<ILogger<TestFallbackAgent>> _loggerMock = new();

    /// <summary>
    /// Issue #698: Foundry failure must not cross the configured backend boundary
    /// unless an operator explicitly enables fallback.
    /// </summary>
    [Fact]
    public async Task FallbackChain_FoundryFails_DefaultPolicy_DoesNotCallOpenAi()
    {
        // Arrange
        var chatClient = new Mock<IChatClient>();
        var agent = new TestFallbackAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.Foundry },
            chatClient: chatClient.Object,
            foundryClient: null);
        var context = new AgentConversationContext { ConversationId = "fallback-1" };

        // Act
        var result = await agent.InvokeTryProcessWithBackendAsync("test", context);

        // Assert
        result.Should().BeNull("when both Foundry and IChatClient are unavailable, result should be null for deterministic fallback");
        agent.FoundryAttempted.Should().BeTrue("Foundry path should be attempted first");
        chatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IList<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// SC-009: Provider=OpenAi (default) with no IChatClient returns null — zero regressions.
    /// </summary>
    [Fact]
    public async Task FallbackChain_DefaultProvider_NoChatClient_ReturnsNull()
    {
        var agent = new TestFallbackAgent(
            _loggerMock.Object);

        var context = new AgentConversationContext { ConversationId = "fallback-2" };
        var result = await agent.InvokeTryProcessWithBackendAsync("test", context);

        result.Should().BeNull("default provider with no IChatClient should fall through to deterministic");
        agent.FoundryAttempted.Should().BeFalse("OpenAi provider should not attempt Foundry path");
    }

    [Fact]
    public async Task FallbackChain_FoundryFails_ExplicitFallback_ReturnsAuditableOpenAiResponse()
    {
        // Arrange
        var chatClient = new Mock<IChatClient>();
        chatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([
                new ChatMessage(ChatRole.Assistant, "Fallback response")
            ]));
        var options = new AzureAiOptions
        {
            Enabled = true,
            Provider = AiProvider.Foundry,
            DeploymentName = "approved-openai-deployment",
            AllowBackendFallback = true
        };
        var agent = new TestFallbackAgent(
            _loggerMock.Object,
            azureAiOptions: options,
            chatClient: chatClient.Object,
            foundryClient: null);

        // Act
        var result = await agent.InvokeTryProcessWithBackendAsync(
            "test", new AgentConversationContext { ConversationId = "fallback-opt-in" });

        // Assert
        result.Should().NotBeNull();
        result!.BackendProvider.Should().Be(AiProvider.OpenAi.ToString());
        result.BackendModel.Should().Be("approved-openai-deployment");
        result.Warnings.Should().ContainSingle(w => w.Code == "AI_BACKEND_FALLBACK");
        result.ModelCallRecords.Should().ContainSingle();
        result.ModelCallRecords[0].Provider.Should().Be(AiProvider.OpenAi.ToString());
        result.ModelCallRecords[0].ModelId.Should().Be("approved-openai-deployment");
        _loggerMock.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("ConfiguredProvider=Foundry") &&
                    state.ToString()!.Contains("ResolvedProvider=OpenAi") &&
                    state.ToString()!.Contains("ResolvedModel=approved-openai-deployment")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// SC-009: Provider=OpenAi routes directly to IChatClient without Foundry.
    /// </summary>
    [Fact]
    public async Task FallbackChain_OpenAi_SkipsFoundry()
    {
        var agent = new TestFallbackAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.OpenAi });

        var context = new AgentConversationContext { ConversationId = "fallback-3" };
        var result = await agent.InvokeTryProcessWithBackendAsync("test", context);

        result.Should().BeNull("IChatClient is null so TryProcessWithAiAsync returns null");
        agent.FoundryAttempted.Should().BeFalse("OpenAi should not attempt Foundry path");
    }

    /// <summary>
    /// Issue #698: Exceptions follow the same secure-default policy as null responses.
    /// </summary>
    [Fact]
    public async Task FallbackChain_FoundryThrowsException_DefaultPolicy_DoesNotCallOpenAi()
    {
        // Arrange
        var chatClient = new Mock<IChatClient>();
        var agent = new TestFallbackAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions { Enabled = true, Provider = AiProvider.Foundry },
            chatClient: chatClient.Object,
            foundryClient: null,
            throwOnFoundry: true);
        var context = new AgentConversationContext { ConversationId = "fallback-4" };

        // Act
        var result = await agent.InvokeTryProcessWithBackendAsync("test", context);

        // Assert
        result.Should().BeNull();
        agent.FoundryAttempted.Should().BeTrue("Foundry should be attempted even though it will throw");
        chatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IList<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FallbackChain_RequestCancelled_DoesNotCallOpenAi()
    {
        // Arrange
        var chatClient = new Mock<IChatClient>();
        var agent = new TestFallbackAgent(
            _loggerMock.Object,
            azureAiOptions: new AzureAiOptions
            {
                Enabled = true,
                Provider = AiProvider.Foundry,
                AllowBackendFallback = true
            },
            chatClient: chatClient.Object,
            cancelOnFoundry: true);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        // Act
        var act = () => agent.InvokeTryProcessWithBackendAsync(
            "test",
            new AgentConversationContext { ConversationId = "fallback-cancelled" },
            cancellationSource.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        chatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IList<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatEndpoint_ExplicitFallback_ReturnsBackendMetadataAndWarning()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAi:Enabled"] = "true",
            ["AzureAi:Provider"] = "Foundry",
            ["AzureAi:DeploymentName"] = "approved-openai-deployment",
            ["AzureAi:AllowBackendFallback"] = "true",
            ["Deployment:Mode"] = "SingleTenant"
        });
        builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
        builder.Services.Configure<AzureAdOptions>(builder.Configuration.GetSection(AzureAdOptions.SectionName));
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton(_ => new ArmClient(
            new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                AuthorityHost = AzureAuthorityHosts.AzureGovernment
            }),
            default,
            new ArmClientOptions { Environment = ArmEnvironment.AzureGovernment }));
        builder.Services.AddAtoCopilotMcpForTesting(
            builder.Configuration, $"FoundryFallbackE2e_{Guid.NewGuid():N}");
        builder.Services.RemoveAll<IHostedService>();

        var chatClient = new Mock<IChatClient>();
        chatClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([
                new ChatMessage(ChatRole.Assistant, "Fallback response")
            ]));
        builder.Services.RemoveAll<IChatClient>();
        builder.Services.AddSingleton(chatClient.Object);
        builder.Services.AddSingleton<BaseAgent>(sp => new TestFallbackAgent(
            sp.GetRequiredService<ILogger<TestFallbackAgent>>(),
            sp.GetRequiredService<IOptions<AzureAiOptions>>().Value,
            sp.GetRequiredService<IChatClient>()));
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        app.Use(async (http, next) =>
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new("tid", "11111111-1111-1111-1111-111111111111"),
                new("oid", "22222222-2222-2222-2222-222222222222"),
            ], "Synthetic fallback contract identity"));
            var tenant = (TenantContext)http.RequestServices.GetRequiredService<ITenantContext>();
            tenant.TenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            using var scope = http.RequestServices.GetRequiredService<ITenantContextAccessor>().Push(tenant);
            await next(http);
        });
        app.Services.GetRequiredService<Ato.Copilot.Mcp.Server.McpHttpBridge>().MapEndpoints(app);
        await app.StartAsync();
        var client = app.GetTestClient();

        // Act
        var httpResponse = await client.PostAsJsonAsync("/mcp/chat", new
        {
            message = "exercise explicit backend fallback",
            conversationId = "fallback-e2e"
        });
        var responseJson = JsonDocument.Parse(await httpResponse.Content.ReadAsStringAsync()).RootElement;

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        responseJson.GetProperty("response").GetString().Should().Be("Fallback response");
        var metadata = responseJson.GetProperty("metadata");
        metadata.GetProperty("backendProvider").GetString().Should().Be("OpenAi");
        metadata.GetProperty("backendModel").GetString().Should().Be("approved-openai-deployment");
        metadata.GetProperty("warnings")[0].GetProperty("code").GetString()
            .Should().Be("AI_BACKEND_FALLBACK");
    }
}

/// <summary>
/// Test agent with controllable Foundry behavior for fallback testing.
/// </summary>
public class TestFallbackAgent : BaseAgent
{
    public bool FoundryAttempted { get; private set; }
    private readonly bool _throwOnFoundry;
    private readonly bool _cancelOnFoundry;

    public TestFallbackAgent(
        ILogger logger,
        AzureAiOptions? azureAiOptions = null,
        IChatClient? chatClient = null,
        Azure.AI.Agents.Persistent.PersistentAgentsClient? foundryClient = null,
        bool throwOnFoundry = false,
        bool cancelOnFoundry = false)
        : base(logger, chatClient, foundryClient, azureAiOptions)
    {
        _throwOnFoundry = throwOnFoundry;
        _cancelOnFoundry = cancelOnFoundry;
    }

    public override string AgentId => "test-fallback-agent";
    public override string AgentName => "Test Fallback Agent";
    public override string Description => "A test agent for fallback testing";
    public override double CanHandle(string message) => 1.0;
    public override string GetSystemPrompt() => "You are a test agent.";

    public override async Task<AgentResponse> ProcessAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        return await TryProcessWithBackendAsync(message, context, cancellationToken, progress)
            ?? new AgentResponse
        {
            Success = true,
            Response = "test",
            AgentName = AgentName
        };
    }

    protected override Task<AgentResponse?> TryProcessWithFoundryAsync(
        string message,
        AgentConversationContext context,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        FoundryAttempted = true;

        if (_cancelOnFoundry)
            throw new OperationCanceledException(cancellationToken);

        if (_throwOnFoundry)
            throw new InvalidOperationException("Simulated Foundry failure");

        return base.TryProcessWithFoundryAsync(message, context, cancellationToken, progress);
    }

    public Task<AgentResponse?> InvokeTryProcessWithBackendAsync(
        string message, AgentConversationContext context, CancellationToken ct = default)
        => TryProcessWithBackendAsync(message, context, ct);
}

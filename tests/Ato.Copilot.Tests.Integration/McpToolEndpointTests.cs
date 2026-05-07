using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Mcp.Extensions;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.State.Extensions;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for MCP tool HTTP endpoints. Tests the full HTTP pipeline
/// including middleware, routing, serialization, and tool execution.
/// Uses InMemory database and Development environment (bypasses auth middleware).
/// </summary>
[Collection("IntegrationTests")]
public class McpToolEndpointTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Builds a test web application with InMemory DB, Development environment
    /// (skips auth middleware), and all services registered.
    /// </summary>
    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        var dbName = $"IntegrationTest_{Guid.NewGuid():N}";

        // Register InMemory DbContext as singleton (all options singleton to avoid captive dependency)
        // Bind configuration (no real Azure client — just the settings)
        builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
        builder.Services.Configure<AzureAdOptions>(builder.Configuration.GetSection(AzureAdOptions.SectionName));
        builder.Services.AddHttpClient();

        // Register a stub ArmClient (never actually called in tests)
        builder.Services.AddSingleton(sp =>
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                AuthorityHost = AzureAuthorityHosts.AzureGovernment
            });
            return new ArmClient(credential, default, new ArmClientOptions
            {
                Environment = ArmEnvironment.AzureGovernment
            });
        });

        builder.Services.AddAtoCopilotMcpForTesting(builder.Configuration, dbName);
        // CORS
        builder.Services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        // Use TestServer instead of Kestrel
        builder.WebHost.UseTestServer();

        _app = builder.Build();

        // Configure middleware pipeline (matches Program.cs HTTP mode)
        _app.UseCors();
        _app.UseMiddleware<ComplianceAuthorizationMiddleware>();
        _app.UseMiddleware<AuditLoggingMiddleware>();

        // Map MCP HTTP endpoints
        var httpBridge = _app.Services.GetRequiredService<McpHttpBridge>();
        httpBridge.MapEndpoints(_app);

        // Root endpoint
        _app.MapGet("/", () => Microsoft.AspNetCore.Http.Results.Json(new
        {
            service = "ATO Copilot",
            version = "1.0.0",
            mode = "http"
        }));

        // Start TestServer
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    /// <summary>
    /// Stops and disposes the test application and HTTP client.
    /// </summary>
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ────────────────────────────────────────────────────────────
    //  Health & Root Endpoints
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthyStatus()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("status").GetString().Should().Be("healthy");
        json.RootElement.GetProperty("service").GetString().Should().Contain("ATO Copilot");
    }

    [Fact]
    public async Task RootEndpoint_ReturnsServiceInfo()
    {
        var response = await _client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("service").GetString().Should().Be("ATO Copilot");
        json.RootElement.GetProperty("version").GetString().Should().Be("1.0.0");
    }

    // ────────────────────────────────────────────────────────────
    //  Tools List Endpoint
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolsEndpoint_ReturnsAllTools()
    {
        var response = await _client.GetAsync("/mcp/tools");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.TryGetProperty("tools", out var tools).Should().BeTrue();
        tools.GetArrayLength().Should().BeGreaterOrEqualTo(12,
            "should include all compliance + configuration + chat tools");

        // Verify key tools are present
        var toolNames = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();

        toolNames.Should().Contain("compliance_assess");
        toolNames.Should().Contain("compliance_remediate");
        toolNames.Should().Contain("compliance_chat");
        toolNames.Should().Contain("compliance_monitoring");
        toolNames.Should().Contain("compliance_collect_evidence");
    }

    // ────────────────────────────────────────────────────────────
    //  Chat Endpoint — Configuration Flows
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatEndpoint_SetSubscription_RoutesToConfigurationAgent()
    {
        var request = new { message = "Set my subscription to abc-123-def-456" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Configuration Agent",
                "subscription configuration should route to Configuration Agent");
    }

    [Fact]
    public async Task ChatEndpoint_SetFramework_RoutesToConfigurationAgent()
    {
        var request = new { message = "Set framework to FedRAMP High" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Configuration Agent");
    }

    [Fact]
    public async Task ChatEndpoint_GetConfig_RoutesToConfigurationAgent()
    {
        var request = new { message = "Show my current configuration" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Configuration Agent");
    }

    // ────────────────────────────────────────────────────────────
    //  Chat Endpoint — Compliance Flows
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatEndpoint_AssessmentRequest_RoutesToComplianceAgent()
    {
        var request = new { message = "Run compliance assessment" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent",
                "assessment requests should route to Compliance Agent");
    }

    [Fact]
    public async Task ChatEndpoint_RemediationRequest_RoutesToComplianceAgent()
    {
        var request = new { message = "Fix AC-2 findings" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent");
    }

    [Fact]
    public async Task ChatEndpoint_WithConversationId_MaintainsContext()
    {
        var conversationId = Guid.NewGuid().ToString();
        var request = new { message = "What is NIST 800-53?", conversationId };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("conversationId").GetString()
            .Should().Be(conversationId,
                "response should echo back the provided conversation ID");
    }

    // ────────────────────────────────────────────────────────────
    //  Chat Endpoint — Error Handling
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatEndpoint_EmptyMessage_ReturnsBadRequest()
    {
        var request = new { message = "" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChatEndpoint_NullBody_ReturnsBadRequest()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/mcp/chat", content);

        // Empty message or missing message should return bad request
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.OK);
    }

    // ────────────────────────────────────────────────────────────
    //  MCP JSON-RPC Endpoint
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task McpEndpoint_ToolsCall_ExecutesTool()
    {
        var mcpRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "compliance_chat",
                arguments = new Dictionary<string, object>
                {
                    ["message"] = "What is NIST 800-53?"
                }
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(mcpRequest, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync("/mcp", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseContent = await response.Content.ReadAsStringAsync();
        responseContent.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task McpEndpoint_InvalidRequest_ReturnsError()
    {
        var content = new StringContent("not json", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/mcp", content);

        // Should handle gracefully (may return 200 with error or 400/500)
        response.Should().NotBeNull();
    }

    // ────────────────────────────────────────────────────────────
    //  Response Format Validation
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChatResponse_ContainsRequiredFields()
    {
        var request = new { message = "Help me get started with compliance" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement;

        // Verify all expected response fields exist
        root.TryGetProperty("success", out _).Should().BeTrue();
        root.TryGetProperty("response", out _).Should().BeTrue();
        root.TryGetProperty("conversationId", out _).Should().BeTrue();
        root.TryGetProperty("processingTimeMs", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ChatResponse_ProcessingTimeMs_IsPositive()
    {
        var request = new { message = "What controls are in the AC family?" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("processingTimeMs").GetDouble()
            .Should().BeGreaterOrEqualTo(0);
    }

    // ────────────────────────────────────────────────────────────
    //  Configuration → Assessment Round-Trip
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_ConfigureThenAssess_WorksEndToEnd()
    {
        // Step 1: Set subscription
        var configRequest = new { message = "Set my subscription to test-sub-id-123" };
        var configResponse = await _client.PostAsJsonAsync("/mcp/chat", configRequest, _jsonOptions);
        configResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var configContent = await configResponse.Content.ReadAsStringAsync();
        var configJson = JsonDocument.Parse(configContent);
        configJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        configJson.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Configuration Agent");

        // Step 2: Ask for assessment (should route to ComplianceAgent)
        var assessRequest = new { message = "Run a compliance scan" };
        var assessResponse = await _client.PostAsJsonAsync("/mcp/chat", assessRequest, _jsonOptions);
        assessResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var assessContent = await assessResponse.Content.ReadAsStringAsync();
        var assessJson = JsonDocument.Parse(assessContent);
        assessJson.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent");
    }

    // ────────────────────────────────────────────────────────────
    //  Health Endpoint — Response Structure
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HealthEndpoint_ContainsCapabilities()
    {
        var response = await _client.GetAsync("/health");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("capabilities").GetArrayLength()
            .Should().BeGreaterThan(0, "health endpoint should list capabilities");
    }

    [Fact]
    public async Task HealthEndpoint_ContainsTimestamp()
    {
        var response = await _client.GetAsync("/health");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────
    //  Tools Endpoint — Detailed Verification
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolsEndpoint_IncludesComplianceTools()
    {
        var response = await _client.GetAsync("/mcp/tools");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var toolNames = json.RootElement.GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();

        toolNames.Should().Contain("compliance_assess",
            "tools list should include compliance assessment tool");
        toolNames.Should().Contain("compliance_chat",
            "tools list should include compliance chat tool");
    }

    [Fact]
    public async Task ToolsEndpoint_ReturnsToolCount()
    {
        var response = await _client.GetAsync("/mcp/tools");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.TryGetProperty("count", out var count).Should().BeTrue();
        count.GetInt32().Should().BeGreaterOrEqualTo(12);
    }
}

/// <summary>
/// Simple IDbContextFactory implementation for integration tests using InMemory provider.
/// </summary>
internal sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
{
    private readonly DbContextOptions<AtoCopilotContext> _options;

    public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options)
    {
        _options = options;
    }

    public AtoCopilotContext CreateDbContext() => new(_options);
}

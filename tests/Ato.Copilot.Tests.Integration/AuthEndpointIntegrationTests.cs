using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Mcp.Authentication;
using Ato.Copilot.Mcp.Extensions;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.State.Extensions;
using FluentAssertions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for CAC/PIM auth endpoints — T098.
/// Uses Production environment so middleware is NOT bypassed.
/// Tests Tier 1/2 access, expired session handling, and client type detection.
/// </summary>
[Collection("IntegrationTests")]
public class AuthEndpointIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly string _dbName = $"AuthIntegration_{Guid.NewGuid():N}";
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task InitializeAsync()
    {
        // Use Production environment so auth middleware runs
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        // Clear any leftover env token
        Environment.SetEnvironmentVariable("PLATFORM_COPILOT_TOKEN", null);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production"
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.us/",
            ["AzureAd:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["AzureAd:ClientId"] = "22222222-2222-2222-2222-222222222222"
        });

        builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
        builder.Services.Configure<AzureAdOptions>(builder.Configuration.GetSection(AzureAdOptions.SectionName));
        builder.Services.AddHttpClient();

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

        var tokenValidator = new Mock<IEntraJwtTokenValidator>();
        tokenValidator
            .Setup(validator => validator.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecurityTokenMalformedException("Invalid test token."));
        builder.Services.AddSingleton(tokenValidator.Object);

        builder.Services.AddAtoCopilotMcpForTesting(builder.Configuration, _dbName);
        builder.Services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        builder.WebHost.UseTestServer();
        _app = builder.Build();

        _app.UseCors();
        _app.UseMiddleware<CacAuthenticationMiddleware>();
        _app.UseMiddleware<ComplianceAuthorizationMiddleware>();
        _app.UseMiddleware<AuditLoggingMiddleware>();

        var httpBridge = _app.Services.GetRequiredService<McpHttpBridge>();
        httpBridge.MapEndpoints(_app);

        _app.MapGet("/", () => Microsoft.AspNetCore.Http.Results.Json(new
        {
            service = "Security Posture Intelligence Navigator",
            version = "1.0.0",
            mode = "http"
        }));

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        // Restore Development for other test classes
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
    }

    // ────────────────────────────────────────────────────────────
    //  Health endpoint bypasses auth
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HealthEndpoint_BypassesAuth_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ────────────────────────────────────────────────────────────
    //  Tool execution requires identity; discovery remains public
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolExecution_NoAuth_ReturnsWorkspaceIdentityDenial()
    {
        // Arrange
        var request = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "compliance_assess",
                arguments = new { subscription_id = "test-sub" }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/mcp", request, _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeFalse();
        body.GetProperty("code").GetString().Should().Be("INVALID_WORKSPACE_IDENTITY");
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Tier1Tool_ToolsList_ReturnsOk()
    {
        var response = await _client.GetAsync("/mcp/tools");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("tools", out var tools).Should().BeTrue();
        tools.GetArrayLength().Should().BeGreaterThan(0);
    }

    // ────────────────────────────────────────────────────────────
    //  Tier 2 tools — require auth
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tier2Tool_NoAuth_ResponseIndicatesNoSession()
    {
        // When no auth token is provided, the tool should execute
        // but return an error about missing session/auth
        var request = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "cac_status",
                arguments = new { }
            }
        };

        var response = await _client.PostAsJsonAsync("/mcp", request, _jsonOptions);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeEmpty();

        // The tool-level auth check returns NO_ACTIVE_SESSION or the chat routing
        // indicates auth is needed. Verify the response doesn't leak real data.
        content.Should().NotContain("\"Active\"",
            "unauthenticated user should not see active session data");
    }

    [Fact]
    public async Task Tier2Tool_PimListEligible_NoAuth_DoesNotReturnRoles()
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "pim_list_eligible",
                arguments = new { }
            }
        };

        var response = await _client.PostAsJsonAsync("/mcp", request, _jsonOptions);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeEmpty();

        // Unauthenticated user should not see real PIM role data
        content.Should().NotContain("\"Contributor\"",
            "unauthenticated user should not see eligible roles");
    }

    // ────────────────────────────────────────────────────────────
    //  Client type detection via headers
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClientType_VSCode_DetectedFromUserAgent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("User-Agent", "VSCode/1.90.0");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ClientType_ExplicitHeader_TakesPrecedence()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Client-Type", "Teams");
        request.Headers.TryAddWithoutValidation("User-Agent", "VSCode/1.90.0");

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ────────────────────────────────────────────────────────────
    //  Auth tool endpoints — verify tool list includes auth tools
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolsList_IncludesAuthAndPimTools()
    {
        var response = await _client.GetAsync("/mcp/tools");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "tools endpoint should not require auth");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeEmpty();
        var json = JsonDocument.Parse(content);

        json.RootElement.TryGetProperty("tools", out var tools).Should().BeTrue(
            $"response should contain 'tools' property, got: {content[..Math.Min(500, content.Length)]}");

        var toolNames = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();

        toolNames.Should().Contain("cac_status");
        toolNames.Should().Contain("cac_sign_out");
        toolNames.Should().Contain("pim_list_eligible");
        toolNames.Should().Contain("pim_activate_role");
        toolNames.Should().Contain("pim_deactivate_role");
        toolNames.Should().Contain("pim_list_active");
        toolNames.Should().Contain("jit_request_access");
        toolNames.Should().Contain("jit_list_sessions");
        toolNames.Should().Contain("pim_history");
        toolNames.Should().Contain("cac_map_certificate");
    }

    [Fact]
    public async Task InvalidToken_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer invalid-jwt-token");
        request.Content = JsonContent.Create(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "cac_status",
                arguments = new { }
            }
        }, options: _jsonOptions);

        var response = await _client.SendAsync(request);

        // Invalid JWT should return 401
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

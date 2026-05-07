using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// Integration tests for CAC simulation mode (Feature 027).
/// Validates that simulation mode injects a simulated ClaimsPrincipal
/// through the full middleware pipeline so CAC-protected workflows succeed
/// without physical smart card hardware.
/// </summary>
[Collection("IntegrationTests")]
public class SimulationModeIssoIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly string _dbName = $"SimISSO_{Guid.NewGuid():N}";
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
        builder.Services.Configure<AzureAdOptions>(builder.Configuration.GetSection(AzureAdOptions.SectionName));

        // Configure CAC simulation mode with ISSO persona
        builder.Services.Configure<CacAuthOptions>(o =>
        {
            o.SimulationMode = true;
            o.SimulatedIdentity = new SimulatedIdentityOptions
            {
                UserPrincipalName = "isso.test@dev.mil",
                DisplayName = "Test ISSO (Simulated)",
                CertificateThumbprint = "ISSO_THUMB_001",
                Roles = ["ISSO", "Global Reader"]
            };
        });

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
            service = "ATO Copilot",
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
    }

    [Fact]
    public async Task IssoPersona_ProtectedEndpoint_SucceedsWithSimulatedIdentity()
    {
        // Invoke a Tier 1 tool via MCP — should succeed with simulated ISSO identity
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

        var response = await _client.PostAsJsonAsync("/mcp", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IssoPersona_ToolsList_SucceedsWithSimulatedIdentity()
    {
        var response = await _client.GetAsync("/mcp/tools");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("tools", out var tools).Should().BeTrue();
        tools.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task IssoPersona_HealthEndpoint_BypassesAuth()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>
/// Integration tests for simulation mode with Platform Engineer persona.
/// Verifies a different identity configuration works without app rebuild.
/// </summary>
[Collection("IntegrationTests")]
public class SimulationModeEngineerIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly string _dbName = $"SimEngineer_{Guid.NewGuid():N}";
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
        builder.Services.Configure<AzureAdOptions>(builder.Configuration.GetSection(AzureAdOptions.SectionName));

        // Configure CAC simulation mode with Platform Engineer persona
        builder.Services.Configure<CacAuthOptions>(o =>
        {
            o.SimulationMode = true;
            o.SimulatedIdentity = new SimulatedIdentityOptions
            {
                UserPrincipalName = "engineer.test@dev.mil",
                DisplayName = "Test Engineer (Simulated)",
                Roles = ["Platform Engineer"]
            };
        });

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
            service = "ATO Copilot",
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
    }

    [Fact]
    public async Task EngineerPersona_ProtectedEndpoint_SucceedsWithSimulatedIdentity()
    {
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

        var response = await _client.PostAsJsonAsync("/mcp", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EngineerPersona_ToolsList_SucceedsWithSimulatedIdentity()
    {
        var response = await _client.GetAsync("/mcp/tools");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EngineerPersona_NoCertificateThumbprint_StillSucceeds()
    {
        // Engineer persona has no CertificateThumbprint configured — should still work
        var response = await _client.GetAsync("/mcp/tools");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
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
    private ClaimsPrincipal? _requestIdentity;
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
        builder.Configuration["Deployment:Mode"] = "SingleTenant";
        builder.Configuration["CacAuth:SimulationMode"] = "false";
        // Apply the fixture persona after the shared graph binds deployment defaults.
        builder.Services.PostConfigure<CacAuthOptions>(o =>
        {
            o.SimulationMode = true;
            o.SimulatedIdentity = new SimulatedIdentityOptions
            {
                UserPrincipalName = "isso.test@dev.mil",
                TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
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
        _app.Use(async (http, next) =>
        {
            _requestIdentity = http.User;
            using var tenantScope = IntegrationTestServiceExtensions.BindSingleTenantContext(
                http, Guid.Parse("33333333-3333-3333-3333-333333333333"));
            await next(http);
        });
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
    }

    [Fact]
    public async Task IssoPersona_ProtectedEndpoint_SucceedsWithSimulatedIdentity()
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
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var principal = _requestIdentity.Should().BeOfType<ClaimsPrincipal>().Which;
        var identity = principal.Identity.Should().BeOfType<ClaimsIdentity>().Which;
        identity.IsAuthenticated.Should().BeTrue();
        identity.AuthenticationType.Should().Be("Simulated");
        principal.FindFirstValue("tid").Should().Be("11111111-1111-1111-1111-111111111111");
        principal.FindFirstValue("oid").Should().Be("22222222-2222-2222-2222-222222222222");
        principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value)
            .Should().BeEquivalentTo("ISSO", "Global Reader");
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
    private ClaimsPrincipal? _requestIdentity;
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
        builder.Configuration["Deployment:Mode"] = "SingleTenant";
        builder.Configuration["CacAuth:SimulationMode"] = "false";
        // Apply the fixture persona after the shared graph binds deployment defaults.
        builder.Services.PostConfigure<CacAuthOptions>(o =>
        {
            o.SimulationMode = true;
            o.SimulatedIdentity = new SimulatedIdentityOptions
            {
                UserPrincipalName = "engineer.test@dev.mil",
                TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ObjectId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
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
        _app.Use(async (http, next) =>
        {
            _requestIdentity = http.User;
            using var tenantScope = IntegrationTestServiceExtensions.BindSingleTenantContext(
                http, Guid.Parse("33333333-3333-3333-3333-333333333333"));
            await next(http);
        });
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
    }

    [Fact]
    public async Task EngineerPersona_ProtectedEndpoint_SucceedsWithSimulatedIdentity()
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
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var principal = _requestIdentity.Should().BeOfType<ClaimsPrincipal>().Which;
        var identity = principal.Identity.Should().BeOfType<ClaimsIdentity>().Which;
        identity.IsAuthenticated.Should().BeTrue();
        identity.AuthenticationType.Should().Be("Simulated");
        principal.FindFirstValue("tid").Should().Be("11111111-1111-1111-1111-111111111111");
        principal.FindFirstValue("oid").Should().Be("44444444-4444-4444-4444-444444444444");
        principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value)
            .Should().BeEquivalentTo("Platform Engineer");
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

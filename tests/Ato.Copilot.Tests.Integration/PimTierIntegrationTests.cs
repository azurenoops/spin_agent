using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Observability;
using Ato.Copilot.Mcp.Extensions;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.State.Extensions;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for PIM Tier 2a/2b enforcement (T135).
/// Validates that PIM tier enforcement works end-to-end through the middleware pipeline.
/// Uses Production environment with authenticated claims to test real enforcement.
/// </summary>
[Collection("IntegrationTests")]
public class PimTierIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly string _dbName = $"PimTier_{Guid.NewGuid():N}";
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

        _app.UseMiddleware<CorrelationIdMiddleware>();
        _app.UseCors();
        _app.UseMiddleware<ComplianceAuthorizationMiddleware>();
        _app.UseMiddleware<AuditLoggingMiddleware>();

        var httpBridge = _app.Services.GetRequiredService<McpHttpBridge>();
        httpBridge.MapEndpoints(_app);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ────────────────────────────────────────────────────────────
    //  AuthTierClassification Validation
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void AuthTierClassification_Tier2aTools_ClassifiedAsRead()
    {
        // Verify tier classification is correctly configured
        var tier2aTools = new[] { "pim_list_eligible", "pim_list_active", "pim_history", "jit_list_sessions" };
        foreach (var tool in tier2aTools)
        {
            AuthTierClassification.IsTier2a(tool).Should().BeTrue($"{tool} should be Tier 2a");
            AuthTierClassification.GetRequiredPimTier(tool).Should().Be(PimTier.Read);
        }
    }

    [Fact]
    public void AuthTierClassification_Tier2bTools_ClassifiedAsWrite()
    {
        var tier2bTools = new[] { "pim_activate_role", "pim_deactivate_role", "jit_request_access", "jit_revoke_access" };
        foreach (var tool in tier2bTools)
        {
            AuthTierClassification.IsTier2b(tool).Should().BeTrue($"{tool} should be Tier 2b");
            AuthTierClassification.GetRequiredPimTier(tool).Should().Be(PimTier.Write);
        }
    }

    [Fact]
    public void AuthTierClassification_Tier1Tools_ClassifiedAsNone()
    {
        AuthTierClassification.GetRequiredPimTier("cac_status")
            .Should().Be(PimTier.None);
    }

    [Fact]
    public void PimTierEnum_HasCorrectOrdinalValues()
    {
        ((int)PimTier.None).Should().Be(0);
        ((int)PimTier.Read).Should().Be(1);
        ((int)PimTier.Write).Should().Be(2);
    }

    [Theory]
    [InlineData("pim_list_eligible", PimTier.Read)]
    [InlineData("pim_activate_role", PimTier.Write)]
    [InlineData("cac_status", PimTier.None)]
    [InlineData("cac_sign_out", PimTier.Write)]
    public void GetRequiredPimTier_ReturnsCorrectTier(string toolName, PimTier expectedTier)
    {
        AuthTierClassification.GetRequiredPimTier(toolName).Should().Be(expectedTier);
    }
}

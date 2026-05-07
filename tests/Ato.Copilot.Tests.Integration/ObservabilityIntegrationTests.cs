using System.Net;
using System.Text.Json;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Observability;
using Ato.Copilot.Mcp.Extensions;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.State.Extensions;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for observability features (T136).
/// Validates health endpoint, correlation ID propagation, and metrics instrumentation.
/// </summary>
[Collection("IntegrationTests")]
public class ObservabilityIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly string _dbName = $"Observability_{Guid.NewGuid():N}";

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

        // Health checks (same as Program.cs)
        builder.Services.AddHealthChecks()
            .AddCheck<AgentHealthCheck>("compliance-agent");

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

        _app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                var agents = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description ?? string.Empty
                });
                var response = new
                {
                    status = report.Status.ToString(),
                    agents,
                    totalDurationMs = report.TotalDuration.TotalMilliseconds
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(response,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
        });

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
    //  Health Endpoint (FR-045 / SC-015)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HealthEndpoint_ReturnsJsonWithAgentStatus()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("status");
        json.Should().Contain("agents");
        json.Should().Contain("totalDurationMs");
    }

    [Fact]
    public async Task HealthEndpoint_RespondsWithinTwoSeconds()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await _client.GetAsync("/health");
        sw.Stop();

        response.IsSuccessStatusCode.Should().BeTrue();
        sw.Elapsed.TotalSeconds.Should().BeLessThan(2.0, "SC-015 requires < 2s");
    }

    [Fact]
    public async Task HealthEndpoint_ContainsValidJsonStructure()
    {
        var response = await _client.GetAsync("/health");
        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("status", out var statusProp).Should().BeTrue();
        statusProp.GetString().Should().NotBeNullOrWhiteSpace()
            .And.BeOneOf("Healthy", "Degraded", "Unhealthy", "healthy", "degraded", "unhealthy");

        root.TryGetProperty("agents", out var agentsProp).Should().BeTrue();
        agentsProp.ValueKind.Should().Be(JsonValueKind.Array);

        root.TryGetProperty("totalDurationMs", out var durationProp).Should().BeTrue();
        durationProp.GetDouble().Should().BeGreaterOrEqualTo(0);
    }

    // ────────────────────────────────────────────────────────────
    //  Correlation ID Propagation (FR-047)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CorrelationId_WithHeader_PropagatedToResponse()
    {
        const string correlationId = "test-correlation-integration-123";
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-ID", correlationId);

        var response = await _client.SendAsync(request);

        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values!.First().Should().Be(correlationId);
    }

    [Fact]
    public async Task CorrelationId_WithoutHeader_GeneratesNewGuid()
    {
        var response = await _client.GetAsync("/health");

        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        var correlationId = values!.First();
        Guid.TryParse(correlationId, out _).Should().BeTrue("auto-generated ID should be a valid GUID");
    }

    // ────────────────────────────────────────────────────────────
    //  ToolMetrics Instruments (FR-046)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ToolMetrics_InstrumentsRegistered()
    {
        ToolMetrics.ToolInvocations.Should().NotBeNull();
        ToolMetrics.ToolDurationMs.Should().NotBeNull();
        ToolMetrics.ToolErrors.Should().NotBeNull();
        ToolMetrics.ActiveSessions.Should().NotBeNull();
        ToolMetrics.MeterName.Should().Be("Ato.Copilot");
    }
}

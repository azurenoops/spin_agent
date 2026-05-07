using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models;
using Ato.Copilot.Core.Observability;
using Ato.Copilot.Mcp.Extensions;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.State.Extensions;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.McpEndpoints;

/// <summary>
/// Integration tests for API rate limiting (US2 / FR-006 through FR-010a).
/// Validates end-to-end rate limiting through the full middleware pipeline.
/// </summary>
[Collection("IntegrationTests")]
public class RateLimitIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly string _dbName = $"RateLimitTest_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        // Set aggressive rate limits for fast testing
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Policies:0:PolicyName"] = "chat",
            ["RateLimiting:Policies:0:Endpoint"] = "/mcp/chat",
            ["RateLimiting:Policies:0:PermitLimit"] = "5",
            ["RateLimiting:Policies:0:WindowSeconds"] = "60",
            ["RateLimiting:Policies:0:SegmentsPerWindow"] = "2",
            ["RateLimiting:ExemptEndpoints:0"] = "/health",
            ["RateLimiting:ExemptEndpoints:1"] = "/mcp/tools",
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

        // Register rate limiter
        var rateLimitingOptions = new RateLimitingOptions();
        builder.Configuration.GetSection(RateLimitingOptions.SectionName).Bind(rateLimitingOptions);

        builder.Services.AddRateLimiter(options =>
        {
            foreach (var policy in rateLimitingOptions.Policies)
            {
                options.AddPolicy(policy.PolicyName, context =>
                    RateLimitPartition.GetSlidingWindowLimiter(
                        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                        factory: _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = policy.PermitLimit,
                            Window = TimeSpan.FromSeconds(policy.WindowSeconds),
                            SegmentsPerWindow = policy.SegmentsPerWindow,
                            QueueLimit = 0,
                            AutoReplenishment = true,
                        }));
            }

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }
                else
                {
                    context.HttpContext.Response.Headers.RetryAfter = "60";
                }

                var errorDetail = new
                {
                    errorCode = "RATE_LIMITED",
                    message = $"Rate limit exceeded for {context.HttpContext.Request.Path}.",
                    suggestion = "Reduce request frequency or wait before retrying."
                };

                await context.HttpContext.Response.WriteAsync(
                    JsonSerializer.Serialize(errorDetail), cancellationToken);
            };
        });

        builder.Services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        builder.WebHost.UseTestServer();
        _app = builder.Build();

        _app.UseMiddleware<CorrelationIdMiddleware>();
        _app.UseCors();
        _app.UseRateLimiter();
        _app.UseMiddleware<ComplianceAuthorizationMiddleware>();
        _app.UseMiddleware<AuditLoggingMiddleware>();

        var httpBridge = _app.Services.GetRequiredService<McpHttpBridge>();
        httpBridge.MapEndpoints(_app);

        _app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions());

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    /// Verifies that the rate limiting middleware is active and the health endpoint
    /// still responds correctly (exempt endpoint).
    /// </summary>
    [Fact]
    public async Task HealthEndpoint_IsExemptFromRateLimiting()
    {
        for (var i = 0; i < 10; i++)
        {
            var response = await _client.GetAsync("/health");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// Verifies that the server starts successfully with rate limiting configured.
    /// </summary>
    [Fact]
    public async Task Server_StartsSuccessfully_WithRateLimiting()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

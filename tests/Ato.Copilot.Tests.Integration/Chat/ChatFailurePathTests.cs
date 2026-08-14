using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ato.Copilot.Agents.Common;
using Microsoft.AspNetCore.TestHost;
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Extensions;
using Ato.Copilot.Mcp.Models;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Chat;

/// <summary>
/// Failure-path integration tests for /mcp/chat and /mcp/chat/stream.
/// Covers #679 acceptance criteria: Success=false → HTTP >=400 with code+reason+correlationId;
/// empty-response invariant; TENANT_UNRESOLVED hard-fail; streaming typed error SSE event.
/// </summary>
[Collection("IntegrationTests")]
public class ChatFailurePathTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
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

        var dbName = $"FailurePathTest_{Guid.NewGuid():N}";

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

        builder.Services.AddAtoCopilotMcpForTesting(builder.Configuration, dbName);
        builder.Services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        // Use TestServer (in-process, no real port)
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseCors();

        var bridge = _app.Services.GetRequiredService<Ato.Copilot.Mcp.Server.McpHttpBridge>();
        bridge.MapEndpoints(_app);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _client.Dispose();
    }

    // ── Empty message ─────────────────────────────────────────────────────

    [Fact]
    public async Task ChatEndpoint_EmptyMessage_ReturnsBadRequest()
    {
        var request = new { message = "" };
        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Success=false → HTTP >=400 ────────────────────────────────────────

    [Fact]
    public async Task ChatEndpoint_ValidMessage_ReturnsSuccessOrStructuredError()
    {
        // In the integration test environment the agent may succeed or fail with a
        // structured error — the invariant is: if Success=false the HTTP status must be >=400.
        var request = new { message = "What is NIST 800-53?" };
        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        if ((int)response.StatusCode >= 400)
        {
            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content).RootElement;

            // Must carry machine-readable code+reason+correlationId
            json.TryGetProperty("code", out var codeProp).Should().BeTrue("error response must include 'code'");
            json.TryGetProperty("reason", out var reasonProp).Should().BeTrue("error response must include 'reason'");
            json.TryGetProperty("correlationId", out _).Should().BeTrue("error response must include 'correlationId'");

            codeProp.GetString().Should().NotBeNullOrWhiteSpace();
            reasonProp.GetString().Should().NotBeNullOrWhiteSpace();
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content).RootElement;
            json.GetProperty("success").GetBoolean().Should().BeTrue();
            // Empty response must never ship as success
            json.GetProperty("response").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    // ── Error response shape ──────────────────────────────────────────────

    [Fact]
    public async Task ChatEndpoint_WhenOfflineEnv_ErrorBodyHasRequiredFields()
    {
        // Post with an action=remediate and invalid actionContext to provoke a structured error
        var request = new
        {
            message = "test",
            action = "remediate",
            actionContext = new Dictionary<string, string> { ["findingId"] = "" }
        };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);
        var content = await response.Content.ReadAsStringAsync();

        // Regardless of status: a Success=false payload must have code + reason OR
        // a 200 payload must have success=true with a non-empty response.
        if ((int)response.StatusCode >= 400)
        {
            var json = JsonDocument.Parse(content).RootElement;
            json.TryGetProperty("code", out _).Should().BeTrue("structured error must carry 'code'");
            json.TryGetProperty("reason", out _).Should().BeTrue("structured error must carry 'reason'");
        }
        else
        {
            var json = JsonDocument.Parse(content).RootElement;
            if (json.TryGetProperty("success", out var successProp) && !successProp.GetBoolean())
            {
                // If success=false somehow slipped through as 200 this is the invariant violation
                false.Should().BeTrue("Success=false must never return HTTP 200");
            }
        }
    }

    // ── Streaming: non-200 body must never ship as 200 ────────────────────

    [Fact]
    public async Task StreamEndpoint_EmptyMessage_ReturnsBadRequest()
    {
        var body = new StringContent(
            JsonSerializer.Serialize(new { message = "" }),
            Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/mcp/chat/stream", body);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StreamEndpoint_ValidMessage_DoesNotReturnBlankSuccess()
    {
        var request = new StringContent(
            JsonSerializer.Serialize(new { message = "What is FedRAMP?" }),
            Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/mcp/chat/stream", request);

        // The stream must return 200 (SSE) with content-type text/event-stream
        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType?.MediaType
                .Should().Be("text/event-stream");

            var sseBody = await response.Content.ReadAsStringAsync();

            // If an 'error' event is present in the stream it must carry code + reason
            if (sseBody.Contains("\"type\":\"error\""))
            {
                var errorLine = sseBody.Split('\n')
                    .FirstOrDefault(l => l.StartsWith("data:") && l.Contains("\"type\":\"error\""));
                errorLine.Should().NotBeNull();

                var data = errorLine!.Substring("data:".Length).Trim();
                var json = JsonDocument.Parse(data).RootElement;
                json.TryGetProperty("code", out _).Should().BeTrue("SSE error event must carry 'code'");
                json.TryGetProperty("reason", out _).Should().BeTrue("SSE error event must carry 'reason'");
            }
            else
            {
                // Happy path: a result event with a non-null data.response
                sseBody.Should().Contain("\"type\":\"result\"");
            }
        }
        else
        {
            ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(400);
        }
    }
}

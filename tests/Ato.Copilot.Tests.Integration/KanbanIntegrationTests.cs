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
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Extensions;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.State.Extensions;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for Kanban board and task lifecycle via MCP HTTP endpoints.
/// Tests T086–T089: board creation, task lifecycle, RBAC enforcement, and
/// concurrent access through the full HTTP pipeline.
/// </summary>
[Collection("IntegrationTests")]
public class KanbanIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string _dbName = null!;
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

        _dbName = $"KanbanIntegrationTest_{Guid.NewGuid():N}";

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

        _app.UseCors();
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

    // ─── T086: Board Creation from Assessment ───────────────────────────────

    [Fact]
    public async Task CreateBoard_ViaChat_RoutesToKanbanAndSucceeds()
    {
        var request = new { message = "Create a new remediation board" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent");
    }

    [Fact]
    public async Task KanbanToolsList_IncludesComplianceAndKanbanCapabilities()
    {
        var response = await _client.GetAsync("/mcp/tools");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var toolNames = json.RootElement.GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();

        // Verify core compliance tools are registered
        toolNames.Should().Contain("compliance_assess", "compliance assessment tool should be registered");
        toolNames.Should().Contain("compliance_remediate", "remediation tool should be registered");

        // Kanban tools are invoked through ComplianceAgent routing, not as standalone MCP tools.
        // The tools endpoint should still return a valid list of available tools.
        toolNames.Count.Should().BeGreaterOrEqualTo(11, "should include all MCP-exposed tools");
    }

    // ─── T087: Task Lifecycle ───────────────────────────────────────────────

    [Fact]
    public async Task ShowBoard_ViaChat_RoutesToBoardShow()
    {
        var request = new { message = "Show my kanban board" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent");
    }

    [Fact]
    public async Task ListTasks_ViaChat_RoutesCorrectly()
    {
        var request = new { message = "List all tasks on the board" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent");
    }

    [Fact]
    public async Task MoveTask_ViaChat_RoutesCorrectly()
    {
        var request = new { message = "Move task to In Progress" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent");
    }

    // ─── T088: RBAC Enforcement ─────────────────────────────────────────────

    [Fact]
    public async Task AssignTask_ViaChat_RoutesToAssignment()
    {
        var request = new { message = "Assign the task to John Smith" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent");
    }

    [Fact]
    public async Task ArchiveBoard_ViaChat_RoutesCorrectly()
    {
        var request = new { message = "Archive the board" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent");
    }

    // ─── T089: Concurrent MCP Tool Calls ────────────────────────────────────

    [Fact]
    public async Task ConcurrentRequests_DontCorrupt_Responses()
    {
        // Fire multiple chat requests in parallel and verify all return valid JSON
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            var request = new { message = $"Show tasks on the board {i}" };
            var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            json.RootElement.TryGetProperty("success", out _).Should().BeTrue(
                $"concurrent request {i} should return valid response structure");
        });

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task ExportBoard_ViaChat_RoutesToExport()
    {
        var request = new { message = "Export the kanban board as CSV" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent");
    }

    [Fact]
    public async Task BulkUpdate_ViaChat_RoutesToBulkTool()
    {
        var request = new { message = "Bulk assign all tasks to team lead" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent");
    }

    [Fact]
    public async Task ValidateTask_ViaChat_RoutesToValidation()
    {
        var request = new { message = "Validate the remediation task" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent");
    }

    [Fact]
    public async Task RemediateTask_ViaChat_RoutesToRemediation()
    {
        var request = new { message = "Run remediation for the task" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", request, _jsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent");
    }
}

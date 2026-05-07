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
using Ato.Copilot.Agents.Extensions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Mcp.Extensions;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.State.Extensions;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for PIM workflow lifecycle — T099.
/// Uses Development environment to bypass auth (tests exercise the service/tool layer).
/// Tests activate→list→extend→deactivate, approval workflow, JIT lifecycle, and history.
/// </summary>
[Collection("IntegrationTests")]
public class PimWorkflowIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly string _dbName = $"PimWorkflow_{Guid.NewGuid():N}";
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

    // ────────────────────────────────────────────────────────────
    //  PIM Role Activation Lifecycle via Chat
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PimChatFlow_AskAboutPimRoles_GetsMeaningfulResponse()
    {
        var chatRequest = new { message = "What PIM roles am I eligible for?" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", chatRequest, _jsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("response").GetString()
            .Should().NotBeEmpty("agent should respond to PIM questions");
    }

    [Fact]
    public async Task PimChatFlow_ActivateRoleRequest_ProcessedByComplianceAgent()
    {
        var chatRequest = new { message = "Activate my Contributor PIM role for security remediation" };

        var response = await _client.PostAsJsonAsync("/mcp/chat", chatRequest, _jsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("agentUsed").GetString()
            .Should().Be("Compliance Agent", "PIM requests should route to Compliance Agent");
    }

    // ────────────────────────────────────────────────────────────
    //  PIM Service Direct Tests via Service Resolution
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PimService_ListEligibleRoles_ReturnsSimulatedRoles()
    {
        using var scope = _app.Services.CreateScope();
        var pimService = scope.ServiceProvider.GetRequiredService<Core.Interfaces.Auth.IPimService>();

        var roles = await pimService.ListEligibleRolesAsync("test-user");

        roles.Should().NotBeEmpty("simulated PIM service should return eligible roles");
        roles.Should().Contain(r => r.RoleName == "Contributor");
        roles.Should().Contain(r => r.RoleName == "Reader");
    }

    [Fact]
    public async Task PimService_ActivateDeactivateLifecycle_WorksEndToEnd()
    {
        using var scope = _app.Services.CreateScope();
        var pimService = scope.ServiceProvider.GetRequiredService<Core.Interfaces.Auth.IPimService>();

        // Step 1: Activate
        var activation = await pimService.ActivateRoleAsync(
            "lifecycle-user", "Contributor", "/subscriptions/default",
            "Remediating AC-2.1 finding per assessment RUN-2026-0221",
            null, 4, Guid.NewGuid());

        activation.Activated.Should().BeTrue("role activation should succeed");
        activation.RoleName.Should().Be("Contributor");

        // Step 2: List active
        var active = await pimService.ListActiveRolesAsync("lifecycle-user");
        active.Should().ContainSingle(r => r.RoleName == "Contributor");

        // Step 3: Extend
        var extension = await pimService.ExtendRoleAsync(
            "lifecycle-user", "Contributor", "/subscriptions/default", 2);

        extension.Extended.Should().BeTrue("extension should succeed");

        // Step 4: Deactivate
        var deactivation = await pimService.DeactivateRoleAsync(
            "lifecycle-user", "Contributor", "/subscriptions/default");

        deactivation.Deactivated.Should().BeTrue("deactivation should succeed");

        // Step 5: Verify no longer active
        var afterDeactivation = await pimService.ListActiveRolesAsync("lifecycle-user");
        afterDeactivation.Should().NotContain(r => r.RoleName == "Contributor");
    }

    [Fact]
    public async Task PimService_HighPrivilegeRole_RequiresApproval()
    {
        using var scope = _app.Services.CreateScope();
        var pimService = scope.ServiceProvider.GetRequiredService<Core.Interfaces.Auth.IPimService>();

        var activation = await pimService.ActivateRoleAsync(
            "approval-user", "Owner", "/subscriptions/default",
            "Emergency access required for production incident remediation steps",
            null, 4, Guid.NewGuid());

        // High-privilege roles go through approval
        activation.PendingApproval.Should().BeTrue("Owner is a high-privilege role");
        activation.PimRequestId.Should().NotBeEmpty("approval request should be created");
    }

    [Fact]
    public async Task PimService_ApprovalWorkflow_ApproveAndActivate()
    {
        using var scope = _app.Services.CreateScope();
        var pimService = scope.ServiceProvider.GetRequiredService<Core.Interfaces.Auth.IPimService>();

        // Submit high-privilege activation
        var activation = await pimService.ActivateRoleAsync(
            "approve-user", "Owner", "/subscriptions/default",
            "Emergency access for production compliance remediation",
            null, 4, Guid.NewGuid());

        activation.PendingApproval.Should().BeTrue();
        var requestId = Guid.Parse(activation.PimRequestId!);

        // Approve
        var approval = await pimService.ApproveRequestAsync(
            requestId, "approver-1", "Security Lead",
            "Approved for emergency access");

        approval.Approved.Should().BeTrue("approval should succeed");
        approval.RoleName.Should().Be("Owner");
    }

    [Fact]
    public async Task PimService_History_RecordsActivations()
    {
        using var scope = _app.Services.CreateScope();
        var pimService = scope.ServiceProvider.GetRequiredService<Core.Interfaces.Auth.IPimService>();

        // Activate a role
        await pimService.ActivateRoleAsync(
            "history-user", "Reader", "/subscriptions/default",
            "Compliance audit review for control family assessment",
            null, 2, Guid.NewGuid());

        // Get history
        var history = await pimService.GetHistoryAsync("history-user", days: 1);

        history.TotalCount.Should().BeGreaterOrEqualTo(1);
        history.History.Should().Contain(e => e.RoleName == "Reader");
        history.NistControlMapping.Should().NotBeEmpty("history should include NIST mappings");
    }

    // ────────────────────────────────────────────────────────────
    //  JIT VM Access Lifecycle
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task JitService_RequestListRevokeLifecycle_WorksEndToEnd()
    {
        using var scope = _app.Services.CreateScope();
        var jitService = scope.ServiceProvider.GetRequiredService<Core.Interfaces.Auth.IJitVmAccessService>();

        // Step 1: Request JIT access
        var request = await jitService.RequestAccessAsync(
            "jit-user", "vm-prod-01", "rg-production",
            "sub-123", 22, "SSH", "10.0.0.1", 4,
            "Emergency maintenance required for compliance fixes",
            null, Guid.NewGuid());

        request.Success.Should().BeTrue("JIT request should succeed");
        request.JitRequestId.Should().NotBeEmpty();

        // Step 2: List active sessions
        var sessions = await jitService.ListActiveSessionsAsync("jit-user");
        sessions.Should().ContainSingle(s => s.VmName == "vm-prod-01");

        // Step 3: Revoke
        var revoke = await jitService.RevokeAccessAsync(
            "jit-user", "vm-prod-01", "rg-production");

        revoke.Revoked.Should().BeTrue("revoke should succeed");

        // Step 4: Verify no longer active
        var afterRevoke = await jitService.ListActiveSessionsAsync("jit-user");
        afterRevoke.Should().NotContain(s => s.VmName == "vm-prod-01");
    }

    // ────────────────────────────────────────────────────────────
    //  Tools List Verification
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolsList_IncludesAllAuthPimJitTools()
    {
        var response = await _client.GetAsync("/mcp/tools");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var toolNames = json.RootElement.GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();

        // Verify all 15 auth/PIM/JIT tools are listed
        var expectedTools = new[]
        {
            "cac_status", "cac_sign_out", "cac_set_timeout", "cac_map_certificate",
            "pim_list_eligible", "pim_activate_role", "pim_deactivate_role",
            "pim_list_active", "pim_extend_role", "pim_approve_request",
            "pim_deny_request", "pim_history",
            "jit_request_access", "jit_list_sessions", "jit_revoke_access"
        };

        foreach (var tool in expectedTools)
        {
            toolNames.Should().Contain(tool, $"tool '{tool}' should be listed");
        }
    }

    [Fact]
    public async Task ToolsList_TotalCount_IncludesAllTools()
    {
        var response = await _client.GetAsync("/mcp/tools");
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var count = json.RootElement.GetProperty("count").GetInt32();
        count.Should().BeGreaterOrEqualTo(27,
            "should include compliance + auth/PIM/JIT + kanban tools");
    }
}

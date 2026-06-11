using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Mcp.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Middleware;

/// <summary>
/// Unit tests for AuthTierClassification — verifies correct Tier 1/Tier 2 classification.
/// T080: IsTier2() returns true for all Tier 2 tools, false for Tier 1 tools.
/// T085: Verify two-tier classification with AUTH_REQUIRED envelope and Tier 1 pass-through.
/// </summary>
/// <remarks>
/// [Collection("MiddlewareEnvTests")] — shares a sequential xUnit collection with
/// <see cref="SimulatedRoleHeaderStripTests"/> and <see cref="CacAuthenticationMiddlewareTests"/>.
/// All three classes mutate the process-wide ASPNETCORE_ENVIRONMENT env var;
/// sequential execution prevents race conditions.
/// </remarks>
[Collection("MiddlewareEnvTests")]
public class AuthTierClassificationTests
{
    // ─── Tier 2 tools should return true ─────────────────────────────────────

    [Theory]
    [InlineData("run_assessment")]
    [InlineData("execute_remediation")]
    [InlineData("validate_remediation")]
    [InlineData("collect_evidence")]
    [InlineData("discover_resources")]
    [InlineData("deploy_template")]
    [InlineData("compliance_assess")]
    [InlineData("compliance_remediate")]
    [InlineData("compliance_validate_remediation")]
    [InlineData("compliance_collect_evidence")]
    [InlineData("compliance_monitoring")]
    [InlineData("kanban_remediate_task")]
    [InlineData("kanban_validate_task")]
    [InlineData("kanban_collect_evidence")]
    [InlineData("cac_sign_out")]
    [InlineData("cac_set_timeout")]
    [InlineData("cac_map_certificate")]
    [InlineData("pim_list_eligible")]
    [InlineData("pim_activate_role")]
    [InlineData("pim_deactivate_role")]
    [InlineData("pim_list_active")]
    [InlineData("pim_extend_role")]
    [InlineData("pim_history")]
    [InlineData("pim_approve_request")]
    [InlineData("pim_deny_request")]
    [InlineData("jit_request_access")]
    [InlineData("jit_list_sessions")]
    [InlineData("jit_revoke_access")]
    public void IsTier2_ShouldReturnTrue_ForTier2Tools(string toolName)
    {
        AuthTierClassification.IsTier2(toolName).Should().BeTrue(
            because: $"{toolName} is a Tier 2 tool requiring CAC authentication");
    }

    // ─── Tier 1 tools should return false ────────────────────────────────────

    [Theory]
    [InlineData("nist_control_query")]
    [InlineData("show_assessment_cached")]
    [InlineData("kanban_view")]
    [InlineData("kanban_board_show")]
    [InlineData("kanban_get_task")]
    [InlineData("kanban_create_board")]
    [InlineData("kanban_create_task")]
    [InlineData("kanban_assign_task")]
    [InlineData("kanban_move_task")]
    [InlineData("kanban_task_list")]
    [InlineData("kanban_task_history")]
    [InlineData("kanban_add_comment")]
    [InlineData("kanban_task_comments")]
    [InlineData("kanban_edit_comment")]
    [InlineData("kanban_delete_comment")]
    [InlineData("kanban_bulk_update")]
    [InlineData("kanban_export")]
    [InlineData("kanban_archive_board")]
    [InlineData("help")]
    [InlineData("cac_status")]
    [InlineData("control_family")]
    [InlineData("compliance_status")]
    [InlineData("compliance_history")]
    [InlineData("assessment_audit_log")]
    public void IsTier2_ShouldReturnFalse_ForTier1Tools(string toolName)
    {
        AuthTierClassification.IsTier2(toolName).Should().BeFalse(
            because: $"{toolName} is a Tier 1 tool that does not require authentication");
    }

    // ─── Case-insensitive matching ───────────────────────────────────────────

    [Theory]
    [InlineData("PIM_ACTIVATE_ROLE")]
    [InlineData("Pim_Activate_Role")]
    [InlineData("pim_activate_role")]
    [InlineData("PIM_ACTIVATE_role")]
    public void IsTier2_ShouldBeCaseInsensitive(string toolName)
    {
        AuthTierClassification.IsTier2(toolName).Should().BeTrue(
            because: "tier classification should be case-insensitive");
    }

    // ─── Unknown tools default to Tier 1 ─────────────────────────────────────

    [Theory]
    [InlineData("unknown_tool")]
    [InlineData("custom_operation")]
    [InlineData("future_tool_xyz")]
    public void IsTier2_ShouldReturnFalse_ForUnknownTools(string toolName)
    {
        AuthTierClassification.IsTier2(toolName).Should().BeFalse(
            because: "unknown tools should default to Tier 1 (no auth required)");
    }

    // ─── Edge cases ──────────────────────────────────────────────────────────

    [Fact]
    public void IsTier2_ShouldReturnFalse_ForNull()
    {
        AuthTierClassification.IsTier2(null!).Should().BeFalse();
    }

    [Fact]
    public void IsTier2_ShouldReturnFalse_ForEmptyString()
    {
        AuthTierClassification.IsTier2(string.Empty).Should().BeFalse();
    }

    // ─── T085: Two-tier classification with AUTH_REQUIRED envelope ────────────

    [Fact]
    public void Tier2Tools_ShouldContainExactly28Tools()
    {
        // All 28 Tier 2 tools from the classification registry
        var allTier2 = new[]
        {
            "run_assessment", "execute_remediation", "validate_remediation",
            "collect_evidence", "discover_resources", "deploy_template",
            "compliance_assess", "compliance_remediate", "compliance_validate_remediation",
            "compliance_collect_evidence", "compliance_monitoring",
            "kanban_remediate_task", "kanban_validate_task", "kanban_collect_evidence",
            "cac_sign_out", "cac_set_timeout", "cac_map_certificate",
            "pim_list_eligible", "pim_activate_role", "pim_deactivate_role",
            "pim_list_active", "pim_extend_role", "pim_history",
            "pim_approve_request", "pim_deny_request",
            "jit_request_access", "jit_list_sessions", "jit_revoke_access"
        };

        allTier2.Should().HaveCount(28);
        allTier2.Should().AllSatisfy(t =>
            AuthTierClassification.IsTier2(t).Should().BeTrue($"{t} should be Tier 2"));
    }

    [Fact]
    public async Task Middleware_ShouldReturnAuthRequired_ForTier2ToolWithoutUser()
    {
        // Arrange — middleware with non-Development environment
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        try
        {
            var nextCalled = false;
            var middleware = new ComplianceAuthorizationMiddleware(
                _ => { nextCalled = true; return Task.CompletedTask; },
                Mock.Of<ILogger<ComplianceAuthorizationMiddleware>>());

            var context = new DefaultHttpContext();
            context.Items["ToolName"] = "pim_activate_role";
            context.Response.Body = new MemoryStream();

            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<ICacSessionService>());
            context.RequestServices = services.BuildServiceProvider();

            // Act
            await middleware.InvokeAsync(context);

            // Assert — AUTH_REQUIRED envelope per contract error code reference
            context.Response.StatusCode.Should().Be(401);
            nextCalled.Should().BeFalse("Tier 2 tool should be blocked without authenticated user");

            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            root.GetProperty("status").GetString().Should().Be("error");
            root.GetProperty("data").GetProperty("errorCode").GetString().Should().Be("AUTH_REQUIRED");
            root.GetProperty("data").GetProperty("message").GetString().Should().Contain("pim_activate_role");
            root.GetProperty("data").GetProperty("suggestion").GetString().Should().Contain("PLATFORM_COPILOT_TOKEN");
            root.GetProperty("metadata").GetProperty("toolName").GetString().Should().Be("pim_activate_role");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }
    }

    [Fact]
    public async Task Middleware_ShouldReturnAuthRequired_ForTier2ToolWithExpiredSession()
    {
        // Arrange — user has oid claim but no active CAC session
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        try
        {
            var nextCalled = false;
            var middleware = new ComplianceAuthorizationMiddleware(
                _ => { nextCalled = true; return Task.CompletedTask; },
                Mock.Of<ILogger<ComplianceAuthorizationMiddleware>>());

            var context = new DefaultHttpContext();
            context.Items["ToolName"] = "run_assessment";
            context.Response.Body = new MemoryStream();

            // Add user with oid claim
            var claims = new[] { new System.Security.Claims.Claim("oid", "user-123") };
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(claims, "Bearer"));

            // Mock ICacSessionService to return false for IsSessionActiveAsync
            var mockCacService = new Mock<ICacSessionService>();
            mockCacService.Setup(s => s.IsSessionActiveAsync("user-123", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var services = new ServiceCollection();
            services.AddSingleton(mockCacService.Object);
            context.RequestServices = services.BuildServiceProvider();

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            context.Response.StatusCode.Should().Be(401);
            nextCalled.Should().BeFalse();

            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
            using var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("data").GetProperty("errorCode").GetString().Should().Be("AUTH_REQUIRED");
            doc.RootElement.GetProperty("data").GetProperty("message").GetString().Should().Contain("expired");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }
    }

    [Theory]
    [InlineData("nist_control_query")]
    [InlineData("show_assessment_cached")]
    [InlineData("kanban_view")]
    [InlineData("kanban_board_show")]
    [InlineData("help")]
    [InlineData("cac_status")]
    public async Task Middleware_ShouldPassThrough_ForTier1ToolsWithoutAuth(string toolName)
    {
        // Arrange — unauthenticated user with Tier 1 tool
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        try
        {
            var nextCalled = false;
            var middleware = new ComplianceAuthorizationMiddleware(
                _ => { nextCalled = true; return Task.CompletedTask; },
                Mock.Of<ILogger<ComplianceAuthorizationMiddleware>>());

            var context = new DefaultHttpContext();
            context.Items["ToolName"] = toolName;

            // Act
            await middleware.InvokeAsync(context);

            // Assert — Tier 1 tools should pass through to next middleware
            nextCalled.Should().BeTrue($"Tier 1 tool '{toolName}' should pass through without auth");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }
    }
}

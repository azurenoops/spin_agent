using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Mcp.Middleware;

namespace Ato.Copilot.Tests.Unit.Middleware;

/// <summary>
/// Tests for ComplianceAuthorizationMiddleware — Tier 2 gate, OPERATION_PAUSED, role checks.
/// </summary>
[Collection("MiddlewareEnvTests")]
public class ComplianceAuthorizationMiddlewareTests
{
    private readonly Mock<ILogger<ComplianceAuthorizationMiddleware>> _logger = new();
    private readonly Mock<IHostEnvironment> _prodEnv;

    public ComplianceAuthorizationMiddlewareTests()
    {
        _prodEnv = new Mock<IHostEnvironment>();
        _prodEnv.Setup(e => e.EnvironmentName).Returns(Environments.Production);
    }

    private ComplianceAuthorizationMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new ComplianceAuthorizationMiddleware(next, _logger.Object, _prodEnv.Object);
    }

    [Fact]
    public async Task Tier2Tool_ExpiredSessionWithPreviousSessionId_ShouldReturnOperationPaused()
    {
        // Simulate mid-operation session expiration per FR-026
        var cacServiceMock = new Mock<ICacSessionService>();
        cacServiceMock.Setup(s => s.IsSessionActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var services = new ServiceCollection();
        services.AddSingleton(cacServiceMock.Object);
        var serviceProvider = services.BuildServiceProvider();

        var nextCalled = false;
        var middleware = CreateMiddleware(ctx => { nextCalled = true; return Task.CompletedTask; });

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/tools";

        // Set up user identity
        var claims = new[] { new Claim("oid", "user-mid-op") };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        // Set up Tier 2 tool and previous session id (mid-operation context)
        context.Items["ToolName"] = "run_assessment";
        context.Items["SessionId"] = "prev-session-123";

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(202);
        nextCalled.Should().BeFalse();

        // Read response body
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("error");
        root.GetProperty("data").GetProperty("errorCode").GetString().Should().Be("OPERATION_PAUSED");
        root.GetProperty("data").GetProperty("context").GetProperty("toolName").GetString().Should().Be("run_assessment");
        root.GetProperty("data").GetProperty("context").GetProperty("previousSessionId").GetString().Should().Be("prev-session-123");
        root.GetProperty("data").GetProperty("context").GetProperty("pausedAt").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Tier2Tool_ExpiredSessionNoPreviousSession_ShouldReturnAuthRequired()
    {
        var cacServiceMock = new Mock<ICacSessionService>();
        cacServiceMock.Setup(s => s.IsSessionActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var services = new ServiceCollection();
        services.AddSingleton(cacServiceMock.Object);
        var serviceProvider = services.BuildServiceProvider();

        var middleware = CreateMiddleware(ctx => Task.CompletedTask);

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/tools";

        var claims = new[] { new Claim("oid", "user-expired") };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        context.Items["ToolName"] = "run_assessment";
        // No SessionId — not a mid-operation scenario

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("data").GetProperty("errorCode").GetString().Should().Be("AUTH_REQUIRED");
    }

    [Fact]
    public async Task Tier1Tool_ShouldPassThroughWithoutAuthCheck()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(ctx => { nextCalled = true; return Task.CompletedTask; });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/tools";
        context.Items["ToolName"] = "cac_status"; // Tier 1

        await middleware.InvokeAsync(context);

        // Tier 1 tool with no auth should not be blocked (no role check for unauthenticated)
        // The middleware should call next for non-Tier 2 tools without strict auth
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public void IsWriteTool_ShouldIdentifyWriteTools()
    {
        ComplianceAuthorizationMiddleware.IsWriteTool("compliance_remediate").Should().BeTrue();
        ComplianceAuthorizationMiddleware.IsWriteTool("compliance_validate_remediation").Should().BeTrue();
        ComplianceAuthorizationMiddleware.IsWriteTool("cac_status").Should().BeFalse();
    }

    [Fact]
    public void IsApprovalTool_ShouldIdentifyApprovalTools()
    {
        ComplianceAuthorizationMiddleware.IsApprovalTool("pim_approve_request").Should().BeTrue();
        ComplianceAuthorizationMiddleware.IsApprovalTool("pim_deny_request").Should().BeTrue();
        ComplianceAuthorizationMiddleware.IsApprovalTool("cac_status").Should().BeFalse();
    }
}

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Mcp.Middleware;

namespace Ato.Copilot.Tests.Unit.Middleware;

/// <summary>
/// Unit tests for PIM tier enforcement in ComplianceAuthorizationMiddleware.
/// Validates Tier 2a (Read) and Tier 2b (Write) PIM tier checks per FR-001, FR-013, FR-034.
/// </summary>
public class PimTierEnforcementTests
{
    private readonly Mock<IPimService> _mockPimService;
    private readonly Mock<ICacSessionService> _mockCacService;
    private readonly Mock<ILogger<ComplianceAuthorizationMiddleware>> _mockLogger;
    private readonly Mock<IHostEnvironment> _mockEnv;

    public PimTierEnforcementTests()
    {
        _mockPimService = new Mock<IPimService>();
        _mockCacService = new Mock<ICacSessionService>();
        _mockLogger = new Mock<ILogger<ComplianceAuthorizationMiddleware>>();
        _mockEnv = new Mock<IHostEnvironment>();
        _mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Production);
    }

    private ComplianceAuthorizationMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new ComplianceAuthorizationMiddleware(next, _mockLogger.Object, _mockEnv.Object);
    }

    private HttpContext CreateAuthenticatedContext(string toolName, string userId = "test-user-id")
    {
        var context = new DefaultHttpContext();
        context.Items["ToolName"] = toolName;

        var claims = new[]
        {
            new Claim("oid", userId),
            new Claim(ClaimTypes.Role, "Compliance.Administrator")
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        // Set up DI
        var services = new ServiceCollection();
        services.AddSingleton(_mockPimService.Object);
        services.AddSingleton(_mockCacService.Object);
        context.RequestServices = services.BuildServiceProvider();

        // CAC session is active by default
        _mockCacService.Setup(s => s.IsSessionActiveAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return context;
    }

    [Fact]
    public async Task Tier2aTool_WithReaderPim_ShouldPass()
    {
        var context = CreateAuthenticatedContext("pim_list_eligible");
        _mockPimService.Setup(s => s.ListActiveRolesAsync("test-user-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PimActiveRole>
            {
                new() { RoleName = "Reader", Scope = "/subscriptions/test" }
            });

        bool nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue("Tier 2a tool should pass with Reader PIM");
    }

    [Fact]
    public async Task Tier2aTool_WithoutPim_ShouldReturnPimElevationRequired()
    {
        var context = CreateAuthenticatedContext("pim_list_eligible");
        _mockPimService.Setup(s => s.ListActiveRolesAsync("test-user-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PimActiveRole>());

        var middleware = CreateMiddleware();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Tier2bTool_WithReaderPim_ShouldReturnInsufficientPimTier()
    {
        var context = CreateAuthenticatedContext("pim_activate_role");
        _mockPimService.Setup(s => s.ListActiveRolesAsync("test-user-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PimActiveRole>
            {
                new() { RoleName = "Reader", Scope = "/subscriptions/test" }
            });

        // Capture the response body
        context.Response.Body = new MemoryStream();
        var middleware = CreateMiddleware();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Tier2bTool_WithContributorPim_ShouldPass()
    {
        var context = CreateAuthenticatedContext("pim_activate_role");
        _mockPimService.Setup(s => s.ListActiveRolesAsync("test-user-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PimActiveRole>
            {
                new() { RoleName = "Contributor", Scope = "/subscriptions/test" }
            });

        bool nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue("Tier 2b tool should pass with Contributor PIM");
    }

    [Fact]
    public async Task Tier1Tool_WithoutAnyPim_ShouldPass()
    {
        var context = CreateAuthenticatedContext("cac_status");
        // No PIM roles at all
        _mockPimService.Setup(s => s.ListActiveRolesAsync("test-user-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PimActiveRole>());

        bool nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue("Tier 1 tool (PimTier.None) should pass without any PIM");
    }

    [Fact]
    public async Task DevelopmentMode_ShouldSkipAllPimChecks()
    {
        _mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        var context = new DefaultHttpContext();
        context.Items["ToolName"] = "pim_activate_role";

        // No auth setup, no PIM — should skip entirely in Development

        bool nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue("Development mode should skip all PIM checks");
    }

    [Fact]
    public async Task Tier2bTool_WithOwnerPim_ShouldPass()
    {
        var context = CreateAuthenticatedContext("execute_remediation");
        _mockPimService.Setup(s => s.ListActiveRolesAsync("test-user-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PimActiveRole>
            {
                new() { RoleName = "Owner", Scope = "/subscriptions/test" }
            });

        bool nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue("Owner PIM role should satisfy Tier 2b requirement");
    }
}

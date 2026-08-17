using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Mcp.Middleware;

namespace Ato.Copilot.Tests.Unit.Middleware;

/// <summary>
/// BUG-21 (#694) — Startup-guard and middleware tests for the
/// ALLOW_DEV_AUTH_BYPASS env-var gate.
///
/// These tests run sequentially (same "MiddlewareEnvTests" collection) because
/// they mutate process-wide environment variables.
/// </summary>
[Collection("MiddlewareEnvTests")]
public class DevAuthBypassGuardTests : IDisposable
{
    // ────────────────────────────────────────────────────────────
    //  Env-var save/restore — prevents cross-test contamination
    //  when CacAuthenticationMiddlewareTests (same collection) also
    //  mutates ASPNETCORE_ENVIRONMENT on the same thread.
    // ────────────────────────────────────────────────────────────

    private readonly string? _savedAspNetCoreEnvironment =
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    private readonly string? _savedAllowDevAuthBypass =
        Environment.GetEnvironmentVariable("ALLOW_DEV_AUTH_BYPASS");

    // ────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────

    private readonly Mock<ILogger<CacAuthenticationMiddleware>> _logger = new();

    private CacAuthenticationMiddleware CreateMiddleware(
        RequestDelegate next,
        string environmentName = "Development")
    {
        var hostEnv = new Mock<IHostEnvironment>();
        hostEnv.Setup(h => h.EnvironmentName).Returns(environmentName);
        return new CacAuthenticationMiddleware(
            next,
            Options.Create(new AzureAdOptions { RequireCac = true }),
            Options.Create(new CacAuthOptions()),       // SimulationMode = false
            Options.Create(new RoleClaimMappingsOptions()),
            hostEnv.Object,
            _logger.Object);
    }

    public void Dispose()
    {
        // Restore env vars to their pre-test values rather than unconditionally
        // clearing them — prevents leaking null into other tests in the collection.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _savedAspNetCoreEnvironment);
        Environment.SetEnvironmentVariable("ALLOW_DEV_AUTH_BYPASS", _savedAllowDevAuthBypass);
    }

    // ────────────────────────────────────────────────────────────
    //  Middleware — dev bypass gate
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DevMode_WithBypassFlag_ShouldPassThroughWithoutJwt()
    {
        // Arrange
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ALLOW_DEV_AUTH_BYPASS", "true");

        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        // No Authorization header — should still pass through

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue("bypass flag is set in Development");
    }

    [Fact]
    public async Task DevMode_WithoutBypassFlag_ShouldFallThroughToJwtValidation()
    {
        // Arrange
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ALLOW_DEV_AUTH_BYPASS", null); // not set

        // next will be called regardless because the JWT check for no-token allows through
        // (Tier-1 tools allow unauthenticated — ComplianceAuthorizationMiddleware gates Tier-2).
        // What we're asserting here is that the bypass shortcut is NOT taken and the
        // warning about the missing flag IS logged.
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();

        // Act — no token, no bypass flag
        await middleware.InvokeAsync(context);

        // The request still passes (Tier-1 allows anonymous) but the bypass warning
        // about the missing flag should have been logged.
        nextCalled.Should().BeTrue();

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("ALLOW_DEV_AUTH_BYPASS is not set")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "should warn that the dev bypass flag is missing");
    }

    [Fact]
    public async Task DevMode_WithBypassFlagSetToFalse_ShouldFallThroughToJwtValidation()
    {
        // ALLOW_DEV_AUTH_BYPASS=false must NOT activate the bypass.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ALLOW_DEV_AUTH_BYPASS", "false");

        // Defensive guard: confirm env vars were applied before invoking the middleware.
        // If this fails it signals a process-level env contamination from another test
        // in the MiddlewareEnvTests collection.
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            .Should().Be("Development", "env var must be set before InvokeAsync");
        Environment.GetEnvironmentVariable("ALLOW_DEV_AUTH_BYPASS")
            .Should().Be("false", "env var must be set before InvokeAsync");

        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        // Request passes (Tier-1 is unauthenticated-allowed) but bypass warning logged
        nextCalled.Should().BeTrue("no-token path allows Tier-1 requests through");

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("ALLOW_DEV_AUTH_BYPASS is not set")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "must warn that ALLOW_DEV_AUTH_BYPASS is not set to 'true' when it is explicitly 'false'");
    }

    [Fact]
    public async Task ProductionMode_WithBypassFlag_ShouldNotBypassAuth()
    {
        // Even if ALLOW_DEV_AUTH_BYPASS=true is set but env is Production,
        // the middleware must NOT grant the bypass (that's the startup guard's job;
        // the middleware only honours the bypass for Development env).
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("ALLOW_DEV_AUTH_BYPASS", "true");

        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; },
            environmentName: "Production");
        var context = new DefaultHttpContext();
        // No JWT → falls to the no-token branch, which also passes through for Tier-1

        await middleware.InvokeAsync(context);

        // Verify the bypass warning is NOT logged (middleware never hits the bypass branch)
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Dev auth bypass ACTIVE")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "Production mode must not log the bypass-active warning");
    }

    // ────────────────────────────────────────────────────────────
    //  Auth-mode logging (startup concern, validated indirectly via
    //  the middleware path — the startup function is tested via integration)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DevMode_BypassActive_ShouldLogBypassActiveWarning()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ALLOW_DEV_AUTH_BYPASS", "true");

        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Dev auth bypass ACTIVE")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "must log a prominent warning when the bypass shortcut is taken");
    }
}

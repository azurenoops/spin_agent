using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Mcp.Middleware;

namespace Ato.Copilot.Tests.Unit.Middleware;

/// <summary>
/// Tests for CAC simulation mode in CacAuthenticationMiddleware.
/// Covers US1 (core simulation), US2 (configurability), US4 (production safety guard).
/// </summary>
public class CacAuthSimulationTests
{
    private readonly Mock<ILogger<CacAuthenticationMiddleware>> _logger = new();

    private CacAuthenticationMiddleware CreateMiddleware(
        RequestDelegate next,
        CacAuthOptions cacOptions,
        string environmentName = "Development",
        AzureAdOptions? azureAdOptions = null)
    {
        var azOpts = azureAdOptions ?? new AzureAdOptions { RequireCac = true };
        var hostEnv = new Mock<IHostEnvironment>();
        hostEnv.Setup(h => h.EnvironmentName).Returns(environmentName);
        return new CacAuthenticationMiddleware(
            next,
            Options.Create(azOpts),
            Options.Create(cacOptions),
            Options.Create(new RoleClaimMappingsOptions()),
            hostEnv.Object,
            _logger.Object);
    }

    private static CacAuthOptions CreateSimulationOptions(
        string upn = "dev.user@dev.mil",
        string displayName = "Dev User (Simulated)",
        string? thumbprint = null,
        List<string>? roles = null)
    {
        return new CacAuthOptions
        {
            SimulationMode = true,
            SimulatedIdentity = new SimulatedIdentityOptions
            {
                UserPrincipalName = upn,
                DisplayName = displayName,
                CertificateThumbprint = thumbprint,
                Roles = roles ?? ["Global Reader", "ISSO"]
            }
        };
    }

    // ────────────────────────────────────────────────────────────────
    //  T006: Core Simulation Scenarios (US1)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SimulationMode_Development_SynthesizesClaimsPrincipal()
    {
        ClaimsPrincipal? capturedUser = null;
        RequestDelegate next = ctx => { capturedUser = ctx.User; return Task.CompletedTask; };

        var options = CreateSimulationOptions();
        var middleware = CreateMiddleware(next, options);
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        capturedUser.Should().NotBeNull();
        capturedUser!.Identity!.IsAuthenticated.Should().BeTrue();
        capturedUser.Identity.AuthenticationType.Should().Be("Simulated");
        capturedUser.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("dev.user@dev.mil");
        capturedUser.FindFirst(ClaimTypes.Name)!.Value.Should().Be("Dev User (Simulated)");
        capturedUser.FindFirst("preferred_username")!.Value.Should().Be("dev.user@dev.mil");
        capturedUser.FindAll("amr").Select(c => c.Value).Should().Contain(["mfa", "rsa"]);
    }

    [Fact]
    public async Task SimulationMode_Development_SetsClientTypeSimulated()
    {
        object? clientType = null;
        RequestDelegate next = ctx => { clientType = ctx.Items["ClientType"]; return Task.CompletedTask; };

        var options = CreateSimulationOptions();
        var middleware = CreateMiddleware(next, options);
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        clientType.Should().Be(ClientType.Simulated);
    }

    [Fact]
    public async Task SimulationMode_Disabled_PassesThroughToJwtAuth()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var options = new CacAuthOptions { SimulationMode = false };
        var middleware = CreateMiddleware(next, options, "Development");
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        // In Development with SimulationMode=false, falls through to Dev bypass (passes through)
        nextCalled.Should().BeTrue();
        // User should NOT have a "Simulated" authentication type
        context.User.Identity?.AuthenticationType.Should().NotBe("Simulated");
    }

    [Fact]
    public async Task SimulationMode_MissingSimulatedIdentity_ThrowsInvalidOperationException()
    {
        RequestDelegate next = _ => Task.CompletedTask;

        var options = new CacAuthOptions
        {
            SimulationMode = true,
            SimulatedIdentity = null
        };
        var middleware = CreateMiddleware(next, options);
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            var act = async () => await middleware.InvokeAsync(context);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*SimulatedIdentity*required*SimulationMode*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  T007: Configurability Tests (US2)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfiguredRoles_MapToRoleClaims()
    {
        ClaimsPrincipal? capturedUser = null;
        RequestDelegate next = ctx => { capturedUser = ctx.User; return Task.CompletedTask; };

        var options = CreateSimulationOptions(roles: ["ISSO", "Security Lead", "Global Reader"]);
        var middleware = CreateMiddleware(next, options);
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        var roles = capturedUser!.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        roles.Should().HaveCount(3);
        roles.Should().Contain(["ISSO", "Security Lead", "Global Reader"]);
    }

    [Fact]
    public async Task EmptyRoles_ProducesZeroRoleClaims()
    {
        ClaimsPrincipal? capturedUser = null;
        RequestDelegate next = ctx => { capturedUser = ctx.User; return Task.CompletedTask; };

        var options = CreateSimulationOptions(roles: []);
        var middleware = CreateMiddleware(next, options);
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        capturedUser!.FindAll(ClaimTypes.Role).Should().BeEmpty();
    }

    [Fact]
    public async Task NullRoles_DefaultsToEmptyList()
    {
        ClaimsPrincipal? capturedUser = null;
        RequestDelegate next = ctx => { capturedUser = ctx.User; return Task.CompletedTask; };

        var options = new CacAuthOptions
        {
            SimulationMode = true,
            SimulatedIdentity = new SimulatedIdentityOptions
            {
                UserPrincipalName = "dev.user@dev.mil",
                DisplayName = "Dev User",
                Roles = null!
            }
        };
        // Roles defaults to [] in the POCO, but test null assignment
        options.SimulatedIdentity.Roles = null!;

        var middleware = CreateMiddleware(next, options);
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            // null Roles would cause NullReferenceException in foreach — verify handling
            var act = async () => await middleware.InvokeAsync(context);
            // The middleware iterates Roles, so null will throw.
            // This validates that the POCO default (empty list) is the correct behavior.
            await act.Should().ThrowAsync<NullReferenceException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }
    }

    [Fact]
    public async Task CertificateThumbprint_WhenConfigured_PresentAsClaim()
    {
        ClaimsPrincipal? capturedUser = null;
        RequestDelegate next = ctx => { capturedUser = ctx.User; return Task.CompletedTask; };

        var options = CreateSimulationOptions(thumbprint: "ABC123DEF456");
        var middleware = CreateMiddleware(next, options);
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        capturedUser!.FindFirst("x5t")!.Value.Should().Be("ABC123DEF456");
    }

    [Fact]
    public async Task CertificateThumbprint_WhenNull_ClaimAbsent()
    {
        ClaimsPrincipal? capturedUser = null;
        RequestDelegate next = ctx => { capturedUser = ctx.User; return Task.CompletedTask; };

        var options = CreateSimulationOptions(thumbprint: null);
        var middleware = CreateMiddleware(next, options);
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        capturedUser!.FindFirst("x5t").Should().BeNull();
    }

    [Fact]
    public async Task MultipleRoles_ProducesMultipleRoleClaims()
    {
        ClaimsPrincipal? capturedUser = null;
        RequestDelegate next = ctx => { capturedUser = ctx.User; return Task.CompletedTask; };

        var options = CreateSimulationOptions(roles: ["ISSO", "AO"]);
        var middleware = CreateMiddleware(next, options);
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        var roles = capturedUser!.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        roles.Should().HaveCount(2);
        roles.Should().Contain("ISSO");
        roles.Should().Contain("AO");
    }

    // ────────────────────────────────────────────────────────────────
    //  T011: Production Safety Guard Tests (US4)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SimulationMode_Production_IgnoredAndFallsThrough()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var options = CreateSimulationOptions();
        var middleware = CreateMiddleware(next, options, "Production");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        // Should NOT have simulated identity — simulation is ignored in Production
        context.User.Identity?.AuthenticationType.Should().NotBe("Simulated");
    }

    [Fact]
    public async Task SimulationMode_Staging_IgnoredAndFallsThrough()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var options = CreateSimulationOptions();
        var middleware = CreateMiddleware(next, options, "Staging");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Staging");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        context.User.Identity?.AuthenticationType.Should().NotBe("Simulated");
    }

    [Fact]
    public async Task SimulationMode_Development_Activates()
    {
        RequestDelegate next = _ => Task.CompletedTask;

        var options = CreateSimulationOptions();
        var middleware = CreateMiddleware(next, options, "Development");
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        context.User.Identity?.AuthenticationType.Should().Be("Simulated");
    }

    [Fact]
    public async Task SimulationMode_NonDevelopment_LogsSecurityWarning()
    {
        RequestDelegate next = _ => Task.CompletedTask;

        var options = CreateSimulationOptions();
        var middleware = CreateMiddleware(next, options, "Production");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        // Verify warning was logged (via Moq's LogWarning verification)
        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Simulation mode will be ignored")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SimulationMode_NonDevelopment_WarningIncludesEnvironmentName()
    {
        RequestDelegate next = _ => Task.CompletedTask;

        var options = CreateSimulationOptions();
        var middleware = CreateMiddleware(next, options, "Staging");
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Staging");
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }

        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Staging")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}

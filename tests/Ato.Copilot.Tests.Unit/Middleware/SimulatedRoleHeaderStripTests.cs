using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Middleware;

/// <summary>
/// Verifies the ADR-002 defense-in-depth layer: <c>X-Simulated-Role</c> is stripped
/// from all inbound requests when the environment is NOT Development.
///
/// The header is a DEV-only escape hatch that lets front-end tests inject a synthetic
/// role claim. If it were allowed through in Production/Staging an attacker could
/// craft the header and impersonate any role without a valid CAC token.
///
/// Reference: docs/architecture/adr-002-simulated-role-header-scope.md (PR #376).
/// </summary>
public class SimulatedRoleHeaderStripTests
{
    private const string HeaderName = "X-Simulated-Role";

    // ─── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal <see cref="TestServer"/> that mirrors the ADR-002 inline
    /// middleware wired in <c>Program.cs</c>. The terminal handler echoes back
    /// whatever header value the server sees after the middleware runs, making it
    /// trivial to assert whether the header was stripped or preserved.
    /// </summary>
    private static TestServer BuildServer(string environment)
    {
        var hostBuilder = new HostBuilder()
            .UseEnvironment(environment)
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.Configure(app =>
                {
                    var env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
                    // ── exact middleware from Program.cs ────────────────────
                    app.Use(async (context, next) =>
                    {
                        if (!env.IsDevelopment())
                        {
                            context.Request.Headers.Remove(HeaderName);
                        }
                        await next();
                    });
                    // ── terminal: echo the header value (empty string = absent) ──
                    app.Run(ctx =>
                    {
                        var value = ctx.Request.Headers[HeaderName].FirstOrDefault() ?? string.Empty;
                        return ctx.Response.WriteAsync(value);
                    });
                });
            });

        var host = hostBuilder.Start();
        return host.GetTestServer();
    }

    // ─── Production ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Production_StripHeader_WhenPresent()
    {
        // Arrange
        using var server = BuildServer("Production");
        var client = server.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation(HeaderName, "SCA");

        // Act
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — header must be absent on the server side
        body.Should().BeEmpty(
            because: $"the middleware must strip {HeaderName} in Production");
    }

    [Fact]
    public async Task Production_NoOp_WhenHeaderAbsent()
    {
        // Arrange — request carries no X-Simulated-Role at all
        using var server = BuildServer("Production");
        var client = server.CreateClient();

        // Act
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/"));
        var body = await response.Content.ReadAsStringAsync();

        // Assert — pipeline must not error when the header is not present
        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ISSO")]
    [InlineData("AO")]
    [InlineData("SCA")]
    [InlineData("Engineer")]
    [InlineData("MissionOwner")]
    public async Task Production_StripHeader_ForAllRoleValues(string roleValue)
    {
        // Arrange
        using var server = BuildServer("Production");
        var client = server.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation(HeaderName, roleValue);

        // Act
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — every known role code must be stripped
        body.Should().BeEmpty(
            because: $"role value '{roleValue}' must not be allowed through in Production");
    }

    // ─── Staging ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Staging_StripHeader_WhenPresent()
    {
        // Arrange
        using var server = BuildServer("Staging");
        var client = server.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation(HeaderName, "ISSO");

        // Act
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — Staging is not Development, so header must be stripped
        body.Should().BeEmpty(
            because: $"the middleware must strip {HeaderName} in Staging (non-Development)");
    }

    // ─── Development ────────────────────────────────────────────────────────

    [Fact]
    public async Task Development_PreservesHeader_WhenPresent()
    {
        // Arrange — in Development the header must pass through untouched
        using var server = BuildServer("Development");
        var client = server.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation(HeaderName, "ISSO");

        // Act
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert — DEV mode: header flows through to the terminal handler
        body.Should().Be("ISSO",
            because: $"{HeaderName} is the DEV escape hatch and must not be stripped in Development");
    }

    [Fact]
    public async Task Development_NoOp_WhenHeaderAbsent()
    {
        // Arrange
        using var server = BuildServer("Development");
        var client = server.CreateClient();

        // Act
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/"));
        var body = await response.Content.ReadAsStringAsync();

        // Assert — pipeline is healthy; no header, no echoed value
        response.IsSuccessStatusCode.Should().BeTrue();
        body.Should().BeEmpty();
    }
}

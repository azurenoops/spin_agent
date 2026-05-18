using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Mcp.Middleware;

namespace Ato.Copilot.Tests.Unit.Middleware;

public class CacAuthenticationMiddlewareTests
{
    private readonly Mock<ILogger<CacAuthenticationMiddleware>> _logger;

    public CacAuthenticationMiddlewareTests()
    {
        _logger = new Mock<ILogger<CacAuthenticationMiddleware>>();
    }

    private CacAuthenticationMiddleware CreateMiddleware(
        RequestDelegate next,
        AzureAdOptions? options = null,
        CacAuthOptions? cacOptions = null,
        string environmentName = "Production")
    {
        var opts = options ?? new AzureAdOptions { RequireCac = true };
        var cacOpts = cacOptions ?? new CacAuthOptions();
        var hostEnv = new Mock<IHostEnvironment>();
        hostEnv.Setup(h => h.EnvironmentName).Returns(environmentName);
        return new CacAuthenticationMiddleware(
            next,
            Options.Create(opts),
            Options.Create(cacOpts),
            Options.Create(new RoleClaimMappingsOptions()),
            hostEnv.Object,
            _logger.Object);
    }

    private static string CreateTestJwt(
        IEnumerable<Claim>? claims = null,
        DateTime? expires = null,
        string issuer = "https://sts.windows.net/test-tenant/",
        DateTime? notBefore = null)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("this-is-a-test-key-that-is-long-enough-for-hmac"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var allClaims = new List<Claim>
        {
            new("oid", "user-123"),
            new("sub", "user-123"),
            new("name", "Test User"),
            new("email", "test@agency.mil"),
            new("tid", "test-tenant")
        };
        if (claims != null) allClaims.AddRange(claims);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: "api://ato-copilot",
            claims: allClaims,
            notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-5),
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task ValidJwt_WithCacAmrClaims_ShouldPassThrough()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = CreateMiddleware(next);
        var context = new DefaultHttpContext();

        // Set non-Development environment
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        var jwt = CreateTestJwt(
            claims: [new Claim("amr", "mfa"), new Claim("amr", "rsa")]);
        context.Request.Headers.Authorization = $"Bearer {jwt}";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().NotBe(401);

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }

    [Fact]
    public async Task ValidJwt_MissingAmrClaims_ShouldBeRejected()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = CreateMiddleware(next);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        var jwt = CreateTestJwt(); // No amr claims
        context.Request.Headers.Authorization = $"Bearer {jwt}";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);

        // Read response body
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain("MFA_CLAIM_MISSING");

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }

    [Fact]
    public async Task ExpiredToken_ShouldBeRejected()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = CreateMiddleware(next);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        var jwt = CreateTestJwt(
            expires: DateTime.UtcNow.AddHours(-1),
            notBefore: DateTime.UtcNow.AddHours(-2),
            claims: [new Claim("amr", "mfa"), new Claim("amr", "rsa")]);
        context.Request.Headers.Authorization = $"Bearer {jwt}";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain("TOKEN_EXPIRED");

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }

    [Fact]
    public async Task RequireCacFalse_ShouldSkipAmrCheck()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var options = new AzureAdOptions { RequireCac = false };
        var middleware = CreateMiddleware(next, options);
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        var jwt = CreateTestJwt(); // No amr claims, but RequireCac=false
        context.Request.Headers.Authorization = $"Bearer {jwt}";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }

    [Fact]
    public async Task HealthEndpoint_ShouldSkipAuth()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = CreateMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Path = "/health";

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }

    [Fact]
    public async Task DevelopmentEnvironment_ShouldSkipAuth()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = CreateMiddleware(next);
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }

    [Fact]
    public async Task NoAuthorizationHeader_ShouldPassThroughForTier1()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = CreateMiddleware(next);
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        // No Authorization header at all
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }

    [Fact]
    public async Task InvalidTokenFormat_ShouldBeRejected()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = CreateMiddleware(next);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        context.Request.Headers.Authorization = "Bearer not-a-valid-jwt";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }

    [Fact]
    public async Task ValidJwt_ShouldPopulateHttpContextUser()
    {
        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = CreateMiddleware(next, new AzureAdOptions { RequireCac = false });
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        var jwt = CreateTestJwt();
        context.Request.Headers.Authorization = $"Bearer {jwt}";

        await middleware.InvokeAsync(context);

        context.User.Should().NotBeNull();
        context.User.Identity!.IsAuthenticated.Should().BeTrue();
        context.User.FindFirst("oid")?.Value.Should().Be("user-123");

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }

    [Fact]
    public async Task ValidJwt_ShouldStoreTokenHash()
    {
        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = CreateMiddleware(next, new AzureAdOptions { RequireCac = false });
        var context = new DefaultHttpContext();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        var jwt = CreateTestJwt();
        context.Request.Headers.Authorization = $"Bearer {jwt}";

        await middleware.InvokeAsync(context);

        context.Items.Should().ContainKey("TokenHash");
        var hash = context.Items["TokenHash"] as string;
        hash.Should().HaveLength(64);

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }
}

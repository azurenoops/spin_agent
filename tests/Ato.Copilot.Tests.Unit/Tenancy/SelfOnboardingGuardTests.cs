using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Services.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

/// <summary>
/// T090 [US4]: Drives <see cref="TenantResolutionMiddleware"/> in
/// isolation against an in-memory SQLite database. Verifies the
/// MultiTenant requests require explicit membership even when the legacy
/// AllowSelfOnboarding setting is enabled; directory affiliation creates no grants.
/// </summary>
/// <remarks>
/// We invoke the middleware directly so the test does not need to stand up
/// the full ASP.NET pipeline. The request's <see cref="HttpContext.User"/>
/// is hand-rolled with a <c>tid</c> claim that the middleware reads.
/// </remarks>
public class SelfOnboardingGuardTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContextFactory<AtoCopilotContext>(opt => opt.UseSqlite(_connection));
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddSingleton(BuildImpersonationStub());
        services.AddSingleton(BuildCspProfileStub());
        services.AddSingleton<IOptions<RoleClaimMappingsOptions>>(Options.Create(new RoleClaimMappingsOptions()));
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        _sp = services.BuildServiceProvider();

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task SelfOnboardingEnabled_UnknownIdentity_DoesNotCreateTenantOrMembership()
    {
        // Arrange
        var unknownEntra = Guid.NewGuid();
        var (mw, http) = await BuildMiddlewareAsync(unknownEntra, allowSelfOnboarding: true);

        // Act
        await mw.InvokeAsync(
            http,
            http.RequestServices.GetRequiredService<ITenantContext>(),
            http.RequestServices.GetRequiredService<ITenantContextAccessor>(),
            BuildImpersonationStub(),
            Options.Create(BuildDeployment(allowSelfOnboarding: true)),
            Options.Create(new RoleClaimMappingsOptions()),
            http.RequestServices.GetRequiredService<IMemoryCache>(),
            http.RequestServices.GetRequiredService<AtoCopilotContext>(),
            BuildConfiguration(),
            BuildCspProfileStub());

        // Assert
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var row = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.EntraTenantId == unknownEntra);
        row.Should().BeNull("directory affiliation is not an enrollment grant");
        (await db.OrganizationMemberships.CountAsync()).Should().Be(0);
        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task SelfOnboardingDisabled_UnknownIdentity_Writes403NoTenantAssignment()
    {
        // Arrange
        var unknownEntra = Guid.NewGuid();
        var (mw, http) = await BuildMiddlewareAsync(unknownEntra, allowSelfOnboarding: false);
        // Hit a path that is *not* on the bypass list so the resolver runs.
        http.Request.Path = "/api/onboarding/tenant/state";

        // Act
        await mw.InvokeAsync(
            http,
            http.RequestServices.GetRequiredService<ITenantContext>(),
            http.RequestServices.GetRequiredService<ITenantContextAccessor>(),
            BuildImpersonationStub(),
            Options.Create(BuildDeployment(allowSelfOnboarding: false)),
            Options.Create(new RoleClaimMappingsOptions()),
            http.RequestServices.GetRequiredService<IMemoryCache>(),
            http.RequestServices.GetRequiredService<AtoCopilotContext>(),
            BuildConfiguration(),
            BuildCspProfileStub());

        // Assert
        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var body = await ReadBodyAsync(http);
        body.Should().Contain("\"errorCode\":\"NO_TENANT_ASSIGNMENT\"");
    }

    private async Task<(TenantResolutionMiddleware, DefaultHttpContext)> BuildMiddlewareAsync(
        Guid entraTid, bool allowSelfOnboarding)
    {
        var middleware = new TenantResolutionMiddleware(
            next: _ => Task.CompletedTask,
            logger: NullLogger<TenantResolutionMiddleware>.Instance);

        var scope = _sp.CreateAsyncScope();
        var http = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        http.Request.Method = "GET";
        http.Request.Path = "/api/deployment/mode";
        http.Response.Body = new MemoryStream();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("tid", entraTid.ToString()),
            new Claim("oid", "00000000-0000-0000-0000-000000000099"),
            new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000099"),
        }, "Test");
        http.User = new ClaimsPrincipal(identity);

        await Task.Yield();
        return (middleware, http);
    }

    private static DeploymentOptions BuildDeployment(bool allowSelfOnboarding)
    {
        return new DeploymentOptions
        {
            Mode = DeploymentMode.MultiTenant,
            Tenants = new TenantPolicyOptions
            {
                AllowSelfOnboarding = allowSelfOnboarding,
            },
        };
    }

    private static IConfiguration BuildConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenant:Resolution:BypassForTests"] = "false",
            })
            .Build();

    private static ITenantImpersonationService BuildImpersonationStub()
    {
        var mock = new Mock<ITenantImpersonationService>();
        mock.SetupGet(s => s.CookieName).Returns("ato.impersonation");
        mock.Setup(s => s.Validate(It.IsAny<string>())).Returns((ImpersonationCookiePayload?)null);
        return mock.Object;
    }

    /// <summary>
    /// Provides an Active singleton CspProfile so the FR-090 CSP-onboarding
    /// gate (added in commit a180962) does not short-circuit the
    /// self-onboarding tests with a 503.
    /// </summary>
    private static ICspProfileService BuildCspProfileStub()
    {
        var mock = new Mock<ICspProfileService>();
        mock.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CspProfile
            {
                LegalEntityName = "Test CSP",
                DisplayName = "Test",
                PrimarySupportEmail = "test@example.com",
                OnboardingState = OnboardingState.Active,
            });
        return mock.Object;
    }

    private static async Task<string> ReadBodyAsync(HttpContext http)
    {
        http.Response.Body.Position = 0;
        using var reader = new StreamReader(http.Response.Body);
        return await reader.ReadToEndAsync();
    }
}

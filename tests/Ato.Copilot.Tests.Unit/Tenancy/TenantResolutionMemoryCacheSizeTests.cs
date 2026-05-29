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
/// Regression: when <c>IMemoryCache</c> is configured with a <c>SizeLimit</c>
/// (the production configuration — see
/// <c>CoreServiceExtensions.AddCoreServices</c>), every <c>Set</c> call MUST
/// declare <c>Size</c>. The simple <c>cache.Set(key, value, TimeSpan)</c>
/// overload omits it and throws
/// <c>InvalidOperationException: "Cache entry must specify a value for Size
/// when SizeLimit is set."</c> at runtime, which surfaces as a 500 on every
/// tenant-resolution hit.
/// </summary>
/// <remarks>
/// Bug surfaced after Feature 048 (T064 / T068) shipped because the
/// integration test fixture uses <c>services.AddMemoryCache()</c> with no
/// <c>SizeLimit</c>, so the bug went undetected until first Docker boot
/// against a host using <c>CoreServiceExtensions.AddCoreServices</c>.
/// </remarks>
public class TenantResolutionMemoryCacheSizeTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        // Reproduce production config: IMemoryCache with SizeLimit set.
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 1024 * 1024; // 1 MiB
        });
        services.AddDbContext<AtoCopilotContext>(opt => opt.UseSqlite(_connection));
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddScoped<ITenantContext, TenantContext>();
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
    public async Task KnownEntraTenant_WithSizeLimitedCache_DoesNotThrow()
    {
        // Arrange
        var entraTid = Guid.NewGuid();
        var internalId = Guid.NewGuid();
        await using (var seed = _sp.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Tenants.Add(new Tenant
            {
                Id = internalId,
                EntraTenantId = entraTid,
                DisplayName = "Test",
                Status = TenantStatus.Active,
                OnboardingState = OnboardingState.Active,
                CreatedBy = "test",
            });
            await db.SaveChangesAsync();
        }

        var (mw, http) = BuildMiddleware(entraTid);

        // Act
        var act = async () => await mw.InvokeAsync(
            http,
            http.RequestServices.GetRequiredService<ITenantContext>(),
            http.RequestServices.GetRequiredService<ITenantContextAccessor>(),
            BuildImpersonationStub(),
            Options.Create(new DeploymentOptions { Mode = DeploymentMode.MultiTenant }),
            Options.Create(new RoleClaimMappingsOptions()),
            http.RequestServices.GetRequiredService<IMemoryCache>(),
            http.RequestServices.GetRequiredService<AtoCopilotContext>(),
            BuildConfiguration(),
            BuildCspProfileStub());

        // Assert — must NOT throw "Cache entry must specify a value for Size".
        await act.Should().NotThrowAsync<InvalidOperationException>();
        http.Response.StatusCode.Should().Be(StatusCodes.Status200OK,
            "the middleware should fall through to _next when tenant resolution succeeds");
    }

    [Fact]
    public async Task SelfOnboardedTenant_WithSizeLimitedCache_DoesNotThrow()
    {
        // Arrange — unknown tid + AllowSelfOnboarding=true exercises the
        // BuildCacheOptions() path inside the self-onboarding branch.
        var entraTid = Guid.NewGuid();
        var (mw, http) = BuildMiddleware(entraTid);

        // Act
        var act = async () => await mw.InvokeAsync(
            http,
            http.RequestServices.GetRequiredService<ITenantContext>(),
            http.RequestServices.GetRequiredService<ITenantContextAccessor>(),
            BuildImpersonationStub(),
            Options.Create(new DeploymentOptions
            {
                Mode = DeploymentMode.MultiTenant,
                Tenants = new TenantPolicyOptions { AllowSelfOnboarding = true },
            }),
            Options.Create(new RoleClaimMappingsOptions()),
            http.RequestServices.GetRequiredService<IMemoryCache>(),
            http.RequestServices.GetRequiredService<AtoCopilotContext>(),
            BuildConfiguration(),
            BuildCspProfileStub());

        // Assert
        await act.Should().NotThrowAsync<InvalidOperationException>();
    }

    private (TenantResolutionMiddleware mw, DefaultHttpContext http) BuildMiddleware(Guid entraTid)
    {
        var middleware = new TenantResolutionMiddleware(
            next: ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            logger: NullLogger<TenantResolutionMiddleware>.Instance);

        var scope = _sp.CreateAsyncScope();
        var http = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        http.Request.Method = "GET";
        http.Request.Path = "/api/dashboard/systems";
        http.Response.Body = new MemoryStream();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("tid", entraTid.ToString()),
            new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000099"),
        }, "Test");
        http.User = new ClaimsPrincipal(identity);

        return (middleware, http);
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
}

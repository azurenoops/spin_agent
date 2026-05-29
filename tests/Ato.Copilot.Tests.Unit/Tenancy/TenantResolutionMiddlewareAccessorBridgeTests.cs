using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
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
/// Regression: <see cref="TenantResolutionMiddleware"/> populates the scoped
/// <see cref="ITenantContext"/> with the correct EffectiveTenantId, but
/// <see cref="AtoCopilotContext"/>'s global query filter reads from
/// <see cref="ITenantContextAccessor"/> (AsyncLocal-backed singleton).
/// Without an explicit <c>accessor.Push(ctx)</c> bridging the two,
/// <c>TenantFilterDisabled</c> is always <c>true</c> and every
/// <c>[TenantScoped]</c> query returns rows from ALL tenants — a silent
/// cross-tenant data leak (Constitution § Security: Tenant Isolation
/// NON-NEGOTIABLE).
/// </summary>
/// <remarks>
/// Bug surfaced 2026-05-29 during Feature 051 live sign-off on a 3-tenant
/// Flankspeed portfolio: every impersonation showed all 5 systems regardless
/// of which tenant was selected. Audit logs proved the middleware had the
/// correct <c>EffectiveTenantId</c> in its scoped <c>ITenantContext</c> —
/// the data simply never reached the EF filter because no production code
/// path called <c>accessor.Push</c>. Fix: middleware now wraps its
/// downstream <c>_next</c> call in <c>using var _ = accessor.Push(ctx);</c>
/// so the AsyncLocal flows through every awaited continuation.
/// </remarks>
public class TenantResolutionMiddlewareAccessorBridgeTests : IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EntraTidA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private SqliteConnection _connection = null!;
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContext<AtoCopilotContext>(opt => opt.UseSqlite(_connection));
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddScoped<ITenantContext, TenantContext>();
        _sp = services.BuildServiceProvider();

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await db.Database.EnsureCreatedAsync();

        // Seed two tenants and two RegisteredSystems (one per tenant) so
        // we can prove the query filter cleanly partitions rows by TenantId.
        db.Tenants.Add(new Tenant
        {
            Id = TenantA,
            EntraTenantId = EntraTidA,
            DisplayName = "T-A",
            Status = TenantStatus.Active,
            OnboardingState = OnboardingState.Active,
            CreatedBy = "test",
        });
        db.Tenants.Add(new Tenant
        {
            Id = TenantB,
            DisplayName = "T-B",
            Status = TenantStatus.Active,
            OnboardingState = OnboardingState.Active,
            CreatedBy = "test",
        });
        db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = TenantA,
            Name = "System-A",
            Acronym = "SYS-A",
            Description = "Tenant A system",
            CreatedBy = "test",
            IsActive = true,
        });
        db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = TenantB,
            Name = "System-B",
            Acronym = "SYS-B",
            Description = "Tenant B system",
            CreatedBy = "test",
            IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Middleware_BridgesScopedContext_IntoAsyncLocalAccessor()
    {
        // Arrange — a request authenticated as a Tenant-A user (no CSP-Admin
        // claim, no impersonation cookie). Inside _next we query
        // RegisteredSystems; the global query filter should restrict the
        // result to TenantA's rows only.
        var accessor = _sp.GetRequiredService<ITenantContextAccessor>();
        List<RegisteredSystem> seenInsideNext = new();
        bool accessorWasNullBeforeMiddleware = accessor.Current is null;

        var middleware = new TenantResolutionMiddleware(
            next: async ctx =>
            {
                // Inside the downstream pipeline, the AsyncLocal MUST be
                // populated — that's the whole contract under test.
                accessor.Current.Should().NotBeNull(
                    "TenantResolutionMiddleware must Push the scoped ITenantContext " +
                    "onto the AsyncLocal accessor that EF's global filter reads");
                accessor.Current!.EffectiveTenantId.Should().Be(TenantA);

                var db = ctx.RequestServices.GetRequiredService<AtoCopilotContext>();
                seenInsideNext = await db.RegisteredSystems.ToListAsync();
                ctx.Response.StatusCode = StatusCodes.Status200OK;
            },
            logger: NullLogger<TenantResolutionMiddleware>.Instance);

        var (http, scope) = BuildHttpContextForTenant(EntraTidA);

        try
        {
            // Act
            await middleware.InvokeAsync(
                http,
                http.RequestServices.GetRequiredService<ITenantContext>(),
                accessor,
                BuildImpersonationStub(),
                Options.Create(new DeploymentOptions { Mode = DeploymentMode.MultiTenant }),
                Options.Create(new RoleClaimMappingsOptions()),
                http.RequestServices.GetRequiredService<IMemoryCache>(),
                http.RequestServices.GetRequiredService<AtoCopilotContext>(),
                BuildConfiguration(),
                BuildCspProfileStub());
        }
        finally
        {
            await scope.DisposeAsync();
        }

        // Assert — the EF query inside _next saw only Tenant A's system.
        // Pre-fix this returned BOTH rows (silent cross-tenant leak).
        accessorWasNullBeforeMiddleware.Should().BeTrue(
            "the AsyncLocal must be empty before the middleware runs, otherwise " +
            "the test isn't proving the bridge — it's just observing prior state");
        accessor.Current.Should().BeNull(
            "the Push must be disposed when the middleware returns so the " +
            "AsyncLocal doesn't leak across requests");
        seenInsideNext.Should().HaveCount(1,
            "the global query filter must restrict to TenantA only");
        seenInsideNext[0].TenantId.Should().Be(TenantA);
        seenInsideNext[0].Acronym.Should().Be("SYS-A");
    }

    [Fact]
    public async Task Middleware_CspAdmin_WithoutImpersonation_SeesAllTenants()
    {
        // Arrange — a CSP-Admin (Role claim) with no impersonation cookie.
        // FR-026: the filter must short-circuit and return rows from EVERY
        // tenant. This is the FR-026 acceptance path; we verify it still
        // works once the AsyncLocal bridge is in place.
        var accessor = _sp.GetRequiredService<ITenantContextAccessor>();
        List<RegisteredSystem> seenInsideNext = new();

        var middleware = new TenantResolutionMiddleware(
            next: async ctx =>
            {
                accessor.Current.Should().NotBeNull();
                accessor.Current!.IsCspAdmin.Should().BeTrue();
                accessor.Current.ImpersonatedTenantId.Should().BeNull();

                var db = ctx.RequestServices.GetRequiredService<AtoCopilotContext>();
                seenInsideNext = await db.RegisteredSystems.ToListAsync();
                ctx.Response.StatusCode = StatusCodes.Status200OK;
            },
            logger: NullLogger<TenantResolutionMiddleware>.Instance);

        var (http, scope) = BuildHttpContextForTenant(EntraTidA, isCspAdmin: true);

        try
        {
            // Act
            await middleware.InvokeAsync(
                http,
                http.RequestServices.GetRequiredService<ITenantContext>(),
                accessor,
                BuildImpersonationStub(),
                Options.Create(new DeploymentOptions { Mode = DeploymentMode.MultiTenant }),
                Options.Create(new RoleClaimMappingsOptions()),
                http.RequestServices.GetRequiredService<IMemoryCache>(),
                http.RequestServices.GetRequiredService<AtoCopilotContext>(),
                BuildConfiguration(),
                BuildCspProfileStub());
        }
        finally
        {
            await scope.DisposeAsync();
        }

        // Assert — CSP-Admin without impersonation sees rows from BOTH tenants.
        seenInsideNext.Should().HaveCount(2,
            "FR-026: CSP-Admin without impersonation bypasses the tenant filter");
    }

    private (DefaultHttpContext http, AsyncServiceScope scope) BuildHttpContextForTenant(
        Guid entraTid,
        bool isCspAdmin = false)
    {
        var scope = _sp.CreateAsyncScope();
        var http = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        http.Request.Method = "GET";
        http.Request.Path = "/api/dashboard/portfolio";
        http.Response.Body = new MemoryStream();

        var claims = new List<Claim>
        {
            new("tid", entraTid.ToString()),
            new(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000099"),
        };
        if (isCspAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "CSP.Admin"));
        }
        var identity = new ClaimsIdentity(claims, "Test");
        http.User = new ClaimsPrincipal(identity);

        return (http, scope);
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

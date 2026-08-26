using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Interceptors;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

/// <summary>
/// DEF-006 — Always-run (no Docker) contract test proving that EF Core's
/// per-tenant <c>HasQueryFilter</c> closures prevent Tenant B from reading
/// rows owned by Tenant A, using an in-process SQLite database.
///
/// <para>
/// Coverage ensures CI never silently skips RLS-equivalent protection
/// because Docker / SQL Server is unavailable.
/// </para>
/// </summary>
public class TenantQueryFilterIsolationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _sp = null!;
    private TenantContextAccessor _accessor = null!;

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0001-0001-0001-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0002-0002-0002-bbbbbbbbbbbb");

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<TenantStampingSaveChangesInterceptor>();
        services.AddDbContext<AtoCopilotContext>((sp, opt) =>
        {
            opt.UseSqlite(_connection);
            opt.AddInterceptors(
                sp.GetRequiredService<TenantStampingSaveChangesInterceptor>());
        });

        _sp = services.BuildServiceProvider();
        _accessor = (TenantContextAccessor)_sp.GetRequiredService<ITenantContextAccessor>();

        // Seed: two tenants, one organization per tenant.
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = TenantA, DisplayName = "Alpha Corp", CreatedBy = "seed" });
        db.Tenants.Add(new Tenant { Id = TenantB, DisplayName = "Bravo Inc", CreatedBy = "seed" });
        await db.SaveChangesAsync();

        // Write org rows scoped to each tenant, bypassing the interceptor
        // by pushing each tenant's context while inserting.
        using (_accessor.Push(new TenantContext(TenantA)))
        {
            db.Organizations.Add(new Organization
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                Name = "Alpha Org",
                CreatedBy = "seed",
            });
            await db.SaveChangesAsync();
        }

        using (_accessor.Push(new TenantContext(TenantB)))
        {
            db.Organizations.Add(new Organization
            {
                Id = Guid.NewGuid(),
                TenantId = TenantB,
                Name = "Bravo Org",
                CreatedBy = "seed",
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>
    /// Tenant A's context must only see Tenant A's organizations — not Tenant B's.
    /// </summary>
    [Fact]
    public async Task TenantA_CannotRead_TenantB_Organizations()
    {
        // Arrange
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Act — query under Tenant A context
        using (_accessor.Push(new TenantContext(TenantA)))
        {
            var orgs = await db.Organizations.ToListAsync();

            // Assert — only Tenant A's org is visible
            orgs.Should().ContainSingle(o => o.TenantId == TenantA,
                "query filter must scope results to the active tenant");
            orgs.Should().NotContain(o => o.TenantId == TenantB,
                "query filter must hide rows from other tenants");
        }
    }

    /// <summary>
    /// Tenant B's context must only see Tenant B's organizations — not Tenant A's.
    /// </summary>
    [Fact]
    public async Task TenantB_CannotRead_TenantA_Organizations()
    {
        // Arrange
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Act — query under Tenant B context
        using (_accessor.Push(new TenantContext(TenantB)))
        {
            var orgs = await db.Organizations.ToListAsync();

            // Assert
            orgs.Should().ContainSingle(o => o.TenantId == TenantB,
                "query filter must scope results to the active tenant");
            orgs.Should().NotContain(o => o.TenantId == TenantA,
                "query filter must hide rows from other tenants");
        }
    }

    /// <summary>
    /// IgnoreQueryFilters() allows a CSP-Admin code-path to read all rows, which
    /// confirms the filter IS active (not absent) on the normal path.
    /// </summary>
    [Fact]
    public async Task IgnoreQueryFilters_ReturnsAllTenants_ProvingFilterIsActive()
    {
        // Arrange
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Act — bypass filter (mirrors CSP-Admin global-read path)
        using (_accessor.Push(new TenantContext(TenantA)))
        {
            var allOrgs = await db.Organizations.IgnoreQueryFilters().ToListAsync();

            // Assert — both tenant rows are visible when filter is bypassed
            allOrgs.Should().Contain(o => o.TenantId == TenantA,
                "IgnoreQueryFilters must reveal TenantA rows");
            allOrgs.Should().Contain(o => o.TenantId == TenantB,
                "IgnoreQueryFilters must reveal TenantB rows — proving the filter was hiding them");
        }
    }
}

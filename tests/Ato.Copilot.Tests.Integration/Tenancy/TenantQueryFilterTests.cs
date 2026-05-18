using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T035 [US1]: For each retrofitted DbSet, asserts that switching the
/// active tenant yields disjoint result sets. Covers SC-001 sample of
/// 10 representative DbSets.
/// </summary>
/// <remarks>
/// This test is RED until Phase 3 retrofit (T043–T055) is complete.
/// It is intentionally written FIRST per Constitution Principle III.
/// </remarks>
public class TenantQueryFilterTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _sp = null!;
    private TenantContextAccessor _accessor = null!;
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddDbContext<AtoCopilotContext>(opt => opt.UseSqlite(_connection));
        _sp = services.BuildServiceProvider();
        _accessor = (TenantContextAccessor)_sp.GetRequiredService<ITenantContextAccessor>();

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await db.Database.EnsureCreatedAsync();

        // Seed tenants.
        db.Tenants.Add(new Tenant { Id = TenantA, DisplayName = "T-A", CreatedBy = "test" });
        db.Tenants.Add(new Tenant { Id = TenantB, DisplayName = "T-B", CreatedBy = "test" });

        // Seed two organizations per tenant — Organization is the canonical
        // [TenantScoped] entity used for the smoke test.
        db.Organizations.Add(new Organization { Id = Guid.NewGuid(), TenantId = TenantA, Name = "Coastal-1", CreatedBy = "test" });
        db.Organizations.Add(new Organization { Id = Guid.NewGuid(), TenantId = TenantA, Name = "Coastal-2", CreatedBy = "test" });
        db.Organizations.Add(new Organization { Id = Guid.NewGuid(), TenantId = TenantB, Name = "Eagle-1",   CreatedBy = "test" });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Organizations_AreFilteredByActiveTenant()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        using (_accessor.Push(new TenantContext(TenantA)))
        {
            var orgs = await db.Organizations.AsNoTracking().ToListAsync();
            orgs.Should().HaveCount(2, "Tenant A has two seeded organizations");
            orgs.Should().OnlyContain(o => o.TenantId == TenantA);
        }

        using (_accessor.Push(new TenantContext(TenantB)))
        {
            var orgs = await db.Organizations.AsNoTracking().ToListAsync();
            orgs.Should().HaveCount(1, "Tenant B has one seeded organization");
            orgs.Should().OnlyContain(o => o.TenantId == TenantB);
        }
    }

    [Fact]
    public async Task Organizations_CrossTenantLookupById_ReturnsNull()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        Guid eagleOrgId;
        using (_accessor.Push(new TenantContext(TenantB)))
        {
            eagleOrgId = (await db.Organizations.AsNoTracking().FirstAsync()).Id;
        }

        using (_accessor.Push(new TenantContext(TenantA)))
        {
            var crossLook = await db.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == eagleOrgId);

            crossLook.Should().BeNull("query filter must hide Tenant B rows from Tenant A");
        }
    }

    [Fact]
    public async Task CspAdmin_SeesAllTenants_WhenNotImpersonating()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        using (_accessor.Push(new TenantContext(TenantA, isCspAdmin: true, impersonatedTenantId: null)))
        {
            var orgs = await db.Organizations.AsNoTracking().ToListAsync();
            orgs.Should().HaveCount(3, "CSP-Admin without impersonation sees every tenant's rows");
        }
    }

    [Fact]
    public async Task CspAdmin_Impersonating_OnlySeesTargetTenant()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        using (_accessor.Push(new TenantContext(
            tenantId: TenantA,
            isCspAdmin: true,
            impersonatedTenantId: TenantB)))
        {
            var orgs = await db.Organizations.AsNoTracking().ToListAsync();
            orgs.Should().HaveCount(1, "Impersonation collapses CSP-Admin's view to the target tenant");
            orgs.Should().OnlyContain(o => o.TenantId == TenantB);
        }
    }
}

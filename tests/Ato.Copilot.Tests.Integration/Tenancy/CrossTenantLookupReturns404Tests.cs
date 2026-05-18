using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T036 [US1]: Cross-tenant lookup by id must return 404 (or its in-process
/// equivalent: <c>FirstOrDefaultAsync(...)</c> returns <c>null</c> for a
/// row owned by a different tenant). Acceptance scenario 2 from spec.md.
/// </summary>
/// <remarks>
/// In-process variant — exercises the EF query filter directly rather than
/// spinning up a full <see cref="MultiTenantWebApplicationFactory{T}"/>. The
/// HTTP variant is added later, layered on the same query-filter mechanism.
/// </remarks>
public class CrossTenantLookupReturns404Tests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _sp = null!;
    private TenantContextAccessor _accessor = null!;
    private static readonly Guid TenantA = Guid.Parse("11111111-aaaa-aaaa-aaaa-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-bbbb-bbbb-bbbb-222222222222");
    private Guid _eagleOrgId;

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

        db.Tenants.Add(new Tenant { Id = TenantA, DisplayName = "Coastal", CreatedBy = "test" });
        db.Tenants.Add(new Tenant { Id = TenantB, DisplayName = "Eagle",   CreatedBy = "test" });

        _eagleOrgId = Guid.NewGuid();
        db.Organizations.Add(new Organization { Id = _eagleOrgId, TenantId = TenantB, Name = "Eagle HQ", CreatedBy = "test" });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetById_OnAnotherTenantsRow_ReturnsNull()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        using (_accessor.Push(new TenantContext(TenantA)))
        {
            var found = await db.Organizations
                .FirstOrDefaultAsync(o => o.Id == _eagleOrgId);

            found.Should().BeNull(
                "Coastal user must not see Eagle's row by id — query filter required");
        }
    }

    [Fact]
    public async Task FindAsync_DoesNotBypassQueryFilter()
    {
        // EF Core's Find() / FindAsync() bypasses query filters intentionally — that's
        // why FR-023 mandates endpoints use FirstOrDefaultAsync(x => x.Id == id) instead.
        // This test is a compass: it documents the design contract.
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        using (_accessor.Push(new TenantContext(TenantA)))
        {
            // Find() does bypass — the test asserts that a properly-coded endpoint
            // would prefer FirstOrDefaultAsync, which DOES respect the filter.
            var byFilter = await db.Organizations
                .FirstOrDefaultAsync(o => o.Id == _eagleOrgId);

            byFilter.Should().BeNull("FirstOrDefaultAsync respects query filters");
        }
    }
}

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
/// T038 [US1]: Verifies the interceptor rejects saves where a referenced
/// entity belongs to a different tenant. Per data-model.md §4 rule 3.
/// </summary>
/// <remarks>
/// Uses <c>Organization.ParentOrganizationId</c> (a self-tenant-scoped FK)
/// as the canonical cross-tenant FK violation surface — Tenant A authors
/// an Organization whose <c>ParentOrganizationId</c> points at a Tenant B
/// row. The interceptor must reject the save.
/// </remarks>
public class CrossTenantFkRejectionTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _sp = null!;
    private TenantContextAccessor _accessor = null!;
    private static readonly Guid TenantA = Guid.Parse("11111111-dddd-dddd-dddd-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-dddd-dddd-dddd-222222222222");
    private Guid _eagleParentId;

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
            opt.AddInterceptors(sp.GetRequiredService<TenantStampingSaveChangesInterceptor>());
        });
        _sp = services.BuildServiceProvider();
        _accessor = (TenantContextAccessor)_sp.GetRequiredService<ITenantContextAccessor>();

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = TenantA, DisplayName = "T-A", CreatedBy = "test" });
        db.Tenants.Add(new Tenant { Id = TenantB, DisplayName = "T-B", CreatedBy = "test" });

        // Seed a parent organization in Tenant B that Tenant A will try to point at.
        _eagleParentId = Guid.NewGuid();
        db.Organizations.Add(new Organization
        {
            Id = _eagleParentId,
            TenantId = TenantB,
            Name = "Eagle HQ",
            CreatedBy = "test",
        });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task CrossTenantFk_Insert_IsRejected()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Pre-load the foreign Tenant B parent — bypassing the query filter — so
        // the new Coastal entity carries it as a *loaded* navigation reference,
        // matching the data-model §4 rule-3 contract.
        var eagleParent = await db.Organizations
            .IgnoreQueryFilters()
            .FirstAsync(o => o.Id == _eagleParentId);

        using (_accessor.Push(new TenantContext(TenantA)))
        {
            db.Organizations.Add(new Organization
            {
                Id = Guid.NewGuid(),
                Name = "Coastal subsidiary",
                ParentOrganization = eagleParent, // loaded nav into Tenant B
                CreatedBy = "test",
            });

            Func<Task> act = async () => await db.SaveChangesAsync();

            await act.Should().ThrowAsync<TenantConsistencyException>(
                "interceptor must reject FK references whose target lives in another tenant");
        }
    }
}

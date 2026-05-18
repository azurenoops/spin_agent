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

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T037 [US1]: Verifies <c>TenantStampingSaveChangesInterceptor</c> (FR-021):
///   • Adding an entity without <c>TenantId</c> (or <c>Guid.Empty</c>) causes
///     the interceptor to stamp <c>EffectiveTenantId</c>.
///   • Setting a <c>TenantId</c> that differs from <c>EffectiveTenantId</c>
///     raises <c>TenantConsistencyException</c>.
/// </summary>
/// <remarks>
/// RED until T040 is implemented and registered (T041).
/// </remarks>
public class SaveChangesStampingTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _sp = null!;
    private TenantContextAccessor _accessor = null!;
    private static readonly Guid TenantA = Guid.Parse("11111111-cccc-cccc-cccc-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-cccc-cccc-cccc-222222222222");

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

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Add_WithEmptyTenantId_StampedFromEffectiveTenantId()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        using (_accessor.Push(new TenantContext(TenantA)))
        {
            var org = new Organization
            {
                Id = Guid.NewGuid(),
                Name = "Stamped",
                CreatedBy = "test",
                // TenantId left as default (Guid.Empty) — interceptor must stamp it.
            };
            db.Organizations.Add(org);
            await db.SaveChangesAsync();

            org.TenantId.Should().Be(TenantA, "interceptor must stamp empty TenantId");
        }
    }

    [Fact]
    public async Task Add_WithMismatchedTenantId_ThrowsTenantConsistencyException()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        using (_accessor.Push(new TenantContext(TenantA, isCspAdmin: false)))
        {
            db.Organizations.Add(new Organization
            {
                Id = Guid.NewGuid(),
                TenantId = TenantB, // mismatched and actor is NOT csp-admin
                Name = "Sneaky",
                CreatedBy = "test",
            });

            Func<Task> act = async () => await db.SaveChangesAsync();

            await act.Should().ThrowAsync<TenantConsistencyException>(
                "non-CSP-Admin actors cannot insert rows with foreign TenantId");
        }
    }

    [Fact]
    public async Task Modify_TenantIdChange_IsForbidden()
    {
        Guid orgId;
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            using (_accessor.Push(new TenantContext(TenantA)))
            {
                var org = new Organization
                {
                    Id = Guid.NewGuid(),
                    Name = "Stable",
                    CreatedBy = "test",
                };
                db.Organizations.Add(org);
                await db.SaveChangesAsync();
                orgId = org.Id;
            }
        }

        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            using (_accessor.Push(new TenantContext(TenantA)))
            {
                var org = await db.Organizations.FirstAsync(o => o.Id == orgId);
                org.TenantId = TenantB; // attempt to change tenant ownership
                Func<Task> act = async () => await db.SaveChangesAsync();
                await act.Should().ThrowAsync<TenantConsistencyException>(
                    "TenantId changes on Modified entries are forbidden");
            }
        }
    }
}

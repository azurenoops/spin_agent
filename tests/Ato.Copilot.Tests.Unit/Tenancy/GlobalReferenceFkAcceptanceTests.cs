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
/// T132 / T136 [Phase 10 / FR-080]: validates the
/// <see cref="TenantStampingSaveChangesInterceptor"/> [GlobalReference]
/// escape hatch.
///
/// Inserts a <see cref="GlobalBaseline"/> ([GlobalReference]) under an
/// active <see cref="TenantContext"/>; the interceptor MUST NOT attempt to
/// stamp <c>TenantId</c> on it (the entity has no such property) and MUST
/// NOT reject the save.
///
/// The cross-tenant FK rejection contract (the negative case for FR-080) is
/// validated by <see cref="CrossTenantFkRejectionTests"/>; this fixture
/// covers the positive case introduced by T136.
/// </summary>
public class GlobalReferenceFkAcceptanceTests : IAsyncLifetime
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");

    private SqliteConnection _connection = default!;
    private ServiceProvider _sp = default!;
    private TenantContextAccessor _accessor = default!;

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
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GlobalReferenceEntity_IsNotStamped_AndIsNotRejected()
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        using (_accessor.Push(new TenantContext(TenantA)))
        {
            var baseline = new GlobalBaseline
            {
                Id = Guid.NewGuid(),
                Kind = "ControlNarrative",
                SourceId = Guid.NewGuid(),
                SourceTenantId = TenantA,
                PublishedBy = "test",
            };
            db.GlobalBaselines.Add(baseline);

            Func<Task> act = async () => await db.SaveChangesAsync();
            await act.Should().NotThrowAsync(
                "[GlobalReference] entities are exempt from FR-021 tenant-stamping and FR-080 cross-tenant FK rejection.");

            var roundtrip = await db.GlobalBaselines.AsNoTracking()
                .FirstAsync(b => b.Id == baseline.Id);
            roundtrip.SourceTenantId.Should().Be(TenantA);
            roundtrip.Kind.Should().Be("ControlNarrative");
        }
    }
}

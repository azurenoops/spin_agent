using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Interceptors;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class CapabilityServiceTenantIsolationTests : IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-9320-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-9320-0000-0000-000000000002");

    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;
    private TenantContextAccessor _tenantAccessor = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<TenantStampingSaveChangesInterceptor>();
        services.AddDbContext<AtoCopilotContext>((provider, options) =>
        {
            options.UseSqlite(_connection);
            options.AddInterceptors(provider.GetRequiredService<TenantStampingSaveChangesInterceptor>());
        });
        _services = services.BuildServiceProvider();
        _tenantAccessor = (TenantContextAccessor)_services.GetRequiredService<ITenantContextAccessor>();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await db.Database.EnsureCreatedAsync();
        db.Tenants.AddRange(
            new Tenant { Id = TenantA, DisplayName = "Tenant A", CreatedBy = "test" },
            new Tenant { Id = TenantB, DisplayName = "Tenant B", CreatedBy = "test" });
        await db.SaveChangesAsync();

        using (_tenantAccessor.Push(new TenantContext(TenantA)))
        {
            db.RegisteredSystems.Add(CreateSystem("system-a"));
            db.SecurityCapabilities.Add(CreateCapability("cap-a", "Tenant A Capability"));
            await db.SaveChangesAsync();
        }

        using (_tenantAccessor.Push(new TenantContext(TenantB)))
        {
            db.RegisteredSystems.Add(CreateSystem("system-b"));
            db.SecurityCapabilities.Add(CreateCapability("cap-b", "Tenant B Capability"));
            await db.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetAvailableCapabilities_UsesTenantFilteredSystemAndCatalog()
    {
        // Arrange
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var sut = new CapabilityService(
            db,
            Mock.Of<ILogger<CapabilityService>>(),
            new NarrativeTemplateService(),
            Mock.Of<IDeviationService>(),
            Mock.Of<IOrgInheritanceService>());

        // Act
        using var tenantScope = _tenantAccessor.Push(new TenantContext(TenantA));
        var ownCatalog = await sut.GetAvailableCapabilitiesAsync("system-a", search: null);
        var otherTenantCatalog = await sut.GetAvailableCapabilitiesAsync("system-b", search: null);

        // Assert
        ownCatalog.Should().NotBeNull();
        ownCatalog!.Items.Should().ContainSingle(item => item.Id == "cap-a");
        ownCatalog.Items.Should().NotContain(item => item.Id == "cap-b");
        otherTenantCatalog.Should().BeNull();
    }

    private static RegisteredSystem CreateSystem(string id) => new()
    {
        Id = id,
        Name = id,
        SystemType = SystemType.MajorApplication,
        MissionCriticality = MissionCriticality.MissionSupport,
        HostingEnvironment = "Azure Government",
        CreatedBy = "test",
        IsActive = true,
    };

    private static SecurityCapability CreateCapability(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Provider = "Test Provider",
        Category = "AC",
        Description = $"{name} description",
        ImplementationStatus = CapabilityStatus.Implemented,
        Owner = "test",
        CreatedBy = "test",
    };
}

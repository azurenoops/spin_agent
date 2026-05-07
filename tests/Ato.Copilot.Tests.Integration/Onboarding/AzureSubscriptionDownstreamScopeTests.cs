using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.AzureSubscriptions;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// Confirms downstream Azure-touching features will scope to the selected
/// subscription set via <see cref="AzureSubscriptionScopeResolver"/>
/// (T097 / FR-072 / SC-010). This is the contract test — actual downstream
/// service wiring happens in their respective query paths consuming
/// <see cref="Core.Interfaces.Onboarding.IAzureSubscriptionScopeResolver"/>.
/// </summary>
public class AzureSubscriptionDownstreamScopeTests : IDisposable
{
    private readonly TestDbContextFactory _factory;

    public AzureSubscriptionDownstreamScopeTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"DownstreamScope_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task Resolver_ScopesQueryToTenantsSelectedSubscriptions()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var aSelected = Guid.NewGuid();
        var aUnavailable = Guid.NewGuid();
        var bSelected = Guid.NewGuid();

        await using (var seed = _factory.CreateDbContext())
        {
            seed.AzureSubscriptionRegistrations.AddRange(
                new AzureSubscriptionRegistration
                {
                    Id = Guid.NewGuid(), TenantId = tenantA,
                    SubscriptionId = aSelected, DisplayName = "A-sel",
                    Status = SubscriptionStatus.Selected,
                },
                new AzureSubscriptionRegistration
                {
                    Id = Guid.NewGuid(), TenantId = tenantA,
                    SubscriptionId = aUnavailable, DisplayName = "A-unavail",
                    Status = SubscriptionStatus.Unavailable,
                },
                new AzureSubscriptionRegistration
                {
                    Id = Guid.NewGuid(), TenantId = tenantB,
                    SubscriptionId = bSelected, DisplayName = "B-sel",
                    Status = SubscriptionStatus.Selected,
                });
            await seed.SaveChangesAsync();
        }

        var resolver = new AzureSubscriptionScopeResolver(_factory);
        var aIds = await resolver.GetSelectedSubscriptionIdsAsync(tenantA);
        var bIds = await resolver.GetSelectedSubscriptionIdsAsync(tenantB);

        aIds.Should().ContainSingle().Which.Should().Be(aSelected, "Unavailable rows are excluded from scope");
        bIds.Should().ContainSingle().Which.Should().Be(bSelected, "tenant isolation");
    }

    private sealed class TestDbContextFactory : Microsoft.EntityFrameworkCore.IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}

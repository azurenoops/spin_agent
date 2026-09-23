using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Workspaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class ProviderCatalogSqlServerTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableTheory]
    [InlineData("component")]
    [InlineData("capability")]
    public async Task CatalogAggregates_MatchSubscriberJoin_WithCaseVariantReferences(string grouping)
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.EnsureCreatedAsync();
        var profile = new CspProfile { DisplayName = "Provider", LegalEntityName = "Provider Ltd" };
        var component = new CspInheritedComponent { CspProfileId = profile.Id, Name = "Component" };
        var capability = new CspInheritedCapability { CspInheritedComponentId = component.Id, Name = "Capability" };
        var tenantA = new Tenant { DisplayName = "A" };
        var tenantB = new Tenant { DisplayName = "B" };
        db.Set<CspProfile>().Add(profile);
        db.CspInheritedComponents.Add(component);
        db.CspInheritedCapabilities.Add(capability);
        db.Tenants.AddRange(tenantA, tenantB);
        db.RegisteredSystems.AddRange(
            new RegisteredSystem { Id = "a-1", TenantId = tenantA.Id, Name = "A1", CreatedBy = "test" },
            new RegisteredSystem { Id = "a-2", TenantId = tenantA.Id, Name = "A2", CreatedBy = "test" },
            new RegisteredSystem { Id = "b-1", TenantId = tenantB.Id, Name = "B1", CreatedBy = "test" });
        await db.SaveChangesAsync();
        foreach (var (tenant, system, uppercase) in new[]
        {
            (tenantA.Id, "a-1", false), (tenantA.Id, "a-1", true),
            (tenantA.Id, "a-2", true), (tenantB.Id, "b-1", false),
            (tenantB.Id, "a-1", false)
        })
            db.CapabilitySubscriptions.Add(new CapabilitySubscription
            {
                Id = Guid.NewGuid().ToString(), RegisteredSystemId = system, RoutingTenantId = tenant,
                CspInheritedCapabilityId = uppercase ? capability.Id.ToString().ToUpperInvariant() : capability.Id.ToString(),
                IsActive = true
            });
        await db.SaveChangesAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(db.Database.GetConnectionString(), sql => sql.EnableRetryOnFailure()).Options;
        var service = new WorkspaceOperationsService(new ContextFactory(options));

        // Act
        var page = await service.ListProviderCatalogAsync(
            new(Grouping: grouping, ComponentId: component.Id.ToString().ToUpperInvariant()), default);
        var subscribers = await service.ListProviderSubscribersAsync(capability.Id, 1, 50, default);

        // Assert
        page.Items.Should().ContainSingle();
        page.Items[0].DistinctAdoptionCount.Should().Be(3);
        page.Items[0].DistinctOrganizationCount.Should().Be(2);
        subscribers.Items.Select(x => (x.OrganizationId, x.SystemId)).Distinct().Should().HaveCount(3);
        subscribers.Items.Should().NotContain(x => x.OrganizationId == tenantB.Id && x.SystemId == "a-1");
    }

    private sealed class ContextFactory(DbContextOptions<AtoCopilotContext> options) : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
    }
}

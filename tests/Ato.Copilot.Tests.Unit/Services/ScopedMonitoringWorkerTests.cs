using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class ScopedMonitoringWorkerTests
{
    [Fact]
    public async Task Worker_discovers_only_routing_ids_then_enters_each_tenant_scope()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var accessor = new TenantContextAccessor();
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor>(accessor);
        services.AddScoped<ITenantContext, TenantContext>();
        var database = Guid.NewGuid().ToString();
        services.AddDbContextFactory<AtoCopilotContext>(b => b.UseInMemoryDatabase(database));
        services.AddScoped<ScopedMonitoringService>();
        services.AddSingleton(Mock.Of<INarrativeChangeImpactService>());
        await using var provider = services.BuildServiceProvider();
        foreach (var tenant in new[] { tenantA, tenantB })
        {
            await using var scope = provider.CreateAsyncScope();
            using var pushed = accessor.Push(new TenantContext(tenant));
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var systemId = tenant.ToString();
            db.RegisteredSystems.Add(new() { Id = systemId, TenantId = tenant, Name = "Mission" });
            db.AuthorizationBoundaryDefinitions.Add(new() { Id = systemId, TenantId = tenant, RegisteredSystemId = systemId, Name = "Boundary" });
            db.SystemComponents.Add(new() { Id = systemId, TenantId = tenant, Name = "Unmapped local component" });
            db.BoundaryComponentAssignments.Add(new() { TenantId = tenant, SystemComponentId = systemId, AuthorizationBoundaryDefinitionId = systemId });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<ScopedMonitoringService>().SaveRuleAsync(systemId, null,
                new("Monitor", systemId, "Reviewed baseline", "reviewer", "Alert", new("Type", "Equals", "Drift"), 60, "High", true), "actor", default);
        }
        using var defaultContext = accessor.Push(new TenantContext(Guid.NewGuid()));
        var worker = new ComplianceWatchHostedService(provider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>(),
            Mock.Of<IComplianceWatchService>(), Mock.Of<IAlertManager>(), Mock.Of<IComplianceEventSource>(),
            Options.Create(new MonitoringOptions()), NullLogger<ComplianceWatchHostedService>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>(), accessor);

        // Act
        await worker.RunTenantChecksAsync(default);

        // Assert
        foreach (var tenant in new[] { tenantA, tenantB })
        {
            using var pushed = accessor.Push(new TenantContext(tenant));
            await using var db = await provider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>().CreateDbContextAsync();
            var evaluation = await db.Set<MonitoringRuleEvaluation>().SingleAsync(x => x.Outcome != "Configured");
            evaluation.TenantId.Should().Be(tenant);
            evaluation.Outcome.Should().Be("CollectionUnavailable");
            (await db.AlertRules.SingleAsync()).LastEvaluatedAt.Should().NotBeNull();
            (await db.Set<MonitoringImpactReview>().CountAsync()).Should().Be(0);
        }
    }
}

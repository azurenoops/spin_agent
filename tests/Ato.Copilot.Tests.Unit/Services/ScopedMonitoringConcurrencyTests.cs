using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class ScopedMonitoringConcurrencyTests
{
    [Fact]
    public async Task Concurrent_disable_rolls_back_review_work_and_schema_is_replay_safe()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new BeforeReviewSave();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).AddInterceptors(interceptor).Options;
        await using var db = new AtoCopilotContext(options);
        await db.Database.EnsureCreatedAsync();
        await ScopedMonitoringSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await ScopedMonitoringSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var tenant = Guid.NewGuid();
        const string resource = "/subscriptions/shared/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/resource";
        db.RegisteredSystems.Add(new() { Id = "system", TenantId = tenant, Name = "Mission" });
        db.AuthorizationBoundaryDefinitions.Add(new() { Id = "boundary", TenantId = tenant, RegisteredSystemId = "system", Name = "Boundary" });
        db.SystemComponents.Add(new() { Id = "component", TenantId = tenant, RegisteredSystemId = "system", Name = "Resource", AzureResourceId = resource });
        db.BoundaryComponentAssignments.Add(new() { TenantId = tenant, SystemComponentId = "component", AuthorizationBoundaryDefinitionId = "boundary" });
        db.ComplianceAlerts.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, Type = AlertType.Drift, AffectedResources = new() { resource }, SubscriptionId = "shared" });
        db.MonitoringConfigurations.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, SubscriptionId = "shared", IsEnabled = true,
            LastRunAt = DateTimeOffset.UtcNow, NextRunAt = DateTimeOffset.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        var service = new ScopedMonitoringService(db, Mock.Of<INarrativeChangeImpactService>());
        var rule = await service.SaveRuleAsync("system", null, new("Rule", "boundary", "reviewed", "owner", "Alert",
            new("Type", "Equals", "Drift"), 60, "High", true), "actor", default);
        interceptor.Callback = async () =>
        {
            await using var concurrent = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
            await concurrent.Database.ExecuteSqlRawAsync("UPDATE AlertRules SET IsEnabled = 0, Version = 2 WHERE Id = {0}", rule.Id);
        };

        // Act
        var evaluate = () => service.EvaluateDueAsync(default);

        // Assert
        await evaluate.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var verify = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        (await verify.Set<MonitoringImpactReview>().CountAsync()).Should().Be(0);
        (await verify.Set<MonitoringRuleEvaluation>().CountAsync(x => x.Outcome == "Matched")).Should().Be(0);
        (await verify.AlertRules.SingleAsync()).IsEnabled.Should().BeFalse();
    }

    private sealed class BeforeReviewSave : SaveChangesInterceptor
    {
        public Func<Task>? Callback { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Callback is { } callback && eventData.Context!.ChangeTracker.Entries<MonitoringImpactReview>()
                .Any(x => x.State == EntityState.Added))
            {
                Callback = null;
                await callback();
            }
            return result;
        }
    }
}

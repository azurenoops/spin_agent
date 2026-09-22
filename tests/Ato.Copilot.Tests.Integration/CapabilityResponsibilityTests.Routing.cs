using System.Net.Http.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

public sealed partial class CapabilityResponsibilityTests
{
    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("invalid-type")]
    [InlineData("inherited-no-provider")]
    [InlineData("shared-no-responsibility")]
    [InlineData("customer-no-responsibility")]
    [InlineData("provider-too-long")]
    public async Task Routing_ConfirmationRejectsInvalidAllocationWithoutPersistingPartialWork(string invalid)
    {
        // Arrange
        await AddBaselineAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        var preview = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Route}/responsibilities");
        var item = preview.GetProperty("items").EnumerateArray().First();
        var allocation = new CapabilityResponsibilityAllocation("AU-6", "Shared", "Synthetic provider", "Customer reviews");
        allocation = invalid switch
        {
            "invalid-type" => allocation with { InheritanceType = "0" },
            "inherited-no-provider" => allocation with { InheritanceType = "Inherited", Provider = null },
            "shared-no-responsibility" => allocation with { CustomerResponsibility = null },
            "customer-no-responsibility" => allocation with { InheritanceType = "Customer", CustomerResponsibility = null },
            "provider-too-long" => allocation with { Provider = new string('x', 201) },
            _ => allocation
        };
        CapabilityResponsibilityAllocation[] allocations = invalid == "empty" ? [] : invalid == "duplicate" ? [allocation, allocation] : [allocation];
        var request = new ConfirmCapabilityResponsibilitiesRequest(preview.GetProperty("baselineId").GetString()!,
            item.GetProperty("sourceRevision").GetString()!, item.GetProperty("reviewRevision").GetString()!, allocations);

        // Act
        var response = await _client.PutAsJsonAsync($"{Route}/{_capability}/responsibilities", request);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<CapabilityResponsibilityConfirmation>().CountAsync()).Should().Be(0);
        (await verify.ControlInheritances.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("TenantInactive", false)]
    [InlineData("ObsoleteRoute", true)]
    [InlineData("MissingActor", false)]
    public async Task Routing_InvalidatedPrerequisitesHaveExplicitDurableOutcomes(string outcome, bool complete)
    {
        // Arrange
        await SeedRoutingAsync(1);
        await using var db = new AtoCopilotContext(_options);
        await CapabilityResponsibilityRouting.ExpandAsync(db);
        var claim = (await CapabilityResponsibilityRouting.ClaimAsync(db)).Single();
        if (outcome == "TenantInactive") (await db.Tenants.SingleAsync(t => t.Id == _tenant)).Status = TenantStatus.Disabled;
        if (outcome == "ObsoleteRoute") (await db.CapabilitySubscriptions.SingleAsync()).IsActive = false;
        if (outcome == "MissingActor") (await db.Set<CspResponsibilitySourceEvent>().SingleAsync()).Actor = null;
        await db.SaveChangesAsync();
        var receiver = new CspResponsibilityFanoutService(db, new TenantContext(_tenant),
            _app.Services.GetRequiredService<ISystemWorkspaceAccessService>(), _impactConsumer.Object, NullLoggerFactory.Instance);

        // Act
        await receiver.ProcessAsync(claim);

        // Assert
        var delivery = await db.Set<CapabilityResponsibilityDelivery>().SingleAsync();
        delivery.Outcome.Should().Be(outcome);
        delivery.CompletedAt.HasValue.Should().Be(complete);
        delivery.LeaseToken.Should().BeNull();
        (await db.ControlInheritances.CountAsync()).Should().Be(0);
        _impactConsumer.Verify(s => s.QueueAsync(It.IsAny<NarrativeChangeImpactRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Routing_ForeignClaimCannotUseAnotherCustomerContext()
    {
        // Arrange
        await SeedRoutingAsync(1);
        await using var db = new AtoCopilotContext(_options);
        await CapabilityResponsibilityRouting.ExpandAsync(db);
        var claim = (await CapabilityResponsibilityRouting.ClaimAsync(db)).Single();
        var receiver = new CspResponsibilityFanoutService(db, new TenantContext(Guid.NewGuid()),
            _app.Services.GetRequiredService<ISystemWorkspaceAccessService>(), _impactConsumer.Object, NullLoggerFactory.Instance);

        // Act
        Func<Task> process = () => receiver.ProcessAsync(claim);

        // Assert
        await process.Should().ThrowAsync<UnauthorizedAccessException>();
        (await db.Set<CapabilityResponsibilityDelivery>().SingleAsync()).CompletedAt.Should().BeNull();
        (await db.ControlInheritances.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Routing_SchemaBootstrapsSubscriptionPrerequisiteBeforeRoutingColumns()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE CapabilitySubscriptions");

        // Act
        await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        (await db.CapabilitySubscriptions.CountAsync()).Should().Be(0);
        (await _client.PostAsJsonAsync(Route, new { capabilityId = _capability })).EnsureSuccessStatusCode();
        var subscription = await db.CapabilitySubscriptions.SingleAsync();
        subscription.RoutingTenantId.Should().Be(_tenant);
        subscription.RoutingCapabilityId.Should().Be(_capability.ToString());
    }

    [Fact]
    public async Task Routing_SchemaPreservesHistoricalSubscriptionAndBackfillsActualOwner()
    {
        // Arrange
        await using var db = new AtoCopilotContext(_options);
        (db.Model.FindEntityType(typeof(CapabilitySubscription))?.GetTableName()).Should().Be("CapabilitySubscriptions");
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE CapabilitySubscriptions;
            CREATE TABLE CapabilitySubscriptions (
              Id TEXT NOT NULL PRIMARY KEY, RegisteredSystemId TEXT NOT NULL,
              CspInheritedCapabilityId TEXT NOT NULL, SubscribedBy TEXT NOT NULL,
              SubscribedAt TEXT NOT NULL, IsActive INTEGER NOT NULL);
            """);
        var subscriptionId = Guid.NewGuid().ToString();
        var originalCapabilityId = _capability.ToString().ToUpperInvariant();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO CapabilitySubscriptions
              (Id,RegisteredSystemId,CspInheritedCapabilityId,SubscribedBy,SubscribedAt,IsActive)
            VALUES ({subscriptionId},{_system},{originalCapabilityId},{"historical-fixture-reviewer"},{DateTime.UtcNow},{true})
            """);

        // Act
        await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        var subscription = await db.CapabilitySubscriptions.SingleAsync();
        subscription.Id.Should().Be(subscriptionId);
        subscription.CspInheritedCapabilityId.Should().Be(originalCapabilityId);
        subscription.SubscribedBy.Should().Be("historical-fixture-reviewer");
        subscription.RoutingTenantId.Should().Be(_tenant);
        subscription.RoutingCapabilityId.Should().Be(_capability.ToString());
        subscription.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Routing_ExpansionIsBoundedCheckpointedAndRepeatable()
    {
        // Arrange
        await SeedRoutingAsync(105, 21);
        await using var db = new AtoCopilotContext(_options);

        // Act
        await CapabilityResponsibilityRouting.ExpandAsync(db);

        // Assert
        (await db.Set<CapabilityResponsibilityDelivery>().CountAsync()).Should().Be(20 * 100);
        (await db.Set<CspResponsibilitySourceEvent>().CountAsync(e => e.LastSubscriptionId != null)).Should().Be(20);
        (await db.Set<CspResponsibilitySourceEvent>().CountAsync(e => e.FanoutCompleted)).Should().Be(0);
        await CapabilityResponsibilityRouting.ExpandAsync(db);
        await CapabilityResponsibilityRouting.ExpandAsync(db);
        await CapabilityResponsibilityRouting.ExpandAsync(db);
        (await db.Set<CapabilityResponsibilityDelivery>().CountAsync()).Should().Be(21 * 105);
        (await db.Set<CspResponsibilitySourceEvent>().CountAsync(e => e.FanoutCompleted)).Should().Be(21);
        (await db.ControlInheritances.CountAsync()).Should().Be(0, "routing metadata must not invent responsibility allocations");
    }

    [Fact]
    public async Task Routing_ExpansionCommitFailureRollsBackCursorAndDeliveries()
    {
        // Arrange
        await SeedRoutingAsync(5);
        _commitFailure.FailNext = true;
        await using (var failed = new AtoCopilotContext(_options))
        {
            // Act
            Func<Task> expand = () => CapabilityResponsibilityRouting.ExpandAsync(failed);

            // Assert
            await expand.Should().ThrowAsync<InvalidOperationException>().WithMessage("Synthetic commit failure");
        }
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<CapabilityResponsibilityDelivery>().CountAsync()).Should().Be(0);
        (await verify.Set<CspResponsibilitySourceEvent>().SingleAsync()).LastSubscriptionId.Should().BeNull();
        await CapabilityResponsibilityRouting.ExpandAsync(verify);
        (await verify.Set<CapabilityResponsibilityDelivery>().CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task Routing_ClaimsAreBoundedExclusiveAndExpiredLeasesAreRetried()
    {
        // Arrange
        await SeedRoutingAsync(105, 2);
        await using var db = new AtoCopilotContext(_options);
        await CapabilityResponsibilityRouting.ExpandAsync(db);
        var first = await CapabilityResponsibilityRouting.ClaimAsync(db);
        var second = await CapabilityResponsibilityRouting.ClaimAsync(db);

        // Act
        await db.Set<CapabilityResponsibilityDelivery>().Where(d => d.Id == first[0].Id)
            .ExecuteUpdateAsync(set => set.SetProperty(d => d.LeaseUntilUtcTicks, DateTime.UtcNow.AddMinutes(-1).Ticks));
        var reclaimed = await CapabilityResponsibilityRouting.ClaimAsync(db);

        // Assert
        first.Should().HaveCount(100);
        second.Should().HaveCount(100);
        first.Select(c => c.Id).Intersect(second.Select(c => c.Id)).Should().BeEmpty();
        reclaimed.Should().ContainSingle(c => c.Id == first[0].Id);
        reclaimed.Single(c => c.Id == first[0].Id).LeaseToken.Should().NotBe(first[0].LeaseToken);
        var service = new CspResponsibilityFanoutService(db, new TenantContext(_tenant),
            _app.Services.GetRequiredService<ISystemWorkspaceAccessService>(), _impactConsumer.Object, NullLoggerFactory.Instance);
        Func<Task> staleClaim = () => service.ProcessAsync(first[0]);
        await staleClaim.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Routing_MissingBaselineDefersWithoutBlockingAnotherTarget()
    {
        // Arrange
        var systems = await SeedRoutingAsync(2);
        await using var db = new AtoCopilotContext(_options);
        db.ControlBaselines.Add(new() { TenantId = _tenant, RegisteredSystemId = systems[1], BaselineLevel = "Moderate",
            ControlIds = ["AC-2"], TotalControls = 1, CreatedBy = "fixture-author" });
        await db.SaveChangesAsync();
        await CapabilityResponsibilityRouting.ExpandAsync(db);
        var claims = await CapabilityResponsibilityRouting.ClaimAsync(db);
        var service = new CspResponsibilityFanoutService(db, new TenantContext(_tenant),
            _app.Services.GetRequiredService<ISystemWorkspaceAccessService>(), _impactConsumer.Object, NullLoggerFactory.Instance);

        // Act
        foreach (var claim in claims) await service.ProcessAsync(claim);

        // Assert
        var deferred = await db.Set<CapabilityResponsibilityDelivery>().SingleAsync(d => d.SourceEventId != null && d.RegisteredSystemId == systems[0]);
        deferred.Outcome.Should().Be("MissingBaseline");
        deferred.CompletedAt.Should().BeNull();
        deferred.NextAttemptUtcTicks.Should().BeGreaterThan(DateTime.UtcNow.Ticks);
        (await db.Set<CapabilityResponsibilityDelivery>().SingleAsync(d => d.SourceEventId != null && d.RegisteredSystemId == systems[1]))
            .CompletedAt.Should().NotBeNull();
        (await CapabilityResponsibilityRouting.ClaimAsync(db)).Should().NotContain(c => c.Id == deferred.Id);
        (await db.ControlInheritances.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Routing_CustomerTransactionPersistsControlCursorAndImpactPointersAtomically()
    {
        // Arrange
        var systems = await SeedRoutingAsync(1);
        await using var db = new AtoCopilotContext(_options);
        var controls = Enumerable.Range(1, 105).Select(i => $"AC-{i:D3}").ToList();
        (await db.CspInheritedCapabilities.SingleAsync()).MappedNistControlIds = controls;
        await CspResponsibilitySourceTracker.StageAsync(db, "provider-routing-fixture", default);
        db.ControlBaselines.Add(new() { TenantId = _tenant, RegisteredSystemId = systems[0], BaselineLevel = "Moderate",
            ControlIds = controls, TotalControls = controls.Count, CreatedBy = "fixture-author" });
        await db.SaveChangesAsync();
        await CapabilityResponsibilityRouting.ExpandAsync(db);
        var service = new CspResponsibilityFanoutService(db, new TenantContext(_tenant),
            _app.Services.GetRequiredService<ISystemWorkspaceAccessService>(), _impactConsumer.Object, NullLoggerFactory.Instance);

        // Act
        foreach (var claim in await CapabilityResponsibilityRouting.ClaimAsync(db))
            await service.ProcessAsync(claim);

        // Assert
        (await db.Set<CapabilityResponsibilityImpact>().CountAsync()).Should().Be(100);
        (await db.Set<CapabilityResponsibilityDelivery>().CountAsync(d => d.ImpactId != null)).Should().Be(100);
        var partial = await db.Set<CapabilityResponsibilityDelivery>().SingleAsync(d => d.Outcome == "MoreControls");
        partial.ControlCursor.Should().Be("AC-100");
        partial.CompletedAt.Should().BeNull();
        (await db.ControlInheritances.CountAsync()).Should().Be(0);
        var next = (await CapabilityResponsibilityRouting.ClaimAsync(db)).Single(c => c.Id == partial.Id);
        await service.ProcessAsync(next);
        (await db.Set<CapabilityResponsibilityImpact>().CountAsync()).Should().Be(105);
        (await db.Set<CapabilityResponsibilityDelivery>().CountAsync(d => d.ImpactId != null)).Should().Be(105);
        partial.ControlCursor.Should().Be("AC-105");
        partial.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Routing_AckFailureAfterQueueCommit_DoesNotReopenReviewedWorkOnReplay()
    {
        // Arrange
        await AddBaselineAsync();
        await AddNarrativesAsync();
        await _client.PostAsJsonAsync(Route, new { capabilityId = _capability });
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();
        ResponsibilityDeliveryClaim claim;
        Guid impactId;
        await using (var routing = new AtoCopilotContext(_options))
        {
            impactId = await routing.Set<CapabilityResponsibilityImpact>().Where(i => i.ControlId == "AU-6")
                .OrderByDescending(i => i.Revision).Select(i => i.Id).FirstAsync();
            var deliveryId = await routing.Set<CapabilityResponsibilityDelivery>().Where(d => d.ImpactId == impactId).Select(d => d.Id).SingleAsync();
            claim = (await CapabilityResponsibilityRouting.ClaimAsync(routing)).Single(c => c.Id == deliveryId);
        }
        var generator = new Mock<IControlNarrativeService>(MockBehavior.Strict);
        _routingAckFailure.FailNextCompletion = true;

        // Act
        await using (var customerDb = new AtoCopilotContext(_options))
        {
            var tenant = new TenantContext(_tenant);
            var queue = new NarrativeProposalService(customerDb, tenant, new NarrativeLibraryService(customerDb, tenant), generator.Object);
            var receiver = new CspResponsibilityFanoutService(customerDb, tenant,
                _app.Services.GetRequiredService<ISystemWorkspaceAccessService>(), queue, NullLoggerFactory.Instance);
            Func<Task> deliver = () => receiver.ProcessAsync(claim);
            (await deliver.Should().ThrowAsync<DbUpdateException>()).WithInnerException<IOException>()
                .WithMessage("Synthetic routing acknowledgment failure");
        }
        await using (var routing = new AtoCopilotContext(_options))
        {
            (await routing.Set<CapabilityResponsibilityImpact>().SingleAsync(i => i.Id == impactId)).AcknowledgedAt.Should().NotBeNull();
            (await routing.Set<CapabilityResponsibilityDelivery>().SingleAsync(d => d.Id == claim.Id)).CompletedAt.Should().BeNull();
            foreach (var proposal in await routing.NarrativeProposals.ToListAsync()) proposal.Status = "NeedsRevision";
            await routing.SaveChangesAsync();
            await CapabilityResponsibilityRouting.RetryAsync(routing, claim, "DeliveryFailed", default);
            await routing.Set<CapabilityResponsibilityDelivery>().Where(d => d.Id == claim.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(d => d.NextAttemptUtcTicks, 0));
            claim = (await CapabilityResponsibilityRouting.ClaimAsync(routing)).Single(c => c.Id == claim.Id);
        }
        await using (var customerDb = new AtoCopilotContext(_options))
        {
            var tenant = new TenantContext(_tenant);
            var queue = new NarrativeProposalService(customerDb, tenant, new NarrativeLibraryService(customerDb, tenant), generator.Object);
            await new CspResponsibilityFanoutService(customerDb, tenant,
                _app.Services.GetRequiredService<ISystemWorkspaceAccessService>(), queue, NullLoggerFactory.Instance).ProcessAsync(claim);
        }

        // Assert
        await using var verify = new AtoCopilotContext(_options);
        (await verify.NarrativeProposals.ToListAsync()).Should().HaveCount(2).And.OnlyContain(p => p.Status == "NeedsRevision");
        (await verify.Set<CapabilityResponsibilityDelivery>().SingleAsync(d => d.Id == claim.Id)).CompletedAt.Should().NotBeNull();
        (await verify.Set<NarrativeImpactReceipt>().CountAsync()).Should().Be(2);
        generator.Verify(g => g.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private async Task<IReadOnlyList<string>> SeedRoutingAsync(int subscribers, int events = 1)
    {
        await using var db = new AtoCopilotContext(_options);
        var systems = new List<string>();
        for (var i = 0; i < subscribers; i++)
        {
            var id = i == 0 ? _system : Guid.NewGuid().ToString();
            if (i > 0) db.RegisteredSystems.Add(new() { Id = id, TenantId = _tenant, Name = $"Synthetic routing system {i}", CreatedBy = "fixture" });
            systems.Add(id);
            db.CapabilitySubscriptions.Add(new() { Id = $"routing-{i:D4}", RegisteredSystemId = id,
                CspInheritedCapabilityId = _capability.ToString(), RoutingTenantId = _tenant,
                RoutingCapabilityId = _capability.ToString(), SubscribedBy = "fixture-subscriber" });
        }
        await db.SaveChangesAsync();
        var capability = await db.CspInheritedCapabilities.SingleAsync();
        for (var i = 0; i < events; i++)
        {
            capability.Description = $"Synthetic provider event {i}";
            await CspResponsibilitySourceTracker.StageAsync(db, "provider-routing-fixture", default);
            await db.SaveChangesAsync();
        }
        return systems;
    }
}

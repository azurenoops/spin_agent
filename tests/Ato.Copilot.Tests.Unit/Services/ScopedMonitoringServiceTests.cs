using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class ScopedMonitoringServiceTests : IDisposable
{
    private readonly AtoCopilotContext db;
    private readonly ScopedMonitoringService service;
    private readonly Mock<INarrativeChangeImpactService> narratives = new();
    private readonly Guid tenant = Guid.NewGuid();
    private readonly TenantContextAccessor accessor = new();
    private readonly IDisposable pushed;
    private const string SystemA = "system-a";
    private const string SystemB = "system-b";
    private const string ResourceA = "/subscriptions/shared/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/a";
    private const string ResourceB = "/subscriptions/shared/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/b";

    public ScopedMonitoringServiceTests()
    {
        db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, accessor);
        pushed = accessor.Push(new TenantContext(tenant));
        service = new ScopedMonitoringService(db, narratives.Object);
        foreach (var (id, resource) in new[] { (SystemA, ResourceA), (SystemB, ResourceB) })
        {
            db.RegisteredSystems.Add(new() { Id = id, TenantId = tenant, Name = id, CreatedBy = "test" });
            db.AuthorizationBoundaryDefinitions.Add(new() { Id = id + "-boundary", TenantId = tenant,
                RegisteredSystemId = id, Name = id, CreatedBy = "test" });
            db.SystemComponents.Add(new() { Id = id + "-component", TenantId = tenant, Name = id,
                RegisteredSystemId = id, AzureResourceId = resource, CreatedBy = "test" });
            db.BoundaryComponentAssignments.Add(new() { Id = id + "-assignment", TenantId = tenant,
                AuthorizationBoundaryDefinitionId = id + "-boundary", SystemComponentId = id + "-component", CreatedBy = "test" });
            db.ComplianceAlerts.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, Title = id,
                SubscriptionId = "shared", Type = AlertType.Drift, ControlId = "SC-7",
                AffectedResources = new() { resource }, CreatedAt = DateTimeOffset.UtcNow });
        }
        db.MonitoringConfigurations.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, SubscriptionId = "shared",
            IsEnabled = true, LastRunAt = DateTimeOffset.UtcNow, NextRunAt = DateTimeOffset.UtcNow.AddHours(1) });
        db.SaveChanges();
    }

    private static SaveMonitoringRuleRequest Rule(string system = SystemA, long? version = null, bool enabled = true) =>
        new("Network changes", system + "-boundary", "reviewed-baseline-1", "reviewer", "Alert",
            new("Type", "Equals", "Drift"), 60, "High", enabled, version);

    [Fact]
    public async Task IndependentProviderReference_DoesNotRequireAzureSubscriptionReconciliation()
    {
        // Arrange
        var resolver = new Mock<ISystemEnvironmentScopeResolver>();
        resolver.Setup(x => x.ResolveAsync(SystemA, EnvironmentScopePurpose.Monitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSystemEnvironmentScopes(SystemA, 0, [],
                [new("provider-scope", "ProviderHosting", "Synthetic provider scope", "Valid", "No subscription link is required.")]));
        await using var provider = new ServiceCollection().AddSingleton(resolver.Object).BuildServiceProvider();
        var canonical = ActivatorUtilities.CreateInstance<ScopedMonitoringService>(provider, db, narratives.Object);

        // Act
        var coverage = await canonical.CoverageAsync(SystemA, default);

        // Assert
        coverage.Should().ContainSingle().Which.Health.Should().Be("Missing");
        coverage.Should().NotContain(x => x.Health == "ReconciliationRequired");
    }

    [Theory]
    [InlineData("OrganizationOwned")]
    [InlineData("ProviderAllocation")]
    public async Task IndependentSubscriptionCoverage_DoesNotRequireProviderComponentOrRelationship(string sourceKind)
    {
        // Arrange
        var resolver = new Mock<ISystemEnvironmentScopeResolver>();
        var source = CanonicalScope(ResourceA, true) with
        {
            Source = sourceKind,
            AllocationId = sourceKind == "ProviderAllocation" ? Guid.NewGuid() : null,
            AllocationVersion = sourceKind == "ProviderAllocation" ? 1 : null
        };
        resolver.Setup(x => x.ResolveAsync(SystemA, EnvironmentScopePurpose.Monitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSystemEnvironmentScopes(SystemA, 1, [source], []));
        await using var provider = new ServiceCollection().AddSingleton(resolver.Object).BuildServiceProvider();
        var canonical = ActivatorUtilities.CreateInstance<ScopedMonitoringService>(provider, db, narratives.Object);

        // Act
        var coverage = await canonical.CoverageAsync(SystemA, default);

        // Assert
        var row = coverage.Should().ContainSingle().Which;
        row.Health.Should().Be("ScopeUnsupported");
        row.ProviderComponentId.Should().BeNull();
        row.AttachmentId.Should().Be(source.AttachmentId);
    }

    [Fact]
    public async Task CanonicalCoverage_DeniedAndUnsupportedSourcesCannotLookHealthy()
    {
        // Arrange
        var resolver = new Mock<ISystemEnvironmentScopeResolver>();
        var scopes = new[] { CanonicalScope(ResourceA, true), CanonicalScope(ResourceB, false),
            CanonicalScope("/subscriptions/third/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/c", true) };
        resolver.Setup(x => x.ResolveAsync(SystemA, EnvironmentScopePurpose.Monitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSystemEnvironmentScopes(SystemA, 1, scopes, []));
        await using var provider = new ServiceCollection().AddSingleton(resolver.Object).BuildServiceProvider();
        var canonical = ActivatorUtilities.CreateInstance<ScopedMonitoringService>(provider, db, narratives.Object);

        // Act
        var coverage = await canonical.CoverageAsync(SystemA, default);

        // Assert
        coverage.Should().HaveCount(3);
        coverage.Should().NotContain(x => x.Health == "Healthy");
        coverage.Should().Contain(x => x.Health == "Ineligible" && x.Error == "Allocation withdrawn");
    }

    [Fact]
    public async Task CanonicalWithdrawal_PreservesRetainedObservationsButCreatesNoNewImpact()
    {
        // Arrange
        var rule = await service.SaveRuleAsync(SystemA, null, Rule(), "actor", default);
        await service.EvaluateDueAsync(default);
        var retainedCount = await db.Set<MonitoringImpactReview>().CountAsync();
        rule.NextEvaluationUtcTicks = 0;
        await db.SaveChangesAsync();
        var resolver = new Mock<ISystemEnvironmentScopeResolver>();
        resolver.Setup(x => x.ResolveAsync(It.IsAny<string>(), EnvironmentScopePurpose.Monitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, EnvironmentScopePurpose _, CancellationToken _) =>
                new ResolvedSystemEnvironmentScopes(id, 2, [CanonicalScope(ResourceA, false)], []));
        await using var provider = new ServiceCollection().AddSingleton(resolver.Object).BuildServiceProvider();
        var canonical = ActivatorUtilities.CreateInstance<ScopedMonitoringService>(provider, db, narratives.Object);

        // Act
        var history = await canonical.ChangesAsync(SystemA, default);
        var evaluation = await canonical.TestAsync(SystemA, rule.Id, default);

        // Assert
        history.Should().ContainSingle();
        evaluation.Should().OnlyContain(x => x.Outcome == "CollectionUnavailable");
        (await db.Set<MonitoringImpactReview>().CountAsync()).Should().Be(retainedCount);
    }

    private ResolvedSystemEnvironmentScope CanonicalScope(string resource, bool eligible) =>
        new(Guid.NewGuid(), 1, Guid.NewGuid(), 1,
            new(Guid.NewGuid(), tenant, Guid.NewGuid(), Guid.NewGuid(), "Government", "Synthetic subscription",
                "Selected", DateTimeOffset.UtcNow), "ProviderAllocation", Guid.NewGuid(), 1, eligible,
            eligible ? null : "Allocation withdrawn", [resource], [], [],
            new("Test", null, null, "Reconciled", null, DateTimeOffset.UtcNow));

    [Fact]
    public async Task LegacyEnvironmentCoverage_IsNotHealthyBeforeScopeReconciliation()
    {
        // Arrange
        var resolver = new Mock<ISystemEnvironmentScopeResolver>();
        resolver.Setup(x => x.ResolveAsync(SystemA, EnvironmentScopePurpose.Monitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSystemEnvironmentScopes(SystemA, 0, [],
                [new("legacy-subscription", "AzureProfile", "Synthetic", "ReconciliationRequired", "Review resource scope.")]));
        await using var provider = new ServiceCollection().AddSingleton(resolver.Object).BuildServiceProvider();
        var canonical = ActivatorUtilities.CreateInstance<ScopedMonitoringService>(provider, db, narratives.Object);

        // Act
        var coverage = await canonical.CoverageAsync(SystemA, default);

        // Assert
        coverage.Should().ContainSingle().Which.Health.Should().Be("ReconciliationRequired");
        coverage.Should().NotContain(x => x.Health == "Healthy");
    }

    [Fact]
    public async Task Shared_subscription_is_not_attribution_and_scope_changes_require_review()
    {
        // Arrange
        var rule = await service.SaveRuleAsync(SystemA, null, Rule(), "actor", default);
        // Act
        var changes = await service.ChangesAsync(SystemA, default);
        await service.EvaluateDueAsync(default);
        // Assert
        changes.Should().ContainSingle().Which.Title.Should().Be(SystemA);
        db.Set<MonitoringImpactReview>().Should().ContainSingle().Which.RegisteredSystemId.Should().Be(SystemA);
        // Arrange
        (await db.BoundaryComponentAssignments.SingleAsync(x => x.Id == SystemA + "-assignment")).IsInScope = false;
        rule.NextEvaluationUtcTicks = 0;
        await db.SaveChangesAsync();
        // Act
        var preview = await service.TestAsync(SystemA, rule.Id, default);
        // Assert
        preview.Should().OnlyContain(x => x.Outcome == "ScopeReviewRequired");
    }

    [Fact]
    public async Task Replay_deduplicates_and_disabled_rule_creates_no_work()
    {
        // Arrange
        var rule = await service.SaveRuleAsync(SystemA, null, Rule(), "actor", default);
        // Act
        await service.EvaluateDueAsync(default);
        rule.NextEvaluationUtcTicks = 0;
        await db.SaveChangesAsync();
        await service.EvaluateDueAsync(default);
        await service.SaveRuleAsync(SystemA, rule.Id, Rule(version: 1, enabled: false), "actor", default);
        await service.EvaluateDueAsync(default);
        // Assert
        db.Set<MonitoringImpactReview>().Should().ContainSingle();
        db.Set<MonitoringRuleEvaluation>().Count(x => x.Outcome == "Matched").Should().Be(1);
        (await service.TestAsync(SystemA, rule.Id, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Collection_failure_is_not_a_healthy_zero_match()
    {
        // Arrange
        var rule = await service.SaveRuleAsync(SystemA, null, Rule(), "actor", default);
        (await db.MonitoringConfigurations.SingleAsync()).CollectionError = "CollectionFailed";
        await db.SaveChangesAsync();
        // Act
        await service.EvaluateDueAsync(default);
        // Assert
        db.Set<MonitoringImpactReview>().Should().BeEmpty();
        db.Set<MonitoringRuleEvaluation>().Where(x => x.Outcome != "Configured")
            .Should().OnlyContain(x => x.Outcome == "CollectionUnavailable");
        (await service.CoverageAsync(SystemA, default)).Should().ContainSingle().Which.Health.Should().Be("Failed");
        // Arrange
        (await db.MonitoringConfigurations.SingleAsync()).CollectionError = null;
        rule.NextEvaluationUtcTicks = 0;
        await db.SaveChangesAsync();
        // Act
        await service.EvaluateDueAsync(default);
        // Assert
        db.Set<MonitoringImpactReview>().Should().ContainSingle();
    }

    [Fact]
    public async Task Foreign_tenant_rules_and_inputs_are_invisible()
    {
        // Arrange
        await service.SaveRuleAsync(SystemA, null, Rule(), "actor", default);
        // Act
        using var otherTenant = accessor.Push(new TenantContext(Guid.NewGuid()));
        var rules = await db.AlertRules.AsNoTracking().ToListAsync();
        var scope = await service.ScopeAsync(SystemA, null, default);
        // Assert
        rules.Should().BeEmpty();
        scope.Should().BeEmpty();
    }

    [Fact]
    public async Task Stale_rule_edit_is_rejected_and_prior_snapshot_retained()
    {
        // Arrange
        var rule = await service.SaveRuleAsync(SystemA, null, Rule(), "actor", default);
        await service.SaveRuleAsync(SystemA, rule.Id, Rule(version: 1), "actor", default);
        // Act
        var action = () => service.SaveRuleAsync(SystemA, rule.Id, Rule(version: 1), "actor", default);
        // Assert
        await action.Should().ThrowAsync<DbUpdateConcurrencyException>();
        db.Set<MonitoringRuleEvaluation>().Count(x => x.Outcome == "Configured").Should().Be(2);
    }

    [Fact]
    public async Task Disposition_stages_existing_narrative_queue_without_an_AO_decision()
    {
        // Arrange
        await service.SaveRuleAsync(SystemA, null, Rule(), "actor", default);
        await service.EvaluateDueAsync(default);
        var impact = await db.Set<MonitoringImpactReview>().SingleAsync();
        narratives.Setup(x => x.QueueAsync(It.IsAny<NarrativeChangeImpactRequest>(), default))
            .ReturnsAsync(new NarrativeChangeImpactResult(new[] { Guid.NewGuid() }));
        // Act
        await service.DispositionAsync(SystemA, impact.Id, new(1, "StageNarrativeReview", "Review exposed network"), "reviewer", default);
        await service.DispositionAsync(SystemA, impact.Id, new(1, "StageNarrativeReview", "Review exposed network"), "reviewer", default);
        // Assert
        narratives.Verify(x => x.QueueAsync(It.Is<NarrativeChangeImpactRequest>(r =>
            r.SystemId == SystemA && r.TenantId == tenant && r.SourceKind == "Monitoring" &&
            r.ControlIds.Contains("SC-7") && r.NarrativeTypes.Contains("Technical")), default), Times.Once);
        impact.Disposition.Should().Be("StageNarrativeReview");
        db.AuthorizationDecisions.Should().BeEmpty();
    }

    [Fact]
    public async Task Shared_provider_dependency_creates_separate_mission_dispositions()
    {
        // Arrange
        var componentId = Guid.NewGuid();
        var capabilityId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        db.CspInheritedComponents.Add(new() { Id = componentId, CspProfileId = Guid.NewGuid(), Name = "Shared provider" });
        db.CspInheritedCapabilities.Add(new() { Id = capabilityId, CspInheritedComponentId = componentId, Name = "Shared logging" });
        db.Set<ProviderCapabilityRelease>().Add(new() { Id = releaseId, CapabilityId = capabilityId, SnapshotJson = "{}" });
        foreach (var system in new[] { SystemA, SystemB })
        {
            db.BoundaryComponentAssignments.Add(new() { TenantId = tenant, CspInheritedComponentId = componentId,
                AuthorizationBoundaryDefinitionId = system + "-boundary", CreatedBy = "test" });
            db.Set<ProviderReleaseImpact>().Add(new() { TenantId = tenant, RegisteredSystemId = system,
                ReleaseId = releaseId, ControlId = "SC-7", DeliveredAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        foreach (var system in new[] { SystemA, SystemB })
            await service.SaveRuleAsync(system, null, Rule(system) with { Signal = "ProviderRelease" }, "actor", default);
        // Act
        await service.EvaluateDueAsync(default);
        var impactA = await db.Set<MonitoringImpactReview>().SingleAsync(x => x.RegisteredSystemId == SystemA);
        await service.DispositionAsync(SystemA, impactA.Id, new(1, "NoImpact", "Mission-specific review"), "reviewer", default);
        // Assert
        (await db.Set<MonitoringImpactReview>().SingleAsync(x => x.RegisteredSystemId == SystemB)).Disposition.Should().Be("Pending");
        db.Set<ProviderReleaseImpact>().Should().OnlyContain(x => x.CustomerReviewState == "Pending");
        db.Set<MonitoringImpactReview>().Count().Should().Be(2);
    }

    [Theory]
    [InlineData(false, "Matched", 1)]
    [InlineData(true, "CollectionUnavailable", 0)]
    public async Task Mixed_boundary_alert_health_checks_local_collection_only(
        bool missingLocalResource, string expectedOutcome, int expectedImpacts)
    {
        // Arrange
        var providerComponent = Guid.NewGuid();
        db.CspInheritedComponents.Add(new() { Id = providerComponent, CspProfileId = Guid.NewGuid(), Name = "Shared provider" });
        db.BoundaryComponentAssignments.Add(new() { TenantId = tenant, CspInheritedComponentId = providerComponent,
            AuthorizationBoundaryDefinitionId = SystemA + "-boundary", CreatedBy = "test" });
        if (missingLocalResource)
        {
            db.SystemComponents.Add(new() { Id = "unmapped-local", TenantId = tenant, RegisteredSystemId = SystemA,
                Name = "Local resource awaiting mapping", CreatedBy = "test" });
            db.BoundaryComponentAssignments.Add(new() { TenantId = tenant, SystemComponentId = "unmapped-local",
                AuthorizationBoundaryDefinitionId = SystemA + "-boundary", CreatedBy = "test" });
        }
        await db.SaveChangesAsync();
        var rule = await service.SaveRuleAsync(SystemA, null, Rule(), "actor", default);

        // Act
        await service.EvaluateDueAsync(default);

        // Assert
        var coverage = await service.CoverageAsync(SystemA, default);
        coverage.Should().Contain(x => x.ProviderComponentId == providerComponent && x.Health == "ProviderDependency");
        coverage.Should().Contain(x => x.ResourceId == ResourceA && x.Health == "Healthy");
        if (missingLocalResource)
            coverage.Should().Contain(x => x.ProviderComponentId == null && x.ResourceId == null && x.Health == "Missing");
        db.Set<MonitoringRuleEvaluation>().Where(x => x.RuleId == rule.Id && x.Outcome != "Configured")
            .Should().ContainSingle().Which.Outcome.Should().Be(expectedOutcome);
        db.Set<MonitoringImpactReview>().Count(x => x.RegisteredSystemId == SystemA).Should().Be(expectedImpacts);
    }

    public void Dispose() { db.Dispose(); pushed.Dispose(); }
}

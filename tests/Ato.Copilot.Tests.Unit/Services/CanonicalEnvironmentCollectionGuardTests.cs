using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class CanonicalEnvironmentCollectionGuardTests : IAsyncLifetime
{
    private readonly ServiceProvider provider;
    private readonly Mock<ISystemEnvironmentScopeResolver> resolver = new();
    private readonly Mock<IAtoComplianceEngine> engine = new();
    private readonly Mock<IComplianceEventSource> events = new();
    private readonly Guid subscription = Guid.NewGuid();
    private const string SystemId = "canonical-monitoring-system";
    private IDbContextFactory<AtoCopilotContext> Factory => provider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
    private CanonicalEnvironmentCollectionGuard Guard => provider.GetRequiredService<CanonicalEnvironmentCollectionGuard>();

    public CanonicalEnvironmentCollectionGuardTests()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AtoCopilotContext>(x => x.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton(resolver.Object);
        services.AddSingleton<CanonicalEnvironmentCollectionGuard>();
        provider = services.BuildServiceProvider();
        engine.Setup(x => x.RunAssessmentAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceAssessment { Status = AssessmentStatus.Completed });
        events.Setup(x => x.GetRecentEventsAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        SetAuthority(false);
    }

    private void SetAuthority(bool eligible)
    {
        var source = new ResolvedSystemEnvironmentScope(Guid.NewGuid(), 2, Guid.NewGuid(), 1,
            new(Guid.NewGuid(), Guid.NewGuid(), subscription, Guid.NewGuid(), "Government", "Synthetic",
                "Selected", DateTimeOffset.UtcNow), "ProviderAllocation", Guid.NewGuid(), 2, eligible,
            eligible ? null : "Allocation withdrawn",
            [$"/subscriptions/{subscription}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/a"],
            [], [], new("Test", null, null, "Reconciled", null, DateTimeOffset.UtcNow));
        resolver.Setup(x => x.ResolveAsync(SystemId, It.IsAny<EnvironmentScopePurpose>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSystemEnvironmentScopes(SystemId, 2, [source], []));
    }

    public async Task InitializeAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        db.RegisteredSystems.Add(new() { Id = SystemId, Name = "Synthetic" });
        db.MonitoringConfigurations.Add(Configuration());
        db.ComplianceBaselines.Add(new() { SubscriptionId = subscription.ToString(), IsActive = true,
            ResourceId = $"/subscriptions/{subscription}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/a" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await provider.DisposeAsync();

    [Fact]
    public async Task ScopedResolver_PreservesTheCurrentTenantAndWorkspaceActor()
    {
        // Arrange
        var accessor = new TenantContextAccessor();
        var current = new TenantContext(Guid.NewGuid()) { PersonId = Guid.NewGuid(), IsWorkspaceRequest = true };
        using var pushed = accessor.Push(current);
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor>(accessor);
        services.AddScoped<ITenantContext, TenantContext>();
        ITenantContext? observed = null;
        services.AddScoped<ISystemEnvironmentScopeResolver>(sp =>
        {
            observed = sp.GetRequiredService<ITenantContext>();
            var mock = new Mock<ISystemEnvironmentScopeResolver>();
            mock.Setup(x => x.ResolveAsync(SystemId, EnvironmentScopePurpose.Assessment, default))
                .ReturnsAsync(new ResolvedSystemEnvironmentScopes(SystemId, 0, [], []));
            return mock.Object;
        });
        await using var scopedProvider = services.BuildServiceProvider();
        var guard = new CanonicalEnvironmentCollectionGuard(Factory,
            scopedProvider.GetRequiredService<IServiceScopeFactory>());

        // Act
        await guard.EnsureSystemAsync(SystemId, EnvironmentScopePurpose.Assessment, default);

        // Assert
        observed.Should().NotBeNull();
        observed!.EffectiveTenantId.Should().Be(current.EffectiveTenantId);
        observed.PersonId.Should().Be(current.PersonId);
        observed.IsWorkspaceRequest.Should().BeTrue();
    }

    [Fact]
    public async Task LegacyMonitoring_RequiresReconciliationWithoutChangingLegacyAssessmentAdmission()
    {
        // Arrange
        await using var db = await Factory.CreateDbContextAsync();
        (await db.RegisteredSystems.SingleAsync()).AzureProfile = new()
            { SubscriptionIds = [subscription.ToString()] };
        await db.SaveChangesAsync();
        resolver.Setup(x => x.ResolveAsync(SystemId, It.IsAny<EnvironmentScopePurpose>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSystemEnvironmentScopes(SystemId, 0, [],
                [new(subscription.ToString(), "AzureProfile", "Synthetic", "ReconciliationRequired", "Review exact resource scope.")]));

        // Act
        var assessment = () => Guard.EnsureSubscriptionAsync(subscription.ToString(), EnvironmentScopePurpose.Assessment, default);
        var monitoring = () => Guard.EnsureSubscriptionAsync(subscription.ToString(), EnvironmentScopePurpose.Monitoring, default, true);

        // Assert
        await assessment.Should().NotThrowAsync();
        await monitoring.Should().ThrowAsync<AssessmentEnvironmentException>();
    }

    private MonitoringConfiguration Configuration() => new()
    {
        Id = Guid.NewGuid(), SubscriptionId = subscription.ToString(), IsEnabled = true,
        Mode = MonitoringMode.Both, NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1)
    };

    [Theory]
    [InlineData("enable")]
    [InlineData("baseline")]
    [InlineData("drift")]
    [InlineData("check")]
    [InlineData("remediation")]
    public async Task Watch_WithdrawnSourceStopsBeforeAssessment(string operation)
    {
        // Arrange
        var watch = ActivatorUtilities.CreateInstance<ComplianceWatchService>(provider, Factory,
            Mock.Of<IAlertManager>(), engine.Object, Mock.Of<IRemediationEngine>(),
            Options.Create(new MonitoringOptions()), Options.Create(new AlertOptions()),
            NullLogger<ComplianceWatchService>.Instance);
        Func<Task> action = operation switch
        {
            "enable" => async () => await watch.EnableMonitoringAsync(subscription.ToString(), "another-rg"),
            "baseline" => async () => await watch.CaptureBaselineAsync(subscription.ToString()),
            "drift" => async () => await watch.DetectDriftAsync(subscription.ToString()),
            "remediation" => async () => await watch.TryAutoRemediateAsync(new()
                { SubscriptionId = subscription.ToString(), ControlFamily = "CM" }),
            _ => async () => await watch.RunMonitoringCheckAsync(Configuration())
        };

        // Act
        var result = await action.Should().ThrowAsync<AssessmentEnvironmentException>();

        // Assert
        result.Which.ErrorCode.Should().Be("ASSESSMENT_ENVIRONMENT_INELIGIBLE");
        engine.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task EventWorker_WithdrawnSourceDoesNotPollAndRetainsFailure()
    {
        // Arrange
        var worker = ActivatorUtilities.CreateInstance<ComplianceWatchHostedService>(provider, Factory,
            Mock.Of<IComplianceWatchService>(), Mock.Of<IAlertManager>(), events.Object,
            Options.Create(new MonitoringOptions()), NullLogger<ComplianceWatchHostedService>.Instance);

        // Act
        await worker.RunEventDrivenChecksAsync(default);

        // Assert
        events.Invocations.Should().BeEmpty();
        await using var db = await Factory.CreateDbContextAsync();
        (await db.MonitoringConfigurations.SingleAsync()).CollectionError.Should().Be("ASSESSMENT_ENVIRONMENT_INELIGIBLE");
    }

    [Fact]
    public async Task SnapshotWorker_UnsupportedScopeCannotGenerateHealthySnapshot()
    {
        // Arrange
        SetAuthority(true);
        var worker = ActivatorUtilities.CreateInstance<ComplianceWatchHostedService>(provider, Factory,
            Mock.Of<IComplianceWatchService>(), Mock.Of<IAlertManager>(), events.Object,
            Options.Create(new MonitoringOptions()), NullLogger<ComplianceWatchHostedService>.Instance);

        // Act
        await worker.CaptureSnapshotsIfDueAsync(default);

        // Assert
        await using var db = await Factory.CreateDbContextAsync();
        (await db.ComplianceSnapshots.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AzureRead_WithdrawalBlocksWarmCacheAndEveryServiceBeforeArm()
    {
        // Arrange
        var arm = new Mock<ArmClient>(MockBehavior.Strict);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"resources:{subscription}:all:all", (IReadOnlyList<GenericResource>)Array.Empty<GenericResource>());
        var resources = new AzureResourceService(arm.Object, cache, NullLogger<AzureResourceService>.Instance, Guard);
        var policy = new AzurePolicyComplianceService(arm.Object, NullLogger<AzurePolicyComplianceService>.Instance, Guard);
        var defender = new DefenderForCloudService(arm.Object, NullLogger<DefenderForCloudService>.Instance, Guard);
        var probe = ActivatorUtilities.CreateInstance<AzureAssessmentConnectionProbe>(
            provider, arm.Object, NullLogger<AzureAssessmentConnectionProbe>.Instance);
        var resource = $"/subscriptions/{subscription}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/a";
        Func<Task>[] operations =
        [
            async () => await resources.GetResourcesAsync(subscription.ToString()),
            async () => await resources.GetRoleAssignmentsAsync(subscription.ToString()),
            () => resources.PreWarmCacheAsync(subscription.ToString()),
            async () => await resources.GetDiagnosticSettingsAsync(resource),
            async () => await resources.GetResourceLocksAsync(subscription.ToString()),
            async () => await policy.GetPolicyStatesAsync(subscription.ToString()),
            async () => await policy.GetComplianceSummaryAsync(subscription.ToString()),
            async () => await defender.GetAssessmentsAsync(subscription.ToString()),
            async () => await defender.GetRecommendationsAsync(subscription.ToString()),
            async () => await defender.GetSecureScoreAsync(subscription.ToString()),
            () => probe.CheckAsync([new(subscription, Guid.NewGuid())])
        ];

        // Act
        foreach (var operation in operations)
            await operation.Should().ThrowAsync<AssessmentEnvironmentException>();

        // Assert
        arm.Invocations.Should().BeEmpty();
    }
}

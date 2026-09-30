using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// T243 — Unit tests for ConMon report enrichment with ComplianceWatchService data (Phase 17 §9a.3).
/// Tests: Watch data included, Watch unavailable gracefully skipped, multi-subscription aggregation.
/// </summary>
public class ConMonReportEnrichmentTests : IDisposable
{
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;
    private readonly IServiceScopeFactory _scopeFactory;

    public ConMonReportEnrichmentTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(databaseName: $"ConMonEnrich_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton(_dbOptions);
        services.AddScoped(sp => new AtoCopilotContext(sp.GetRequiredService<DbContextOptions<AtoCopilotContext>>()));
        var provider = services.BuildServiceProvider();
        _scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
    }

    public void Dispose()
    {
        using var db = new AtoCopilotContext(_dbOptions);
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task CanonicalReport_DoesNotReuseLegacyNewestCheckOrHealthyConfiguration()
    {
        // Arrange
        var systemId = await SeedFullSystem(["sub-001"], monitoringEnabled: true);
        var resolver = new Mock<ISystemEnvironmentScopeResolver>();
        resolver.Setup(x => x.ResolveAsync(systemId, EnvironmentScopePurpose.Monitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSystemEnvironmentScopes(systemId, 2, [], []));
        var services = new ServiceCollection();
        services.AddSingleton(resolver.Object);
        services.AddSingleton(Mock.Of<IDbContextFactory<AtoCopilotContext>>());
        services.AddSingleton<CanonicalEnvironmentCollectionGuard>();
        await using var provider = services.BuildServiceProvider();
        var service = ActivatorUtilities.CreateInstance<ConMonService>(provider, _scopeFactory,
            Mock.Of<ILogger<ConMonService>>(), Mock.Of<IComplianceWatchService>());

        // Act
        var report = await service.GenerateReportAsync(systemId, "Monthly", "2026-03", "test-user");

        // Assert
        report.MonitoringEnabled.Should().BeFalse();
        report.LastMonitoringCheck.Should().BeNull();
        report.ReportContent.Should().Contain("Collection unavailable");
    }

    [Fact]
    public async Task GenerateReportAsync_WithWatchData_IncludesEnrichmentFields()
    {
        // Arrange
        var systemId = await SeedFullSystem(
            subscriptionIds: new List<string> { "sub-001", "sub-002" },
            monitoringEnabled: true,
            driftAlertCount: 3,
            autoRemRuleCount: 2);

        var service = CreateConMonService(withWatch: true);

        // Act
        var report = await service.GenerateReportAsync(systemId, "Monthly", "2026-03", "test-user");

        // Assert
        report.MonitoringEnabled.Should().BeTrue();
        report.DriftAlertCount.Should().Be(3);
        report.AutoRemediationRuleCount.Should().Be(2);
        report.LastMonitoringCheck.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateReportAsync_WithoutWatchService_SkipsEnrichmentGracefully()
    {
        // Arrange
        var systemId = await SeedFullSystem(subscriptionIds: new List<string> { "sub-001" });
        var service = CreateConMonService(withWatch: false); // No watch service

        // Act
        var report = await service.GenerateReportAsync(systemId, "Monthly", "2026-03", "test-user");

        // Assert — enrichment fields should remain null
        report.MonitoringEnabled.Should().BeNull();
        report.DriftAlertCount.Should().BeNull();
        report.AutoRemediationRuleCount.Should().BeNull();
        report.LastMonitoringCheck.Should().BeNull();
        // Report should still be valid
        report.ComplianceScore.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GenerateReportAsync_NoSubscriptions_MonitoringFalse()
    {
        // Arrange — system with no Azure subscriptions
        var systemId = await SeedFullSystem(subscriptionIds: new List<string>());
        var service = CreateConMonService(withWatch: true);

        // Act
        var report = await service.GenerateReportAsync(systemId, "Monthly", "2026-03", "test-user");

        // Assert
        report.MonitoringEnabled.Should().BeFalse();
        report.DriftAlertCount.Should().BeNull();
    }

    [Fact]
    public async Task GenerateReportAsync_MultiSubscription_AggregatesDriftAlerts()
    {
        // Arrange — system with 2 subscriptions, alerts on both
        var systemId = await SeedFullSystem(
            subscriptionIds: new List<string> { "sub-A", "sub-B" },
            monitoringEnabled: true,
            driftAlertCount: 5, // 3 on sub-A, 2 on sub-B  
            autoRemRuleCount: 1);

        var service = CreateConMonService(withWatch: true);

        // Act
        var report = await service.GenerateReportAsync(systemId, "Monthly", "2026-03", "test-user");

        // Assert — drift alerts aggregated across subscriptions
        report.DriftAlertCount.Should().Be(5);
    }

    private ConMonService CreateConMonService(bool withWatch)
    {
        var mockWatch = withWatch ? Mock.Of<IComplianceWatchService>() : null;
        return new ConMonService(
            _scopeFactory,
            Mock.Of<ILogger<ConMonService>>(),
            watchService: mockWatch);
    }

    private async Task<string> SeedFullSystem(
        List<string> subscriptionIds,
        bool monitoringEnabled = false,
        int driftAlertCount = 0,
        int autoRemRuleCount = 0)
    {
        await using var db = new AtoCopilotContext(_dbOptions);

        var systemId = Guid.NewGuid().ToString();

        // Create system
        var system = new RegisteredSystem
        {
            Id = systemId,
            Name = "Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionCritical,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            AzureProfile = subscriptionIds.Count > 0 ? new AzureEnvironmentProfile
            {
                CloudEnvironment = AzureCloudEnvironment.Government,
                ArmEndpoint = "https://management.usgovcloudapi.net",
                AuthenticationEndpoint = "https://login.microsoftonline.us",
                SubscriptionIds = subscriptionIds
            } : null
        };
        db.RegisteredSystems.Add(system);

        // Create ConMon plan (required for report generation)
        db.ConMonPlans.Add(new ConMonPlan
        {
            RegisteredSystemId = systemId,
            AssessmentFrequency = "Monthly",
            AnnualReviewDate = DateTime.UtcNow.AddMonths(6),
            CreatedBy = "test"
        });

        // Create monitoring configs
        if (monitoringEnabled && subscriptionIds.Count > 0)
        {
            foreach (var sub in subscriptionIds)
            {
                db.MonitoringConfigurations.Add(new MonitoringConfiguration
                {
                    SubscriptionId = sub,
                    IsEnabled = true,
                    Mode = MonitoringMode.Scheduled,
                    Frequency = MonitoringFrequency.Hourly,
                    CreatedBy = "test",
                    LastRunAt = DateTimeOffset.UtcNow.AddMinutes(-10)
                });
            }
        }

        // Create drift alerts
        var alertsPerSub = subscriptionIds.Count > 0 ? driftAlertCount / Math.Max(subscriptionIds.Count, 1) : 0;
        var remainder = driftAlertCount - (alertsPerSub * subscriptionIds.Count);
        for (var s = 0; s < subscriptionIds.Count; s++)
        {
            var count = alertsPerSub + (s == 0 ? remainder : 0);
            for (var i = 0; i < count; i++)
            {
                db.ComplianceAlerts.Add(new ComplianceAlert
                {
                    AlertId = $"ALT-{DateTime.UtcNow:yyyyMMdd}{s:D2}{i:D3}",
                    Type = AlertType.Drift,
                    Severity = AlertSeverity.Medium,
                    Status = AlertStatus.New,
                    Title = $"Test drift alert {i}",
                    Description = "Drift test",
                    SubscriptionId = subscriptionIds[s]
                });
            }
        }

        // Create auto-remediation rules
        for (var i = 0; i < autoRemRuleCount && subscriptionIds.Count > 0; i++)
        {
            db.AutoRemediationRules.Add(new AutoRemediationRule
            {
                Name = $"Rule {i}",
                SubscriptionId = subscriptionIds[0],
                IsEnabled = true,
                Action = "TestAction",
                CreatedBy = "test"
            });
        }

        await db.SaveChangesAsync();
        return systemId;
    }
}

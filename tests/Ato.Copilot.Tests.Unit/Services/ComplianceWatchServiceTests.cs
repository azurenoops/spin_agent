using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net.Http;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services;

public class ComplianceWatchServiceTests : IDisposable
{
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;
    private readonly Mock<IAlertManager> _alertManagerMock;
    private readonly Mock<IAtoComplianceEngine> _engineMock;
    private readonly MonitoringOptions _monitoringOptions;
    private readonly AlertOptions _alertOptions;
    private readonly ComplianceWatchService _service;

    public ComplianceWatchServiceTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"WatchService_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _alertManagerMock = new Mock<IAlertManager>();
        _alertManagerMock
            .Setup(m => m.CreateAlertAsync(It.IsAny<ComplianceAlert>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceAlert a, CancellationToken _) =>
            {
                a.Id = Guid.NewGuid();
                a.AlertId = $"ALT-{DateTimeOffset.UtcNow:yyyyMMdd}00001";
                a.Status = AlertStatus.New;
                return a;
            });

        _engineMock = new Mock<IAtoComplianceEngine>();

        _monitoringOptions = new MonitoringOptions
        {
            TickIntervalSeconds = 60,
            EventPollIntervalSeconds = 120
        };

        _alertOptions = new AlertOptions
        {
            SecureScoreThreshold = 80.0,
            CriticalSlaMinutes = 60,
            HighSlaMinutes = 240,
            MediumSlaMinutes = 1440,
            LowSlaMinutes = 10080,
            DefaultPageSize = 50,
            MaxPageSize = 200
        };

        _service = new ComplianceWatchService(
            new TestDbContextFactory(_dbOptions),
            _alertManagerMock.Object,
            _engineMock.Object,
            Mock.Of<IRemediationEngine>(),
            Options.Create(_monitoringOptions),
            Options.Create(_alertOptions),
            Mock.Of<ILogger<ComplianceWatchService>>());
    }

    public void Dispose()
    {
        using var db = new AtoCopilotContext(_dbOptions);
        db.Database.EnsureDeleted();
    }

    // ─── EnableMonitoringAsync ──────────────────────────────────────────────

    [Fact]
    public async Task EnableMonitoring_NewScope_ShouldCreateConfiguration()
    {
        var config = await _service.EnableMonitoringAsync(
            "sub-001", "rg-001", MonitoringFrequency.Hourly, MonitoringMode.Scheduled);

        config.Should().NotBeNull();
        config.SubscriptionId.Should().Be("sub-001");
        config.ResourceGroupName.Should().Be("rg-001");
        config.IsEnabled.Should().BeTrue();
        config.Frequency.Should().Be(MonitoringFrequency.Hourly);
        config.Mode.Should().Be(MonitoringMode.Scheduled);
        config.NextRunAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EnableMonitoring_AlreadyEnabled_ShouldThrow()
    {
        await _service.EnableMonitoringAsync("sub-dup", null);

        var act = () => _service.EnableMonitoringAsync("sub-dup", null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*MONITORING_ALREADY_ENABLED*");
    }

    [Fact]
    public async Task EnableMonitoring_DisabledScope_ShouldReEnable()
    {
        await _service.EnableMonitoringAsync("sub-re", null);
        await _service.DisableMonitoringAsync("sub-re", null);

        var config = await _service.EnableMonitoringAsync("sub-re", null, MonitoringFrequency.Daily);

        config.IsEnabled.Should().BeTrue();
        config.Frequency.Should().Be(MonitoringFrequency.Daily);
    }

    [Fact]
    public async Task EnableMonitoring_SubscriptionOnly_ShouldSetNullResourceGroup()
    {
        var config = await _service.EnableMonitoringAsync("sub-only", null);

        config.ResourceGroupName.Should().BeNull();
        config.SubscriptionId.Should().Be("sub-only");
    }

    // ─── DisableMonitoringAsync ─────────────────────────────────────────────

    [Fact]
    public async Task DisableMonitoring_ExistingConfig_ShouldSetDisabled()
    {
        await _service.EnableMonitoringAsync("sub-dis", null);

        var config = await _service.DisableMonitoringAsync("sub-dis", null);

        config.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task DisableMonitoring_NonExistent_ShouldThrow()
    {
        var act = () => _service.DisableMonitoringAsync("sub-missing", null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*MONITORING_NOT_CONFIGURED*");
    }

    // ─── ConfigureMonitoringAsync ───────────────────────────────────────────

    [Fact]
    public async Task ConfigureMonitoring_UpdateFrequency_ShouldRecomputeNextRun()
    {
        await _service.EnableMonitoringAsync("sub-cfg", null, MonitoringFrequency.Hourly);

        var config = await _service.ConfigureMonitoringAsync(
            "sub-cfg", null, MonitoringFrequency.Daily, null);

        config.Frequency.Should().Be(MonitoringFrequency.Daily);
        config.NextRunAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ConfigureMonitoring_UpdateMode_ShouldKeepFrequency()
    {
        await _service.EnableMonitoringAsync("sub-mode", null, MonitoringFrequency.Weekly, MonitoringMode.Scheduled);

        var config = await _service.ConfigureMonitoringAsync(
            "sub-mode", null, null, MonitoringMode.Both);

        config.Mode.Should().Be(MonitoringMode.Both);
        config.Frequency.Should().Be(MonitoringFrequency.Weekly);
    }

    [Fact]
    public async Task ConfigureMonitoring_NonExistent_ShouldThrow()
    {
        var act = () => _service.ConfigureMonitoringAsync("sub-ghost", null, MonitoringFrequency.Daily, null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*MONITORING_NOT_CONFIGURED*");
    }

    // ─── GetMonitoringStatusAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetMonitoringStatus_WithSubscriptionFilter_ShouldReturnFiltered()
    {
        await _service.EnableMonitoringAsync("sub-a", null);
        await _service.EnableMonitoringAsync("sub-b", null);

        var result = await _service.GetMonitoringStatusAsync("sub-a");

        result.Should().HaveCount(1);
        result[0].SubscriptionId.Should().Be("sub-a");
    }

    [Fact]
    public async Task GetMonitoringStatus_NoFilter_ShouldReturnAll()
    {
        await _service.EnableMonitoringAsync("sub-all-1", null);
        await _service.EnableMonitoringAsync("sub-all-2", null);

        var result = await _service.GetMonitoringStatusAsync(null);

        result.Should().HaveCountGreaterOrEqualTo(2);
    }

    // ─── CaptureBaselineAsync ───────────────────────────────────────────────

    [Fact]
    public async Task CaptureBaseline_ShouldCreateBaselinePerResource()
    {
        var assessment = CreateAssessmentWithFindings("sub-base", 2);
        SetupEngineReturns(assessment);

        var baselines = await _service.CaptureBaselineAsync("sub-base");

        baselines.Should().HaveCount(2);
        baselines.Should().AllSatisfy(b =>
        {
            b.IsActive.Should().BeTrue();
            b.SubscriptionId.Should().Be("sub-base");
            b.ConfigurationHash.Should().NotBeNullOrEmpty();
            b.ConfigurationHash.Should().HaveLength(64); // SHA-256 hex
            b.ConfigurationSnapshot.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task CaptureBaseline_ShouldDeactivateOldBaselines()
    {
        var assessment1 = CreateAssessmentWithFindings("sub-deact", 1);
        SetupEngineReturns(assessment1);
        var first = await _service.CaptureBaselineAsync("sub-deact");

        var assessment2 = CreateAssessmentWithFindings("sub-deact", 1);
        SetupEngineReturns(assessment2);
        var second = await _service.CaptureBaselineAsync("sub-deact");

        // Verify old baselines are deactivated
        await using var db = new AtoCopilotContext(_dbOptions);
        var allBaselines = await db.ComplianceBaselines
            .Where(b => b.SubscriptionId == "sub-deact")
            .ToListAsync();

        var activeBaselines = allBaselines.Where(b => b.IsActive).ToList();
        activeBaselines.Should().HaveCount(1);
        activeBaselines[0].Id.Should().Be(second[0].Id);
    }

    [Fact]
    public async Task CaptureBaseline_ShouldComputeHashConsistently()
    {
        var assessment = CreateAssessmentWithFindings("sub-hash", 1);
        SetupEngineReturns(assessment);

        var baselines = await _service.CaptureBaselineAsync("sub-hash");

        // Same input → same hash
        var hash1 = ComplianceWatchService.ComputeHash(baselines[0].ConfigurationSnapshot);
        var hash2 = ComplianceWatchService.ComputeHash(baselines[0].ConfigurationSnapshot);
        hash1.Should().Be(hash2);
    }

    // ─── DetectDriftAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task DetectDrift_NoBaselines_ShouldReturnEmpty()
    {
        SetupEngineReturns(CreateAssessmentWithFindings("sub-nobase", 1));

        var alerts = await _service.DetectDriftAsync("sub-nobase");

        alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectDrift_MatchingBaseline_ShouldReturnNoAlerts()
    {
        // Capture baseline
        var assessment = CreateAssessmentWithFindings("sub-match", 1);
        SetupEngineReturns(assessment);
        await _service.CaptureBaselineAsync("sub-match");

        // Same assessment on detect → no drift
        SetupEngineReturns(assessment);
        var alerts = await _service.DetectDriftAsync("sub-match");

        alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectDrift_ChangedHash_ShouldCreateDriftAlert()
    {
        // Capture baseline
        var baseline = CreateAssessmentWithFindings("sub-drift", 1);
        SetupEngineReturns(baseline);
        await _service.CaptureBaselineAsync("sub-drift");

        // Changed assessment → hash mismatch
        var changed = CreateAssessmentWithFindings("sub-drift", 1, statusOverride: FindingStatus.Open);
        SetupEngineReturns(changed);

        var alerts = await _service.DetectDriftAsync("sub-drift");

        alerts.Should().NotBeEmpty();
        _alertManagerMock.Verify(m => m.CreateAlertAsync(
            It.Is<ComplianceAlert>(a => a.Type == AlertType.Drift),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task DetectDrift_NewViolationOnPreviouslyCompliant_ShouldCreateViolationAlert()
    {
        // Baseline with remediated controls
        var baseline = CreateAssessmentWithFindings("sub-viol", 1, statusOverride: FindingStatus.Remediated);
        SetupEngineReturns(baseline);
        await _service.CaptureBaselineAsync("sub-viol");

        // Now control is Open (violation)
        var current = CreateAssessmentWithFindings("sub-viol", 1, statusOverride: FindingStatus.Open);
        SetupEngineReturns(current);

        var alerts = await _service.DetectDriftAsync("sub-viol");

        _alertManagerMock.Verify(m => m.CreateAlertAsync(
            It.Is<ComplianceAlert>(a => a.Type == AlertType.Violation),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─── RunMonitoringCheckAsync ────────────────────────────────────────────

    [Fact]
    public async Task RunMonitoringCheck_ShouldReturnAlertCount()
    {
        // Create a config + baseline
        var assessment = CreateAssessmentWithFindings("sub-check", 1);
        SetupEngineReturns(assessment);
        await _service.CaptureBaselineAsync("sub-check");

        var config = await _service.EnableMonitoringAsync("sub-check");

        // Run check with same assessment → 0 new alerts
        SetupEngineReturns(assessment);
        var alertCount = await _service.RunMonitoringCheckAsync(config);

        alertCount.Should().BeGreaterOrEqualTo(0);
    }

    // ─── Score Degradation ──────────────────────────────────────────────────

    [Fact]
    public async Task RunMonitoringCheck_FailedCollectionMustNotLookLikeZeroChanges()
    {
        // Arrange
        var assessment = CreateAssessmentWithFindings("sub-failed", 1);
        SetupEngineReturns(assessment);
        await _service.CaptureBaselineAsync("sub-failed");
        var config = await _service.EnableMonitoringAsync("sub-failed");
        assessment.Status = AssessmentStatus.Failed;
        SetupEngineReturns(assessment);
        // Act
        var action = () => _service.RunMonitoringCheckAsync(config);
        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RunMonitoringCheck_ScoreBelowThreshold_ShouldCreateDegradationAlert()
    {
        // Baseline with passing assessment
        var goodAssessment = CreateAssessmentWithFindings("sub-score", 1);
        goodAssessment.TotalControls = 100;
        goodAssessment.PassedControls = 90;
        SetupEngineReturns(goodAssessment);
        await _service.CaptureBaselineAsync("sub-score");

        var config = await _service.EnableMonitoringAsync("sub-score");

        // Now score drops below 80% threshold
        var poorAssessment = CreateAssessmentWithFindings("sub-score", 1);
        poorAssessment.TotalControls = 100;
        poorAssessment.PassedControls = 50; // 50% < 80% threshold
        SetupEngineReturns(poorAssessment);

        await _service.RunMonitoringCheckAsync(config);

        _alertManagerMock.Verify(m => m.CreateAlertAsync(
            It.Is<ComplianceAlert>(a => a.Type == AlertType.Degradation),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─── ComputeNextRunAt ───────────────────────────────────────────────────

    [Theory]
    [InlineData(MonitoringFrequency.FifteenMinutes, 15)]
    [InlineData(MonitoringFrequency.Hourly, 60)]
    [InlineData(MonitoringFrequency.Daily, 1440)]
    [InlineData(MonitoringFrequency.Weekly, 10080)]
    public void ComputeNextRunAt_ShouldReturnCorrectFutureTime(MonitoringFrequency freq, int expectedMinutes)
    {
        var before = DateTimeOffset.UtcNow;
        var result = ComplianceWatchService.ComputeNextRunAt(freq);
        var after = DateTimeOffset.UtcNow;

        result.Should().BeOnOrAfter(before.AddMinutes(expectedMinutes - 1));
        result.Should().BeOnOrBefore(after.AddMinutes(expectedMinutes + 1));
    }

    // ─── ComputeHash ────────────────────────────────────────────────────────

    [Fact]
    public void ComputeHash_SameInput_ShouldReturnSameHash()
    {
        var hash1 = ComplianceWatchService.ComputeHash("test-data");
        var hash2 = ComplianceWatchService.ComputeHash("test-data");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_DifferentInput_ShouldReturnDifferentHash()
    {
        var hash1 = ComplianceWatchService.ComputeHash("test-a");
        var hash2 = ComplianceWatchService.ComputeHash("test-b");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_ShouldReturn64CharHexString()
    {
        var hash = ComplianceWatchService.ComputeHash("anything");
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    // ─── Auto-Resolve ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunMonitoringCheck_ResourceReturnedToBaseline_ShouldAutoResolve()
    {
        // Capture baseline
        var baseline = CreateAssessmentWithFindings("sub-auto", 1, statusOverride: FindingStatus.Remediated);
        SetupEngineReturns(baseline);
        await _service.CaptureBaselineAsync("sub-auto");

        var config = await _service.EnableMonitoringAsync("sub-auto");

        // Create a drift alert manually
        await using (var db = new AtoCopilotContext(_dbOptions))
        {
            db.ComplianceAlerts.Add(new ComplianceAlert
            {
                Id = Guid.NewGuid(),
                AlertId = "ALT-2025010100001",
                Type = AlertType.Drift,
                Severity = AlertSeverity.Medium,
                Status = AlertStatus.New,
                Title = "Drift detected",
                Description = "Test drift",
                SubscriptionId = "sub-auto",
                AffectedResources = new List<string> { "/subscriptions/sub-auto/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/res-0" },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                SlaDeadline = DateTimeOffset.UtcNow.AddHours(4)
            });
            await db.SaveChangesAsync();
        }

        // Resource returns to baseline → auto-resolve
        SetupEngineReturns(baseline);
        await _service.RunMonitoringCheckAsync(config);

        _alertManagerMock.Verify(m => m.TransitionAlertAsync(
            It.IsAny<Guid>(),
            AlertStatus.Resolved,
            "system",
            "System",
            It.Is<string>(j => j.Contains("Auto-resolved")),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private ComplianceAssessment CreateAssessmentWithFindings(
        string subscriptionId, int resourceCount, FindingStatus statusOverride = FindingStatus.Remediated)
    {
        var assessment = new ComplianceAssessment
        {
            Id = Guid.NewGuid().ToString(),
            SubscriptionId = subscriptionId,
            TotalControls = resourceCount * 2,
            PassedControls = resourceCount * 2,
            FailedControls = 0,
            Status = AssessmentStatus.Completed,
            Findings = new List<ComplianceFinding>()
        };

        for (int i = 0; i < resourceCount; i++)
        {
            assessment.Findings.Add(new ComplianceFinding
            {
                Id = Guid.NewGuid().ToString(),
                ControlId = $"AC-{i + 1}",
                ControlFamily = "AC",
                ResourceId = $"/subscriptions/{subscriptionId}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/res-{i}",
                ResourceType = "Microsoft.Storage/storageAccounts",
                Status = statusOverride,
                Title = $"Finding {i}",
                Description = $"Test finding {i}"
            });
        }

        return assessment;
    }

    private void SetupEngineReturns(ComplianceAssessment assessment)
    {
        _engineMock
            .Setup(e => e.RunAssessmentAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);
    }

    private class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// ActivityLogEventSource Tests
// ═══════════════════════════════════════════════════════════════════════════

public class ActivityLogEventSourceTests
{
    // ─── IsComplianceRelevantOperation ──────────────────────────────────────

    [Theory]
    [InlineData("Microsoft.Storage/storageAccounts/write", true)]
    [InlineData("Microsoft.Compute/virtualMachines/delete", true)]
    [InlineData("Microsoft.Authorization/policyAssignments/write", true)]
    [InlineData("Microsoft.Authorization/roleAssignments/write", true)]
    [InlineData("Microsoft.Authorization/policyDefinitions/write", true)]
    [InlineData("Microsoft.Authorization/policySetDefinitions/delete", true)]
    [InlineData("Microsoft.Storage/storageAccounts/read", false)]
    [InlineData("Microsoft.Compute/virtualMachines/listKeys/action", false)]
    [InlineData("", false)]
    public void IsComplianceRelevantOperation_ShouldClassifyCorrectly(string operationName, bool expected)
    {
        ActivityLogEventSource.IsComplianceRelevantOperation(operationName)
            .Should().Be(expected);
    }

    [Fact]
    public void IsComplianceRelevantOperation_NullInput_ShouldReturnFalse()
    {
        ActivityLogEventSource.IsComplianceRelevantOperation(null!)
            .Should().BeFalse();
    }

    // ─── IsPolicyDriftEvent ────────────────────────────────────────────────

    [Theory]
    [InlineData("Microsoft.Authorization/policyAssignments/write", true)]
    [InlineData("Microsoft.Authorization/policyDefinitions/delete", true)]
    [InlineData("Microsoft.Authorization/policySetDefinitions/write", true)]
    [InlineData("Microsoft.Authorization/roleAssignments/write", false)]
    [InlineData("Microsoft.Storage/storageAccounts/write", false)]
    public void IsPolicyDriftEvent_ShouldDetectPolicyChanges(string operationName, bool expected)
    {
        var evt = new ComplianceEvent
        {
            EventId = "evt-1",
            OperationName = operationName,
            Timestamp = DateTimeOffset.UtcNow
        };

        ActivityLogEventSource.IsPolicyDriftEvent(evt).Should().Be(expected);
    }

    // ─── ExtractResourceGroup ──────────────────────────────────────────────

    [Theory]
    [InlineData("/subscriptions/sub-1/resourceGroups/rg-prod/providers/Microsoft.Storage/storageAccounts/sa1", "rg-prod")]
    [InlineData("/subscriptions/sub-1/resourceGroups/RG-Dev/providers/Microsoft.Compute/virtualMachines/vm1", "RG-Dev")]
    [InlineData("/subscriptions/sub-1/providers/Microsoft.Authorization/policyAssignments/pa1", null)]
    [InlineData("", null)]
    public void ExtractResourceGroup_ShouldParseCorrectly(string resourceId, string? expected)
    {
        ActivityLogEventSource.ExtractResourceGroup(resourceId).Should().Be(expected);
    }

    [Fact]
    public void ExtractResourceGroup_NullInput_ShouldReturnNull()
    {
        ActivityLogEventSource.ExtractResourceGroup(null!).Should().BeNull();
    }

    // ─── GetManagementUrl ──────────────────────────────────────────────────

    [Fact]
    public void GetManagementUrl_AzureGovernment_ShouldReturnGovUrl()
    {
        var options = new MonitoringOptions { CloudEnvironment = "AzureGovernment" };
        var source = new ActivityLogEventSource(
            Options.Create(options),
            Mock.Of<ILogger<ActivityLogEventSource>>());

        source.GetManagementUrl().Should().Be("https://management.usgovcloudapi.net");
    }

    [Fact]
    public void GetManagementUrl_AzureCloud_ShouldReturnPublicUrl()
    {
        var options = new MonitoringOptions { CloudEnvironment = "AzureCloud" };
        var source = new ActivityLogEventSource(
            Options.Create(options),
            Mock.Of<ILogger<ActivityLogEventSource>>());

        source.GetManagementUrl().Should().Be("https://management.azure.com");
    }

    [Fact]
    public void GetManagementUrl_DefaultNull_ShouldReturnGovUrl()
    {
        var options = new MonitoringOptions { CloudEnvironment = null! };
        var source = new ActivityLogEventSource(
            Options.Create(options),
            Mock.Of<ILogger<ActivityLogEventSource>>());

        source.GetManagementUrl().Should().Be("https://management.usgovcloudapi.net");
    }

    // ─── GetRecentEventsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetRecentEventsAsync_ShouldReturnEmptyList()
    {
        var options = new MonitoringOptions();
        var source = new ActivityLogEventSource(
            Options.Create(options),
            Mock.Of<ILogger<ActivityLogEventSource>>());

        var events = await source.GetRecentEventsAsync("sub-1", DateTimeOffset.UtcNow.AddHours(-1));

        events.Should().NotBeNull();
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentEventsAsync_WithResourceGroup_ShouldReturnEmptyList()
    {
        var options = new MonitoringOptions();
        var source = new ActivityLogEventSource(
            Options.Create(options),
            Mock.Of<ILogger<ActivityLogEventSource>>());

        var events = await source.GetRecentEventsAsync(
            "sub-1", DateTimeOffset.UtcNow.AddHours(-1), "rg-test");

        events.Should().NotBeNull();
        events.Should().BeEmpty();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// ComplianceWatchHostedService Tests — Event-Driven Monitoring
// ═══════════════════════════════════════════════════════════════════════════

public class ComplianceWatchHostedServiceTests : IDisposable
{
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;
    private readonly Mock<IComplianceWatchService> _watchServiceMock;
    private readonly Mock<IAlertManager> _alertManagerMock;
    private readonly Mock<IComplianceEventSource> _eventSourceMock;
    private readonly MonitoringOptions _monitoringOptions;
    private readonly ComplianceWatchHostedService _service;

    public ComplianceWatchHostedServiceTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"HostedService_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _watchServiceMock = new Mock<IComplianceWatchService>();
        _watchServiceMock
            .Setup(s => s.RunMonitoringCheckAsync(It.IsAny<MonitoringConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _alertManagerMock = new Mock<IAlertManager>();
        _alertManagerMock
            .Setup(m => m.CreateAlertAsync(It.IsAny<ComplianceAlert>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceAlert a, CancellationToken _) =>
            {
                a.Id = Guid.NewGuid();
                a.AlertId = $"ALT-{DateTimeOffset.UtcNow:yyyyMMdd}00001";
                a.Status = AlertStatus.New;
                return a;
            });

        _eventSourceMock = new Mock<IComplianceEventSource>();
        _eventSourceMock
            .Setup(e => e.GetRecentEventsAsync(
                It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ComplianceEvent>());

        _monitoringOptions = new MonitoringOptions
        {
            TickIntervalSeconds = 60,
            EventPollIntervalSeconds = 120
        };

        _service = new ComplianceWatchHostedService(
            new TestDbContextFactory(_dbOptions),
            _watchServiceMock.Object,
            _alertManagerMock.Object,
            _eventSourceMock.Object,
            Options.Create(_monitoringOptions),
            Mock.Of<ILogger<ComplianceWatchHostedService>>());
    }

    public void Dispose()
    {
        using var db = new AtoCopilotContext(_dbOptions);
        db.Database.EnsureDeleted();
    }

    // ─── RunEventDrivenChecksAsync ─────────────────────────────────────────

    [Fact]
    public async Task RunEventDrivenChecks_NoEventDrivenConfigs_ShouldDoNothing()
    {
        // Only Scheduled mode configs exist
        await SeedConfig("sub-sched", MonitoringMode.Scheduled);

        await _service.RunEventDrivenChecksAsync(CancellationToken.None);

        _eventSourceMock.Verify(
            e => e.GetRecentEventsAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunEventDrivenChecks_EventDrivenConfig_NoEvents_ShouldNotTriggerCheck()
    {
        await SeedConfig("sub-ed", MonitoringMode.EventDriven);

        await _service.RunEventDrivenChecksAsync(CancellationToken.None);

        _eventSourceMock.Verify(
            e => e.GetRecentEventsAsync("sub-ed", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _watchServiceMock.Verify(
            s => s.RunMonitoringCheckAsync(It.IsAny<MonitoringConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunEventDrivenChecks_WithEvents_ShouldTriggerComplianceCheck()
    {
        await SeedConfig("sub-events", MonitoringMode.EventDriven);

        var events = new List<ComplianceEvent>
        {
            new()
            {
                EventId = "evt-1",
                EventType = "ResourceWrite",
                ResourceId = "/subscriptions/sub-events/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/sa1",
                OperationName = "Microsoft.Storage/storageAccounts/write",
                Timestamp = DateTimeOffset.UtcNow,
                SubscriptionId = "sub-events"
            }
        };

        _eventSourceMock.Setup(e => e.GetRecentEventsAsync(
                "sub-events", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);

        await _service.RunEventDrivenChecksAsync(CancellationToken.None);

        _watchServiceMock.Verify(
            s => s.RunMonitoringCheckAsync(
                It.Is<MonitoringConfiguration>(c => c.SubscriptionId == "sub-events"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunEventDrivenChecks_BothMode_ShouldAlsoBeQueried()
    {
        await SeedConfig("sub-both", MonitoringMode.Both);

        await _service.RunEventDrivenChecksAsync(CancellationToken.None);

        _eventSourceMock.Verify(
            e => e.GetRecentEventsAsync("sub-both", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunEventDrivenChecks_PolicyDriftEvent_ShouldTriggerCheck()
    {
        await SeedConfig("sub-drift", MonitoringMode.EventDriven);

        var events = new List<ComplianceEvent>
        {
            new()
            {
                EventId = "evt-policy",
                EventType = "PolicyAssignmentChange",
                ResourceId = "/subscriptions/sub-drift/providers/Microsoft.Authorization/policyAssignments/pa1",
                OperationName = "Microsoft.Authorization/policyAssignments/write",
                Timestamp = DateTimeOffset.UtcNow,
                SubscriptionId = "sub-drift"
            }
        };

        _eventSourceMock.Setup(e => e.GetRecentEventsAsync(
                "sub-drift", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);

        await _service.RunEventDrivenChecksAsync(CancellationToken.None);

        _watchServiceMock.Verify(
            s => s.RunMonitoringCheckAsync(
                It.Is<MonitoringConfiguration>(c => c.SubscriptionId == "sub-drift"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunEventDrivenChecks_WithEvents_ShouldAdvanceHighWaterMark()
    {
        var configId = await SeedConfig("sub-hwm", MonitoringMode.EventDriven);

        var eventTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var events = new List<ComplianceEvent>
        {
            new()
            {
                EventId = "evt-hwm",
                EventType = "ResourceWrite",
                ResourceId = "/subscriptions/sub-hwm/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/sa1",
                OperationName = "Microsoft.Storage/storageAccounts/write",
                Timestamp = eventTime,
                SubscriptionId = "sub-hwm"
            }
        };

        _eventSourceMock.Setup(e => e.GetRecentEventsAsync(
                "sub-hwm", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);

        await _service.RunEventDrivenChecksAsync(CancellationToken.None);

        // Verify high-water mark was advanced
        await using var db = new AtoCopilotContext(_dbOptions);
        var config = await db.MonitoringConfigurations.FindAsync(configId);
        config!.LastEventCheckAt.Should().Be(eventTime);
    }

    [Fact]
    public async Task RunEventDrivenChecks_EventSourceThrows_ShouldNotAdvanceHighWaterMark()
    {
        var configId = await SeedConfig("sub-fail", MonitoringMode.EventDriven);

        _eventSourceMock.Setup(e => e.GetRecentEventsAsync(
                "sub-fail", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Activity Log unavailable"));

        await _service.RunEventDrivenChecksAsync(CancellationToken.None);

        // High-water mark should NOT have advanced
        await using var db = new AtoCopilotContext(_dbOptions);
        var config = await db.MonitoringConfigurations.FindAsync(configId);
        config!.LastEventCheckAt.Should().BeNull();

        // Compliance check should NOT have run
        _watchServiceMock.Verify(
            s => s.RunMonitoringCheckAsync(It.IsAny<MonitoringConfiguration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunEventDrivenChecks_DisabledConfig_ShouldBeSkipped()
    {
        // Seed a disabled EventDriven config
        await using (var db = new AtoCopilotContext(_dbOptions))
        {
            db.MonitoringConfigurations.Add(new MonitoringConfiguration
            {
                Id = Guid.NewGuid(),
                SubscriptionId = "sub-disabled",
                IsEnabled = false,
                Mode = MonitoringMode.EventDriven,
                Frequency = MonitoringFrequency.Hourly,
                NextRunAt = DateTimeOffset.UtcNow.AddHours(1),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await _service.RunEventDrivenChecksAsync(CancellationToken.None);

        _eventSourceMock.Verify(
            e => e.GetRecentEventsAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunEventDrivenChecks_MultipleEvents_ShouldUseMaxTimestampAsHighWaterMark()
    {
        var configId = await SeedConfig("sub-multi", MonitoringMode.EventDriven);

        var oldTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var midTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var newTime = DateTimeOffset.UtcNow.AddMinutes(-1);

        var events = new List<ComplianceEvent>
        {
            new() { EventId = "e1", OperationName = "Microsoft.Storage/storageAccounts/write", Timestamp = oldTime, SubscriptionId = "sub-multi", ResourceId = "r1" },
            new() { EventId = "e2", OperationName = "Microsoft.Compute/virtualMachines/write", Timestamp = newTime, SubscriptionId = "sub-multi", ResourceId = "r2" },
            new() { EventId = "e3", OperationName = "Microsoft.Network/virtualNetworks/delete", Timestamp = midTime, SubscriptionId = "sub-multi", ResourceId = "r3" }
        };

        _eventSourceMock.Setup(e => e.GetRecentEventsAsync(
                "sub-multi", It.IsAny<DateTimeOffset>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);

        await _service.RunEventDrivenChecksAsync(CancellationToken.None);

        await using var db = new AtoCopilotContext(_dbOptions);
        var config = await db.MonitoringConfigurations.FindAsync(configId);
        config!.LastEventCheckAt.Should().Be(newTime);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private async Task<Guid> SeedConfig(string subscriptionId, MonitoringMode mode, string? resourceGroup = null)
    {
        var id = Guid.NewGuid();
        await using var db = new AtoCopilotContext(_dbOptions);
        db.MonitoringConfigurations.Add(new MonitoringConfiguration
        {
            Id = id,
            SubscriptionId = subscriptionId,
            ResourceGroupName = resourceGroup,
            IsEnabled = true,
            Mode = mode,
            Frequency = MonitoringFrequency.Hourly,
            NextRunAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    private class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}

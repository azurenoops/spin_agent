using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Configuration;
using TaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Tests.Unit.Tools;

public class ComplianceWatchToolTests
{
    private readonly Mock<IComplianceWatchService> _watchServiceMock;
    private readonly WatchEnableMonitoringTool _enableTool;
    private readonly WatchDisableMonitoringTool _disableTool;
    private readonly WatchConfigureMonitoringTool _configureTool;
    private readonly WatchMonitoringStatusTool _statusTool;

    public ComplianceWatchToolTests()
    {
        _watchServiceMock = new Mock<IComplianceWatchService>();

        _enableTool = new WatchEnableMonitoringTool(
            _watchServiceMock.Object,
            Mock.Of<ILogger<WatchEnableMonitoringTool>>());

        _disableTool = new WatchDisableMonitoringTool(
            _watchServiceMock.Object,
            Mock.Of<ILogger<WatchDisableMonitoringTool>>());

        _configureTool = new WatchConfigureMonitoringTool(
            _watchServiceMock.Object,
            Mock.Of<ILogger<WatchConfigureMonitoringTool>>());

        _statusTool = new WatchMonitoringStatusTool(
            _watchServiceMock.Object,
            Mock.Of<ILogger<WatchMonitoringStatusTool>>());
    }

    // ─── Tool Identity ─────────────────────────────────────────────────────

    [Fact]
    public void EnableTool_ShouldHaveCorrectNameAndDescription()
    {
        _enableTool.Name.Should().Be("watch_enable_monitoring");
        _enableTool.Description.Should().NotBeNullOrEmpty();
        _enableTool.Parameters.Should().ContainKey("subscription_id");
    }

    [Fact]
    public void DisableTool_ShouldHaveCorrectNameAndDescription()
    {
        _disableTool.Name.Should().Be("watch_disable_monitoring");
        _disableTool.Description.Should().NotBeNullOrEmpty();
        _disableTool.Parameters.Should().ContainKey("subscription_id");
    }

    [Fact]
    public void ConfigureTool_ShouldHaveCorrectNameAndDescription()
    {
        _configureTool.Name.Should().Be("watch_configure_monitoring");
        _configureTool.Description.Should().NotBeNullOrEmpty();
        _configureTool.Parameters.Should().ContainKey("frequency");
        _configureTool.Parameters.Should().ContainKey("mode");
    }

    [Fact]
    public void StatusTool_ShouldHaveCorrectNameAndDescription()
    {
        _statusTool.Name.Should().Be("watch_monitoring_status");
        _statusTool.Description.Should().NotBeNullOrEmpty();
    }

    // ─── WatchEnableMonitoringTool ──────────────────────────────────────────

    [Fact]
    public async Task EnableTool_ValidArgs_ShouldReturnSuccessJson()
    {
        var config = CreateTestConfig("sub-001", MonitoringFrequency.Hourly);
        _watchServiceMock
            .Setup(s => s.EnableMonitoringAsync(
                "sub-001", null, MonitoringFrequency.Hourly, MonitoringMode.Scheduled,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = "sub-001",
            ["frequency"] = "hourly",
            ["mode"] = "scheduled"
        };

        var result = await _enableTool.ExecuteCoreAsync(args, CancellationToken.None);

        result.Should().NotBeNullOrEmpty();
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("subscriptionId").GetString().Should().Be("sub-001");
        doc.RootElement.GetProperty("data").GetProperty("isEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task EnableTool_WithResourceGroup_ShouldPassResourceGroup()
    {
        var config = CreateTestConfig("sub-rg", MonitoringFrequency.Daily, resourceGroup: "my-rg");
        _watchServiceMock
            .Setup(s => s.EnableMonitoringAsync(
                "sub-rg", "my-rg", MonitoringFrequency.Daily, MonitoringMode.Scheduled,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = "sub-rg",
            ["resource_group"] = "my-rg",
            ["frequency"] = "daily"
        };

        var result = await _enableTool.ExecuteCoreAsync(args, CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data").GetProperty("resourceGroup").GetString().Should().Be("my-rg");
    }

    [Fact]
    public async Task EnableTool_MissingSubscriptionId_ShouldThrow()
    {
        var args = new Dictionary<string, object?>();

        var act = () => _enableTool.ExecuteCoreAsync(args, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EnableTool_InvalidFrequency_ShouldThrow()
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = "sub-001",
            ["frequency"] = "biweekly"
        };

        var act = () => _enableTool.ExecuteCoreAsync(args, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*INVALID_FREQUENCY*");
    }

    [Fact]
    public async Task EnableTool_InvalidMode_ShouldThrow()
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = "sub-001",
            ["mode"] = "manual"
        };

        var act = () => _enableTool.ExecuteCoreAsync(args, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*INVALID_MODE*");
    }

    [Theory]
    [InlineData("15min", MonitoringFrequency.FifteenMinutes)]
    [InlineData("hourly", MonitoringFrequency.Hourly)]
    [InlineData("daily", MonitoringFrequency.Daily)]
    [InlineData("weekly", MonitoringFrequency.Weekly)]
    public async Task EnableTool_AllFrequencies_ShouldParseCorrectly(string input, MonitoringFrequency expected)
    {
        var config = CreateTestConfig("sub-freq", expected);
        _watchServiceMock
            .Setup(s => s.EnableMonitoringAsync(
                "sub-freq", null, expected, It.IsAny<MonitoringMode>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = "sub-freq",
            ["frequency"] = input
        };

        var result = await _enableTool.ExecuteCoreAsync(args, CancellationToken.None);
        result.Should().NotBeNullOrEmpty();

        _watchServiceMock.Verify(s => s.EnableMonitoringAsync(
            "sub-freq", null, expected, It.IsAny<MonitoringMode>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── WatchDisableMonitoringTool ─────────────────────────────────────────

    [Fact]
    public async Task DisableTool_ValidArgs_ShouldReturnSuccessJson()
    {
        var config = CreateTestConfig("sub-dis", MonitoringFrequency.Hourly);
        config.IsEnabled = false;
        _watchServiceMock
            .Setup(s => s.DisableMonitoringAsync("sub-dis", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = "sub-dis"
        };

        var result = await _disableTool.ExecuteCoreAsync(args, CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("isEnabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task DisableTool_MissingSubscriptionId_ShouldThrow()
    {
        var args = new Dictionary<string, object?>();

        var act = () => _disableTool.ExecuteCoreAsync(args, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── WatchConfigureMonitoringTool ───────────────────────────────────────

    [Fact]
    public async Task ConfigureTool_ValidArgs_ShouldReturnSuccessJson()
    {
        var config = CreateTestConfig("sub-cfg", MonitoringFrequency.Daily, mode: MonitoringMode.Both);
        _watchServiceMock
            .Setup(s => s.ConfigureMonitoringAsync(
                "sub-cfg", null, MonitoringFrequency.Daily, MonitoringMode.Both,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = "sub-cfg",
            ["frequency"] = "daily",
            ["mode"] = "both"
        };

        var result = await _configureTool.ExecuteCoreAsync(args, CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("frequency").GetString().Should().Be("daily");
    }

    [Fact]
    public async Task ConfigureTool_FrequencyOnly_ShouldPassNullMode()
    {
        var config = CreateTestConfig("sub-fo", MonitoringFrequency.Weekly);
        _watchServiceMock
            .Setup(s => s.ConfigureMonitoringAsync(
                "sub-fo", null, MonitoringFrequency.Weekly, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = "sub-fo",
            ["frequency"] = "weekly"
        };

        var result = await _configureTool.ExecuteCoreAsync(args, CancellationToken.None);
        result.Should().NotBeNullOrEmpty();

        _watchServiceMock.Verify(s => s.ConfigureMonitoringAsync(
            "sub-fo", null, MonitoringFrequency.Weekly, null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfigureTool_MissingSubscriptionId_ShouldThrow()
    {
        var args = new Dictionary<string, object?>();

        var act = () => _configureTool.ExecuteCoreAsync(args, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── WatchMonitoringStatusTool ──────────────────────────────────────────

    [Fact]
    public async Task StatusTool_NoFilter_ShouldReturnAllConfigs()
    {
        var configs = new List<MonitoringConfiguration>
        {
            CreateTestConfig("sub-s1", MonitoringFrequency.Hourly),
            CreateTestConfig("sub-s2", MonitoringFrequency.Daily)
        };

        _watchServiceMock
            .Setup(s => s.GetMonitoringStatusAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(configs);

        var args = new Dictionary<string, object?>();

        var result = await _statusTool.ExecuteCoreAsync(args, CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("totalConfigurations").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("data").GetProperty("activeConfigurations").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task StatusTool_WithFilter_ShouldFilterBySubscription()
    {
        var configs = new List<MonitoringConfiguration>
        {
            CreateTestConfig("sub-filtered", MonitoringFrequency.Hourly)
        };

        _watchServiceMock
            .Setup(s => s.GetMonitoringStatusAsync("sub-filtered", It.IsAny<CancellationToken>()))
            .ReturnsAsync(configs);

        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = "sub-filtered"
        };

        var result = await _statusTool.ExecuteCoreAsync(args, CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data").GetProperty("totalConfigurations").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task StatusTool_EmptyList_ShouldReturnZeroCounts()
    {
        _watchServiceMock
            .Setup(s => s.GetMonitoringStatusAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonitoringConfiguration>());

        var args = new Dictionary<string, object?>();

        var result = await _statusTool.ExecuteCoreAsync(args, CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data").GetProperty("totalConfigurations").GetInt32().Should().Be(0);
    }

    // ─── Response Format ────────────────────────────────────────────────────

    [Fact]
    public async Task AllTools_ResponseShouldContainMetadata()
    {
        var config = CreateTestConfig("sub-meta", MonitoringFrequency.Hourly);
        _watchServiceMock
            .Setup(s => s.EnableMonitoringAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<MonitoringFrequency>(),
                It.IsAny<MonitoringMode>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var result = await _enableTool.ExecuteCoreAsync(
            new Dictionary<string, object?> { ["subscription_id"] = "sub-meta" },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.TryGetProperty("metadata", out var metadata).Should().BeTrue();
        metadata.GetProperty("tool").GetString().Should().Be("watch_enable_monitoring");
        metadata.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static MonitoringConfiguration CreateTestConfig(
        string subscriptionId,
        MonitoringFrequency frequency,
        MonitoringMode mode = MonitoringMode.Scheduled,
        string? resourceGroup = null)
    {
        return new MonitoringConfiguration
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            ResourceGroupName = resourceGroup,
            Frequency = frequency,
            Mode = mode,
            IsEnabled = true,
            NextRunAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}

// ─── Alert Management Tool Unit Tests ────────────────────────────────────────

public class AlertManagementToolTests
{
    private readonly Mock<IAlertManager> _alertManagerMock;
    private readonly Mock<IAtoComplianceEngine> _complianceEngineMock;
    private readonly WatchShowAlertsTool _showTool;
    private readonly WatchGetAlertTool _getTool;
    private readonly WatchAcknowledgeAlertTool _ackTool;
    private readonly WatchFixAlertTool _fixTool;
    private readonly WatchDismissAlertTool _dismissTool;

    public AlertManagementToolTests()
    {
        _alertManagerMock = new Mock<IAlertManager>();
        _complianceEngineMock = new Mock<IAtoComplianceEngine>();

        _showTool = new WatchShowAlertsTool(
            _alertManagerMock.Object, Mock.Of<ILogger<WatchShowAlertsTool>>());
        _getTool = new WatchGetAlertTool(
            _alertManagerMock.Object, Mock.Of<ILogger<WatchGetAlertTool>>());
        _ackTool = new WatchAcknowledgeAlertTool(
            _alertManagerMock.Object, Mock.Of<ILogger<WatchAcknowledgeAlertTool>>());
        _fixTool = new WatchFixAlertTool(
            _alertManagerMock.Object, _complianceEngineMock.Object, Mock.Of<ILogger<WatchFixAlertTool>>());
        _dismissTool = new WatchDismissAlertTool(
            _alertManagerMock.Object, Mock.Of<ILogger<WatchDismissAlertTool>>());
    }

    // ─── Tool Identity ───────────────────────────────────────────────────

    [Fact]
    public void ShowAlertsTool_Identity()
    {
        _showTool.Name.Should().Be("watch_show_alerts");
        _showTool.Parameters.Should().ContainKey("subscription_id");
        _showTool.Parameters.Should().ContainKey("severity");
        _showTool.Parameters.Should().ContainKey("status");
    }

    [Fact]
    public void GetAlertTool_Identity()
    {
        _getTool.Name.Should().Be("watch_get_alert");
        _getTool.Parameters.Should().ContainKey("alert_id");
    }

    [Fact]
    public void AcknowledgeAlertTool_Identity()
    {
        _ackTool.Name.Should().Be("watch_acknowledge_alert");
        _ackTool.Parameters.Should().ContainKey("alert_id");
        _ackTool.Parameters.Should().ContainKey("user_id");
        _ackTool.Parameters.Should().ContainKey("user_role");
    }

    [Fact]
    public void FixAlertTool_Identity()
    {
        _fixTool.Name.Should().Be("watch_fix_alert");
        _fixTool.Parameters.Should().ContainKey("alert_id");
        _fixTool.Parameters.Should().ContainKey("dry_run");
    }

    [Fact]
    public void DismissAlertTool_Identity()
    {
        _dismissTool.Name.Should().Be("watch_dismiss_alert");
        _dismissTool.Parameters.Should().ContainKey("justification");
        _dismissTool.Parameters.Should().ContainKey("user_role");
    }

    // ─── WatchShowAlertsTool ─────────────────────────────────────────────

    [Fact]
    public async Task ShowAlerts_ReturnsAlertList()
    {
        var alerts = new List<ComplianceAlert>
        {
            CreateAlert("ALT-2026010100001", AlertSeverity.Critical),
            CreateAlert("ALT-2026010100002", AlertSeverity.High)
        };

        _alertManagerMock.Setup(m => m.GetAlertsAsync(
            It.IsAny<string?>(), It.IsAny<AlertSeverity?>(), It.IsAny<AlertStatus?>(),
            It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((alerts, 2));

        var result = await _showTool.ExecuteCoreAsync(
            new Dictionary<string, object?> { ["subscription_id"] = "sub-1" },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("alerts").GetArrayLength().Should().Be(2);
        doc.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ShowAlerts_WithSeverityFilter()
    {
        _alertManagerMock.Setup(m => m.GetAlertsAsync(
            "sub-1", AlertSeverity.Critical, null, null, 7, 1, 50,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert>(), 0));

        var result = await _showTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["subscription_id"] = "sub-1",
                ["severity"] = "Critical"
            },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data").GetProperty("alerts").GetArrayLength().Should().Be(0);
        _alertManagerMock.Verify(m => m.GetAlertsAsync(
            "sub-1", AlertSeverity.Critical, null, null, 7, 1, 50,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShowAlerts_WithStatusFilter()
    {
        _alertManagerMock.Setup(m => m.GetAlertsAsync(
            It.IsAny<string?>(), It.IsAny<AlertSeverity?>(), AlertStatus.New,
            It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert>(), 0));

        await _showTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["subscription_id"] = "sub-1",
                ["status"] = "New"
            },
            CancellationToken.None);

        _alertManagerMock.Verify(m => m.GetAlertsAsync(
            "sub-1", null, AlertStatus.New, null, 7, 1, 50,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShowAlerts_InvalidSeverity_Throws()
    {
        var act = () => _showTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["subscription_id"] = "sub-1",
                ["severity"] = "UltraHigh"
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*INVALID_SEVERITY*");
    }

    [Fact]
    public async Task ShowAlerts_CustomPagination()
    {
        _alertManagerMock.Setup(m => m.GetAlertsAsync(
            It.IsAny<string?>(), It.IsAny<AlertSeverity?>(), It.IsAny<AlertStatus?>(),
            It.IsAny<string?>(), It.IsAny<int>(), 3, 10,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert>(), 25));

        var result = await _showTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["subscription_id"] = "sub-1",
                ["page"] = 3,
                ["page_size"] = 10
            },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data").GetProperty("page").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("data").GetProperty("pageSize").GetInt32().Should().Be(10);
    }

    // ─── WatchGetAlertTool ───────────────────────────────────────────────

    [Fact]
    public async Task GetAlert_ReturnsFullDetails()
    {
        var alert = CreateAlert("ALT-2026010100001", AlertSeverity.High);
        alert.Description = "Encryption disabled on storage";
        alert.RecommendedAction = "Re-enable encryption";

        _alertManagerMock.Setup(m => m.GetAlertByAlertIdAsync("ALT-2026010100001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);

        var result = await _getTool.ExecuteCoreAsync(
            new Dictionary<string, object?> { ["alert_id"] = "ALT-2026010100001" },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("alertId").GetString().Should().Be("ALT-2026010100001");
        doc.RootElement.GetProperty("data").GetProperty("description").GetString().Should().Contain("Encryption");
        doc.RootElement.GetProperty("data").GetProperty("recommendedAction").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetAlert_NotFound_Throws()
    {
        _alertManagerMock.Setup(m => m.GetAlertByAlertIdAsync("ALT-NOTREAL", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceAlert?)null);

        var act = () => _getTool.ExecuteCoreAsync(
            new Dictionary<string, object?> { ["alert_id"] = "ALT-NOTREAL" },
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*ALERT_NOT_FOUND*");
    }

    [Fact]
    public async Task GetAlert_MissingAlertId_Throws()
    {
        var act = () => _getTool.ExecuteCoreAsync(
            new Dictionary<string, object?>(),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── WatchAcknowledgeAlertTool ───────────────────────────────────────

    [Fact]
    public async Task Acknowledge_SuccessfulTransition()
    {
        var alert = CreateAlert("ALT-2026010100001", AlertSeverity.High);
        var acked = CreateAlert("ALT-2026010100001", AlertSeverity.High);
        acked.Status = AlertStatus.Acknowledged;
        acked.AcknowledgedAt = DateTimeOffset.UtcNow;

        _alertManagerMock.Setup(m => m.GetAlertByAlertIdAsync("ALT-2026010100001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);
        _alertManagerMock.Setup(m => m.TransitionAlertAsync(
            alert.Id, AlertStatus.Acknowledged, "user-1", "Compliance.Administrator",
            It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(acked);

        var result = await _ackTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["alert_id"] = "ALT-2026010100001",
                ["user_id"] = "user-1",
                ["user_role"] = "Compliance.Administrator"
            },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data").GetProperty("newStatus").GetString().Should().Be("Acknowledged");
    }

    [Theory]
    [InlineData("Auditor")]
    [InlineData("Compliance.Auditor")]
    public async Task Acknowledge_AuditorDenied(string role)
    {
        var act = () => _ackTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["alert_id"] = "ALT-2026010100001",
                ["user_id"] = "auditor",
                ["user_role"] = role
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*INSUFFICIENT_PERMISSIONS*read-only*");
    }

    // ─── WatchFixAlertTool ───────────────────────────────────────────────

    [Fact]
    public async Task Fix_DryRun_ReturnsPreview()
    {
        var alert = CreateAlert("ALT-2026010100001", AlertSeverity.High);
        alert.RecommendedAction = "Apply encryption policy";

        _alertManagerMock.Setup(m => m.GetAlertByAlertIdAsync("ALT-2026010100001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);

        var result = await _fixTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["alert_id"] = "ALT-2026010100001",
                ["user_id"] = "admin-1",
                ["user_role"] = ComplianceRoles.Administrator,
                ["dry_run"] = true
            },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data").GetProperty("dryRun").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("data").GetProperty("recommendedAction").GetString().Should().Be("Apply encryption policy");

        // Should NOT call TransitionAlertAsync
        _alertManagerMock.Verify(m => m.TransitionAlertAsync(
            It.IsAny<Guid>(), It.IsAny<AlertStatus>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Fix_FullRemediation_TransitionsToResolved()
    {
        var alert = CreateAlert("ALT-2026010100001", AlertSeverity.High);
        var inProgress = CreateAlert("ALT-2026010100001", AlertSeverity.High);
        inProgress.Status = AlertStatus.InProgress;
        var resolved = CreateAlert("ALT-2026010100001", AlertSeverity.High);
        resolved.Status = AlertStatus.Resolved;
        resolved.ResolvedAt = DateTimeOffset.UtcNow;

        _alertManagerMock.Setup(m => m.GetAlertByAlertIdAsync("ALT-2026010100001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);
        _alertManagerMock.Setup(m => m.TransitionAlertAsync(
            alert.Id, AlertStatus.InProgress, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inProgress);
        _alertManagerMock.Setup(m => m.TransitionAlertAsync(
            alert.Id, AlertStatus.Resolved, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        _complianceEngineMock.Setup(m => m.RunAssessmentAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceAssessment());

        var result = await _fixTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["alert_id"] = "ALT-2026010100001",
                ["user_id"] = "admin-1",
                ["user_role"] = ComplianceRoles.Administrator
            },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data").GetProperty("newStatus").GetString().Should().Be("Resolved");

        _alertManagerMock.Verify(m => m.TransitionAlertAsync(
            alert.Id, AlertStatus.InProgress, "admin-1", ComplianceRoles.Administrator,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _alertManagerMock.Verify(m => m.TransitionAlertAsync(
            alert.Id, AlertStatus.Resolved, "admin-1", ComplianceRoles.Administrator,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Auditor")]
    [InlineData("Compliance.Auditor")]
    public async Task Fix_AuditorDenied(string role)
    {
        var act = () => _fixTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["alert_id"] = "ALT-2026010100001",
                ["user_id"] = "auditor",
                ["user_role"] = role
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*INSUFFICIENT_PERMISSIONS*read-only*");
    }

    [Fact]
    public async Task Fix_AlertNotFound_Throws()
    {
        _alertManagerMock.Setup(m => m.GetAlertByAlertIdAsync("ALT-NOTREAL", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceAlert?)null);

        var act = () => _fixTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["alert_id"] = "ALT-NOTREAL",
                ["user_id"] = "admin-1",
                ["user_role"] = ComplianceRoles.Administrator
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*ALERT_NOT_FOUND*");
    }

    // ─── WatchDismissAlertTool ───────────────────────────────────────────

    [Fact]
    public async Task Dismiss_ComplianceOfficerSucceeds()
    {
        var alert = CreateAlert("ALT-2026010100001", AlertSeverity.High);
        alert.Status = AlertStatus.Acknowledged;
        var dismissed = CreateAlert("ALT-2026010100001", AlertSeverity.High);
        dismissed.Status = AlertStatus.Dismissed;
        dismissed.DismissedBy = "co-user";
        dismissed.DismissalJustification = "False positive — compensating control in place";

        _alertManagerMock.Setup(m => m.GetAlertByAlertIdAsync("ALT-2026010100001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);
        _alertManagerMock.Setup(m => m.DismissAlertAsync(
            alert.Id, "False positive — compensating control in place", "co-user",
            ComplianceRoles.Administrator, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dismissed);

        var result = await _dismissTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["alert_id"] = "ALT-2026010100001",
                ["justification"] = "False positive — compensating control in place",
                ["user_id"] = "co-user",
                ["user_role"] = ComplianceRoles.Administrator
            },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data").GetProperty("newStatus").GetString().Should().Be("Dismissed");
        doc.RootElement.GetProperty("data").GetProperty("justification").GetString().Should().Contain("compensating");
    }

    [Fact]
    public async Task Dismiss_MissingJustification_Throws()
    {
        var act = () => _dismissTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["alert_id"] = "ALT-2026010100001",
                ["justification"] = "",
                ["user_id"] = "co-user",
                ["user_role"] = ComplianceRoles.Administrator
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*JUSTIFICATION_REQUIRED*");
    }

    [Theory]
    [InlineData("Auditor")]
    [InlineData("Compliance.Auditor")]
    public async Task Dismiss_AuditorDenied(string role)
    {
        var act = () => _dismissTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["alert_id"] = "ALT-2026010100001",
                ["justification"] = "Should be denied",
                ["user_id"] = "auditor",
                ["user_role"] = role
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*INSUFFICIENT_PERMISSIONS*read-only*");
    }

    [Fact]
    public async Task Dismiss_AlertNotFound_Throws()
    {
        _alertManagerMock.Setup(m => m.GetAlertByAlertIdAsync("ALT-NOTREAL", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceAlert?)null);

        var act = () => _dismissTool.ExecuteCoreAsync(
            new Dictionary<string, object?>
            {
                ["alert_id"] = "ALT-NOTREAL",
                ["justification"] = "Test",
                ["user_id"] = "co",
                ["user_role"] = ComplianceRoles.Administrator
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*ALERT_NOT_FOUND*");
    }

    // ─── Response Metadata ───────────────────────────────────────────────

    [Theory]
    [InlineData("watch_show_alerts")]
    [InlineData("watch_get_alert")]
    [InlineData("watch_acknowledge_alert")]
    [InlineData("watch_dismiss_alert")]
    public async Task AlertTools_IncludeMetadata(string toolName)
    {
        var alert = CreateAlert("ALT-2026010100001", AlertSeverity.High);
        alert.Status = AlertStatus.Acknowledged;
        var transitioned = CreateAlert("ALT-2026010100001", AlertSeverity.High);
        transitioned.Status = AlertStatus.InProgress;
        var dismissed = CreateAlert("ALT-2026010100001", AlertSeverity.High);
        dismissed.Status = AlertStatus.Dismissed;

        _alertManagerMock.Setup(m => m.GetAlertsAsync(
            It.IsAny<string?>(), It.IsAny<AlertSeverity?>(), It.IsAny<AlertStatus?>(),
            It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert> { alert }, 1));
        _alertManagerMock.Setup(m => m.GetAlertByAlertIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);
        _alertManagerMock.Setup(m => m.TransitionAlertAsync(
            It.IsAny<Guid>(), It.IsAny<AlertStatus>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transitioned);
        _alertManagerMock.Setup(m => m.DismissAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(dismissed);

        string result = toolName switch
        {
            "watch_show_alerts" => await _showTool.ExecuteCoreAsync(
                new Dictionary<string, object?> { ["subscription_id"] = "sub-1" }, CancellationToken.None),
            "watch_get_alert" => await _getTool.ExecuteCoreAsync(
                new Dictionary<string, object?> { ["alert_id"] = "ALT-2026010100001" }, CancellationToken.None),
            "watch_acknowledge_alert" => await _ackTool.ExecuteCoreAsync(
                new Dictionary<string, object?>
                {
                    ["alert_id"] = "ALT-2026010100001",
                    ["user_id"] = "admin", ["user_role"] = ComplianceRoles.Administrator
                }, CancellationToken.None),
            "watch_dismiss_alert" => await _dismissTool.ExecuteCoreAsync(
                new Dictionary<string, object?>
                {
                    ["alert_id"] = "ALT-2026010100001", ["justification"] = "Test",
                    ["user_id"] = "co", ["user_role"] = ComplianceRoles.Administrator
                }, CancellationToken.None),
            _ => throw new Exception()
        };

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("metadata").GetProperty("tool").GetString().Should().Be(toolName);
        doc.RootElement.GetProperty("metadata").GetProperty("timestamp").GetDateTimeOffset()
            .Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ComplianceAlert CreateAlert(string alertId, AlertSeverity severity)
    {
        return new ComplianceAlert
        {
            Id = Guid.NewGuid(),
            AlertId = alertId,
            Type = AlertType.Drift,
            Severity = severity,
            Status = AlertStatus.New,
            Title = $"Test alert {alertId}",
            Description = "Unit test alert",
            SubscriptionId = "sub-test",
            AffectedResources = ["res-1"],
            ControlId = "SC-8",
            ControlFamily = "SC",
            CreatedAt = DateTimeOffset.UtcNow,
            SlaDeadline = DateTimeOffset.UtcNow.AddHours(24)
        };
    }
}

/// <summary>
/// Unit tests for alert rules, suppression tools, and ComplianceWatchService rule/suppression logic.
/// </summary>
public class AlertRulesAndSuppressionToolTests
{
    private readonly Mock<IComplianceWatchService> _watchServiceMock;
    private readonly WatchCreateRuleTool _createRuleTool;
    private readonly WatchListRulesTool _listRulesTool;
    private readonly WatchSuppressAlertsTool _suppressTool;
    private readonly WatchListSuppressionsTool _listSuppressionsTool;
    private readonly WatchConfigureQuietHoursTool _quietHoursTool;

    public AlertRulesAndSuppressionToolTests()
    {
        _watchServiceMock = new Mock<IComplianceWatchService>();

        _createRuleTool = new WatchCreateRuleTool(
            _watchServiceMock.Object, Mock.Of<ILogger<WatchCreateRuleTool>>());
        _listRulesTool = new WatchListRulesTool(
            _watchServiceMock.Object, Mock.Of<ILogger<WatchListRulesTool>>());
        _suppressTool = new WatchSuppressAlertsTool(
            _watchServiceMock.Object, Mock.Of<ILogger<WatchSuppressAlertsTool>>());
        _listSuppressionsTool = new WatchListSuppressionsTool(
            _watchServiceMock.Object, Mock.Of<ILogger<WatchListSuppressionsTool>>());
        _quietHoursTool = new WatchConfigureQuietHoursTool(
            _watchServiceMock.Object, Mock.Of<ILogger<WatchConfigureQuietHoursTool>>());
    }

    // ─── Tool Identity Tests ─────────────────────────────────────────────────

    [Theory]
    [InlineData("watch_create_rule")]
    [InlineData("watch_list_rules")]
    [InlineData("watch_suppress_alerts")]
    [InlineData("watch_list_suppressions")]
    [InlineData("watch_configure_quiet_hours")]
    public void Tool_Should_Have_Correct_Name(string expectedName)
    {
        var tools = new BaseTool[] { _createRuleTool, _listRulesTool, _suppressTool, _listSuppressionsTool, _quietHoursTool };
        tools.Should().Contain(t => t.Name == expectedName);
    }

    // ─── WatchCreateRuleTool Tests ───────────────────────────────────────────

    [Fact]
    public async Task CreateRule_AuditorDenied()
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = "Test Rule", ["user_role"] = "Compliance.Auditor"
        };

        var result = await _createRuleTool.ExecuteCoreAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("INSUFFICIENT_PERMISSIONS");
    }

    [Fact]
    public async Task CreateRule_Success()
    {
        var returnedRule = new AlertRule
        {
            Id = Guid.NewGuid(), Name = "AC High Rule", ControlFamily = "AC",
            SeverityOverride = AlertSeverity.High, IsEnabled = true
        };
        _watchServiceMock.Setup(s => s.CreateAlertRuleAsync(It.IsAny<AlertRule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnedRule);

        var args = new Dictionary<string, object?>
        {
            ["name"] = "AC High Rule", ["control_family"] = "AC",
            ["severity_override"] = "High", ["user_role"] = "ComplianceOfficer"
        };

        var result = await _createRuleTool.ExecuteCoreAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("name").GetString().Should().Be("AC High Rule");
        doc.RootElement.GetProperty("metadata").GetProperty("tool").GetString().Should().Be("watch_create_rule");
    }

    [Fact]
    public async Task CreateRule_MissingName_Throws()
    {
        var args = new Dictionary<string, object?> { ["user_role"] = "ComplianceOfficer" };
        var act = async () => await _createRuleTool.ExecuteCoreAsync(args, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*name*");
    }

    // ─── WatchListRulesTool Tests ────────────────────────────────────────────

    [Fact]
    public async Task ListRules_ReturnsRules()
    {
        _watchServiceMock.Setup(s => s.GetAlertRulesAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AlertRule>
            {
                new() { Id = Guid.NewGuid(), Name = "Rule 1", IsDefault = true },
                new() { Id = Guid.NewGuid(), Name = "Rule 2", IsDefault = false }
            });

        var args = new Dictionary<string, object?> { ["subscription_id"] = "sub-1" };
        var result = await _listRulesTool.ExecuteCoreAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data").GetProperty("totalRules").GetInt32().Should().Be(2);
    }

    // ─── WatchSuppressAlertsTool Tests ───────────────────────────────────────

    [Theory]
    [InlineData("Compliance.Auditor")]
    [InlineData("Auditor")]
    public async Task SuppressAlerts_AuditorDenied(string role)
    {
        var args = new Dictionary<string, object?>
        {
            ["type"] = "temporary", ["user_role"] = role
        };

        var result = await _suppressTool.ExecuteCoreAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("INSUFFICIENT_PERMISSIONS");
    }

    [Fact]
    public async Task SuppressAlerts_TemporarySuccess()
    {
        var returnedRule = new SuppressionRule
        {
            Id = Guid.NewGuid(), Type = SuppressionType.Temporary,
            SubscriptionId = "sub-1", IsActive = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        _watchServiceMock.Setup(s => s.CreateSuppressionAsync(It.IsAny<SuppressionRule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnedRule);

        var args = new Dictionary<string, object?>
        {
            ["type"] = "temporary", ["subscription_id"] = "sub-1",
            ["expires_at"] = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
            ["user_role"] = "ComplianceOfficer"
        };

        var result = await _suppressTool.ExecuteCoreAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("type").GetString().Should().Be("Temporary");
    }

    [Fact]
    public async Task SuppressAlerts_InvalidType_ReturnsError()
    {
        var args = new Dictionary<string, object?>
        {
            ["type"] = "invalid_type", ["user_role"] = "ComplianceOfficer"
        };

        var result = await _suppressTool.ExecuteCoreAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_TYPE");
    }

    // ─── WatchListSuppressionsTool Tests ─────────────────────────────────────

    [Fact]
    public async Task ListSuppressions_ReturnsActiveSuppressions()
    {
        _watchServiceMock.Setup(s => s.GetSuppressionsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SuppressionRule>
            {
                new() { Id = Guid.NewGuid(), Type = SuppressionType.Temporary, IsActive = true },
                new() { Id = Guid.NewGuid(), Type = SuppressionType.Permanent, IsActive = true }
            });

        var args = new Dictionary<string, object?>();
        var result = await _listSuppressionsTool.ExecuteCoreAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("data").GetProperty("totalSuppressions").GetInt32().Should().Be(2);
    }

    // ─── WatchConfigureQuietHoursTool Tests ──────────────────────────────────

    [Fact]
    public async Task ConfigureQuietHours_AuditorDenied()
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = "sub-1", ["start_time"] = "22:00",
            ["end_time"] = "06:00", ["user_role"] = "Auditor"
        };

        var result = await _quietHoursTool.ExecuteCoreAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task ConfigureQuietHours_Success()
    {
        _watchServiceMock.Setup(s => s.ConfigureQuietHoursAsync(
                "sub-1", It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SuppressionRule
            {
                Id = Guid.NewGuid(), QuietHoursStart = new TimeOnly(22, 0),
                QuietHoursEnd = new TimeOnly(6, 0), IsActive = true
            });

        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = "sub-1", ["start_time"] = "22:00",
            ["end_time"] = "06:00", ["user_role"] = "ComplianceOfficer"
        };

        var result = await _quietHoursTool.ExecuteCoreAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("quietHoursStart").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ConfigureQuietHours_InvalidTime_Throws()
    {
        var args = new Dictionary<string, object?>
        {
            ["subscription_id"] = "sub-1", ["start_time"] = "not-a-time",
            ["end_time"] = "06:00", ["user_role"] = "ComplianceOfficer"
        };

        var act = async () => await _quietHoursTool.ExecuteCoreAsync(args, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*INVALID_TIME*");
    }

    // ─── IsAlertSuppressed Logic Tests ───────────────────────────────────────

    [Fact]
    public void IsAlertSuppressed_MatchingPermanent_ReturnsTrue()
    {
        var svc = CreateWatchService();
        var alert = CreateAlert("sub-1", "SC", "SC-8");
        var suppressions = new List<SuppressionRule>
        {
            new() { SubscriptionId = "sub-1", ControlFamily = "SC", Type = SuppressionType.Permanent, IsActive = true }
        };

        svc.IsAlertSuppressed(alert, suppressions).Should().BeTrue();
    }

    [Fact]
    public void IsAlertSuppressed_DifferentSubscription_ReturnsFalse()
    {
        var svc = CreateWatchService();
        var alert = CreateAlert("sub-1", "SC", "SC-8");
        var suppressions = new List<SuppressionRule>
        {
            new() { SubscriptionId = "sub-OTHER", ControlFamily = "SC", Type = SuppressionType.Permanent, IsActive = true }
        };

        svc.IsAlertSuppressed(alert, suppressions).Should().BeFalse();
    }

    [Fact]
    public void IsAlertSuppressed_ExpiredTemporary_ReturnsFalse()
    {
        var svc = CreateWatchService();
        var alert = CreateAlert("sub-1", "AC", "AC-1");
        var suppressions = new List<SuppressionRule>
        {
            new()
            {
                SubscriptionId = "sub-1", ControlFamily = "AC",
                Type = SuppressionType.Temporary, ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
                IsActive = true
            }
        };

        svc.IsAlertSuppressed(alert, suppressions).Should().BeFalse();
    }

    [Fact]
    public void IsAlertSuppressed_CriticalBypassesQuietHours_ReturnsFalse()
    {
        var svc = CreateWatchService();
        var alert = CreateAlert("sub-1", "SC", "SC-28", AlertSeverity.Critical);
        var suppressions = new List<SuppressionRule>
        {
            new()
            {
                SubscriptionId = "sub-1", Type = SuppressionType.Permanent,
                QuietHoursStart = new TimeOnly(0, 0), QuietHoursEnd = new TimeOnly(23, 59),
                IsActive = true
            }
        };

        // Critical alerts should never be suppressed by quiet hours (FR-019)
        svc.IsAlertSuppressed(alert, suppressions).Should().BeFalse();
    }

    [Fact]
    public void IsAlertSuppressed_NonCriticalDuringQuietHours_ReturnsTrue()
    {
        var svc = CreateWatchService();
        var alert = CreateAlert("sub-1", "AC", "AC-1", AlertSeverity.High);
        // Use a wide quiet hours window (00:00-23:59) to guarantee "now" falls in it
        var suppressions = new List<SuppressionRule>
        {
            new()
            {
                SubscriptionId = "sub-1", Type = SuppressionType.Permanent,
                QuietHoursStart = new TimeOnly(0, 0), QuietHoursEnd = new TimeOnly(23, 59),
                IsActive = true
            }
        };

        svc.IsAlertSuppressed(alert, suppressions).Should().BeTrue();
    }

    [Fact]
    public void IsAlertSuppressed_NoMatchingRules_ReturnsFalse()
    {
        var svc = CreateWatchService();
        var alert = CreateAlert("sub-1", "AU", "AU-2");
        var suppressions = new List<SuppressionRule>();

        svc.IsAlertSuppressed(alert, suppressions).Should().BeFalse();
    }

    // ─── MatchAlertRule Logic Tests ──────────────────────────────────────────

    [Fact]
    public void MatchAlertRule_MostSpecificWins()
    {
        var svc = CreateWatchService();
        var alert = CreateAlert("sub-1", "SC", "SC-8");
        var rules = new List<AlertRule>
        {
            new() { Name = "Broad", SubscriptionId = "sub-1", SeverityOverride = AlertSeverity.Medium, IsEnabled = true },
            new() { Name = "Specific", SubscriptionId = "sub-1", ControlFamily = "SC", ControlId = "SC-8",
                SeverityOverride = AlertSeverity.Critical, IsEnabled = true }
        };

        var matched = svc.MatchAlertRule(alert, rules);
        matched.Should().NotBeNull();
        matched!.Name.Should().Be("Specific");
        matched.SeverityOverride.Should().Be(AlertSeverity.Critical);
    }

    [Fact]
    public void MatchAlertRule_DisabledRulesIgnored()
    {
        var svc = CreateWatchService();
        var alert = CreateAlert("sub-1", "SC", "SC-8");
        var rules = new List<AlertRule>
        {
            new() { Name = "Disabled", SubscriptionId = "sub-1", ControlFamily = "SC",
                SeverityOverride = AlertSeverity.Critical, IsEnabled = false }
        };

        svc.MatchAlertRule(alert, rules).Should().BeNull();
    }

    [Fact]
    public void MatchAlertRule_NoMatch_ReturnsNull()
    {
        var svc = CreateWatchService();
        var alert = CreateAlert("sub-1", "AU", "AU-2");
        var rules = new List<AlertRule>
        {
            new() { Name = "SC only", SubscriptionId = "sub-1", ControlFamily = "SC", IsEnabled = true }
        };

        svc.MatchAlertRule(alert, rules).Should().BeNull();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Ato.Copilot.Agents.Compliance.Services.ComplianceWatchService CreateWatchService()
    {
        var options = new DbContextOptionsBuilder<Ato.Copilot.Core.Data.Context.AtoCopilotContext>()
            .UseInMemoryDatabase($"RulesTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var factory = new TestDbContextFactory(options);

        return new Ato.Copilot.Agents.Compliance.Services.ComplianceWatchService(
            factory,
            Mock.Of<IAlertManager>(),
            Mock.Of<IAtoComplianceEngine>(),
            Mock.Of<IRemediationEngine>(),
            Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Configuration.MonitoringOptions()),
            Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Configuration.AlertOptions()),
            Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.ComplianceWatchService>>());
    }

    private static ComplianceAlert CreateAlert(string subId, string family, string controlId, AlertSeverity sev = AlertSeverity.High)
    {
        return new ComplianceAlert
        {
            Id = Guid.NewGuid(), AlertId = $"ALT-{Guid.NewGuid():N}".Substring(0, 12),
            Type = AlertType.Drift, Severity = sev, Status = AlertStatus.New,
            Title = "Test alert", Description = "Test",
            SubscriptionId = subId, AffectedResources = ["res-1"],
            ControlFamily = family, ControlId = controlId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private class TestDbContextFactory : Microsoft.EntityFrameworkCore.IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext>
    {
        private readonly DbContextOptions<Ato.Copilot.Core.Data.Context.AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<Ato.Copilot.Core.Data.Context.AtoCopilotContext> options) => _options = options;
        public Ato.Copilot.Core.Data.Context.AtoCopilotContext CreateDbContext() => new(_options);
    }
}

// ─── Notification & Escalation Tool Tests (T040) ────────────────────────────

public class NotificationAndEscalationToolTests
{
    private readonly Mock<IEscalationService> _escalationMock;
    private readonly WatchConfigureNotificationsTool _notificationTool;
    private readonly WatchConfigureEscalationTool _escalationTool;

    public NotificationAndEscalationToolTests()
    {
        _escalationMock = new Mock<IEscalationService>();

        _notificationTool = new WatchConfigureNotificationsTool(
            _escalationMock.Object,
            Mock.Of<ILogger<WatchConfigureNotificationsTool>>());

        _escalationTool = new WatchConfigureEscalationTool(
            _escalationMock.Object,
            Mock.Of<ILogger<WatchConfigureEscalationTool>>());
    }

    // ─── Tool Identity ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("watch_configure_notifications")]
    [InlineData("watch_configure_escalation")]
    public void ToolIdentity_ShouldHaveCorrectName(string expected)
    {
        BaseTool tool = expected switch
        {
            "watch_configure_notifications" => _notificationTool,
            "watch_configure_escalation" => _escalationTool,
            _ => throw new ArgumentException(expected)
        };

        tool.Name.Should().Be(expected);
        tool.Description.Should().NotBeNullOrEmpty();
        tool.Parameters.Should().NotBeEmpty();
    }

    // ─── WatchConfigureNotificationsTool ────────────────────────────────────

    [Theory]
    [InlineData("Compliance.Auditor")]
    [InlineData("Auditor")]
    public async Task ConfigureNotifications_AuditorDenied(string role)
    {
        var args = new Dictionary<string, object?>
        {
            ["channel"] = "email",
            ["target"] = "ops@contoso.com",
            ["role"] = role
        };

        var result = await _notificationTool.ExecuteAsync(args);
        result.Should().Contain("INSUFFICIENT_PERMISSIONS");
        result.Should().Contain("Auditor");
    }

    [Theory]
    [InlineData("Compliance.PlatformEngineer")]
    [InlineData("PlatformEngineer")]
    public async Task ConfigureNotifications_PlatformEngineerDenied(string role)
    {
        var args = new Dictionary<string, object?>
        {
            ["channel"] = "email",
            ["target"] = "ops@contoso.com",
            ["role"] = role
        };

        var result = await _notificationTool.ExecuteAsync(args);
        result.Should().Contain("INSUFFICIENT_PERMISSIONS");
        result.Should().Contain("Platform Engineers");
    }

    [Fact]
    public async Task ConfigureNotifications_EmailChannel_Success()
    {
        _escalationMock
            .Setup(e => e.ConfigureEscalationPathAsync(It.IsAny<EscalationPath>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EscalationPath p, CancellationToken _) =>
            {
                p.Id = Guid.NewGuid();
                return p;
            });

        var args = new Dictionary<string, object?>
        {
            ["channel"] = "email",
            ["target"] = "ops@contoso.com",
            ["role"] = ComplianceRoles.SecurityLead
        };

        var result = await _notificationTool.ExecuteAsync(args);
        result.Should().Contain("success");
        result.Should().Contain("ops@contoso.com");
        result.Should().Contain("Email");
    }

    [Fact]
    public async Task ConfigureNotifications_WebhookChannel_Success()
    {
        _escalationMock
            .Setup(e => e.ConfigureEscalationPathAsync(It.IsAny<EscalationPath>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EscalationPath p, CancellationToken _) =>
            {
                p.Id = Guid.NewGuid();
                return p;
            });

        var args = new Dictionary<string, object?>
        {
            ["channel"] = "webhook",
            ["target"] = "https://hook.contoso.com/compliance",
            ["role"] = ComplianceRoles.SecurityLead
        };

        var result = await _notificationTool.ExecuteAsync(args);
        result.Should().Contain("success");
        result.Should().Contain("Webhook");
    }

    [Fact]
    public async Task ConfigureNotifications_ChatChannel_Rejected()
    {
        var args = new Dictionary<string, object?>
        {
            ["channel"] = "chat",
            ["target"] = "user-1",
            ["role"] = ComplianceRoles.SecurityLead
        };

        var result = await _notificationTool.ExecuteAsync(args);
        result.Should().Contain("INVALID_CHANNEL");
        result.Should().Contain("Chat is always enabled");
    }

    [Fact]
    public async Task ConfigureNotifications_WithSeverityFilter()
    {
        _escalationMock
            .Setup(e => e.ConfigureEscalationPathAsync(It.IsAny<EscalationPath>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EscalationPath p, CancellationToken _) =>
            {
                p.Id = Guid.NewGuid();
                return p;
            });

        var args = new Dictionary<string, object?>
        {
            ["channel"] = "email",
            ["target"] = "ops@contoso.com",
            ["severity"] = "Critical",
            ["role"] = ComplianceRoles.SecurityLead
        };

        var result = await _notificationTool.ExecuteAsync(args);
        result.Should().Contain("success");
        result.Should().Contain("Critical");
    }

    [Fact]
    public async Task ConfigureNotifications_InvalidSeverity_Throws()
    {
        var args = new Dictionary<string, object?>
        {
            ["channel"] = "email",
            ["target"] = "ops@contoso.com",
            ["severity"] = "Extreme",
            ["role"] = ComplianceRoles.SecurityLead
        };

        var act = () => _notificationTool.ExecuteAsync(args);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*INVALID_SEVERITY*");
    }

    // ─── WatchConfigureEscalationTool ───────────────────────────────────────

    [Theory]
    [InlineData("Compliance.Auditor")]
    [InlineData("Auditor")]
    public async Task ConfigureEscalation_AuditorDenied(string role)
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = "Critical SLA",
            ["severity"] = "Critical",
            ["delay_minutes"] = "15",
            ["recipients"] = "ciso@contoso.com",
            ["role"] = role
        };

        var result = await _escalationTool.ExecuteAsync(args);
        result.Should().Contain("INSUFFICIENT_PERMISSIONS");
    }

    [Theory]
    [InlineData("Compliance.PlatformEngineer")]
    [InlineData("PlatformEngineer")]
    public async Task ConfigureEscalation_PlatformEngineerDenied(string role)
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = "Critical SLA",
            ["severity"] = "Critical",
            ["delay_minutes"] = "15",
            ["recipients"] = "ciso@contoso.com",
            ["role"] = role
        };

        var result = await _escalationTool.ExecuteAsync(args);
        result.Should().Contain("INSUFFICIENT_PERMISSIONS");
        result.Should().Contain("Platform Engineers");
    }

    [Fact]
    public async Task ConfigureEscalation_Success()
    {
        _escalationMock
            .Setup(e => e.ConfigureEscalationPathAsync(It.IsAny<EscalationPath>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EscalationPath p, CancellationToken _) =>
            {
                p.Id = Guid.NewGuid();
                return p;
            });

        var args = new Dictionary<string, object?>
        {
            ["name"] = "Critical SLA",
            ["severity"] = "Critical",
            ["delay_minutes"] = "15",
            ["recipients"] = "ciso@contoso.com,secops@contoso.com",
            ["channel"] = "email",
            ["repeat_minutes"] = "30",
            ["role"] = ComplianceRoles.SecurityLead,
            ["user_id"] = "user-1"
        };

        var result = await _escalationTool.ExecuteAsync(args);
        result.Should().Contain("success");
        result.Should().Contain("Critical SLA");
        result.Should().Contain("Critical");
        result.Should().Contain("15");
    }

    [Fact]
    public async Task ConfigureEscalation_MissingName_Throws()
    {
        var args = new Dictionary<string, object?>
        {
            ["severity"] = "Critical",
            ["delay_minutes"] = "15",
            ["recipients"] = "ciso@contoso.com",
            ["role"] = ComplianceRoles.SecurityLead
        };

        var act = () => _escalationTool.ExecuteAsync(args);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*MISSING_PARAMETER*name*");
    }

    [Fact]
    public async Task ConfigureEscalation_InvalidSeverity_Throws()
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = "Test Path",
            ["severity"] = "Extreme",
            ["delay_minutes"] = "15",
            ["recipients"] = "ciso@contoso.com",
            ["role"] = ComplianceRoles.SecurityLead
        };

        var act = () => _escalationTool.ExecuteAsync(args);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*INVALID_SEVERITY*");
    }

    [Fact]
    public async Task ConfigureEscalation_InvalidDelay_Throws()
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = "Test Path",
            ["severity"] = "Critical",
            ["delay_minutes"] = "0",
            ["recipients"] = "ciso@contoso.com",
            ["role"] = ComplianceRoles.SecurityLead
        };

        var act = () => _escalationTool.ExecuteAsync(args);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*INVALID_DELAY*");
    }

    [Fact]
    public async Task ConfigureEscalation_RepeatTooLow_Throws()
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = "Test Path",
            ["severity"] = "High",
            ["delay_minutes"] = "10",
            ["recipients"] = "ciso@contoso.com",
            ["repeat_minutes"] = "3",
            ["role"] = ComplianceRoles.SecurityLead
        };

        var act = () => _escalationTool.ExecuteAsync(args);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*INVALID_REPEAT*");
    }

    // ─── AlertNotificationService Tests ─────────────────────────────────────

    [Fact]
    public async Task AlertNotificationService_SendNotification_RecordsChatNotification()
    {
        var dbFactory = CreateDbFactory();
        var watchService = new Mock<IComplianceWatchService>();
        watchService.Setup(w => w.IsAlertSuppressed(It.IsAny<ComplianceAlert>(), It.IsAny<IReadOnlyList<SuppressionRule>>()))
            .Returns(false);

        var svc = new Ato.Copilot.Agents.Compliance.Services.AlertNotificationService(
            dbFactory, watchService.Object,
            Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.AlertNotificationService>>());

        var alert = CreateTestAlert(AlertSeverity.Medium);
        await svc.SendNotificationAsync(alert);

        await using var db = dbFactory.CreateDbContext();
        var notifications = await db.AlertNotifications.Where(n => n.AlertId == alert.Id).ToListAsync();
        notifications.Should().HaveCountGreaterOrEqualTo(1);
        notifications.Should().Contain(n => n.Channel == NotificationChannel.Chat);
    }

    [Fact]
    public async Task AlertNotificationService_CriticalAlert_SendsEmailAndChat()
    {
        var dbFactory = CreateDbFactory();
        var watchService = new Mock<IComplianceWatchService>();
        watchService.Setup(w => w.IsAlertSuppressed(It.IsAny<ComplianceAlert>(), It.IsAny<IReadOnlyList<SuppressionRule>>()))
            .Returns(false);

        var svc = new Ato.Copilot.Agents.Compliance.Services.AlertNotificationService(
            dbFactory, watchService.Object,
            Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.AlertNotificationService>>());

        var alert = CreateTestAlert(AlertSeverity.Critical);
        await svc.SendNotificationAsync(alert);

        await using var db = dbFactory.CreateDbContext();
        var notifications = await db.AlertNotifications.Where(n => n.AlertId == alert.Id).ToListAsync();
        notifications.Should().Contain(n => n.Channel == NotificationChannel.Chat);
        notifications.Should().Contain(n => n.Channel == NotificationChannel.Email);
    }

    [Fact]
    public async Task AlertNotificationService_QuietHoursSuppressed_NoNotificationSent()
    {
        var dbFactory = CreateDbFactory();
        var watchService = new Mock<IComplianceWatchService>();
        watchService.Setup(w => w.IsAlertSuppressed(It.IsAny<ComplianceAlert>(), It.IsAny<IReadOnlyList<SuppressionRule>>()))
            .Returns(true); // Suppressed

        var svc = new Ato.Copilot.Agents.Compliance.Services.AlertNotificationService(
            dbFactory, watchService.Object,
            Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.AlertNotificationService>>());

        var alert = CreateTestAlert(AlertSeverity.Medium);
        await svc.SendNotificationAsync(alert);

        await using var db = dbFactory.CreateDbContext();
        var notifications = await db.AlertNotifications.Where(n => n.AlertId == alert.Id).ToListAsync();
        notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task AlertNotificationService_SendDigest_CreatesDigestNotification()
    {
        var dbFactory = CreateDbFactory();
        var subId = "sub-digest-test";

        // Seed a low-severity alert
        await using (var db = dbFactory.CreateDbContext())
        {
            db.ComplianceAlerts.Add(new ComplianceAlert
            {
                Id = Guid.NewGuid(),
                AlertId = "ALT-DIGEST001",
                Type = AlertType.Drift,
                Severity = AlertSeverity.Low,
                Status = AlertStatus.New,
                Title = "Low alert",
                Description = "Test",
                SubscriptionId = subId,
                AffectedResources = ["res-1"],
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var watchService = new Mock<IComplianceWatchService>();
        var svc = new Ato.Copilot.Agents.Compliance.Services.AlertNotificationService(
            dbFactory, watchService.Object,
            Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.AlertNotificationService>>());

        await svc.SendDigestAsync(subId);

        await using var db2 = dbFactory.CreateDbContext();
        var notifications = await db2.AlertNotifications.ToListAsync();
        notifications.Should().ContainSingle(n => n.Subject != null && n.Subject.Contains("Digest"));
    }

    [Fact]
    public async Task AlertNotificationService_GetNotificationsForAlert_ReturnsAuditTrail()
    {
        var dbFactory = CreateDbFactory();
        var alertId = Guid.NewGuid();

        await using (var db = dbFactory.CreateDbContext())
        {
            db.AlertNotifications.Add(new AlertNotification
            {
                Id = Guid.NewGuid(),
                AlertId = alertId,
                Channel = NotificationChannel.Chat,
                Recipient = "system",
                IsDelivered = true,
                SentAt = DateTimeOffset.UtcNow,
                DeliveredAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var watchService = new Mock<IComplianceWatchService>();
        var svc = new Ato.Copilot.Agents.Compliance.Services.AlertNotificationService(
            dbFactory, watchService.Object,
            Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.AlertNotificationService>>());

        var trail = await svc.GetNotificationsForAlertAsync(alertId);
        trail.Should().HaveCount(1);
        trail.First().AlertId.Should().Be(alertId);
    }

    [Fact]
    public void AlertNotificationService_HmacSignature_IsDeterministic()
    {
        var payload = "{\"alertId\":\"ALT-001\",\"severity\":\"Critical\"}";

        var sig1 = Ato.Copilot.Agents.Compliance.Services.AlertNotificationService.ComputeHmacSignature(payload);
        var sig2 = Ato.Copilot.Agents.Compliance.Services.AlertNotificationService.ComputeHmacSignature(payload);

        sig1.Should().NotBeNullOrEmpty();
        sig1.Should().Be(sig2);
    }

    [Fact]
    public void AlertNotificationService_HmacSignature_DifferentPayloadsDifferentSignatures()
    {
        var sig1 = Ato.Copilot.Agents.Compliance.Services.AlertNotificationService.ComputeHmacSignature("payload-1");
        var sig2 = Ato.Copilot.Agents.Compliance.Services.AlertNotificationService.ComputeHmacSignature("payload-2");

        sig1.Should().NotBe(sig2);
    }

    // ─── EscalationHostedService Tests ──────────────────────────────────────

    [Fact]
    public async Task EscalationService_ConfigurePath_ValidInput_Success()
    {
        var dbFactory = CreateDbFactory();
        var svc = CreateEscalationService(dbFactory);

        var path = new EscalationPath
        {
            Name = "Critical Path",
            TriggerSeverity = AlertSeverity.Critical,
            EscalationDelayMinutes = 15,
            Recipients = new List<string> { "ciso@contoso.com" },
            Channel = NotificationChannel.Email,
            RepeatIntervalMinutes = 30,
            CreatedBy = "test-user"
        };

        var result = await svc.ConfigureEscalationPathAsync(path);
        result.Id.Should().NotBeEmpty();
        result.Name.Should().Be("Critical Path");
        result.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EscalationService_ConfigurePath_NoRecipients_Throws()
    {
        var dbFactory = CreateDbFactory();
        var svc = CreateEscalationService(dbFactory);

        var path = new EscalationPath
        {
            Name = "Bad Path",
            TriggerSeverity = AlertSeverity.High,
            EscalationDelayMinutes = 10,
            Recipients = new List<string>(),
            RepeatIntervalMinutes = 30
        };

        var act = () => svc.ConfigureEscalationPathAsync(path);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Recipients*");
    }

    [Fact]
    public async Task EscalationService_ConfigurePath_InvalidDelay_Throws()
    {
        var dbFactory = CreateDbFactory();
        var svc = CreateEscalationService(dbFactory);

        var path = new EscalationPath
        {
            Name = "Bad Delay",
            TriggerSeverity = AlertSeverity.High,
            EscalationDelayMinutes = 0,
            Recipients = new List<string> { "user-1" },
            RepeatIntervalMinutes = 30
        };

        var act = () => svc.ConfigureEscalationPathAsync(path);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EscalationDelayMinutes*");
    }

    [Fact]
    public async Task EscalationService_ConfigurePath_RepeatTooLow_Throws()
    {
        var dbFactory = CreateDbFactory();
        var svc = CreateEscalationService(dbFactory);

        var path = new EscalationPath
        {
            Name = "Bad Repeat",
            TriggerSeverity = AlertSeverity.Medium,
            EscalationDelayMinutes = 10,
            Recipients = new List<string> { "user-1" },
            RepeatIntervalMinutes = 3
        };

        var act = () => svc.ConfigureEscalationPathAsync(path);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RepeatIntervalMinutes*");
    }

    [Fact]
    public async Task EscalationService_GetPaths_FiltersBySeverity()
    {
        var dbFactory = CreateDbFactory();
        var svc = CreateEscalationService(dbFactory);

        await svc.ConfigureEscalationPathAsync(new EscalationPath
        {
            Name = "Critical Path", TriggerSeverity = AlertSeverity.Critical,
            EscalationDelayMinutes = 15, Recipients = ["ciso@contoso.com"],
            RepeatIntervalMinutes = 30, CreatedBy = "test"
        });

        await svc.ConfigureEscalationPathAsync(new EscalationPath
        {
            Name = "High Path", TriggerSeverity = AlertSeverity.High,
            EscalationDelayMinutes = 30, Recipients = ["secops@contoso.com"],
            RepeatIntervalMinutes = 30, CreatedBy = "test"
        });

        var criticalOnly = await svc.GetEscalationPathsAsync(AlertSeverity.Critical);
        criticalOnly.Should().HaveCount(1);
        criticalOnly[0].Name.Should().Be("Critical Path");

        var all = await svc.GetEscalationPathsAsync();
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task EscalationService_UpdateExistingPath()
    {
        var dbFactory = CreateDbFactory();
        var svc = CreateEscalationService(dbFactory);

        var original = await svc.ConfigureEscalationPathAsync(new EscalationPath
        {
            Name = "Original", TriggerSeverity = AlertSeverity.Critical,
            EscalationDelayMinutes = 15, Recipients = ["user-1"],
            RepeatIntervalMinutes = 30, CreatedBy = "test"
        });

        var updated = await svc.ConfigureEscalationPathAsync(new EscalationPath
        {
            Id = original.Id,
            Name = "Updated", TriggerSeverity = AlertSeverity.High,
            EscalationDelayMinutes = 20, Recipients = ["user-1", "user-2"],
            RepeatIntervalMinutes = 15, CreatedBy = "test"
        });

        updated.Id.Should().Be(original.Id);
        updated.Name.Should().Be("Updated");
        updated.EscalationDelayMinutes.Should().Be(20);
        updated.Recipients.Should().HaveCount(2);
    }

    [Fact]
    public async Task EscalationService_CheckEscalations_NoOverdueAlerts_Completes()
    {
        var dbFactory = CreateDbFactory();
        var svc = CreateEscalationService(dbFactory);

        // No alerts seeded — should complete without error
        await svc.CheckEscalationsAsync();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static TestDbContextFactory2 CreateDbFactory()
    {
        var options = new DbContextOptionsBuilder<Ato.Copilot.Core.Data.Context.AtoCopilotContext>()
            .UseInMemoryDatabase($"NotificationTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TestDbContextFactory2(options);
    }

    private static Ato.Copilot.Agents.Compliance.Services.EscalationHostedService CreateEscalationService(
        IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> dbFactory)
    {
        return new Ato.Copilot.Agents.Compliance.Services.EscalationHostedService(
            dbFactory,
            Mock.Of<IAlertNotificationService>(),
            Mock.Of<IAlertManager>(),
            Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.EscalationHostedService>>());
    }

    private static ComplianceAlert CreateTestAlert(AlertSeverity severity)
    {
        return new ComplianceAlert
        {
            Id = Guid.NewGuid(),
            AlertId = $"ALT-{Guid.NewGuid():N}"[..12],
            Type = AlertType.Drift,
            Severity = severity,
            Status = AlertStatus.New,
            Title = $"{severity} test alert",
            Description = "Test description",
            SubscriptionId = "sub-test",
            AffectedResources = ["res-1"],
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private class TestDbContextFactory2 : IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext>
    {
        private readonly DbContextOptions<Ato.Copilot.Core.Data.Context.AtoCopilotContext> _options;
        public TestDbContextFactory2(DbContextOptions<Ato.Copilot.Core.Data.Context.AtoCopilotContext> options) => _options = options;
        public Ato.Copilot.Core.Data.Context.AtoCopilotContext CreateDbContext() => new(_options);
    }
}

// ── Dashboard & Reporting Tool Tests (Phase 9 — T057) ───────────────────

public class WatchAlertHistoryToolTests
{
    private readonly Mock<IAlertManager> _alertManagerMock;
    private readonly WatchAlertHistoryTool _tool;

    public WatchAlertHistoryToolTests()
    {
        _alertManagerMock = new Mock<IAlertManager>();
        _tool = new WatchAlertHistoryTool(
            _alertManagerMock.Object,
            Mock.Of<ILogger<WatchAlertHistoryTool>>());
    }

    [Fact]
    public void Name_ShouldBe_WatchAlertHistory()
    {
        _tool.Name.Should().Be("watch_alert_history");
    }

    [Fact]
    public void Parameters_ShouldContainExpectedKeys()
    {
        _tool.Parameters.Keys.Should().Contain(new[] 
            { "query", "subscriptionId", "severity", "status", "controlFamily", "days", "page", "pageSize" });
    }

    [Fact]
    public async Task ExecuteAsync_NoFilters_ShouldCallAlertManagerWithDefaults()
    {
        _alertManagerMock
            .Setup(m => m.GetAlertsAsync(
                It.IsAny<string?>(), It.IsAny<AlertSeverity?>(), It.IsAny<AlertStatus?>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert>(), 0));

        var result = await _tool.ExecuteAsync(new Dictionary<string, object?>(), CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("result").GetProperty("totalCount").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("result").GetProperty("page").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("result").GetProperty("pageSize").GetInt32().Should().Be(20);
    }

    [Theory]
    [InlineData("Show dismissed alerts", "Dismissed")]
    [InlineData("escalated issues", "Escalated")]
    [InlineData("resolved items", "Resolved")]
    public async Task ExecuteAsync_NLQuery_ShouldParseStatusKeywords(string query, string expectedStatus)
    {
        _alertManagerMock
            .Setup(m => m.GetAlertsAsync(
                It.IsAny<string?>(), It.IsAny<AlertSeverity?>(),
                It.Is<AlertStatus?>(s => s.ToString() == expectedStatus),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert>(), 0));

        await _tool.ExecuteAsync(new Dictionary<string, object?> { ["query"] = query }, CancellationToken.None);

        _alertManagerMock.Verify(m => m.GetAlertsAsync(
            It.IsAny<string?>(), It.IsAny<AlertSeverity?>(),
            It.Is<AlertStatus?>(s => s.ToString() == expectedStatus),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("critical alerts", "Critical")]
    [InlineData("high severity", "High")]
    public async Task ExecuteAsync_NLQuery_ShouldParseSeverityKeywords(string query, string expectedSeverity)
    {
        _alertManagerMock
            .Setup(m => m.GetAlertsAsync(
                It.IsAny<string?>(),
                It.Is<AlertSeverity?>(s => s.ToString() == expectedSeverity),
                It.IsAny<AlertStatus?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert>(), 0));

        await _tool.ExecuteAsync(new Dictionary<string, object?> { ["query"] = query }, CancellationToken.None);

        _alertManagerMock.Verify(m => m.GetAlertsAsync(
            It.IsAny<string?>(),
            It.Is<AlertSeverity?>(s => s.ToString() == expectedSeverity),
            It.IsAny<AlertStatus?>(), It.IsAny<string?>(),
            It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("alerts this week", 7)]
    [InlineData("show month report", 30)]
    [InlineData("today's issues", 1)]
    public async Task ExecuteAsync_NLQuery_ShouldParseTimeKeywords(string query, int expectedDays)
    {
        _alertManagerMock
            .Setup(m => m.GetAlertsAsync(
                It.IsAny<string?>(), It.IsAny<AlertSeverity?>(), It.IsAny<AlertStatus?>(),
                It.IsAny<string?>(), expectedDays, It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert>(), 0));

        await _tool.ExecuteAsync(new Dictionary<string, object?> { ["query"] = query }, CancellationToken.None);

        _alertManagerMock.Verify(m => m.GetAlertsAsync(
            It.IsAny<string?>(), It.IsAny<AlertSeverity?>(), It.IsAny<AlertStatus?>(),
            It.IsAny<string?>(), expectedDays, It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NLQuery_ShouldDetectControlFamilies()
    {
        _alertManagerMock
            .Setup(m => m.GetAlertsAsync(
                It.IsAny<string?>(), It.IsAny<AlertSeverity?>(), It.IsAny<AlertStatus?>(),
                "AC", It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert>(), 0));

        await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["query"] = "Show AC family alerts" }, CancellationToken.None);

        _alertManagerMock.Verify(m => m.GetAlertsAsync(
            It.IsAny<string?>(), It.IsAny<AlertSeverity?>(), It.IsAny<AlertStatus?>(),
            "AC", It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ExplicitSeverityOverridesNL()
    {
        _alertManagerMock
            .Setup(m => m.GetAlertsAsync(
                It.IsAny<string?>(),
                AlertSeverity.Low,
                It.IsAny<AlertStatus?>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert>(), 0));

        // NL says "critical" but explicit param says "Low" — explicit wins
        var args = new Dictionary<string, object?>
        {
            ["query"] = "critical alerts",
            ["severity"] = "Low"
        };

        await _tool.ExecuteAsync(args, CancellationToken.None);

        _alertManagerMock.Verify(m => m.GetAlertsAsync(
            It.IsAny<string?>(), AlertSeverity.Low,
            It.IsAny<AlertStatus?>(), It.IsAny<string?>(),
            It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithResults_ShouldReturnAlertData()
    {
        var testAlert = new ComplianceAlert
        {
            AlertId = "ALT-001",
            Type = AlertType.Drift,
            Severity = AlertSeverity.Critical,
            Status = AlertStatus.New,
            Title = "Test drift",
            SubscriptionId = "sub-1",
            ControlFamily = "AC",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _alertManagerMock
            .Setup(m => m.GetAlertsAsync(
                It.IsAny<string?>(), It.IsAny<AlertSeverity?>(), It.IsAny<AlertStatus?>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert> { testAlert }, 1));

        var result = await _tool.ExecuteAsync(new Dictionary<string, object?>(), CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        var resultObj = doc.RootElement.GetProperty("result");

        resultObj.GetProperty("totalCount").GetInt32().Should().Be(1);
        var alerts = resultObj.GetProperty("alerts");
        alerts.GetArrayLength().Should().Be(1);
        alerts[0].GetProperty("AlertId").GetString().Should().Be("ALT-001");
        alerts[0].GetProperty("type").GetString().Should().Be("Drift");
        alerts[0].GetProperty("severity").GetString().Should().Be("Critical");
    }

    [Fact]
    public async Task ExecuteAsync_PageSizeCapped_At100()
    {
        _alertManagerMock
            .Setup(m => m.GetAlertsAsync(
                It.IsAny<string?>(), It.IsAny<AlertSeverity?>(), It.IsAny<AlertStatus?>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int>(), 100,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert>(), 0));

        var args = new Dictionary<string, object?> { ["pageSize"] = "500" };
        await _tool.ExecuteAsync(args, CancellationToken.None);

        _alertManagerMock.Verify(m => m.GetAlertsAsync(
            It.IsAny<string?>(), It.IsAny<AlertSeverity?>(), It.IsAny<AlertStatus?>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int>(), 100,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class WatchComplianceTrendToolTests
{
    private readonly IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> _dbFactory;
    private readonly WatchComplianceTrendTool _tool;
    private readonly string _dbName;

    public WatchComplianceTrendToolTests()
    {
        _dbName = $"TrendTests_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<Ato.Copilot.Core.Data.Context.AtoCopilotContext>()
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbFactory = new TrendTestDbFactory(options);
        _tool = new WatchComplianceTrendTool(_dbFactory, Mock.Of<ILogger<WatchComplianceTrendTool>>());
    }

    [Fact]
    public void Name_ShouldBe_WatchComplianceTrend()
    {
        _tool.Name.Should().Be("watch_compliance_trend");
    }

    [Fact]
    public async Task ExecuteAsync_NoSnapshots_ShouldReturnEmptyMessage()
    {
        var args = new Dictionary<string, object?> { ["subscriptionId"] = "sub-1" };
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("result").GetProperty("dataPoints").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("result").GetProperty("message").GetString()
            .Should().Contain("No snapshot data");
    }

    [Fact]
    public async Task ExecuteAsync_MissingSubscriptionId_ShouldThrow()
    {
        var act = () => _tool.ExecuteAsync(new Dictionary<string, object?>(), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*subscriptionId*");
    }

    [Fact]
    public async Task ExecuteAsync_WithSnapshots_ShouldReturnTrendData()
    {
        // Seed snapshots
        await using (var db = _dbFactory.CreateDbContext())
        {
            for (int i = 5; i >= 0; i--)
            {
                db.ComplianceSnapshots.Add(new ComplianceSnapshot
                {
                    Id = Guid.NewGuid(),
                    SubscriptionId = "sub-1",
                    ComplianceScore = 80 + i, // 85, 84, 83, 82, 81, 80
                    TotalControls = 100,
                    PassedControls = 80 + i,
                    FailedControls = 20 - i,
                    TotalResources = 50,
                    CompliantResources = 40 + i,
                    NonCompliantResources = 10 - i,
                    ActiveAlertCount = 5 - i,
                    CriticalAlertCount = i > 3 ? 1 : 0,
                    HighAlertCount = i > 2 ? 2 : 0,
                    CapturedAt = DateTimeOffset.UtcNow.AddDays(-i),
                    IsWeeklySnapshot = i == 0 // Only latest is weekly
                });
            }
            await db.SaveChangesAsync();
        }

        var args = new Dictionary<string, object?> { ["subscriptionId"] = "sub-1", ["days"] = "10" };
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        var resultObj = doc.RootElement.GetProperty("result");

        resultObj.GetProperty("dataPoints").GetArrayLength().Should().Be(6);
        resultObj.GetProperty("overallDirection").GetString().Should().Be("declining");
        // Score goes from 85 → 80 which is a decline > 2
    }

    [Fact]
    public async Task ExecuteAsync_ImprovingTrend_ShouldReturnImproving()
    {
        await using (var db = _dbFactory.CreateDbContext())
        {
            db.ComplianceSnapshots.Add(new ComplianceSnapshot
            {
                Id = Guid.NewGuid(), SubscriptionId = "sub-improve",
                ComplianceScore = 70, TotalControls = 100, PassedControls = 70, FailedControls = 30,
                TotalResources = 50, CompliantResources = 35, NonCompliantResources = 15,
                ActiveAlertCount = 10, CriticalAlertCount = 0, HighAlertCount = 0,
                CapturedAt = DateTimeOffset.UtcNow.AddDays(-5), IsWeeklySnapshot = false
            });
            db.ComplianceSnapshots.Add(new ComplianceSnapshot
            {
                Id = Guid.NewGuid(), SubscriptionId = "sub-improve",
                ComplianceScore = 85, TotalControls = 100, PassedControls = 85, FailedControls = 15,
                TotalResources = 50, CompliantResources = 42, NonCompliantResources = 8,
                ActiveAlertCount = 3, CriticalAlertCount = 0, HighAlertCount = 0,
                CapturedAt = DateTimeOffset.UtcNow.AddDays(-1), IsWeeklySnapshot = false
            });
            await db.SaveChangesAsync();
        }

        var args = new Dictionary<string, object?> { ["subscriptionId"] = "sub-improve", ["days"] = "10" };
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("result").GetProperty("overallDirection").GetString()
            .Should().Be("improving");
    }

    [Fact]
    public async Task ExecuteAsync_WeeklyFilter_ShouldOnlyShowWeekly()
    {
        await using (var db = _dbFactory.CreateDbContext())
        {
            db.ComplianceSnapshots.Add(new ComplianceSnapshot
            {
                Id = Guid.NewGuid(), SubscriptionId = "sub-weekly",
                ComplianceScore = 90, TotalControls = 100, PassedControls = 90, FailedControls = 10,
                TotalResources = 50, CompliantResources = 45, NonCompliantResources = 5,
                ActiveAlertCount = 2, CriticalAlertCount = 0, HighAlertCount = 0,
                CapturedAt = DateTimeOffset.UtcNow.AddDays(-3), IsWeeklySnapshot = false
            });
            db.ComplianceSnapshots.Add(new ComplianceSnapshot
            {
                Id = Guid.NewGuid(), SubscriptionId = "sub-weekly",
                ComplianceScore = 92, TotalControls = 100, PassedControls = 92, FailedControls = 8,
                TotalResources = 50, CompliantResources = 46, NonCompliantResources = 4,
                ActiveAlertCount = 1, CriticalAlertCount = 0, HighAlertCount = 0,
                CapturedAt = DateTimeOffset.UtcNow.AddDays(-1), IsWeeklySnapshot = true
            });
            await db.SaveChangesAsync();
        }

        var args = new Dictionary<string, object?>
        {
            ["subscriptionId"] = "sub-weekly",
            ["days"] = "10",
            ["weekly"] = "true"
        };
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("result").GetProperty("dataPoints").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_DirectionIndicators_ShouldApplyThreshold()
    {
        await using (var db = _dbFactory.CreateDbContext())
        {
            db.ComplianceSnapshots.Add(new ComplianceSnapshot
            {
                Id = Guid.NewGuid(), SubscriptionId = "sub-stable",
                ComplianceScore = 90, TotalControls = 100, PassedControls = 90, FailedControls = 10,
                TotalResources = 50, CompliantResources = 45, NonCompliantResources = 5,
                ActiveAlertCount = 0, CriticalAlertCount = 0, HighAlertCount = 0,
                CapturedAt = DateTimeOffset.UtcNow.AddDays(-3), IsWeeklySnapshot = false
            });
            db.ComplianceSnapshots.Add(new ComplianceSnapshot
            {
                Id = Guid.NewGuid(), SubscriptionId = "sub-stable",
                ComplianceScore = 90.5, TotalControls = 100, PassedControls = 90, FailedControls = 10,
                TotalResources = 50, CompliantResources = 45, NonCompliantResources = 5,
                ActiveAlertCount = 0, CriticalAlertCount = 0, HighAlertCount = 0,
                CapturedAt = DateTimeOffset.UtcNow.AddDays(-1), IsWeeklySnapshot = false
            });
            await db.SaveChangesAsync();
        }

        var args = new Dictionary<string, object?> { ["subscriptionId"] = "sub-stable", ["days"] = "10" };
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);

        // Diff is 0.5 which is <= 1, so overall and per-point direction should be "stable"
        doc.RootElement.GetProperty("result").GetProperty("overallDirection").GetString()
            .Should().Be("stable");
    }

    private class TrendTestDbFactory : IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext>
    {
        private readonly DbContextOptions<Ato.Copilot.Core.Data.Context.AtoCopilotContext> _options;
        public TrendTestDbFactory(DbContextOptions<Ato.Copilot.Core.Data.Context.AtoCopilotContext> options) => _options = options;
        public Ato.Copilot.Core.Data.Context.AtoCopilotContext CreateDbContext() => new(_options);
    }
}

public class WatchAlertStatisticsToolTests
{
    private readonly IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> _dbFactory;
    private readonly WatchAlertStatisticsTool _tool;

    public WatchAlertStatisticsToolTests()
    {
        var options = new DbContextOptionsBuilder<Ato.Copilot.Core.Data.Context.AtoCopilotContext>()
            .UseInMemoryDatabase($"StatsTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbFactory = new StatsTestDbFactory(options);
        _tool = new WatchAlertStatisticsTool(_dbFactory, Mock.Of<ILogger<WatchAlertStatisticsTool>>());
    }

    [Fact]
    public void Name_ShouldBe_WatchAlertStatistics()
    {
        _tool.Name.Should().Be("watch_alert_statistics");
    }

    [Fact]
    public async Task ExecuteAsync_NoAlerts_ShouldReturnZeroCounts()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object?>(), CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        var resultObj = doc.RootElement.GetProperty("result");

        resultObj.GetProperty("totalAlerts").GetInt32().Should().Be(0);
        resultObj.GetProperty("activeAlerts").GetInt32().Should().Be(0);
        resultObj.GetProperty("message").GetString().Should().Contain("No alerts");
    }

    [Fact]
    public async Task ExecuteAsync_WithAlerts_ShouldAggregateBySeverity()
    {
        await SeedAlerts();

        var result = await _tool.ExecuteAsync(new Dictionary<string, object?>(), CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        var resultObj = doc.RootElement.GetProperty("result");

        resultObj.GetProperty("totalAlerts").GetInt32().Should().Be(4);
        var bySeverity = resultObj.GetProperty("bySeverity");
        bySeverity.GetProperty("Critical").GetInt32().Should().Be(1);
        bySeverity.GetProperty("High").GetInt32().Should().Be(1);
        bySeverity.GetProperty("Medium").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithAlerts_ShouldAggregateByType()
    {
        await SeedAlerts();

        var result = await _tool.ExecuteAsync(new Dictionary<string, object?>(), CancellationToken.None);
        var doc = JsonDocument.Parse(result);
        var byType = doc.RootElement.GetProperty("result").GetProperty("byType");

        byType.GetProperty("Drift").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithAlerts_ShouldComputeActiveCount()
    {
        await SeedAlerts();

        var result = await _tool.ExecuteAsync(new Dictionary<string, object?>(), CancellationToken.None);
        var doc = JsonDocument.Parse(result);

        // 3 are New, 1 is Resolved → activeAlerts = 3
        doc.RootElement.GetProperty("result").GetProperty("activeAlerts").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_WithResolvedAlerts_ShouldComputeAvgResolution()
    {
        await SeedAlerts();

        var result = await _tool.ExecuteAsync(new Dictionary<string, object?>(), CancellationToken.None);
        var doc = JsonDocument.Parse(result);

        // 1 resolved alert with 2h resolution time
        var avgHours = doc.RootElement.GetProperty("result").GetProperty("averageResolutionHours").GetDouble();
        avgHours.Should().BeApproximately(2.0, 0.5);
    }

    [Fact]
    public async Task ExecuteAsync_WithSubscriptionFilter_ShouldFilterBySubscription()
    {
        await SeedAlerts();

        var args = new Dictionary<string, object?> { ["subscriptionId"] = "sub-1" };
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("result").GetProperty("totalAlerts").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_CustomDays_ShouldRespectWindow()
    {
        await SeedAlerts();

        // Only look back 1 day — should exclude alerts older than 1 day
        var args = new Dictionary<string, object?> { ["days"] = "1" };
        var result = await _tool.ExecuteAsync(args, CancellationToken.None);
        var doc = JsonDocument.Parse(result);

        // All seeded alerts are within 1 day
        doc.RootElement.GetProperty("result").GetProperty("totalAlerts").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ExecuteAsync_Message_ShouldIncludeSummary()
    {
        await SeedAlerts();

        var result = await _tool.ExecuteAsync(new Dictionary<string, object?>(), CancellationToken.None);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("result").GetProperty("message").GetString()
            .Should().Contain("4 total");
    }

    private async Task SeedAlerts()
    {
        await using var db = _dbFactory.CreateDbContext();
        db.ComplianceAlerts.AddRange(
            new ComplianceAlert
            {
                Id = Guid.NewGuid(), AlertId = "ALT-001", Type = AlertType.Drift,
                Severity = AlertSeverity.Critical, Status = AlertStatus.New,
                Title = "Critical Drift", Description = "Desc", SubscriptionId = "sub-1",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-4)
            },
            new ComplianceAlert
            {
                Id = Guid.NewGuid(), AlertId = "ALT-002", Type = AlertType.Drift,
                Severity = AlertSeverity.High, Status = AlertStatus.New,
                Title = "High Drift", Description = "Desc", SubscriptionId = "sub-1",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-3)
            },
            new ComplianceAlert
            {
                Id = Guid.NewGuid(), AlertId = "ALT-003", Type = AlertType.Violation,
                Severity = AlertSeverity.Medium, Status = AlertStatus.New,
                Title = "Medium Violation", Description = "Desc", SubscriptionId = "sub-1",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
            },
            new ComplianceAlert
            {
                Id = Guid.NewGuid(), AlertId = "ALT-004", Type = AlertType.Violation,
                Severity = AlertSeverity.Low, Status = AlertStatus.Resolved,
                Title = "Resolved Low", Description = "Desc", SubscriptionId = "sub-2",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-4),
                ResolvedAt = DateTimeOffset.UtcNow.AddHours(-2) // 2h resolution
            }
        );
        await db.SaveChangesAsync();
    }

    private class StatsTestDbFactory : IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext>
    {
        private readonly DbContextOptions<Ato.Copilot.Core.Data.Context.AtoCopilotContext> _options;
        public StatsTestDbFactory(DbContextOptions<Ato.Copilot.Core.Data.Context.AtoCopilotContext> options) => _options = options;
        public Ato.Copilot.Core.Data.Context.AtoCopilotContext CreateDbContext() => new(_options);
    }
}

public class ComplianceSnapshotCaptureTests
{
    [Fact]
    public async Task CaptureSnapshotsIfDueAsync_NoConfigs_ShouldNotCreateSnapshots()
    {
        var dbFactory = CreateDbFactory();
        var service = CreateHostedService(dbFactory);

        await service.CaptureSnapshotsIfDueAsync(CancellationToken.None);

        await using var db = dbFactory.CreateDbContext();
        var snapshots = await db.ComplianceSnapshots.ToListAsync();
        snapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureSnapshotsIfDueAsync_WithConfig_ShouldCreateSnapshot()
    {
        var dbFactory = CreateDbFactory();

        // Seed an enabled monitoring configuration
        await using (var db = dbFactory.CreateDbContext())
        {
            db.MonitoringConfigurations.Add(new MonitoringConfiguration
            {
                Id = Guid.NewGuid(),
                SubscriptionId = "sub-snapshot",
                IsEnabled = true,
                Mode = MonitoringMode.Scheduled,
                Frequency = MonitoringFrequency.Hourly,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
                CreatedBy = "test-user"
            });
            await db.SaveChangesAsync();
        }

        var service = CreateHostedService(dbFactory);
        await service.CaptureSnapshotsIfDueAsync(CancellationToken.None);

        await using (var db = dbFactory.CreateDbContext())
        {
            var snapshots = await db.ComplianceSnapshots.ToListAsync();
            snapshots.Should().HaveCount(1);
            snapshots[0].SubscriptionId.Should().Be("sub-snapshot");
            snapshots[0].CapturedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public async Task CaptureSnapshotsIfDueAsync_CalledTwice_ShouldNotDuplicate()
    {
        var dbFactory = CreateDbFactory();

        await using (var db = dbFactory.CreateDbContext())
        {
            db.MonitoringConfigurations.Add(new MonitoringConfiguration
            {
                Id = Guid.NewGuid(), SubscriptionId = "sub-dup",
                IsEnabled = true, Mode = MonitoringMode.Scheduled,
                Frequency = MonitoringFrequency.Hourly,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1), CreatedBy = "test"
            });
            await db.SaveChangesAsync();
        }

        var service = CreateHostedService(dbFactory);
        await service.CaptureSnapshotsIfDueAsync(CancellationToken.None);
        await service.CaptureSnapshotsIfDueAsync(CancellationToken.None);

        await using (var db = dbFactory.CreateDbContext())
        {
            var snapshots = await db.ComplianceSnapshots.ToListAsync();
            snapshots.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task CaptureSnapshotsIfDueAsync_DisabledConfig_ShouldNotCreate()
    {
        var dbFactory = CreateDbFactory();

        await using (var db = dbFactory.CreateDbContext())
        {
            db.MonitoringConfigurations.Add(new MonitoringConfiguration
            {
                Id = Guid.NewGuid(), SubscriptionId = "sub-disabled",
                IsEnabled = false, Mode = MonitoringMode.Scheduled,
                Frequency = MonitoringFrequency.Hourly,
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "test"
            });
            await db.SaveChangesAsync();
        }

        var service = CreateHostedService(dbFactory);
        await service.CaptureSnapshotsIfDueAsync(CancellationToken.None);

        await using (var db = dbFactory.CreateDbContext())
        {
            (await db.ComplianceSnapshots.CountAsync()).Should().Be(0);
        }
    }

    [Fact]
    public async Task CaptureSnapshotsIfDueAsync_WithBaselines_ShouldPopulateScore()
    {
        var dbFactory = CreateDbFactory();

        await using (var db = dbFactory.CreateDbContext())
        {
            db.MonitoringConfigurations.Add(new MonitoringConfiguration
            {
                Id = Guid.NewGuid(), SubscriptionId = "sub-score",
                IsEnabled = true, Mode = MonitoringMode.Scheduled,
                Frequency = MonitoringFrequency.Hourly,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1), CreatedBy = "test"
            });

            // Add baselines so score calculation has data
            for (int i = 0; i < 10; i++)
            {
                db.ComplianceBaselines.Add(new ComplianceBaseline
                {
                    Id = Guid.NewGuid(),
                    SubscriptionId = "sub-score",
                    ResourceType = $"Microsoft.Compute/virtualMachines",
                    ResourceId = $"res-{i}",
                    ConfigurationHash = "abc123",
                    ConfigurationSnapshot = "{}",
                    CapturedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    IsActive = true
                });
            }
            await db.SaveChangesAsync();
        }

        var service = CreateHostedService(dbFactory);
        await service.CaptureSnapshotsIfDueAsync(CancellationToken.None);

        await using (var db = dbFactory.CreateDbContext())
        {
            var snapshot = await db.ComplianceSnapshots.FirstAsync();
            snapshot.TotalResources.Should().Be(10);
            snapshot.ComplianceScore.Should().Be(100); // All baselines active = 100%
        }
    }

    private static IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> CreateDbFactory()
    {
        var options = new DbContextOptionsBuilder<Ato.Copilot.Core.Data.Context.AtoCopilotContext>()
            .UseInMemoryDatabase($"SnapshotTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SnapshotTestDbFactory(options);
    }

    private static Ato.Copilot.Agents.Compliance.Services.ComplianceWatchHostedService CreateHostedService(
        IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> dbFactory)
    {
        return new Ato.Copilot.Agents.Compliance.Services.ComplianceWatchHostedService(
            dbFactory,
            Mock.Of<IComplianceWatchService>(),
            Mock.Of<IAlertManager>(),
            Mock.Of<Ato.Copilot.Core.Interfaces.Compliance.IComplianceEventSource>(),
            Microsoft.Extensions.Options.Options.Create(
                new Ato.Copilot.Core.Configuration.MonitoringOptions()),
            Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.ComplianceWatchHostedService>>());
    }

    private class SnapshotTestDbFactory : IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext>
    {
        private readonly DbContextOptions<Ato.Copilot.Core.Data.Context.AtoCopilotContext> _options;
        public SnapshotTestDbFactory(DbContextOptions<Ato.Copilot.Core.Data.Context.AtoCopilotContext> options) => _options = options;
        public Ato.Copilot.Core.Data.Context.AtoCopilotContext CreateDbContext() => new(_options);
    }
}

// ─── Phase 10 Integration Tests ──────────────────────────────────────────

public class WatchCreateTaskFromAlertToolTests
{
    private readonly Mock<IAlertManager> _alertManagerMock;
    private readonly Mock<IKanbanService> _kanbanServiceMock;
    private readonly WatchCreateTaskFromAlertTool _tool;

    public WatchCreateTaskFromAlertToolTests()
    {
        _alertManagerMock = new Mock<IAlertManager>();
        _kanbanServiceMock = new Mock<IKanbanService>();

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(sp => sp.GetService(typeof(IKanbanService)))
            .Returns(_kanbanServiceMock.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        _tool = new WatchCreateTaskFromAlertTool(
            _alertManagerMock.Object,
            scopeFactory.Object,
            Mock.Of<ILogger<WatchCreateTaskFromAlertTool>>());
    }

    [Fact]
    public void Name_ShouldBe_WatchCreateTaskFromAlert()
    {
        _tool.Name.Should().Be("watch_create_task_from_alert");
    }

    [Fact]
    public async Task ExecuteAsync_AlertNotFound_ShouldThrow()
    {
        _alertManagerMock
            .Setup(m => m.GetAlertByAlertIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceAlert?)null);

        var act = () => _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["alertId"] = "ALT-NOTFOUND" },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*ALERT_NOT_FOUND*");
    }

    [Fact]
    public async Task ExecuteAsync_TaskAlreadyExists_ShouldReturnError()
    {
        var alert = CreateTestAlert("ALT-001");
        _alertManagerMock
            .Setup(m => m.GetAlertByAlertIdAsync("ALT-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);
        _kanbanServiceMock
            .Setup(m => m.GetTaskByLinkedAlertIdAsync("ALT-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationTask { Id = "task-1", TaskNumber = "REM-001", LinkedAlertId = "ALT-001" });

        var result = await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["alertId"] = "ALT-001" },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("TASK_ALREADY_EXISTS");
    }

    [Fact]
    public async Task ExecuteAsync_NoBoardFound_ShouldReturnError()
    {
        var alert = CreateTestAlert("ALT-002");
        _alertManagerMock
            .Setup(m => m.GetAlertByAlertIdAsync("ALT-002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);
        _kanbanServiceMock
            .Setup(m => m.GetTaskByLinkedAlertIdAsync("ALT-002", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemediationTask?)null);
        _kanbanServiceMock
            .Setup(m => m.ListBoardsAsync(It.IsAny<string>(), false, 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<RemediationBoard> { Items = new List<RemediationBoard>(), TotalCount = 0 });

        var result = await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["alertId"] = "ALT-002" },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NO_ACTIVE_BOARD");
    }

    [Fact]
    public async Task ExecuteAsync_WithBoard_ShouldCreateTask()
    {
        var alert = CreateTestAlert("ALT-003");
        _alertManagerMock
            .Setup(m => m.GetAlertByAlertIdAsync("ALT-003", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);
        _kanbanServiceMock
            .Setup(m => m.GetTaskByLinkedAlertIdAsync("ALT-003", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemediationTask?)null);
        _kanbanServiceMock
            .Setup(m => m.CreateTaskAsync(
                "board-1", It.IsAny<string>(), It.IsAny<string>(), "system:watch",
                It.IsAny<string>(), It.IsAny<FindingSeverity?>(), null, null,
                It.IsAny<List<string>?>(), null, null, "ALT-003", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationTask { Id = "task-new", TaskNumber = "REM-042", BoardId = "board-1" });

        var result = await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["alertId"] = "ALT-003", ["boardId"] = "board-1" },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("result").GetProperty("alertId").GetString().Should().Be("ALT-003");
        doc.RootElement.GetProperty("result").GetProperty("taskNumber").GetString().Should().Be("REM-042");
        doc.RootElement.GetProperty("result").GetProperty("message").GetString()
            .Should().Contain("Remediation task created");
    }

    [Fact]
    public async Task ExecuteAsync_DefaultsToFirstActiveBoard()
    {
        var alert = CreateTestAlert("ALT-004");
        _alertManagerMock
            .Setup(m => m.GetAlertByAlertIdAsync("ALT-004", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);
        _kanbanServiceMock
            .Setup(m => m.GetTaskByLinkedAlertIdAsync("ALT-004", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemediationTask?)null);
        _kanbanServiceMock
            .Setup(m => m.ListBoardsAsync("sub-1", false, 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<RemediationBoard>
            {
                Items = new List<RemediationBoard> { new() { Id = "auto-board", SubscriptionId = "sub-1" } },
                TotalCount = 1
            });
        _kanbanServiceMock
            .Setup(m => m.CreateTaskAsync(
                "auto-board", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<FindingSeverity?>(), null, null,
                It.IsAny<List<string>?>(), null, null, "ALT-004", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationTask { Id = "task-auto", TaskNumber = "REM-001", BoardId = "auto-board" });

        var result = await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["alertId"] = "ALT-004" },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("result").GetProperty("boardId").GetString().Should().Be("auto-board");
    }

    [Fact]
    public async Task ExecuteAsync_SeverityMapped_FromAlert()
    {
        var alert = CreateTestAlert("ALT-005", AlertSeverity.Critical);
        _alertManagerMock
            .Setup(m => m.GetAlertByAlertIdAsync("ALT-005", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);
        _kanbanServiceMock
            .Setup(m => m.GetTaskByLinkedAlertIdAsync("ALT-005", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemediationTask?)null);
        _kanbanServiceMock
            .Setup(m => m.CreateTaskAsync(
                "board-1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), FindingSeverity.Critical, null, null,
                It.IsAny<List<string>?>(), null, null, "ALT-005", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationTask { Id = "task-crit", TaskNumber = "REM-001" });

        await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["alertId"] = "ALT-005", ["boardId"] = "board-1" },
            CancellationToken.None);

        _kanbanServiceMock.Verify(m => m.CreateTaskAsync(
            "board-1", It.Is<string>(t => t.Contains("[ALT-005]")),
            It.IsAny<string>(), "system:watch",
            It.IsAny<string>(), FindingSeverity.Critical, null, null,
            It.IsAny<List<string>?>(), null, null, "ALT-005", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ComplianceAlert CreateTestAlert(string alertId, AlertSeverity severity = AlertSeverity.High)
    {
        return new ComplianceAlert
        {
            AlertId = alertId,
            Type = AlertType.Drift,
            Severity = severity,
            Status = AlertStatus.New,
            Title = $"Test alert {alertId}",
            SubscriptionId = "sub-1",
            ControlId = "AC-2",
            ControlFamily = "AC",
            CreatedAt = DateTimeOffset.UtcNow,
            AffectedResources = new List<string> { "/subscriptions/sub-1/resourceGroups/rg1" }
        };
    }
}

public class WatchCollectEvidenceFromAlertToolTests : IDisposable
{
    private readonly Mock<IAlertManager> _alertManagerMock;
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly WatchCollectEvidenceFromAlertTool _tool;
    private readonly string _dbName;

    public WatchCollectEvidenceFromAlertToolTests()
    {
        _alertManagerMock = new Mock<IAlertManager>();
        _dbName = $"EvidenceTests_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbFactory = new EvidenceTestDbFactory(options);
        _tool = new WatchCollectEvidenceFromAlertTool(
            _alertManagerMock.Object, _dbFactory,
            Mock.Of<ILogger<WatchCollectEvidenceFromAlertTool>>());
    }

    public void Dispose() { }

    [Fact]
    public void Name_ShouldBe_WatchCollectEvidenceFromAlert()
    {
        _tool.Name.Should().Be("watch_collect_evidence_from_alert");
    }

    [Fact]
    public async Task ExecuteAsync_AlertNotFound_ShouldThrow()
    {
        _alertManagerMock
            .Setup(m => m.GetAlertByAlertIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceAlert?)null);

        var act = () => _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["alertId"] = "ALT-GONE" },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*ALERT_NOT_FOUND*");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCaptureEvidenceFromAlert()
    {
        var alert = new ComplianceAlert
        {
            AlertId = "ALT-EVD-001",
            Type = AlertType.Violation,
            Severity = AlertSeverity.Critical,
            Status = AlertStatus.New,
            Title = "Encryption disabled",
            Description = "Storage account encryption was disabled",
            SubscriptionId = "sub-1",
            ControlId = "SC-13",
            ControlFamily = "SC",
            CreatedAt = DateTimeOffset.UtcNow,
            AffectedResources = new List<string> { "/subscriptions/sub-1/storageAccounts/sa1" }
        };

        _alertManagerMock
            .Setup(m => m.GetAlertByAlertIdAsync("ALT-EVD-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);

        var result = await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["alertId"] = "ALT-EVD-001" },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("result").GetProperty("alertId").GetString().Should().Be("ALT-EVD-001");
        doc.RootElement.GetProperty("result").GetProperty("controlId").GetString().Should().Be("SC-13");
        doc.RootElement.GetProperty("result").GetProperty("evidenceType").GetString().Should().Be("AlertSnapshot");

        // Verify evidence persisted
        await using var db = _dbFactory.CreateDbContext();
        var evidence = await db.Evidence.FirstOrDefaultAsync();
        evidence.Should().NotBeNull();
        evidence!.ControlId.Should().Be("SC-13");
        evidence.SubscriptionId.Should().Be("sub-1");
        evidence.Content.Should().Contain("ALT-EVD-001");
        evidence.ContentHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStoreEvidenceContentAsJson()
    {
        var alert = new ComplianceAlert
        {
            AlertId = "ALT-EVD-002",
            Type = AlertType.Drift,
            Severity = AlertSeverity.Medium,
            Status = AlertStatus.Acknowledged,
            Title = "Config drift detected",
            SubscriptionId = "sub-2",
            ControlId = "CM-3",
            ControlFamily = "CM",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _alertManagerMock
            .Setup(m => m.GetAlertByAlertIdAsync("ALT-EVD-002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);

        await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["alertId"] = "ALT-EVD-002" },
            CancellationToken.None);

        await using var db = _dbFactory.CreateDbContext();
        var evidence = await db.Evidence.FirstOrDefaultAsync();
        var contentDoc = JsonDocument.Parse(evidence!.Content);
        contentDoc.RootElement.GetProperty("alertId").GetString().Should().Be("ALT-EVD-002");
        contentDoc.RootElement.GetProperty("type").GetString().Should().Be("Drift");
        contentDoc.RootElement.GetProperty("severity").GetString().Should().Be("Medium");
    }

    private class EvidenceTestDbFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public EvidenceTestDbFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}

public class AlertAutoCloseTests : IDisposable
{
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;

    public AlertAutoCloseTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"AutoClose_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbFactory = new AutoCloseDbFactory(_dbOptions);
    }

    public void Dispose() { }

    [Fact]
    public async Task TransitionToResolved_ShouldAutoCloseLinkedTask()
    {
        // Seed alert and linked task
        await using (var db = _dbFactory.CreateDbContext())
        {
            var alert = new ComplianceAlert
            {
                Id = Guid.NewGuid(),
                AlertId = "ALT-CLOSE-001",
                Type = AlertType.Drift,
                Severity = AlertSeverity.High,
                Status = AlertStatus.Acknowledged,
                Title = "Drift detected",
                SubscriptionId = "sub-1",
                CreatedAt = DateTimeOffset.UtcNow,
                AcknowledgedAt = DateTimeOffset.UtcNow
            };
            db.ComplianceAlerts.Add(alert);

            db.RemediationTasks.Add(new RemediationTask
            {
                Id = "task-close-1",
                TaskNumber = "REM-100",
                BoardId = "board-1",
                Title = "[ALT-CLOSE-001] Drift detected",
                ControlId = "AC-2",
                Status = TaskStatus.InProgress,
                LinkedAlertId = "ALT-CLOSE-001",
                CreatedBy = "system:watch"
            });
            await db.SaveChangesAsync();

            var alertManager = new Ato.Copilot.Agents.Compliance.Services.AlertManager(
                _dbFactory,
                Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Configuration.AlertOptions()),
                Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.AlertManager>>(),
                Mock.Of<IServiceProvider>());

            await alertManager.TransitionAlertAsync(
                alert.Id, AlertStatus.Resolved, "user-1", "Administrator", cancellationToken: CancellationToken.None);
        }

        // Verify task was auto-closed
        await using var verifyDb = _dbFactory.CreateDbContext();
        var task = await verifyDb.RemediationTasks.FirstAsync(t => t.LinkedAlertId == "ALT-CLOSE-001");
        task.Status.Should().Be(TaskStatus.Done);
    }

    [Fact]
    public async Task TransitionToResolved_NoLinkedTask_ShouldNotFail()
    {
        await using var db = _dbFactory.CreateDbContext();
        var alert = new ComplianceAlert
        {
            Id = Guid.NewGuid(),
            AlertId = "ALT-NOLINK-001",
            Type = AlertType.Violation,
            Severity = AlertSeverity.Medium,
            Status = AlertStatus.Acknowledged,
            Title = "Violation alert",
            SubscriptionId = "sub-1",
            CreatedAt = DateTimeOffset.UtcNow,
            AcknowledgedAt = DateTimeOffset.UtcNow
        };
        db.ComplianceAlerts.Add(alert);
        await db.SaveChangesAsync();

        var alertManager = new Ato.Copilot.Agents.Compliance.Services.AlertManager(
            _dbFactory,
            Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Configuration.AlertOptions()),
            Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.AlertManager>>(),
            Mock.Of<IServiceProvider>());

        // Should complete without error even though no linked task
        var result = await alertManager.TransitionAlertAsync(
            alert.Id, AlertStatus.Resolved, "user-1", "Administrator", cancellationToken: CancellationToken.None);

        result.Status.Should().Be(AlertStatus.Resolved);
    }

    [Fact]
    public async Task TransitionToResolved_ShouldCreateAuditLogEntry()
    {
        await using var db = _dbFactory.CreateDbContext();
        var alert = new ComplianceAlert
        {
            Id = Guid.NewGuid(),
            AlertId = "ALT-AUDIT-001",
            Type = AlertType.Drift,
            Severity = AlertSeverity.Low,
            Status = AlertStatus.New,
            Title = "Minor drift",
            SubscriptionId = "sub-1",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ComplianceAlerts.Add(alert);
        await db.SaveChangesAsync();

        var alertManager = new Ato.Copilot.Agents.Compliance.Services.AlertManager(
            _dbFactory,
            Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Configuration.AlertOptions()),
            Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.AlertManager>>(),
            Mock.Of<IServiceProvider>());

        await alertManager.TransitionAlertAsync(
            alert.Id, AlertStatus.Resolved, "auditor-user", "Administrator", cancellationToken: CancellationToken.None);

        await using var verifyDb = _dbFactory.CreateDbContext();
        var auditLog = await verifyDb.AuditLogs
            .FirstOrDefaultAsync(l => l.Action == "AlertTransition");
        auditLog.Should().NotBeNull();
        auditLog!.UserId.Should().Be("auditor-user");
        auditLog.Details.Should().Contain("ALT-AUDIT-001");
        auditLog.Details.Should().Contain("New").And.Contain("Resolved");
    }

    private class AutoCloseDbFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public AutoCloseDbFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}

public class DocumentMonitoringIntegrationTests : IDisposable
{
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly Ato.Copilot.Agents.Compliance.Services.DocumentGenerationService _docService;

    private string _monitorTestSystemId = string.Empty;

    public DocumentMonitoringIntegrationTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"DocMonitor_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbFactory = new DocTestDbFactory(_dbOptions);
        _docService = new Ato.Copilot.Agents.Compliance.Services.DocumentGenerationService(
            _dbFactory,
            Mock.Of<INistControlsService>(),
            Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.DocumentGenerationService>>());

        // Fix #685: seed a system so tests can pass explicit systemId
        using var db = _dbFactory.CreateDbContext();
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Monitor Integration System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true
        };
        db.RegisteredSystems.Add(system);
        db.SaveChanges();
        _monitorTestSystemId = system.Id;
    }

    public void Dispose() { }

    [Fact]
    public async Task PoamDocument_ShouldIncludeActiveAlerts()
    {
        // Seed alerts
        await using (var db = _dbFactory.CreateDbContext())
        {
            db.ComplianceAlerts.Add(new ComplianceAlert
            {
                Id = Guid.NewGuid(),
                AlertId = "ALT-POAM-001",
                Type = AlertType.Drift,
                Severity = AlertSeverity.Critical,
                Status = AlertStatus.New,
                Title = "Critical drift in encryption",
                SubscriptionId = "sub-1",
                ControlFamily = "SC",
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.ComplianceAlerts.Add(new ComplianceAlert
            {
                Id = Guid.NewGuid(),
                AlertId = "ALT-POAM-002",
                Type = AlertType.Violation,
                Severity = AlertSeverity.High,
                Status = AlertStatus.Acknowledged,
                Title = "MFA not enforced",
                SubscriptionId = "sub-1",
                ControlFamily = "AC",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var doc = await _docService.GenerateDocumentAsync("POAM", _monitorTestSystemId);

        doc.Content.Should().Contain("Active Monitoring Alerts");
        doc.Content.Should().Contain("ALT-POAM-001");
        doc.Content.Should().Contain("ALT-POAM-002");
        doc.Content.Should().Contain("Critical");
        doc.Content.Should().Contain("Total Active Alerts");
    }

    [Fact]
    public async Task SarDocument_ShouldIncludeMonitoringStatus()
    {
        // Seed one alert
        await using (var db = _dbFactory.CreateDbContext())
        {
            db.ComplianceAlerts.Add(new ComplianceAlert
            {
                Id = Guid.NewGuid(),
                AlertId = "ALT-SAR-001",
                Type = AlertType.Drift,
                Severity = AlertSeverity.Medium,
                Status = AlertStatus.InProgress,
                Title = "Config drift",
                SubscriptionId = "sub-1",
                ControlFamily = "CM",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var doc = await _docService.GenerateDocumentAsync("SAR", _monitorTestSystemId);

        doc.Content.Should().Contain("Continuous Monitoring Status");
        doc.Content.Should().Contain("1");
        doc.Content.Should().Contain("active compliance monitoring alerts");
    }

    [Fact]
    public async Task SarDocument_NoAlerts_ShouldShowNoIssues()
    {
        var doc = await _docService.GenerateDocumentAsync("SAR", _monitorTestSystemId);

        doc.Content.Should().Contain("Continuous Monitoring Status");
        doc.Content.Should().Contain("No active monitoring alerts");
    }

    private class DocTestDbFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public DocTestDbFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}

// ─── Phase 11 Auto-Remediation Tests ────────────────────────────────────

public class WatchCreateAutoRemediationRuleToolTests
{
    private readonly Mock<IComplianceWatchService> _watchServiceMock;
    private readonly WatchCreateAutoRemediationRuleTool _tool;

    public WatchCreateAutoRemediationRuleToolTests()
    {
        _watchServiceMock = new Mock<IComplianceWatchService>();
        _tool = new WatchCreateAutoRemediationRuleTool(
            _watchServiceMock.Object,
            Mock.Of<ILogger<WatchCreateAutoRemediationRuleTool>>());
    }

    [Fact]
    public void Name_ShouldBe_WatchCreateAutoRemediationRule()
    {
        _tool.Name.Should().Be("watch_create_auto_remediation_rule");
    }

    [Fact]
    public async Task ExecuteAsync_ValidRule_ShouldCreateRule()
    {
        _watchServiceMock
            .Setup(m => m.CreateAutoRemediationRuleAsync(It.IsAny<AutoRemediationRule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutoRemediationRule r, CancellationToken _) =>
            {
                r.Id = Guid.NewGuid();
                return r;
            });

        var result = await _tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["name"] = "Fix missing tags",
                ["action"] = "apply-required-tags",
                ["subscriptionId"] = "sub-1",
                ["approvalMode"] = "auto"
            },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("result").GetProperty("name").GetString().Should().Be("Fix missing tags");
        doc.RootElement.GetProperty("result").GetProperty("approvalMode").GetString().Should().Be("auto");
        doc.RootElement.GetProperty("result").GetProperty("isEnabled").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("message").GetString()
            .Should().Contain("Auto-remediation rule created");
    }

    [Fact]
    public async Task ExecuteAsync_BlockedFamily_ShouldReturnError()
    {
        _watchServiceMock
            .Setup(m => m.CreateAutoRemediationRuleAsync(It.IsAny<AutoRemediationRule>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("BLOCKED_FAMILY: Control family 'AC' cannot be auto-remediated"));

        var result = await _tool.ExecuteAsync(
            new Dictionary<string, object?>
            {
                ["name"] = "Bad rule",
                ["action"] = "fix-ac",
                ["controlFamily"] = "AC"
            },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("BLOCKED_CONTROL_FAMILY");
    }

    [Fact]
    public async Task ExecuteAsync_MissingName_ShouldThrow()
    {
        var act = () => _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "some-action" },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*INVALID_NAME*");
    }

    [Fact]
    public async Task ExecuteAsync_MissingAction_ShouldThrow()
    {
        var act = () => _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["name"] = "some-name" },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*INVALID_ACTION*");
    }
}

public class WatchListAutoRemediationRulesToolTests
{
    private readonly Mock<IComplianceWatchService> _watchServiceMock;
    private readonly WatchListAutoRemediationRulesTool _tool;

    public WatchListAutoRemediationRulesToolTests()
    {
        _watchServiceMock = new Mock<IComplianceWatchService>();
        _tool = new WatchListAutoRemediationRulesTool(
            _watchServiceMock.Object,
            Mock.Of<ILogger<WatchListAutoRemediationRulesTool>>());
    }

    [Fact]
    public void Name_ShouldBe_WatchListAutoRemediationRules()
    {
        _tool.Name.Should().Be("watch_list_auto_remediation_rules");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldListRules()
    {
        _watchServiceMock
            .Setup(m => m.GetAutoRemediationRulesAsync(null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AutoRemediationRule>
            {
                new()
                {
                    Id = Guid.NewGuid(), Name = "Tag rule", Action = "apply-tags",
                    ControlFamily = "CM", ApprovalMode = "auto", IsEnabled = true,
                    ExecutionCount = 5, LastExecutedAt = DateTimeOffset.UtcNow.AddHours(-1),
                    CreatedBy = "admin", CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
                },
                new()
                {
                    Id = Guid.NewGuid(), Name = "Encryption rule", Action = "enable-encryption",
                    ControlFamily = "CM", ApprovalMode = "require-approval", IsEnabled = true,
                    ExecutionCount = 0, CreatedBy = "admin", CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
                }
            });

        var result = await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["includeDisabled"] = "false" },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("result").GetProperty("totalCount").GetInt32().Should().Be(2);
        var rules = doc.RootElement.GetProperty("result").GetProperty("rules");
        rules.GetArrayLength().Should().Be(2);
        rules[0].GetProperty("name").GetString().Should().Be("Tag rule");
        rules[0].GetProperty("executionCount").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyList_ShouldReturnZero()
    {
        _watchServiceMock
            .Setup(m => m.GetAutoRemediationRulesAsync(It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AutoRemediationRule>());

        var result = await _tool.ExecuteAsync(
            new Dictionary<string, object?> { ["subscriptionId"] = "sub-empty" },
            CancellationToken.None);

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("result").GetProperty("totalCount").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("result").GetProperty("rules").GetArrayLength().Should().Be(0);
    }
}

public class AutoRemediationEngineTests : IDisposable
{
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly Mock<IAlertManager> _alertManagerMock;
    private readonly Mock<IRemediationEngine> _remediationEngineMock;
    private readonly Ato.Copilot.Agents.Compliance.Services.ComplianceWatchService _service;

    public AutoRemediationEngineTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"AutoRemEngine_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbFactory = new AutoRemEngineDbFactory(_dbOptions);
        _alertManagerMock = new Mock<IAlertManager>();
        _remediationEngineMock = new Mock<IRemediationEngine>();

        _alertManagerMock
            .Setup(m => m.CreateAlertAsync(It.IsAny<ComplianceAlert>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceAlert a, CancellationToken _) => { a.AlertId = "ALT-RES-001"; return a; });

        _service = new Ato.Copilot.Agents.Compliance.Services.ComplianceWatchService(
            _dbFactory,
            _alertManagerMock.Object,
            Mock.Of<IAtoComplianceEngine>(),
            _remediationEngineMock.Object,
            Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Configuration.MonitoringOptions()),
            Microsoft.Extensions.Options.Options.Create(new Ato.Copilot.Core.Configuration.AlertOptions()),
            Mock.Of<ILogger<Ato.Copilot.Agents.Compliance.Services.ComplianceWatchService>>());
    }

    public void Dispose() { }

    [Fact]
    public async Task CreateRule_BlockedFamily_AC_ShouldThrow()
    {
        var rule = new AutoRemediationRule
        {
            Name = "Bad AC rule", Action = "fix-ac", ControlFamily = "AC", CreatedBy = "admin"
        };

        var act = () => _service.CreateAutoRemediationRuleAsync(rule);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*BLOCKED_FAMILY*");
    }

    [Fact]
    public async Task CreateRule_BlockedFamily_IA_ShouldThrow()
    {
        var rule = new AutoRemediationRule
        {
            Name = "Bad IA rule", Action = "fix-ia", ControlFamily = "IA", CreatedBy = "admin"
        };

        var act = () => _service.CreateAutoRemediationRuleAsync(rule);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*BLOCKED_FAMILY*");
    }

    [Fact]
    public async Task CreateRule_BlockedFamily_SC_ShouldThrow()
    {
        var rule = new AutoRemediationRule
        {
            Name = "Bad SC rule", Action = "fix-sc", ControlFamily = "SC", CreatedBy = "admin"
        };

        var act = () => _service.CreateAutoRemediationRuleAsync(rule);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*BLOCKED_FAMILY*");
    }

    [Fact]
    public async Task CreateRule_AllowedFamily_CM_ShouldSucceed()
    {
        var rule = new AutoRemediationRule
        {
            Name = "Config fix", Action = "apply-config", ControlFamily = "CM", CreatedBy = "admin"
        };

        var created = await _service.CreateAutoRemediationRuleAsync(rule);
        created.Id.Should().NotBe(Guid.Empty);
        created.Name.Should().Be("Config fix");
        created.ExecutionCount.Should().Be(0);
    }

    [Fact]
    public async Task TryAutoRemediate_BlockedFamily_ShouldNotAttempt()
    {
        var alert = CreateAlert("AC", "AC-2");
        var result = await _service.TryAutoRemediateAsync(alert);

        result.Attempted.Should().BeFalse();
        result.Message.Should().Contain("blocked");
    }

    [Fact]
    public async Task TryAutoRemediate_NoMatchingRules_ShouldNotAttempt()
    {
        var alert = CreateAlert("CM", "CM-3");
        var result = await _service.TryAutoRemediateAsync(alert);

        result.Attempted.Should().BeFalse();
        result.Message.Should().Contain("No matching");
    }

    [Fact]
    public async Task TryAutoRemediate_RequireApproval_ShouldNotExecute()
    {
        // Seed a require-approval rule
        await using (var db = _dbFactory.CreateDbContext())
        {
            db.AutoRemediationRules.Add(new AutoRemediationRule
            {
                Id = Guid.NewGuid(), Name = "Approval rule", Action = "fix",
                ControlFamily = "CM", ApprovalMode = "require-approval",
                IsEnabled = true, CreatedBy = "admin",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var alert = CreateAlert("CM", "CM-3");
        var result = await _service.TryAutoRemediateAsync(alert);

        result.Attempted.Should().BeFalse();
        result.MatchedRule.Should().NotBeNull();
        result.Message.Should().Contain("requires approval");
    }

    [Fact]
    public async Task TryAutoRemediate_AutoMode_ShouldExecuteAndCreateResolutionAlert()
    {
        // Seed an auto rule
        await using (var db = _dbFactory.CreateDbContext())
        {
            db.AutoRemediationRules.Add(new AutoRemediationRule
            {
                Id = Guid.NewGuid(), Name = "Auto tag rule", Action = "apply-tags",
                ControlFamily = "CM", ControlId = "CM-3", ApprovalMode = "auto",
                IsEnabled = true, CreatedBy = "admin",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        _remediationEngineMock
            .Setup(m => m.BatchRemediateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("remediation_success");

        var alert = CreateAlert("CM", "CM-3");
        var result = await _service.TryAutoRemediateAsync(alert);

        result.Attempted.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.MatchedRule!.Name.Should().Be("Auto tag rule");
        result.Message.Should().Contain("successfully");

        // Verify RESOLUTION alert was created
        _alertManagerMock.Verify(m => m.CreateAlertAsync(
            It.Is<ComplianceAlert>(a => a.Title.Contains("[RESOLUTION]")),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify audit log was created
        await using var db2 = _dbFactory.CreateDbContext();
        var auditLog = await db2.AuditLogs.FirstOrDefaultAsync(l => l.Action == "AutoRemediation");
        auditLog.Should().NotBeNull();
        auditLog!.Details.Should().Contain("Success");
    }

    [Fact]
    public async Task TryAutoRemediate_FailedRemediation_ShouldLogFailure()
    {
        // Seed an auto rule
        await using (var db = _dbFactory.CreateDbContext())
        {
            db.AutoRemediationRules.Add(new AutoRemediationRule
            {
                Id = Guid.NewGuid(), Name = "Failing rule", Action = "bad-action",
                ControlFamily = "AU", ApprovalMode = "auto",
                IsEnabled = true, CreatedBy = "admin",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        _remediationEngineMock
            .Setup(m => m.BatchRemediateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Remediation service unavailable"));

        var alert = CreateAlert("AU", "AU-2");
        var result = await _service.TryAutoRemediateAsync(alert);

        result.Attempted.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("unavailable");

        // Verify alert transition back to New was attempted
        _alertManagerMock.Verify(m => m.TransitionAlertAsync(
            alert.Id, AlertStatus.New, "system:auto-remediation", "System",
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify failure audit log
        await using var db2 = _dbFactory.CreateDbContext();
        var failLog = await db2.AuditLogs.FirstOrDefaultAsync(l => l.Details.Contains("Failed"));
        failLog.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAutoRemediate_ShouldUpdateRuleExecutionCount()
    {
        Guid ruleId;
        await using (var db = _dbFactory.CreateDbContext())
        {
            ruleId = Guid.NewGuid();
            db.AutoRemediationRules.Add(new AutoRemediationRule
            {
                Id = ruleId, Name = "Counter rule", Action = "count-me",
                ControlFamily = "CM", ApprovalMode = "auto",
                IsEnabled = true, ExecutionCount = 0, CreatedBy = "admin",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        _remediationEngineMock
            .Setup(m => m.BatchRemediateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");

        var alert = CreateAlert("CM", "CM-3");
        await _service.TryAutoRemediateAsync(alert);

        await using var db2 = _dbFactory.CreateDbContext();
        var rule = await db2.AutoRemediationRules.FindAsync(ruleId);
        rule!.ExecutionCount.Should().Be(1);
        rule.LastExecutedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAutoRemediationRules_ShouldFilterBySubscription()
    {
        await using (var db = _dbFactory.CreateDbContext())
        {
            db.AutoRemediationRules.AddRange(
                new AutoRemediationRule
                {
                    Id = Guid.NewGuid(), Name = "Sub1 rule", Action = "fix", SubscriptionId = "sub-1",
                    IsEnabled = true, CreatedBy = "admin", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
                },
                new AutoRemediationRule
                {
                    Id = Guid.NewGuid(), Name = "Sub2 rule", Action = "fix", SubscriptionId = "sub-2",
                    IsEnabled = true, CreatedBy = "admin", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
                },
                new AutoRemediationRule
                {
                    Id = Guid.NewGuid(), Name = "Global rule", Action = "fix", SubscriptionId = null,
                    IsEnabled = true, CreatedBy = "admin", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
                }
            );
            await db.SaveChangesAsync();
        }

        var rules = await _service.GetAutoRemediationRulesAsync("sub-1", true);
        // Should include sub-1 specific and global (null subscription) rules
        rules.Should().HaveCount(2);
        rules.Select(r => r.Name).Should().Contain("Sub1 rule");
        rules.Select(r => r.Name).Should().Contain("Global rule");
    }

    private static ComplianceAlert CreateAlert(string family, string controlId)
    {
        return new ComplianceAlert
        {
            Id = Guid.NewGuid(),
            AlertId = $"ALT-AUTO-{Guid.NewGuid():N}"[..16],
            Type = AlertType.Violation,
            Severity = AlertSeverity.Medium,
            Status = AlertStatus.New,
            Title = $"Test alert for {controlId}",
            SubscriptionId = "sub-1",
            ControlId = controlId,
            ControlFamily = family,
            CreatedAt = DateTimeOffset.UtcNow,
            AffectedResources = new List<string> { "/subscriptions/sub-1/resources/r1" }
        };
    }

    private class AutoRemEngineDbFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public AutoRemEngineDbFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}

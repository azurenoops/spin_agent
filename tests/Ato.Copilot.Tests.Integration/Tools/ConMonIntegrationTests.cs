using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Integration.Tools;

/// <summary>
/// Integration tests for Feature 015 Phase 11 — Continuous Monitoring &amp; Lifecycle (US9).
/// Uses real ConMonService, AuthorizationService, and RmfLifecycleService with in-memory EF Core.
/// Validates: create ConMon plan → generate report → report significant change →
/// verify reauthorization trigger → check expiration alert → multi-system dashboard.
/// </summary>
public class ConMonIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RegisterSystemTool _registerTool;
    private readonly CreateConMonPlanTool _createPlanTool;
    private readonly GenerateConMonReportTool _generateReportTool;
    private readonly ReportSignificantChangeTool _reportChangeTool;
    private readonly TrackAtoExpirationTool _trackExpirationTool;
    private readonly MultiSystemDashboardTool _dashboardTool;
    private readonly ReauthorizationWorkflowTool _reauthorizationTool;
    private readonly NotificationDeliveryTool _notificationTool;

    public ConMonIntegrationTests()
    {
        var dbName = $"ConMonIntTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddDbContextFactory<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var lifecycleSvc = new RmfLifecycleService(_scopeFactory, _serviceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>(), Mock.Of<ILogger<RmfLifecycleService>>());
        var conMonSvc = new ConMonService(_scopeFactory, Mock.Of<ILogger<ConMonService>>());
        var authorizationSvc = new AuthorizationService(_scopeFactory, Mock.Of<ILogger<AuthorizationService>>());

        _registerTool = new RegisterSystemTool(lifecycleSvc, Mock.Of<ILogger<RegisterSystemTool>>());
        _createPlanTool = new CreateConMonPlanTool(conMonSvc, Mock.Of<ILogger<CreateConMonPlanTool>>());
        _generateReportTool = new GenerateConMonReportTool(conMonSvc, Mock.Of<ILogger<GenerateConMonReportTool>>());
        _reportChangeTool = new ReportSignificantChangeTool(conMonSvc, Mock.Of<ILogger<ReportSignificantChangeTool>>());
        _trackExpirationTool = new TrackAtoExpirationTool(conMonSvc, Mock.Of<ILogger<TrackAtoExpirationTool>>());
        _dashboardTool = new MultiSystemDashboardTool(conMonSvc, Mock.Of<ILogger<MultiSystemDashboardTool>>());
        _reauthorizationTool = new ReauthorizationWorkflowTool(conMonSvc, Mock.Of<ILogger<ReauthorizationWorkflowTool>>());
        _notificationTool = new NotificationDeliveryTool(conMonSvc, Mock.Of<ILogger<NotificationDeliveryTool>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    /// <summary>
    /// End-to-end: Register system → create ConMon plan → generate report →
    /// report significant change → check reauthorization → check expiration →
    /// view dashboard.
    /// </summary>
    [Fact]
    public async Task FullConMonLifecycle_EndToEnd()
    {
        // ─── Step 1: Register a system ────────────────────────────────
        var systemId = await RegisterSystem("ConMon Integration System", "MajorApplication");

        // ─── Step 2: Create ConMon plan ───────────────────────────────
        var planResult = await _createPlanTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["assessment_frequency"] = "Monthly",
            ["annual_review_date"] = "2026-06-15"
        });

        var planDoc = JsonDocument.Parse(planResult);
        planDoc.RootElement.GetProperty("status").GetString().Should().Be("success",
            because: $"Create plan should succeed but got: {planResult}");
        var planData = planDoc.RootElement.GetProperty("data");
        planData.GetProperty("system_id").GetString().Should().Be(systemId);
        planData.GetProperty("assessment_frequency").GetString().Should().Be("Monthly");
        var planId = planData.GetProperty("plan_id").GetString();
        planId.Should().NotBeNullOrEmpty();

        // ─── Step 3: Update plan (upsert) ─────────────────────────────
        var updateResult = await _createPlanTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["assessment_frequency"] = "Quarterly",
            ["annual_review_date"] = "2026-12-01"
        });

        var updateDoc = JsonDocument.Parse(updateResult);
        updateDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        updateDoc.RootElement.GetProperty("data").GetProperty("assessment_frequency")
            .GetString().Should().Be("Quarterly");
        // Plan ID should be the same (upsert, not duplicate)
        updateDoc.RootElement.GetProperty("data").GetProperty("plan_id")
            .GetString().Should().Be(planId);

        // ─── Step 4: Generate ConMon report ───────────────────────────
        var reportResult = await _generateReportTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["report_type"] = "Monthly",
            ["period"] = "2026-02"
        });

        var reportDoc = JsonDocument.Parse(reportResult);
        reportDoc.RootElement.GetProperty("status").GetString().Should().Be("success",
            because: $"Generate report should succeed but got: {reportResult}");
        var reportData = reportDoc.RootElement.GetProperty("data");
        reportData.GetProperty("report_type").GetString().Should().Be("Monthly");
        reportData.GetProperty("period").GetString().Should().Be("2026-02");
        reportData.GetProperty("compliance_score").GetDouble().Should().BeGreaterThanOrEqualTo(0);

        // ─── Step 5: Report significant change ────────────────────────
        var changeResult = await _reportChangeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["change_type"] = "New Interconnection",
            ["description"] = "Added VPN tunnel to partner org for data sharing"
        });

        var changeDoc = JsonDocument.Parse(changeResult);
        changeDoc.RootElement.GetProperty("status").GetString().Should().Be("success",
            because: $"Report change should succeed but got: {changeResult}");
        var changeData = changeDoc.RootElement.GetProperty("data");
        changeData.GetProperty("change_type").GetString().Should().Be("New Interconnection");
        changeData.GetProperty("requires_reauthorization").GetBoolean().Should().BeTrue();

        // ─── Step 6: Check reauthorization (no auth → should detect) ──
        var reauthResult = await _reauthorizationTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var reauthDoc = JsonDocument.Parse(reauthResult);
        reauthDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var reauthData = reauthDoc.RootElement.GetProperty("data");
        reauthData.GetProperty("unreviewed_change_count").GetInt32().Should().BeGreaterThan(0);

        // ─── Step 7: Check expiration (no auth → appropriate status) ──
        var expResult = await _trackExpirationTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var expDoc = JsonDocument.Parse(expResult);
        expDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var expData = expDoc.RootElement.GetProperty("data");
        expData.GetProperty("system_id").GetString().Should().Be(systemId);

        // ─── Step 8: Multi-system dashboard ───────────────────────────
        var dashResult = await _dashboardTool.ExecuteAsync(new Dictionary<string, object?>());

        var dashDoc = JsonDocument.Parse(dashResult);
        dashDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var dashData = dashDoc.RootElement.GetProperty("data");
        dashData.GetProperty("total_systems").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    /// <summary>
    /// Expiration: System with active ATO at 85 days remaining shows Info alert.
    /// </summary>
    [Fact]
    public async Task ExpirationAlert_ActiveAto_ReturnsAlert()
    {
        var systemId = await RegisterSystem("Expiration Test System", "MajorApplication");

        // Seed an active authorization decision expiring in 85 days
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.AuthorizationDecisions.Add(new AuthorizationDecision
            {
                RegisteredSystemId = systemId,
                DecisionType = AuthorizationDecisionType.Ato,
                DecisionDate = DateTime.UtcNow.AddDays(-280),
                ExpirationDate = DateTime.UtcNow.AddDays(85),
                ResidualRiskLevel = ComplianceRiskLevel.Low,
                IssuedBy = "ao-user",
                IssuedByName = "AO User",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var result = await _trackExpirationTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("has_active_authorization").GetBoolean().Should().BeTrue();
        data.GetProperty("alert_level").GetString().Should().Be("Info");
        data.GetProperty("is_expired").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Notification: Sending expiration notification for a system with an active alert.
    /// </summary>
    [Fact]
    public async Task Notification_ExpirationAlert_Delivers()
    {
        var systemId = await RegisterSystem("Notification Test System", "MajorApplication");

        // Seed an active authorization decision expiring in 55 days (Warning)
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.AuthorizationDecisions.Add(new AuthorizationDecision
            {
                RegisteredSystemId = systemId,
                DecisionType = AuthorizationDecisionType.Ato,
                DecisionDate = DateTime.UtcNow.AddDays(-310),
                ExpirationDate = DateTime.UtcNow.AddDays(55),
                ResidualRiskLevel = ComplianceRiskLevel.Low,
                IssuedBy = "ao-user",
                IssuedByName = "AO User",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var result = await _notificationTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["notification_type"] = "expiration"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("notification_type").GetString().Should().Be("expiration");
        data.GetProperty("alert_level").GetString().Should().Be("Warning");
        data.GetProperty("delivered").GetBoolean().Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helper methods
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<string> RegisterSystem(string name, string systemType)
    {
        var result = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = name,
            ["system_type"] = systemType,
            ["mission_criticality"] = "MissionEssential",
            ["hosting_environment"] = "AzureGovernment"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success",
            because: $"Register system should succeed but got: {result}");
        return doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }
}

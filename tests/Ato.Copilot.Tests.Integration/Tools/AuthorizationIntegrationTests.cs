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
/// Integration tests for Feature 015 Phase 10 — Authorization Decisions &amp; Risk Acceptance (US8).
/// Uses real AuthorizationService + RmfLifecycleService with in-memory EF Core.
/// Validates: register system → create assessment → assess controls → issue authorization →
/// accept risk → view risk register → create POA&amp;M → list POA&amp;M → generate RAR →
/// bundle authorization package.
/// </summary>
public class AuthorizationIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RegisterSystemTool _registerTool;
    private readonly AssessControlTool _assessControlTool;
    private readonly IssueAuthorizationTool _issueAuthorizationTool;
    private readonly AcceptRiskTool _acceptRiskTool;
    private readonly ShowRiskRegisterTool _showRiskRegisterTool;
    private readonly CreatePoamTool _createPoamTool;
    private readonly ListPoamTool _listPoamTool;
    private readonly GenerateRarTool _generateRarTool;
    private readonly BundleAuthorizationPackageTool _bundlePackageTool;

    public AuthorizationIntegrationTests()
    {
        var dbName = $"AuthIntTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var lifecycleSvc = new RmfLifecycleService(_scopeFactory, Mock.Of<ILogger<RmfLifecycleService>>());
        var assessmentSvc = new AssessmentArtifactService(_scopeFactory, Mock.Of<ILogger<AssessmentArtifactService>>());
        var authorizationSvc = new AuthorizationService(_scopeFactory, Mock.Of<ILogger<AuthorizationService>>());

        _registerTool = new RegisterSystemTool(lifecycleSvc, Mock.Of<ILogger<RegisterSystemTool>>());
        _assessControlTool = new AssessControlTool(assessmentSvc, Mock.Of<ILogger<AssessControlTool>>());
        _issueAuthorizationTool = new IssueAuthorizationTool(authorizationSvc, Mock.Of<ILogger<IssueAuthorizationTool>>());
        _acceptRiskTool = new AcceptRiskTool(Mock.Of<IDeviationService>(), Mock.Of<ILogger<AcceptRiskTool>>());
        _showRiskRegisterTool = new ShowRiskRegisterTool(authorizationSvc, Mock.Of<ILogger<ShowRiskRegisterTool>>());
        _createPoamTool = new CreatePoamTool(authorizationSvc, Mock.Of<ILogger<CreatePoamTool>>());
        _listPoamTool = new ListPoamTool(authorizationSvc, Mock.Of<ILogger<ListPoamTool>>());
        _generateRarTool = new GenerateRarTool(authorizationSvc, Mock.Of<ILogger<GenerateRarTool>>());
        _bundlePackageTool = new BundleAuthorizationPackageTool(authorizationSvc, Mock.Of<ILogger<BundleAuthorizationPackageTool>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    /// <summary>
    /// End-to-end: Register system → create assessment → assess controls →
    /// issue ATO → accept risk → view risk register → create POA&amp;M →
    /// list POA&amp;M → generate RAR → bundle package.
    /// </summary>
    [Fact]
    public async Task FullAuthorizationLifecycle_EndToEnd()
    {
        // ─── Step 1: Register a system ────────────────────────────────
        var systemId = await RegisterSystem("Authorization Integration System", "MajorApplication");

        // ─── Step 2: Create assessment and assess controls ────────────
        string assessmentId;
        string findingId;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

            var assessment = new ComplianceAssessment
            {
                Framework = "NIST80053",
                Baseline = "Moderate",
                ScanType = "combined",
                InitiatedBy = "mcp-user",
                RegisteredSystemId = systemId
            };
            db.Assessments.Add(assessment);

            // Create a finding for risk acceptance
            var finding = new ComplianceFinding
            {
                ControlId = "AC-3",
                ControlFamily = "AC",
                Title = "Access Enforcement Weakness",
                Description = "Insufficient access controls detected",
                Severity = FindingSeverity.High,
                Status = FindingStatus.Open,
                ResourceId = "resource-1",
                ResourceType = "VirtualMachine",
                RemediationGuidance = "Implement RBAC",
                Source = "SCA Assessment",
                ScanSource = ScanSourceType.Policy,
                RemediationType = RemediationType.Manual,
                RiskLevel = RiskLevel.High,
                AssessmentId = assessment.Id,
                ControlTitle = "Access Enforcement",
                ControlDescription = "Enforce access decisions",
                CatSeverity = CatSeverity.CatII
            };
            db.Findings.Add(finding);
            await db.SaveChangesAsync();

            assessmentId = assessment.Id;
            findingId = finding.Id;
        }

        // Assess some controls
        var r1 = await _assessControlTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = assessmentId,
            ["control_id"] = "AC-1",
            ["determination"] = "Satisfied",
            ["method"] = "Examine"
        });
        JsonDocument.Parse(r1).RootElement.GetProperty("status").GetString().Should().Be("success");

        var r2 = await _assessControlTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = assessmentId,
            ["control_id"] = "AC-3",
            ["determination"] = "OtherThanSatisfied",
            ["cat_severity"] = "CatII",
            ["notes"] = "Weakness in access enforcement"
        });
        JsonDocument.Parse(r2).RootElement.GetProperty("status").GetString().Should().Be("success");

        // ─── Step 3: Issue ATO ────────────────────────────────────────
        var atoResult = await _issueAuthorizationTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["decision_type"] = "AtoWithConditions",
            ["expiration_date"] = DateTime.UtcNow.AddYears(3).ToString("O"),
            ["residual_risk_level"] = "Medium",
            ["terms_and_conditions"] = "Must remediate AC-3 finding within 90 days",
            ["residual_risk_justification"] = "Compensating controls mitigate risk"
        });

        var atoDoc = JsonDocument.Parse(atoResult);
        atoDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var atoData = atoDoc.RootElement.GetProperty("data");
        atoData.GetProperty("decision_type").GetString().Should().Be("AtoWithConditions");
        atoData.GetProperty("is_active").GetBoolean().Should().BeTrue();
        atoData.GetProperty("residual_risk_level").GetString().Should().Be("Medium");

        // ─── Step 4: Accept risk on finding ───────────────────────────
        var riskResult = await _acceptRiskTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["finding_id"] = findingId,
            ["control_id"] = "AC-3",
            ["cat_severity"] = "CatII",
            ["justification"] = "Compensating controls in place; AC-3(4) provides equivalent protection",
            ["compensating_control"] = "AC-3(4) Role-Based Access Control",
            ["expiration_date"] = DateTime.UtcNow.AddDays(180).ToString("O")
        });

        var riskDoc = JsonDocument.Parse(riskResult);
        riskDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var riskData = riskDoc.RootElement.GetProperty("data");
        riskData.GetProperty("control_id").GetString().Should().Be("AC-3");
        riskData.GetProperty("is_active").GetBoolean().Should().BeTrue();

        // ─── Step 5: View risk register ───────────────────────────────
        var regResult = await _showRiskRegisterTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["status_filter"] = "active"
        });

        var regDoc = JsonDocument.Parse(regResult);
        regDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var regData = regDoc.RootElement.GetProperty("data");
        regData.GetProperty("active_count").GetInt32().Should().BeGreaterOrEqualTo(1);

        // ─── Step 6: Create POA&M item ────────────────────────────────
        var poamResult = await _createPoamTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["finding_id"] = findingId,
            ["weakness"] = "Insufficient access enforcement mechanisms on server resources",
            ["control_id"] = "AC-3",
            ["cat_severity"] = "CatII",
            ["poc"] = "Jane Smith, ISSM",
            ["scheduled_completion"] = DateTime.UtcNow.AddDays(90).ToString("O"),
            ["resources_required"] = "RBAC implementation - 40 hours engineering",
            ["milestones"] = JsonSerializer.Serialize(new[]
            {
                new { description = "Design RBAC model", target_date = DateTime.UtcNow.AddDays(30).ToString("O") },
                new { description = "Implement RBAC", target_date = DateTime.UtcNow.AddDays(60).ToString("O") },
                new { description = "Validate and test", target_date = DateTime.UtcNow.AddDays(90).ToString("O") }
            })
        });

        var poamDoc = JsonDocument.Parse(poamResult);
        poamDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var poamData = poamDoc.RootElement.GetProperty("data");
        poamData.GetProperty("control_id").GetString().Should().Be("AC-3");
        poamData.GetProperty("cat_severity").GetString().Should().Be("CatII");
        poamData.GetProperty("status").GetString().Should().Be("Ongoing");

        // ─── Step 7: List POA&M items ─────────────────────────────────
        var listResult = await _listPoamTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var listDoc = JsonDocument.Parse(listResult);
        listDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        listDoc.RootElement.GetProperty("data").GetProperty("total_items").GetInt32().Should().Be(1);

        // ─── Step 8: Generate RAR ─────────────────────────────────────
        var rarResult = await _generateRarTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["assessment_id"] = assessmentId
        });

        var rarDoc = JsonDocument.Parse(rarResult);
        rarDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var rarData = rarDoc.RootElement.GetProperty("data");
        rarData.GetProperty("content").GetString().Should().Contain("Risk Assessment Report");
        rarData.GetProperty("aggregate_risk_level").GetString().Should().NotBeNullOrEmpty();

        // ─── Step 9: Bundle authorization package ─────────────────────
        var bundleResult = await _bundlePackageTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var bundleDoc = JsonDocument.Parse(bundleResult);
        bundleDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var bundleData = bundleDoc.RootElement.GetProperty("data");
        bundleData.GetProperty("document_count").GetInt32().Should().BeGreaterOrEqualTo(4);
        bundleData.GetProperty("system_id").GetString().Should().Be(systemId);
    }

    /// <summary>
    /// Issuing a second ATO supersedes the first.
    /// </summary>
    [Fact]
    public async Task IssueAuthorization_SupersedesPrevious()
    {
        var systemId = await RegisterSystem("Supersede Test System", "MajorApplication");

        // Issue first ATO
        var r1 = await _issueAuthorizationTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["decision_type"] = "ATO",
            ["expiration_date"] = DateTime.UtcNow.AddYears(1).ToString("O"),
            ["residual_risk_level"] = "Low"
        });
        var d1 = JsonDocument.Parse(r1);
        d1.RootElement.GetProperty("status").GetString().Should().Be("success");

        // Issue second ATO (should supersede first)
        var r2 = await _issueAuthorizationTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["decision_type"] = "AtoWithConditions",
            ["expiration_date"] = DateTime.UtcNow.AddYears(2).ToString("O"),
            ["residual_risk_level"] = "Medium",
            ["terms_and_conditions"] = "Updated conditions"
        });
        var d2 = JsonDocument.Parse(r2);
        d2.RootElement.GetProperty("status").GetString().Should().Be("success");
        d2.RootElement.GetProperty("data").GetProperty("is_active").GetBoolean().Should().BeTrue();

        // Verify only one active decision remains
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var activeDecisions = await db.AuthorizationDecisions
            .Where(d => d.RegisteredSystemId == systemId && d.IsActive)
            .ToListAsync();
        activeDecisions.Count.Should().Be(1);
        activeDecisions[0].DecisionType.Should().Be(AuthorizationDecisionType.AtoWithConditions);
    }

    /// <summary>
    /// Accepting risk without an active authorization fails.
    /// </summary>
    [Fact]
    public async Task AcceptRisk_NoActiveAuth_ReturnsError()
    {
        var systemId = await RegisterSystem("No Auth System", "MajorApplication");

        // Create a finding
        string findingId;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var assessment = new ComplianceAssessment
            {
                Framework = "NIST80053", Baseline = "Moderate", ScanType = "combined",
                InitiatedBy = "mcp-user", RegisteredSystemId = systemId
            };
            db.Assessments.Add(assessment);
            var finding = new ComplianceFinding
            {
                ControlId = "CM-6", ControlFamily = "CM", Title = "Config Finding",
                Description = "test", Severity = FindingSeverity.Medium,
                Status = FindingStatus.Open, ResourceId = "r1", ResourceType = "VM",
                RemediationGuidance = "fix it", Source = "test", ScanSource = ScanSourceType.Policy,
                RemediationType = RemediationType.Manual, RiskLevel = RiskLevel.Standard,
                AssessmentId = assessment.Id, ControlTitle = "Config Mgmt",
                ControlDescription = "test"
            };
            db.Findings.Add(finding);
            await db.SaveChangesAsync();
            findingId = finding.Id;
        }

        // Try to accept risk — should fail
        var result = await _acceptRiskTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["finding_id"] = findingId,
            ["control_id"] = "CM-6",
            ["cat_severity"] = "CatII",
            ["justification"] = "test",
            ["expiration_date"] = DateTime.UtcNow.AddDays(90).ToString("O")
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("message").GetString().Should().Contain("No active authorization");
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

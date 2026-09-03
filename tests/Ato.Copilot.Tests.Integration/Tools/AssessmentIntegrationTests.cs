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
/// Integration tests for Feature 015 Phase 9 — Assessment Artifacts &amp; CAT Severity (US7).
/// Uses real AssessmentArtifactService + RmfLifecycleService with in-memory EF Core.
/// Validates: register system → assess controls → take snapshot → verify evidence →
/// check completeness → compare snapshots → generate SAR.
/// </summary>
public class AssessmentIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RegisterSystemTool _registerTool;
    private readonly AssessControlTool _assessControlTool;
    private readonly TakeSnapshotTool _takeSnapshotTool;
    private readonly CompareSnapshotsTool _compareSnapshotsTool;
    private readonly VerifyEvidenceTool _verifyEvidenceTool;
    private readonly CheckEvidenceCompletenessTool _checkEvidenceCompletenessTool;
    private readonly GenerateSarTool _generateSarTool;

    public AssessmentIntegrationTests()
    {
        var dbName = $"AssessIntTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddDbContextFactory<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var lifecycleSvc = new RmfLifecycleService(_scopeFactory, _serviceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>(), Mock.Of<ILogger<RmfLifecycleService>>());
        var assessmentSvc = new AssessmentArtifactService(_scopeFactory, Mock.Of<ILogger<AssessmentArtifactService>>());

        _registerTool = new RegisterSystemTool(lifecycleSvc, Mock.Of<ILogger<RegisterSystemTool>>());
        _assessControlTool = new AssessControlTool(assessmentSvc, Mock.Of<ILogger<AssessControlTool>>());
        _takeSnapshotTool = new TakeSnapshotTool(assessmentSvc, Mock.Of<ILogger<TakeSnapshotTool>>());
        _compareSnapshotsTool = new CompareSnapshotsTool(assessmentSvc, Mock.Of<ILogger<CompareSnapshotsTool>>());
        _verifyEvidenceTool = new VerifyEvidenceTool(assessmentSvc, Mock.Of<ILogger<VerifyEvidenceTool>>());
        _checkEvidenceCompletenessTool = new CheckEvidenceCompletenessTool(assessmentSvc, Mock.Of<ILogger<CheckEvidenceCompletenessTool>>());
        _generateSarTool = new GenerateSarTool(assessmentSvc, Mock.Of<ILogger<GenerateSarTool>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    /// <summary>
    /// End-to-end: Register system → create assessment → assess 5 controls
    /// (mix Satisfied/OtherThanSatisfied with CAT) → take snapshot → verify hash →
    /// assess more → take second snapshot → compare → generate SAR.
    /// </summary>
    [Fact]
    public async Task FullAssessmentLifecycle_AssessSnapshotCompareSar()
    {
        // ─── Step 1: Register a system ────────────────────────────────
        var systemId = await RegisterSystem("SAR Integration System", "MajorApplication");

        // ─── Step 2: Create a ComplianceAssessment manually ───────────
        string assessmentId;
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
            await db.SaveChangesAsync();
            assessmentId = assessment.Id;
        }

        // ─── Step 3: Assess 5 controls ───────────────────────────────
        // 3 Satisfied, 2 OtherThanSatisfied (1 CatII, 1 CatIII)
        var assessResults = new List<JsonDocument>();

        // AC-1: Satisfied
        var r1 = await _assessControlTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = assessmentId,
            ["control_id"] = "AC-1",
            ["determination"] = "Satisfied",
            ["method"] = "Examine"
        });
        var j1 = JsonDocument.Parse(r1);
        j1.RootElement.GetProperty("status").GetString().Should().Be("success");
        j1.RootElement.GetProperty("data").GetProperty("control_id").GetString().Should().Be("AC-1");
        j1.RootElement.GetProperty("data").GetProperty("determination").GetString().Should().Be("Satisfied");

        // AC-2: Satisfied
        var r2 = await _assessControlTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = assessmentId,
            ["control_id"] = "AC-2",
            ["determination"] = "Satisfied",
            ["method"] = "Test"
        });
        JsonDocument.Parse(r2).RootElement.GetProperty("status").GetString().Should().Be("success");

        // AC-3: OtherThanSatisfied, CatII
        var r3 = await _assessControlTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = assessmentId,
            ["control_id"] = "AC-3",
            ["determination"] = "OtherThanSatisfied",
            ["method"] = "Test",
            ["notes"] = "Missing mandatory access control checks",
            ["cat_severity"] = "CatII"
        });
        var j3 = JsonDocument.Parse(r3);
        j3.RootElement.GetProperty("status").GetString().Should().Be("success");
        j3.RootElement.GetProperty("data").GetProperty("cat_severity").GetString().Should().Be("CatII");

        // AU-2: Satisfied
        var r4 = await _assessControlTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = assessmentId,
            ["control_id"] = "AU-2",
            ["determination"] = "Satisfied",
            ["method"] = "Interview"
        });
        JsonDocument.Parse(r4).RootElement.GetProperty("status").GetString().Should().Be("success");

        // AU-6: OtherThanSatisfied, CatIII
        var r5 = await _assessControlTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = assessmentId,
            ["control_id"] = "AU-6",
            ["determination"] = "OtherThanSatisfied",
            ["notes"] = "Audit review procedures not documented",
            ["cat_severity"] = "CatIII"
        });
        var j5 = JsonDocument.Parse(r5);
        j5.RootElement.GetProperty("status").GetString().Should().Be("success");

        // ─── Step 4: Take first snapshot ──────────────────────────────
        var snap1Result = await _takeSnapshotTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["assessment_id"] = assessmentId
        });
        var snap1Json = JsonDocument.Parse(snap1Result);
        snap1Json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var snap1Data = snap1Json.RootElement.GetProperty("data");
        var snap1Id = snap1Data.GetProperty("snapshot_id").GetString();
        var snap1Hash = snap1Data.GetProperty("integrity_hash").GetString();
        snap1Id.Should().NotBeNullOrEmpty();
        snap1Hash.Should().NotBeNullOrEmpty();
        snap1Hash!.Length.Should().Be(64); // SHA-256 hex
        snap1Data.GetProperty("is_immutable").GetBoolean().Should().BeTrue();

        // ─── Step 5: Remediate AC-3 → now Satisfied ──────────────────
        var r6 = await _assessControlTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = assessmentId,
            ["control_id"] = "AC-3",
            ["determination"] = "Satisfied",
            ["method"] = "Test",
            ["notes"] = "Remediation applied; mandatory AC checks verified"
        });
        JsonDocument.Parse(r6).RootElement.GetProperty("status").GetString().Should().Be("success");

        // ─── Step 6: Take second snapshot ─────────────────────────────
        var snap2Result = await _takeSnapshotTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["assessment_id"] = assessmentId
        });
        var snap2Json = JsonDocument.Parse(snap2Result);
        snap2Json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var snap2Data = snap2Json.RootElement.GetProperty("data");
        var snap2Id = snap2Data.GetProperty("snapshot_id").GetString();
        var snap2Hash = snap2Data.GetProperty("integrity_hash").GetString();
        snap2Id.Should().NotBeNullOrEmpty();
        snap2Hash.Should().NotBe(snap1Hash, "score changed so hash must differ");

        // ─── Step 7: Compare snapshots ────────────────────────────────
        var compareResult = await _compareSnapshotsTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["snapshot_id_a"] = snap1Id,
            ["snapshot_id_b"] = snap2Id
        });
        var compareJson = JsonDocument.Parse(compareResult);
        compareJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var compareData = compareJson.RootElement.GetProperty("data");
        compareData.GetProperty("score_delta").GetDouble().Should().BeGreaterThan(0,
            "remediated AC-3 should increase compliance score");

        // ─── Step 8: Generate SAR ─────────────────────────────────────
        var sarResult = await _generateSarTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["assessment_id"] = assessmentId
        });
        var sarJson = JsonDocument.Parse(sarResult);
        sarJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var sarData = sarJson.RootElement.GetProperty("data");
        sarData.GetProperty("controls_assessed").GetInt32().Should().Be(5);
        sarData.GetProperty("controls_satisfied").GetInt32().Should().Be(4); // AC-1, AC-2, AC-3 (remediated), AU-2
        sarData.GetProperty("controls_other_than_satisfied").GetInt32().Should().Be(1); // AU-6
        sarData.GetProperty("content").GetString().Should().Contain("Security Assessment Report");
        sarData.GetProperty("compliance_score").GetDouble().Should().Be(80.0); // 4/5
    }

    /// <summary>
    /// Assess control with OtherThanSatisfied but no CAT → error.
    /// </summary>
    [Fact]
    public async Task AssessControl_OtherThanSatisfied_NoCat_ReturnsError()
    {
        var systemId = await RegisterSystem("CatRequired System", "MajorApplication");
        string assessmentId;
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
            await db.SaveChangesAsync();
            assessmentId = assessment.Id;
        }

        var result = await _assessControlTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = assessmentId,
            ["control_id"] = "AC-3",
            ["determination"] = "OtherThanSatisfied"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("message").GetString().Should().Contain("cat_severity");
    }

    /// <summary>
    /// Check evidence completeness when no evidence exists.
    /// </summary>
    [Fact]
    public async Task CheckEvidenceCompleteness_NoEvidence_ReportsEmpty()
    {
        var systemId = await RegisterSystem("NoEvidence System", "MajorApplication");
        string assessmentId;
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
            await db.SaveChangesAsync();
            assessmentId = assessment.Id;
        }

        // Assess a control first so there's something to check
        await _assessControlTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = assessmentId,
            ["control_id"] = "AC-1",
            ["determination"] = "Satisfied"
        });

        var result = await _checkEvidenceCompletenessTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["assessment_id"] = assessmentId
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helper methods
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<string> RegisterSystem(string name, string type)
    {
        var result = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = name,
            ["system_type"] = type,
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "AzureGovernment",
            ["description"] = $"Integration test system: {name}"
        });
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        return doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }
}

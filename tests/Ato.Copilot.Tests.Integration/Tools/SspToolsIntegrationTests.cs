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
/// Integration tests for Feature 022 — SSP 800-18 Full Sections + OSCAL Output.
/// Covers: WriteSspSectionTool, ReviewSspSectionTool, SspCompletenessTool,
/// ExportOscalSspTool, ValidateOscalSspTool, and enhanced GenerateSspTool.
/// </summary>
public class SspToolsIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly RegisterSystemTool _registerTool;
    private readonly CategorizeSystemTool _categorizeTool;
    private readonly SelectBaselineTool _selectBaselineTool;
    private readonly WriteSspSectionTool _writeSspSectionTool;
    private readonly ReviewSspSectionTool _reviewSspSectionTool;
    private readonly SspCompletenessTool _sspCompletenessTool;
    private readonly ExportOscalSspTool _exportOscalSspTool;
    private readonly ValidateOscalSspTool _validateOscalSspTool;
    private readonly GenerateSspTool _generateSspTool;
    private readonly WriteNarrativeTool _writeNarrativeTool;

    public SspToolsIntegrationTests()
    {
        var dbName = $"SspToolsIntTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var lifecycleSvc = new RmfLifecycleService(scopeFactory, Mock.Of<ILogger<RmfLifecycleService>>());
        var categorizationSvc = new CategorizationService(scopeFactory, Mock.Of<ILogger<CategorizationService>>(), Mock.Of<IPrivacyService>());
        var referenceDataSvc = new ReferenceDataService(Mock.Of<ILogger<ReferenceDataService>>());
        var baselineSvc = new BaselineService(scopeFactory, referenceDataSvc, Mock.Of<ILogger<BaselineService>>(), Mock.Of<IOrgInheritanceService>());
        var sspSvc = new SspService(scopeFactory, Mock.Of<ILogger<SspService>>());
        var oscalExportSvc = new OscalSspExportService(scopeFactory, Mock.Of<ILogger<OscalSspExportService>>());
        var oscalValidationSvc = new OscalValidationService();

        _registerTool = new RegisterSystemTool(lifecycleSvc, Mock.Of<ILogger<RegisterSystemTool>>());
        _categorizeTool = new CategorizeSystemTool(categorizationSvc, Mock.Of<ILogger<CategorizeSystemTool>>());
        _selectBaselineTool = new SelectBaselineTool(baselineSvc, Mock.Of<ILogger<SelectBaselineTool>>());
        _writeSspSectionTool = new WriteSspSectionTool(sspSvc, Mock.Of<ILogger<WriteSspSectionTool>>());
        _reviewSspSectionTool = new ReviewSspSectionTool(sspSvc, Mock.Of<ILogger<ReviewSspSectionTool>>());
        _sspCompletenessTool = new SspCompletenessTool(sspSvc, Mock.Of<INarrativeGovernanceService>(), Mock.Of<ILogger<SspCompletenessTool>>());
        _exportOscalSspTool = new ExportOscalSspTool(oscalExportSvc, Mock.Of<ILogger<ExportOscalSspTool>>());
        _validateOscalSspTool = new ValidateOscalSspTool(oscalExportSvc, oscalValidationSvc, Mock.Of<ILogger<ValidateOscalSspTool>>());
        _generateSspTool = new GenerateSspTool(sspSvc, Mock.Of<ILogger<GenerateSspTool>>());
        _writeNarrativeTool = new WriteNarrativeTool(sspSvc, Mock.Of<ILogger<WriteNarrativeTool>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    // ─────────────────────────────────────────────────────────────────────────
    // End-to-end lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full lifecycle: register → categorize → baseline → write authored sections →
    /// review/approve → check completeness → generate 13-section SSP →
    /// export OSCAL JSON → validate OSCAL.
    /// </summary>
    [Fact]
    public async Task FullSspLifecycle_EndToEnd()
    {
        // ─── Setup: register + categorize + baseline ─────────────────
        var systemId = await RegisterSystem("Full SSP System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");
        await SelectBaseline(systemId);

        // ─── Write an authored section (§5 — General Description) ───
        var writeResult = await _writeSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 5,
            ["content"] = "The Full SSP System is a web application that manages compliance workflows.",
            ["authored_by"] = "test-issm@example.com"
        });

        var writeJson = JsonDocument.Parse(writeResult);
        writeJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        writeJson.RootElement.GetProperty("data").GetProperty("section_number").GetInt32().Should().Be(5);
        writeJson.RootElement.GetProperty("data").GetProperty("version").GetInt32().Should().Be(1);
        writeJson.RootElement.GetProperty("data").GetProperty("status").GetString().Should().Be("Draft");

        // ─── Submit §5 for review ────────────────────────────────────
        var submitResult = await _writeSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 5,
            ["content"] = "The Full SSP System is a web application that manages compliance workflows.",
            ["authored_by"] = "test-issm@example.com",
            ["submit_for_review"] = true
        });
        var submitJson = JsonDocument.Parse(submitResult);
        submitJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        submitJson.RootElement.GetProperty("data").GetProperty("status").GetString().Should().Be("UnderReview");

        // ─── Approve §5 ─────────────────────────────────────────────
        var reviewResult = await _reviewSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 5,
            ["decision"] = "approve",
            ["reviewer"] = "test-ao@example.com"
        });
        var reviewJson = JsonDocument.Parse(reviewResult);
        reviewJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        reviewJson.RootElement.GetProperty("data").GetProperty("status").GetString().Should().Be("Approved");

        // ─── Check completeness ──────────────────────────────────────
        var completenessResult = await _sspCompletenessTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });
        var complJson = JsonDocument.Parse(completenessResult);
        complJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var complData = complJson.RootElement.GetProperty("data");
        complData.GetProperty("total_sections").GetInt32().Should().Be(13);
        complData.GetProperty("approved_count").GetInt32().Should().BeGreaterOrEqualTo(1);

        // ─── Generate full 13-section SSP ────────────────────────────
        var sspResult = await _generateSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });
        var sspJson = JsonDocument.Parse(sspResult);
        sspJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var sspContent = sspJson.RootElement.GetProperty("data").GetProperty("content").GetString()!;
        sspContent.Should().Contain("System Identification");
        sspContent.Should().Contain("Security Categorization");
        sspContent.Should().Contain("General Description");
        sspContent.Should().Contain("Full SSP System");

        // ─── Export OSCAL SSP JSON ───────────────────────────────────
        var exportResult = await _exportOscalSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });
        var exportJson = JsonDocument.Parse(exportResult);
        exportJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var oscalJson = exportJson.RootElement.GetProperty("data")
            .GetProperty("oscal_ssp_json").GetString()!;
        oscalJson.Should().Contain("system-security-plan");
        oscalJson.Should().Contain("1.1.2");

        // ─── Validate OSCAL SSP ─────────────────────────────────────
        var validateResult = await _validateOscalSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });
        var valJson = JsonDocument.Parse(validateResult);
        valJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        valJson.RootElement.GetProperty("data").GetProperty("is_valid").GetBoolean().Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Section management
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Write → review with request_revision → update → re-submit → approve.
    /// </summary>
    [Fact]
    public async Task WriteSspSection_ReviewCycle_RequestRevisionThenApprove()
    {
        var systemId = await RegisterSystem("Review Cycle System", "MajorApplication");

        // Write §8 (authored: Laws/Regulations)
        var write1 = await _writeSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 8,
            ["content"] = "FISMA, HIPAA, and FedRAMP requirements apply.",
            ["authored_by"] = "issm@test.mil",
            ["submit_for_review"] = true
        });
        JsonDocument.Parse(write1).RootElement.GetProperty("data")
            .GetProperty("status").GetString().Should().Be("UnderReview");

        // Request revision
        var revise = await _reviewSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 8,
            ["decision"] = "request_revision",
            ["reviewer"] = "ao@test.mil",
            ["comments"] = "Please add DoD-specific regulations."
        });
        var reviseJson = JsonDocument.Parse(revise);
        reviseJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        reviseJson.RootElement.GetProperty("data").GetProperty("status").GetString().Should().Be("Draft");
        reviseJson.RootElement.GetProperty("data").GetProperty("reviewer_comments").GetString()
            .Should().Contain("DoD-specific");

        // Update and resubmit
        var write2 = await _writeSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 8,
            ["content"] = "FISMA, HIPAA, FedRAMP, and DoD-specific: DoDI 8510.01 (RMF for DOD IT).",
            ["authored_by"] = "issm@test.mil",
            ["submit_for_review"] = true
        });
        JsonDocument.Parse(write2).RootElement.GetProperty("data")
            .GetProperty("status").GetString().Should().Be("UnderReview");

        // Approve
        var approve = await _reviewSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 8,
            ["decision"] = "approve",
            ["reviewer"] = "ao@test.mil"
        });
        JsonDocument.Parse(approve).RootElement.GetProperty("data")
            .GetProperty("status").GetString().Should().Be("Approved");
    }

    /// <summary>
    /// Optimistic concurrency: writing with wrong expected_version fails.
    /// </summary>
    [Fact]
    public async Task WriteSspSection_ConcurrencyConflict_ReturnsError()
    {
        var systemId = await RegisterSystem("Concurrency System", "MajorApplication");

        // Write §12 (authored: Personnel Security)
        await _writeSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 12,
            ["content"] = "Personnel security procedures v1.",
            ["authored_by"] = "issm@test.mil"
        });

        // Attempt to write with wrong expected_version
        var conflictResult = await _writeSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 12,
            ["content"] = "Personnel security procedures v2.",
            ["authored_by"] = "issm@test.mil",
            ["expected_version"] = 99
        });

        var json = JsonDocument.Parse(conflictResult);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("CONCURRENCY_CONFLICT");
    }

    /// <summary>
    /// Review on section that is not in UnderReview status fails.
    /// </summary>
    [Fact]
    public async Task ReviewSspSection_WrongStatus_ReturnsError()
    {
        var systemId = await RegisterSystem("Wrong Status System", "MajorApplication");

        // Write §12 but do NOT submit for review
        await _writeSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 12,
            ["content"] = "Personnel security procedures draft.",
            ["authored_by"] = "issm@test.mil"
        });

        // Try to review while still Draft
        var reviewResult = await _reviewSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 12,
            ["decision"] = "approve",
            ["reviewer"] = "ao@test.mil"
        });

        var json = JsonDocument.Parse(reviewResult);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_STATUS_FOR_REVIEW");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SSP generation with new section keys
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Backward-compatible old keys (system_information, baseline, controls) still work.
    /// </summary>
    [Fact]
    public async Task GenerateSsp_BackwardCompatibleKeys_ResolveCorrectly()
    {
        var systemId = await RegisterSystem("Backward Compat System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");
        await SelectBaseline(systemId);

        var result = await _generateSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["sections"] = "system_information,baseline,controls"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var content = json.RootElement.GetProperty("data").GetProperty("content").GetString()!;
        // system_information → §1 System Identification
        content.Should().Contain("System Identification");
        // baseline → §9 Minimum Security Controls
        content.Should().Contain("Minimum Security Controls");
        // controls → §10 Control Implementations
        content.Should().Contain("Control Implementations");
    }

    /// <summary>
    /// New section keys work for selective generation.
    /// </summary>
    [Fact]
    public async Task GenerateSsp_NewSectionKeys_GeneratesRequestedSections()
    {
        var systemId = await RegisterSystem("New Keys System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");

        var result = await _generateSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["sections"] = "categorization,system_type,authorization_boundary"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var content = json.RootElement.GetProperty("data").GetProperty("content").GetString()!;
        content.Should().Contain("Security Categorization");
        content.Should().Contain("Information System Type");
        content.Should().Contain("Authorization Boundary");
        // Should NOT contain other sections
        content.Should().NotContain("System Interconnections");
        content.Should().NotContain("Contingency Plan");
    }

    /// <summary>
    /// Generated SSP includes YAML front-matter.
    /// </summary>
    [Fact]
    public async Task GenerateSsp_FullGeneration_IncludesYamlFrontMatter()
    {
        var systemId = await RegisterSystem("YAML FM System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");

        var result = await _generateSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var json = JsonDocument.Parse(result);
        var content = json.RootElement.GetProperty("data").GetProperty("content").GetString()!;
        content.Should().StartWith("---");
        content.Should().Contain("document_version:");
        content.Should().Contain("generated_at:");
        content.Should().Contain("system_name:");
        content.Should().Contain("YAML FM System");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OSCAL Export + Validation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Export OSCAL with pretty_print=false produces compact JSON.
    /// </summary>
    [Fact]
    public async Task ExportOscalSsp_CompactJson_NoPrettyPrint()
    {
        var systemId = await RegisterSystem("Compact OSCAL System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");

        var result = await _exportOscalSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["pretty_print"] = false
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var oscalJson = json.RootElement.GetProperty("data")
            .GetProperty("oscal_ssp_json").GetString()!;
        // Compact JSON should not contain leading whitespace on lines
        oscalJson.Should().NotContain("\n  ");
    }

    /// <summary>
    /// Export OSCAL without back matter excludes resources.
    /// </summary>
    [Fact]
    public async Task ExportOscalSsp_NoBackMatter_OmitsResources()
    {
        var systemId = await RegisterSystem("No BackMatter System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");

        var result = await _exportOscalSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["include_back_matter"] = false
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var stats = json.RootElement.GetProperty("data").GetProperty("statistics");
        stats.GetProperty("back_matter_resource_count").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// Validate tool returns errors/warnings list.
    /// </summary>
    [Fact]
    public async Task ValidateOscalSsp_WithData_ReturnsStatistics()
    {
        var systemId = await RegisterSystem("Validate Stats System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");
        await SelectBaseline(systemId);

        var result = await _validateOscalSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("is_valid").GetBoolean().Should().BeTrue();
        var stats = json.RootElement.GetProperty("data").GetProperty("statistics");
        stats.GetProperty("control_count").GetInt32().Should().BeGreaterOrEqualTo(0);
        stats.GetProperty("component_count").GetInt32().Should().BeGreaterOrEqualTo(0);
    }

    /// <summary>
    /// Export tool with non-existent system returns SYSTEM_NOT_FOUND.
    /// </summary>
    [Fact]
    public async Task ExportOscalSsp_NonExistentSystem_ReturnsError()
    {
        var result = await _exportOscalSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "non-existent-system-id"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("SYSTEM_NOT_FOUND");
    }

    /// <summary>
    /// Validate tool with non-existent system returns SYSTEM_NOT_FOUND.
    /// </summary>
    [Fact]
    public async Task ValidateOscalSsp_NonExistentSystem_ReturnsError()
    {
        var result = await _validateOscalSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "non-existent-system-id"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("SYSTEM_NOT_FOUND");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Completeness reporting
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Completeness with no sections written shows zero approved.
    /// </summary>
    [Fact]
    public async Task SspCompleteness_NoSectionsWritten_ZeroApproved()
    {
        var systemId = await RegisterSystem("Empty Sections System", "MajorApplication");

        var result = await _sspCompletenessTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("total_sections").GetInt32().Should().Be(13);
        data.GetProperty("approved_count").GetInt32().Should().Be(0);
        data.GetProperty("overall_readiness_percent").GetDouble().Should().BeLessThan(100.0);
    }

    /// <summary>
    /// Completeness with non-existent system returns error.
    /// </summary>
    [Fact]
    public async Task SspCompleteness_NonExistentSystem_ReturnsError()
    {
        var result = await _sspCompletenessTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "non-existent-system-id"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("SYSTEM_NOT_FOUND");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T049: End-to-end smoke test matching AT-01 through AT-30 scenarios
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// T049 smoke test: write §6 → update §6 → review §6 (approve) → write §11 →
    /// completeness → generate full 13-section SSP → export OSCAL → validate.
    /// Covers AT-01, AT-02, AT-03, AT-06, AT-07, AT-11, AT-18, AT-22, AT-26.
    /// </summary>
    [Fact]
    public async Task SmokeTest_FullWorkflow_AT01Through30()
    {
        var systemId = await RegisterSystem("Smoke Test System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");
        await SelectBaseline(systemId);

        // ─── AT-01: ISSO authors §6 (System Environment) ────────────
        var write1 = await _writeSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 6,
            ["content"] = "The system is hosted in Azure Government (IL5) using AKS and Azure SQL.",
            ["authored_by"] = "isso@test.mil"
        });
        var w1Json = JsonDocument.Parse(write1);
        w1Json.RootElement.GetProperty("status").GetString().Should().Be("success");
        w1Json.RootElement.GetProperty("data").GetProperty("status").GetString().Should().Be("Draft");
        w1Json.RootElement.GetProperty("data").GetProperty("section_number").GetInt32().Should().Be(6);
        w1Json.RootElement.GetProperty("data").GetProperty("version").GetInt32().Should().Be(1);

        // ─── AT-02: ISSO updates §6 ─────────────────────────────────
        var write2 = await _writeSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 6,
            ["content"] = "The system is hosted in Azure Government (IL5) using AKS, Azure SQL, and Azure Key Vault.",
            ["authored_by"] = "isso@test.mil",
            ["submit_for_review"] = true
        });
        var w2Json = JsonDocument.Parse(write2);
        w2Json.RootElement.GetProperty("data").GetProperty("version").GetInt32().Should().Be(2);
        w2Json.RootElement.GetProperty("data").GetProperty("status").GetString().Should().Be("UnderReview");

        // ─── AT-03: ISSM approves §6 ────────────────────────────────
        var approve = await _reviewSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 6,
            ["decision"] = "approve",
            ["reviewer"] = "issm@test.mil"
        });
        var appJson = JsonDocument.Parse(approve);
        appJson.RootElement.GetProperty("data").GetProperty("status").GetString().Should().Be("Approved");

        // ─── Write §11 (auto-gen section with manual override) ──────
        var write11 = await _writeSspSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["section_number"] = 11,
            ["content"] = "Custom boundary description for the system.",
            ["authored_by"] = "isso@test.mil"
        });
        JsonDocument.Parse(write11).RootElement.GetProperty("status").GetString().Should().Be("success");

        // ─── Completeness check ──────────────────────────────────────
        var completeness = await _sspCompletenessTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });
        var compJson = JsonDocument.Parse(completeness);
        compJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        compJson.RootElement.GetProperty("data").GetProperty("total_sections").GetInt32().Should().Be(13);
        compJson.RootElement.GetProperty("data").GetProperty("approved_count").GetInt32()
            .Should().BeGreaterOrEqualTo(1); // At least §6 is approved

        // ─── AT-06: Generate full 13-section SSP ─────────────────────
        var sspResult = await _generateSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });
        var sspJson = JsonDocument.Parse(sspResult);
        sspJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var sspContent = sspJson.RootElement.GetProperty("data").GetProperty("content").GetString()!;

        // All 13 sections present
        sspContent.Should().Contain("1. System Identification");
        sspContent.Should().Contain("2. Security Categorization");
        sspContent.Should().Contain("3. System Owner");
        sspContent.Should().Contain("4. Information System Type");
        sspContent.Should().Contain("5. General Description");
        sspContent.Should().Contain("6. System Environment");
        sspContent.Should().Contain("7. System Interconnections");
        sspContent.Should().Contain("8. Related Laws");
        sspContent.Should().Contain("9. Minimum Security Controls");
        sspContent.Should().Contain("10. Control Implementations");
        sspContent.Should().Contain("11. Authorization Boundary");
        sspContent.Should().Contain("12. Personnel Security");
        sspContent.Should().Contain("13. Contingency Plan");

        // AT-07: Missing authored sections show placeholder
        sspContent.Should().Contain("[NOT STARTED]"); // §8, §12, §13 not authored

        // YAML front-matter present
        sspContent.Should().StartWith("---");
        sspContent.Should().Contain("system_name:"); // YAML front-matter
        sspContent.Should().Contain("Smoke Test System");

        // ─── AT-11 + AT-26: Export OSCAL SSP JSON ────────────────────
        var exportResult = await _exportOscalSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });
        var expJson = JsonDocument.Parse(exportResult);
        expJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var oscalJson = expJson.RootElement.GetProperty("data")
            .GetProperty("oscal_ssp_json").GetString()!;
        oscalJson.Should().Contain("system-security-plan");
        oscalJson.Should().Contain("metadata");
        oscalJson.Should().Contain("import-profile");
        oscalJson.Should().Contain("system-characteristics");
        oscalJson.Should().Contain("system-implementation");
        oscalJson.Should().Contain("control-implementation");
        // AT-26: OSCAL version 1.1.2
        oscalJson.Should().Contain("1.1.2");

        // ─── AT-18: Validate OSCAL → passes ─────────────────────────
        var valResult = await _validateOscalSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });
        var valJson = JsonDocument.Parse(valResult);
        valJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        valJson.RootElement.GetProperty("data").GetProperty("is_valid").GetBoolean().Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string> RegisterSystem(string name, string systemType)
    {
        var result = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = name,
            ["system_type"] = systemType,
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "AzureGovernment",
            ["description"] = $"Test system: {name}"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        return json.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task CategorizeSystem(string systemId, string conf, string integ, string avail)
    {
        var result = await _categorizeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["information_types"] = new List<InformationTypeInput>
            {
                new()
                {
                    Sp80060Id = "C.3.5.8", Name = "Information Security",
                    Category = "Management and Support",
                    ConfidentialityImpact = conf, IntegrityImpact = integ, AvailabilityImpact = avail
                }
            },
            ["justification"] = "Integration test categorization"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
    }

    private async Task SelectBaseline(string systemId)
    {
        var result = await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = true
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
    }
}

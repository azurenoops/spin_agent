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
/// Integration tests for Feature 015 Phase 7 — SSP Authoring &amp; Narrative Management.
/// Uses real SspService + BaselineService + CategorizationService + RmfLifecycleService with in-memory EF Core.
/// Validates: register → categorize → select baseline → set inheritance → write narrative →
/// suggest narrative → batch populate inherited → check progress → generate SSP.
/// </summary>
public class SspAuthoringIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly RegisterSystemTool _registerTool;
    private readonly CategorizeSystemTool _categorizeTool;
    private readonly SelectBaselineTool _selectBaselineTool;
    private readonly SetInheritanceTool _setInheritanceTool;
    private readonly WriteNarrativeTool _writeNarrativeTool;
    private readonly SuggestNarrativeTool _suggestNarrativeTool;
    private readonly BatchPopulateNarrativesTool _batchPopulateNarrativesTool;
    private readonly NarrativeProgressTool _narrativeProgressTool;
    private readonly GenerateSspTool _generateSspTool;

    public SspAuthoringIntegrationTests()
    {
        var dbName = $"SspIntTest_{Guid.NewGuid()}";
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

        _registerTool = new RegisterSystemTool(lifecycleSvc, Mock.Of<ILogger<RegisterSystemTool>>());
        _categorizeTool = new CategorizeSystemTool(categorizationSvc, Mock.Of<ILogger<CategorizeSystemTool>>());
        _selectBaselineTool = new SelectBaselineTool(baselineSvc, Mock.Of<ILogger<SelectBaselineTool>>());
        _setInheritanceTool = new SetInheritanceTool(baselineSvc, Mock.Of<ILogger<SetInheritanceTool>>());
        _writeNarrativeTool = new WriteNarrativeTool(sspSvc, Mock.Of<ILogger<WriteNarrativeTool>>());
        _suggestNarrativeTool = new SuggestNarrativeTool(sspSvc, Mock.Of<ILogger<SuggestNarrativeTool>>());
        _batchPopulateNarrativesTool = new BatchPopulateNarrativesTool(sspSvc, Mock.Of<ILogger<BatchPopulateNarrativesTool>>());
        _narrativeProgressTool = new NarrativeProgressTool(sspSvc, Mock.Of<INarrativeGovernanceService>(), Mock.Of<ILogger<NarrativeProgressTool>>());
        _generateSspTool = new GenerateSspTool(sspSvc, Mock.Of<ILogger<GenerateSspTool>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    /// <summary>
    /// End-to-end: Register → Categorize → Select Baseline → Set Inheritance →
    /// Write narrative for AC-1 → Suggest narrative for AC-2 →
    /// Batch-populate inherited controls → Check progress → Generate SSP Markdown.
    /// </summary>
    [Fact]
    public async Task FullSspLifecycle_WriteNarrativeSuggestBatchPopulateProgressGenerateSsp()
    {
        // ─── Step 1: Register a system ────────────────────────────────
        var systemId = await RegisterSystem("SSP Integration System", "MajorApplication");

        // ─── Step 2: Categorize as Moderate ────────────────────────────
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");

        // ─── Step 3: Select baseline ──────────────────────────────────
        var selectResult = await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = true
        });
        var selectJson = JsonDocument.Parse(selectResult);
        selectJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var totalControls = selectJson.RootElement.GetProperty("data").GetProperty("total_controls").GetInt32();
        totalControls.Should().BeGreaterOrEqualTo(200);

        // ─── Step 4: Set some inheritance ─────────────────────────────
        var controlIds = selectJson.RootElement.GetProperty("data").GetProperty("control_ids");
        var firstTwoControls = new List<string>();
        for (int i = 0; i < Math.Min(2, controlIds.GetArrayLength()); i++)
            firstTwoControls.Add(controlIds[i].GetString()!);

        // Set 2 controls as Inherited using inheritance_mappings
        var inheritanceMappings = firstTwoControls.Select(c => new InheritanceInput
        {
            ControlId = c,
            InheritanceType = "Inherited",
            Provider = "Azure Cloud (FedRAMP High)"
        }).ToList();

        var setResult = await _setInheritanceTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["inheritance_mappings"] = inheritanceMappings
        });
        var setJson = JsonDocument.Parse(setResult);
        setJson.RootElement.GetProperty("status").GetString().Should().Be("success");

        // ─── Step 5: Write narrative for a control ────────────────────
        var writeControlId = firstTwoControls.Count > 0 ? firstTwoControls[0] : "AC-1";
        var writeResult = await _writeNarrativeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["control_id"] = writeControlId,
            ["narrative"] = "Access control policy is maintained in SharePoint and reviewed annually by the Security Lead.",
            ["status"] = "Implemented"
        });

        var writeJson = JsonDocument.Parse(writeResult);
        writeJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        writeJson.RootElement.GetProperty("data").GetProperty("control_id").GetString().Should().Be(writeControlId);
        writeJson.RootElement.GetProperty("data").GetProperty("implementation_status").GetString().Should().Be("Implemented");
        writeJson.RootElement.GetProperty("data").GetProperty("is_auto_populated").GetBoolean().Should().BeFalse();

        // ─── Step 6: Suggest narrative for the second control ─────────
        var suggestControlId = firstTwoControls.Count > 1 ? firstTwoControls[1] : "AC-2";
        var suggestResult = await _suggestNarrativeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["control_id"] = suggestControlId
        });

        var suggestJson = JsonDocument.Parse(suggestResult);
        suggestJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        suggestJson.RootElement.GetProperty("data").GetProperty("confidence").GetDouble().Should().BeGreaterThan(0.0);
        suggestJson.RootElement.GetProperty("data").GetProperty("suggested_narrative").GetString().Should().NotBeNullOrEmpty();

        // ─── Step 7: Batch populate inherited controls ────────────────
        var batchResult = await _batchPopulateNarrativesTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["inheritance_type"] = "Inherited"
        });

        var batchJson = JsonDocument.Parse(batchResult);
        batchJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var populatedCount = batchJson.RootElement.GetProperty("data").GetProperty("populated_count").GetInt32();
        var skippedCount = batchJson.RootElement.GetProperty("data").GetProperty("skipped_count").GetInt32();
        // We wrote a narrative for one inherited control already, so it should be skipped
        (populatedCount + skippedCount).Should().Be(2); // We set 2 controls as Inherited
        skippedCount.Should().BeGreaterOrEqualTo(1); // The one we wrote manually should be skipped

        // ─── Step 8: Check progress ──────────────────────────────────
        var progressResult = await _narrativeProgressTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var progressJson = JsonDocument.Parse(progressResult);
        progressJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var progressData = progressJson.RootElement.GetProperty("data");
        progressData.GetProperty("total_controls").GetInt32().Should().Be(totalControls);
        progressData.GetProperty("completed_narratives").GetInt32().Should().BeGreaterOrEqualTo(1);
        progressData.GetProperty("overall_percentage").GetDouble().Should().BeGreaterThan(0.0);
        progressData.GetProperty("family_breakdowns").GetArrayLength().Should().BeGreaterOrEqualTo(1);

        // ─── Step 9: Generate SSP ────────────────────────────────────
        var sspResult = await _generateSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var sspJson = JsonDocument.Parse(sspResult);
        sspJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var sspData = sspJson.RootElement.GetProperty("data");
        sspData.GetProperty("system_name").GetString().Should().Be("SSP Integration System");
        sspData.GetProperty("total_controls").GetInt32().Should().Be(totalControls);
        sspData.GetProperty("controls_with_narratives").GetInt32().Should().BeGreaterOrEqualTo(2); // 1 manual + at least 1 batch
        sspData.GetProperty("content").GetString().Should().Contain("System Security Plan");
        sspData.GetProperty("content").GetString().Should().Contain("SSP Integration System");
        sspData.GetProperty("sections").GetArrayLength().Should().Be(13);
    }

    /// <summary>
    /// Write narrative → update same control → verify upsert behavior.
    /// </summary>
    [Fact]
    public async Task WriteNarrative_UpdateExisting_OverwritesNarrative()
    {
        var systemId = await RegisterSystem("Upsert Test System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");
        await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = true
        });

        // Write initial narrative
        var write1 = await _writeNarrativeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["control_id"] = "AC-1",
            ["narrative"] = "Initial narrative v1",
            ["status"] = "Planned"
        });

        var json1 = JsonDocument.Parse(write1);
        json1.RootElement.GetProperty("status").GetString().Should().Be("success");
        json1.RootElement.GetProperty("data").GetProperty("implementation_status").GetString().Should().Be("Planned");

        // Update same control
        var write2 = await _writeNarrativeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["control_id"] = "AC-1",
            ["narrative"] = "Updated narrative v2 with more detail",
            ["status"] = "Implemented"
        });

        var json2 = JsonDocument.Parse(write2);
        json2.RootElement.GetProperty("status").GetString().Should().Be("success");
        json2.RootElement.GetProperty("data").GetProperty("narrative").GetString().Should().Contain("v2");
        json2.RootElement.GetProperty("data").GetProperty("implementation_status").GetString().Should().Be("Implemented");
        json2.RootElement.GetProperty("data").GetProperty("modified_at").GetString().Should().NotBeNull();
    }

    /// <summary>
    /// Batch populate is idempotent — second run skips all previously populated controls.
    /// </summary>
    [Fact]
    public async Task BatchPopulate_Idempotent_SecondRunSkipsAll()
    {
        var systemId = await RegisterSystem("Idempotent Test System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");
        await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = true
        });

        // Set 2 controls as Inherited using inheritance_mappings
        var inheritanceMappings = new List<InheritanceInput>
        {
            new() { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure (FedRAMP High)" },
            new() { ControlId = "AC-2", InheritanceType = "Inherited", Provider = "Azure (FedRAMP High)" }
        };

        var setResult = await _setInheritanceTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["inheritance_mappings"] = inheritanceMappings
        });
        JsonDocument.Parse(setResult).RootElement.GetProperty("status").GetString().Should().Be("success");

        // First batch populate
        var batch1 = await _batchPopulateNarrativesTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["inheritance_type"] = "Inherited"
        });

        var batchJson1 = JsonDocument.Parse(batch1);
        batchJson1.RootElement.GetProperty("status").GetString().Should().Be("success");
        var populated1 = batchJson1.RootElement.GetProperty("data").GetProperty("populated_count").GetInt32();
        populated1.Should().Be(2); // Both controls populated

        // Second batch populate — should skip all
        var batch2 = await _batchPopulateNarrativesTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["inheritance_type"] = "Inherited"
        });

        var batchJson2 = JsonDocument.Parse(batch2);
        batchJson2.RootElement.GetProperty("status").GetString().Should().Be("success");
        batchJson2.RootElement.GetProperty("data").GetProperty("populated_count").GetInt32().Should().Be(0);
        batchJson2.RootElement.GetProperty("data").GetProperty("skipped_count").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// SSP with section filter only includes requested sections.
    /// </summary>
    [Fact]
    public async Task GenerateSsp_SectionFilter_OnlyIncludesRequested()
    {
        var systemId = await RegisterSystem("Section Filter System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");
        await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = true
        });

        var sspResult = await _generateSspTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["sections"] = "system_information,categorization"
        });

        var sspJson = JsonDocument.Parse(sspResult);
        sspJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var sspData = sspJson.RootElement.GetProperty("data");
        sspData.GetProperty("sections").GetArrayLength().Should().Be(2);
        sspData.GetProperty("content").GetString().Should().Contain("System Identification");
        sspData.GetProperty("content").GetString().Should().Contain("Security Categorization");
    }

    /// <summary>
    /// Progress with family filter returns only the specified family.
    /// </summary>
    [Fact]
    public async Task NarrativeProgress_FamilyFilter_ReturnsSingleFamily()
    {
        var systemId = await RegisterSystem("Family Filter System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");
        await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = true
        });

        // Write a narrative for AC-1
        await _writeNarrativeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["control_id"] = "AC-1",
            ["narrative"] = "Access control policy documented.",
            ["status"] = "Implemented"
        });

        // Check progress for AC family only
        var progressResult = await _narrativeProgressTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["family_filter"] = "AC"
        });

        var json = JsonDocument.Parse(progressResult);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("family_breakdowns").GetArrayLength().Should().Be(1);
        data.GetProperty("family_breakdowns")[0].GetProperty("family").GetString().Should().Be("AC");
        data.GetProperty("family_breakdowns")[0].GetProperty("completed").GetInt32().Should().BeGreaterOrEqualTo(1);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

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
}

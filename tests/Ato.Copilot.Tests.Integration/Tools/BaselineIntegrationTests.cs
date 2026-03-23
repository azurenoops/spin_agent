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
/// Integration tests for Feature 015 Phase 5 — Baseline Selection, Tailoring, Inheritance, CRM.
/// Uses real BaselineService + CategorizationService + RmfLifecycleService with in-memory EF Core database.
/// Validates: register → categorize → select baseline → apply overlay → tailor → set inheritance → generate CRM.
/// </summary>
public class BaselineIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly RegisterSystemTool _registerTool;
    private readonly CategorizeSystemTool _categorizeTool;
    private readonly SelectBaselineTool _selectBaselineTool;
    private readonly TailorBaselineTool _tailorBaselineTool;
    private readonly SetInheritanceTool _setInheritanceTool;
    private readonly GetBaselineTool _getBaselineTool;
    private readonly GenerateCrmTool _generateCrmTool;

    public BaselineIntegrationTests()
    {
        var dbName = $"BaselineIntTest_{Guid.NewGuid()}";
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

        _registerTool = new RegisterSystemTool(lifecycleSvc, Mock.Of<ILogger<RegisterSystemTool>>());
        _categorizeTool = new CategorizeSystemTool(categorizationSvc, Mock.Of<ILogger<CategorizeSystemTool>>());
        _selectBaselineTool = new SelectBaselineTool(baselineSvc, Mock.Of<ILogger<SelectBaselineTool>>());
        _tailorBaselineTool = new TailorBaselineTool(baselineSvc, Mock.Of<ILogger<TailorBaselineTool>>());
        _setInheritanceTool = new SetInheritanceTool(baselineSvc, Mock.Of<ILogger<SetInheritanceTool>>());
        _getBaselineTool = new GetBaselineTool(baselineSvc, Mock.Of<ILogger<GetBaselineTool>>());
        _generateCrmTool = new GenerateCrmTool(baselineSvc, Mock.Of<ILogger<GenerateCrmTool>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    /// <summary>
    /// End-to-end: Register → Categorize (Moderate) → Select baseline → Apply overlay →
    /// Tailor (add 2, remove 1) → Set inheritance (50 inherited, 10 shared) → Generate CRM → Verify counts.
    /// </summary>
    [Fact]
    public async Task FullBaselineLifecycle_SelectTailorInheritCrm()
    {
        // Step 1: Register a system
        var systemId = await RegisterSystem("Baseline Lifecycle System", "MajorApplication");

        // Step 2: Categorize as Moderate (C=Moderate, I=Moderate, A=Low → Overall=Moderate → IL4)
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");

        // Step 3: Select baseline with overlay
        var selectResult = await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = true
        });

        var selectJson = JsonDocument.Parse(selectResult);
        selectJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var selectData = selectJson.RootElement.GetProperty("data");
        selectData.GetProperty("baseline_level").GetString().Should().Be("Moderate");
        selectData.GetProperty("overlay_applied").GetString().Should().Contain("CNSSI 1253 IL4");
        var totalAfterSelect = selectData.GetProperty("total_controls").GetInt32();
        totalAfterSelect.Should().BeGreaterOrEqualTo(329); // Moderate baseline + possible overlay additions

        // Step 4: Tailor — add 2, remove 1
        var controlIds = selectData.GetProperty("control_ids");
        var firstControl = controlIds[0].GetString()!;

        var tailorResult = await _tailorBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["tailoring_actions"] = new List<TailoringInput>
            {
                new() { ControlId = "ZZ-99", Action = "Added", Rationale = "Custom org control" },
                new() { ControlId = "ZZ-100", Action = "Added", Rationale = "Custom org control 2" },
                new() { ControlId = firstControl, Action = "Removed", Rationale = "Not applicable — cloud-hosted" }
            }
        });

        var tailorJson = JsonDocument.Parse(tailorResult);
        tailorJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var tailorData = tailorJson.RootElement.GetProperty("data");
        tailorData.GetProperty("accepted_count").GetInt32().Should().Be(3);
        tailorData.GetProperty("rejected_count").GetInt32().Should().Be(0);
        tailorData.GetProperty("tailored_in").GetInt32().Should().Be(2);
        tailorData.GetProperty("tailored_out").GetInt32().Should().Be(1);

        // Verify total: +2 -1 = net +1
        var totalAfterTailor = tailorData.GetProperty("total_controls").GetInt32();
        totalAfterTailor.Should().Be(totalAfterSelect + 1);

        // Step 5: Set inheritance for some controls
        var getResult = await _getBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });
        var getJson = JsonDocument.Parse(getResult);
        var currentControls = getJson.RootElement.GetProperty("data").GetProperty("control_ids");
        var controlList = new List<string>();
        foreach (var c in currentControls.EnumerateArray())
            controlList.Add(c.GetString()!);

        // Set 50 inherited, 10 shared from first 60 controls
        var inheritanceMappings = new List<InheritanceInput>();
        for (int i = 0; i < Math.Min(50, controlList.Count); i++)
        {
            inheritanceMappings.Add(new InheritanceInput
            {
                ControlId = controlList[i],
                InheritanceType = "Inherited",
                Provider = "Azure Government (FedRAMP High)"
            });
        }
        for (int i = 50; i < Math.Min(60, controlList.Count); i++)
        {
            inheritanceMappings.Add(new InheritanceInput
            {
                ControlId = controlList[i],
                InheritanceType = "Shared",
                Provider = "Azure Government",
                CustomerResponsibility = "Customer configures policies"
            });
        }

        var inhResult = await _setInheritanceTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["inheritance_mappings"] = inheritanceMappings
        });

        var inhJson = JsonDocument.Parse(inhResult);
        inhJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var inhData = inhJson.RootElement.GetProperty("data");
        inhData.GetProperty("controls_updated").GetInt32().Should().Be(60);
        inhData.GetProperty("inherited_count").GetInt32().Should().Be(50);
        inhData.GetProperty("shared_count").GetInt32().Should().Be(10);

        // Step 6: Generate CRM
        var crmResult = await _generateCrmTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var crmJson = JsonDocument.Parse(crmResult);
        crmJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var crmData = crmJson.RootElement.GetProperty("data");
        crmData.GetProperty("system_name").GetString().Should().Be("Baseline Lifecycle System");
        crmData.GetProperty("baseline_level").GetString().Should().Be("Moderate");
        crmData.GetProperty("inherited_controls").GetInt32().Should().Be(50);
        crmData.GetProperty("shared_controls").GetInt32().Should().Be(10);
        crmData.GetProperty("total_controls").GetInt32().Should().Be(totalAfterTailor);
        crmData.GetProperty("undesignated_controls").GetInt32().Should().Be(totalAfterTailor - 60);
        crmData.GetProperty("inheritance_percentage").GetDouble().Should().BeGreaterThan(0);
        crmData.GetProperty("family_groups").GetArrayLength().Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Select baseline without overlay, verify no overlay applied.
    /// </summary>
    [Fact]
    public async Task SelectBaseline_WithoutOverlay_PureNistBaseline()
    {
        var systemId = await RegisterSystem("No Overlay System", "Enclave");
        await CategorizeSystem(systemId, "Low", "Low", "Low");

        var result = await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = false
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("baseline_level").GetString().Should().Be("Low");
        data.GetProperty("overlay_applied").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("total_controls").GetInt32().Should().Be(152); // Pure Low baseline
    }

    /// <summary>
    /// Reselecting baseline replaces the previous baseline.
    /// </summary>
    [Fact]
    public async Task ReselectBaseline_ReplacesPreviousBaseline()
    {
        var systemId = await RegisterSystem("Reselect System", "MajorApplication");
        await CategorizeSystem(systemId, "Moderate", "Moderate", "Low");

        // First selection
        await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = false
        });

        // Set some inheritance on first baseline
        var inhResult1 = await _setInheritanceTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["inheritance_mappings"] = new List<InheritanceInput>
            {
                new() { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Test" }
            }
        });

        // Re-select — should replace and clear inheritance
        var result2 = await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = true
        });

        var json2 = JsonDocument.Parse(result2);
        json2.RootElement.GetProperty("status").GetString().Should().Be("success");
        json2.RootElement.GetProperty("data").GetProperty("inherited_controls").GetInt32().Should().Be(0);
        json2.RootElement.GetProperty("data").GetProperty("overlay_applied").GetString().Should().Contain("CNSSI 1253");
    }

    /// <summary>
    /// Select baseline fails when no categorization exists.
    /// </summary>
    [Fact]
    public async Task SelectBaseline_NoCategorization_ReturnsError()
    {
        var systemId = await RegisterSystem("Uncategorized System", "PlatformIt");

        var result = await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("BASELINE_SELECTION_FAILED");
        json.RootElement.GetProperty("message").GetString().Should().Contain("no security categorization");
    }

    /// <summary>
    /// Get baseline with details includes tailoring and inheritance records.
    /// </summary>
    [Fact]
    public async Task GetBaseline_WithDetails_ShowsTailoringAndInheritance()
    {
        var systemId = await RegisterSystem("Details System", "MajorApplication");
        await CategorizeSystem(systemId, "Low", "Low", "Low");

        await _selectBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["apply_overlay"] = false
        });

        // Tailor
        await _tailorBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["tailoring_actions"] = new List<TailoringInput>
            {
                new() { ControlId = "ZZ-1", Action = "Added", Rationale = "Org requirement" }
            }
        });

        // Set inheritance
        await _setInheritanceTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["inheritance_mappings"] = new List<InheritanceInput>
            {
                new() { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure" }
            }
        });

        // Get with details
        var result = await _getBaselineTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["include_details"] = true
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("tailorings").GetArrayLength().Should().Be(1);
        data.GetProperty("inheritances").GetArrayLength().Should().Be(1);

        var tailoring = data.GetProperty("tailorings")[0];
        tailoring.GetProperty("control_id").GetString().Should().Be("ZZ-1");
        tailoring.GetProperty("action").GetString().Should().Be("Added");

        var inheritance = data.GetProperty("inheritances")[0];
        inheritance.GetProperty("control_id").GetString().Should().Be("AC-1");
        inheritance.GetProperty("inheritance_type").GetString().Should().Be("Inherited");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

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

        return JsonDocument.Parse(result).RootElement
            .GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task CategorizeSystem(string systemId, string c, string i, string a)
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
                    ConfidentialityImpact = c, IntegrityImpact = i, AvailabilityImpact = a
                }
            },
            ["justification"] = "Integration test categorization"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
    }
}

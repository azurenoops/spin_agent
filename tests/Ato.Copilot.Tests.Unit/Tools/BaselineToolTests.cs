using System.Text.Json;
using Moq;
using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Tools;

/// <summary>
/// Unit tests for Feature 015 Phase 5 — Baseline Selection, Tailoring, Inheritance, CRM Tools.
/// T054: SelectBaselineTool tests
/// T055: TailorBaselineTool tests
/// T056: SetInheritanceTool tests
/// T057: GenerateCrmTool / GetBaselineTool tests
/// </summary>
public class BaselineToolTests
{
    private readonly Mock<IBaselineService> _baselineMock = new();

    // ────────────────────────────────────────────────────────────────────────
    // T054: SelectBaselineTool Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectBaseline_LowBaseline_ReturnsSuccess()
    {
        var baseline = CreateBaseline("sys-1", "Low", null, 152);
        _baselineMock
            .Setup(s => s.SelectBaselineAsync("sys-1", true, null, "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseline);

        var tool = CreateSelectBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("baseline_level").GetString().Should().Be("Low");
        json.RootElement.GetProperty("data").GetProperty("total_controls").GetInt32().Should().Be(152);
    }

    [Fact]
    public async Task SelectBaseline_ModerateWithOverlay_ReturnsOverlayApplied()
    {
        var baseline = CreateBaseline("sys-2", "Moderate", "CNSSI 1253 IL4", 340);
        _baselineMock
            .Setup(s => s.SelectBaselineAsync("sys-2", true, null, "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseline);

        var tool = CreateSelectBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-2",
            ["apply_overlay"] = true
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("baseline_level").GetString().Should().Be("Moderate");
        json.RootElement.GetProperty("data").GetProperty("overlay_applied").GetString().Should().Be("CNSSI 1253 IL4");
        json.RootElement.GetProperty("data").GetProperty("total_controls").GetInt32().Should().Be(340);
    }

    [Fact]
    public async Task SelectBaseline_HighBaseline_ReturnsHighCount()
    {
        var baseline = CreateBaseline("sys-3", "High", "CNSSI 1253 IL5", 410);
        _baselineMock
            .Setup(s => s.SelectBaselineAsync("sys-3", true, null, "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseline);

        var tool = CreateSelectBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-3"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("baseline_level").GetString().Should().Be("High");
        json.RootElement.GetProperty("data").GetProperty("total_controls").GetInt32().Should().BeGreaterOrEqualTo(400);
    }

    [Fact]
    public async Task SelectBaseline_WithoutOverlay_ReturnsNoOverlay()
    {
        var baseline = CreateBaseline("sys-4", "Moderate", null, 329);
        _baselineMock
            .Setup(s => s.SelectBaselineAsync("sys-4", false, null, "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseline);

        var tool = CreateSelectBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-4",
            ["apply_overlay"] = false
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("overlay_applied").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task SelectBaseline_MissingCategorization_ReturnsError()
    {
        _baselineMock
            .Setup(s => s.SelectBaselineAsync("uncategorized", true, null, "mcp-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System 'uncategorized' has no security categorization."));

        var tool = CreateSelectBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "uncategorized"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("BASELINE_SELECTION_FAILED");
        json.RootElement.GetProperty("message").GetString().Should().Contain("no security categorization");
    }

    [Fact]
    public async Task SelectBaseline_MissingSystemId_ReturnsError()
    {
        var tool = CreateSelectBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task SelectBaseline_SystemNotFound_ReturnsError()
    {
        _baselineMock
            .Setup(s => s.SelectBaselineAsync("nonexistent", true, null, "mcp-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System 'nonexistent' not found."));

        var tool = CreateSelectBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "nonexistent"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("message").GetString().Should().Contain("not found");
    }

    [Fact]
    public async Task SelectBaseline_CustomOverlayName_PassesOverlayName()
    {
        var baseline = CreateBaseline("sys-5", "Moderate", "Custom Overlay v2", 335);
        _baselineMock
            .Setup(s => s.SelectBaselineAsync("sys-5", true, "Custom Overlay v2", "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseline);

        var tool = CreateSelectBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-5",
            ["overlay_name"] = "Custom Overlay v2"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("overlay_applied").GetString().Should().Be("Custom Overlay v2");
    }

    [Fact]
    public async Task SelectBaseline_VerifiesControlIdsList()
    {
        var baseline = CreateBaseline("sys-6", "Low", null, 3);
        baseline.ControlIds = new List<string> { "AC-1", "AC-2", "AT-1" };

        _baselineMock
            .Setup(s => s.SelectBaselineAsync("sys-6", true, null, "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseline);

        var tool = CreateSelectBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-6"
        });

        var json = JsonDocument.Parse(result);
        var controlIds = json.RootElement.GetProperty("data").GetProperty("control_ids");
        controlIds.GetArrayLength().Should().Be(3);
        controlIds[0].GetString().Should().Be("AC-1");
    }

    // ────────────────────────────────────────────────────────────────────────
    // T055: TailorBaselineTool Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TailorBaseline_AddControl_ReturnsAccepted()
    {
        var tailoringResult = new TailoringResult
        {
            Baseline = CreateBaseline("sys-1", "Moderate", "CNSSI 1253 IL4", 330),
            Accepted = new List<TailoringActionResult>
            {
                new() { ControlId = "SI-7(15)", Action = "Added", Accepted = true }
            }
        };
        tailoringResult.Baseline.TailoredInControls = 1;

        _baselineMock
            .Setup(s => s.TailorBaselineAsync("sys-1", It.IsAny<IEnumerable<TailoringInput>>(), "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tailoringResult);

        var tool = CreateTailorBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["tailoring_actions"] = new List<TailoringInput>
            {
                new() { ControlId = "SI-7(15)", Action = "Added", Rationale = "Required for FedRAMP High" }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("accepted_count").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("data").GetProperty("rejected_count").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("data").GetProperty("tailored_in").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task TailorBaseline_RemoveControl_ReturnsAccepted()
    {
        var tailoringResult = new TailoringResult
        {
            Baseline = CreateBaseline("sys-1", "Moderate", "CNSSI 1253 IL4", 328),
            Accepted = new List<TailoringActionResult>
            {
                new() { ControlId = "PE-4", Action = "Removed", Accepted = true }
            }
        };
        tailoringResult.Baseline.TailoredOutControls = 1;

        _baselineMock
            .Setup(s => s.TailorBaselineAsync("sys-1", It.IsAny<IEnumerable<TailoringInput>>(), "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tailoringResult);

        var tool = CreateTailorBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["tailoring_actions"] = new List<TailoringInput>
            {
                new() { ControlId = "PE-4", Action = "Removed", Rationale = "Not applicable — cloud-hosted" }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("accepted_count").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("data").GetProperty("tailored_out").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task TailorBaseline_OverlayRequiredRemoval_WarnsButAccepts()
    {
        var tailoringResult = new TailoringResult
        {
            Baseline = CreateBaseline("sys-1", "Moderate", "CNSSI 1253 IL4", 328),
            Accepted = new List<TailoringActionResult>
            {
                new()
                {
                    ControlId = "AC-2", Action = "Removed", Accepted = true,
                    Reason = "WARNING: Control is required by overlay. Removal documented with rationale."
                }
            }
        };

        _baselineMock
            .Setup(s => s.TailorBaselineAsync("sys-1", It.IsAny<IEnumerable<TailoringInput>>(), "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tailoringResult);

        var tool = CreateTailorBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["tailoring_actions"] = new List<TailoringInput>
            {
                new() { ControlId = "AC-2", Action = "Removed", Rationale = "Compensating control AC-2(1) in place" }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var accepted = json.RootElement.GetProperty("data").GetProperty("accepted");
        accepted.EnumerateArray().First().GetProperty("reason").GetString().Should().Contain("WARNING");
    }

    [Fact]
    public async Task TailorBaseline_DuplicateAdd_ReturnsRejected()
    {
        var tailoringResult = new TailoringResult
        {
            Baseline = CreateBaseline("sys-1", "Moderate", "CNSSI 1253 IL4", 329),
            Rejected = new List<TailoringActionResult>
            {
                new()
                {
                    ControlId = "AC-1", Action = "Added", Accepted = false,
                    Reason = "Control 'AC-1' is already in the baseline."
                }
            }
        };

        _baselineMock
            .Setup(s => s.TailorBaselineAsync("sys-1", It.IsAny<IEnumerable<TailoringInput>>(), "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tailoringResult);

        var tool = CreateTailorBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["tailoring_actions"] = new List<TailoringInput>
            {
                new() { ControlId = "AC-1", Action = "Added", Rationale = "already present" }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("rejected_count").GetInt32().Should().Be(1);
        var rejected = json.RootElement.GetProperty("data").GetProperty("rejected");
        rejected.EnumerateArray().First().GetProperty("reason").GetString().Should().Contain("already in the baseline");
    }

    [Fact]
    public async Task TailorBaseline_MissingSystemId_ReturnsError()
    {
        var tool = CreateTailorBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["tailoring_actions"] = new List<TailoringInput>()
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task TailorBaseline_MissingActions_ReturnsError()
    {
        var tool = CreateTailorBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task TailorBaseline_NoBaselineExists_ReturnsError()
    {
        _baselineMock
            .Setup(s => s.TailorBaselineAsync("no-baseline", It.IsAny<IEnumerable<TailoringInput>>(), "mcp-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No baseline found for system 'no-baseline'."));

        var tool = CreateTailorBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "no-baseline",
            ["tailoring_actions"] = new List<TailoringInput>
            {
                new() { ControlId = "AC-1", Action = "Added", Rationale = "test" }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("TAILORING_FAILED");
    }

    [Fact]
    public async Task TailorBaseline_MixedAddRemove_ReturnsBothResults()
    {
        var tailoringResult = new TailoringResult
        {
            Baseline = CreateBaseline("sys-1", "Moderate", "CNSSI 1253 IL4", 330),
            Accepted = new List<TailoringActionResult>
            {
                new() { ControlId = "SI-7(15)", Action = "Added", Accepted = true },
                new() { ControlId = "PE-4", Action = "Removed", Accepted = true }
            },
            Rejected = new List<TailoringActionResult>
            {
                new() { ControlId = "AC-1", Action = "Added", Accepted = false, Reason = "Already in baseline." }
            }
        };

        _baselineMock
            .Setup(s => s.TailorBaselineAsync("sys-1", It.IsAny<IEnumerable<TailoringInput>>(), "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tailoringResult);

        var tool = CreateTailorBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["tailoring_actions"] = new List<TailoringInput>
            {
                new() { ControlId = "SI-7(15)", Action = "Added", Rationale = "test" },
                new() { ControlId = "PE-4", Action = "Removed", Rationale = "cloud" },
                new() { ControlId = "AC-1", Action = "Added", Rationale = "dup" }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("accepted_count").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("data").GetProperty("rejected_count").GetInt32().Should().Be(1);
    }

    // ────────────────────────────────────────────────────────────────────────
    // T056: SetInheritanceTool Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetInheritance_Inherited_ReturnsSuccess()
    {
        var inheritanceResult = new InheritanceResult
        {
            Baseline = CreateBaseline("sys-1", "Moderate", "CNSSI 1253 IL4", 329),
            ControlsUpdated = 1,
            InheritedCount = 1,
            SharedCount = 0,
            CustomerCount = 0
        };

        _baselineMock
            .Setup(s => s.SetInheritanceAsync("sys-1", It.IsAny<IEnumerable<InheritanceInput>>(), "mcp-user", It.IsAny<InheritanceChangeSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inheritanceResult);

        var tool = CreateSetInheritanceTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["inheritance_mappings"] = new List<InheritanceInput>
            {
                new() { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure Government (FedRAMP High)" }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("controls_updated").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("data").GetProperty("inherited_count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SetInheritance_SharedWithResponsibility_ReturnsSuccess()
    {
        var inheritanceResult = new InheritanceResult
        {
            Baseline = CreateBaseline("sys-1", "Moderate", "CNSSI 1253 IL4", 329),
            ControlsUpdated = 1,
            InheritedCount = 0,
            SharedCount = 1,
            CustomerCount = 0
        };

        _baselineMock
            .Setup(s => s.SetInheritanceAsync("sys-1", It.IsAny<IEnumerable<InheritanceInput>>(), "mcp-user", It.IsAny<InheritanceChangeSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inheritanceResult);

        var tool = CreateSetInheritanceTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["inheritance_mappings"] = new List<InheritanceInput>
            {
                new()
                {
                    ControlId = "AC-2", InheritanceType = "Shared",
                    Provider = "Azure Government",
                    CustomerResponsibility = "Customer configures RBAC policies"
                }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("shared_count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SetInheritance_Customer_ReturnsSuccess()
    {
        var inheritanceResult = new InheritanceResult
        {
            Baseline = CreateBaseline("sys-1", "Moderate", "CNSSI 1253 IL4", 329),
            ControlsUpdated = 1,
            InheritedCount = 0,
            SharedCount = 0,
            CustomerCount = 1
        };

        _baselineMock
            .Setup(s => s.SetInheritanceAsync("sys-1", It.IsAny<IEnumerable<InheritanceInput>>(), "mcp-user", It.IsAny<InheritanceChangeSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inheritanceResult);

        var tool = CreateSetInheritanceTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["inheritance_mappings"] = new List<InheritanceInput>
            {
                new() { ControlId = "AC-6", InheritanceType = "Customer" }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("customer_count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SetInheritance_InvalidControl_Skipped()
    {
        var inheritanceResult = new InheritanceResult
        {
            Baseline = CreateBaseline("sys-1", "Moderate", "CNSSI 1253 IL4", 329),
            ControlsUpdated = 0,
            SkippedControls = new List<string> { "INVALID-99" }
        };

        _baselineMock
            .Setup(s => s.SetInheritanceAsync("sys-1", It.IsAny<IEnumerable<InheritanceInput>>(), "mcp-user", It.IsAny<InheritanceChangeSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inheritanceResult);

        var tool = CreateSetInheritanceTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["inheritance_mappings"] = new List<InheritanceInput>
            {
                new() { ControlId = "INVALID-99", InheritanceType = "Inherited", Provider = "Test" }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("controls_updated").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("data").GetProperty("skipped_controls").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task SetInheritance_MissingSystemId_ReturnsError()
    {
        var tool = CreateSetInheritanceTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["inheritance_mappings"] = new List<InheritanceInput>()
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task SetInheritance_MissingMappings_ReturnsError()
    {
        var tool = CreateSetInheritanceTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task SetInheritance_BulkMappings_ReturnsCombinedCounts()
    {
        var inheritanceResult = new InheritanceResult
        {
            Baseline = CreateBaseline("sys-1", "Moderate", "CNSSI 1253 IL4", 329),
            ControlsUpdated = 3,
            InheritedCount = 50,
            SharedCount = 10,
            CustomerCount = 100
        };

        _baselineMock
            .Setup(s => s.SetInheritanceAsync("sys-1", It.IsAny<IEnumerable<InheritanceInput>>(), "mcp-user", It.IsAny<InheritanceChangeSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inheritanceResult);

        var tool = CreateSetInheritanceTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["inheritance_mappings"] = new List<InheritanceInput>
            {
                new() { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure" },
                new() { ControlId = "AC-2", InheritanceType = "Shared", Provider = "Azure", CustomerResponsibility = "RBAC" },
                new() { ControlId = "AC-3", InheritanceType = "Customer" }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("controls_updated").GetInt32().Should().Be(3);
        json.RootElement.GetProperty("data").GetProperty("inherited_count").GetInt32().Should().Be(50);
        json.RootElement.GetProperty("data").GetProperty("shared_count").GetInt32().Should().Be(10);
        json.RootElement.GetProperty("data").GetProperty("customer_count").GetInt32().Should().Be(100);
    }

    // ────────────────────────────────────────────────────────────────────────
    // T057: GetBaselineTool & GenerateCrmTool Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBaseline_NoBaseline_ReturnsNullData()
    {
        _baselineMock
            .Setup(s => s.GetBaselineAsync("no-baseline", false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ControlBaseline?)null);

        var tool = CreateGetBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "no-baseline"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("message").GetString().Should().Contain("No baseline");
    }

    [Fact]
    public async Task GetBaseline_WithDetails_IncludesTailoringsAndInheritances()
    {
        var baseline = CreateBaseline("sys-1", "Moderate", "CNSSI 1253 IL4", 329);
        baseline.Tailorings = new List<ControlTailoring>
        {
            new()
            {
                ControlBaselineId = baseline.Id, ControlId = "SI-7(15)",
                Action = TailoringAction.Added, Rationale = "FedRAMP High", TailoredBy = "issm"
            }
        };
        baseline.Inheritances = new List<ControlInheritance>
        {
            new()
            {
                ControlBaselineId = baseline.Id, ControlId = "AC-1",
                InheritanceType = InheritanceType.Inherited, Provider = "Azure Gov", SetBy = "issm"
            }
        };

        _baselineMock
            .Setup(s => s.GetBaselineAsync("sys-1", true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseline);

        var tool = CreateGetBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["include_details"] = true
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("tailorings").GetArrayLength().Should().Be(1);
        data.GetProperty("inheritances").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetBaseline_FamilyFilter_FiltersResults()
    {
        var baseline = CreateBaseline("sys-1", "Moderate", null, 5);
        baseline.ControlIds = new List<string> { "AC-1", "AC-2" };

        _baselineMock
            .Setup(s => s.GetBaselineAsync("sys-1", false, "AC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseline);

        var tool = CreateGetBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["family_filter"] = "AC"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("control_count").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("data").GetProperty("family_filter").GetString().Should().Be("AC");
    }

    [Fact]
    public async Task GetBaseline_MissingSystemId_ReturnsError()
    {
        var tool = CreateGetBaselineTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task GenerateCrm_CorrectCounts_ReturnsSuccess()
    {
        var crm = new CrmResult
        {
            SystemId = "sys-1",
            SystemName = "Test System",
            BaselineLevel = "Moderate",
            TotalControls = 329,
            InheritedControls = 50,
            SharedControls = 10,
            CustomerControls = 200,
            UndesignatedControls = 69,
            InheritancePercentage = 18.2,
            FamilyGroups = new List<CrmFamilyGroup>
            {
                new()
                {
                    Family = "AC", FamilyName = "Access Control",
                    Controls = new List<CrmEntry>
                    {
                        new() { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure Gov" },
                        new() { ControlId = "AC-2", InheritanceType = "Shared", Provider = "Azure Gov", CustomerResponsibility = "RBAC config" },
                        new() { ControlId = "AC-3", InheritanceType = "Customer" }
                    }
                }
            }
        };

        _baselineMock
            .Setup(s => s.GenerateCrmAsync("sys-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(crm);

        var tool = CreateGenerateCrmTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("total_controls").GetInt32().Should().Be(329);
        data.GetProperty("inherited_controls").GetInt32().Should().Be(50);
        data.GetProperty("shared_controls").GetInt32().Should().Be(10);
        data.GetProperty("customer_controls").GetInt32().Should().Be(200);
        data.GetProperty("undesignated_controls").GetInt32().Should().Be(69);
        data.GetProperty("inheritance_percentage").GetDouble().Should().Be(18.2);
        data.GetProperty("family_groups").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GenerateCrm_EmptyBaseline_ReturnsZeroCounts()
    {
        var crm = new CrmResult
        {
            SystemId = "sys-empty",
            SystemName = "Empty System",
            BaselineLevel = "Low",
            TotalControls = 0,
            InheritedControls = 0,
            SharedControls = 0,
            CustomerControls = 0,
            UndesignatedControls = 0,
            InheritancePercentage = 0.0,
            FamilyGroups = new List<CrmFamilyGroup>()
        };

        _baselineMock
            .Setup(s => s.GenerateCrmAsync("sys-empty", It.IsAny<CancellationToken>()))
            .ReturnsAsync(crm);

        var tool = CreateGenerateCrmTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-empty"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("total_controls").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("data").GetProperty("family_groups").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GenerateCrm_AllInherited_Returns100Percent()
    {
        var crm = new CrmResult
        {
            SystemId = "sys-all-inh",
            SystemName = "All Inherited",
            BaselineLevel = "Low",
            TotalControls = 152,
            InheritedControls = 152,
            SharedControls = 0,
            CustomerControls = 0,
            UndesignatedControls = 0,
            InheritancePercentage = 100.0,
            FamilyGroups = new List<CrmFamilyGroup>()
        };

        _baselineMock
            .Setup(s => s.GenerateCrmAsync("sys-all-inh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(crm);

        var tool = CreateGenerateCrmTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-all-inh"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("inheritance_percentage").GetDouble().Should().Be(100.0);
        json.RootElement.GetProperty("data").GetProperty("inherited_controls").GetInt32().Should().Be(152);
    }

    [Fact]
    public async Task GenerateCrm_NoBaseline_ReturnsError()
    {
        _baselineMock
            .Setup(s => s.GenerateCrmAsync("no-baseline", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No baseline found."));

        var tool = CreateGenerateCrmTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "no-baseline"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("CRM_GENERATION_FAILED");
    }

    [Fact]
    public async Task GenerateCrm_MissingSystemId_ReturnsError()
    {
        var tool = CreateGenerateCrmTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task GenerateCrm_FamilyGroupStructure_ContainsExpectedFields()
    {
        var crm = new CrmResult
        {
            SystemId = "sys-1",
            SystemName = "Test System",
            BaselineLevel = "Moderate",
            TotalControls = 329,
            InheritedControls = 100,
            SharedControls = 50,
            CustomerControls = 150,
            UndesignatedControls = 29,
            InheritancePercentage = 45.6,
            FamilyGroups = new List<CrmFamilyGroup>
            {
                new()
                {
                    Family = "AC", FamilyName = "Access Control",
                    Controls = new List<CrmEntry>
                    {
                        new() { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure" },
                        new() { ControlId = "AC-2", InheritanceType = "Undesignated" }
                    }
                },
                new()
                {
                    Family = "AT", FamilyName = "Awareness and Training",
                    Controls = new List<CrmEntry>
                    {
                        new() { ControlId = "AT-1", InheritanceType = "Customer" }
                    }
                }
            }
        };

        _baselineMock
            .Setup(s => s.GenerateCrmAsync("sys-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(crm);

        var tool = CreateGenerateCrmTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        var groups = json.RootElement.GetProperty("data").GetProperty("family_groups");
        groups.GetArrayLength().Should().Be(2);

        var acGroup = groups[0];
        acGroup.GetProperty("family").GetString().Should().Be("AC");
        acGroup.GetProperty("family_name").GetString().Should().Be("Access Control");
        acGroup.GetProperty("control_count").GetInt32().Should().Be(2);
        acGroup.GetProperty("controls")[0].GetProperty("inheritance_type").GetString().Should().Be("Inherited");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private SelectBaselineTool CreateSelectBaselineTool() =>
        new(_baselineMock.Object, Mock.Of<ILogger<SelectBaselineTool>>());

    private TailorBaselineTool CreateTailorBaselineTool() =>
        new(_baselineMock.Object, Mock.Of<ILogger<TailorBaselineTool>>());

    private SetInheritanceTool CreateSetInheritanceTool() =>
        new(_baselineMock.Object, Mock.Of<ILogger<SetInheritanceTool>>());

    private GetBaselineTool CreateGetBaselineTool() =>
        new(_baselineMock.Object, Mock.Of<ILogger<GetBaselineTool>>());

    private GenerateCrmTool CreateGenerateCrmTool() =>
        new(_baselineMock.Object, Mock.Of<ILogger<GenerateCrmTool>>());

    private static ControlBaseline CreateBaseline(string systemId, string level, string? overlay, int totalControls)
    {
        var controlIds = new List<string>();
        for (int i = 1; i <= Math.Min(totalControls, 5); i++)
            controlIds.Add($"AC-{i}");

        return new ControlBaseline
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = systemId,
            BaselineLevel = level,
            OverlayApplied = overlay,
            TotalControls = totalControls,
            ControlIds = controlIds,
            CreatedBy = "mcp-user",
            CreatedAt = DateTime.UtcNow
        };
    }
}

// ────────────────────────────────────────────────────────────────────────────
// T064: ShowStigMappingTool Tests
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Unit tests for Feature 015 Phase 6 — ShowStigMappingTool (STIG-to-NIST mapping via CCI chain).
/// Tests: valid control mapping, no mappings, severity filter, CCI chain resolution, missing control_id.
/// </summary>
public class ShowStigMappingToolTests
{
    private readonly Mock<IStigKnowledgeService> _stigMock = new();

    [Fact]
    public async Task ShowStigMapping_ValidControl_ReturnsMatchingRules()
    {
        var stigControls = new List<StigControl>
        {
            CreateStigControl("V-254239", "High", "Windows_Server_2022_STIG", ["AC-2"], ["CCI-000015"]),
            CreateStigControl("V-254240", "Medium", "Windows_Server_2022_STIG", ["AC-2"], ["CCI-000016"]),
            CreateStigControl("V-260332", "High", "Azure_Foundations_STIG", ["AC-2"], ["CCI-000015"]),
        };

        var cciMappings = new List<CciMapping>
        {
            new("CCI-000015", "AC-2", "The organization manages information system accounts.", "published"),
            new("CCI-000016", "AC-2", "The organization defines account types.", "published"),
        };

        _stigMock
            .Setup(s => s.GetStigsByCciChainAsync("AC-2", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stigControls);
        _stigMock
            .Setup(s => s.GetCciMappingsAsync("AC-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cciMappings);

        var tool = CreateShowStigMappingTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["control_id"] = "AC-2"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");

        var data = json.RootElement.GetProperty("data");
        data.GetProperty("control_id").GetString().Should().Be("AC-2");
        data.GetProperty("total_stig_rules").GetInt32().Should().Be(3);
        data.GetProperty("cci_count").GetInt32().Should().Be(2);

        var catSummary = data.GetProperty("cat_summary");
        catSummary.GetProperty("cat_i_high").GetInt32().Should().Be(2);
        catSummary.GetProperty("cat_ii_medium").GetInt32().Should().Be(1);

        var rules = data.GetProperty("stig_rules");
        rules.GetArrayLength().Should().Be(3);
        rules[0].GetProperty("stig_id").GetString().Should().Be("V-254239");
    }

    [Fact]
    public async Task ShowStigMapping_NoMappings_ReturnsEmptyResults()
    {
        _stigMock
            .Setup(s => s.GetStigsByCciChainAsync("ZZ-99", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StigControl>());
        _stigMock
            .Setup(s => s.GetCciMappingsAsync("ZZ-99", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CciMapping>());

        var tool = CreateShowStigMappingTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["control_id"] = "ZZ-99"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");

        var data = json.RootElement.GetProperty("data");
        data.GetProperty("total_stig_rules").GetInt32().Should().Be(0);
        data.GetProperty("cci_count").GetInt32().Should().Be(0);
        data.GetProperty("stig_rules").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ShowStigMapping_SeverityFilter_ReturnsFilteredRules()
    {
        var highControls = new List<StigControl>
        {
            CreateStigControl("V-260328", "High", "Azure_Foundations_STIG", ["SI-4"], ["CCI-002702"]),
        };

        _stigMock
            .Setup(s => s.GetStigsByCciChainAsync("SI-4", StigSeverity.High, It.IsAny<CancellationToken>()))
            .ReturnsAsync(highControls);
        _stigMock
            .Setup(s => s.GetCciMappingsAsync("SI-4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CciMapping> { new("CCI-002702", "SI-4", "Monitor the information system.", "published") });

        var tool = CreateShowStigMappingTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["control_id"] = "SI-4",
            ["severity"] = "High"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");

        var data = json.RootElement.GetProperty("data");
        data.GetProperty("total_stig_rules").GetInt32().Should().Be(1);
        data.GetProperty("stig_rules")[0].GetProperty("severity").GetString().Should().Be("High");
        data.GetProperty("stig_rules")[0].GetProperty("category").GetString().Should().Be("CAT I");
    }

    [Fact]
    public async Task ShowStigMapping_InvalidSeverity_ReturnsError()
    {
        var tool = CreateShowStigMappingTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["control_id"] = "AC-2",
            ["severity"] = "Critical"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
        json.RootElement.GetProperty("message").GetString().Should().Contain("Critical");
    }

    [Fact]
    public async Task ShowStigMapping_MissingControlId_ReturnsError()
    {
        var tool = CreateShowStigMappingTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["control_id"] = ""
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task ShowStigMapping_NullControlId_ReturnsError()
    {
        var tool = CreateShowStigMappingTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["control_id"] = null
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task ShowStigMapping_CciChainResolution_ReturnsBenchmarkBreakdown()
    {
        var stigControls = new List<StigControl>
        {
            CreateStigControl("V-254253", "Medium", "Windows_Server_2022_STIG", ["SC-8"], ["CCI-002418"]),
            CreateStigControl("V-218724", "High", "IIS_10_STIG", ["SC-8"], ["CCI-002418"]),
            CreateStigControl("V-255615", "High", "SQL_Server_2019_STIG", ["SC-8"], ["CCI-002418"]),
            CreateStigControl("V-260336", "High", "Azure_Foundations_STIG", ["SC-8"], ["CCI-002418"]),
            CreateStigControl("V-242378", "High", "Kubernetes_STIG", ["SC-8"], ["CCI-002418"]),
        };

        var cciMappings = new List<CciMapping>
        {
            new("CCI-002418", "SC-8", "Protect confidentiality and integrity of transmitted information.", "published"),
            new("CCI-002421", "SC-8", "Implement cryptographic mechanisms to protect data in transit.", "published"),
        };

        _stigMock
            .Setup(s => s.GetStigsByCciChainAsync("SC-8", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stigControls);
        _stigMock
            .Setup(s => s.GetCciMappingsAsync("SC-8", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cciMappings);

        var tool = CreateShowStigMappingTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["control_id"] = "SC-8"
        });

        var json = JsonDocument.Parse(result);
        var data = json.RootElement.GetProperty("data");

        // Verify benchmark breakdown
        var benchmarks = data.GetProperty("benchmarks");
        benchmarks.GetArrayLength().Should().BeGreaterOrEqualTo(4);

        // Verify total rules
        data.GetProperty("total_stig_rules").GetInt32().Should().Be(5);

        // Verify CCI mappings are included
        data.GetProperty("cci_count").GetInt32().Should().Be(2);
        data.GetProperty("cci_mappings").GetArrayLength().Should().Be(2);
        data.GetProperty("cci_mappings")[0].GetProperty("cci_id").GetString().Should().Be("CCI-002418");
    }

    [Fact]
    public async Task ShowStigMapping_MaxResults_LimitsOutput()
    {
        var manyControls = Enumerable.Range(1, 30)
            .Select(i => CreateStigControl($"V-{100000 + i}", "Medium", "Test_STIG", ["AU-2"], [$"CCI-{i:D6}"]))
            .ToList();

        _stigMock
            .Setup(s => s.GetStigsByCciChainAsync("AU-2", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(manyControls);
        _stigMock
            .Setup(s => s.GetCciMappingsAsync("AU-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CciMapping>());

        var tool = CreateShowStigMappingTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["control_id"] = "AU-2",
            ["max_results"] = 5
        });

        var json = JsonDocument.Parse(result);
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("total_stig_rules").GetInt32().Should().Be(30);
        data.GetProperty("returned_rules").GetInt32().Should().Be(5);
        data.GetProperty("stig_rules").GetArrayLength().Should().Be(5);
    }

    [Fact]
    public async Task ShowStigMapping_IncludesXccdfFields()
    {
        var control = CreateStigControl("V-254239", "High", "Windows_Server_2022_STIG", ["CM-6"], ["CCI-000366"],
            stigVersion: "WN22-SO-000010", responsibility: "System Administrator");

        _stigMock
            .Setup(s => s.GetStigsByCciChainAsync("CM-6", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StigControl> { control });
        _stigMock
            .Setup(s => s.GetCciMappingsAsync("CM-6", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CciMapping>());

        var tool = CreateShowStigMappingTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["control_id"] = "CM-6"
        });

        var json = JsonDocument.Parse(result);
        var rule = json.RootElement.GetProperty("data").GetProperty("stig_rules")[0];
        rule.GetProperty("benchmark_id").GetString().Should().Be("Windows_Server_2022_STIG");
        rule.GetProperty("stig_version").GetString().Should().Be("WN22-SO-000010");
        rule.GetProperty("responsibility").GetString().Should().Be("System Administrator");
        rule.GetProperty("weight").GetDecimal().Should().Be(10.0m);
    }

    [Fact]
    public async Task ShowStigMapping_ToolMetadata_IncludesCorrectInfo()
    {
        _stigMock
            .Setup(s => s.GetStigsByCciChainAsync("AC-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StigControl>());
        _stigMock
            .Setup(s => s.GetCciMappingsAsync("AC-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CciMapping>());

        var tool = CreateShowStigMappingTool();

        tool.Name.Should().Be("compliance_show_stig_mapping");
        tool.Description.Should().Contain("STIG");
        tool.Description.Should().Contain("CCI");
        tool.Parameters.Should().ContainKey("control_id");
        tool.Parameters.Should().ContainKey("severity");
        tool.Parameters.Should().ContainKey("max_results");
        tool.Parameters["control_id"].Required.Should().BeTrue();
        tool.Parameters["severity"].Required.Should().BeFalse();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private ShowStigMappingTool CreateShowStigMappingTool() =>
        new(_stigMock.Object, Mock.Of<ILogger<ShowStigMappingTool>>());

    private static StigControl CreateStigControl(
        string stigId,
        string severity,
        string benchmarkId,
        List<string> nistControls,
        List<string> cciRefs,
        string? stigVersion = null,
        string? responsibility = null)
    {
        var sev = Enum.Parse<StigSeverity>(severity);
        return new StigControl(
            StigId: stigId,
            VulnId: stigId,
            RuleId: $"SV-{stigId[2..]}r1_rule",
            Title: $"Test STIG rule {stigId}",
            Description: $"Description for {stigId}",
            Severity: sev,
            Category: "Test",
            StigFamily: "Test Family",
            NistControls: nistControls,
            CciRefs: cciRefs,
            CheckText: "Verify the setting.",
            FixText: "Configure the setting.",
            AzureImplementation: new Dictionary<string, string>
            {
                ["Service"] = "Azure Test",
                ["Configuration"] = "Test config"
            },
            ServiceType: "Azure Test",
            StigVersion: stigVersion ?? $"{benchmarkId}-001",
            BenchmarkId: benchmarkId,
            Responsibility: responsibility ?? "Test Administrator",
            Documentable: sev != StigSeverity.High,
            Weight: 10.0m,
            ReleaseDate: new DateTime(2024, 1, 25));
    }
}

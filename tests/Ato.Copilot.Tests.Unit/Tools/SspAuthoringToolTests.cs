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
/// Unit tests for Feature 015 Phase 7 — SSP Authoring &amp; Narrative Management Tools.
/// T076: WriteNarrativeTool tests
/// T077: SuggestNarrativeTool tests
/// T078: BatchPopulateNarrativesTool tests
/// T079: NarrativeProgressTool tests
/// T080: GenerateSspTool tests
/// </summary>
public class SspAuthoringToolTests
{
    private readonly Mock<ISspService> _sspMock = new();

    // ────────────────────────────────────────────────────────────────────────
    // T076: WriteNarrativeTool Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteNarrative_NewNarrative_ReturnsSuccess()
    {
        var ci = CreateImplementation("sys-1", "AC-1", "Implemented", "Policy docs stored in SharePoint.");
        _sspMock
            .Setup(s => s.WriteNarrativeAsync("sys-1", "AC-1", "Policy docs stored in SharePoint.", null, "mcp-user", It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ci);

        var tool = CreateWriteNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["control_id"] = "AC-1",
            ["narrative"] = "Policy docs stored in SharePoint."
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("control_id").GetString().Should().Be("AC-1");
        json.RootElement.GetProperty("data").GetProperty("implementation_status").GetString().Should().Be("Implemented");
        json.RootElement.GetProperty("data").GetProperty("narrative").GetString().Should().Contain("SharePoint");
    }

    [Fact]
    public async Task WriteNarrative_WithExplicitStatus_ReturnsStatus()
    {
        var ci = CreateImplementation("sys-2", "AU-3", "PartiallyImplemented", "Audit logs capture basic events.");
        _sspMock
            .Setup(s => s.WriteNarrativeAsync("sys-2", "AU-3", "Audit logs capture basic events.", "PartiallyImplemented", "mcp-user", It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ci);

        var tool = CreateWriteNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-2",
            ["control_id"] = "AU-3",
            ["narrative"] = "Audit logs capture basic events.",
            ["status"] = "PartiallyImplemented"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("implementation_status").GetString().Should().Be("PartiallyImplemented");
    }

    [Fact]
    public async Task WriteNarrative_UpdateExisting_ReturnsModifiedAt()
    {
        var ci = CreateImplementation("sys-1", "AC-1", "Implemented", "Updated narrative v2.");
        ci.ModifiedAt = DateTime.UtcNow;

        _sspMock
            .Setup(s => s.WriteNarrativeAsync("sys-1", "AC-1", "Updated narrative v2.", null, "mcp-user", It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ci);

        var tool = CreateWriteNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["control_id"] = "AC-1",
            ["narrative"] = "Updated narrative v2."
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("modified_at").GetString().Should().NotBeNull();
    }

    [Fact]
    public async Task WriteNarrative_MissingSystemId_ReturnsError()
    {
        var tool = CreateWriteNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["control_id"] = "AC-1",
            ["narrative"] = "Some narrative."
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task WriteNarrative_MissingControlId_ReturnsError()
    {
        var tool = CreateWriteNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["narrative"] = "Some narrative."
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task WriteNarrative_MissingNarrative_ReturnsError()
    {
        var tool = CreateWriteNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["control_id"] = "AC-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
        json.RootElement.GetProperty("message").GetString().Should().Contain("narrative");
    }

    [Fact]
    public async Task WriteNarrative_ServiceThrows_ReturnsError()
    {
        _sspMock
            .Setup(s => s.WriteNarrativeAsync("sys-bad", "AC-1", "text", null, "mcp-user", It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System 'sys-bad' not found."));

        var tool = CreateWriteNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-bad",
            ["control_id"] = "AC-1",
            ["narrative"] = "text"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("WRITE_NARRATIVE_FAILED");
        json.RootElement.GetProperty("message").GetString().Should().Contain("not found");
    }

    [Fact]
    public async Task WriteNarrative_AutoPopulatedFalse_ByDefault()
    {
        var ci = CreateImplementation("sys-1", "SI-2", "Planned", "Flaw remediation via patching.");
        ci.IsAutoPopulated = false;
        ci.AiSuggested = false;

        _sspMock
            .Setup(s => s.WriteNarrativeAsync("sys-1", "SI-2", "Flaw remediation via patching.", null, "mcp-user", It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ci);

        var tool = CreateWriteNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["control_id"] = "SI-2",
            ["narrative"] = "Flaw remediation via patching."
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("is_auto_populated").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("data").GetProperty("ai_suggested").GetBoolean().Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────────────────
    // T077: SuggestNarrativeTool Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuggestNarrative_InheritedControl_ReturnsNullConfidenceAndHumanValidationFlag()
    {
        // Template path never invokes a model — confidence must be null, not a fabricated constant.
        var suggestion = new NarrativeSuggestion
        {
            ControlId = "AC-2",
            Narrative = "Azure AD manages account provisioning automatically.",
            Confidence = null, // no grounding signal on template path
            DerivationBasis = NarrativeDerivationBasis.InheritedFromAuthorizedProvider,
            RequiresHumanValidation = true,
            References = new List<string> { "FedRAMP Moderate 2024 [scaffold reference — unverified]", "Azure AD Docs [scaffold reference — unverified]" }
        };
        _sspMock
            .Setup(s => s.SuggestNarrativeAsync("sys-1", "AC-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestion);

        var tool = CreateSuggestNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["control_id"] = "AC-2"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("control_id").GetString().Should().Be("AC-2");

        // confidence MUST be null — never a fabricated literal (#705 / #645)
        json.RootElement.GetProperty("data").GetProperty("confidence").ValueKind.Should().Be(JsonValueKind.Null);

        // derivation_basis and requires_human_validation must be present
        json.RootElement.GetProperty("data").GetProperty("derivation_basis").GetString()
            .Should().Be("InheritedFromAuthorizedProvider");
        json.RootElement.GetProperty("data").GetProperty("requires_human_validation").GetBoolean()
            .Should().BeTrue();

        json.RootElement.GetProperty("data").GetProperty("suggested_narrative").GetString().Should().Contain("Azure AD");
        json.RootElement.GetProperty("data").GetProperty("references").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task SuggestNarrative_CustomerControl_ReturnsNullConfidenceAndTemplateScaffoldBasis()
    {
        // Customer-implemented template path: confidence must be null, derivation_basis must be TemplateScaffold (#705)
        var suggestion = new NarrativeSuggestion
        {
            ControlId = "CM-3",
            Narrative = "The organization manages configuration changes through...",
            Confidence = null,
            DerivationBasis = NarrativeDerivationBasis.TemplateScaffold,
            RequiresHumanValidation = true,
            References = new List<string> { "NIST SP 800-53 Rev. 5 — CM-3 [scaffold reference — unverified]" }
        };
        _sspMock
            .Setup(s => s.SuggestNarrativeAsync("sys-1", "CM-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestion);

        var tool = CreateSuggestNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["control_id"] = "CM-3"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("confidence").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("data").GetProperty("derivation_basis").GetString().Should().Be("TemplateScaffold");
        json.RootElement.GetProperty("data").GetProperty("requires_human_validation").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SuggestNarrative_MissingSystemId_ReturnsError()
    {
        var tool = CreateSuggestNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["control_id"] = "AC-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task SuggestNarrative_MissingControlId_ReturnsError()
    {
        var tool = CreateSuggestNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task SuggestNarrative_ServiceThrows_ReturnsError()
    {
        _sspMock
            .Setup(s => s.SuggestNarrativeAsync("sys-bad", "AC-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System not found."));

        var tool = CreateSuggestNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-bad",
            ["control_id"] = "AC-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("SUGGEST_NARRATIVE_FAILED");
    }

    [Fact]
    public async Task SuggestNarrative_WithReferences_ContainsExpectedSources()
    {
        var suggestion = new NarrativeSuggestion
        {
            ControlId = "IA-2",
            Narrative = "Multi-factor authentication is enforced via Azure AD.",
            Confidence = null, // template path — no grounding signal
            DerivationBasis = NarrativeDerivationBasis.TemplateScaffold,
            RequiresHumanValidation = true,
            References = new List<string> { "Azure AD MFA Docs [scaffold reference — unverified]", "FedRAMP Moderate 2024 [scaffold reference — unverified]", "NIST SP 800-63B [scaffold reference — unverified]" }
        };
        _sspMock
            .Setup(s => s.SuggestNarrativeAsync("sys-1", "IA-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestion);

        var tool = CreateSuggestNarrativeTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["control_id"] = "IA-2"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("references").GetArrayLength().Should().Be(3);
    }

    // ────────────────────────────────────────────────────────────────────────
    // T078: BatchPopulateNarrativesTool Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BatchPopulate_InheritedControls_ReturnsPopulatedCount()
    {
        var batchResult = new BatchPopulateResult
        {
            PopulatedCount = 45,
            SkippedCount = 5,
            PopulatedControlIds = Enumerable.Range(1, 45).Select(i => $"AC-{i}").ToList(),
            SkippedControlIds = new List<string> { "AC-46", "AC-47", "AC-48", "AC-49", "AC-50" }
        };
        _sspMock
            .Setup(s => s.BatchPopulateNarrativesAsync("sys-1", "Inherited", "mcp-user", It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var tool = CreateBatchPopulateNarrativesTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["inheritance_type"] = "Inherited"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("populated_count").GetInt32().Should().Be(45);
        json.RootElement.GetProperty("data").GetProperty("skipped_count").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task BatchPopulate_NoInheritanceType_PopulatesBoth()
    {
        var batchResult = new BatchPopulateResult
        {
            PopulatedCount = 80,
            SkippedCount = 0,
            PopulatedControlIds = Enumerable.Range(1, 80).Select(i => $"X-{i}").ToList(),
            SkippedControlIds = new List<string>()
        };
        _sspMock
            .Setup(s => s.BatchPopulateNarrativesAsync("sys-2", null, "mcp-user", It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var tool = CreateBatchPopulateNarrativesTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-2"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("populated_count").GetInt32().Should().Be(80);
        json.RootElement.GetProperty("data").GetProperty("skipped_count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task BatchPopulate_IdempotentRun_AllSkipped()
    {
        var batchResult = new BatchPopulateResult
        {
            PopulatedCount = 0,
            SkippedCount = 50,
            PopulatedControlIds = new List<string>(),
            SkippedControlIds = Enumerable.Range(1, 50).Select(i => $"AC-{i}").ToList()
        };
        _sspMock
            .Setup(s => s.BatchPopulateNarrativesAsync("sys-1", "Inherited", "mcp-user", It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var tool = CreateBatchPopulateNarrativesTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["inheritance_type"] = "Inherited"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("populated_count").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("data").GetProperty("skipped_count").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task BatchPopulate_MissingSystemId_ReturnsError()
    {
        var tool = CreateBatchPopulateNarrativesTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task BatchPopulate_ServiceThrows_ReturnsError()
    {
        _sspMock
            .Setup(s => s.BatchPopulateNarrativesAsync("sys-bad", null, "mcp-user", It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System 'sys-bad' not found."));

        var tool = CreateBatchPopulateNarrativesTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-bad"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("BATCH_POPULATE_FAILED");
    }

    /// <summary>
    /// T078-R1 — Regression: baseline-less system returns NO_BASELINE_SELECTED with actionable message.
    /// Acceptance criteria #1 from issue #537.
    /// </summary>
    [Fact]
    public async Task BatchPopulate_NoBaseline_ReturnsNoBaselineSelectedError()
    {
        const string actionableMessage =
            "No control baseline is selected for system 'sys-nobaseline'. " +
            "Complete the Select phase (compliance_select_baseline) before auto-generating narratives.";

        _sspMock
            .Setup(s => s.BatchPopulateNarrativesAsync(
                "sys-nobaseline", null, "mcp-user",
                It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(actionableMessage));

        var tool = CreateBatchPopulateNarrativesTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-nobaseline"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("NO_BASELINE_SELECTED");
        json.RootElement.GetProperty("message").GetString().Should()
            .Contain("compliance_select_baseline");
        // Must NOT contain the old misleading message
        json.RootElement.GetProperty("message").GetString().Should()
            .NotContain("document service lookup failure");
    }

    [Fact]
    public async Task BatchPopulate_SharedControls_ReturnsSharedPopulated()
    {
        var batchResult = new BatchPopulateResult
        {
            PopulatedCount = 12,
            SkippedCount = 3,
            PopulatedControlIds = new List<string> { "CM-1", "CM-2", "CM-3", "CM-4", "CM-5", "CM-6", "CM-7", "CM-8", "CM-9", "CM-10", "CM-11", "IA-1" },
            SkippedControlIds = new List<string> { "IA-2", "IA-3", "IA-4" }
        };
        _sspMock
            .Setup(s => s.BatchPopulateNarrativesAsync("sys-1", "Shared", "mcp-user", It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var tool = CreateBatchPopulateNarrativesTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["inheritance_type"] = "Shared"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("populated_count").GetInt32().Should().Be(12);
        json.RootElement.GetProperty("data").GetProperty("populated_control_ids").GetArrayLength().Should().Be(12);
    }

    // ────────────────────────────────────────────────────────────────────────
    // T079: NarrativeProgressTool Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NarrativeProgress_FullCompletion_Returns100Percent()
    {
        var progress = CreateProgress("sys-1", 10, 10, 0, 0, 100.0,
            new FamilyProgress { Family = "AC", Total = 5, Completed = 5, Draft = 0, Missing = 0 },
            new FamilyProgress { Family = "AU", Total = 5, Completed = 5, Draft = 0, Missing = 0 });

        _sspMock
            .Setup(s => s.GetNarrativeProgressAsync("sys-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(progress);

        var tool = CreateNarrativeProgressTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("overall_percentage").GetDouble().Should().Be(100.0);
        json.RootElement.GetProperty("data").GetProperty("missing_narratives").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task NarrativeProgress_NoNarratives_Returns0Percent()
    {
        var progress = CreateProgress("sys-2", 20, 0, 0, 20, 0.0,
            new FamilyProgress { Family = "AC", Total = 10, Completed = 0, Draft = 0, Missing = 10 },
            new FamilyProgress { Family = "AU", Total = 10, Completed = 0, Draft = 0, Missing = 10 });

        _sspMock
            .Setup(s => s.GetNarrativeProgressAsync("sys-2", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(progress);

        var tool = CreateNarrativeProgressTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-2"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("overall_percentage").GetDouble().Should().Be(0.0);
        json.RootElement.GetProperty("data").GetProperty("missing_narratives").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task NarrativeProgress_PartialCompletion_ReturnsCorrectPercentage()
    {
        var progress = CreateProgress("sys-3", 20, 12, 3, 5, 60.0,
            new FamilyProgress { Family = "AC", Total = 10, Completed = 8, Draft = 1, Missing = 1 },
            new FamilyProgress { Family = "AU", Total = 10, Completed = 4, Draft = 2, Missing = 4 });

        _sspMock
            .Setup(s => s.GetNarrativeProgressAsync("sys-3", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(progress);

        var tool = CreateNarrativeProgressTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-3"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("overall_percentage").GetDouble().Should().Be(60.0);
        json.RootElement.GetProperty("data").GetProperty("completed_narratives").GetInt32().Should().Be(12);
        json.RootElement.GetProperty("data").GetProperty("draft_narratives").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task NarrativeProgress_WithFamilyFilter_ReturnsFilteredData()
    {
        var progress = CreateProgress("sys-1", 5, 3, 1, 1, 60.0,
            new FamilyProgress { Family = "AC", Total = 5, Completed = 3, Draft = 1, Missing = 1 });

        _sspMock
            .Setup(s => s.GetNarrativeProgressAsync("sys-1", "AC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(progress);

        var tool = CreateNarrativeProgressTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["family_filter"] = "AC"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("family_breakdowns").GetArrayLength().Should().Be(1);
        json.RootElement.GetProperty("data").GetProperty("family_breakdowns")[0].GetProperty("family").GetString().Should().Be("AC");
    }

    [Fact]
    public async Task NarrativeProgress_MissingSystemId_ReturnsError()
    {
        var tool = CreateNarrativeProgressTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task NarrativeProgress_ServiceThrows_ReturnsError()
    {
        _sspMock
            .Setup(s => s.GetNarrativeProgressAsync("sys-bad", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System not found."));

        var tool = CreateNarrativeProgressTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-bad"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("PROGRESS_FAILED");
    }

    [Fact]
    public async Task NarrativeProgress_MultipleFamilies_ReturnsAllBreakdowns()
    {
        var progress = CreateProgress("sys-1", 30, 20, 5, 5, 66.7,
            new FamilyProgress { Family = "AC", Total = 10, Completed = 8, Draft = 1, Missing = 1 },
            new FamilyProgress { Family = "AU", Total = 10, Completed = 7, Draft = 2, Missing = 1 },
            new FamilyProgress { Family = "CM", Total = 10, Completed = 5, Draft = 2, Missing = 3 });

        _sspMock
            .Setup(s => s.GetNarrativeProgressAsync("sys-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(progress);

        var tool = CreateNarrativeProgressTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("family_breakdowns").GetArrayLength().Should().Be(3);
    }

    // ────────────────────────────────────────────────────────────────────────
    // T080: GenerateSspTool Tests
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// T079-R1 — Regression: baseline-less system returns a valid non-error progress state.
    /// Acceptance criteria #3 from issue #537.
    /// </summary>
    [Fact]
    public async Task NarrativeProgress_NoBaseline_ReturnsNoBaselineState()
    {
        var noBaselineProgress = new NarrativeProgress
        {
            SystemId = "sys-nobaseline",
            BaselineSelected = false,
            StatusMessage = "0/0 — no baseline selected for system 'sys-nobaseline'. " +
                            "Complete the Select phase (compliance_select_baseline) before generating narratives."
        };

        _sspMock
            .Setup(s => s.GetNarrativeProgressAsync("sys-nobaseline", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(noBaselineProgress);

        var tool = CreateNarrativeProgressTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-nobaseline"
        });

        var json = JsonDocument.Parse(result);
        // Must NOT be an error — must be a valid state
        json.RootElement.GetProperty("status").GetString().Should().Be("no_baseline");
        json.RootElement.GetProperty("data").GetProperty("baseline_selected").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("data").GetProperty("status_message").GetString().Should()
            .Contain("compliance_select_baseline");
        // Counts should be zero, not errors
        json.RootElement.GetProperty("data").GetProperty("total_controls").GetInt32().Should().Be(0);
    }

    // ────────────────────────────────────────────────────────────────────────
    // T080: GenerateSspTool Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateSsp_FullDocument_ReturnsAllSections()
    {
        var doc = CreateSspDocument("sys-1", "MySystem", 50, 48, 2,
            new[] { "system_information", "categorization", "baseline", "controls" },
            new[] { "2 controls missing narratives" });

        _sspMock
            .Setup(s => s.GenerateSspAsync("sys-1", "markdown", null, It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var tool = CreateGenerateSspTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("system_name").GetString().Should().Be("MySystem");
        json.RootElement.GetProperty("data").GetProperty("total_controls").GetInt32().Should().Be(50);
        json.RootElement.GetProperty("data").GetProperty("controls_with_narratives").GetInt32().Should().Be(48);
        json.RootElement.GetProperty("data").GetProperty("controls_missing_narratives").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("data").GetProperty("sections").GetArrayLength().Should().Be(4);
        json.RootElement.GetProperty("data").GetProperty("content").GetString().Should().Contain("System Security Plan");
    }

    [Fact]
    public async Task GenerateSsp_SectionFilter_ReturnsFilteredSections()
    {
        var doc = CreateSspDocument("sys-1", "MySystem", 50, 48, 2,
            new[] { "system_information", "categorization" },
            new List<string>().ToArray());

        _sspMock
            .Setup(s => s.GenerateSspAsync("sys-1", "markdown",
                It.Is<IEnumerable<string>>(sections =>
                    sections.Contains("system_information") && sections.Contains("categorization")),
                It.IsAny<IProgress<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var tool = CreateGenerateSspTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["sections"] = "system_information,categorization"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("sections").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GenerateSsp_WithWarnings_IncludesWarnings()
    {
        var doc = CreateSspDocument("sys-1", "MySystem", 50, 40, 10,
            new[] { "system_information", "categorization", "baseline", "controls" },
            new[] { "10 controls missing narratives", "Categorization not finalized" });

        _sspMock
            .Setup(s => s.GenerateSspAsync("sys-1", "markdown", null, It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var tool = CreateGenerateSspTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("warnings").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GenerateSsp_MissingSystemId_ReturnsError()
    {
        var tool = CreateGenerateSspTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task GenerateSsp_ServiceThrows_ReturnsError()
    {
        _sspMock
            .Setup(s => s.GenerateSspAsync("sys-bad", "markdown", null, It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System not found."));

        var tool = CreateGenerateSspTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-bad"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("GENERATE_SSP_FAILED");
    }

    [Fact]
    public async Task GenerateSsp_NoMissingNarratives_ZeroWarnings()
    {
        var doc = CreateSspDocument("sys-1", "CleanSystem", 30, 30, 0,
            new[] { "system_information", "categorization", "baseline", "controls" },
            Array.Empty<string>());

        _sspMock
            .Setup(s => s.GenerateSspAsync("sys-1", "markdown", null, It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var tool = CreateGenerateSspTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("controls_missing_narratives").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("data").GetProperty("warnings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GenerateSsp_FormatDefault_IsMarkdown()
    {
        var doc = CreateSspDocument("sys-1", "MySystem", 10, 10, 0,
            new[] { "system_information" }, Array.Empty<string>());

        _sspMock
            .Setup(s => s.GenerateSspAsync("sys-1", "markdown", null, It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var tool = CreateGenerateSspTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("format").GetString().Should().Be("markdown");
    }

    [Fact]
    public async Task GenerateSsp_IncludesGeneratedAtTimestamp()
    {
        var doc = CreateSspDocument("sys-1", "MySystem", 10, 10, 0,
            new[] { "system_information" }, Array.Empty<string>());

        _sspMock
            .Setup(s => s.GenerateSspAsync("sys-1", "markdown", null, It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var tool = CreateGenerateSspTool();
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("generated_at").GetString().Should().NotBeNullOrEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Tool factories
    // ────────────────────────────────────────────────────────────────────────

    private WriteNarrativeTool CreateWriteNarrativeTool() =>
        new(_sspMock.Object, Mock.Of<ILogger<WriteNarrativeTool>>());

    private SuggestNarrativeTool CreateSuggestNarrativeTool() =>
        new(_sspMock.Object, Mock.Of<ILogger<SuggestNarrativeTool>>());

    private BatchPopulateNarrativesTool CreateBatchPopulateNarrativesTool() =>
        new(_sspMock.Object, Mock.Of<ILogger<BatchPopulateNarrativesTool>>());

    private NarrativeProgressTool CreateNarrativeProgressTool() =>
        new(_sspMock.Object, Mock.Of<INarrativeGovernanceService>(), Mock.Of<ILogger<NarrativeProgressTool>>());

    private GenerateSspTool CreateGenerateSspTool() =>
        new(_sspMock.Object, Mock.Of<ILogger<GenerateSspTool>>());

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private static ControlImplementation CreateImplementation(
        string systemId, string controlId, string status, string narrative)
    {
        var implStatus = Enum.Parse<ImplementationStatus>(status);
        return new ControlImplementation
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = systemId,
            ControlId = controlId,
            ImplementationStatus = implStatus,
            Narrative = narrative,
            IsAutoPopulated = false,
            AiSuggested = false,
            AuthoredBy = "mcp-user",
            AuthoredAt = DateTime.UtcNow
        };
    }

    private static NarrativeProgress CreateProgress(
        string systemId, int total, int completed, int draft, int missing, double percentage,
        params FamilyProgress[] families) => new()
    {
        SystemId = systemId,
        TotalControls = total,
        CompletedNarratives = completed,
        DraftNarratives = draft,
        MissingNarratives = missing,
        OverallPercentage = percentage,
        FamilyBreakdowns = families.ToList()
    };

    private static SspDocument CreateSspDocument(
        string systemId, string systemName, int total, int withNarratives, int missing,
        string[] sections, string[] warnings) => new()
    {
        SystemId = systemId,
        SystemName = systemName,
        Format = "markdown",
        Content = $"# System Security Plan: {systemName}\n\nGenerated document content.",
        TotalControls = total,
        ControlsWithNarratives = withNarratives,
        ControlsMissingNarratives = missing,
        Sections = sections.ToList(),
        GeneratedAt = DateTime.UtcNow,
        Warnings = warnings.ToList()
    };
}

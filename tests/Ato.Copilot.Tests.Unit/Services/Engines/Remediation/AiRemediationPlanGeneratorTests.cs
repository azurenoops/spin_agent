using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Services.Engines.Remediation;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services.Engines.Remediation;

/// <summary>
/// Unit tests for AiRemediationPlanGenerator: script generation with mock IChatClient,
/// guidance generation, AI prioritization with business context, null IChatClient fallback,
/// IsAvailable property.
/// </summary>
public class AiRemediationPlanGeneratorTests
{
    private readonly Mock<IChatClient> _chatClientMock = new();
    private readonly Mock<ILogger<AiRemediationPlanGenerator>> _loggerMock = new();

    private AiRemediationPlanGenerator CreateGenerator(IChatClient? chatClient = null) =>
        new(_loggerMock.Object, chatClient);

    private static ComplianceFinding CreateFinding(
        string controlId = "AC-2",
        string family = "AC",
        FindingSeverity severity = FindingSeverity.High) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            ControlId = controlId,
            ControlFamily = family,
            Title = $"Test finding for {controlId}",
            Description = $"Non-compliance for {controlId}",
            Severity = severity,
            Status = FindingStatus.Open,
            ResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/test",
            ResourceType = "Microsoft.Storage/storageAccounts",
            RemediationGuidance = $"Fix {controlId} compliance issue",
            RemediationType = RemediationType.ResourceConfiguration,
            AutoRemediable = true,
            Source = "PolicyInsights"
        };

    private void SetupChatClientResponse(string responseText)
    {
        var chatMessage = new ChatMessage(ChatRole.Assistant, responseText);
        var chatResponse = new ChatResponse(chatMessage);

        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);
    }

    // ─── IsAvailable ──────────────────────────────────────────────────────────

    [Fact]
    public void IsAvailable_WithChatClient_ReturnsTrue()
    {
        var generator = CreateGenerator(_chatClientMock.Object);

        generator.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_WithoutChatClient_ReturnsFalse()
    {
        var generator = CreateGenerator(null);

        generator.IsAvailable.Should().BeFalse();
    }

    // ─── GenerateScriptAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GenerateScriptAsync_WithChatClient_ReturnsScript()
    {
        SetupChatClientResponse("az storage account update --name test --min-tls-version TLS1_2");
        var generator = CreateGenerator(_chatClientMock.Object);
        var finding = CreateFinding();

        var script = await generator.GenerateScriptAsync(finding, ScriptType.AzureCli);

        script.Should().NotBeNull();
        script!.Content.Should().Contain("az storage account update");
        script.ScriptType.Should().Be(ScriptType.AzureCli);
        script.IsSanitized.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateScriptAsync_WithoutChatClient_ReturnsNull()
    {
        var generator = CreateGenerator(null);
        var finding = CreateFinding();

        var script = await generator.GenerateScriptAsync(finding, ScriptType.AzureCli);

        script.Should().BeNull();
    }

    [Fact]
    public async Task GenerateScriptAsync_EmptyResponse_ReturnsNull()
    {
        SetupChatClientResponse("   ");
        var generator = CreateGenerator(_chatClientMock.Object);
        var finding = CreateFinding();

        var script = await generator.GenerateScriptAsync(finding, ScriptType.AzureCli);

        script.Should().BeNull();
    }

    [Fact]
    public async Task GenerateScriptAsync_ChatClientThrows_ReturnsNull()
    {
        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI service unavailable"));
        var generator = CreateGenerator(_chatClientMock.Object);
        var finding = CreateFinding();

        var script = await generator.GenerateScriptAsync(finding, ScriptType.PowerShell);

        script.Should().BeNull();
    }

    [Fact]
    public async Task GenerateScriptAsync_SetsCorrectScriptType()
    {
        SetupChatClientResponse("Set-AzStorageAccount -MinimumTlsVersion TLS1_2");
        var generator = CreateGenerator(_chatClientMock.Object);
        var finding = CreateFinding();

        var script = await generator.GenerateScriptAsync(finding, ScriptType.PowerShell);

        script.Should().NotBeNull();
        script!.ScriptType.Should().Be(ScriptType.PowerShell);
    }

    // ─── GetGuidanceAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetGuidanceAsync_WithValidJsonResponse_ReturnsGuidance()
    {
        var json = """
            {
                "explanation": "The storage account has TLS 1.0 enabled",
                "technicalPlan": "Update the minimum TLS version to 1.2",
                "references": ["https://docs.microsoft.com/azure/tls"]
            }
            """;
        SetupChatClientResponse(json);
        var generator = CreateGenerator(_chatClientMock.Object);
        var finding = CreateFinding("SC-8", "SC");

        var guidance = await generator.GetGuidanceAsync(finding);

        guidance.Should().NotBeNull();
        guidance!.FindingId.Should().Be(finding.Id);
        guidance.Explanation.Should().Contain("TLS");
        guidance.TechnicalPlan.Should().Contain("1.2");
        guidance.References.Should().ContainSingle();
        // confidence must be null — no grounding signal justifies a hardcoded constant (#705)
        guidance.ConfidenceScore.Should().BeNull();
    }

    [Fact]
    public async Task GetGuidanceAsync_WithNonJsonResponse_UsesRawText()
    {
        SetupChatClientResponse("Enable TLS 1.2 on all storage accounts");
        var generator = CreateGenerator(_chatClientMock.Object);
        var finding = CreateFinding();

        var guidance = await generator.GetGuidanceAsync(finding);

        guidance.Should().NotBeNull();
        guidance!.Explanation.Should().Contain("Enable TLS");
        // confidence must be null — fallback text path has no grounding signal (#705)
        guidance.ConfidenceScore.Should().BeNull();
    }

    [Fact]
    public async Task GetGuidanceAsync_WithoutChatClient_ReturnsNull()
    {
        var generator = CreateGenerator(null);
        var finding = CreateFinding();

        var guidance = await generator.GetGuidanceAsync(finding);

        guidance.Should().BeNull();
    }

    [Fact]
    public async Task GetGuidanceAsync_EmptyResponse_ReturnsNull()
    {
        SetupChatClientResponse("");
        var generator = CreateGenerator(_chatClientMock.Object);
        var finding = CreateFinding();

        var guidance = await generator.GetGuidanceAsync(finding);

        guidance.Should().BeNull();
    }

    // ─── PrioritizeAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task PrioritizeAsync_WithoutChatClient_ReturnsSeverityBasedPriority()
    {
        var generator = CreateGenerator(null);
        var findings = new[]
        {
            CreateFinding("AC-2", "AC", FindingSeverity.Critical),
            CreateFinding("AC-3", "AC", FindingSeverity.Low)
        };

        var prioritized = await generator.PrioritizeAsync(findings, null);

        prioritized.Should().HaveCount(2);
        prioritized[0].AiPriority.Should().Be(RemediationPriority.P0);
        prioritized[0].OriginalPriority.Should().Be(RemediationPriority.P0);
        prioritized[0].Justification.Should().Contain("Severity-based");
        prioritized[1].AiPriority.Should().Be(RemediationPriority.P3);
    }

    [Fact]
    public async Task PrioritizeAsync_WithValidJsonResponse_ReturnsAiPriority()
    {
        var finding = CreateFinding("AC-2", "AC", FindingSeverity.Medium);
        var json = $$"""
            [{
                "id": "{{finding.Id}}",
                "priority": "P0",
                "justification": "Critical access control issue",
                "businessImpact": "High exposure risk"
            }]
            """;
        SetupChatClientResponse(json);
        var generator = CreateGenerator(_chatClientMock.Object);

        var prioritized = await generator.PrioritizeAsync(new[] { finding }, "Healthcare application");

        prioritized.Should().HaveCount(1);
        prioritized[0].AiPriority.Should().Be(RemediationPriority.P0);
        prioritized[0].OriginalPriority.Should().Be(RemediationPriority.P2);
        prioritized[0].Justification.Should().Contain("Critical access control");
        prioritized[0].BusinessImpact.Should().Contain("High exposure");
    }

    [Fact]
    public async Task PrioritizeAsync_EmptyResponse_ReturnsFallback()
    {
        SetupChatClientResponse("");
        var generator = CreateGenerator(_chatClientMock.Object);
        var findings = new[] { CreateFinding("AC-2", "AC", FindingSeverity.High) };

        var prioritized = await generator.PrioritizeAsync(findings, null);

        prioritized.Should().HaveCount(1);
        prioritized[0].AiPriority.Should().Be(RemediationPriority.P1);
        prioritized[0].Justification.Should().Contain("fallback");
    }

    [Fact]
    public async Task PrioritizeAsync_InvalidJsonResponse_ReturnsFallback()
    {
        SetupChatClientResponse("Not valid JSON at all");
        var generator = CreateGenerator(_chatClientMock.Object);
        var findings = new[] { CreateFinding("SC-8", "SC", FindingSeverity.Critical) };

        var prioritized = await generator.PrioritizeAsync(findings, null);

        prioritized.Should().HaveCount(1);
        prioritized[0].AiPriority.Should().Be(RemediationPriority.P0);
    }

    [Fact]
    public async Task PrioritizeAsync_SeverityMapping_CorrectPriorities()
    {
        var generator = CreateGenerator(null);
        var findings = new[]
        {
            CreateFinding("A-1", "A", FindingSeverity.Critical),
            CreateFinding("B-1", "B", FindingSeverity.High),
            CreateFinding("C-1", "C", FindingSeverity.Medium),
            CreateFinding("D-1", "D", FindingSeverity.Low),
            CreateFinding("E-1", "E", FindingSeverity.Informational)
        };

        var prioritized = await generator.PrioritizeAsync(findings, null);

        prioritized[0].AiPriority.Should().Be(RemediationPriority.P0);
        prioritized[1].AiPriority.Should().Be(RemediationPriority.P1);
        prioritized[2].AiPriority.Should().Be(RemediationPriority.P2);
        prioritized[3].AiPriority.Should().Be(RemediationPriority.P3);
        prioritized[4].AiPriority.Should().Be(RemediationPriority.P4);
    }

    // ─── GenerateEnhancedPlanAsync ────────────────────────────────────────────

    [Fact]
    public async Task GenerateEnhancedPlanAsync_WithChatClient_ReturnsPlan()
    {
        SetupChatClientResponse("""{"steps": [{"description": "Update TLS", "effort": "Low"}]}""");
        var generator = CreateGenerator(_chatClientMock.Object);
        var finding = CreateFinding("SC-8", "SC");

        var plan = await generator.GenerateEnhancedPlanAsync(finding);

        plan.Should().NotBeNull();
        plan!.Steps.Should().HaveCount(1);
        plan.Steps[0].FindingId.Should().Be(finding.Id);
        plan.Steps[0].ControlId.Should().Be("SC-8");
        plan.TotalFindings.Should().Be(1);
    }

    [Fact]
    public async Task GenerateEnhancedPlanAsync_WithoutChatClient_ReturnsNull()
    {
        var generator = CreateGenerator(null);
        var finding = CreateFinding();

        var plan = await generator.GenerateEnhancedPlanAsync(finding);

        plan.Should().BeNull();
    }

    [Fact]
    public async Task GenerateEnhancedPlanAsync_EmptyResponse_ReturnsNull()
    {
        SetupChatClientResponse("   ");
        var generator = CreateGenerator(_chatClientMock.Object);
        var finding = CreateFinding();

        var plan = await generator.GenerateEnhancedPlanAsync(finding);

        plan.Should().BeNull();
    }

    [Fact]
    public async Task GenerateEnhancedPlanAsync_ChatClientThrows_ReturnsNull()
    {
        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Service failure"));
        var generator = CreateGenerator(_chatClientMock.Object);
        var finding = CreateFinding();

        var plan = await generator.GenerateEnhancedPlanAsync(finding);

        plan.Should().BeNull();
    }
}
